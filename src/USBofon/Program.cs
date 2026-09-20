using System;
using System.Threading;
using System.Windows.Forms;

namespace USBofon
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // Программа живёт в трее, поэтому вторую копию не запускаем — будим первую, и она показывает окно.
            using (var mutex = new Mutex(true, @"Global\USB-of_on-running", out var first))
            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Global\USB-of_on-show"))
            {
                if (!first)
                {
                    show.Set();
                    return;
                }

                Application.EnableVisualStyles();
                // Одно оформление на все меню, включая контекстные.
                ToolStripManager.Renderer = new ModernRenderer();
                Application.SetCompatibleTextRenderingDefault(false);
                var form = new MainForm();

                var listener = new Thread(() =>
                {
                    while (true)
                    {
                        show.WaitOne();
                        try { form.BeginInvoke(new Action(form.ShowFromTray)); }
                        catch (InvalidOperationException) { }
                    }
                }) { IsBackground = true };
                listener.Start();

                Application.Run(form);
                GC.KeepAlive(mutex);
            }
        }
    }
}
