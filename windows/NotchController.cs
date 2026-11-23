using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using SD = System.Drawing;
using WF = System.Windows.Forms;

namespace NotchZero
{
    /// <summary>Wires the services to the island and runs its little state machine.</summary>
    internal sealed class NotchController : IDisposable
    {
        private readonly AppSettings _settings = AppSettings.Load();
        private readonly MediaService _media = new MediaService();
        private readonly SystemService _system = new SystemService();
        private readonly PrayerService _prayer;

        private IslandWindow _window;
        private ClosedRow _closed;
        private PeekView _peek;
        private AlertView _alert;
        private DropView _drop;
        private MessageView _message;
        private OpenPanel _open;

        private IslandState _state = IslandState.Closed;
        private ClosedMode _mode = ClosedMode.Plain;
        private MascotMood _mood = MascotMood.Awake;
        private double _lastArea;

        private DispatcherTimer _hoverTimer;
        private DispatcherTimer _moodTimer;
        private DispatcherTimer _fullscreenTimer;
        private DispatcherTimer _topmostTimer;
        private DispatcherTimer _peekTimer;
        private DispatcherTimer _peekDebounce;
        private DispatcherTimer _alertTimer;
        private DispatcherTimer _messageTimer;
        private DispatcherTimer _dragLeaveTimer;

        private DateTime? _hoverSince;
        private DateTime? _leaveSince;
        private PrayerEvent? _pendingAlert;
        private DateTime _pendingAt;
        private bool _menuOpen;
        private bool _modal;
        private bool _dragging;
        private bool _hiddenForFullscreen;
        private bool _wasPlaying;

        private WF.ContextMenuStrip _menu;
        private WF.NotifyIcon _tray;

        public NotchController()
        {
            _prayer = new PrayerService(_settings);
        }

        public void Start()
        {
            _window = new IslandWindow();
            _closed = new ClosedRow();
            _peek = new PeekView(_media);
            _alert = new AlertView();
            _drop = new DropView();
            _message = new MessageView();
            _open = new OpenPanel(_media, _prayer, _system);
            _open.ShareClicked += PickFilesToShare;
            _open.SettingsClicked += button => ShowMenuAt(button);

            foreach (var view in new FrameworkElement[] { _closed, _peek, _alert, _drop, _message, _open })
            {
                view.Opacity = 0;
                view.Visibility = Visibility.Collapsed;
                view.IsHitTestVisible = false;
                _window.AddView(view);
            }

            _window.SurfaceClicked += () =>
            {
                if (_state != IslandState.Open && _state != IslandState.Drop) Open();
            };
            _window.DragHover += OnDragHover;
            _window.FilesDropped += OnFilesDropped;

            ApplyMascot();
            UpdateClosedMode();
            var size = SizeFor(_state);
            _window.SnapTo(size.w, size.h, size.r, 0, true);
            _window.Show();
            ApplyState();

            _media.Changed += OnMediaChanged;
            _media.TrackChanged += SchedulePeek;
            _media.Start();

            _system.Changed += () =>
            {
                if (_state == IslandState.Open) _open.UpdateSystem();
            };
            _system.Start();

            _prayer.Changed += OnPrayerChanged;
            _prayer.PrayerTime += OnPrayerTime;
            _prayer.Start();

            _hoverTimer = Every(50, OnHoverTick);
            _moodTimer = Every(5000, UpdateClosedMode);
            _fullscreenTimer = Every(1500, CheckFullscreen);
            _topmostTimer = Every(4000, () => _window.KeepOnTop());

            BuildTray();

            if (!_settings.FirstRunDone)
            {
                _settings.FirstRunDone = true;
                _settings.Save();
                ShowMessage("Hover here to open NotchZero", 5000);
            }
        }

        public void Dispose()
        {
            foreach (var t in new[] { _hoverTimer, _moodTimer, _fullscreenTimer, _topmostTimer, _peekTimer, _alertTimer, _messageTimer })
                t?.Stop();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
        }

        // MARK: Timers

        private static DispatcherTimer Every(int ms, Action action)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (s, e) =>
            {
                try { action(); }
                catch (Exception ex) { Log.Error(ex, "Timer"); }
            };
            timer.Start();
            return timer;
        }

