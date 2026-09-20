using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace USBofon
{
    /// <summary>
    /// Заряд устройств Logitech по их протоколу HID++ 2.0: мыши и клавиатуры через приёмник
    /// (Unifying, Lightspeed) системе заряд не сообщают, но отвечают на служебный запрос сами.
    /// Гарнитуры G HUB говорят по другому протоколу и сюда не попадают.
    /// </summary>
    internal static class LogitechBattery
    {
        private const ushort LogitechVendor = 0x046D;
        // HID++ бывает трёх размеров: короткий, длинный и очень длинный. У гарнитур вроде G435
        // доступен только очень длинный — 64 байта плюс номер отчёта.
        /// <summary>Номер отчёта по размеру коллекции: короткий, длинный и очень длинный.</summary>
        private static byte ReportFor(int size) => size == 7 ? (byte)0x10 : size == 20 ? (byte)0x11 : (byte)0x12;

        private static bool Supported(int size) => size == 7 || size == 20 || size >= 32;

        /// <summary>Что происходило при опросе — попадает в отчёт.</summary>
        public static readonly List<string> Log = new List<string>();

        // Возможности HID++: UnifiedBattery отдаёт проценты, BatteryStatus — уровень разряда.
        private const ushort FeatureUnifiedBattery = 0x1004;
        private const ushort FeatureBatteryStatus = 0x1000;

        private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(60);
        private static readonly Dictionary<uint, (Answer Value, DateTime Read)> Cache =
            new Dictionary<uint, (Answer, DateTime)>();

        /// <summary>Что ответило устройство: заряд и собственное название.</summary>
        public sealed class Answer
        {
            public int Percent;
            public string Name;
        }

        // Устройства, которые на HID++ не отвечают (звуковые коллекции, чужие приёмники),
        // не дёргаем каждый раз: иначе опрос растягивается на секунды.
        private static readonly Dictionary<uint, DateTime> Silent = new Dictionary<uint, DateTime>();

        /// <summary>Спрашивает все приёмники и устройства Logitech: «узел устройства → проценты».</summary>
        public static Dictionary<uint, Answer> ReadAll()
        {
            Log.Clear();
            var result = new Dictionary<uint, Answer>();
            try
            {
                var interfaces = Battery.HidInterfaces()
                    .Select(x => (x.DevInst, x.Path, x.Vendor, x.UsagePage, x.OutputLength));
                Read(result, interfaces);
            }
            catch { }
            return result;
        }

        /// <summary>Дописывает найденный заряд в общую таблицу «узел устройства → проценты».</summary>
        public static void Read(Dictionary<uint, Answer> result, IEnumerable<(uint DevInst, string Path, ushort Vendor, ushort UsagePage, int OutputLength)> interfaces)
        {
            foreach (var iface in interfaces)
            {
                if (iface.Vendor != LogitechVendor || iface.UsagePage < 0xFF00) continue;
                if (!Supported(iface.OutputLength)) continue;

                if (Cache.TryGetValue(iface.DevInst, out var cached) && DateTime.Now - cached.Read < CacheTime)
                {
                    result[iface.DevInst] = cached.Value;
                    continue;
                }

                if (Silent.TryGetValue(iface.DevInst, out var silentSince) && DateTime.Now - silentSince < CacheTime)
                    continue;

                var answer = Query(iface.Path, iface.OutputLength);
                if (answer != null)
                {
                    Silent.Remove(iface.DevInst);
                    Cache[iface.DevInst] = (answer, DateTime.Now);
                    result[iface.DevInst] = answer;
                }
                else
                {
                    Silent[iface.DevInst] = DateTime.Now;
                }
            }
        }

        private static Answer Query(string path, int size)
        {
            var format = (Report: ReportFor(size), Size: size);
            var handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == INVALID_HANDLE)
            {
                Log.Add($"не открылось: {path}");
                return null;
            }
            try
            {
                // 0xFF — устройство подключено прямо, 1..6 — место в приёмнике.
                foreach (byte index in new byte[] { 0xFF, 1, 2, 3, 4, 5, 6 })
                {
                    var answer = QueryDevice(handle, index, format);
                    if (answer != null)
                    {
                        Log.Add($"отчёт 0x{format.Report:X2} ({size} байт), устройство {index:X2}: "
                                + $"заряд {answer.Percent}%, имя «{answer.Name}»");
                        return answer;
                    }
                }
                Log.Add($"отчёт 0x{format.Report:X2} ({size} байт): ответа нет — {Short(path)}");
                return null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static string Short(string path)
        {
            var start = path.IndexOf("vid_", StringComparison.OrdinalIgnoreCase);
            return start < 0 ? path : path.Substring(start, Math.Min(30, path.Length - start));
        }

        private static Answer QueryDevice(IntPtr handle, byte index, (byte Report, int Size) format)
        {
            // Пинг корневой возможности: отвечают только живые устройства.
            if (Call(handle, index, 0x00, 0x11, 0, 0, 0xAA, format) == null) return null;

            foreach (var feature in new[] { FeatureUnifiedBattery, FeatureBatteryStatus })
            {
                var lookup = Call(handle, index, 0x00, 0x01, (byte)(feature >> 8), (byte)feature, 0, format);
                if (lookup == null || lookup[4] == 0) continue;

                var answer = Call(handle, index, lookup[4],
                    feature == FeatureUnifiedBattery ? (byte)0x11 : (byte)0x01, 0, 0, 0, format);
                if (answer == null) continue;

                var percent = answer[4];
                if (percent <= 100) return new Answer { Percent = percent, Name = DeviceName(handle, index, format) };
            }
            return null;
        }

        /// <summary>
        /// Собственное название устройства по HID++ (возможность 0x0005): так узнаём, что за приёмником
        /// сидит «G502 X LIGHTSPEED», а не безымянный «USB Receiver».
        /// </summary>
        private static string DeviceName(IntPtr handle, byte index, (byte Report, int Size) format)
        {
            var lookup = Call(handle, index, 0x00, 0x01, 0x00, 0x05, 0, format);
            if (lookup == null || lookup[4] == 0) return null;
            var feature = lookup[4];

            var count = Call(handle, index, feature, 0x01, 0, 0, 0, format);
            if (count == null || count[4] == 0) return null;

            var name = new System.Text.StringBuilder();
            for (byte position = 0; position < count[4] && name.Length < 64; position += 16)
            {
                var chunk = Call(handle, index, feature, 0x11, position, 0, 0, format);
                if (chunk == null) break;
                for (var i = 4; i < chunk.Length; i++)
                {
                    if (chunk[i] == 0) break;
                    if (chunk[i] >= 32 && chunk[i] < 127) name.Append((char)chunk[i]);
                }
            }
            var text = name.ToString().Trim();
            return text.Length >= 3 ? text : null;
        }

        private static byte[] Call(IntPtr handle, byte device, byte featureIndex, byte function,
            byte p0, byte p1, byte p2, (byte Report, int Size) format)
        {
            var request = new byte[format.Size];
            request[0] = format.Report;
            request[1] = device;
            request[2] = featureIndex;
            request[3] = function;
            request[4] = p0;
            request[5] = p1;
            request[6] = p2;
            if (!Write(handle, request)) return null;

            // В канале идут и чужие кадры (их шлёт G HUB), поэтому ждём именно свой ответ.
            var deadline = DateTime.Now.AddMilliseconds(900);
            while (DateTime.Now < deadline)
            {
                var answer = Read(handle, 250, format.Size);
                if (answer == null) continue;
                if (answer[1] != device) continue;                       // ответ другому устройству приёмника
                if (answer[2] == 0xFF && answer[4] == featureIndex) return null;   // ошибка на наш запрос
                if (answer[2] == featureIndex && answer[3] == function) return answer;
            }
            return null;
        }

        private static bool Write(IntPtr handle, byte[] data)
        {
            var wait = CreateEvent(IntPtr.Zero, true, false, null);
            var overlapped = AllocOverlapped(wait);
            try
            {
                if (!WriteFile(handle, data, data.Length, out _, overlapped) && Marshal.GetLastWin32Error() != ERROR_IO_PENDING)
                    return false;
                if (WaitForSingleObject(wait, 400) == 0) return true;
                CancelIo(handle);
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(overlapped);
                CloseHandle(wait);
            }
        }

        private static byte[] Read(IntPtr handle, int timeout, int size)
        {
            var buffer = new byte[size];
            var wait = CreateEvent(IntPtr.Zero, true, false, null);
            var overlapped = AllocOverlapped(wait);
            try
            {
                if (!ReadFile(handle, buffer, buffer.Length, out _, overlapped) && Marshal.GetLastWin32Error() != ERROR_IO_PENDING)
                    return null;
                if (WaitForSingleObject(wait, timeout) != 0)
                {
                    CancelIo(handle);
                    return null;
                }
                return buffer;
            }
            finally
            {
                Marshal.FreeHGlobal(overlapped);
                CloseHandle(wait);
            }
        }

        private static IntPtr AllocOverlapped(IntPtr wait)
        {
            var overlapped = new System.Threading.NativeOverlapped { EventHandle = wait };
            var memory = Marshal.AllocHGlobal(Marshal.SizeOf(overlapped));
            Marshal.StructureToPtr(overlapped, memory, false);
            return memory;
        }

        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const int ERROR_IO_PENDING = 997;
        private static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr handle, byte[] buffer, int count, out int written, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr handle, byte[] buffer, int count, out int read, IntPtr overlapped);

        [DllImport("kernel32.dll")]
        private static extern bool CancelIo(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string name);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, int milliseconds);
    }
}
