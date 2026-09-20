using System.Drawing;
using System.Windows.Forms;

namespace USBofon
{
    /// <summary>Спокойное современное оформление панели и меню: без градиентов и рамок девяностых.</summary>
    internal sealed class ModernColors : ProfessionalColorTable
    {
        private static Color Surface => Theme.Surface;
        private static Color Hover => Theme.Hover;
        private static Color Pressed => Theme.Pressed;
        private static Color Line => Theme.Border;

        public override Color ToolStripGradientBegin => Surface;
        public override Color ToolStripGradientMiddle => Surface;
        public override Color ToolStripGradientEnd => Surface;
        public override Color ToolStripContentPanelGradientBegin => Surface;
        public override Color ToolStripContentPanelGradientEnd => Surface;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Pressed;
        public override Color MenuItemPressedGradientMiddle => Pressed;
        public override Color MenuItemPressedGradientEnd => Pressed;
        public override Color ButtonSelectedHighlight => Hover;
        public override Color ButtonSelectedHighlightBorder => Hover;
        public override Color ButtonSelectedBorder => Hover;
        public override Color ButtonSelectedGradientBegin => Hover;
        public override Color ButtonSelectedGradientMiddle => Hover;
        public override Color ButtonSelectedGradientEnd => Hover;
        public override Color ButtonPressedGradientBegin => Pressed;
        public override Color ButtonPressedGradientMiddle => Pressed;
        public override Color ButtonPressedGradientEnd => Pressed;
        public override Color ButtonCheckedGradientBegin => Pressed;
        public override Color ButtonCheckedGradientMiddle => Pressed;
        public override Color ButtonCheckedGradientEnd => Pressed;
        public override Color CheckBackground => Pressed;
        public override Color CheckSelectedBackground => Pressed;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Surface;
        public override Color StatusStripGradientBegin => Surface;
        public override Color StatusStripGradientEnd => Surface;
    }

    internal sealed class ModernRenderer : ToolStripProfessionalRenderer
    {
        public ModernRenderer() : base(new ModernColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // Полоса-разделитель только снизу панели, боковых рамок нет.
            if (e.ToolStrip is ToolStripDropDown)
            {
                base.OnRenderToolStripBorder(e);
                return;
            }
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawLine(pen, 0, e.AffectedBounds.Bottom - 1, e.ToolStrip.Width, e.AffectedBounds.Bottom - 1);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            if (!item.Selected && !item.Pressed && !(item is ToolStripButton button && button.Checked))
                return;

            var color = item.Pressed ? Theme.Pressed
                : item is ToolStripButton b && b.Checked && !item.Selected ? Theme.Pressed
                : Theme.Hover;
            using (var brush = new SolidBrush(color))
            using (var path = Rounded(new Rectangle(1, 1, item.Width - 2, item.Height - 2), 6))
                e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => OnRenderButtonBackground(e);

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // Цвет пунктов задаём сами: иначе в тёмной теме остаётся системный тёмный текст.
            if (!e.Item.Enabled) e.TextColor = Theme.Subtext;
            else if (IsDefaultColor(e.Item.ForeColor)) e.TextColor = Theme.Text;
            base.OnRenderItemText(e);
        }

        private static bool IsDefaultColor(Color color) =>
            color.IsEmpty || color == SystemColors.ControlText || color == SystemColors.MenuText
            || color == Color.FromArgb(17, 24, 39) || color == Color.FromArgb(248, 250, 252);

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            using (var back = new SolidBrush(Theme.Pressed))
            using (var path = Rounded(new Rectangle(e.ImageRectangle.X - 2, e.ImageRectangle.Y - 2,
                e.ImageRectangle.Width + 4, e.ImageRectangle.Height + 4), 4))
                e.Graphics.FillPath(back, path);
            using (var pen = new Pen(Theme.Text, 1.6f))
            {
                var r = e.ImageRectangle;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(pen, new[]
                {
                    new Point(r.Left + 3, r.Top + r.Height / 2),
                    new Point(r.Left + r.Width / 2 - 1, r.Bottom - 4),
                    new Point(r.Right - 3, r.Top + 3),
                });
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (e.Vertical)
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, e.Item.Width / 2, 6, e.Item.Width / 2, e.Item.Height - 6);
                return;
            }
            base.OnRenderSeparator(e);
        }

        private static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r, int radius)
        {
            var d = radius * 2;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