        private static void After(ref DispatcherTimer slot, int ms, Action action)
        {
            slot?.Stop();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                action();
            };
            slot = timer;
            timer.Start();
        }

        private static void Cancel(ref DispatcherTimer slot)
        {
            slot?.Stop();
            slot = null;
        }

        // MARK: State

        private (double w, double h, double r) SizeFor(IslandState state)
        {
            switch (state)
            {
                case IslandState.Open: return (OpenPanel.PanelWidth, OpenPanel.PanelHeight, 30);
                case IslandState.Drop: return (DropView.ViewWidth, DropView.ViewHeight, 30);
                case IslandState.Alert: return (AlertView.ViewWidth, AlertView.ViewHeight, 30);
                case IslandState.Peek: return (PeekView.ViewWidth, PeekView.ViewHeight, 28);
                case IslandState.Message: return (MessageView.ViewWidth, MessageView.ViewHeight, 22);
                default:
                    return _mode == ClosedMode.Plain ? (96, 8, 4) : (ClosedRow.RowWidth, ClosedRow.RowHeight, 16);
            }
        }

        private void ApplyState()
        {
            var size = SizeFor(_state);
            bool open = _state == IslandState.Open;
            bool glass = open || _state == IslandState.Drop;
            double area = size.w * size.h;
            bool growing = area > _lastArea;
            _lastArea = area;
            _window.AnimateTo(size.w, size.h, size.r, glass ? 1 : 0, !glass, growing);

            Theme.Fade(_closed, _state == IslandState.Closed && _mode != ClosedMode.Plain, 180, 60);
            Theme.Fade(_peek, _state == IslandState.Peek, 200, 80);
            Theme.Fade(_alert, _state == IslandState.Alert, 220, 90);
            Theme.Fade(_drop, _state == IslandState.Drop, 160, 40);
            Theme.Fade(_message, _state == IslandState.Message, 180, 60);
            bool wasOpen = _open.IsHitTestVisible;
            Theme.Fade(_open, open, open ? 240 : 100, open ? 60 : 0);
            if (open && !wasOpen) _open.AnimateIn();

            _open.SetActive(open);
            _media.SetActive(open);
            _system.SetActive(open);
        }

        private void Open()
        {
            if (_state == IslandState.Open) return;
            _hoverSince = null;
            _leaveSince = null;
            Cancel(ref _peekTimer);
            Cancel(ref _alertTimer);
            Cancel(ref _messageTimer);
            if (_state == IslandState.Alert) _pendingAlert = null; // seen it
            _state = IslandState.Open;
            UpdateMood();
            _open.UpdateMusic();
            _open.UpdatePrayer();
            _open.UpdateSystem();
            ApplyState();
        }

        private void Close()
        {
            if (_state == IslandState.Closed) return;
            _state = IslandState.Closed;
            _hoverSince = null;
            _leaveSince = null;
            UpdateClosedMode();
            ApplyState();

            if (_pendingAlert.HasValue && (DateTime.UtcNow - _pendingAt).TotalSeconds < 180)
            {
                var e = _pendingAlert.Value;
                _pendingAlert = null;
                ShowAlert(e);
            }
        }

        private void OnHoverTick()
        {
            if (_hiddenForFullscreen || _window == null) return;
            var now = DateTime.UtcNow;
            switch (_state)
            {
                case IslandState.Open:
                    if (_menuOpen || _modal)
                    {
                        _leaveSince = null;
                        return;
                    }
                    if (_window.IsCursorNear(16))
                    {
                        _leaveSince = null;
                    }
                    else
                    {
                        _leaveSince ??= now;
                        if ((now - _leaveSince.Value).TotalMilliseconds >= 280) Close();
                    }
                    break;

                case IslandState.Drop:
                    break;

                default:
                    bool near = _window.IsCursorNear(_state == IslandState.Closed ? 8 : 2);
                    if (near && !_dragging && !_menuOpen)
                    {
                        _hoverSince ??= now;
                        double dwell = _state == IslandState.Closed ? 220 : 120;
                        if ((now - _hoverSince.Value).TotalMilliseconds >= dwell) Open();
                    }
                    else
                    {
                        _hoverSince = null;
                    }
                    break;
            }
        }

        private void CheckFullscreen()
        {
            bool fullscreen = Native.IsFullscreenAppActive(_window.Handle);
            if (fullscreen == _hiddenForFullscreen) return;
            _hiddenForFullscreen = fullscreen;
            if (fullscreen)
            {
                if (_state == IslandState.Open || _state == IslandState.Drop) Close();
                _window.Hide();
            }
            else
            {
                _window.Show();
                _window.KeepOnTop();
            }
        }

        // MARK: Closed mode and mood

        private void UpdateClosedMode()
        {
            var window = _prayer.CurrentWindow;
            ClosedMode mode;
            if (window.kind != PrayerWindowKind.None) mode = ClosedMode.Prayer;
            else if (_media.IsPlaying) mode = ClosedMode.Music;
            else if (_settings.ShowMascotWhenIdle) mode = ClosedMode.Companion;
            else mode = ClosedMode.Plain;

            string title = null, value = null;
            if (mode == ClosedMode.Prayer)
            {
                title = window.ev.Name.ToString();
                value = window.kind == PrayerWindowKind.Now ? "now" : _prayer.CountdownText(window.ev).Replace("in ", "");
            }
            else if (mode == ClosedMode.Companion)
            {
                var next = _prayer.NextEvent;
                if (next.HasValue)
                {
                    title = next.Value.Name.ToString();
                    value = PrayerApi.Short(next.Value.Time);
                }
            }
            _closed.Configure(mode, _media.IsPlaying, title, value);
            UpdateMood();

            if (mode != _mode)
            {
                _mode = mode;
                if (_state == IslandState.Closed) ApplyState();
            }
        }

        private void UpdateMood()
        {
            var kind = _prayer.CurrentWindow.kind;
            MascotMood mood;
            if (kind == PrayerWindowKind.Now) mood = MascotMood.Praying;
            else if (kind == PrayerWindowKind.Soon) mood = MascotMood.Alert;
            else if (_media.IsPlaying) mood = MascotMood.Vibing;
            else if (Native.IdleSeconds() > 180 || DateTime.Now.Hour < 5) mood = MascotMood.Sleeping;
            else mood = MascotMood.Awake;

            _mood = mood;
            _closed.SetMood(mood);
            _open.SetMood(mood);
        }

        private void ApplyMascot()
        {
            var kind = _settings.MascotKind;
            _closed.Mascot.Kind = kind;
            _open.Mascot.Kind = kind;
            _alert.Mascot.Kind = kind;
        }

        // MARK: Music

        private void OnMediaChanged()
        {
            bool playing = _media.IsPlaying;
            if (playing && !_wasPlaying) SchedulePeek();
            _wasPlaying = playing;
            UpdateClosedMode();
            _peek.Update();
            if (_state == IslandState.Open) _open.UpdateMusic();
        }

        private void SchedulePeek() => After(ref _peekDebounce, 350, ShowSongPeek);

        private void ShowSongPeek()
        {
            if (!_settings.ShowSongPeek || !_media.IsPlaying || string.IsNullOrEmpty(_media.Title)) return;
            if (_state != IslandState.Closed && _state != IslandState.Peek) return;
            if (_prayer.CurrentWindow.kind != PrayerWindowKind.None) return;
            _peek.Update();
            _state = IslandState.Peek;
            ApplyState();
            After(ref _peekTimer, 4000, () =>
            {
                if (_state == IslandState.Peek) Close();
            });
        }

        // MARK: Prayer

        private void OnPrayerChanged()
        {
            UpdateClosedMode();
            if (_state == IslandState.Open) _open.UpdatePrayer();
        }

        private void OnPrayerTime(PrayerEvent e)
        {
            Log.Info($"Prayer time: {e.Name} {PrayerApi.Clock(e.Time)}");
            _media.PauseBriefly(5); // a few seconds' pause, then the music carries on
            if (_state == IslandState.Closed || _state == IslandState.Peek || _state == IslandState.Message)
            {
                ShowAlert(e);
            }
            else
            {
                _pendingAlert = e;
                _pendingAt = DateTime.UtcNow;
            }
        }

        private void ShowAlert(PrayerEvent e)
        {
            Cancel(ref _peekTimer);
            Cancel(ref _messageTimer);
            _alert.Set(e, _prayer.ZoneName);
            _state = IslandState.Alert;
            ApplyState();
            After(ref _alertTimer, 30000, () =>
            {
                if (_state == IslandState.Alert) Close();
            });
        }

        private void ShowMessage(string text, int ms = 2600)
        {
            if (_state != IslandState.Closed && _state != IslandState.Peek && _state != IslandState.Message) return;
            Cancel(ref _peekTimer);
            _message.Set(text);
            _state = IslandState.Message;
            ApplyState();
            After(ref _messageTimer, ms, () =>
            {
                if (_state == IslandState.Message) Close();
            });
        }

        // MARK: Sharing

        private void OnDragHover(bool over)
        {
            if (over)
            {
                Cancel(ref _dragLeaveTimer);
                _dragging = true;
                if (_state != IslandState.Drop)
                {
                    Cancel(ref _peekTimer);
                    Cancel(ref _messageTimer);
                    _state = IslandState.Drop;
                    _drop.SetHot(true);
                    ApplyState();
                }
            }
            else
            {
                After(ref _dragLeaveTimer, 250, () =>
                {
                    _dragging = false;
                    if (_state == IslandState.Drop) Close();
                });
            }
        }

        private void OnFilesDropped(string[] paths)
        {
            Cancel(ref _dragLeaveTimer);
            _dragging = false;
            if (_state == IslandState.Drop) Close();
            ShareFiles(paths);
        }

        private async void ShareFiles(IReadOnlyList<string> paths)
        {
            if (paths == null || paths.Count == 0) return;
            _window.BringToFront();
            bool ok = await ShareService.ShareFilesAsync(_window.Handle, paths);
            if (!ok) ShowMessage("Couldn't open Windows Share");
        }

        private void PickFilesToShare()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose files to share", Multiselect = true };
            bool? picked;
            _modal = true;
            try
            {
                _window.BringToFront();
                picked = dialog.ShowDialog(_window);
            }
            finally
            {
                _modal = false;
            }
            if (picked == true && dialog.FileNames.Length > 0)
            {
                Close();
                ShareFiles(dialog.FileNames);
            }
        }

        // MARK: Menu and tray

        private void BuildTray()
        {
            WF.ToolStripManager.Renderer = new DarkMenuRenderer();
            _menu = new WF.ContextMenuStrip
            {
                ShowCheckMargin = true,
                ShowImageMargin = false,
                Font = new SD.Font("Segoe UI", 9.5f),
            };
            _menu.Opening += (s, e) =>
            {
                BuildMenu();
                e.Cancel = false;
            };
            _menu.Opened += (s, e) => _menuOpen = true;
            _menu.Closed += (s, e) =>
            {
                _menuOpen = false;
                _leaveSince = null;
            };

            _tray = new WF.NotifyIcon
            {
                Text = "NotchZero",
                Icon = LoadTrayIcon(),
                ContextMenuStrip = _menu,
                Visible = true,
            };
            _tray.MouseClick += (s, e) =>
            {
                if (e.Button != WF.MouseButtons.Left) return;
                if (_state == IslandState.Open) Close();
                else Open();
            };
        }

        private static SD.Icon LoadTrayIcon()
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NotchZero.ico");
                if (stream != null) return new SD.Icon(stream, WF.SystemInformation.SmallIconSize);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Tray icon");
            }
            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe)) return SD.Icon.ExtractAssociatedIcon(exe);
            }
            catch
            {
                // Fall through to the default.
            }
            return SD.SystemIcons.Application;
        }

        private void ShowMenuAt(IconButton button)
        {
            try
            {
                var p = button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight + 4));
                _window.BringToFront();
                _menuOpen = true;
                _menu.Show(new SD.Point((int)p.X, (int)p.Y), WF.ToolStripDropDownDirection.BelowLeft);
            }
            catch (Exception ex)
            {
                _menuOpen = false;
                Log.Error(ex, "Menu");
            }
        }

        private static WF.ToolStripMenuItem Item(string text, Action action, bool isChecked = false)
        {
            var item = new WF.ToolStripMenuItem(text) { Checked = isChecked };
            item.Click += (s, e) =>
            {
                try { action(); }
                catch (Exception ex) { Log.Error(ex, "Menu action"); }
            };
            return item;
        }

        private static WF.ToolStripMenuItem Info(string text, bool bold = false)
        {
            var item = new WF.ToolStripMenuItem(text) { Enabled = false };
            if (bold) item.Font = new SD.Font("Segoe UI Semibold", 9.5f);
            return item;
        }

        private static WF.ToolStripMenuItem Submenu(string text)
        {
            var item = new WF.ToolStripMenuItem(text);
            if (item.DropDown is WF.ToolStripDropDownMenu dd)
            {
                dd.ShowCheckMargin = true;
                dd.ShowImageMargin = false;
            }
            return item;
        }

        private void BuildMenu()
        {
            var items = _menu.Items;
            items.Clear();

            items.Add(Info("NotchZero", true));
            items.Add(new WF.ToolStripSeparator());

            var mascot = Submenu("Mascot");
            foreach (var kind in MascotInfo.All)
            {
                var k = kind;
                mascot.DropDownItems.Add(Item(MascotInfo.MenuTitle(k), () =>
                {
                    _settings.MascotKind = k;
                    _settings.Save();
                    ApplyMascot();
                }, _settings.MascotKind == k));
            }
            items.Add(mascot);
            items.Add(Item("Show Mascot When Idle", () =>
            {
                _settings.ShowMascotWhenIdle = !_settings.ShowMascotWhenIdle;
                _settings.Save();
                UpdateClosedMode();
            }, _settings.ShowMascotWhenIdle));
            items.Add(Item("Show Song Peek", () =>
            {
                _settings.ShowSongPeek = !_settings.ShowSongPeek;
                _settings.Save();
            }, _settings.ShowSongPeek));

            items.Add(new WF.ToolStripSeparator());
            string zoneLabel = $"Prayer Zone: {_prayer.ZoneName} ({_prayer.Zone})";
            if (_settings.AutoZone) zoneLabel += _prayer.ZoneIsAutomatic ? " · Auto" : " · Auto (no location)";
            items.Add(Info(zoneLabel));

            var zones = Submenu("Choose Prayer Zone");
            zones.DropDownItems.Add(Item("Automatic (Location)", () => _prayer.SetAutomatic(), _settings.AutoZone));
            zones.DropDownItems.Add(new WF.ToolStripSeparator());
            var list = _prayer.Zones.Count > 0 ? _prayer.Zones : PrayerApi.FallbackZones();
            foreach (var group in list.GroupBy(z => string.IsNullOrEmpty(z.State) ? PrayerApi.ZoneName(z.Code) : z.State))
            {
                var state = Submenu(group.Key);
                foreach (var zone in group)
                {
                    var code = zone.Code;
                    var district = zone.District ?? "";
                    if (district.Length > 64) district = district.Substring(0, 61) + "…";
                    var label = string.IsNullOrEmpty(district) ? code : $"{code} · {district}";
                    state.DropDownItems.Add(Item(label, () => _prayer.SetManualZone(code),
                                                 !_settings.AutoZone && _settings.Zone == code));
                }
                zones.DropDownItems.Add(state);
            }
            items.Add(zones);
            items.Add(Item("Refresh Prayer Times", () => _prayer.Refresh()));

            items.Add(new WF.ToolStripSeparator());
            items.Add(Item("Start with Windows", () => StartupManager.Set(!StartupManager.IsEnabled), StartupManager.IsEnabled));
            items.Add(Item("Open Log Folder", () =>
            {
                System.IO.Directory.CreateDirectory(Log.Dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Log.Dir) { UseShellExecute = true });
            }));

            items.Add(new WF.ToolStripSeparator());
            items.Add(Item("Quit NotchZero", Quit));
        }

        private void Quit()
        {
            Dispose();
            Application.Current.Shutdown();
        }
    }

    /// <summary>Dark, minimal context menu to match the island.</summary>
    internal sealed class DarkMenuRenderer : WF.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(WF.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? SD.Color.FromArgb(240, 240, 242) : SD.Color.FromArgb(150, 150, 155);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(WF.ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = SD.Color.FromArgb(200, 200, 205);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(WF.ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            var r = e.ImageRectangle;
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new SD.Pen(SD.Color.White, 1.6f);
            g.DrawLines(pen, new[]
            {
                new SD.PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.52f),
                new SD.PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.74f),
                new SD.PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.3f),
            });
        }
    }

    internal sealed class DarkMenuColors : WF.ProfessionalColorTable
    {
        private static readonly SD.Color Bg = SD.Color.FromArgb(28, 28, 31);
        private static readonly SD.Color Hover = SD.Color.FromArgb(54, 54, 59);
        private static readonly SD.Color Line = SD.Color.FromArgb(62, 62, 68);

        public override SD.Color ToolStripDropDownBackground => Bg;
        public override SD.Color ImageMarginGradientBegin => Bg;
        public override SD.Color ImageMarginGradientMiddle => Bg;
        public override SD.Color ImageMarginGradientEnd => Bg;
        public override SD.Color MenuBorder => Line;
        public override SD.Color MenuItemBorder => Hover;
        public override SD.Color MenuItemSelected => Hover;
        public override SD.Color MenuItemSelectedGradientBegin => Hover;
        public override SD.Color MenuItemSelectedGradientEnd => Hover;
        public override SD.Color MenuItemPressedGradientBegin => Hover;
        public override SD.Color MenuItemPressedGradientMiddle => Hover;
        public override SD.Color MenuItemPressedGradientEnd => Hover;
        public override SD.Color SeparatorDark => Line;
        public override SD.Color SeparatorLight => Bg;
        public override SD.Color CheckBackground => Bg;
        public override SD.Color CheckSelectedBackground => Hover;
        public override SD.Color CheckPressedBackground => Hover;
    }
}
