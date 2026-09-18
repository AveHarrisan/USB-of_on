using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using static USBofon.NativeMethods;

namespace USBofon
{
    /// <summary>
    /// Заряд устройства. Два источника:
    /// 1) стандартный отчёт HID «Battery Strength» (Usage Page 0x06, Usage 0x20) — его же читает Windows;
    /// 2) свойство Bluetooth-устройства, которое видно в «Параметры → Bluetooth и устройства».
    /// Фирменные протоколы (приёмники Logitech, игровые гарнитуры) сюда не попадают: заряд идёт мимо системы.
    /// </summary>
    internal static class Battery
    {
        private static readonly Guid HidInterface = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

        // DEVPKEY_Bluetooth_Battery — Windows 10 1809 и новее.
        private static readonly DEVPROPKEY BluetoothBattery =
            new DEVPROPKEY("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);

        /// <summary>Заряд в процентах по узлам устройств: ключ — devInst, значение — 0..100.</summary>
        public static Dictionary<uint, int> Read()
        {
            var result = new Dictionary<uint, int>();
            ReadHid(result);
            return result;
        }

        /// <summary>Заряд из свойства Bluetooth для одного устройства, если оно там есть.</summary>
        public static int? ReadBluetooth(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA data)
        {
            var buffer = new byte[16];
            var key = BluetoothBattery;
            if (!SetupDiGetDeviceProperty(deviceInfoSet, ref data, ref key, out _, buffer, (uint)buffer.Length, out var size, 0)
                || size < 1)
                return null;
            var value = buffer[0];
            return value <= 100 ? (int?)value : null;
        }

        private static void ReadHid(Dictionary<uint, int> result)
        {
            var guid = HidInterface;
            var set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == INVALID_HANDLE_VALUE) return;
            try
            {
                var iface = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref iface); i++)
                {
                    var info = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                    var path = InterfacePath(set, ref iface, ref info);
                    if (path == null) continue;

                    var percent = ReadHidBattery(path);
                    if (percent.HasValue) result[info.DevInst] = percent.Value;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
        }

        private static string InterfacePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA iface, ref SP_DEVINFO_DATA info)
        {
            SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out var required, IntPtr.Zero);
            if (required == 0) return null;

            var buffer = Marshal.AllocHGlobal((int)required);
            try
            {
                // cbSize структуры SP_DEVICE_INTERFACE_DETAIL_DATA: 8 в 64-битном процессе, 6 в 32-битном.
                Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(set, ref iface, buffer, required, out _, ref info))
                    return null;
                return Marshal.PtrToStringUni(buffer + 4);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static int? ReadHidBattery(string path)
        {
            // Открываем без прав чтения и записи: так устройство не отбирается у того, кто с ним работает.
            var handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == INVALID_HANDLE_VALUE) return null;
            var preparsed = IntPtr.Zero;
            try
            {
                if (!HidD_GetPreparsedData(handle, out preparsed)) return null;
                if (HidP_GetCaps(preparsed, out var caps) != HIDP_STATUS_SUCCESS || caps.NumberFeatureValueCaps == 0)
                    return null;

                var count = caps.NumberFeatureValueCaps;
                var valueCaps = new HIDP_VALUE_CAPS[count];
                if (HidP_GetValueCaps(HidP_Feature, valueCaps, ref count, preparsed) != HIDP_STATUS_SUCCESS)
                    return null;

                for (var i = 0; i < count; i++)
                {
                    var cap = valueCaps[i];
                    if (cap.UsagePage != UsagePageGenericDeviceControls || cap.Usage != UsageBatteryStrength) continue;

                    var report = new byte[Math.Max((int)caps.FeatureReportByteLength, 2)];
                    report[0] = cap.ReportID;
                    if (!HidD_GetFeature(handle, report, report.Length)) continue;

                    if (HidP_GetUsageValue(HidP_Feature, cap.UsagePage, 0, cap.Usage, out var value, preparsed, report, report.Length)
                        != HIDP_STATUS_SUCCESS)
                        continue;

                    var min = cap.LogicalMin;
                    var max = cap.LogicalMax > min ? cap.LogicalMax : 100;
                    var percent = (int)Math.Round((value - min) * 100.0 / (max - min));
                    if (percent >= 0 && percent <= 100) return percent;
                }
                return null;
            }
            finally
            {
                if (preparsed != IntPtr.Zero) HidD_FreePreparsedData(preparsed);
                CloseHandle(handle);
            }
        }

        private const ushort UsagePageGenericDeviceControls = 0x06;
        private const ushort UsageBatteryStrength = 0x20;
        private const int HidP_Feature = 2;
        private const int HIDP_STATUS_SUCCESS = 0x00110000;
        private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_VALUE_CAPS
        {
            public ushort UsagePage;
            public byte ReportID;
            [MarshalAs(UnmanagedType.U1)] public bool IsAlias;
            public ushort BitField;
            public ushort LinkCollection;
            public ushort LinkUsage;
            public ushort LinkUsagePage;
            [MarshalAs(UnmanagedType.U1)] public bool IsRange;
            [MarshalAs(UnmanagedType.U1)] public bool IsStringRange;
            [MarshalAs(UnmanagedType.U1)] public bool IsDesignatorRange;
            [MarshalAs(UnmanagedType.U1)] public bool IsAbsolute;
            [MarshalAs(UnmanagedType.U1)] public bool HasNull;
            public byte Reserved;
            public ushort BitSize;
            public ushort ReportCount;
            public ushort Reserved2a, Reserved2b, Reserved2c, Reserved2d, Reserved2e;
            public uint UnitsExp;
            public uint Units;
            public int LogicalMin;
            public int LogicalMax;
            public int PhysicalMin;
            public int PhysicalMax;
            public ushort Usage;      // объединение Range/NotRange: для не-диапазона здесь Usage
            public ushort Reserved3;
            public ushort StringIndex;
            public ushort Reserved4;
            public ushort DesignatorIndex;
            public ushort Reserved5;
            public ushort DataIndex;
            public ushort Reserved6;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA interfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA interfaceData, IntPtr detailData, uint detailSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA interfaceData, IntPtr detailData, uint detailSize, out uint requiredSize, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string fileName, uint access, uint shareMode, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(IntPtr device, out IntPtr preparsedData);

        [DllImport("hid.dll")]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        private static extern bool HidD_GetFeature(IntPtr device, byte[] report, int reportLength);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS caps);

        [DllImport("hid.dll")]
        private static extern int HidP_GetValueCaps(int reportType, [Out] HIDP_VALUE_CAPS[] valueCaps, ref ushort valueCapsLength, IntPtr preparsedData);

        [DllImport("hid.dll")]
        private static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage, out uint value, IntPtr preparsedData, byte[] report, int reportLength);
    }
}
