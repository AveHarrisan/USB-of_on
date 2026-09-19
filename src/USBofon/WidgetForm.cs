using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace USBofon
{
    /// <summary>
    /// Виджет на рабочем столе: заряд и состояние устройств. В заблокированном виде не ловит мышь —
    /// щелчки проходят сквозь него, случайно ничего не нажать. Блокировка снимается из меню в трее.
    /// </summary>
    internal sealed class WidgetForm : Form
    {
        private const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
        private const int WM_NCHITTEST = 0x84, HTCAPTION = 2;

        private List<(string Title, string Note, int? Battery, Color Accent, bool Disabled)> _rows =
            new List<(string, string, int?, Color, bool)>();

        private static readonly Font TitleFont = new Font("Segoe UI Semibold", 9.5f);
        private static readonly Font NoteFont = new Font("Segoe UI", 8f);
        private static readonly Font HeadFont = new Font("Segoe UI Semibold", 8.5f);

        public WidgetForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(17, 24, 39);
            Opacity = 0.92;
            Size = new Size(260, 140);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            var position = Settings.WidgetPosition;
            Location = position ?? new Point(
                Screen.PrimaryScreen.WorkingArea.Right - Width - 24,
                Screen.PrimaryScreen.WorkingArea.Top + 24);

            LocationChanged += (s, e) => { if (Visible) Settings.WidgetPosition = Location; };
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                if (Settings.WidgetLocked) cp.ExStyle |= WS_EX_TRANSPARENT;
                return cp;
            }
        }

        public void ApplyLock()
        {
            // Стиль «сквозной для мыши» задаётся при создании окна — пересоздаём его.
            if (IsHandleCreated) RecreateHandle();
            Invalidate();
        }

        /// <summary>Перетаскивание за любое место — заголовка у виджета нет.</summary>
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && !Settings.WidgetLocked && m.Result == (IntPtr)1)
                m.Result = (IntPtr)HTCAPTION;
        }

        public void Update(IList<(UsbDevice Dev, SavedDevice Saved)> devices)
        {
            var rows = new List<(string, string, int?, Color, bool)>();
            foreach (var (dev, saved) in devices)
            {
                var kind = DevicePresentation.Kind(dev);
                var title = string.IsNullOrEmpty(saved?.Name) ? DevicePresentation.FriendlyName(dev) : saved.Name;
                var note = dev.Battery.HasValue ? DevicePresentation.KindText(kind) : dev.StatusText;
                rows.Add((title, note, dev.Battery, DevicePresentation.Accent(kind), dev.Disabled));
            }
            _rows = rows;
            Height = Math.Max(90, 46 + Math.Max(rows.Count, 1) * 34 + 10);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var head = Settings.WidgetLocked ? "USB-of_on  ·  закреплён" : "USB-of_on  ·  перетащите мышью";
            TextRenderer.DrawText(g, head, HeadFont, new Rectangle(14, 12, Width - 28, 18),
                Color.FromArgb(148, 163, 184), TextFormatFlags.Left);

            var y = 40;
            if (_rows.Count == 0)
            {
                TextRenderer.DrawText(g, "Нет устройств для показа", NoteFont,
                    new Rectangle(14, y, Width - 28, 20), Color.FromArgb(148, 163, 184), TextFormatFlags.Left);
                return;
            }

            foreach (var row in _rows)
            {
                using (var b = new SolidBrush(row.Accent))
                    g.FillEllipse(b, 14, y + 8, 8, 8);

                var right = Width - 14;
                if (row.Battery.HasValue)
                {
                    var percent = row.Battery.Value;
                    var color = percent <= 15 ? Color.FromArgb(248, 113, 113)
                        : percent <= 35 ? Color.FromArgb(251, 191, 36)
                        : Color.FromArgb(74, 222, 128);
                    var text = percent + "%";
                    var size = TextRenderer.MeasureText(g, text, TitleFont);
                    TextRenderer.DrawText(g, text, TitleFont, new Point(right - size.Width, y + 2), color);
                    right -= size.Width + 8;

                    var bar = new Rectangle(right - 42, y + 8, 42, 8);
                    using (var b = new SolidBrush(Color.FromArgb(55, 65, 81)))
                        g.FillRectangle(b, bar);
                    using (var b = new SolidBrush(color))
                        g.FillRectangle(b, bar.Left, bar.Top, Math.Max(2, bar.Width * percent / 100), bar.Height);
                    right -= 50;
                }
                else if (row.Disabled)
                {
                    var size = TextRenderer.MeasureText(g, "выкл", NoteFont);
                    TextRenderer.DrawText(g, "выкл", NoteFont, new Point(right - size.Width, y + 4), Color.FromArgb(248, 113, 113));
                    right -= size.Width + 8;
                }

                TextRenderer.DrawText(g, row.Title, TitleFont, new Rectangle(30, y, right - 34, 18),
                    Color.White, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, row.Note, NoteFont, new Rectangle(30, y + 16, right - 34, 16),
                    Color.FromArgb(148, 163, 184), TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                y += 34;
            }
        }
    }
}
