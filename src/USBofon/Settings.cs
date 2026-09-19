using Microsoft.Win32;

namespace USBofon
{
    /// <summary>Личные настройки пользователя (вид окна) — в реестре HKCU.</summary>
    internal static class Settings
    {
        private const string Key = @"Software\USB-of_on";

        public static bool StartMinimized
        {
            get => GetFlag("StartMinimized");
            set => SetFlag("StartMinimized", value);
        }

        /// <summary>Показывать только устройства, которым дали имя.</summary>
        public static bool NamedOnly
        {
            get => GetFlag("NamedOnly");
            set => SetFlag("NamedOnly", value);
        }

        /// <summary>Уведомление у трея, когда подключают устройство. Включено по умолчанию.</summary>
        public static bool NotifyConnected
        {
            get => GetFlag("NotifyConnected", true);
            set => SetFlag("NotifyConnected", value);
        }

        public static bool WidgetVisible
        {
            get => GetFlag("WidgetVisible");
            set => SetFlag("WidgetVisible", value);
        }

        public static bool WidgetLocked
        {
            get => GetFlag("WidgetLocked");
            set => SetFlag("WidgetLocked", value);
        }

        /// <summary>Где стоит виджет; null — ещё не ставили.</summary>
        public static System.Drawing.Point? WidgetPosition
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(Key))
                    {
                        if (k?.GetValue("WidgetX") is int x && k.GetValue("WidgetY") is int y)
                            return new System.Drawing.Point(x, y);
                    }
                }
                catch { }
                return null;
            }
            set
            {
                if (value == null) return;
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(Key))
                    {
                        k?.SetValue("WidgetX", value.Value.X, RegistryValueKind.DWord);
                        k?.SetValue("WidgetY", value.Value.Y, RegistryValueKind.DWord);
                    }
                }
                catch { }
            }
        }

        private static bool GetFlag(string name, bool fallback = false)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key))
                    return k?.GetValue(name) is int v ? v == 1 : fallback;
            }
            catch { return fallback; }
        }

        private static void SetFlag(string name, bool value)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                    k?.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
        }

        public static bool SimpleView
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(Key))
                        return !(k?.GetValue("View") is string v) || v != "detailed";
                }
                catch { return true; }
            }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(Key))
                        k?.SetValue("View", value ? "simple" : "detailed");
                }
                catch { }
            }
        }
    }
}
