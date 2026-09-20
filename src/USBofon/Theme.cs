using System;
using System.Drawing;
using System.Windows.Forms;
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

        /// <summary>Тёмная ли тема у самого приложения.</summary>
        public static bool AppDark
        {
            get
            {
                switch (Settings.AppTheme)
                {
                    case 1: return true;
                    case 2: return false;
                    default: return SystemDark;
                }
            }
        }

        // Цвета приложения: светлые и тёмные пары.
        public static Color Surface => AppDark ? Color.FromArgb(26, 30, 38) : Color.White;
        public static Color Canvas => AppDark ? Color.FromArgb(18, 21, 27) : Color.FromArgb(243, 244, 246);
        public static Color Card => AppDark ? Color.FromArgb(35, 41, 51) : Color.White;
        public static Color Border => AppDark ? Color.FromArgb(64, 72, 88) : Color.FromArgb(226, 232, 240);
        public static Color Text => AppDark ? Color.FromArgb(248, 250, 252) : Color.FromArgb(17, 24, 39);
        public static Color Subtext => AppDark ? Color.FromArgb(190, 199, 212) : Color.FromArgb(107, 114, 128);
        public static Color Hover => AppDark ? Color.FromArgb(44, 50, 62) : Color.FromArgb(238, 242, 255);
        public static Color Pressed => AppDark ? Color.FromArgb(55, 62, 78) : Color.FromArgb(224, 231, 255);
        /// <summary>Рамка карточки под курсором.</summary>
        public static Color HoverBorder => AppDark ? Color.FromArgb(88, 110, 155) : Color.FromArgb(191, 219, 254);
        /// <summary>Полоса «вышла новая версия».</summary>
        public static Color UpdateBar => AppDark ? Color.FromArgb(22, 48, 38) : Color.FromArgb(232, 245, 233);
        public static Color Link => AppDark ? Color.FromArgb(125, 190, 255) : Color.FromArgb(37, 99, 235);

        public static Color Highlight => AppDark ? Color.FromArgb(70, 60, 24) : Color.FromArgb(254, 243, 199);

        /// <summary>Раскрашивает окно и всё, что внутри, под выбранную тему.</summary>
        public static void Apply(Control root)
        {
            root.BackColor = root is Form ? Surface : root.BackColor;
            Paint(root, true);
            root.Invalidate(true);
        }

        private static void Paint(Control control, bool top)
        {
            foreach (Control child in control.Controls)
            {
                switch (child)
                {
                    case ListView list:
                        list.BackColor = Surface;
                        list.ForeColor = Text;
                        break;
                    case TextBox box:
                        box.BackColor = AppDark ? Color.FromArgb(38, 43, 54) : Color.White;
                        box.ForeColor = Text;
                        break;
                    case RichTextBox rich:
                        rich.BackColor = Surface;
                        rich.ForeColor = Text;
                        break;
                    case ComboBox combo:
                        combo.BackColor = AppDark ? Color.FromArgb(38, 43, 54) : Color.White;
                        combo.ForeColor = Text;
                        break;
                    case Button button:
                        if (button.FlatStyle == FlatStyle.Flat)
                        {
                            button.BackColor = Card;
                            button.ForeColor = Text;
                            button.FlatAppearance.BorderColor = Border;
                        }
                        break;
                    case LinkLabel link:
                        link.BackColor = Color.Transparent;
                        link.LinkColor = Link;
                        link.ActiveLinkColor = Link;
                        link.VisitedLinkColor = Link;
                        break;
                    case Label label:
                        label.ForeColor = label.ForeColor == SystemColors.GrayText || label.ForeColor == Subtext
                            ? Subtext : Text;
                        break;
                    case CheckBox check:
                        check.ForeColor = Text;
                        break;
                    case ToolStrip strip:
                        strip.BackColor = Surface;
                        strip.ForeColor = Text;
                        foreach (ToolStripItem item in strip.Items)
                            if (item.ForeColor == Color.Empty || item.ForeColor == SystemColors.ControlText || item.ForeColor == Text
                                || item.ForeColor == Color.FromArgb(17, 24, 39) || item.ForeColor == Color.FromArgb(237, 240, 245))
                                item.ForeColor = Text;
                        break;
                    case Panel panel:
                        if (panel.BackColor != Color.Transparent) panel.BackColor = top ? Surface : panel.BackColor;
                        break;
                }

                if (child is FlowLayoutPanel flow && flow.BackColor != Color.Transparent) flow.BackColor = Surface;
                Paint(child, false);
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
