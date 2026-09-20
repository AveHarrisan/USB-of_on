using System;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;

namespace USBofon
{
    public enum DeviceKind { Token, Storage, Phone, Keyboard, Camera, Audio, Printer, Other }

    /// <summary>Как показать устройство человеку: тип, иконка, понятное название.</summary>
    public static class DevicePresentation
    {
        // Производители токенов ЭЦП: Актив (Рутокен), Аладдин Р.Д. (JaCarta), SafeNet (eToken), ISBC (ESMART).
        private static readonly string[] TokenVendors = { "0A89", "24DC", "0529", "2CE4" };

        public static DeviceKind Kind(UsbDevice d)
        {
            bool Has(params string[] classes) =>
                classes.Any(c => string.Equals(d.ClassName, c, StringComparison.OrdinalIgnoreCase)
                                 || d.ChildClasses.Any(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase)));

            if (Has("SmartCardReader", "SmartCard", "SmartCardFilter") || TokenVendors.Contains(d.Vid ?? "", StringComparer.OrdinalIgnoreCase))
                return DeviceKind.Token;
            if (Has("WPD", "AndroidUsbDeviceClass") || string.Equals(d.Vid, "05AC", StringComparison.OrdinalIgnoreCase))
                return DeviceKind.Phone;
            if (Has("DiskDrive", "CDROM") || string.Equals(d.Service, "USBSTOR", StringComparison.OrdinalIgnoreCase))
                return DeviceKind.Storage;
            if (d.IsInput) return DeviceKind.Keyboard;
            if (Has("Camera", "Image")) return DeviceKind.Camera;
            if (Has("MEDIA", "AudioEndpoint") || AudioWords.IsMatch(string.Join(" ", d.Description, d.BusName, d.Manufacturer)))
                return DeviceKind.Audio;
            if (Has("Printer", "PrintQueue")) return DeviceKind.Printer;
            return DeviceKind.Other;
        }

        public static string KindText(DeviceKind k)
        {
            switch (k)
            {
                case DeviceKind.Token: return "Токен / смарт-карта";
                case DeviceKind.Storage: return "Флешка / диск";
                case DeviceKind.Phone: return "Телефон";
                case DeviceKind.Keyboard: return "Клавиатура / мышь";
                case DeviceKind.Camera: return "Камера";
                case DeviceKind.Audio: return "Звук";
                case DeviceKind.Printer: return "Принтер";
                default: return "USB-устройство";
            }
        }

        /// <summary>Значок из шрифта Segoe MDL2 Assets (есть в Windows 10 и 11).</summary>
        public static string Glyph(DeviceKind k)
        {
            switch (k)
            {
                case DeviceKind.Token: return "";    // ключ
                case DeviceKind.Storage: return "";  // USB-накопитель
                case DeviceKind.Phone: return "";
                case DeviceKind.Keyboard: return "";
                case DeviceKind.Camera: return "";
                case DeviceKind.Audio: return "";
                case DeviceKind.Printer: return "";
                default: return "";
            }
        }

        public static Color Accent(DeviceKind k)
        {
            switch (k)
            {
                case DeviceKind.Token: return Color.FromArgb(217, 119, 6);
                case DeviceKind.Storage: return Color.FromArgb(37, 99, 235);
                case DeviceKind.Phone: return Color.FromArgb(124, 58, 237);
                case DeviceKind.Keyboard: return Color.FromArgb(71, 85, 105);
                case DeviceKind.Camera: return Color.FromArgb(219, 39, 119);
                case DeviceKind.Audio: return Color.FromArgb(5, 150, 105);
                case DeviceKind.Printer: return Color.FromArgb(8, 145, 178);
                default: return Color.FromArgb(100, 116, 139);
            }
        }

        // Звуковые карты со своим драйвером (Focusrite и т. п.) не попадают в класс MEDIA.
        private static readonly Regex AudioWords = new Regex(@"audio|sound|headset|speaker|microphone|звук|динамик|микрофон|наушник",
            RegexOptions.IgnoreCase);

        private static readonly Regex Generic = new Regex(
            @"^(USB[- ]устройство ввода|HID-|Составное USB|USB Composite|USB Input Device|Запоминающее устройство для USB|USB Mass Storage|USB Device$|Универсальный USB|Неизвестное USB)",
            RegexOptions.IgnoreCase);

        /// <summary>Название без служебных слов Windows: «Kingston DataTraveler 3.0», а не «Запоминающее устройство для USB».</summary>
        public static string FriendlyName(UsbDevice d)
        {
            // Название от программы производителя точнее системного: «G502 X LIGHTSPEED», а не «USB Receiver».
            if (!string.IsNullOrWhiteSpace(d.KnownName)) return d.KnownName.Trim();

            string Clean(string s) => Regex.Replace(s ?? "", @"\s+USB Device$", "", RegexOptions.IgnoreCase).Trim();

            foreach (var candidate in new[] { d.BusName }.Concat(d.Children).Concat(new[] { d.Description }))
            {
                var c = Clean(candidate);
                if (c.Length > 0 && !Generic.IsMatch(c)) return c;
            }
            var fallback = Clean(d.BusName);
            return fallback.Length > 0 && !string.Equals(fallback, "USB", StringComparison.OrdinalIgnoreCase)
                ? fallback
                : Clean(d.Description);
        }
    }
}
