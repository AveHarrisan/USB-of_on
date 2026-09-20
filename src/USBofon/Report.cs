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

            Line("== Сводка ==");
            var present = devices.Where(d => d.Present).ToList();
            Line("Всего записей: " + devices.Count + ", подключено: " + present.Count);
            Line("   из них хабов: " + present.Count(d => d.IsHub)
                 + ", служебных интерфейсов: " + present.Count(d => d.IsInterface)
                 + ", частей других устройств (подсветка, слоты): " + present.Count(d => d.IsPart && !d.IsInterface)
                 + ", самостоятельных устройств: " + present.Count(d => !d.IsHub && !d.IsInterface && !d.IsPart));
            Line("   показывается в простом виде: " + present.Count(d => !d.IsHub && !d.IsInterface && !d.IsPart
                                                                        && store.Find(d.InstanceId)?.Hidden != true));
            Line("   с зарядом: " + present.Count(d => d.Battery.HasValue));

            // Несколько записей об одном физическом устройстве — частая причина «откуда столько приёмников».
            Line();
            Line("Записи с одинаковым VID:PID (это одно устройство, показанное частями):");
            foreach (var group in present.Where(d => !string.IsNullOrEmpty(d.VidPid))
                         .GroupBy(d => d.VidPid).Where(g => g.Count() > 1).OrderBy(g => g.Key))
            {
                Line($"   {group.Key} — записей {group.Count()}:");
                foreach (var dev in group)
                    Line($"      {(dev.IsInterface ? "интерфейс" : dev.IsHub ? "хаб" : "устройство")}: {dev.Description} | {dev.InstanceId}");
            }

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
                Line($"   признаки: хаб={dev.IsHub}, интерфейс={dev.IsInterface}, часть другого={dev.IsPart}, ввод={dev.IsInput}, "
                     + $"состояние={dev.StatusText}, тип={DevicePresentation.KindText(DevicePresentation.Kind(dev))}");
                Line("   заряд: " + (dev.Battery.HasValue ? dev.Battery + "%" : "нет")
                     + (string.IsNullOrEmpty(dev.BatterySource) ? "" : "  ← " + dev.BatterySource));
                Line("   родитель: " + (dev.ParentId ?? Parent(dev) ?? "нет"));
                if (dev.Children.Count > 0) Line("   внутри: " + string.Join("; ", dev.Children));
                if (dev.ChildInstanceIds.Count > 0)
                    foreach (var child in dev.ChildInstanceIds)
                        Line("      дочерний id: " + child);
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

        /// <summary>Идентификатор родителя: по нему видно, что «лишние» записи — части одного устройства.</summary>
        private static string Parent(UsbDevice dev)
        {
            if (NativeMethods.CM_Get_Parent(out var parent, dev.DevInst, 0) != NativeMethods.CR_SUCCESS) return null;
            var buffer = new char[512];
            if (NativeMethods.CM_Get_Device_ID(parent, buffer, buffer.Length, 0) != NativeMethods.CR_SUCCESS) return null;
            var text = new string(buffer);
            var zero = text.IndexOf('\0');
            return zero >= 0 ? text.Substring(0, zero) : text.Trim();
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
