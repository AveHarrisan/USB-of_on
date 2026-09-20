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


        /// <summary>Плотность подложки виджета, 0–100 %: 0 — только текст поверх обоев.</summary>
        public static int WidgetBackground
        {
            get => Clamp(GetNumber("WidgetBackground", 85), 0, 100);
            set => SetNumber("WidgetBackground", Clamp(value, 0, 100));
        }

        /// <summary>Непрозрачность виджета, 40–100 %.</summary>
        public static int WidgetOpacity
        {
            get => Clamp(GetNumber("WidgetOpacity", 92), 40, 100);
            set => SetNumber("WidgetOpacity", Clamp(value, 40, 100));
        }

        /// <summary>
        /// Что показывать в виджете: 0 — только с зарядом, 1 — с зарядом и подписанные,
        /// 2 — всё подключённое, 3 — только подписанные.
        /// </summary>
        public static int WidgetContent
        {
            get => Clamp(GetNumber("WidgetContent", 1), 0, 3);
            set => SetNumber("WidgetContent", Clamp(value, 0, 3));
        }

        /// <summary>Как часто обновлять заряд, минуты.</summary>
        public static int BatteryMinutes
        {
            get => Clamp(GetNumber("BatteryMinutes", 5), 1, 60);
            set => SetNumber("BatteryMinutes", Clamp(value, 1, 60));
        }

        public static bool UseGHub
        {
            get => GetFlag("UseGHub", true);
            set => SetFlag("UseGHub", value);
        }

        public static bool UseSynapse
        {
            get => GetFlag("UseSynapse", true);
            set => SetFlag("UseSynapse", value);
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        private static int GetNumber(string name, int fallback)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key))
                    return k?.GetValue(name) is int v ? v : fallback;
            }
            catch { return fallback; }
        }

        private static void SetNumber(string name, int value)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                    k?.SetValue(name, value, RegistryValueKind.DWord);
            }
            catch { }
        }

        /// <summary>Тема приложения: 0 — как в Windows, 1 — тёмная, 2 — светлая.</summary>
        public static int AppTheme
        {
            get => Clamp(GetNumber("AppTheme", 0), 0, 2);
            set => SetNumber("AppTheme", Clamp(value, 0, 2));
        }

        /// <summary>Тема виджета: 0 — как в Windows, 1 — тёмная, 2 — светлая.</summary>
        public static int WidgetTheme
        {
            get => Clamp(GetNumber("WidgetTheme", 0), 0, 2);
            set => SetNumber("WidgetTheme", Clamp(value, 0, 2));
        }

        /// <summary>Свой цвет подложки виджета (ARGB). 0 — брать из темы.</summary>
        public static int WidgetColor
        {
            get => GetNumber("WidgetColor", 0);
            set => SetNumber("WidgetColor", value);
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
