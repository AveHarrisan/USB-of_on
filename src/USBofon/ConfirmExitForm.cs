using System.Drawing;
using System.Windows.Forms;

namespace USBofon
{
    /// <summary>«Вы точно хотите закрыть приложение?» — Отмена / Да.</summary>
    internal sealed class ConfirmExitForm : Form
    {
        public ConfirmExitForm()
        {
            Text = AppInfo.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(16);

            var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            row.Controls.Add(new PictureBox
            {
                Image = SystemIcons.Question.ToBitmap(),
                Size = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(0, 0, 12, 0),
            });
            row.Controls.Add(new Label
            {
                Text = "Вы точно хотите закрыть приложение?",
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0),
            });
            root.Controls.Add(row);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0, 16, 0, 0) };
            var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 90 };
            var yes = new Button { Text = "Да", DialogResult = DialogResult.Yes, Width = 90 };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(yes);
            root.Controls.Add(buttons);

            Controls.Add(root);
            AcceptButton = yes;
            CancelButton = cancel;
            Shown += (s, e) => cancel.Focus();

            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Theme.Apply(this);
        }
    }
}
