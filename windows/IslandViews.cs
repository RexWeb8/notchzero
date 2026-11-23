using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NotchZero
{
    internal enum IslandState { Closed, Open, Drop, Alert, Peek, Message }

    internal enum ClosedMode { Plain, Music, Companion, Prayer }

    /// <summary>
    /// The compact island: white equalizer bars (or the next prayer) on the left, the mascot on the right.
    /// </summary>
    internal sealed class ClosedRow : Canvas
    {
        public const double RowWidth = 180;
        public const double RowHeight = 32;
        private const double Item = 19;

        private readonly EqualizerBars _eq;
        private readonly StackPanel _mini;
        private readonly TextBlock _miniTitle;
        private readonly TextBlock _miniValue;
        private readonly MascotView _mascot;

        public ClosedRow()
        {
            Width = RowWidth;
            Height = RowHeight;

            _eq = Theme.Place(this, new EqualizerBars(Math.Round(Item * 0.72), Math.Round(Item * 0.62)), 17, (RowHeight - Math.Round(Item * 0.62)) / 2);

            _mini = new StackPanel { Width = 50, IsHitTestVisible = false };
            _miniTitle = Theme.Text("", 7, FontWeights.Bold, 0.5);
            _miniTitle.HorizontalAlignment = HorizontalAlignment.Center;
            _miniValue = Theme.Text("", 11, FontWeights.SemiBold, 1, true);
            _miniValue.HorizontalAlignment = HorizontalAlignment.Center;
            _miniValue.Margin = new Thickness(0, -2, 0, 0);
            _mini.Children.Add(_miniTitle);
            _mini.Children.Add(_miniValue);
            Theme.Place(this, _mini, 9, 4);

            _mascot = new MascotView(Item);
            Children.Add(_mascot);
            PlaceMascot();
        }

        public MascotView Mascot => _mascot;

        private void PlaceMascot() =>
            Theme.Move(_mascot, RowWidth - 15 - Item, (RowHeight - _mascot.Height) / 2);

        public void Configure(ClosedMode mode, bool playing, string miniTitle, string miniValue)
        {
            bool music = mode == ClosedMode.Music;
            _eq.Visibility = music ? Visibility.Visible : Visibility.Collapsed;
            _eq.IsPlaying = playing;
            bool mini = (mode == ClosedMode.Companion || mode == ClosedMode.Prayer) && !string.IsNullOrEmpty(miniValue);
            _mini.Visibility = mini ? Visibility.Visible : Visibility.Collapsed;
            _miniTitle.Text = (miniTitle ?? "").ToUpperInvariant();
            _miniValue.Text = miniValue ?? "";
            _mascot.Visibility = mode == ClosedMode.Plain ? Visibility.Collapsed : Visibility.Visible;
        }

        public void SetMood(MascotMood mood)
        {
            _mascot.Mood = mood;
            _mascot.Mat = mood == MascotMood.Praying;
            PlaceMascot();
        }
    }

    /// <summary>A quiet peek when a new song starts: artwork, title, artist, bars, elapsed time.</summary>
    internal sealed class PeekView : Canvas
    {
        public const double ViewWidth = 340;
        public const double ViewHeight = 56;

        private readonly MediaService _media;
        private readonly ArtTile _art;
        private readonly TextBlock _title;
        private readonly TextBlock _artist;
        private readonly TextBlock _time;
        private readonly EqualizerBars _eq;
        private readonly DispatcherTimer _tick;

        public PeekView(MediaService media)
        {
            _media = media;
            Width = ViewWidth;
            Height = ViewHeight;

            _art = Theme.Place(this, new ArtTile(36, 9), 10, 10);
            _title = Theme.Place(this, Theme.Text("", 12.5, FontWeights.SemiBold), 56, 9);
            _title.Width = ViewWidth - 56 - 70;
            _artist = Theme.Place(this, Theme.Text("", 10.5, FontWeights.Medium, 0.55), 56, 27);
            _artist.Width = _title.Width;
            _eq = Theme.Place(this, new EqualizerBars(16, 13), ViewWidth - 18 - 16, 12);
            _eq.IsPlaying = true;
            _time = Theme.Place(this, Theme.Text("0:00", 10, FontWeights.SemiBold, 0.55, true), ViewWidth - 16 - 50, 29);
            _time.Width = 50;
            _time.TextAlignment = TextAlignment.Right;

            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tick.Tick += (s, e) => _time.Text = CardChrome.Time(_media.CurrentPosition());
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible) _tick.Start();
                else _tick.Stop();
            };
        }

        public void Update()
        {
            _art.Image = _media.Artwork;
            _title.Text = string.IsNullOrEmpty(_media.Title) ? "Unknown Track" : _media.Title;
            _artist.Text = string.IsNullOrEmpty(_media.Artist) ? _media.SourceName : _media.Artist;
            _eq.IsPlaying = _media.IsPlaying;
            _time.Text = CardChrome.Time(_media.CurrentPosition());
        }
    }

    /// <summary>"Time for Asr": the mascot on its sejadah, the time and the zone.</summary>
    internal sealed class AlertView : Canvas
    {
        public const double ViewWidth = 340;
        public const double ViewHeight = 84;

        private readonly MascotView _mascot;
        private readonly TextBlock _title;
        private readonly TextBlock _detail;

        public AlertView()
        {
            Width = ViewWidth;
            Height = ViewHeight;
            _mascot = new MascotView(46, true) { Mood = MascotMood.Praying };
            Theme.Place(this, _mascot, 20, (ViewHeight - _mascot.Height) / 2);
            _title = Theme.Place(this, Theme.Text("", 16, FontWeights.SemiBold), 82, 20);
            _title.Width = ViewWidth - 82 - 16;
            _detail = Theme.Place(this, Theme.Text("", 11.5, FontWeights.Medium, 0.55), 82, 45);
            _detail.Width = _title.Width;
        }

        public MascotView Mascot => _mascot;

        public void Set(PrayerEvent e, string zoneName)
        {
            _title.Text = "Time for " + e.Name;
            _detail.Text = PrayerApi.Clock(e.Time) + " · " + zoneName;
        }
    }

    /// <summary>Drop zone shown while files are dragged onto the island.</summary>
    internal sealed class DropView : Canvas
    {
        public const double ViewWidth = 460;
        public const double ViewHeight = 120;

        private readonly DashedFrame _frame;
        private readonly TextBlock _title;

        public DropView()
        {
            Width = ViewWidth;
            Height = ViewHeight;
            _frame = Theme.Place(this, new DashedFrame(ViewWidth - 24, ViewHeight - 24, 20), 12, 12);

            var icon = Theme.Place(this, Theme.Text(Theme.GlyphShare, 20, FontWeights.Normal, 0.9, false, Theme.Icons), 0, 24);
            icon.Width = ViewWidth;
            icon.TextAlignment = TextAlignment.Center;
            _title = Theme.Place(this, Theme.Text("Drop Files to Share", 14, FontWeights.SemiBold), 0, 54);
            _title.Width = ViewWidth;
            _title.TextAlignment = TextAlignment.Center;
            var sub = Theme.Place(this, Theme.Text("Nearby Share, Outlook, WhatsApp and more", 10.5, FontWeights.Medium, 0.5), 0, 76);
            sub.Width = ViewWidth;
            sub.TextAlignment = TextAlignment.Center;
        }

        public void SetHot(bool hot)
        {
            _title.Text = hot ? "Release to Share" : "Drop Files to Share";
            _frame.Hot = hot;
        }
    }

    internal sealed class DashedFrame : FrameworkElement
    {
        private readonly double _radius;
        private bool _hot;

        public DashedFrame(double width, double height, double radius)
        {
            Width = width;
            Height = height;
            _radius = radius;
            IsHitTestVisible = false;
        }

        public bool Hot
        {
            set
            {
                if (_hot == value) return;
                _hot = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            var pen = new Pen(Theme.W(_hot ? 0.55 : 0.28), 1.4)
            {
                DashStyle = new DashStyle(new double[] { 4, 3 }, 0),
                DashCap = PenLineCap.Round,
            };
            if (_hot) dc.DrawRoundedRectangle(Theme.W(0.05), null, new Rect(0, 0, Width, Height), _radius, _radius);
            dc.DrawRoundedRectangle(null, pen, new Rect(0.7, 0.7, Width - 1.4, Height - 1.4), _radius, _radius);
        }
    }

    /// <summary>A one-line message ("Hover the top of the screen…", "Couldn't open Share").</summary>
    internal sealed class MessageView : Canvas
    {
        public const double ViewWidth = 320;
        public const double ViewHeight = 44;
        private readonly TextBlock _text;

        public MessageView()
        {
            Width = ViewWidth;
            Height = ViewHeight;
            _text = Theme.Place(this, Theme.Text("", 12.5, FontWeights.Medium, 0.9), 14, 12);
            _text.Width = ViewWidth - 28;
            _text.TextAlignment = TextAlignment.Center;
        }

        public void Set(string text) => _text.Text = text;
    }

    /// <summary>Time and date on the left; Share and Settings on the right.</summary>
    internal sealed class TopBar : Canvas
    {
        private readonly TextBlock _time;
        private readonly TextBlock _date;
        private readonly DispatcherTimer _tick;

        public event Action ShareClicked;
        public event Action<IconButton> SettingsClicked;

        public TopBar(double width)
        {
            Width = width;
            Height = 30;

            var stack = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
            _time = Theme.Text("", 14, FontWeights.SemiBold, 1, true);
            _date = Theme.Text("", 11.5, FontWeights.Medium, 0.55);
            _date.Margin = new Thickness(8, 0, 0, 0);
            _date.VerticalAlignment = VerticalAlignment.Bottom;
            _date.Padding = new Thickness(0, 0, 0, 1.5);
            stack.Children.Add(_time);
            stack.Children.Add(_date);
            Theme.Place(this, stack, 0, 5);

            var share = Theme.Place(this, new IconButton(Theme.GlyphShare, 13), width - 28 - 6 - 28, 1);
            share.ToolTip = "Share Files";
            share.Click += () => ShareClicked?.Invoke();
            var gear = Theme.Place(this, new IconButton(Theme.GlyphSettings, 13), width - 28, 1);
            gear.ToolTip = "Settings";
            gear.Click += () => SettingsClicked?.Invoke(gear);

            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _tick.Tick += (s, e) => UpdateClock();
            UpdateClock();
        }

        public void SetActive(bool active)
        {
            if (active)
            {
                UpdateClock();
                _tick.Start();
            }
            else
            {
                _tick.Stop();
            }
        }

        private void UpdateClock()
        {
            var now = DateTime.Now;
            _time.Text = now.ToString("h:mm tt", PrayerApi.En);
            _date.Text = now.ToString("ddd, d MMM", PrayerApi.En);
        }
    }

    /// <summary>The open panel: top bar, then music, prayer and system cards.</summary>
    internal sealed class OpenPanel : Canvas
    {
        public const double PanelWidth = 900;
        public const double PanelHeight = 238;

        private readonly TopBar _top;
        private readonly MusicCard _music;
        private readonly PrayerCard _prayer;
        private readonly SystemCard _system;
        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);

        public event Action ShareClicked;
        public event Action<IconButton> SettingsClicked;

        public OpenPanel(MediaService media, PrayerService prayer, SystemService system)
        {
            Width = PanelWidth;
            Height = PanelHeight;
            RenderTransform = _scale;
            RenderTransformOrigin = new Point(0.5, 0);

            _top = Theme.Place(this, new TopBar(PanelWidth - 44), 24, 12);
            _top.ShareClicked += () => ShareClicked?.Invoke();
            _top.SettingsClicked += b => SettingsClicked?.Invoke(b);

            const double y = 52;
            const double gap = 14;
            double prayerWidth = PanelWidth - 32 - MusicCard.Size - SystemCard.CardWidth - gap * 2;
            _music = Theme.Place(this, new MusicCard(media), 16, y);
            _prayer = Theme.Place(this, new PrayerCard(prayer, prayerWidth), 16 + MusicCard.Size + gap, y);
            _system = Theme.Place(this, new SystemCard(system), PanelWidth - 16 - SystemCard.CardWidth, y);
        }

        public MascotView Mascot => _prayer.Mascot;

        public void SetActive(bool active)
        {
            _top.SetActive(active);
            _music.SetActive(active);
            if (active)
            {
                _prayer.Update();
                _system.Update();
            }
        }

        /// <summary>The panel settles in from 96% as the island grows.</summary>
        public void AnimateIn()
        {
            var anim = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            _prayer.SweepIn();
        }

        public void SetMood(MascotMood mood) => _prayer.SetMood(mood);

        public void UpdateMusic() => _music.Update();

        public void UpdatePrayer() => _prayer.Update();

        public void UpdateSystem() => _system.Update();
    }
}
