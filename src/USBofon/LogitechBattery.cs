using System;
using System.Collections.Generic;
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
        private const byte LongReport = 0x11;
        private const int LongReportSize = 20;

        // Возможности HID++: UnifiedBattery отдаёт проценты, BatteryStatus — уровень разряда.
        private const ushort FeatureUnifiedBattery = 0x1004;
        private const ushort FeatureBatteryStatus = 0x1000;

        private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(60);
        private static readonly Dictionary<uint, (int Percent, DateTime Read)> Cache =
            new Dictionary<uint, (int, DateTime)>();

        // Устройства, которые на HID++ не отвечают (звуковые коллекции, чужие приёмники),
        // не дёргаем каждый раз: иначе опрос растягивается на секунды.
        private static readonly Dictionary<uint, DateTime> Silent = new Dictionary<uint, DateTime>();

        /// <summary>Дописывает найденный заряд в общую таблицу «узел устройства → проценты».</summary>
        public static void Read(Dictionary<uint, int> result, IEnumerable<(uint DevInst, string Path, ushort Vendor, ushort UsagePage, int OutputLength)> interfaces)
        {
            foreach (var iface in interfaces)
            {
                if (iface.Vendor != LogitechVendor || iface.UsagePage < 0xFF00 || iface.OutputLength != LongReportSize)
                    continue;

                if (Cache.TryGetValue(iface.DevInst, out var cached) && DateTime.Now - cached.Read < CacheTime)
                {
                    result[iface.DevInst] = cached.Percent;
                    continue;
                }

                if (Silent.TryGetValue(iface.DevInst, out var silentSince) && DateTime.Now - silentSince < CacheTime)
                    continue;

                var percent = Query(iface.Path);
                if (percent.HasValue)
                {
                    Silent.Remove(iface.DevInst);
                    Cache[iface.DevInst] = (percent.Value, DateTime.Now);
                    result[iface.DevInst] = percent.Value;
                }
                else
                {
                    Silent[iface.DevInst] = DateTime.Now;
                }
            }
        }

        private static int? Query(string path)
        {
            var handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == INVALID_HANDLE) return null;
            try
            {
                // 0xFF — устройство подключено прямо, 1..6 — место в приёмнике.
                foreach (byte index in new byte[] { 0xFF, 1, 2, 3, 4, 5, 6 })
                {
                    var percent = QueryDevice(handle, index);
                    if (percent.HasValue) return percent;
                }
                return null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static int? QueryDevice(IntPtr handle, byte index)
        {
            // Пинг корневой возможности: отвечают только живые устройства.
            if (Call(handle, index, 0x00, 0x11, 0, 0, 0xAA) == null) return null;

            foreach (var feature in new[] { FeatureUnifiedBattery, FeatureBatteryStatus })
            {
                var lookup = Call(handle, index, 0x00, 0x01, (byte)(feature >> 8), (byte)feature, 0);
                if (lookup == null || lookup[4] == 0) continue;

                var answer = Call(handle, index, lookup[4], feature == FeatureUnifiedBattery ? (byte)0x11 : (byte)0x01, 0, 0, 0);
                if (answer == null) continue;

                var percent = answer[4];
                if (percent <= 100) return percent;
            }
            return null;
        }

        private static byte[] Call(IntPtr handle, byte device, byte featureIndex, byte function, byte p0, byte p1, byte p2)
        {
            var request = new byte[LongReportSize];
            request[0] = LongReport;
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
                var answer = Read(handle, 250);
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

        private static byte[] Read(IntPtr handle, int timeout)
        {
            var buffer = new byte[LongReportSize];
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
