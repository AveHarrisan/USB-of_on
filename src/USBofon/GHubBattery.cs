using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace USBofon
{
    /// <summary>
    /// Заряд от Logitech G HUB. Игровые гарнитуры (PRO X 2 и подобные) не отвечают на HID++ и не
    /// сообщают заряд системе, но G HUB знает его и отдаёт по локальному каналу на порту 9010.
    /// Работает, только пока G HUB запущен; устройство узнаём по коду модели (PID).
    /// </summary>
    internal static class GHubBattery
    {
        private const string Address = "ws://localhost:9010";
        private const int Port = 9010;

        public sealed class Entry
        {
            public ushort Pid;
            public string Kind;      // MOUSE, KEYBOARD, HEADSET и т. п.
            public string Name;
            /// <summary>Заряд в процентах; null — G HUB устройство знает, но заряд не сообщает.</summary>
            public int? Percent;
            /// <summary>Что ответил G HUB на запрос заряда — для отчёта.</summary>
            public string Answer;
            /// <summary>Состояние по G HUB: ACTIVE — работает, BLOCKED — выключено или уснуло.</summary>
            public string State;
        }

        private static TimeSpan CacheTime => TimeSpan.FromMinutes(Settings.BatteryMinutes);
        private static List<Entry> _cache = new List<Entry>();
        private static DateTime _read = DateTime.MinValue;

        /// <summary>Последний ответ G HUB со списком устройств — целиком, для отчёта.</summary>
        public static string LastDevicesJson { get; private set; }

        /// <summary>Что знает G HUB о заряде подключённых к нему устройств.</summary>
        public static List<Entry> Read()
        {
            if (!Settings.UseGHub) return new List<Entry>();
            if (DateTime.Now - _read < CacheTime) return _cache;
            _read = DateTime.Now;
            try
            {
                _cache = Query();
            }
            catch
            {
                _cache = new List<Entry>();
            }
            return _cache;
        }

        private static List<Entry> Query()
        {
            var result = new List<Entry>();
            if (!PortOpen()) return result;      // G HUB не запущен — не ждём соединения впустую

            var devices = Ask("/devices/list");
            LastDevicesJson = devices;
            if (devices == null) return result;

            foreach (Match match in Regex.Matches(devices, @"""id"":\s*""(dev[0-9a-f]+)"""))
            {
                var id = match.Groups[1].Value;
                // Поля устройства идут блоком, но порядок бывает разным — ищем код модели рядом.
                var block = devices.Substring(match.Index, Math.Min(4000, devices.Length - match.Index));
                var pidMatch = Regex.Match(block, @"""pid"":\s*(\d+)");
                if (!pidMatch.Success || !ushort.TryParse(pidMatch.Groups[1].Value, out var pid)) continue;

                var state = Ask("/battery/" + id + "/state");
                var percent = state == null ? Match.Empty : Regex.Match(state, @"""percentage"":\s*([0-9]+)");
                int? value = percent.Success && int.TryParse(percent.Groups[1].Value, out var parsed)
                             && parsed >= 0 && parsed <= 100
                    ? (int?)parsed
                    : null;

                // Короткий ответ G HUB оставляем для отчёта: по нему видно, знает он заряд или нет.
                var answer = state == null ? "нет ответа"
                    : value.HasValue ? "заряд получен"
                    : Regex.Match(state, @"""code"":\s*""([A-Z_]+)""") is Match code && code.Success
                        ? code.Groups[1].Value
                        : "без поля percentage";

                // Выключенная или уснувшая гарнитура остаётся в списке G HUB (приёмник-то в USB),
                // но с состоянием BLOCKED. Её прежний заряд не показываем: он уже неправда.
                var stateMatch = Regex.Match(block, @"""state"":\s*""([A-Z_]+)""");
                var deviceState = stateMatch.Success ? stateMatch.Groups[1].Value : "";
                if (deviceState.Length > 0 && deviceState != "ACTIVE")
                {
                    value = null;
                    answer = "устройство выключено (" + deviceState + ")";
                }

                // Тип и название берём из описания устройства: по ним сопоставим приёмник с его мышью.
                var tail = block;
                var kind = Regex.Match(tail, @"""deviceType"":\s*""([A-Z_]+)""");
                var name = Regex.Match(tail, @"""displayName"":\s*""([^""]+)""");
                result.Add(new Entry
                {
                    Pid = pid,
                    Kind = kind.Success ? kind.Groups[1].Value : "",
                    Name = name.Success ? name.Groups[1].Value : "",
                    Percent = value,
                    Answer = answer,
                    State = deviceState,
                });
            }
            return result;
        }

        /// <summary>Забыть запомненный ответ: следующий опрос спросит G HUB заново.</summary>
        public static void Invalidate() => _read = DateTime.MinValue;

        /// <summary>
        /// G HUB сообщил, что устройство включилось, выключилось или изменился заряд.
        /// Вызывается из фонового потока.
        /// </summary>
        public static event Action Changed;

        private static Thread _watcher;

        /// <summary>
        /// Слушать события G HUB. Гарнитура за приёмником выключается и включается без ведома
        /// Windows: приёмник остаётся в USB, и сигнала о смене устройств нет. Раньше виджет
        /// поэтому не замечал включённых наушников, пока не нажмёшь «Обновить». G HUB же сам
        /// рассылает /devices/state/changed (ACTIVE ↔ BLOCKED) и /battery/state/changed —
        /// подписываемся на них. Проверено 25.09.2026 на PRO X 2: событие приходит в ту же секунду.
        /// </summary>
        public static void StartWatching()
        {
            if (_watcher != null) return;
            _watcher = new Thread(WatchLoop) { IsBackground = true, Name = "G HUB events" };
            _watcher.Start();
        }

        private static void WatchLoop()
        {
            while (true)
            {
                try
                {
                    // G HUB может быть не запущен или выключен в настройках — ждём и пробуем снова.
                    if (Settings.UseGHub && PortOpen()) Listen();
                }
                catch
                {
                    // Канал оборвался (G HUB закрыли или перезапустили) — переподключимся.
                }
                Thread.Sleep(TimeSpan.FromSeconds(10));
            }
        }

        private static void Listen()
        {
            using (var socket = new ClientWebSocket())
            {
                socket.Options.AddSubProtocol("json");
                using (var connect = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                    socket.ConnectAsync(new Uri(Address), connect.Token).GetAwaiter().GetResult();

                foreach (var path in new[] { "/devices/state/changed", "/battery/state/changed" })
                {
                    var request = Encoding.UTF8.GetBytes("{\"msgId\":\"w\",\"verb\":\"SUBSCRIBE\",\"path\":\"" + path + "\"}");
                    socket.SendAsync(new ArraySegment<byte>(request), WebSocketMessageType.Text, true, CancellationToken.None)
                        .GetAwaiter().GetResult();
                }

                // Здесь читаем без срока: события приходят когда угодно, хоть через час.
                var buffer = new byte[64 * 1024];
                while (socket.State == WebSocketState.Open && Settings.UseGHub)
                {
                    var message = new System.IO.MemoryStream();
                    while (true)
                    {
                        var received = socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None)
                            .GetAwaiter().GetResult();
                        if (received.MessageType == WebSocketMessageType.Close) return;
                        message.Write(buffer, 0, received.Count);
                        if (received.EndOfMessage) break;
                    }

                    var text = Encoding.UTF8.GetString(message.ToArray());
                    if (!text.Contains("\"BROADCAST\"")) continue;
                    if (!text.Contains("/devices/state/changed") && !text.Contains("/battery/state/changed")) continue;

                    Invalidate();
                    try { Changed?.Invoke(); } catch { }
                }
            }
        }

        private static bool PortOpen()
        {
            try
            {
                using (var probe = new TcpClient())
                    return probe.ConnectAsync("127.0.0.1", Port).Wait(200) && probe.Connected;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Один запрос: G HUB закрывает канал на незнакомый путь, поэтому соединение своё на каждый.</summary>
        private static string Ask(string path)
        {
            using (var socket = new ClientWebSocket())
            using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                socket.Options.AddSubProtocol("json");
                socket.ConnectAsync(new Uri(Address), cancel.Token).GetAwaiter().GetResult();

                var request = Encoding.UTF8.GetBytes("{\"msgId\":\"q\",\"verb\":\"GET\",\"path\":\"" + path + "\"}");
                socket.SendAsync(new ArraySegment<byte>(request), WebSocketMessageType.Text, true, cancel.Token)
                    .GetAwaiter().GetResult();

                var buffer = new byte[64 * 1024];
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    // Ответ приходит частями: собираем до конца сообщения, иначе список устройств обрывается.
                    var message = new System.IO.MemoryStream();
                    while (true)
                    {
                        var received = socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancel.Token).GetAwaiter().GetResult();
                        if (received.MessageType == WebSocketMessageType.Close) return null;
                        message.Write(buffer, 0, received.Count);
                        if (received.EndOfMessage) break;
                    }

                    var text = Encoding.UTF8.GetString(message.ToArray());
                    if (text.Contains("\"msgId\"") && text.Contains("\"q\"")) return text;   // ответ именно на наш запрос
                }
                return null;
            }
        }
    }
}
