using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NotchZero
{
    internal static class CardChrome
    {
        /// <summary>The faint glass card behind each section.</summary>
        public static Border Background(double width, double height) => new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(22),
            Background = Theme.W(0.05),
            BorderBrush = Theme.W(0.08),
            BorderThickness = new Thickness(1),
        };

        public static string Time(TimeSpan t)
        {
            int s = (int)Math.Max(0, Math.Floor(t.TotalSeconds));
            return $"{s / 60}:{s % 60:00}";
        }
    }

    // MARK: - Music

    /// <summary>
    /// Square music tile: artwork fills the card, a glass pill with the silver-ring orb and title on top,
    /// time, progress and glass controls at the bottom.
    /// </summary>
    internal sealed class MusicCard : Canvas
    {
        public const double Size = 168;

        private readonly MediaService _media;
        private readonly Image _art;
        private readonly NoteIcon _note;
        private readonly Border _shade;
        private readonly Canvas _playing;
        private readonly Canvas _empty;
        private readonly AlbumOrb _orb;
        private readonly TextBlock _title;
        private readonly TextBlock _artist;
        private readonly TextBlock _elapsed;
        private readonly TextBlock _remaining;
        private readonly ProgressView _progress;
        private readonly RoundButton _play;
        private readonly DispatcherTimer _tick;
        private bool _active;

        public MusicCard(MediaService media)
        {
            _media = media;
            Width = Size;
            Height = Size;
            Clip = new RectangleGeometry(new Rect(0, 0, Size, Size), 22, 22);

            Children.Add(new Border { Width = Size, Height = Size, Background = Theme.W(0.06) });
            _note = Theme.Place(this, new NoteIcon(30, Theme.W(0.3)), (Size - 30) / 2, (Size - 30) / 2);

            _art = new Image { Width = Size, Height = Size, Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(_art, BitmapScalingMode.HighQuality);
            Children.Add(_art);

            var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(115, 0, 0, 0), 0));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.35));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.45));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(184, 0, 0, 0), 1));
            shade.Freeze();
            _shade = new Border { Width = Size, Height = Size, Background = shade, IsHitTestVisible = false };
            Children.Add(_shade);

            // Playing layer
            _playing = new Canvas { Width = Size, Height = Size };
            var pill = new Border
            {
                Width = Size - 16,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Background = Theme.K(0.4),
                BorderBrush = Theme.W(0.14),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
            };
            var pillContent = new Canvas { Width = Size - 18, Height = 32 };
            _orb = Theme.Place(pillContent, new AlbumOrb(26), 3, 3);
            _title = Theme.Place(pillContent, Theme.Text("", 11, FontWeights.SemiBold), 35, 2);
            _title.Width = Size - 18 - 35 - 10;
            _artist = Theme.Place(pillContent, Theme.Text("", 9.5, FontWeights.Medium, 0.7), 35, 16);
            _artist.Width = _title.Width;
            pill.Child = pillContent;
            Theme.Place(_playing, pill, 8, 8);

            _elapsed = Theme.Place(_playing, Theme.Text("0:00", 9, FontWeights.SemiBold, 0.8, true), 12, 94);
            _remaining = Theme.Place(_playing, Theme.Text("-0:00", 9, FontWeights.SemiBold, 0.8, true), Size - 12 - 60, 94);
            _remaining.Width = 60;
            _remaining.TextAlignment = TextAlignment.Right;

            _progress = Theme.Place(_playing, new ProgressView(Size - 24), 12, 108);
            _progress.Seek += f => _media.Seek(f);

            const double centerY = 141;
            var prev = Theme.Place(_playing, new RoundButton(28, 10, Glyph.Previous), 27, centerY - 14);
            _play = Theme.Place(_playing, new RoundButton(34, 13, Glyph.Play), 27 + 28 + 12, centerY - 17);
            var next = Theme.Place(_playing, new RoundButton(28, 10, Glyph.Next), 27 + 28 + 12 + 34 + 12, centerY - 14);
            prev.Click += () => _media.Previous();
            _play.Click += () => _media.PlayPause();
            next.Click += () => _media.Next();
            Children.Add(_playing);

            // Nothing playing
            _empty = new Canvas { Width = Size, Height = Size };
            var nothing = Theme.Place(_empty, Theme.Text("Nothing Playing", 12, FontWeights.SemiBold), 0, 100);
            nothing.Width = Size;
            nothing.TextAlignment = TextAlignment.Center;
            var spotify = new PillButton("Open Spotify");
            spotify.Click += MediaService.OpenSpotify;
            Theme.Place(_empty, spotify, (Size - spotify.Width) / 2, 124);
            Children.Add(_empty);

            Children.Add(new Border
            {
                Width = Size,
                Height = Size,
                CornerRadius = new CornerRadius(22),
                BorderBrush = Theme.W(0.1),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
            });

            _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _tick.Tick += (s, e) => UpdateProgress();
            Update();
        }

        public void SetActive(bool active)
        {
            _active = active;
            Update();
        }

        public void Update()
        {
            bool has = _media.HasSession && (!string.IsNullOrEmpty(_media.Title) || _media.IsPlaying);
            var art = has ? _media.Artwork : null;
            _art.Source = art;
            _art.Visibility = art != null ? Visibility.Visible : Visibility.Collapsed;
            _note.Visibility = art == null ? Visibility.Visible : Visibility.Collapsed;
            Canvas.SetTop(_note, has ? (Size - 30) / 2 : (Size - 30) / 2 - 22);
            _shade.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            _playing.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            _empty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;

            _title.Text = string.IsNullOrEmpty(_media.Title) ? "Unknown Track" : _media.Title;
            _artist.Text = string.IsNullOrEmpty(_media.Artist) ? _media.SourceName : _media.Artist;
            _orb.Image = art;
            _orb.Spinning = _media.IsPlaying && _active;
            _play.Glyph = _media.IsPlaying ? Glyph.Pause : Glyph.Play;

            UpdateProgress();
            if (_active && _media.IsPlaying) _tick.Start();
            else _tick.Stop();
        }

        private void UpdateProgress()
        {
            var duration = _media.Duration;
            bool known = duration > TimeSpan.Zero;
            _progress.Visibility = known ? Visibility.Visible : Visibility.Hidden;
            _elapsed.Visibility = known ? Visibility.Visible : Visibility.Hidden;
            _remaining.Visibility = known ? Visibility.Visible : Visibility.Hidden;
            if (!known) return;
            var pos = _media.CurrentPosition();
            _elapsed.Text = CardChrome.Time(pos);
            _remaining.Text = "-" + CardChrome.Time(duration > pos ? duration - pos : TimeSpan.Zero);
            _progress.Fraction = pos.TotalSeconds / duration.TotalSeconds;
        }
    }

    // MARK: - Prayer

    /// <summary>Where things sit on the sky arc. 0 = sunrise (left horizon), 1 = sunset (right horizon).</summary>
    internal readonly struct SkyGeometry
    {
        public SkyGeometry(double x0, double x1, double horizon, double height)
        {
            X0 = x0;
            X1 = x1;
            Horizon = horizon;
            Height = height;
        }

        public double X0 { get; }
        public double X1 { get; }
        public double Horizon { get; }
        public double Height { get; }

        /// <summary>Below 0 or above 1 the body drops under the horizon, so it rises / sets out of view.</summary>
        public Point PointAt(double f)
        {
            const double sink = 320;
            if (f < 0) return new Point(X0, Horizon + -f * sink);
            if (f > 1) return new Point(X1, Horizon + (f - 1) * sink);
            return new Point(X0 + (X1 - X0) * f, Horizon - Math.Sin(f * Math.PI) * Height);
        }
    }

    /// <summary>The live sky: dotted arc, the part travelled today, sun by day, moon and stars by night.</summary>
    internal sealed class SkyView : FrameworkElement
    {
        public const double SkyHeight = 112;
        public const double Horizon = 100;

        private static readonly (double x, double y, double r, double o)[] Stars =
        {
            (0.05, 0.55, 0.8, 0.5), (0.18, 0.15, 1.0, 0.7), (0.32, 0.6, 0.7, 0.4), (0.47, 0.05, 0.9, 0.6),
            (0.58, 0.42, 0.7, 0.45), (0.71, 0.12, 1.1, 0.75), (0.84, 0.5, 0.8, 0.5), (0.95, 0.22, 0.9, 0.6),
        };

        private static readonly Pen DottedPen = CreateDottedPen();
        private readonly FrameLoop _loop;
        private double _shown = 0.5;
        private double _from, _to, _elapsed, _duration;
        private bool _ease;
        private bool _night;
        private double[] _dots = Array.Empty<double>();

        public SkyView(double width)
        {
            Width = width;
            Height = SkyHeight;
            IsHitTestVisible = false;
            _loop = new FrameLoop(Step);
        }

        private static Pen CreateDottedPen()
        {
            var pen = new Pen(Theme.W(0.16), 1)
            {
                DashStyle = new DashStyle(new double[] { 1.5, 4 }, 0),
                DashCap = PenLineCap.Round,
            };
            pen.Freeze();
            return pen;
        }

        private SkyGeometry Sky => new SkyGeometry(162, Width - 24, Horizon, 72);

        public double Shown => _shown;

        public void Configure(bool night, double[] dots)
        {
            if (night != _night || dots.Length != _dots.Length)
            {
                _night = night;
                _dots = dots;
                InvalidateVisual();
            }
            else
            {
                _dots = dots;
            }
        }

        /// <summary>Glide to a new position (the sky moving through the day).</summary>
        public void MoveTo(double target)
        {
            if (Math.Abs(target - _shown) < 0.0005 && !_loop.IsRunning) return;
            Animate(_shown, target, 0.6, false);
        }

        /// <summary>When the panel opens, the sun (or moon) rises from the horizon and travels to where it is now.</summary>
        public void SweepIn(double target)
        {
            Animate(_night ? 0 : -0.06, target, 1.6, true);
        }

        private void Animate(double from, double to, double seconds, bool ease)
        {
            _from = from;
            _to = to;
            _elapsed = 0;
            _duration = seconds;
            _ease = ease;
            _shown = from;
            InvalidateVisual();
            _loop.Start();
        }

        private bool Step(double dt)
        {
            _elapsed += dt;
            double t = Math.Clamp(_elapsed / _duration, 0, 1);
            double eased = _ease ? CubicBezier(0.25, 0.1, 0.25, 1, t) : t;
            _shown = _from + (_to - _from) * eased;
            InvalidateVisual();
            return t < 1 && IsVisible;
        }

        /// <summary>CSS-style cubic-bezier easing.</summary>
        private static double CubicBezier(double x1, double y1, double x2, double y2, double x)
        {
            double Bx(double t) => 3 * (1 - t) * (1 - t) * t * x1 + 3 * (1 - t) * t * t * x2 + t * t * t;
            double By(double t) => 3 * (1 - t) * (1 - t) * t * y1 + 3 * (1 - t) * t * t * y2 + t * t * t;
            double Dx(double t) => 3 * (1 - t) * (1 - t) * x1 + 6 * (1 - t) * t * (x2 - x1) + 3 * t * t * (1 - x2);
            double u = x;
            for (int i = 0; i < 8; i++)
            {
                double err = Bx(u) - x;
                if (Math.Abs(err) < 1e-5) break;
                double d = Dx(u);
                if (Math.Abs(d) < 1e-6) break;
                u = Math.Clamp(u - err / d, 0, 1);
            }
            return By(u);
        }

        private static Geometry Arc(SkyGeometry g, double a, double b)
        {
            int steps = Math.Max(2, (int)Math.Ceiling(64 * (b - a)));
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(g.PointAt(a), false, false);
                for (int i = 1; i <= steps; i++) ctx.LineTo(g.PointAt(a + (b - a) * i / steps), true, true);
            }
            geo.Freeze();
            return geo;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = Width;
            var g = Sky;

            if (_night)
            {
                foreach (var s in Stars)
                    dc.DrawEllipse(Theme.W(s.o), null, new Point(g.X0 + (g.X1 - g.X0) * s.x, 18 + (Horizon - 34) * s.y), s.r, s.r);
            }

            // Whole path, faint and dotted.
            dc.DrawGeometry(null, DottedPen, Arc(g, 0, 1));

            // The part already travelled today.
            double travelled = Math.Clamp(_shown, 0, 1);
            if (travelled > 0.002)
                dc.DrawGeometry(null, Theme.RoundPen(Theme.W(_night ? 0.3 : 0.55), 1.5), Arc(g, 0, travelled));

            // Dhuhr and Asr marked on the arc.
            if (!_night)
                foreach (var f in _dots)
                    if (f >= 0 && f <= 1) dc.DrawEllipse(Theme.W(0.6), null, g.PointAt(f), 2, 2);

            // Sun or moon, hidden below the horizon so it really sets.
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, Horizon)));
            var p = g.PointAt(_shown);
            if (_night)
            {
                dc.DrawEllipse(new RadialGradientBrush(Theme.WC(0.35), Theme.WC(0)), null, p, 15, 15);
                var moon = new CombinedGeometry(GeometryCombineMode.Exclude,
                                                new EllipseGeometry(p, 6.5, 6.5),
                                                new EllipseGeometry(new Point(p.X + 3.4, p.Y - 2.6), 5.6, 5.6));
                dc.DrawGeometry(Brushes.White, null, moon);
            }
            else
            {
                dc.DrawEllipse(new RadialGradientBrush(Theme.WC(0.4), Theme.WC(0)), null, p, 24, 24);
                dc.DrawEllipse(Theme.W(0.10), null, p, 17, 17);
                dc.DrawEllipse(Theme.W(0.18), null, p, 11, 11);
                dc.DrawEllipse(Brushes.White, null, p, 6.5, 6.5);
            }
            dc.Pop();

            // Horizon, with a soft glow where the sun touches it.
            dc.DrawRectangle(Theme.W(0.18), null, new Rect(0, Horizon - 0.5, w, 1));
            if (!_night)
            {
                double near = Math.Max(0, 1 - Math.Min(Math.Abs(_shown), Math.Abs(_shown - 1)) / 0.12);
                if (near > 0.01)
                {
                    var glow = new RadialGradientBrush(Theme.WC(0.3 * near), Theme.WC(0));
                    dc.DrawEllipse(glow, null, new Point(_shown < 0.5 ? g.X0 : g.X1, Horizon), 56, 12);
                }
            }
        }
    }

    /// <summary>Big prayer card: next prayer, a live sky (sun by day, moon by night) and today's five times.</summary>
    internal sealed class PrayerCard : Canvas
    {
        private readonly PrayerService _prayer;
        private readonly double _width;
        private readonly SkyView _sky;
        private readonly TextBlock _label;
        private readonly TextBlock _title;
        private readonly TextBlock _detail;
        private readonly TextBlock _zone;
        private readonly MascotView _mascot;
        private readonly Border[] _cells = new Border[5];
        private readonly TextBlock[] _names = new TextBlock[5];
        private readonly TextBlock[] _times = new TextBlock[5];

        public PrayerCard(PrayerService prayer, double width)
        {
            _prayer = prayer;
            _width = width;
            Width = width;
            Height = MusicCard.Size;
            Clip = new RectangleGeometry(new Rect(0, 0, width, Height), 22, 22);
            Children.Add(CardChrome.Background(width, Height));

            _sky = Theme.Place(this, new SkyView(width), 0, 0);

            var headline = new StackPanel { Width = 146, IsHitTestVisible = false };
            _label = Theme.Text("NEXT PRAYER", 8.5, FontWeights.Bold, 0.45);
            _title = Theme.Text("", 28, FontWeights.SemiBold);
            _title.Margin = new Thickness(0, -2, 0, -2);
            _detail = Theme.Text("", 11, FontWeights.Medium, 0.6, true);
            headline.Children.Add(_label);
            headline.Children.Add(_title);
            headline.Children.Add(_detail);
            Theme.Place(this, headline, 16, 13);

            _zone = Theme.Place(this, Theme.Text("", 9.5, FontWeights.Medium, 0.4), width - 16 - 200, 14);
            _zone.Width = 200;
            _zone.TextAlignment = TextAlignment.Right;

            _mascot = new MascotView(24);
            Children.Add(_mascot);
            PlaceMascot();

            var row = new Grid { Width = width - 20, Height = Height - SkyView.SkyHeight - 4 };
            for (int i = 0; i < 5; i++)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                _names[i] = Theme.Text(PrayerNames.All[i].ToString().ToUpperInvariant(), 8.5, FontWeights.Bold);
                _names[i].HorizontalAlignment = HorizontalAlignment.Center;
                _times[i] = Theme.Text("--:--", 14, FontWeights.SemiBold, 1, true);
                _times[i].HorizontalAlignment = HorizontalAlignment.Center;
                stack.Children.Add(_names[i]);
                stack.Children.Add(_times[i]);
                _cells[i] = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(0, 5, 0, 5),
                    Margin = new Thickness(2, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = stack,
                    IsHitTestVisible = false,
                };
                Grid.SetColumn(_cells[i], i);
                row.Children.Add(_cells[i]);
            }
            Theme.Place(this, row, 10, SkyView.SkyHeight);
        }

        public MascotView Mascot => _mascot;

        private void PlaceMascot()
        {
            double centerY = SkyView.Horizon - (_mascot.Mat ? 13 : 12);
            Theme.Move(_mascot, 136 - 12, centerY - _mascot.Height / 2);
        }

        public void SetMood(MascotMood mood)
        {
            _mascot.Mood = mood;
            _mascot.Mat = mood == MascotMood.Praying;
            PlaceMascot();
        }

        /// <summary>Where the sun (day) or moon (night) should be right now, and which it is.</summary>
        private (double fraction, bool night) SkyTarget()
        {
            var today = _prayer.Today;
            if (today == null) return (0.5, false);
            var now = _prayer.Now;
            var rise = today.Sunrise;
            var set = today.Maghrib;
            double dayLength = Math.Max((set - rise).TotalSeconds, 1);
            double day = (now - rise).TotalSeconds / dayLength;
            if (day >= -0.06 && day <= 1.06) return (day, false);

            var nightStart = day > 1 ? set : set.AddDays(-1);
            var nightEnd = day > 1 ? rise.AddDays(1) : rise;
            double night = (now - nightStart).TotalSeconds / Math.Max((nightEnd - nightStart).TotalSeconds, 1);
            return (Math.Clamp(night, 0, 1), true);
        }

        private double[] Dots(bool night)
        {
            var today = _prayer.Today;
            if (night || today == null) return Array.Empty<double>();
            double len = Math.Max((today.Maghrib - today.Sunrise).TotalSeconds, 1);
            return new[]
            {
                (today.Dhuhr - today.Sunrise).TotalSeconds / len,
                (today.Asr - today.Sunrise).TotalSeconds / len,
            };
        }

        public void SweepIn()
        {
            var target = SkyTarget();
            _sky.Configure(target.night, Dots(target.night));
            _sky.SweepIn(target.fraction);
        }

        public void Update()
        {
            var target = SkyTarget();
            _sky.Configure(target.night, Dots(target.night));
            if (IsVisible) _sky.MoveTo(target.fraction);

            // Headline
            var window = _prayer.CurrentWindow;
            var next = _prayer.NextEvent;
            if (window.kind == PrayerWindowKind.Now)
            {
                _label.Text = "NOW";
                _title.Text = window.ev.Name.ToString();
                _detail.Text = "Started " + PrayerApi.Clock(window.ev.Time);
            }
            else if (next.HasValue)
            {
                _label.Text = "NEXT PRAYER";
                _title.Text = next.Value.Name.ToString();
                _detail.Text = PrayerApi.Clock(next.Value.Time) + " · " + _prayer.CountdownText(next.Value);
            }
            else
            {
                _label.Text = "PRAYER TIMES";
                _title.Text = _prayer.IsLoading ? "Loading…" : "Unavailable";
                _detail.Text = _prayer.ZoneName;
            }
            _title.FontSize = _title.Text.Length > 8 ? 23 : 28;
            _zone.Text = _prayer.ZoneName + (_prayer.ZoneIsAutomatic ? "" : " · " + _prayer.Zone);

            // Times row
            var today = _prayer.Today;
            for (int i = 0; i < 5; i++)
            {
                var name = PrayerNames.All[i];
                if (today == null)
                {
                    _times[i].Text = "--:--";
                    _names[i].Foreground = _times[i].Foreground = Theme.W(0.4);
                    _cells[i].Background = Brushes.Transparent;
                    continue;
                }
                var time = today.Time(name);
                bool isNext = next.HasValue && next.Value.Name == name && Math.Abs((next.Value.Time - time).TotalSeconds) < 60;
                bool isPast = time <= _prayer.Now && !isNext;
                var brush = Theme.W(isNext ? 1 : (isPast ? 0.3 : 0.6));
                _times[i].Text = PrayerApi.Short(time);
                _names[i].Foreground = brush;
                _times[i].Foreground = brush;
                _cells[i].Background = isNext ? Theme.W(0.12) : Brushes.Transparent;
            }
        }
    }

    // MARK: - System

    /// <summary>CPU on a dial with a glowing needle; RAM and battery as glowing segment bars.</summary>
    internal sealed class SystemCard : Canvas
    {
        public const double CardWidth = 222;

        private readonly SystemService _system;
        private readonly TextBlock _memory;
        private readonly DialGauge _dial;
        private readonly SegmentMeter _ram;
        private readonly SegmentMeter _battery;

        public SystemCard(SystemService system)
        {
            _system = system;
            Width = CardWidth;
            Height = MusicCard.Size;
            Children.Add(CardChrome.Background(CardWidth, Height));

            Theme.Place(this, Theme.Text("System", 12, FontWeights.SemiBold), 14, 12);
            _memory = Theme.Place(this, Theme.Text("", 9.5, FontWeights.Medium, 0.45, true), CardWidth - 14 - 130, 15);
            _memory.Width = 130;
            _memory.TextAlignment = TextAlignment.Right;

            _dial = Theme.Place(this, new DialGauge("CPU"), 12, 52);
            _ram = Theme.Place(this, new SegmentMeter("RAM"), 116, 53);
            _battery = Theme.Place(this, new SegmentMeter("Battery"), 116, 53 + 38 + 14);
            Update();
        }

        public void Update()
        {
            _dial.Value = _system.Cpu;
            _ram.Set("RAM", _system.MemoryFraction, Percent(_system.MemoryFraction), false);
            if (_system.HasBattery)
                _battery.Set("Battery", _system.Battery, Percent(_system.Battery), _system.IsCharging);
            else
                _battery.Set("Power", 1, "AC", false);
            _memory.Text = string.Format(PrayerApi.En, "{0:0.0} / {1:0} GB", _system.MemoryUsedGB, _system.MemoryTotalGB);
        }

        public static string Percent(double v) => $"{(int)Math.Round(Math.Clamp(v, 0, 1) * 100)}%";
    }

    /// <summary>Speedometer-style dial: tick marks over 270°, a glowing needle, the value underneath.</summary>
    internal sealed class DialGauge : FrameworkElement
    {
        public static readonly DependencyProperty AnimatedValueProperty = DependencyProperty.Register(
            nameof(AnimatedValue), typeof(double), typeof(DialGauge),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private const double StartAngle = 135;
        private const double Sweep = 270;
        private static readonly Pen TickPen = Theme.RoundPen(Theme.W(0.35), 1);
        private static readonly Pen RingPen = Theme.RoundPen(Theme.W(0.22), 1.5);
        private static readonly Pen NeedleGlowWide = Theme.RoundPen(Theme.W(0.08), 9);
        private static readonly Pen NeedleGlow = Theme.RoundPen(Theme.W(0.2), 5);
        private static readonly Pen NeedlePen = Theme.RoundPen(Brushes.White, 2.6);
        private readonly string _label;
        private double _target = -1;

        public DialGauge(string label)
        {
            _label = label;
            Width = 92;
            Height = 92;
            IsHitTestVisible = false;
        }

        public double AnimatedValue
        {
            get => (double)GetValue(AnimatedValueProperty);
            set => SetValue(AnimatedValueProperty, value);
        }

        public double Value
        {
            set
            {
                value = Math.Clamp(value, 0, 1);
                if (Math.Abs(value - _target) < 0.004) return;
                _target = value;
                var anim = new DoubleAnimation(value, TimeSpan.FromMilliseconds(650))
                {
                    EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut },
                };
                BeginAnimation(AnimatedValueProperty, anim);
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double r = Math.Min(Width, Height) / 2;
            var c = new Point(Width / 2, Height / 2);
            double v = Math.Clamp(AnimatedValue, 0, 1);

            dc.DrawEllipse(new RadialGradientBrush(Theme.WC(0.10), Theme.WC(0)), null, c, r, r);
            dc.DrawEllipse(null, RingPen, c, r - 0.75, r - 0.75);

            const int count = 24;
            for (int i = 0; i <= count; i++)
            {
                double a = (StartAngle + Sweep * i / count) * Math.PI / 180;
                bool major = i % 6 == 0;
                double outer = r - 5;
                double inner = r - (major ? 11 : 8);
                dc.DrawLine(TickPen,
                            new Point(c.X + Math.Cos(a) * inner, c.Y + Math.Sin(a) * inner),
                            new Point(c.X + Math.Cos(a) * outer, c.Y + Math.Sin(a) * outer));
            }

            double na = (StartAngle + Sweep * v) * Math.PI / 180;
            var tip = new Point(c.X + Math.Cos(na) * r * 0.62, c.Y + Math.Sin(na) * r * 0.62);
            dc.DrawLine(NeedleGlowWide, c, tip);
            dc.DrawLine(NeedleGlow, c, tip);
            dc.DrawLine(NeedlePen, c, tip);
            dc.DrawEllipse(Brushes.White, null, c, 2.5, 2.5);

            double dpi = Theme.Dpi(this);
            var value = Theme.Formatted(SystemCard.Percent(Math.Max(_target, 0)), 15, FontWeights.SemiBold, Brushes.White, dpi);
            var label = Theme.Formatted(_label, 8.5, FontWeights.Bold, Theme.W(0.5), dpi);
            double top = c.Y + r * 0.5 - (value.Height + label.Height - 2) / 2;
            dc.DrawText(value, new Point(c.X - value.Width / 2, top));
            dc.DrawText(label, new Point(c.X - label.Width / 2, top + value.Height - 2));
        }
    }

    /// <summary>Row of glowing bars, lit up to the value.</summary>
    internal sealed class SegmentMeter : FrameworkElement
    {
        public static readonly DependencyProperty AnimatedValueProperty = DependencyProperty.Register(
            nameof(AnimatedValue), typeof(double), typeof(SegmentMeter),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private const int Segments = 14;
        private string _label;
        private string _detail = "";
        private bool _charging;
        private double _target = -1;

        public SegmentMeter(string label)
        {
            _label = label;
            Width = 88;
            Height = 40;
            IsHitTestVisible = false;
        }

        public double AnimatedValue
        {
            get => (double)GetValue(AnimatedValueProperty);
            set => SetValue(AnimatedValueProperty, value);
        }

        public void Set(string label, double value, string detail, bool charging)
        {
            value = Math.Clamp(value, 0, 1);
            bool textChanged = label != _label || detail != _detail || charging != _charging;
            _label = label;
            _detail = detail;
            _charging = charging;
            if (Math.Abs(value - _target) > 0.003)
            {
                _target = value;
                BeginAnimation(AnimatedValueProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(400))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                });
            }
            if (textChanged) InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double dpi = Theme.Dpi(this);
            var label = Theme.Formatted(_label, 9.5, FontWeights.Medium, Theme.W(0.55), dpi);
            var detail = Theme.Formatted(_detail, 11, FontWeights.SemiBold, Brushes.White, dpi);
            dc.DrawText(label, new Point(0, 2));
            double dx = Width - detail.Width;
            dc.DrawText(detail, new Point(dx, 0));
            if (_charging) Icons.Bolt(dc, new Point(dx - 7, detail.Height / 2 + 0.5), 9, Brushes.White);

            int lit = (int)Math.Round(Math.Clamp(AnimatedValue, 0, 1) * Segments);
            const double barW = 3, gap = 3.5, barH = 18, top = 21;
            for (int i = 0; i < Segments; i++)
            {
                double x = i * (barW + gap);
                if (i < lit)
                {
                    dc.DrawRoundedRectangle(Theme.W(0.16), null, new Rect(x - 1.5, top - 1.5, barW + 3, barH + 3), 3, 3);
                    dc.DrawRoundedRectangle(Theme.W(0.95), null, new Rect(x, top, barW, barH), 1.5, 1.5);
                }
                else
                {
                    dc.DrawRoundedRectangle(Theme.W(0.15), null, new Rect(x, top, barW, barH), 1.5, 1.5);
                }
            }
        }
    }
}
