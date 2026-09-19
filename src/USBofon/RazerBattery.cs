using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace USBofon
{
    /// <summary>
    /// Заряд устройств Razer. Своего локального канала, как у G HUB, у Synapse нет, но он пишет заряд
    /// в свой журнал — читаем оттуда. К устройствам программа при этом не обращается, будить их нечем.
    /// Работает, пока Synapse запущен и успел записать значение.
    /// </summary>
    internal static class RazerBattery
    {
        private static TimeSpan CacheTime => TimeSpan.FromMinutes(Settings.BatteryMinutes);
        private static List<(string Name, int Percent)> _cache = new List<(string, int)>();
        private static DateTime _read = DateTime.MinValue;

        // «Battery level: 87», «battery 87%», «BatteryPercentage":87» — у разных версий Synapse по-разному.
        private static readonly Regex Line = new Regex(
            @"(?<name>[A-Za-z0-9 \-\.]{3,40}?)[^\r\n]{0,80}?batter[^\r\n]{0,40}?(?<percent>\d{1,3})\s*%?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<(string Name, int Percent)> Read()
        {
            if (!Settings.UseSynapse) return new List<(string, int)>();
            if (DateTime.Now - _read < CacheTime) return _cache;
            _read = DateTime.Now;
            try { _cache = Parse(); }
            catch { _cache = new List<(string, int)>(); }
            return _cache;
        }

        private static List<(string Name, int Percent)> Parse()
        {
            var result = new List<(string, int)>();
            var log = NewestLog();
            if (log == null) return result;

            // Журнал большой: берём только хвост — там свежие записи.
            var text = Tail(log, 200 * 1024);
            foreach (Match match in Line.Matches(text))
            {
                if (!int.TryParse(match.Groups["percent"].Value, out var percent) || percent < 0 || percent > 100)
                    continue;
                var name = match.Groups["name"].Value.Trim();
                if (name.Length < 3) continue;
                result.RemoveAll(x => string.Equals(x.Item1, name, StringComparison.OrdinalIgnoreCase));
                result.Add((name, percent));   // позже в журнале — значит свежее
            }
            return result;
        }

        private static string NewestLog()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidates = new List<string>();
            var synapse3 = Path.Combine(local, @"Razer\Synapse3\Log");
            var synapse4 = Path.Combine(local, @"Razer\RazerAppEngine\User Data\Logs");
            foreach (var dir in new[] { synapse4, synapse3 })
                if (Directory.Exists(dir))
                    candidates.AddRange(Directory.GetFiles(dir, "*.log", SearchOption.TopDirectoryOnly));

            return candidates
                .Select(path => new FileInfo(path))
                .Where(file => file.Length > 0 && DateTime.Now - file.LastWriteTime < TimeSpan.FromHours(12))
                .OrderByDescending(file => file.LastWriteTime)
                .Select(file => file.FullName)
                .FirstOrDefault();
        }

        private static string Tail(string path, int bytes)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length > bytes) stream.Seek(-bytes, SeekOrigin.End);
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        /// <summary>Заряд для устройства Razer: по названию, а если оно одно — просто первое значение.</summary>
        public static int? For(UsbDevice dev, List<(string Name, int Percent)> entries)
        {
            if (entries.Count == 0 || !string.Equals(dev.Vid, "1532", StringComparison.OrdinalIgnoreCase)) return null;

            var title = (DevicePresentation.FriendlyName(dev) ?? "").ToLowerInvariant();
            foreach (var entry in entries)
            {
                var name = entry.Name.ToLowerInvariant();
                if (title.Contains(name) || name.Contains(title)) return entry.Percent;
            }
            return entries.Count == 1 ? (int?)entries[0].Percent : null;
        }
    }
}
