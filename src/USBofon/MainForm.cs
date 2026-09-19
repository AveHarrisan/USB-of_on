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

        private readonly CardView _cards = new CardView();
        private readonly NotifyIcon _tray = new NotifyIcon();
        private WidgetForm _widget;
        private bool _exitRequested, _trayHintShown, _loaded;

        // Уведомления о подключении: что было подключено при прошлом обновлении и что подсветить.
        private HashSet<string> _knownPresent;
        private List<string> _pendingHighlight;
        private HashSet<string> _highlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer _highlightTimer = new Timer { Interval = 15000 };
        private bool _startHidden = Autostart.IsStartedByAutostart && Settings.StartMinimized;
        private ToolStripButton _viewSimple, _viewDetailed;
        private ToolStripItem[] _detailedOnly;
        private bool _simple = Settings.SimpleView;
        private DateTime _lastToggle;

        private readonly Panel _updateBar = new Panel();
        private readonly Label _updateText = new Label();
        private readonly LinkLabel _updateNotes = new LinkLabel();
        private readonly Button _updateButton = new Button();
        private readonly ProgressBar _updateProgress = new ProgressBar();
        private ToolStripButton _btnUpdate;
        private readonly Timer _updateTimer = new Timer { Interval = 6 * 60 * 60 * 1000 };
        private ReleaseInfo _release;
        private bool _updating;
        private AboutForm _about;

        public MainForm()
        {
            Text = "USB-of_on — USB-устройства";
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(1180, 620);
            MinimumSize = new Size(700, 360);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Text += "  " + AppInfo.VersionText;

            BuildUi();

            _deviceChangeTimer.Tick += (s, e) => { _deviceChangeTimer.Stop(); RefreshDevices(); };
            BuildTray();

            Load += (s, e) =>
            {
                if (_loaded) return;
                _loaded = true;
                Task.Run(() => Autostart.RepairPath());
                if (Settings.WidgetVisible) ShowWidget(true);
                try { _store.Load(); }
                catch (Exception ex) { ShowError("Не удалось прочитать сохранённые имена:\r\n" + _store.FilePath, ex); }
                RefreshDevices();
                Updater.Cleanup();
                _ = CheckUpdates(false);
                _updateTimer.Start();
            };
            _updateTimer.Tick += (s, e) => { _ = CheckUpdates(false); };
        }

        private void BuildUi()
        {
            var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 4, 6, 4) };
            var btnRefresh = new ToolStripButton("Обновить", null, (s, e) => RefreshDevices()) { ToolTipText = "F5" };
            _btnRename = new ToolStripButton("Имя…", null, (s, e) => RenameSelected()) { ToolTipText = "Дать имя устройству (F2)" };
            _btnHide = new ToolStripButton("Скрыть", null, (s, e) => ToggleHiddenSelected()) { ToolTipText = "Скрыть устройство из списка (Del)" };
            _btnEnable = new ToolStripButton("Включить", null, (s, e) => SetEnabledSelected(true));
            _btnDisable = new ToolStripButton("Выключить", null, (s, e) => SetEnabledSelected(false));
            _viewSimple = new ToolStripButton("Простой вид", null, (s, e) => SetView(true)) { ToolTipText = "Карточки устройств" };
            _viewDetailed = new ToolStripButton("Подробный вид", null, (s, e) => SetView(false)) { ToolTipText = "Таблица со всеми сведениями" };
            var sep1 = new ToolStripSeparator();
            var sep2 = new ToolStripSeparator();
            _detailedOnly = new ToolStripItem[] { sep1, _btnRename, _btnHide, sep2, _btnEnable, _btnDisable };
            toolbar.Items.AddRange(new ToolStripItem[]
            {
                _viewSimple, _viewDetailed, new ToolStripSeparator(), btnRefresh, sep1, _btnRename, _btnHide, sep2, _btnEnable, _btnDisable,
            });

            var btnAbout = new ToolStripButton("О программе", null, (s, e) => ShowAbout()) { Alignment = ToolStripItemAlignment.Right };
            var btnSettings = BuildSettingsMenu();
            var btnSupport = new ToolStripDropDownButton("Поддержать") { Alignment = ToolStripItemAlignment.Right, ForeColor = Color.Firebrick };
            foreach (var link in AppInfo.Support)
            {
                var url = link.Url;
                btnSupport.DropDownItems.Add(new ToolStripMenuItem(link.Title + " — " + link.Hint,
                    new Bitmap(Resources.Image(link.Image), new Size(16, 16)), (s, e) => AppInfo.Open(url)));
            }
            _btnUpdate = new ToolStripButton("Обновить", null, (s, e) => StartUpdate())
            {
                Alignment = ToolStripItemAlignment.Right,
                Visible = false,
                Font = new Font(toolbar.Font, FontStyle.Bold),
                ForeColor = Color.SeaGreen,
            };
            toolbar.Items.AddRange(new ToolStripItem[] { btnAbout, btnSettings, btnSupport, _btnUpdate });

            BuildUpdateBar();

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
            _list.Columns.Add("Заряд", 60);
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

            _cards.Dock = DockStyle.Fill;
            _cards.ToggleRequested += (d, on) =>
            {
                // Двойной щелчок по переключателю не должен дёргать устройство дважды.
                if ((DateTime.Now - _lastToggle).TotalMilliseconds < 800) return;
                _lastToggle = DateTime.Now;
                SetEnabled(new List<UsbDevice> { d }, on, confirm: false);
            };
            _cards.RenameRequested += RenameDevice;
            _cards.MenuRequested += ShowCardMenu;

            var statusStrip = new StatusStrip();
            _status.Spring = true;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            statusStrip.Items.Add(_status);

            Controls.Add(_list);
            Controls.Add(_cards);
            Controls.Add(filters);
            Controls.Add(_updateBar);
            Controls.Add(toolbar);
            Controls.Add(statusStrip);
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F5) { RefreshDevices(); e.Handled = true; }
            };
            ApplyView();
        }

        private ToolStripDropDownButton BuildSettingsMenu()
        {
            var button = new ToolStripDropDownButton("Настройки") { Alignment = ToolStripItemAlignment.Right };
            var autostart = new ToolStripMenuItem("Запускать при входе в Windows") { CheckOnClick = false };
            var minimized = new ToolStripMenuItem("Запускаться в трее") { CheckOnClick = false, ToolTipText = "При автозапуске окно не открывается — только значок в трее" };

            autostart.Click += (s, e) =>
            {
                try
                {
                    if (autostart.Checked) Autostart.Disable(); else Autostart.Enable();
                }
                catch (Exception ex)
                {
                    ShowError("Не удалось изменить автозапуск.", ex);
                }
            };
            minimized.Click += (s, e) => Settings.StartMinimized = !Settings.StartMinimized;

            // Состояние читаем при каждом открытии: задачу могли удалить в Планировщике вручную.
            button.DropDownOpening += (s, e) =>
            {
                autostart.Checked = Autostart.Enabled;
                minimized.Checked = Settings.StartMinimized;
                minimized.Enabled = autostart.Checked;
            };
            var notify = new ToolStripMenuItem("Уведомлять о подключении устройств")
            {
                ToolTipText = "Уведомление у трея, когда вставляют флешку, токен или другое устройство",
            };
            notify.Click += (s, e) => Settings.NotifyConnected = !Settings.NotifyConnected;
            button.DropDownOpening += (s, e) => notify.Checked = Settings.NotifyConnected;

            var widget = new ToolStripMenuItem("Виджет на рабочем столе");
            widget.Click += (s, e) => ShowWidget(!Settings.WidgetVisible);
            var widgetLock = new ToolStripMenuItem("Закрепить виджет")
            {
                ToolTipText = "Закреплённый виджет не ловит мышь — случайно ничего не нажать",
            };
            widgetLock.Click += (s, e) =>
            {
                Settings.WidgetLocked = !Settings.WidgetLocked;
                _widget?.ApplyLock();
            };
            button.DropDownOpening += (s, e) =>
            {
                widget.Checked = Settings.WidgetVisible;
                widgetLock.Checked = Settings.WidgetLocked;
                widgetLock.Enabled = Settings.WidgetVisible;
            };

            var namedOnly = new ToolStripMenuItem("Показывать только подписанные устройства")
            {
                ToolTipText = "Только устройства, которым вы дали имя",
            };
            namedOnly.Click += (s, e) =>
            {
                Settings.NamedOnly = !Settings.NamedOnly;
                FillList();
            };
            button.DropDownOpening += (s, e) => namedOnly.Checked = Settings.NamedOnly;

            button.DropDownItems.AddRange(new ToolStripItem[] { namedOnly, notify, new ToolStripSeparator(), widget, widgetLock, new ToolStripSeparator(), autostart, minimized });
            return button;
        }

        private void SetView(bool simple)
        {
            _simple = simple;
            Settings.SimpleView = simple;
            ApplyView();
            FillList();
        }

        private void ApplyView()
        {
            _viewSimple.Checked = _simple;
            _viewDetailed.Checked = !_simple;
            _cards.Visible = _simple;
            _list.Visible = !_simple;
            _showService.Visible = !_simple;
            _showAbsent.Visible = !_simple;
            foreach (var item in _detailedOnly) item.Visible = !_simple;
            if (_simple) _cards.BringToFront(); else _list.BringToFront();
        }

        private void ShowCardMenu(UsbDevice dev, Control owner, Point at)
        {
            var saved = _store.Find(dev.InstanceId);
            var list = new List<UsbDevice> { dev };
            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem(string.IsNullOrEmpty(saved?.Name) ? "Дать имя…" : "Переименовать…", null, (s, e) => RenameDevice(dev)));
            if (dev.Present && !dev.IsHub)
                menu.Items.Add(new ToolStripMenuItem(dev.Disabled ? "Включить" : "Выключить", null, (s, e) => SetEnabled(list, dev.Disabled, confirm: false)));
            menu.Items.Add(new ToolStripMenuItem(saved?.Hidden == true ? "Показать в списке" : "Скрыть из списка", null, (s, e) => ToggleHidden(list)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Копировать сведения", null, (s, e) => CopyInfo(list)));
            menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(owner, at);
        }

        private void BuildUpdateBar()
        {
            _updateBar.Dock = DockStyle.Top;
            _updateBar.Height = 40;
            _updateBar.BackColor = Color.FromArgb(232, 245, 233);
            _updateBar.Padding = new Padding(10, 6, 10, 6);
            _updateBar.Visible = false;

            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            _updateText.AutoSize = true;
            _updateText.Margin = new Padding(0, 6, 12, 0);
            _updateNotes.Text = "Что нового";
            _updateNotes.AutoSize = true;
            _updateNotes.Margin = new Padding(0, 6, 12, 0);
            _updateNotes.LinkClicked += (s, e) => ShowNotes();
            _updateButton.Text = "Обновить";
            _updateButton.AutoSize = true;
            _updateButton.Margin = new Padding(0, 0, 12, 0);
            _updateButton.Click += (s, e) => StartUpdate();
            _updateProgress.Width = 200;
            _updateProgress.Margin = new Padding(0, 6, 0, 0);
            _updateProgress.Visible = false;
            flow.Controls.AddRange(new Control[] { _updateText, _updateNotes, _updateButton, _updateProgress });
            _updateBar.Controls.Add(flow);
        }

        /// <summary>Проверка обновлений. При ручной проверке сообщаем и об отсутствии новой версии, и об ошибке.</summary>
        public async Task CheckUpdates(bool manual)
        {
            if (_updating) return;
            try
            {
                var release = await Updater.CheckAsync();
                if (release != null)
                {
                    _release = release;
                    _updateText.Text = $"Вышла версия {release.Version.ToString(3)} (у вас {AppInfo.VersionText}).";
                    _updateNotes.Visible = !string.IsNullOrWhiteSpace(release.Notes);
                    _btnUpdate.Text = "Обновить до " + release.Version.ToString(3);
                    _btnUpdate.Visible = true;
                    _updateBar.Visible = true;
                    if (manual)
                    {
                        // Окно «О программе» модальное — закрываем его, а обновление начинаем уже после.
                        _about?.Close();
                        BeginInvoke(new Action(StartUpdate));
                    }
                }
                else if (manual)
                {
                    MessageBox.Show(this, "У вас последняя версия: " + AppInfo.VersionText, Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                if (manual) ShowError("Не удалось проверить обновления.", ex);
            }
        }

        private void ShowNotes()
        {
            if (_release == null) return;
            MessageBox.Show(this, "Версия " + _release.Version.ToString(3) + "\r\n\r\n" + _release.Notes.Trim(), "Что нового",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async void StartUpdate()
        {
            if (_release == null || _updating) return;
            if (MessageBox.Show(this,
                    $"Обновить до версии {_release.Version.ToString(3)}?\r\n\r\nПрограмма закроется, установится новая версия и откроется снова. Имена и скрытые устройства сохранятся.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _updating = true;
            _updateBar.Visible = true;
            _updateButton.Enabled = false;
            _btnUpdate.Enabled = false;
            _updateProgress.Value = 0;
            _updateProgress.Visible = true;
            _updateText.Text = "Скачивание обновления…";
            try
            {
                var progress = new Progress<int>(p => _updateProgress.Value = Math.Max(0, Math.Min(100, p)));
                var setup = await Updater.DownloadAsync(_release, progress, System.Threading.CancellationToken.None);
                _updateText.Text = "Установка…";
                Updater.RunInstaller(setup);
                ExitApp();
            }
            catch (Exception ex)
            {
                _updateText.Text = $"Вышла версия {_release.Version.ToString(3)} (у вас {AppInfo.VersionText}).";
                _updateProgress.Visible = false;
                _updateButton.Enabled = _btnUpdate.Enabled = true;
                ShowError("Не удалось скачать обновление.", ex);
            }
            finally
            {
                _updating = false;
            }
        }

        private void ShowAbout()
        {
            using (_about = new AboutForm(CheckUpdates))
                _about.ShowDialog(this);
            _about = null;
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

        private void BuildTray()
        {
            _tray.Icon = Icon;
            _tray.Text = AppInfo.Name;
            _tray.Visible = true;

            var menu = new ContextMenuStrip();
            var open = new ToolStripMenuItem("Открыть " + AppInfo.Name, null, (s, e) => ShowFromTray()) { Font = new Font(menu.Font, FontStyle.Bold) };
            var widget = new ToolStripMenuItem("Виджет на рабочем столе", null, (s, e) => ShowWidget(!Settings.WidgetVisible));
            var widgetLock = new ToolStripMenuItem("Закрепить виджет", null, (s, e) =>
            {
                Settings.WidgetLocked = !Settings.WidgetLocked;
                _widget?.ApplyLock();
            });
            menu.Opening += (s, e) =>
            {
                widget.Checked = Settings.WidgetVisible;
                widgetLock.Checked = Settings.WidgetLocked;
                widgetLock.Enabled = Settings.WidgetVisible;
            };
            var exit = new ToolStripMenuItem("Закрыть приложение", null, (s, e) => ConfirmExit());
            menu.Items.AddRange(new ToolStripItem[] { open, widget, widgetLock, new ToolStripSeparator(), exit });
            _tray.ContextMenuStrip = menu;
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
            _tray.BalloonTipClicked += (s, e) => OpenHighlighted();
            _highlightTimer.Tick += (s, e) =>
            {
                _highlightTimer.Stop();
                _highlight.Clear();
                FillList();
            };
        }

        private void NotifyConnected()
        {
            var present = new HashSet<string>(
                _devices.Where(d => d.Present && !d.IsHub && !d.IsInterface).Select(d => d.InstanceId),
                StringComparer.OrdinalIgnoreCase);
            var known = _knownPresent;
            _knownPresent = present;
            // Первое обновление после запуска — это не подключение, а то, что уже было вставлено.
            if (known == null || !Settings.NotifyConnected) return;

            var added = _devices
                .Where(d => present.Contains(d.InstanceId) && !known.Contains(d.InstanceId))
                .Where(d => _store.Find(d.InstanceId)?.Hidden != true)
                .ToList();
            if (added.Count == 0) return;

            string title, text;
            if (added.Count == 1)
            {
                var d = added[0];
                var name = _store.Find(d.InstanceId)?.Name;
                var kind = DevicePresentation.KindText(DevicePresentation.Kind(d));
                var letters = d.DriveLetters.Count > 0 ? " (" + string.Join(" ", d.DriveLetters) + ")" : "";
                if (string.IsNullOrEmpty(name))
                {
                    title = "Новое устройство: " + kind.ToLowerInvariant();
                    text = DevicePresentation.FriendlyName(d) + letters + "\r\nНажмите, чтобы открыть и дать имя.";
                }
                else
                {
                    title = "Подключено: " + name;
                    text = DevicePresentation.FriendlyName(d) + letters + "\r\nНажмите, чтобы открыть.";
                }
            }
            else
            {
                title = "Подключено устройств: " + added.Count;
                text = string.Join("\r\n", added.Take(3).Select(Title)) + (added.Count > 3 ? "\r\n…" : "");
            }

            _pendingHighlight = added.Select(d => d.InstanceId).ToList();
            _tray.ShowBalloonTip(6000, title, text, ToolTipIcon.Info);
        }

        /// <summary>Щелчок по уведомлению: открываем окно и подсвечиваем подключённые устройства.</summary>
        private void OpenHighlighted()
        {
            var ids = _pendingHighlight;
            _pendingHighlight = null;
            if (ids == null) return;

            ShowFromTray();
            _highlight = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            FillList();
            _highlightTimer.Stop();
            _highlightTimer.Start();

            // Устройство может быть не видно из-за «только подписанные» — тогда сразу предлагаем дать имя.
            var visible = _simple ? _cards.HighlightedCount : _list.Items.Cast<ListViewItem>().Count(i => _highlight.Contains(((UsbDevice)i.Tag).InstanceId));
            if (visible == 0 && ids.Count == 1)
            {
                var dev = _devices.FirstOrDefault(d => string.Equals(d.InstanceId, ids[0], StringComparison.OrdinalIgnoreCase));
                if (dev != null) RenameDevice(dev);
            }
        }

        /// <summary>Виджет на рабочем столе: список устройств с зарядом, а если таких нет — подписанные.</summary>
        private void ShowWidget(bool show)
        {
            Settings.WidgetVisible = show;
            if (!show)
            {
                _widget?.Close();
                _widget = null;
                return;
            }
            if (_widget == null || _widget.IsDisposed) _widget = new WidgetForm();
            UpdateWidget();
            _widget.Show();
        }

        private void UpdateWidget()
        {
            if (_widget == null || _widget.IsDisposed) return;
            var rows = _devices
                .Select(d => (Dev: d, Saved: _store.Find(d.InstanceId)))
                .Where(x => x.Dev.Present && !x.Dev.IsHub && !x.Dev.IsInterface)
                .Where(x => x.Saved?.Hidden != true)
                .Where(x => x.Dev.Battery.HasValue || !string.IsNullOrEmpty(x.Saved?.Name))
                .OrderByDescending(x => x.Dev.Battery.HasValue)
                .ThenBy(x => x.Dev.Battery ?? 0)
                .ToList();
            _widget.Update(rows);
        }

        public void ShowFromTray()
        {
            _startHidden = false;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        private void ConfirmExit()
        {
            ShowFromTray();
            using (var dlg = new ConfirmExitForm())
                if (dlg.ShowDialog(this) == DialogResult.Yes)
                    ExitApp();
        }

        private void ExitApp()
        {
            _exitRequested = true;
            _tray.Visible = false;
            Close();
        }

        // При автозапуске «в трей» окно не показываем вовсе.
        protected override void SetVisibleCore(bool value)
        {
            if (_startHidden && value)
            {
                if (!IsHandleCreated) CreateHandle();
                OnLoad(EventArgs.Empty);
                _startHidden = false;
                value = false;
            }
            base.SetVisibleCore(value);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Крестик прячет окно в трей. Закрывается программа только из меню в трее,
            // при обновлении и когда Windows завершает работу.
            if (e.CloseReason == CloseReason.UserClosing && !_exitRequested)
            {
                e.Cancel = true;
                Hide();
                if (!_trayHintShown)
                {
                    _trayHintShown = true;
                    _pendingHighlight = null;
                    _tray.ShowBalloonTip(4000, AppInfo.Name,
                        "Программа продолжает работать в трее. Закрыть её можно правой кнопкой мыши по значку.", ToolTipIcon.Info);
                }
                return;
            }
            _tray.Visible = false;
            _widget?.Close();
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            // Завершение работы Windows и установщик обновления (Restart Manager) — закрываемся по-настоящему.
            if (m.Msg == 0x11 /* WM_QUERYENDSESSION */) _exitRequested = true;
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

                NotifyConnected();

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
                .Where(x => !Settings.NamedOnly || !string.IsNullOrEmpty(x.Saved?.Name))
                .Where(x => x.Dev.Present || _showAbsent.Checked || !string.IsNullOrEmpty(x.Saved?.Name))
                .Where(x => query.Length == 0 || Matches(x.Dev, x.Saved, query))
                .OrderByDescending(x => x.Dev.Present)
                .ThenByDescending(x => !string.IsNullOrEmpty(x.Saved?.Name))
                .ThenBy(x => x.Saved?.Name ?? x.Dev.DisplayDescription, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (_simple)
            {
                var cards = _devices
                    .Select(d => (Dev: d, Saved: _store.Find(d.InstanceId)))
                    .Where(x => _showHidden.Checked || x.Saved == null || !x.Saved.Hidden)
                    .Where(x => !x.Dev.IsHub && !x.Dev.IsInterface)
                    .Where(x => !Settings.NamedOnly || !string.IsNullOrEmpty(x.Saved?.Name))
                    .Where(x => x.Dev.Present || !string.IsNullOrEmpty(x.Saved?.Name))
                    .Where(x => query.Length == 0 || Matches(x.Dev, x.Saved, query))
                    .OrderByDescending(x => x.Dev.Present)
                    .ThenByDescending(x => !string.IsNullOrEmpty(x.Saved?.Name))
                    .ThenBy(x => x.Saved?.Name ?? DevicePresentation.FriendlyName(x.Dev), StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                _cards.SetItems(cards, query.Length > 0 || Settings.NamedOnly, _highlight);
            }

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var x in visible)
            {
                var d = x.Dev;
                var item = new ListViewItem(x.Saved?.Name ?? "") { Tag = d };
                item.SubItems.Add(d.StatusText);
                item.SubItems.Add(d.DisplayDescription);
                item.SubItems.Add(d.Battery.HasValue ? d.Battery.Value + "%" : "");
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
                if (_highlight.Contains(d.InstanceId))
                {
                    item.BackColor = Color.FromArgb(254, 243, 199);
                    item.Selected = true;
                }
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            UpdateWidget();
            var firstHighlighted = _list.Items.Cast<ListViewItem>().FirstOrDefault(i => _highlight.Contains(((UsbDevice)i.Tag).InstanceId));
            firstHighlighted?.EnsureVisible();

            var present = _devices.Count(d => d.Present && !d.IsHub && !d.IsInterface);
            var disabled = _devices.Count(d => d.Present && d.Disabled);
            _status.Text = $"Показано: {visible.Count}   Подключено устройств: {present}   Выключено: {disabled}   Данные: {_store.FilePath}";
            UpdateButtons();
        }

        private static bool Matches(UsbDevice d, SavedDevice s, string q)
        {
            var hay = string.Join("\n", s?.Name, s?.Note, d.DisplayDescription, DevicePresentation.FriendlyName(d), d.Manufacturer, d.Serial, d.VidPid,
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
            if (sel.Count == 1) RenameDevice(sel[0]);
        }

        private void RenameDevice(UsbDevice dev)
        {
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

        private void ToggleHiddenSelected() => ToggleHidden(SelectedDevices());

        private void ToggleHidden(List<UsbDevice> sel)
        {
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

        private void SetEnabledSelected(bool enable) => SetEnabled(SelectedDevices(), enable, confirm: true);

        private void SetEnabled(List<UsbDevice> devices, bool enable, bool confirm)
        {
            var targets = devices.Where(d => d.Present && d.Disabled == enable).ToList();
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

                // В простом виде переключатель щёлкают поштучно — спрашиваем только про клавиатуру и мышь.
                if (confirm || targets.Any(d => d.IsInput))
                {
                    var names = string.Join("\r\n", targets.Select(d => "• " + Title(d)));
                    var warning = targets.Any(d => d.IsInput)
                        ? "\r\n\r\n⚠ Среди них есть клавиатура или мышь — после выключения ими нельзя будет пользоваться."
                        : "";
                    if (MessageBox.Show(this, "Выключить устройства?\r\n\r\n" + names + warning, Text,
                            MessageBoxButtons.YesNo, targets.Any(d => d.IsInput) ? MessageBoxIcon.Warning : MessageBoxIcon.Question)
                        != DialogResult.Yes)
                        return;
                }
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

        private void CopySelected() => CopyInfo(SelectedDevices());

        private void CopyInfo(List<UsbDevice> sel)
        {
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
                if (d.Battery.HasValue) sb.AppendLine("Заряд: " + d.Battery.Value + "%");
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
            var friendly = DevicePresentation.FriendlyName(d);
            return string.IsNullOrEmpty(name) ? friendly : name + " (" + friendly + ")";
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
