using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace USBofon
{
    /// <summary>
    /// Отчёт для разбора ошибок: что нашла программа, откуда взяла заряд и что в это время
    /// отвечали G HUB и журнал Synapse. Файл текстовый — перед отправкой его видно целиком.
    /// </summary>
    internal static class Report
    {
        public static string Build(IList<UsbDevice> devices, DeviceStore store)
        {
            var text = new StringBuilder();
            void Line(string value = "") => text.AppendLine(value);

            Line(AppInfo.Name + " — отчёт");
            Line("Собран: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Line("Версия программы: " + AppInfo.VersionText);
            Line("Windows: " + Environment.OSVersion.Version + ", " + (Environment.Is64BitOperatingSystem ? "64-бит" : "32-бит")
                 + ", процесс " + (Environment.Is64BitProcess ? "64-бит" : "32-бит"));
            Line("Права администратора: " + IsAdmin());
            Line("Файл имён: " + store.FilePath);
            Line();

            Line("== Источники заряда ==");
            Line("Опрос заряда включён: " + DeviceManager.PollBattery);
            Line("Брать у G HUB: " + Settings.UseGHub + ", из журнала Synapse: " + Settings.UseSynapse
                 + ", обновление раз в " + Settings.BatteryMinutes + " мин");

            var ghub = Settings.UseGHub ? GHubBattery.Read() : new List<GHubBattery.Entry>();
            Line("G HUB сообщил устройств: " + ghub.Count);
            foreach (var entry in ghub)
                Line($"   pid={entry.Pid} (0x{entry.Pid:X4}) тип={entry.Kind} «{entry.Name}» = {entry.Percent}%");

            var razer = Settings.UseSynapse ? RazerBattery.Read() : new List<(string Name, int Percent)>();
            Line("Журнал Synapse дал записей: " + razer.Count);
            foreach (var entry in razer)
                Line($"   «{entry.Name}» = {entry.Percent}%");
            Line();

            Line("== Устройства ==");
            foreach (var dev in devices.OrderByDescending(d => d.Present).ThenBy(d => d.InstanceId))
            {
                var saved = store.Find(dev.InstanceId);
                Line((dev.Present ? "[подключено] " : "[не подключено] ") + dev.DisplayDescription);
                if (!string.IsNullOrEmpty(saved?.Name)) Line("   имя: " + saved.Name);
                Line("   id: " + dev.InstanceId);
                Line($"   шина: {dev.Bus}, класс: {dev.ClassName}, служба: {dev.Service}");
                Line($"   VID:PID: {dev.VidPid}, серийный: {dev.Serial}");
                Line($"   признаки: хаб={dev.IsHub}, интерфейс={dev.IsInterface}, ввод={dev.IsInput}, "
                     + $"состояние={dev.StatusText}, тип={DevicePresentation.KindText(DevicePresentation.Kind(dev))}");
                Line("   заряд: " + (dev.Battery.HasValue ? dev.Battery + "%" : "нет")
                     + (string.IsNullOrEmpty(dev.BatterySource) ? "" : "  ← " + dev.BatterySource));
                if (dev.Children.Count > 0) Line("   внутри: " + string.Join("; ", dev.Children));
                if (dev.DriveLetters.Count > 0) Line("   диски: " + string.Join(" ", dev.DriveLetters));
                Line();
            }

            Line("В отчёте есть названия и серийные номера ваших устройств. Личных данных, паролей и содержимого дисков нет.");
            return text.ToString();
        }

        /// <summary>Сохраняет отчёт и показывает его в Проводнике.</summary>
        public static void Save(IWin32Window owner, IList<UsbDevice> devices, DeviceStore store)
        {
            var name = "USB-of_on-отчёт-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt";
            using (var dialog = new SaveFileDialog
            {
                Title = "Куда сохранить отчёт",
                FileName = name,
                Filter = "Текстовый файл|*.txt",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            })
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dialog.FileName, Build(devices, store), Encoding.UTF8);
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + dialog.FileName + "\"");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(owner, "Не удалось сохранить отчёт.\r\n\r\n" + ex.Message, AppInfo.Name,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static bool IsAdmin()
        {
            try
            {
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                    return new System.Security.Principal.WindowsPrincipal(identity)
                        .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
