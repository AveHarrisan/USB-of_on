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

            foreach (Match match in Regex.Matches(devices, @"""id"":\s*""(dev[0-9a-f]+)"",\s*""pid"":\s*(\d+)"))
            {
                var id = match.Groups[1].Value;
                if (!ushort.TryParse(match.Groups[2].Value, out var pid)) continue;

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

                // Тип и название берём из описания устройства: по ним сопоставим приёмник с его мышью.
                var tail = devices.Substring(match.Index, Math.Min(3000, devices.Length - match.Index));
                var kind = Regex.Match(tail, @"""deviceType"":\s*""([A-Z_]+)""");
                var name = Regex.Match(tail, @"""displayName"":\s*""([^""]+)""");
                result.Add(new Entry
                {
                    Pid = pid,
                    Kind = kind.Success ? kind.Groups[1].Value : "",
                    Name = name.Success ? name.Groups[1].Value : "",
                    Percent = value,
                    Answer = answer,
                });
            }
            return result;
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
                    var received = socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancel.Token).GetAwaiter().GetResult();
                    var text = Encoding.UTF8.GetString(buffer, 0, received.Count);
                    if (text.Contains("\"msgId\"") && text.Contains("\"q\"")) return text;   // ответ именно на наш запрос
                }
                return null;
            }
        }
    }
}
