using Microsoft.Win32;

namespace USBofon
{
    /// <summary>Личные настройки пользователя (вид окна) — в реестре HKCU.</summary>
    internal static class Settings
    {
        private const string Key = @"Software\USB-of_on";

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
