using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using System.Windows.Forms;

namespace USBofon
{
    /// <summary>
    /// Запуск при входе в Windows. Через ключ Run нельзя: программе нужны права администратора,
    /// а такие программы Windows при входе не запускает. Поэтому — задача Планировщика с наивысшими правами.
    /// </summary>
    internal static class Autostart
    {
        public const string TaskName = "USB-of_on";
        public const string Argument = "--autostart";

        public static bool IsStartedByAutostart =>
            Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, Argument, StringComparison.OrdinalIgnoreCase));

        /// <summary>Путь к exe, на который смотрит задача, или null, если задачи нет.</summary>
        private static string TaskCommand()
        {
            var (code, output) = Schtasks("/Query /TN \"" + TaskName + "\" /XML");
            if (code != 0) return null;
            var start = output.IndexOf("<Command>", StringComparison.Ordinal);
            var end = output.IndexOf("</Command>", StringComparison.Ordinal);
            if (start < 0 || end < start) return "";
            return SecurityElement.FromString("<c>" + output.Substring(start + 9, end - start - 9) + "</c>")?.Text?.Trim('"') ?? "";
        }

        public static bool Enabled => TaskCommand() != null;

        public static void Enable()
        {
            var xml = Path.Combine(Path.GetTempPath(), "USB-of_on-task.xml");
            try
            {
                File.WriteAllText(xml, TaskXml(), Encoding.Unicode);
                var (code, output) = Schtasks("/Create /TN \"" + TaskName + "\" /XML \"" + xml + "\" /F");
                if (code != 0) throw new InvalidOperationException(output.Trim());
            }
            finally
            {
                try { File.Delete(xml); } catch { }
            }
        }

        public static void Disable()
        {
            var (code, output) = Schtasks("/Delete /TN \"" + TaskName + "\" /F");
            if (code != 0 && Enabled) throw new InvalidOperationException(output.Trim());
        }

        /// <summary>Если программу переставили в другую папку (например, после обновления), перенаправляем задачу.</summary>
        public static void RepairPath()
        {
            try
            {
                var command = TaskCommand();
                if (command != null && !string.Equals(command, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                    Enable();
            }
            catch { }
        }

        private static string TaskXml()
        {
            string E(string s) => SecurityElement.Escape(s);
            var user = Environment.UserDomainName + "\\" + Environment.UserName;
            return
$@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Запуск USB-of_on при входе в Windows</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{E(user)}</UserId>
      <Delay>PT10S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{E(user)}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>""{E(Application.ExecutablePath)}""</Command>
      <Arguments>{Argument}</Arguments>
    </Exec>
  </Actions>
</Task>";
        }

        private static (int Code, string Output) Schtasks(string args)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.GetEncoding(866),
                StandardErrorEncoding = Encoding.GetEncoding(866),
            };
            using (var p = Process.Start(psi))
            {
                var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                return (p.ExitCode, output);
            }
        }
    }
}
