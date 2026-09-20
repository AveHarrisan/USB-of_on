using System;
using System.Drawing;
using Microsoft.Win32;

namespace USBofon
{
    /// <summary>
    /// Цвета оформления. По умолчанию берутся из системы: тёмная или светлая тема Windows
    /// и цвет, выбранный в её настройках. Виджету можно задать свой цвет подложки.
    /// </summary>
    internal static class Theme
    {
        private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string Dwm = @"Software\Microsoft\Windows\DWM";

        /// <summary>Тёмная ли тема у приложений Windows.</summary>
        public static bool SystemDark
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(Personalize))
                        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
                }
                catch { return false; }
            }
        }

        /// <summary>Цвет, выбранный в оформлении Windows.</summary>
        public static Color SystemAccent
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(Dwm))
                    {
                        if (key?.GetValue("AccentColor") is int value)
                        {
                            // В реестре порядок A-B-G-R.
                            var bytes = BitConverter.GetBytes(value);
                            return Color.FromArgb(bytes[2], bytes[1], bytes[0]);
                        }
                    }
                }
                catch { }
                return Color.FromArgb(37, 99, 235);
            }
        }

        /// <summary>Тёмный ли виджет: по системе или как задано в настройках.</summary>
        public static bool WidgetDark
        {
            get
            {
                switch (Settings.WidgetTheme)
                {
                    case 1: return true;
                    case 2: return false;
                    default: return SystemDark;
                }
            }
        }

        /// <summary>Подложка виджета: свой цвет, если задан, иначе по теме.</summary>
        public static Color WidgetBack
        {
            get
            {
                var custom = Settings.WidgetColor;
                if (custom != 0) return Color.FromArgb(custom);
                return WidgetDark ? Color.FromArgb(17, 24, 39) : Color.FromArgb(248, 250, 252);
            }
        }

        public static Color WidgetText => Readable(WidgetBack, Color.White, Color.FromArgb(17, 24, 39));
        public static Color WidgetSubtext => Readable(WidgetBack, Color.FromArgb(203, 213, 225), Color.FromArgb(71, 85, 105));

        /// <summary>Тень под текстом нужна на светлой подложке белому тексту и наоборот.</summary>
        public static Color WidgetShadow => Luminance(WidgetBack) < 0.5 ? Color.Black : Color.White;

        private static Color Readable(Color back, Color onDark, Color onLight) =>
            Luminance(back) < 0.55 ? onDark : onLight;

        public static double Luminance(Color color) =>
            (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;

        /// <summary>Цвет из строки вида #1E2A3A или 1E2A3A; null, если не разобрали.</summary>
        public static Color? Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var value = text.Trim().TrimStart('#');
            if (value.Length != 6 || !int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
                return null;
            return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }

        public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
