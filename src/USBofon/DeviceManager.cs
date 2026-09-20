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
            public int? Battery;
        }

        // Bluetooth-мыши и клавиатуры идут не через USB, но в списке им самое место.
        private static readonly (string Enumerator, string Bus)[] Buses =
        {
            ("USB", "USB"),
            ("BTHENUM", "Bluetooth"),
            ("BTHLEDEVICE", "Bluetooth"),
        };

        /// <summary>Спрашивать ли заряд. Выключается, когда окно скрыто и виджет не показан.</summary>
        public static bool PollBattery = true;

        /// <summary>Все устройства: подключённые сейчас и те, что Windows помнит с прошлых подключений.</summary>
        public static List<UsbDevice> Enumerate()
        {
            var present = EnumeratePresentNodes();
            var letters = GetDriveLettersByDisk();
            // Устройства сами мы не опрашиваем: заряд берём у Windows (Bluetooth) и у G HUB,
            // который и так знает его для своего окна. Спящая мышь от этого не просыпается.
            var ghub = PollBattery ? GHubBattery.Read() : new List<GHubBattery.Entry>();
            var razer = PollBattery ? RazerBattery.Read() : new List<(string Name, int Percent)>();
            // Прямой опрос по HID++ — только если человек разрешил: он будит спящие устройства.
            var direct = PollBattery && Settings.AskDevices
                ? LogitechBattery.ReadAll()
                : new Dictionary<uint, int>();

            var result = new List<UsbDevice>();
            foreach (var bus in Buses)
                Enumerate(bus.Enumerator, bus.Bus, present, letters, razer, direct, result);

            AddDevicesWithBattery(present, result);
            MarkParts(result);
            AssignGHub(result, ghub);
            return result;
        }

        private static void Enumerate(string enumerator, string bus, Dictionary<uint, NodeInfo> present,
            Dictionary<string, List<string>> letters, List<(string Name, int Percent)> razer,
            Dictionary<uint, int> direct, List<UsbDevice> result)
        {
            var set = SetupDiGetClassDevs(IntPtr.Zero, enumerator, IntPtr.Zero, DIGCF_ALLCLASSES);
            if (set == INVALID_HANDLE_VALUE) throw new Win32Exception();
            try
            {
                var data = NewDevInfo();
                for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
                {
                    var dev = new UsbDevice
                    {
                        Bus = bus,
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


                    dev.IsHub = dev.InstanceId.IndexOf("ROOT_HUB", StringComparison.OrdinalIgnoreCase) >= 0
                        || string.Equals(dev.Service, "usbhub", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(dev.Service, "usbhub3", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(dev.ClassName, "USB", StringComparison.OrdinalIgnoreCase)
                           && (dev.Description.IndexOf("hub", StringComparison.OrdinalIgnoreCase) >= 0
                               || dev.Description.IndexOf("концентратор", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (dev.Present)
                    {
                        CollectChildren(dev, dev.DevInst, present, letters, 0);
                        if (!dev.IsHub)
                            dev.Battery = Mark(dev, "Windows", Battery.ReadBluetooth(set, ref data))
                                ?? Mark(dev, "Windows, у части устройства", ChildBattery(dev.DevInst, present, 0))
                                ?? Mark(dev, "журнал Razer Synapse", RazerBattery.For(dev, razer))
                                ?? Mark(dev, "прямой опрос HID++", DirectBattery(dev.DevInst, direct, 0));
                        dev.ParentId = ParentId(dev.DevInst);
                    }

                    result.Add(dev);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
        }

        /// <summary>Запоминает, откуда взялся заряд: нужно для отчёта.</summary>
        private static int? Mark(UsbDevice dev, string source, int? percent)
        {
            if (percent.HasValue) dev.BatterySource = source;
            return percent;
        }

        /// <summary>Заряд, полученный прямым опросом: он привязан к HID-интерфейсу внутри устройства.</summary>
        private static int? DirectBattery(uint devInst, Dictionary<uint, int> direct, int depth)
        {
            if (direct.Count == 0) return null;
            if (direct.TryGetValue(devInst, out var own)) return own;
            if (depth > 4 || CM_Get_Child(out var child, devInst, 0) != CR_SUCCESS) return null;
            do
            {
                if (direct.TryGetValue(child, out var percent)) return percent;
                var deeper = DirectBattery(child, direct, depth + 1);
                if (deeper.HasValue) return deeper;
            } while (CM_Get_Sibling(out child, child, 0) == CR_SUCCESS);
            return null;
        }

        /// <summary>Заряд, который Windows знает у частей устройства: например, у Bluetooth-клавиатуры внутри составного.</summary>
        private static int? ChildBattery(uint devInst, Dictionary<uint, NodeInfo> present, int depth)
        {
            if (depth > 4 || CM_Get_Child(out var child, devInst, 0) != CR_SUCCESS) return null;
            do
            {
                if (present.TryGetValue(child, out var info) && info.Battery.HasValue) return info.Battery;
                var deeper = ChildBattery(child, present, depth + 1);
                if (deeper.HasValue) return deeper;
            } while (CM_Get_Sibling(out child, child, 0) == CR_SUCCESS);
            return null;
        }

        /// <summary>
        /// Устройства с зарядом, которые не попали в список по шине: игровые контроллеры, перья и прочее,
        /// подключённое мимо USB и Bluetooth. Заряд берётся из готового свойства Windows.
        /// </summary>
        private static void AddDevicesWithBattery(Dictionary<uint, NodeInfo> present, List<UsbDevice> result)
        {
            var known = new HashSet<uint>(result.Select(d => d.DevInst));
            foreach (var pair in present)
            {
                if (!pair.Value.Battery.HasValue || known.Contains(pair.Key)) continue;
                if (CoveredByParent(pair.Key, known)) continue;

                var id = pair.Value.InstanceId ?? "";
                var bus = id.Split(new[] { '\\' }, 2, StringSplitOptions.None)[0];
                result.Add(new UsbDevice
                {
                    DevInst = pair.Key,
                    InstanceId = id,
                    Description = pair.Value.Description ?? id,
                    ClassName = pair.Value.ClassName,
                    Bus = BusName(bus),
                    Present = true,
                    Battery = pair.Value.Battery,
                });
            }
        }

        /// <summary>Часть уже показанного устройства отдельной строкой не показываем.</summary>
        private static bool CoveredByParent(uint devInst, HashSet<uint> known)
        {
            var node = devInst;
            for (var depth = 0; depth < 8; depth++)
            {
                if (CM_Get_Parent(out var parent, node, 0) != CR_SUCCESS) return false;
                if (known.Contains(parent)) return true;
                node = parent;
            }
            return false;
        }

        private static string BusName(string enumerator)
        {
            switch (enumerator.ToUpperInvariant())
            {
                case "USB": return "USB";
                case "BTHENUM":
                case "BTHLE":
                case "BTHLEDEVICE": return "Bluetooth";
                case "HID": return "HID";
                default: return enumerator;
            }
        }

        /// <summary>
        /// Раздаёт заряд от G HUB в два прохода: сначала точные совпадения по коду модели,
        /// потом догадки по типу — только тем значениям, которые никому не достались.
        /// Без этого заряд клавиатуры мог оказаться на чужом приёмнике.
        /// </summary>
        private static void AssignGHub(List<UsbDevice> devices, List<GHubBattery.Entry> ghub)
        {
            if (ghub.Count == 0) return;
            var used = new HashSet<ushort>();

            foreach (var dev in devices.Where(d => d.Present && !d.Battery.HasValue && IsLogitech(d)
                                                   && !d.IsPart && !d.IsInterface && !d.IsHub))
            {
                if (!ushort.TryParse(dev.Pid ?? "", System.Globalization.NumberStyles.HexNumber, null, out var pid)) continue;
                var exact = ghub.FirstOrDefault(e => e.Pid == pid && e.Percent.HasValue);
                if (exact == null) continue;
                dev.Battery = exact.Percent;
                dev.BatterySource = "G HUB, по коду модели";
                dev.KnownName = exact.Name;
                used.Add(exact.Pid);
            }

            foreach (var dev in devices.Where(d => d.Present && !d.Battery.HasValue && IsLogitech(d)
                                                   && !d.IsPart && !d.IsInterface && !d.IsHub))
            {
                var kind = DevicePresentation.Kind(dev);
                var wanted = kind == DeviceKind.Keyboard ? new[] { "MOUSE", "KEYBOARD" }
                    : kind == DeviceKind.Audio ? new[] { "HEADSET" }
                    : null;
                if (wanted == null) continue;

                var free = ghub.Where(e => e.Percent.HasValue && !used.Contains(e.Pid)
                                           && Array.IndexOf(wanted, e.Kind) >= 0).ToList();
                if (free.Count == 0) continue;

                // Если название совпадает — берём его, иначе догадываемся только при единственном кандидате.
                var title = (DevicePresentation.FriendlyName(dev) + " " + dev.Description).ToLowerInvariant();
                var match = free.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name) && title.Contains(e.Name.ToLowerInvariant()))
                            ?? (free.Count == 1 ? free[0] : null);
                if (match == null) continue;

                dev.Battery = match.Percent;
                dev.BatterySource = "G HUB, по типу устройства (" + match.Name + ")";
                dev.KnownName = match.Name;
                used.Add(match.Pid);
            }
        }

        private static bool IsLogitech(UsbDevice dev) =>
            string.Equals(dev.Vid, "046D", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Помечает записи, которые на деле — части другого устройства из списка: подсветка, слоты,
        /// интерфейсы. Иначе одна клавиатура выглядит как пять устройств с одинаковым зарядом.
        /// </summary>
        private static void MarkParts(List<UsbDevice> devices)
        {
            var byId = new Dictionary<string, UsbDevice>(StringComparer.OrdinalIgnoreCase);
            foreach (var dev in devices)
                if (!string.IsNullOrEmpty(dev.InstanceId)) byId[dev.InstanceId] = dev;

            foreach (var dev in devices)
            {
                if (dev.IsHub) continue;
                var node = dev.DevInst;
                for (var depth = 0; depth < 6; depth++)
                {
                    var parentId = ParentId(node);
                    if (string.IsNullOrEmpty(parentId)) break;
                    // Хаб — не устройство-владелец: внутри него сидят самостоятельные устройства.
                    if (byId.TryGetValue(parentId, out var owner) && owner != dev && !owner.IsHub)
                    {
                        dev.IsPart = true;
                        break;
                    }
                    if (CM_Get_Parent(out var parent, node, 0) != CR_SUCCESS) break;
                    node = parent;
                }
            }
        }

        private static string ParentId(uint devInst)
        {
            if (devInst == 0 || CM_Get_Parent(out var parent, devInst, 0) != CR_SUCCESS) return null;
            var buffer = new char[512];
            if (CM_Get_Device_ID(parent, buffer, buffer.Length, 0) != CR_SUCCESS) return null;
            var text = new string(buffer);
            var zero = text.IndexOf('\0');
            return zero >= 0 ? text.Substring(0, zero) : text.Trim();
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
                    if (!string.IsNullOrEmpty(info.ClassName)) dev.ChildClasses.Add(info.ClassName);
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
                        // Заряд, который Windows уже знает: своих запросов к устройству нет.
                        Battery = Battery.ReadBluetooth(set, ref data),
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
