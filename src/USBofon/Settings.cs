using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Win32;

namespace USBofon
{
    /// <summary>
    /// Настройки. Хранятся в файле рядом с именами устройств — в ProgramData, а не в реестре
    /// пользователя: программа запускается от администратора, и у разных запусков реестр мог отличаться,
    /// отчего настройки выглядели сброшенными. Прежние значения из реестра переносятся при первом запуске.
    /// </summary>
    internal static class Settings
    {
        private const string RegistryKey = @"Software\USB-of_on";

        [DataContract]
        private sealed class Item
        {
            [DataMember(Order = 1)] public string Name;
            [DataMember(Order = 2)] public string Value;
        }

        [DataContract]
        private sealed class File_
        {
            [DataMember(Order = 1)] public List<Item> Items = new List<Item>();
        }

        private static readonly object Lock = new object();
        private static readonly Dictionary<string, string> Values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string FilePath { get; private set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "USB-of_on", "settings.json");

        /// <summary>Запасное место, если в ProgramData писать не выходит.</summary>
        private static readonly string UserPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "USB-of_on", "settings.json");

        /// <summary>Что случилось при последней записи — попадает в отчёт.</summary>
        public static string LastSaveResult { get; private set; } = "ещё не сохраняли";

        static Settings()
        {
            try { Load(); }
            catch { }
        }

        // ——— сами настройки ———

        public static bool SimpleView
        {
            get => GetText("View", "simple") != "detailed";
            set => SetText("View", value ? "simple" : "detailed");
        }

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

        /// <summary>Как виджет держится среди окон: 0 — поверх всех, 1 — обычное окно, 2 — на рабочем столе.</summary>
        public static int WidgetLayer
        {
            get => Clamp(GetNumber("WidgetLayer", 0), 0, 2);
            set => SetNumber("WidgetLayer", Clamp(value, 0, 2));
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
        public static Point? WidgetPosition
        {
            get
            {
                var x = GetNumber("WidgetX", int.MinValue);
                var y = GetNumber("WidgetY", int.MinValue);
                return x == int.MinValue || y == int.MinValue ? (Point?)null : new Point(x, y);
            }
            set
            {
                if (value == null) return;
                SetNumber("WidgetX", value.Value.X);
                SetNumber("WidgetY", value.Value.Y);
            }
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

        /// <summary>Спрашивать заряд у самих устройств Logitech по их протоколу. Будит спящие — по умолчанию выключено.</summary>
        public static bool AskDevices
        {
            get => GetFlag("AskDevices");
            set => SetFlag("AskDevices", value);
        }

        public static bool UseSynapse
        {
            get => GetFlag("UseSynapse", true);
            set => SetFlag("UseSynapse", value);
        }

        // ——— чтение и запись ———

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        private static bool GetFlag(string name, bool fallback = false) =>
            GetNumber(name, fallback ? 1 : 0) == 1;

        private static void SetFlag(string name, bool value) => SetNumber(name, value ? 1 : 0);

        private static int GetNumber(string name, int fallback) =>
            int.TryParse(GetText(name, null), out var value) ? value : fallback;

        private static void SetNumber(string name, int value) => SetText(name, value.ToString());

        private static string GetText(string name, string fallback)
        {
            lock (Lock)
                return Values.TryGetValue(name, out var value) ? value : fallback;
        }

        private static void SetText(string name, string value)
        {
            lock (Lock)
            {
                Values[name] = value;
                Save();
            }
        }

        private static void Load()
        {
            // Если в общей папке файла нет, а в пользовательской есть — читаем оттуда.
            if (!System.IO.File.Exists(FilePath) && System.IO.File.Exists(UserPath)) FilePath = UserPath;

            if (System.IO.File.Exists(FilePath))
            {
                using (var stream = System.IO.File.OpenRead(FilePath))
                {
                    var file = (File_)new DataContractJsonSerializer(typeof(File_)).ReadObject(stream);
                    foreach (var item in file.Items ?? new List<Item>())
                        if (!string.IsNullOrEmpty(item.Name)) Values[item.Name] = item.Value;
                }
                return;
            }

            // Первый запуск после обновления: забираем, что было в реестре.
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKey))
                {
                    if (key == null) return;
                    foreach (var name in key.GetValueNames())
                        Values[name] = Convert.ToString(key.GetValue(name));
                }
                if (Values.Count > 0) Save();
            }
            catch { }
        }

        private static void Save()
        {
            if (TrySave(FilePath)) return;

            // В общей папке не вышло — пишем в пользовательскую, чтобы настройки не терялись.
            if (TrySave(UserPath))
            {
                FilePath = UserPath;
                LastSaveResult = "общая папка недоступна, сохраняем в " + UserPath;
            }
        }

        private static bool TrySave(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var file = new File_();
                foreach (var pair in Values) file.Items.Add(new Item { Name = pair.Key, Value = pair.Value });

                using (var stream = System.IO.File.Create(path))
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, true, true))
                    new DataContractJsonSerializer(typeof(File_)).WriteObject(writer, file);

                LastSaveResult = "сохранено в " + path;
                return true;
            }
            catch (Exception ex)
            {
                LastSaveResult = "не удалось записать " + path + ": " + ex.Message;
                return false;
            }
        }
    }
}
