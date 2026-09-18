using System.Collections.Generic;

namespace USBofon
{
    public sealed class UsbDevice
    {
        public string InstanceId;
        public string Description;
        public string BusName;
        public string Manufacturer;
        public string ClassName;
        public string Service;
        public string Location;
        public string Vid;
        public string Pid;
        public string Serial;
        public uint DevInst;
        /// <summary>«USB» или «Bluetooth».</summary>
        public string Bus = "USB";
        /// <summary>Заряд в процентах, если устройство его сообщает.</summary>
        public int? Battery;

        public bool Present;
        public bool Disabled;
        public uint Problem;

        public bool IsHub;
        public bool IsInterface;
        public bool IsInput;

        public readonly List<string> Children = new List<string>();
        public readonly List<string> ChildInstanceIds = new List<string>();
        public readonly List<string> ChildClasses = new List<string>();
        public readonly List<string> DriveLetters = new List<string>();

        public string VidPid => Vid == null ? "" : Vid + ":" + Pid;

        public string DisplayDescription =>
            string.IsNullOrEmpty(BusName) || BusName == Description ? Description : Description + " — " + BusName;

        public string StatusText
        {
            get
            {
                if (!Present) return "Не подключено";
                if (Disabled) return "Выключено";
                if (Problem != 0) return "Ошибка (код " + Problem + ")";
                return "Включено";
            }
        }
    }
}
