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

        private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(60);
        private static Dictionary<ushort, int> _cache = new Dictionary<ushort, int>();
        private static DateTime _read = DateTime.MinValue;

        /// <summary>Заряд по коду модели устройства: ключ — PID, значение — проценты.</summary>
        public static Dictionary<ushort, int> Read()
        {
            if (DateTime.Now - _read < CacheTime) return _cache;
            _read = DateTime.Now;
            try
            {
                _cache = Query();
            }
            catch
            {
                _cache = new Dictionary<ushort, int>();
            }
            return _cache;
        }

        private static Dictionary<ushort, int> Query()
        {
            var result = new Dictionary<ushort, int>();
            if (!PortOpen()) return result;      // G HUB не запущен — не ждём соединения впустую

            var devices = Ask("/devices/list");
            if (devices == null) return result;

            foreach (Match match in Regex.Matches(devices, @"""id"":\s*""(dev[0-9a-f]+)"",\s*""pid"":\s*(\d+)"))
            {
                var id = match.Groups[1].Value;
                if (!ushort.TryParse(match.Groups[2].Value, out var pid)) continue;

                var state = Ask("/battery/" + id + "/state");
                if (state == null) continue;
                var percent = Regex.Match(state, @"""percentage"":\s*([0-9]+)");
                if (percent.Success && int.TryParse(percent.Groups[1].Value, out var value) && value >= 0 && value <= 100)
                    result[pid] = value;
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
