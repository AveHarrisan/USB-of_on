using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace USBofon
{
    /// <summary>Простой вид: устройства карточками, с переключателем и меню.</summary>
    internal sealed class CardView : FlowLayoutPanel
    {
        public static readonly Color Background = Color.FromArgb(243, 244, 246);

        public event Action<UsbDevice, bool> ToggleRequested;
        public event Action<UsbDevice, Control, Point> MenuRequested;
        public event Action<UsbDevice> RenameRequested;

        private readonly Label _empty = new Label();

        public CardView()
        {
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoScroll = true;
            BackColor = Background;
            Padding = new Padding(LogicalToDeviceUnits(16), LogicalToDeviceUnits(8), LogicalToDeviceUnits(16), LogicalToDeviceUnits(16));
            DoubleBuffered = true;

            _empty.AutoSize = false;
            _empty.TextAlign = ContentAlignment.MiddleCenter;
            _empty.ForeColor = Color.FromArgb(107, 114, 128);
            _empty.Font = new Font("Segoe UI", 11f);
        }

        public int HighlightedCount { get; private set; }

        public void SetItems(IList<(UsbDevice Dev, SavedDevice Saved)> items, bool filtered, ICollection<string> highlight)
        {
            HighlightedCount = 0;
            DeviceCard firstHighlighted = null;
            SuspendLayout();
            var old = new List<Control>();
            foreach (Control c in Controls) old.Add(c);
            Controls.Clear();
            foreach (var c in old) if (c != _empty) c.Dispose();

            if (items.Count == 0)
            {
                _empty.Text = filtered ? "Ничего не найдено.\r\nЕсли включено «Настройки → Показывать только подписанные устройства», дайте устройству имя в подробном виде или снимите эту галочку." : "USB-устройств не подключено.\r\nВставьте токен или флешку — они появятся здесь.";
                _empty.Height = LogicalToDeviceUnits(160);
                Controls.Add(_empty);
            }

            bool? section = null;
            foreach (var (dev, saved) in items)
            {
                if (section != dev.Present)
                {
                    section = dev.Present;
                    Controls.Add(new Label
                    {
                        Text = dev.Present ? "ПОДКЛЮЧЕНЫ" : "НЕ ПОДКЛЮЧЕНЫ",
                        AutoSize = false,
                        Height = LogicalToDeviceUnits(34),
                        TextAlign = ContentAlignment.BottomLeft,
                        Font = new Font("Segoe UI Semibold", 8.5f),
                        ForeColor = Color.FromArgb(107, 114, 128),
                        Margin = new Padding(LogicalToDeviceUnits(4), 0, 0, LogicalToDeviceUnits(6)),
                    });
                }
                var card = new DeviceCard(dev, saved) { Highlighted = highlight.Contains(dev.InstanceId) };
                if (card.Highlighted)
                {
                    HighlightedCount++;
                    if (firstHighlighted == null) firstHighlighted = card;
                }
                card.ToggleRequested += (d, on) => ToggleRequested?.Invoke(d, on);
                card.MenuRequested += (d, p) => MenuRequested?.Invoke(d, card, p);
                card.RenameRequested += d => RenameRequested?.Invoke(d);
                Controls.Add(card);
            }
            ResumeLayout(false);
            UpdateWidths();
            PerformLayout();
            // Прокручиваем после раскладки и только если карточку не видно.
            if (firstHighlighted != null && IsHandleCreated)
                BeginInvoke(new Action(() =>
                {
                    if (!firstHighlighted.IsDisposed && !ClientRectangle.Contains(firstHighlighted.Bounds))
                        ScrollControlIntoView(firstHighlighted);
                }));
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            UpdateWidths();
        }

        private void UpdateWidths()
        {
            var width = ClientSize.Width - Padding.Horizontal;
            if (width <= 0) return;
            SuspendLayout();
            foreach (Control c in Controls) c.Width = width - c.Margin.Horizontal;
            ResumeLayout();
        }
    }

    internal sealed class DeviceCard : Control
    {
        public event Action<UsbDevice, bool> ToggleRequested;
        public event Action<UsbDevice, Point> MenuRequested;
        public event Action<UsbDevice> RenameRequested;

        private readonly UsbDevice _dev;
        private readonly SavedDevice _saved;
        private readonly DeviceKind _kind;
        private bool _hover, _hoverToggle, _hoverMenu;

        /// <summary>Только что подключённое устройство, открытое из уведомления.</summary>
        public bool Highlighted { get; set; }

        private static readonly Font TitleFont = new Font("Segoe UI Semibold", 11.5f);
        private static readonly Font TextFont = new Font("Segoe UI", 9f);
        private static readonly Font StatusFont = new Font("Segoe UI Semibold", 9f);
        private static readonly Font GlyphFont = new Font("Segoe MDL2 Assets", 15f);
        private static readonly Font MenuFont = new Font("Segoe MDL2 Assets", 11f);

        private static readonly Color Border = Color.FromArgb(229, 231, 235);
        private static readonly Color TextMain = Color.FromArgb(17, 24, 39);
        private static readonly Color TextSub = Color.FromArgb(107, 114, 128);
        private static readonly Color On = Color.FromArgb(22, 163, 74);
        private static readonly Color Off = Color.FromArgb(203, 213, 225);
        private static readonly Color Danger = Color.FromArgb(220, 38, 38);

        public DeviceCard(UsbDevice dev, SavedDevice saved)
        {
            _dev = dev;
            _saved = saved;
            _kind = DevicePresentation.Kind(dev);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = CardView.Background;
            Height = LogicalToDeviceUnits(76);
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(8));
            Cursor = Cursors.Default;

            var tip = new ToolTip();
            tip.SetToolTip(this, dev.DisplayDescription + (string.IsNullOrEmpty(saved?.Note) ? "" : "\r\n" + saved.Note));
        }

        private bool Hidden => _saved?.Hidden == true;
        private bool CanToggle => _dev.Present && !_dev.IsHub;
        private int S(int v) => LogicalToDeviceUnits(v);

        private Rectangle MenuRect => new Rectangle(Width - S(16) - S(32), (Height - S(32)) / 2, S(32), S(32));
        private Rectangle ToggleRect => new Rectangle(MenuRect.Left - S(12) - S(44), (Height - S(24)) / 2, S(44), S(24));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var card = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Rounded(card, S(10)))
            using (var fill = new SolidBrush(Highlighted ? Color.FromArgb(254, 252, 232) : Color.White))
            using (var pen = new Pen(Highlighted ? Color.FromArgb(245, 158, 11) : _hover ? Color.FromArgb(191, 219, 254) : Border, Highlighted ? S(2) : 1))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            var muted = !_dev.Present || Hidden;
            var accent = muted ? Color.FromArgb(156, 163, 175) : DevicePresentation.Accent(_kind);

            // Значок типа в цветном круге.
            var icon = new Rectangle(S(16), (Height - S(44)) / 2, S(44), S(44));
            using (var b = new SolidBrush(Color.FromArgb(28, accent)))
                g.FillEllipse(b, icon);
            TextRenderer.DrawText(g, DevicePresentation.Glyph(_kind), GlyphFont, icon, accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // Состояние справа.
            string status;
            Color statusColor;
            if (!_dev.Present) { status = "Не подключено"; statusColor = TextSub; }
            else if (_dev.Disabled) { status = "Выключено"; statusColor = Danger; }
            else if (_dev.Problem != 0) { status = "Ошибка " + _dev.Problem; statusColor = Color.FromArgb(217, 119, 6); }
            else { status = "Включено"; statusColor = On; }

            var statusRight = CanToggle ? ToggleRect.Left - S(12) : MenuRect.Left - S(12);
            var statusRect = new Rectangle(statusRight - S(120), 0, S(120), Height);
            TextRenderer.DrawText(g, status, StatusFont, statusRect, statusColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // Название и подпись.
            var friendly = DevicePresentation.FriendlyName(_dev);
            var named = !string.IsNullOrEmpty(_saved?.Name);
            var title = named ? _saved.Name : friendly;

            var parts = new List<string>();
            if (named) parts.Add(friendly);
            parts.Add(DevicePresentation.KindText(_kind));
            if (_dev.Battery.HasValue) parts.Add("заряд " + _dev.Battery.Value + "%");
            if (_dev.Bus == "Bluetooth") parts.Add("Bluetooth");
            if (_dev.DriveLetters.Count > 0) parts.Add("диск " + string.Join(" ", _dev.DriveLetters));
            if (!_dev.Present && !string.IsNullOrEmpty(_saved?.LastSeen)) parts.Add("был " + _saved.LastSeen);
            if (Hidden) parts.Add("скрыто");
            if (!named) parts.Add("двойной щелчок — дать имя");

            var textLeft = icon.Right + S(14);
            var textWidth = statusRect.Left - S(8) - textLeft;
            var titleRect = new Rectangle(textLeft, Height / 2 - S(24), textWidth, S(26));
            var subRect = new Rectangle(textLeft, Height / 2 + S(1), textWidth, S(20));
            TextRenderer.DrawText(g, title, TitleFont, titleRect, muted ? TextSub : TextMain,
                TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, string.Join("  ·  ", parts), TextFont, subRect, TextSub,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            // Переключатель.
            if (CanToggle)
            {
                var t = ToggleRect;
                var isOn = !_dev.Disabled;
                var track = isOn ? On : Off;
                if (_hoverToggle) track = ControlPaint.Dark(track, 0.05f);
                using (var path = Rounded(t, t.Height / 2))
                using (var b = new SolidBrush(track))
                    g.FillPath(b, path);
                var knob = S(18);
                var kx = isOn ? t.Right - S(3) - knob : t.Left + S(3);
                using (var b = new SolidBrush(Color.White))
                    g.FillEllipse(b, kx, t.Top + (t.Height - knob) / 2, knob, knob);
            }

            // Кнопка меню «⋯».
            var m = MenuRect;
            if (_hoverMenu)
                using (var path = Rounded(m, S(6)))
                using (var b = new SolidBrush(Color.FromArgb(243, 244, 246)))
                    g.FillPath(b, path);
            TextRenderer.DrawText(g, "", MenuFont, m, TextSub,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var t = CanToggle && ToggleRect.Contains(e.Location);
            var m = MenuRect.Contains(e.Location);
            if (t != _hoverToggle || m != _hoverMenu || !_hover)
            {
                _hover = true;
                _hoverToggle = t;
                _hoverMenu = m;
                Cursor = t || m ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = _hoverToggle = _hoverMenu = false;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Right)
                MenuRequested?.Invoke(_dev, e.Location);
            else if (MenuRect.Contains(e.Location))
                MenuRequested?.Invoke(_dev, new Point(MenuRect.Left, MenuRect.Bottom));
            else if (CanToggle && ToggleRect.Contains(e.Location))
                ToggleRequested?.Invoke(_dev, _dev.Disabled);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (!MenuRect.Contains(e.Location) && !(CanToggle && ToggleRect.Contains(e.Location)))
                RenameRequested?.Invoke(_dev);
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
    }
}
