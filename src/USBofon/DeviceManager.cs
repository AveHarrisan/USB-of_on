using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using static USBofon.NativeMethods;

namespace USBofon
{
    public static class DeviceManager
    {
        private sealed class NodeInfo
        {
            public string InstanceId;
            public string Description;
            public string ClassName;
        }

        /// <summary>Все USB-устройства: подключённые сейчас и те, что Windows помнит с прошлых подключений.</summary>
        public static List<UsbDevice> Enumerate()
        {
            var present = EnumeratePresentNodes();
            var letters = GetDriveLettersByDisk();
            var result = new List<UsbDevice>();

            var set = SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, DIGCF_ALLCLASSES);
            if (set == INVALID_HANDLE_VALUE) throw new Win32Exception();
            try
            {
                var data = NewDevInfo();
                for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
                {
                    var dev = new UsbDevice
                    {
                        DevInst = data.DevInst,
                        InstanceId = GetInstanceId(set, ref data),
                        Manufacturer = GetRegString(set, ref data, SPDRP_MFG),
                        ClassName = GetRegString(set, ref data, SPDRP_CLASS),
                        Service = GetRegString(set, ref data, SPDRP_SERVICE),
                        Location = GetRegString(set, ref data, SPDRP_LOCATION_INFORMATION),
                        BusName = GetStringProperty(set, ref data, DEVPKEY_Device_BusReportedDeviceDesc),
                    };
                    dev.Description = FirstNonEmpty(GetRegString(set, ref data, SPDRP_FRIENDLYNAME),
                        GetRegString(set, ref data, SPDRP_DEVICEDESC), dev.BusName, dev.InstanceId);

                    ParseInstanceId(dev);
                    ReadStatus(dev);

                    if (dev.Present)
                        CollectChildren(dev, dev.DevInst, present, letters, 0);

                    dev.IsHub = dev.InstanceId.IndexOf("ROOT_HUB", StringComparison.OrdinalIgnoreCase) >= 0
                        || string.Equals(dev.Service, "usbhub", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(dev.Service, "usbhub3", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(dev.ClassName, "USB", StringComparison.OrdinalIgnoreCase)
                           && (dev.Description.IndexOf("hub", StringComparison.OrdinalIgnoreCase) >= 0
                               || dev.Description.IndexOf("концентратор", StringComparison.OrdinalIgnoreCase) >= 0);

                    result.Add(dev);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
            return result;
        }

        public enum ChangeResult { Done, NeedsReboot }

        public static ChangeResult SetEnabled(string instanceId, bool enable)
        {
            var set = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
            if (set == INVALID_HANDLE_VALUE) throw new Win32Exception();
            try
            {
                var data = NewDevInfo();
                if (!SetupDiOpenDeviceInfo(set, instanceId, IntPtr.Zero, 0, ref data))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Устройство не найдено: " + instanceId);

                var p = new SP_PROPCHANGE_PARAMS
                {
                    ClassInstallHeader = new SP_CLASSINSTALL_HEADER
                    {
                        cbSize = (uint)Marshal.SizeOf(typeof(SP_CLASSINSTALL_HEADER)),
                        InstallFunction = DIF_PROPERTYCHANGE,
                    },
                    StateChange = enable ? DICS_ENABLE : DICS_DISABLE,
                    Scope = DICS_FLAG_GLOBAL,
                    HwProfile = 0,
                };
                if (!SetupDiSetClassInstallParams(set, ref data, ref p, Marshal.SizeOf(typeof(SP_PROPCHANGE_PARAMS))))
                    throw new Win32Exception();
                if (!SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, set, ref data))
                    throw new Win32Exception();

                var ip = new SP_DEVINSTALL_PARAMS { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINSTALL_PARAMS)) };
                if (SetupDiGetDeviceInstallParams(set, ref data, ref ip) && (ip.Flags & (DI_NEEDREBOOT | DI_NEEDRESTART)) != 0)
                    return ChangeResult.NeedsReboot;
                return ChangeResult.Done;
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
        }

        private static SP_DEVINFO_DATA NewDevInfo() =>
            new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };

        private static void ReadStatus(UsbDevice dev)
        {
            if (CM_Get_DevNode_Status(out var status, out var problem, dev.DevInst, 0) != CR_SUCCESS)
            {
                dev.Present = false;
                return;
            }
            dev.Present = true;
            if ((status & DN_HAS_PROBLEM) != 0)
            {
                if (problem == CM_PROB_DISABLED) dev.Disabled = true;
                else dev.Problem = problem;
            }
        }

        // USB\VID_0A89&PID_0030\123456  или  USB\VID_046D&PID_C52B&MI_00\7&2a...
        private static void ParseInstanceId(UsbDevice dev)
        {
            var parts = dev.InstanceId.Split('\\');
            if (parts.Length < 2) return;
            foreach (var token in parts[1].Split('&'))
            {
                if (token.StartsWith("VID_", StringComparison.OrdinalIgnoreCase)) dev.Vid = token.Substring(4);
                else if (token.StartsWith("PID_", StringComparison.OrdinalIgnoreCase)) dev.Pid = token.Substring(4);
                else if (token.StartsWith("MI_", StringComparison.OrdinalIgnoreCase)) dev.IsInterface = true;
            }
            // Серийный номер настоящий, если Windows не сгенерировала его сама (в сгенерированных есть «&»).
            if (parts.Length >= 3 && parts[2].IndexOf('&') < 0 && !dev.IsInterface)
                dev.Serial = parts[2];
        }

