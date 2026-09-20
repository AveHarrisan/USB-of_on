using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace USBofon
{
    /// <summary>Настройки разделами: каждый раздел сворачивается, изменения применяются сразу.</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly Action _apply;
        private readonly FlowLayoutPanel _list = new FlowLayoutPanel();
        private readonly Panel _scroll = new Panel();
        private readonly List<(Label Header, string Title, Control Body)> _headers =
            new List<(Label, string, Control)>();
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
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(560, 640);
            BackColor = Color.White;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            BackColor = Theme.Surface;
            // Прокрутка живёт на внешней панели: так она появляется всегда, когда разделы раскрыты.
            _scroll.Dock = DockStyle.Fill;
            _scroll.AutoScroll = true;
            _scroll.Padding = new Padding(18, 14, 4, 14);
            _scroll.BackColor = Theme.Surface;

            _list.FlowDirection = FlowDirection.TopDown;
            _list.WrapContents = false;
            _list.AutoSize = true;
            _list.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _list.Location = new Point(0, 0);
            _list.BackColor = Theme.Surface;
            _scroll.Controls.Add(_list);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Surface };
            var close = new Button
            {
                Text = "Закрыть",
                DialogResult = DialogResult.OK,
                Size = new Size(110, 32),
                FlatStyle = FlatStyle.Flat,
            };
            close.Location = new Point(ClientSize.Width - close.Width - 18, 12);
            close.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            bottom.Controls.Add(close);
            using (var line = new Panel()) { }
            bottom.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0);
            };

            Controls.Add(_scroll);
            Controls.Add(bottom);
            AcceptButton = close;
            CancelButton = close;

            BuildSections();
            ApplyTheme();
            _loading = false;

            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Theme.Apply(this);
        }

        private void BuildSections()
        {
            _list.Controls.Add(Section("Список устройств", true,
                Check("Показывать только подписанные устройства",
                    "В списке останутся только устройства, которым вы дали имя",
                    () => Settings.NamedOnly, v => Settings.NamedOnly = v),
                Check("Уведомлять о подключении устройств",
                    "Уведомление у часов, когда вставляют флешку, токен или другое устройство",
                    () => Settings.NotifyConnected, v => Settings.NotifyConnected = v)));

            _list.Controls.Add(Section("Запуск", false,
                Check("Запускать при входе в Windows",
                    "Задача Планировщика с правами администратора, без окна контроля учётных записей",
                    () => Autostart.Enabled, SetAutostart),
                Check("Запускаться в трее",
                    "При автозапуске окно не открывается — только значок у часов",
                    () => Settings.StartMinimized, v => Settings.StartMinimized = v)));

            _list.Controls.Add(Section("Виджет на рабочем столе", true,
                Check("Показывать виджет", "Заряд и состояние устройств поверх окон",
                    () => Settings.WidgetVisible, v => Settings.WidgetVisible = v),
                Check("Закрепить виджет",
                    "Закреплённый виджет не ловит мышь: щелчки проходят сквозь него",
                    () => Settings.WidgetLocked, v => Settings.WidgetLocked = v),
                Choice("Что показывать", new[]
                    {
                        "только устройства с зарядом",
                        "с зарядом и подписанные",
                        "все подключённые",
                        "только подписанные",
                    },
                    () => Settings.WidgetContent, v => Settings.WidgetContent = v),
                Button("Вернуть виджет на место", () =>
                {
                    Settings.WidgetPosition = new Point(
                        Screen.PrimaryScreen.WorkingArea.Right - 300,
                        Screen.PrimaryScreen.WorkingArea.Top + 24);
                    Apply();
                })));

            _list.Controls.Add(Section("Оформление", false,
                Choice("Тема приложения", new[] { "как в Windows", "тёмная", "светлая" },
                    () => Settings.AppTheme, v => Settings.AppTheme = v)));

            _list.Controls.Add(Section("Внешний вид виджета", false,
                Choice("Тема", new[] { "как в Windows", "тёмная", "светлая" },
                    () => Settings.WidgetTheme, v => Settings.WidgetTheme = v),
                ColorPicker(),
                Slider("Плотность подложки", 0, 100, 5,
                    () => Settings.WidgetBackground, v => Settings.WidgetBackground = v,
                    v => v == 0 ? "без подложки" : v + " %"),
                Slider("Непрозрачность", 40, 100, 5,
                    () => Settings.WidgetOpacity, v => Settings.WidgetOpacity = v, v => v + " %")));

            _list.Controls.Add(Section("Заряд устройств", false,
                Note("Сами устройства программа не опрашивает — это будило бы беспроводные. "
                     + "Заряд берётся из данных Windows и у программ производителей."),
                Check("Брать заряд у Logitech G HUB", "Мыши, клавиатуры и гарнитуры Logitech, пока G HUB запущен",
                    () => Settings.UseGHub, v => Settings.UseGHub = v),
                Check("Брать заряд из журнала Razer Synapse", "Устройства Razer, пока Synapse запущен",
                    () => Settings.UseSynapse, v => Settings.UseSynapse = v),
                Slider("Обновлять заряд раз в", 1, 30, 1,
                    () => Settings.BatteryMinutes, v => Settings.BatteryMinutes = v, Minutes)));
        }

        private static string Minutes(int value) =>
            value == 1 ? "1 минуту" : value < 5 ? value + " минуты" : value + " минут";

        // ——— разделы ———

        private Control Section(string title, bool expanded, params Control[] items)
        {
            var body = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(6, 4, 0, 10),
                Visible = expanded,
            };
            foreach (var item in items) body.Controls.Add(item);

            var header = new Label
            {
                Text = (expanded ? "⌄  " : "›  ") + title,
                AutoSize = false,
                Size = new Size(480, 34),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 10.5f),
                ForeColor = Theme.Text,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 6, 0, 0),
            };
            header.Click += (s, e) =>
            {
                body.Visible = !body.Visible;
                header.Text = (body.Visible ? "⌄  " : "›  ") + title;
                _scroll.PerformLayout();
            };
            header.MouseEnter += (s, e) =>
            {
                header.BackColor = Theme.Hover;
                header.ForeColor = Theme.Text;
            };
            header.MouseLeave += (s, e) =>
            {
                header.BackColor = Theme.Surface;
                header.ForeColor = Theme.Text;
            };
            _headers.Add((header, title, body));

            var panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                Width = 500,
            };
            panel.Controls.Add(header);
            panel.Controls.Add(body);
            return panel;
        }

        // ——— элементы ———

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
            ApplyTheme();       // тему могли переключить прямо здесь — перекрашиваем окно сразу
        }

        /// <summary>Перекрашивает окно настроек под текущую тему.</summary>
        private void ApplyTheme()
        {
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            _scroll.BackColor = Theme.Surface;
            _list.BackColor = Theme.Surface;
            Theme.Apply(this);
            foreach (var (header, title, body) in _headers)
            {
                header.BackColor = Theme.Surface;
                header.ForeColor = Theme.Text;
                header.Text = (body.Visible ? "⌄  " : "›  ") + title;
            }
            Invalidate(true);
        }

        private static Control Note(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(470, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 6),
        };

        private Control Check(string text, string hint, Func<bool> read, Action<bool> write)
        {
            var box = new CheckBox { Text = text, AutoSize = true, Checked = read(), Margin = new Padding(0, 4, 0, 0) };
            box.CheckedChanged += (s, e) => { write(box.Checked); Apply(); };

            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            panel.Controls.Add(box);
            panel.Controls.Add(new Label
            {
                Text = hint,
                AutoSize = true,
                MaximumSize = new Size(448, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(22, 0, 0, 8),
            });
            return panel;
        }

        private Control Slider(string text, int min, int max, int step, Func<int> read, Action<int> write, Func<int, string> format)
        {
            var label = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            var bar = new TrackBar
            {
                Minimum = min,
                Maximum = max,
                SmallChange = step,
                LargeChange = step * 2,
                TickFrequency = Math.Max(step * 5, 1),
                Value = Math.Min(Math.Max(read(), min), max),
                Width = 300,
                Margin = new Padding(0, 0, 0, 8),
            };
            label.Text = text + ": " + format(bar.Value);
            bar.ValueChanged += (s, e) =>
            {
                label.Text = text + ": " + format(bar.Value);
                write(bar.Value);
                Apply();
            };

            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            panel.Controls.Add(label);
            panel.Controls.Add(bar);
            return panel;
        }

        private Control Choice(string text, string[] options, Func<int> read, Action<int> write)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Margin = new Padding(0, 0, 0, 8) };
            box.Items.AddRange(options);
            box.SelectedIndex = Math.Min(Math.Max(read(), 0), options.Length - 1);
            box.SelectedIndexChanged += (s, e) => { write(box.SelectedIndex); Apply(); };

            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            panel.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(0, 8, 0, 2) });
            panel.Controls.Add(box);
            return panel;
        }

        /// <summary>Свой цвет подложки: образец, поле с кодом вида #1E2A3A и выбор из палитры.</summary>
        private Control ColorPicker()
        {
            var sample = new Panel
            {
                Size = new Size(34, 26),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.WidgetBack,
                Margin = new Padding(0, 2, 8, 0),
            };
            var code = new TextBox
            {
                Width = 110,
                Text = Settings.WidgetColor == 0 ? "" : Theme.ToHex(Color.FromArgb(Settings.WidgetColor)),
                Margin = new Padding(0, 3, 8, 0),
            };
            var pick = new Button { Text = "Выбрать…", AutoSize = true, Margin = new Padding(0, 1, 8, 0), FlatStyle = FlatStyle.Flat };
            var reset = new Button { Text = "Как в теме", AutoSize = true, Margin = new Padding(0, 1, 0, 0), FlatStyle = FlatStyle.Flat };

            void Use(Color? color)
            {
                Settings.WidgetColor = color?.ToArgb() ?? 0;
                sample.BackColor = Theme.WidgetBack;
                code.Text = color == null ? "" : Theme.ToHex(color.Value);
                Apply();
            }

            code.TextChanged += (s, e) =>
            {
                if (code.Text.Trim().Length == 0) { Use(null); return; }
                var parsed = Theme.Parse(code.Text);
                if (parsed != null)
                {
                    Settings.WidgetColor = parsed.Value.ToArgb();
                    sample.BackColor = parsed.Value;
                    Apply();
                }
            };
            pick.Click += (s, e) =>
            {
                using (var dialog = new ColorDialog { Color = Theme.WidgetBack, FullOpen = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK) Use(dialog.Color);
            };
            reset.Click += (s, e) => Use(null);

            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
            row.Controls.AddRange(new Control[] { sample, code, pick, reset });

            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            panel.Controls.Add(new Label { Text = "Цвет подложки", AutoSize = true, Margin = new Padding(0, 8, 0, 4) });
            panel.Controls.Add(row);
            return panel;
        }

        private static Control Button(string text, Action click)
        {
            var button = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 4, 0, 8), FlatStyle = FlatStyle.Flat };
            button.Click += (s, e) => click();
            return button;
        }
    }
}
