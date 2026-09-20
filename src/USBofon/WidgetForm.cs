using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace USBofon
{
    /// <summary>
    /// Виджет на рабочем столе: заряд и состояние устройств. Рисуется слоем с попиксельной прозрачностью,
    /// поэтому подложку можно сделать сколь угодно бледной, а текст останется чётким.
    /// Закреплённый виджет не ловит мышь — щелчки проходят сквозь него.
    /// </summary>
    internal sealed class WidgetForm : Form
    {
        private const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
        private const int WM_NCHITTEST = 0x84, HTCAPTION = 2;
        private const int ULW_ALPHA = 2;

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
            Size = new Size(280, 140);

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

        public void ApplyLook() => Redraw();

        public void ApplyLock()
        {
            // «Сквозной для мыши» задаётся при создании окна — пересоздаём его.
            if (IsHandleCreated) RecreateHandle();
            Redraw();
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
            Redraw();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Redraw();
        }

        /// <summary>Перетаскивание за любое место — заголовка у виджета нет.</summary>
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && !Settings.WidgetLocked && m.Result == (IntPtr)1)
                m.Result = (IntPtr)HTCAPTION;
        }

        private bool ShowHeader => !Settings.WidgetLocked;

        private void Redraw()
        {
            if (!IsHandleCreated || !Visible) return;

            var top = ShowHeader ? 40 : 12;
            var height = top + Math.Max(_rows.Count, 1) * 34 + 8;
            var width = ContentWidth();
            if (Width != width || Height != height) Size = new Size(width, height);

            using (var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    Paint(g, top);
                }
                Push(bitmap);
            }
        }

        private void Paint(Graphics g, int top)
        {
            var opacity = Settings.WidgetOpacity / 100.0;
            var backAlpha = (int)Math.Round(255 * (Settings.WidgetBackground / 100.0) * opacity);
            var textAlpha = (int)Math.Round(255 * opacity);

            Color Text(Color color) => Color.FromArgb(textAlpha, color);

            if (backAlpha > 0)
                using (var back = new SolidBrush(Color.FromArgb(backAlpha, Theme.WidgetBack)))
                using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10))
                    g.FillPath(back, path);

            // Тень под текстом нужна, когда подложка почти прозрачная: иначе текст теряется на обоях.
            var shadow = backAlpha < 140;
            var main = Theme.WidgetText;
            var sub = Theme.WidgetSubtext;
            var shadowColor = Theme.WidgetShadow;

            var y = top;
            if (ShowHeader)
                Draw(g, "USB-of_on  ·  перетащите мышью", HeadFont, new RectangleF(14, 12, Width - 28, 18),
                    Text(sub), shadow, textAlpha, shadowColor);

            if (_rows.Count == 0)
            {
                Draw(g, "Нет устройств для показа", NoteFont, new RectangleF(14, y, Width - 28, 20),
                    Text(sub), shadow, textAlpha, shadowColor);
                return;
            }

            foreach (var row in _rows)
            {
                using (var dot = new SolidBrush(Text(row.Accent)))
                    g.FillEllipse(dot, 14, y + 8, 8, 8);

                var right = (float)Width - 14;
                if (row.Battery.HasValue)
                {
                    var percent = row.Battery.Value;
                    var color = percent <= 15 ? Color.FromArgb(248, 113, 113)
                        : percent <= 35 ? Color.FromArgb(251, 191, 36)
                        : Color.FromArgb(74, 222, 128);
                    var text = percent + "%";
                    var size = g.MeasureString(text, TitleFont);
                    Draw(g, text, TitleFont, new RectangleF(right - size.Width, y + 1, size.Width + 2, 20), Text(color), shadow, textAlpha, shadowColor);
                    right -= size.Width + 8;

                    var bar = new RectangleF(right - 42, y + 8, 42, 8);
                    using (var empty = new SolidBrush(Color.FromArgb(Math.Max(textAlpha / 3, 40), 148, 163, 184)))
                        g.FillRectangle(empty, bar);
                    using (var full = new SolidBrush(Text(color)))
                        g.FillRectangle(full, bar.X, bar.Y, Math.Max(2, bar.Width * percent / 100f), bar.Height);
                    right -= 50;
                }
                else if (row.Disabled)
                {
                    var size = g.MeasureString("выкл", NoteFont);
                    Draw(g, "выкл", NoteFont, new RectangleF(right - size.Width, y + 4, size.Width + 2, 18),
                        Text(Color.FromArgb(248, 113, 113)), shadow, textAlpha, shadowColor);
                    right -= size.Width + 8;
                }

                Draw(g, row.Title, TitleFont, new RectangleF(30, y, right - 34, 18), Text(main), shadow, textAlpha, shadowColor);
                Draw(g, row.Note, NoteFont, new RectangleF(30, y + 16, right - 34, 16),
                    Text(sub), shadow, textAlpha, shadowColor);
                y += 34;
            }
        }

        private static void Draw(Graphics g, string text, Font font, RectangleF bounds, Color color, bool shadow, int alpha, Color shadowColor)
        {
            using (var format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
            {
                if (shadow)
                    using (var under = new SolidBrush(Color.FromArgb(Math.Min(alpha, 190), shadowColor)))
                        g.DrawString(text, font, under, new RectangleF(bounds.X + 1, bounds.Y + 1, bounds.Width, bounds.Height), format);
                using (var brush = new SolidBrush(color))
                    g.DrawString(text, font, brush, bounds, format);
            }
        }

        /// <summary>Ширина по самому длинному названию, чтобы имена не обрезались.</summary>
        private int ContentWidth()
        {
            var longest = 0f;
            using (var g = CreateGraphics())
            {
                foreach (var row in _rows)
                {
                    longest = Math.Max(longest, g.MeasureString(row.Title, TitleFont).Width);
                    longest = Math.Max(longest, g.MeasureString(row.Note, NoteFont).Width);
                }
                if (ShowHeader)
                    longest = Math.Max(longest, g.MeasureString("USB-of_on  ·  перетащите мышью", HeadFont).Width - 80);
            }
            return (int)Math.Min(Math.Max(longest + 30 + 110, 240), 460);
        }

        /// <summary>Отдаёт готовую картинку окну: так работает попиксельная прозрачность.</summary>
        private void Push(Bitmap bitmap)
        {
            var screen = GetDC(IntPtr.Zero);
            var memory = CreateCompatibleDC(screen);
            var handle = bitmap.GetHbitmap(Color.FromArgb(0));
            var old = SelectObject(memory, handle);
            try
            {
                var size = new SIZE { cx = bitmap.Width, cy = bitmap.Height };
                var source = new POINT { x = 0, y = 0 };
                var position = new POINT { x = Left, y = Top };
                var blend = new BLENDFUNCTION
                {
                    BlendOp = 0,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = 1,
                };
                UpdateLayeredWindow(Handle, screen, ref position, ref size, memory, ref source, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                SelectObject(memory, old);
                DeleteObject(handle);
                DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION
        {
            public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screenDc, ref POINT position, ref SIZE size,
            IntPtr sourceDc, ref POINT source, int key, ref BLENDFUNCTION blend, int flags);

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    }
}
