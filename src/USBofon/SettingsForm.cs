using System;
using System.Drawing;
using System.Windows.Forms;

namespace USBofon
{
    /// <summary>Все настройки в одном окне. Каждое изменение применяется сразу.</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly Action _apply;
        private bool _loading = true;

        public SettingsForm(Action apply)
        {
            _apply = apply;
            Text = "Настройки — " + AppInfo.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(16);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            var root = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                MaximumSize = new Size(560, 0),
            };

            root.Controls.Add(Head("Список устройств"));
            root.Controls.Add(Check("Показывать только подписанные устройства",
                "В списке останутся только устройства, которым вы дали имя",
                () => Settings.NamedOnly, v => Settings.NamedOnly = v));
            root.Controls.Add(Check("Уведомлять о подключении устройств",
                "Уведомление у часов, когда вставляют флешку, токен или другое устройство",
                () => Settings.NotifyConnected, v => Settings.NotifyConnected = v));

            root.Controls.Add(Head("Запуск"));
            root.Controls.Add(Check("Запускать при входе в Windows",
                "Задача Планировщика с правами администратора, без окна контроля учётных записей",
                () => Autostart.Enabled, SetAutostart));
            root.Controls.Add(Check("Запускаться в трее",
                "При автозапуске окно не открывается — только значок у часов",
                () => Settings.StartMinimized, v => Settings.StartMinimized = v));

            root.Controls.Add(Head("Виджет на рабочем столе"));
            root.Controls.Add(Check("Показывать виджет", "Заряд и состояние устройств поверх окон",
                () => Settings.WidgetVisible, v => Settings.WidgetVisible = v));
            root.Controls.Add(Check("Закрепить виджет",
                "Закреплённый виджет не ловит мышь: щелчки проходят сквозь него, случайно ничего не нажать",
                () => Settings.WidgetLocked, v => Settings.WidgetLocked = v));
            root.Controls.Add(Slider("Плотность подложки", 0, 100, 5,
                () => Settings.WidgetBackground, v => Settings.WidgetBackground = v,
                v => v == 0 ? "без подложки" : v + " %"));
            root.Controls.Add(Slider("Непрозрачность", 40, 100, 5,
                () => Settings.WidgetOpacity, v => Settings.WidgetOpacity = v, v => v + " %"));
            root.Controls.Add(Choice("Что показывать в виджете", new[]
                {
                    "только устройства с зарядом",
                    "с зарядом и подписанные",
                    "все подключённые",
                },
                () => Settings.WidgetContent, v => Settings.WidgetContent = v));
            root.Controls.Add(Button("Вернуть виджет на место", () =>
            {
                Settings.WidgetPosition = new Point(
                    Screen.PrimaryScreen.WorkingArea.Right - 284,
                    Screen.PrimaryScreen.WorkingArea.Top + 24);
                Apply();
            }));

            root.Controls.Add(Head("Заряд устройств"));
            root.Controls.Add(Note("Сами устройства программа не опрашивает — это будило бы беспроводные. "
                                   + "Заряд берётся из данных Windows и у программ производителей."));
            root.Controls.Add(Check("Брать заряд у Logitech G HUB", "Мыши, клавиатуры и гарнитуры Logitech, пока G HUB запущен",
                () => Settings.UseGHub, v => Settings.UseGHub = v));
            root.Controls.Add(Check("Брать заряд из журнала Razer Synapse", "Устройства Razer, пока Synapse запущен",
                () => Settings.UseSynapse, v => Settings.UseSynapse = v));
            root.Controls.Add(Slider("Обновлять заряд раз в", 1, 30, 1,
                () => Settings.BatteryMinutes, v => Settings.BatteryMinutes = v, Minutes));

            var close = new Button { Text = "Закрыть", AutoSize = true, DialogResult = DialogResult.OK, Margin = new Padding(0, 16, 0, 0) };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
            buttons.Controls.Add(close);
            root.Controls.Add(buttons);

            Controls.Add(root);
            AcceptButton = close;
            CancelButton = close;
            _loading = false;
        }

        private static string Minutes(int value) =>
            value == 1 ? "1 минуту" : value < 5 ? value + " минуты" : value + " минут";

        private void SetAutostart(bool value)
        {
            try
            {
                if (value) Autostart.Enable(); else Autostart.Disable();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось изменить автозапуск.\r\n\r\n" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Apply()
        {
            if (_loading) return;
            _apply?.Invoke();
        }

        private static Control Head(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10.5f),
            Margin = new Padding(0, 14, 0, 4),
        };

        private static Control Note(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 6),
        };

        private Control Check(string text, string hint, Func<bool> read, Action<bool> write)
        {
            var box = new CheckBox
            {
                Text = text,
                AutoSize = true,
                Checked = read(),
                Margin = new Padding(0, 2, 0, 0),
            };
            box.CheckedChanged += (s, e) =>
            {
                write(box.Checked);
                Apply();
            };
            new ToolTip().SetToolTip(box, hint);

            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            panel.Controls.Add(box);
            panel.Controls.Add(new Label
            {
                Text = hint,
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(20, 0, 0, 6),
            });
            return panel;
        }

        private Control Slider(string text, int min, int max, int step, Func<int> read, Action<int> write, Func<int, string> format)
        {
            var label = new Label { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            var bar = new TrackBar
            {
                Minimum = min,
                Maximum = max,
                SmallChange = step,
                LargeChange = step * 2,
                TickFrequency = step * 5,
                Value = Math.Min(Math.Max(read(), min), max),
                Width = 260,
                Margin = new Padding(0, 0, 0, 6),
            };
            label.Text = text + ": " + format(bar.Value);
            bar.ValueChanged += (s, e) =>
            {
                label.Text = text + ": " + format(bar.Value);
                write(bar.Value);
                Apply();
            };

            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            panel.Controls.Add(label);
            panel.Controls.Add(bar);
            return panel;
        }

        private Control Choice(string text, string[] options, Func<int> read, Action<int> write)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Margin = new Padding(0, 0, 0, 6) };
            box.Items.AddRange(options);
            box.SelectedIndex = Math.Min(Math.Max(read(), 0), options.Length - 1);
            box.SelectedIndexChanged += (s, e) =>
            {
                write(box.SelectedIndex);
                Apply();
            };

            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            panel.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
            panel.Controls.Add(box);
            return panel;
        }

        private static Control Button(string text, Action click)
        {
            var button = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 2, 0, 6) };
            button.Click += (s, e) => click();
            return button;
        }
    }
}
