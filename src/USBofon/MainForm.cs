using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace USBofon
{
    internal sealed class MainForm : Form
    {
        private readonly DeviceStore _store = new DeviceStore();
        private List<UsbDevice> _devices = new List<UsbDevice>();

        private readonly ListView _list = new ListView();
        private readonly TextBox _search = new TextBox();
        private readonly CheckBox _showHidden = new CheckBox();
        private readonly CheckBox _showService = new CheckBox();
        private readonly CheckBox _showAbsent = new CheckBox();
        private readonly ToolStripStatusLabel _status = new ToolStripStatusLabel();
        private readonly Timer _deviceChangeTimer = new Timer { Interval = 800 };

        private ToolStripButton _btnRename, _btnHide, _btnEnable, _btnDisable;
        private ToolStripMenuItem _miRename, _miHide, _miEnable, _miDisable;
        private bool _refreshing, _refreshPending;

        public MainForm()
        {
            Text = "USB-of_on — USB-устройства";
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(1180, 620);
            MinimumSize = new Size(700, 360);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            BuildUi();

            _deviceChangeTimer.Tick += (s, e) => { _deviceChangeTimer.Stop(); RefreshDevices(); };
            Load += (s, e) =>
            {
                try { _store.Load(); }
                catch (Exception ex) { ShowError("Не удалось прочитать сохранённые имена:\r\n" + _store.FilePath, ex); }
                RefreshDevices();
            };
        }

        private void BuildUi()
        {
            var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 4, 6, 4) };
            var btnRefresh = new ToolStripButton("Обновить", null, (s, e) => RefreshDevices()) { ToolTipText = "F5" };
            _btnRename = new ToolStripButton("Имя…", null, (s, e) => RenameSelected()) { ToolTipText = "Дать имя устройству (F2)" };
            _btnHide = new ToolStripButton("Скрыть", null, (s, e) => ToggleHiddenSelected()) { ToolTipText = "Скрыть устройство из списка (Del)" };
            _btnEnable = new ToolStripButton("Включить", null, (s, e) => SetEnabledSelected(true));
            _btnDisable = new ToolStripButton("Выключить", null, (s, e) => SetEnabledSelected(false));
            toolbar.Items.AddRange(new ToolStripItem[]
            {
                btnRefresh, new ToolStripSeparator(), _btnRename, _btnHide, new ToolStripSeparator(), _btnEnable, _btnDisable,
            });

            var filters = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(8, 6, 8, 2),
                WrapContents = true,
            };
            _search.Width = 260;
            _search.Margin = new Padding(0, 2, 16, 2);
            SetCue(_search, "Поиск по имени, описанию, серийному номеру…");
            _search.TextChanged += (s, e) => FillList();
            _showHidden.Text = "Показывать скрытые";
            _showService.Text = "Показывать хабы и интерфейсы";
            _showAbsent.Text = "Показывать все отключённые ранее";
            foreach (var cb in new[] { _showHidden, _showService, _showAbsent })
            {
                cb.AutoSize = true;
                cb.Margin = new Padding(0, 4, 16, 2);
                cb.CheckedChanged += (s, e) => FillList();
            }
            filters.Controls.AddRange(new Control[] { _search, _showHidden, _showService, _showAbsent });

            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.GridLines = true;
            _list.Columns.Add("Имя", 200);
            _list.Columns.Add("Состояние", 110);
            _list.Columns.Add("Устройство", 280);
            _list.Columns.Add("Диск", 55);
            _list.Columns.Add("Состав", 240);
            _list.Columns.Add("Производитель", 130);
            _list.Columns.Add("VID:PID", 85);
            _list.Columns.Add("Серийный номер", 150);
            _list.Columns.Add("Заметка", 200);
            _list.SelectedIndexChanged += (s, e) => UpdateButtons();
            _list.DoubleClick += (s, e) => RenameSelected();
            _list.KeyDown += OnListKeyDown;

            var menu = new ContextMenuStrip();
            _miRename = new ToolStripMenuItem("Имя и заметка…", null, (s, e) => RenameSelected());
            _miHide = new ToolStripMenuItem("Скрыть", null, (s, e) => ToggleHiddenSelected());
            _miEnable = new ToolStripMenuItem("Включить", null, (s, e) => SetEnabledSelected(true));
            _miDisable = new ToolStripMenuItem("Выключить", null, (s, e) => SetEnabledSelected(false));
            menu.Items.AddRange(new ToolStripItem[]
            {
                _miRename, _miHide, new ToolStripSeparator(), _miEnable, _miDisable, new ToolStripSeparator(),
                new ToolStripMenuItem("Копировать сведения", null, (s, e) => CopySelected()),
            });
            _list.ContextMenuStrip = menu;

            var statusStrip = new StatusStrip();
            _status.Spring = true;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            statusStrip.Items.Add(_status);

            Controls.Add(_list);
            Controls.Add(filters);
            Controls.Add(toolbar);
            Controls.Add(statusStrip);
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F5) { RefreshDevices(); e.Handled = true; }
            };
            UpdateButtons();
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F2) RenameSelected();
            else if (e.KeyCode == Keys.Delete) ToggleHiddenSelected();
            else if (e.KeyCode == Keys.Enter) RenameSelected();
            else if (e.Control && e.KeyCode == Keys.C) CopySelected();
            else if (e.Control && e.KeyCode == Keys.A)
                foreach (ListViewItem item in _list.Items) item.Selected = true;
            else return;
            e.Handled = true;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // Windows сообщает о любом подключении/отключении — обновляем список с небольшой задержкой.
            if (m.Msg == NativeMethods.WM_DEVICECHANGE)
            {
                _deviceChangeTimer.Stop();
                _deviceChangeTimer.Start();
            }
        }

        private async void RefreshDevices()
        {
            if (_refreshing) { _refreshPending = true; return; }
            _refreshing = true;
            _status.Text = "Обновление…";
            try
            {
                do
                {
                    _refreshPending = false;
                    _devices = await Task.Run(() => DeviceManager.Enumerate());
                } while (_refreshPending);

                var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                var changed = false;
                foreach (var dev in _devices)
                {
                    var saved = _store.Find(dev.InstanceId);
                    if (saved == null) continue;
                    if (dev.Present && saved.LastSeen != now) { saved.LastSeen = now; changed = true; }
                    if (saved.LastDescription != dev.DisplayDescription) { saved.LastDescription = dev.DisplayDescription; changed = true; }
                }
                if (changed) TrySave();
                FillList();
            }
            catch (Exception ex)
            {
                ShowError("Не удалось получить список устройств.", ex);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void FillList()
        {
            var selected = new HashSet<string>(SelectedDevices().Select(d => d.InstanceId), StringComparer.OrdinalIgnoreCase);
            var query = _search.Text.Trim();

            var visible = _devices
                .Select(d => new { Dev = d, Saved = _store.Find(d.InstanceId) })
                .Where(x => _showHidden.Checked || x.Saved == null || !x.Saved.Hidden)
                .Where(x => _showService.Checked || !(x.Dev.IsHub || x.Dev.IsInterface))
                .Where(x => x.Dev.Present || _showAbsent.Checked || !string.IsNullOrEmpty(x.Saved?.Name))
                .Where(x => query.Length == 0 || Matches(x.Dev, x.Saved, query))
                .OrderByDescending(x => x.Dev.Present)
                .ThenByDescending(x => !string.IsNullOrEmpty(x.Saved?.Name))
                .ThenBy(x => x.Saved?.Name ?? x.Dev.DisplayDescription, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var x in visible)
            {
                var d = x.Dev;
                var item = new ListViewItem(x.Saved?.Name ?? "") { Tag = d };
                item.SubItems.Add(d.StatusText);
                item.SubItems.Add(d.DisplayDescription);
                item.SubItems.Add(string.Join(" ", d.DriveLetters));
                item.SubItems.Add(string.Join("; ", d.Children));
                item.SubItems.Add(d.Manufacturer ?? "");
                item.SubItems.Add(d.VidPid);
                item.SubItems.Add(d.Serial ?? "");
                item.SubItems.Add(x.Saved?.Note ?? "");

                if (!d.Present) item.ForeColor = SystemColors.GrayText;
                else if (d.Disabled) item.ForeColor = Color.Firebrick;
                else if (d.Problem != 0) item.ForeColor = Color.DarkOrange;
                if (x.Saved != null && x.Saved.Hidden)
                {
                    item.Font = new Font(_list.Font, FontStyle.Italic);
                    item.ForeColor = SystemColors.GrayText;
                }
                if (!string.IsNullOrEmpty(x.Saved?.Name))
                    item.UseItemStyleForSubItems = true;

                item.Selected = selected.Contains(d.InstanceId);
                _list.Items.Add(item);
            }
            _list.EndUpdate();

            var present = _devices.Count(d => d.Present && !d.IsHub && !d.IsInterface);
            var disabled = _devices.Count(d => d.Present && d.Disabled);
            _status.Text = $"Показано: {visible.Count}   Подключено устройств: {present}   Выключено: {disabled}   Данные: {_store.FilePath}";
            UpdateButtons();
        }

        private static bool Matches(UsbDevice d, SavedDevice s, string q)
        {
            var hay = string.Join("\n", s?.Name, s?.Note, d.DisplayDescription, d.Manufacturer, d.Serial, d.VidPid,
                d.InstanceId, string.Join(" ", d.Children), string.Join(" ", d.DriveLetters));
            return hay.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private List<UsbDevice> SelectedDevices() =>
            _list.SelectedItems.Cast<ListViewItem>().Select(i => (UsbDevice)i.Tag).ToList();

        private void UpdateButtons()
        {
            var sel = SelectedDevices();
            var any = sel.Count > 0;
            var allHidden = any && sel.All(d => _store.Find(d.InstanceId)?.Hidden == true);
            var hideText = allHidden ? "Показать" : "Скрыть";

            _btnRename.Enabled = _miRename.Enabled = sel.Count == 1;
            _btnHide.Enabled = _miHide.Enabled = any;
            _btnHide.Text = _miHide.Text = hideText;
            _btnEnable.Enabled = _miEnable.Enabled = sel.Any(d => d.Present && d.Disabled);
            _btnDisable.Enabled = _miDisable.Enabled = sel.Any(d => d.Present && !d.Disabled);
        }

        private void RenameSelected()
        {
            var sel = SelectedDevices();
            if (sel.Count != 1) return;
            var dev = sel[0];
            using (var dlg = new EditForm(dev, _store.Find(dev.InstanceId)))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var saved = _store.GetOrAdd(dev.InstanceId);
                saved.Name = dlg.DeviceName.Length == 0 ? null : dlg.DeviceName;
                saved.Note = dlg.Note.Length == 0 ? null : dlg.Note;
                saved.LastDescription = dev.DisplayDescription;
                if (dev.Present) saved.LastSeen = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                _store.RemoveIfEmpty(dev.InstanceId);
            }
            TrySave();
            FillList();
        }

        private void ToggleHiddenSelected()
        {
            var sel = SelectedDevices();
            if (sel.Count == 0) return;
            var hide = !sel.All(d => _store.Find(d.InstanceId)?.Hidden == true);
            foreach (var dev in sel)
            {
                var saved = _store.GetOrAdd(dev.InstanceId);
                saved.Hidden = hide;
                saved.LastDescription = dev.DisplayDescription;
                _store.RemoveIfEmpty(dev.InstanceId);
            }
            TrySave();
            FillList();
        }

        private void SetEnabledSelected(bool enable)
        {
            var targets = SelectedDevices().Where(d => d.Present && d.Disabled == enable).ToList();
            if (targets.Count == 0) return;

            if (!enable)
            {
                var hubs = targets.Where(d => d.IsHub).ToList();
                if (hubs.Count > 0)
                {
                    MessageBox.Show(this,
                        "USB-хабы выключать нельзя: вместе с ними пропадут все устройства за ними, включая клавиатуру и мышь.\r\n\r\n" +
                        string.Join("\r\n", hubs.Select(Title)),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    targets = targets.Except(hubs).ToList();
                    if (targets.Count == 0) return;
                }

                var names = string.Join("\r\n", targets.Select(d => "• " + Title(d)));
                var warning = targets.Any(d => d.IsInput)
                    ? "\r\n\r\n⚠ Среди них есть клавиатура или мышь — после выключения ими нельзя будет пользоваться."
                    : "";
                if (MessageBox.Show(this, "Выключить устройства?\r\n\r\n" + names + warning, Text,
                        MessageBoxButtons.YesNo, targets.Any(d => d.IsInput) ? MessageBoxIcon.Warning : MessageBoxIcon.Question)
                    != DialogResult.Yes)
                    return;
            }

            var errors = new StringBuilder();
            var reboot = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (var dev in targets)
                {
                    try
                    {
                        if (DeviceManager.SetEnabled(dev.InstanceId, enable) == DeviceManager.ChangeResult.NeedsReboot)
                            reboot = true;
                    }
                    catch (Exception ex)
                    {
                        errors.AppendLine(Title(dev) + ": " + ex.Message);
                    }
                }
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            RefreshDevices();

            if (errors.Length > 0)
                MessageBox.Show(this, "Не получилось:\r\n\r\n" + errors, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (reboot)
                MessageBox.Show(this,
                    "Windows применит изменение только после перезагрузки — скорее всего, устройство сейчас используется (открыт файл с флешки, работает КриптоПро и т. п.).",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void CopySelected()
        {
            var sel = SelectedDevices();
            if (sel.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var d in sel)
            {
                var s = _store.Find(d.InstanceId);
                if (!string.IsNullOrEmpty(s?.Name)) sb.AppendLine("Имя: " + s.Name);
                sb.AppendLine("Устройство: " + d.DisplayDescription);
                sb.AppendLine("Состояние: " + d.StatusText);
                if (d.Children.Count > 0) sb.AppendLine("Состав: " + string.Join("; ", d.Children));
                if (d.DriveLetters.Count > 0) sb.AppendLine("Диск: " + string.Join(" ", d.DriveLetters));
                if (!string.IsNullOrEmpty(d.Manufacturer)) sb.AppendLine("Производитель: " + d.Manufacturer);
                if (!string.IsNullOrEmpty(d.VidPid)) sb.AppendLine("VID:PID: " + d.VidPid);
                if (!string.IsNullOrEmpty(d.Serial)) sb.AppendLine("Серийный номер: " + d.Serial);
                if (!string.IsNullOrEmpty(d.Location)) sb.AppendLine("Порт: " + d.Location);
                sb.AppendLine("ID: " + d.InstanceId);
                if (!string.IsNullOrEmpty(s?.Note)) sb.AppendLine("Заметка: " + s.Note);
                sb.AppendLine();
            }
            Clipboard.SetText(sb.ToString().TrimEnd());
        }

        private string Title(UsbDevice d)
        {
            var name = _store.Find(d.InstanceId)?.Name;
            return string.IsNullOrEmpty(name) ? d.DisplayDescription : name + " (" + d.DisplayDescription + ")";
        }

        private void TrySave()
        {
            try { _store.Save(); }
            catch (Exception ex) { ShowError("Не удалось сохранить:\r\n" + _store.FilePath, ex); }
        }

        private void ShowError(string message, Exception ex) =>
            MessageBox.Show(this, message + "\r\n\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);

        private static void SetCue(TextBox box, string text)
        {
            box.HandleCreated += (s, e) => SendMessage(box.Handle, 0x1501, (IntPtr)1, text);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }
}
