using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static USBofon.NativeMethods;

namespace USBofon
{
    /// <summary>
    /// Заряд Bluetooth-устройств. Значение ведёт сама Windows — то же, что в «Параметрах»;
    /// программа только читает готовое свойство и ничего у устройства не спрашивает,
    /// поэтому уснувшая мышь или клавиатура не просыпается.
    /// </summary>
    internal static class Battery
    {
        // DEVPKEY_Bluetooth_Battery — Windows 10 1809 и новее.
        private static readonly DEVPROPKEY BluetoothBattery =
            new DEVPROPKEY("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);

        /// <summary>
        /// Для отчёта: все свойства устройств, в которых лежит одиночное число 0–100. Если Windows
        /// где-то хранит заряд под другим ключом, он найдётся здесь, и его можно будет читать напрямую.
        /// </summary>
        public static List<string> ScanByteProperties()
        {
            var found = new List<string>();
            var set = NativeMethods.SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
            if (set == INVALID_HANDLE_VALUE) return found;
            try
            {
                var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
                {
                    SetupDiGetDevicePropertyKeys(set, ref data, null, 0, out var count, 0);
                    if (count == 0) continue;
                    var keys = new DEVPROPKEY[count];
                    if (!SetupDiGetDevicePropertyKeys(set, ref data, keys, count, out _, 0)) continue;

                    foreach (var key in keys)
                    {
                        var current = key;
                        var buffer = new byte[8];
                        if (!SetupDiGetDeviceProperty(set, ref data, ref current, out var type, buffer,
                                (uint)buffer.Length, out var size, 0))
                            continue;
                        if (type != DEVPROP_TYPE_BYTE || size != 1 || buffer[0] > 100 || buffer[0] == 0) continue;

                        var id = new System.Text.StringBuilder(512);
                        SetupDiGetDeviceInstanceId(set, ref data, id, id.Capacity, out _);
                        found.Add($"{id} | {{{current.fmtid}}} {current.pid} = {buffer[0]}");
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
            return found;
        }

        /// <summary>HID-коллекции устройств: нужны и для отчёта, и для прямого опроса Logitech.</summary>
        public static List<(uint DevInst, string Path, ushort Vendor, ushort UsagePage, int OutputLength, string Name)> HidInterfaces()
        {
            var list = new List<(uint, string, ushort, ushort, int, string)>();
            var guid = HidInterface;
            var set = SetupDiGetClassDevsByGuid(ref guid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == INVALID_HANDLE_VALUE) return list;
            try
            {
                var iface = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref iface); i++)
                {
                    var info = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                    var path = InterfacePath(set, ref iface, ref info);
                    if (path == null) continue;

                    var handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                    if (handle == INVALID_HANDLE_VALUE) continue;
                    var preparsed = IntPtr.Zero;
                    try
                    {
                        var attributes = new HIDD_ATTRIBUTES { Size = (uint)Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                        if (!HidD_GetAttributes(handle, ref attributes)) continue;
                        if (!HidD_GetPreparsedData(handle, out preparsed)) continue;
                        if (HidP_GetCaps(preparsed, out var caps) != HIDP_STATUS_SUCCESS) continue;

                        var name = new System.Text.StringBuilder(128);
                        HidD_GetProductString(handle, name, 256);
                        list.Add((info.DevInst, path, attributes.VendorID, caps.UsagePage,
                            caps.OutputReportByteLength, name.ToString()));
                    }
                    finally
                    {
                        if (preparsed != IntPtr.Zero) HidD_FreePreparsedData(preparsed);
                        CloseHandle(handle);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
            return list;
        }

        /// <summary>
        /// Для отчёта: какие HID-коллекции дают устройства. По ним видно приёмники и их служебные
        /// каналы. Устройства открываются без прав чтения и записи, то есть не просыпаются.
        /// </summary>
        public static List<string> HidInventory()
        {
            var list = new List<string>();
            var guid = HidInterface;
            var set = SetupDiGetClassDevsByGuid(ref guid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == INVALID_HANDLE_VALUE) return list;
            try
            {
                var iface = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref iface); i++)
                {
                    var info = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                    var path = InterfacePath(set, ref iface, ref info);
                    if (path == null) continue;

                    var handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                    if (handle == INVALID_HANDLE_VALUE) continue;
                    var preparsed = IntPtr.Zero;
                    try
                    {
                        var attributes = new HIDD_ATTRIBUTES { Size = (uint)Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                        if (!HidD_GetAttributes(handle, ref attributes)) continue;
                        if (!HidD_GetPreparsedData(handle, out preparsed)) continue;
                        if (HidP_GetCaps(preparsed, out var caps) != HIDP_STATUS_SUCCESS) continue;

                        var name = new System.Text.StringBuilder(128);
                        HidD_GetProductString(handle, name, 256);
                        list.Add($"VID:PID {attributes.VendorID:X4}:{attributes.ProductID:X4} "
                                 + $"usage {caps.UsagePage:X2}:{caps.Usage:X2} "
                                 + $"вход={caps.InputReportByteLength} выход={caps.OutputReportByteLength} "
                                 + $"feature={caps.FeatureReportByteLength} «{name}»");
                    }
                    finally
                    {
                        if (preparsed != IntPtr.Zero) HidD_FreePreparsedData(preparsed);
                        CloseHandle(handle);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        private static extern bool HidD_GetProductString(IntPtr device, System.Text.StringBuilder buffer, int length);

        private const uint DEVPROP_TYPE_BYTE = 0x00000003;

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiGetDevicePropertyKeys(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
            [Out] DEVPROPKEY[] propertyKeys, uint propertyKeyCount, out uint requiredCount, uint flags);

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

        // ——— служебное для перечисления HID-коллекций ———

        private static readonly Guid HidInterface = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
        private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        private const int HIDP_STATUS_SUCCESS = 0x00110000;

        private static string InterfacePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA iface, ref SP_DEVINFO_DATA info)
        {
            SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out var required, IntPtr.Zero);
            if (required == 0) return null;

            var buffer = Marshal.AllocHGlobal((int)required);
            try
            {
                // cbSize структуры: 8 в 64-битном процессе, 6 в 32-битном.
                Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(set, ref iface, buffer, required, out _, ref info)) return null;
                return Marshal.PtrToStringUni(buffer + 4);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public uint Size;
            public ushort VendorID, ProductID, VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
        private static extern IntPtr SetupDiGetClassDevsByGuid(ref Guid classGuid, string enumerator, IntPtr parent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index,
            ref SP_DEVICE_INTERFACE_DATA data);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data,
            IntPtr detail, uint size, out uint required, IntPtr info);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data,
            IntPtr detail, uint size, out uint required, ref SP_DEVINFO_DATA info);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation,
            uint flags, IntPtr template);

        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(IntPtr device, ref HIDD_ATTRIBUTES attributes);
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr device, out IntPtr preparsed);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr preparsed);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);
    }
}
