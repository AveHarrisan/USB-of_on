using System.Drawing;
using System.Windows.Forms;

namespace USBofon
{
    /// <summary>Спокойное современное оформление панели и меню: без градиентов и рамок девяностых.</summary>
    internal sealed class ModernColors : ProfessionalColorTable
    {
        private static readonly Color Surface = Color.White;
        private static readonly Color Hover = Color.FromArgb(238, 242, 255);
        private static readonly Color Pressed = Color.FromArgb(224, 231, 255);
        private static readonly Color Line = Color.FromArgb(226, 232, 240);

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
            using (var pen = new Pen(Color.FromArgb(226, 232, 240)))
                e.Graphics.DrawLine(pen, 0, e.AffectedBounds.Bottom - 1, e.ToolStrip.Width, e.AffectedBounds.Bottom - 1);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            if (!item.Selected && !item.Pressed && !(item is ToolStripButton button && button.Checked))
                return;

            var color = item.Pressed ? Color.FromArgb(224, 231, 255)
                : item is ToolStripButton b && b.Checked && !item.Selected ? Color.FromArgb(232, 236, 252)
                : Color.FromArgb(238, 242, 255);
            using (var brush = new SolidBrush(color))
            using (var path = Rounded(new Rectangle(1, 1, item.Width - 2, item.Height - 2), 6))
                e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => OnRenderButtonBackground(e);

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (e.Vertical)
            {
                using (var pen = new Pen(Color.FromArgb(226, 232, 240)))
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
