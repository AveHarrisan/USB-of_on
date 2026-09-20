using System;
using System.Drawing;
using System.Windows.Forms;

namespace USBofon
{
    internal sealed class AboutForm : Form
    {
        private readonly Func<bool, System.Threading.Tasks.Task> _checkUpdates;

        public AboutForm(Func<bool, System.Threading.Tasks.Task> checkUpdates)
        {
            _checkUpdates = checkUpdates;
            Text = "О программе";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(16);

            var root = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
            };

            var header = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            header.Controls.Add(new PictureBox
            {
                Image = Resources.Image("icon"),
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(64, 64),
                Margin = new Padding(0, 0, 12, 0),
            });
            var title = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            title.Controls.Add(new Label
            {
                Text = AppInfo.Name,
                AutoSize = true,
                Font = new Font(Font.FontFamily, Font.Size * 1.8f, FontStyle.Bold),
            });
            title.Controls.Add(new Label { Text = "Версия " + AppInfo.VersionText, AutoSize = true });
            title.Controls.Add(new Label
            {
                Text = "USB-устройства: имена, скрытие, включение и выключение.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
            });
            header.Controls.Add(title);
            root.Controls.Add(header);

            root.Controls.Add(Section("Поддержать",
                "Программа делается в свободное время. Если она вам пригодилась:"));
            root.Controls.Add(LinkRow(AppInfo.Support));
            root.Controls.Add(Section("Найти меня", null));
            root.Controls.Add(LinkRow(AppInfo.FindMe));

            var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 16, 0, 0), WrapContents = false };
            var check = new Button { Text = "Проверить обновления", AutoSize = true };
            check.Click += async (s, e) =>
            {
                check.Enabled = false;
                try { await _checkUpdates(true); }
                finally { if (!IsDisposed) check.Enabled = true; }
            };
            var help = new Button { Text = "Инструкция", AutoSize = true };
            help.Click += (s, e) =>
            {
                using (var form = new HelpForm())
                    form.ShowDialog(this);
            };
            var data = new Button { Text = "Папка с сохранениями", AutoSize = true };
            data.Click += (s, e) => OpenDataFolder();
            var close = new Button { Text = "Закрыть", AutoSize = true, DialogResult = DialogResult.OK };
            buttons.Controls.Add(help);
            buttons.Controls.Add(check);
            buttons.Controls.Add(data);
            buttons.Controls.Add(close);
            root.Controls.Add(buttons);

            Controls.Add(root);
            AcceptButton = close;
            CancelButton = close;

            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Theme.Apply(this);
        }

        /// <summary>Имена и скрытые устройства лежат в ProgramData, а не рядом с программой.</summary>
        private static void OpenDataFolder()
        {
            var dir = System.IO.Path.GetDirectoryName(new DeviceStore().FilePath);
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch { }
        }

        private static Control Section(string caption, string text)
        {
            var panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 16, 0, 4),
            };
            panel.Controls.Add(new Label
            {
                Text = caption,
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            });
            if (text != null) panel.Controls.Add(new Label { Text = text, AutoSize = true });
            return panel;
        }

        private static Control LinkRow(AppInfo.Link[] links)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var tip = new ToolTip();
            foreach (var link in links)
            {
                var button = new Button
                {
                    Text = link.Title,
                    Image = new Bitmap(Resources.Image(link.Image), new Size(48, 48)),
                    TextImageRelation = TextImageRelation.ImageAboveText,
                    FlatStyle = FlatStyle.Flat,
                    Size = new Size(120, 90),
                    Cursor = Cursors.Hand,
                    Margin = new Padding(0, 0, 8, 0),
                };
                button.FlatAppearance.BorderColor = SystemColors.ControlLight;
                var url = link.Url;
                button.Click += (s, e) => AppInfo.Open(url);
                tip.SetToolTip(button, link.Hint + "\r\n" + link.Url);
                row.Controls.Add(button);
            }
            return row;
        }
    }

    internal static class Resources
    {
        public static Image Image(string name)
        {
            using (var stream = typeof(Resources).Assembly.GetManifestResourceStream("img." + name + ".png"))
                return new Bitmap(stream);
        }
    }
}
