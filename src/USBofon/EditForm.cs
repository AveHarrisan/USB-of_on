using System.Drawing;
using System.Windows.Forms;

namespace USBofon
{
    /// <summary>Окно «Имя и заметка» для устройства.</summary>
    internal sealed class EditForm : Form
    {
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _note = new TextBox();

        public string DeviceName => _name.Text.Trim();
        public string Note => _note.Text.Trim();

        public EditForm(UsbDevice dev, SavedDevice saved)
        {
            Text = "Имя устройства";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(480, 260);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
            };

            var info = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(450, 0),
                Text = dev.DisplayDescription + "\r\n" + dev.InstanceId,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 0, 0, 10),
            };

            _name.Text = saved?.Name ?? "";
            _name.Dock = DockStyle.Fill;
            _note.Text = saved?.Note ?? "";
            _note.Dock = DockStyle.Fill;
            _note.Multiline = true;
            _note.Height = 60;
            _note.ScrollBars = ScrollBars.Vertical;

            var reset = new Button
            {
                Text = "Вернуть имя по умолчанию",
                AutoSize = true,
                Visible = !string.IsNullOrEmpty(saved?.Name),
                Margin = new Padding(0, 0, 12, 0),
            };
            reset.Click += (s, e) =>
            {
                // Имя убираем, заметку оставляем — она про устройство, а не про название.
                _name.Text = "";
                DialogResult = DialogResult.OK;
                Close();
            };

            var ok = new Button { Text = "Сохранить", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true };
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(0, 10, 0, 0),
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(reset);

            layout.Controls.Add(info);
            layout.Controls.Add(new Label { Text = "Имя (например, организация):", AutoSize = true });
            layout.Controls.Add(_name);
            layout.Controls.Add(new Label { Text = "Заметка:", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
            layout.Controls.Add(_note);
            layout.Controls.Add(buttons);
            Controls.Add(layout);

            AcceptButton = ok;
            CancelButton = cancel;
            Shown += (s, e) => { _name.Focus(); _name.SelectAll(); };

            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Theme.Apply(this);
        }
    }
}