        private static void CollectChildren(UsbDevice dev, uint devInst, Dictionary<uint, NodeInfo> present,
            Dictionary<string, List<string>> letters, int depth)
        {
            if (depth > 4 || CM_Get_Child(out var child, devInst, 0) != CR_SUCCESS) return;
            do
            {
                if (present.TryGetValue(child, out var info))
                {
                    dev.ChildInstanceIds.Add(info.InstanceId);
                    if (string.Equals(info.ClassName, "Keyboard", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(info.ClassName, "Mouse", StringComparison.OrdinalIgnoreCase))
                        dev.IsInput = true;
                    if (!string.IsNullOrEmpty(info.Description)
                        && !string.Equals(info.ClassName, "Volume", StringComparison.OrdinalIgnoreCase)
                        && !dev.Children.Contains(info.Description))
                        dev.Children.Add(info.Description);
                    if (letters.TryGetValue(info.InstanceId.ToUpperInvariant(), out var l))
                        foreach (var letter in l)
                            if (!dev.DriveLetters.Contains(letter)) dev.DriveLetters.Add(letter);
                }
                CollectChildren(dev, child, present, letters, depth + 1);
            } while (CM_Get_Sibling(out child, child, 0) == CR_SUCCESS);
        }

        private static Dictionary<uint, NodeInfo> EnumeratePresentNodes()
        {
            var map = new Dictionary<uint, NodeInfo>();
            var set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
            if (set == INVALID_HANDLE_VALUE) return map;
            try
            {
                var data = NewDevInfo();
                for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
                {
                    map[data.DevInst] = new NodeInfo
                    {
                        InstanceId = GetInstanceId(set, ref data),
                        Description = FirstNonEmpty(GetRegString(set, ref data, SPDRP_FRIENDLYNAME),
                            GetRegString(set, ref data, SPDRP_DEVICEDESC)),
                        ClassName = GetRegString(set, ref data, SPDRP_CLASS),
                    };
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
            return map;
        }

        /// <summary>Буквы дисков по идентификатору диска (USBSTOR\DISK&...).</summary>
        private static Dictionary<string, List<string>> GetDriveLettersByDisk()
        {
            var map = new Dictionary<string, List<string>>();
            try
            {
                using (var disks = new ManagementObjectSearcher("SELECT DeviceID, PNPDeviceID FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject disk in disks.Get())
                    {
                        var pnp = (disk["PNPDeviceID"] as string)?.ToUpperInvariant();
                        var id = disk["DeviceID"] as string;
                        if (pnp == null || id == null) continue;
                        var list = new List<string>();
                        using (var parts = new ManagementObjectSearcher(
                            "ASSOCIATORS OF {Win32_DiskDrive.DeviceID='" + id.Replace("\\", "\\\\") +
                            "'} WHERE AssocClass = Win32_DiskDriveToDiskPartition"))
                        {
                            foreach (ManagementObject part in parts.Get())
                            {
                                using (var logical = new ManagementObjectSearcher(
                                    "ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" + part["DeviceID"] +
                                    "'} WHERE AssocClass = Win32_LogicalDiskToPartition"))
                                {
                                    foreach (ManagementObject ld in logical.Get())
                                        list.Add((string)ld["Name"]);
                                }
                            }
                        }
                        if (list.Count > 0) map[pnp] = list;
                    }
                }
            }
            catch (ManagementException)
            {
                // WMI может быть отключён — буквы дисков просто не покажем.
            }
            catch (COMException)
            {
            }
            return map;
        }

        private static string GetInstanceId(IntPtr set, ref SP_DEVINFO_DATA data)
        {
            var sb = new StringBuilder(512);
            return SetupDiGetDeviceInstanceId(set, ref data, sb, sb.Capacity, out _) ? sb.ToString() : "";
        }

        private static string GetRegString(IntPtr set, ref SP_DEVINFO_DATA data, uint prop)
        {
            var buf = new byte[1024];
            if (!SetupDiGetDeviceRegistryProperty(set, ref data, prop, out _, buf, (uint)buf.Length, out var size))
                return null;
            return DecodeString(buf, size);
        }

        private static string GetStringProperty(IntPtr set, ref SP_DEVINFO_DATA data, DEVPROPKEY key)
        {
            var buf = new byte[1024];
            if (!SetupDiGetDeviceProperty(set, ref data, ref key, out _, buf, (uint)buf.Length, out var size, 0))
                return null;
            return DecodeString(buf, size);
        }

        private static string DecodeString(byte[] buf, uint size)
        {
            var s = Encoding.Unicode.GetString(buf, 0, (int)Math.Min(size, (uint)buf.Length));
            var zero = s.IndexOf('\0');
            if (zero >= 0) s = s.Substring(0, zero);
            s = s.Trim();
            return s.Length == 0 ? null : s;
        }

        private static string FirstNonEmpty(params string[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
    }
}
