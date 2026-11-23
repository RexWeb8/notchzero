using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NotchZero
{
    /// <summary>Four white bars that bounce smoothly while music plays and settle when paused.</summary>
    internal sealed class EqualizerBars : FrameworkElement
    {
        private const int Count = 4;
        private readonly double[] _heights = { 0.2, 0.2, 0.2, 0.2 };
        private readonly double[] _velocities = new double[Count];
        private readonly double[] _targets = { 0.2, 0.2, 0.2, 0.2 };
        private readonly double[] _retargetIn = { 0, 0.05, 0.1, 0.15 };
        private readonly Random _random = new Random();
        private readonly FrameLoop _loop;
        private bool _playing;

        public EqualizerBars(double width, double height)
        {
            Width = width;
            Height = height;
            IsHitTestVisible = false;
            _loop = new FrameLoop(Step);
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible) _loop.Start();
                else _loop.Stop();
            };
        }

        public bool IsPlaying
        {
            get => _playing;
            set
            {
                if (_playing == value) return;
                _playing = value;
                if (IsVisible) _loop.Start();
            }
        }

        private double Rand(double lo, double hi) => lo + _random.NextDouble() * (hi - lo);

        private bool Step(double dt)
        {
            bool settled = true;
            for (int i = 0; i < Count; i++)
            {
                if (_playing)
                {
                    _retargetIn[i] -= dt;
                    if (_retargetIn[i] <= 0)
                    {
                        // New level for this bar; outer bars a little calmer than the middle ones.
                        bool middle = i == 1 || i == 2;
                        _targets[i] = middle ? Rand(0.35, 1.0) : Rand(0.25, 0.8);
                        _retargetIn[i] = Rand(0.11, 0.22);
                    }
                }
                else
                {
                    _targets[i] = 0.18;
                }

                // Critically damped spring towards the target: smooth, no jitter.
                const double stiffness = 170.0;
                double damping = 2 * Math.Sqrt(stiffness);
                double accel = stiffness * (_targets[i] - _heights[i]) - damping * _velocities[i];
                _velocities[i] += accel * dt;
                _heights[i] += _velocities[i] * dt;
                if (Math.Abs(_heights[i] - _targets[i]) > 0.002 || Math.Abs(_velocities[i]) > 0.002) settled = false;
            }
            InvalidateVisual();
            if (!_playing && settled) return false;
            return IsVisible;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = Width, h = Height;
            double barWidth = Math.Max(2, w / (Count * 2 - 1));
            double gap = (w - barWidth * Count) / (Count - 1);
            for (int i = 0; i < Count; i++)
            {
                double bh = Math.Max(barWidth, h * Math.Clamp(_heights[i], 0, 1));
                double x = i * (barWidth + gap);
                double y = (h - bh) / 2;
                dc.DrawRoundedRectangle(Brushes.White, null, new Rect(x, y, barWidth, bh), barWidth / 2, barWidth / 2);
            }
        }
    }

    internal enum Glyph { Play, Pause, Previous, Next }

    internal static class Icons
    {
        public static void Draw(DrawingContext dc, Glyph glyph, Point c, double s, Brush b)
        {
            var pen = new Pen(b, s * 0.14) { LineJoin = PenLineJoin.Round };
            switch (glyph)
            {
                case Glyph.Play:
                    dc.DrawGeometry(b, pen, Triangle(new Point(c.X - s * 0.34, c.Y - s * 0.48),
                                                     new Point(c.X + s * 0.5, c.Y),
                                                     new Point(c.X - s * 0.34, c.Y + s * 0.48)));
                    break;
                case Glyph.Pause:
                {
                    double w = s * 0.27, h = s * 0.92;
                    dc.DrawRoundedRectangle(b, null, new Rect(c.X - w - s * 0.09, c.Y - h / 2, w, h), w * 0.3, w * 0.3);
                    dc.DrawRoundedRectangle(b, null, new Rect(c.X + s * 0.09, c.Y - h / 2, w, h), w * 0.3, w * 0.3);
                    break;
                }
                case Glyph.Next:
                {
                    double h = s * 0.42;
                    dc.DrawGeometry(b, pen, Triangle(new Point(c.X - s * 0.52, c.Y - h), new Point(c.X, c.Y), new Point(c.X - s * 0.52, c.Y + h)));
                    dc.DrawGeometry(b, pen, Triangle(new Point(c.X, c.Y - h), new Point(c.X + s * 0.52, c.Y), new Point(c.X, c.Y + h)));
                    break;
                }
                case Glyph.Previous:
                {
                    double h = s * 0.42;
                    dc.DrawGeometry(b, pen, Triangle(new Point(c.X + s * 0.52, c.Y - h), new Point(c.X, c.Y), new Point(c.X + s * 0.52, c.Y + h)));
                    dc.DrawGeometry(b, pen, Triangle(new Point(c.X, c.Y - h), new Point(c.X - s * 0.52, c.Y), new Point(c.X, c.Y + h)));
                    break;
                }
            }
        }

        private static Geometry Triangle(Point a, Point b, Point c)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(a, true, true);
                ctx.LineTo(b, true, true);
                ctx.LineTo(c, true, true);
            }
            g.Freeze();
            return g;
        }

        /// <summary>A small music note, centred on c.</summary>
        public static void Note(DrawingContext dc, Point c, double s, Brush b)
        {
            dc.DrawEllipse(b, null, new Point(c.X - s * 0.14, c.Y + s * 0.3), s * 0.19, s * 0.15);
            dc.DrawRectangle(b, null, new Rect(c.X + s * 0.0, c.Y - s * 0.44, s * 0.08, s * 0.74));
            var flag = new StreamGeometry();
            using (var ctx = flag.Open())
            {
                ctx.BeginFigure(new Point(c.X + s * 0.04, c.Y - s * 0.44), true, true);
                ctx.QuadraticBezierTo(new Point(c.X + s * 0.38, c.Y - s * 0.32), new Point(c.X + s * 0.36, c.Y - s * 0.02), true, true);
                ctx.QuadraticBezierTo(new Point(c.X + s * 0.3, c.Y - s * 0.22), new Point(c.X + s * 0.04, c.Y - s * 0.24), true, true);
            }
            flag.Freeze();
            dc.DrawGeometry(b, null, flag);
        }

        /// <summary>A lightning bolt (charging), centred on c.</summary>
        public static void Bolt(DrawingContext dc, Point c, double s, Brush b)
        {
            dc.DrawGeometry(b, null, Theme.Polygon(
                c.X + s * 0.12, c.Y - s * 0.5,
                c.X - s * 0.32, c.Y + s * 0.08,
                c.X - s * 0.02, c.Y + s * 0.08,
                c.X - s * 0.12, c.Y + s * 0.5,
                c.X + s * 0.32, c.Y - s * 0.08,
                c.X + s * 0.02, c.Y - s * 0.08));
        }
    }

    /// <summary>Base for small custom buttons: hover, press, click.</summary>
    internal abstract class PressableElement : FrameworkElement
    {
        protected bool Hover;
        protected bool Down;

        public event Action Click;

        protected PressableElement()
        {
            Cursor = Cursors.Hand;
            Focusable = false;
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            Hover = true;
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            Hover = false;
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Down = true;
            CaptureMouse();
            e.Handled = true;
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            var p = e.GetPosition(this);
            bool inside = p.X >= 0 && p.Y >= 0 && p.X <= ActualWidth && p.Y <= ActualHeight;
            bool fire = Down && inside;
            Down = false;
            ReleaseMouseCapture();
            e.Handled = true;
            InvalidateVisual();
            if (fire) Click?.Invoke();
        }
    }

    /// <summary>Round "glass" control, like the music tile's play/pause.</summary>
    internal sealed class RoundButton : PressableElement
    {
        private readonly double _iconSize;
        private Glyph _glyph;

        public RoundButton(double diameter, double iconSize, Glyph glyph)
        {
            Width = diameter;
            Height = diameter;
            _iconSize = iconSize;
            _glyph = glyph;
        }

        public Glyph Glyph
        {
            get => _glyph;
            set
            {
                if (_glyph == value) return;
                _glyph = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double d = Width;
            var c = new Point(d / 2, d / 2);
            double scale = Down ? 0.94 : (Hover ? 1.06 : 1);
            dc.PushTransform(new ScaleTransform(scale, scale, c.X, c.Y));
            dc.DrawEllipse(Theme.W(Hover ? 0.28 : 0.18), new Pen(Theme.W(0.18), 1), c, d / 2 - 0.5, d / 2 - 0.5);
            Icons.Draw(dc, _glyph, c, _iconSize, Brushes.White);
            dc.Pop();
        }
    }

    /// <summary>Small round icon button using the Windows icon font (settings, share).</summary>
    internal sealed class IconButton : PressableElement
    {
        private readonly string _glyph;
        private readonly double _size;

        public IconButton(string glyph, double size = 13, double diameter = 28)
        {
            _glyph = glyph;
            _size = size;
            Width = diameter;
            Height = diameter;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var c = new Point(Width / 2, Height / 2);
            dc.DrawEllipse(Hover ? Theme.W(Down ? 0.2 : 0.14) : Brushes.Transparent, null, c, Width / 2, Height / 2);
            var text = Theme.Formatted(_glyph, _size, FontWeights.Normal, Theme.W(Hover ? 1 : 0.8), Theme.Dpi(this), Theme.Icons);
            var geometry = text.BuildGeometry(new Point(0, 0));
            var bounds = geometry.Bounds;
            if (bounds.IsEmpty)
            {
                dc.DrawText(text, new Point(c.X - text.Width / 2, c.Y - text.Height / 2));
                return;
            }
            dc.PushTransform(new TranslateTransform(c.X - (bounds.X + bounds.Width / 2), c.Y - (bounds.Y + bounds.Height / 2)));
            dc.DrawGeometry(Theme.W(Hover ? 1 : 0.8), null, geometry);
            dc.Pop();
        }
    }

    /// <summary>White capsule button with dark text.</summary>
    internal sealed class PillButton : PressableElement
    {
        private readonly string _title;

        public PillButton(string title)
        {
            _title = title;
            var probe = Theme.Formatted(title, 11, FontWeights.SemiBold, Brushes.Black, 1.0);
            Width = Math.Ceiling(probe.WidthIncludingTrailingWhitespace) + 24;
            Height = 24;
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRoundedRectangle(Theme.W(Down ? 0.75 : (Hover ? 0.85 : 0.95)), null, new Rect(0, 0, Width, Height), Height / 2, Height / 2);
            var text = Theme.Formatted(_title, 11, FontWeights.SemiBold, Brushes.Black, Theme.Dpi(this));
            dc.DrawText(text, new Point((Width - text.Width) / 2, (Height - text.Height) / 2));
        }
    }

    /// <summary>Thin progress line; click anywhere on it to seek.</summary>
    internal sealed class ProgressView : FrameworkElement
    {
        private double _fraction;

        public event Action<double> Seek;

        public ProgressView(double width)
        {
            Width = width;
            Height = 10;
            Cursor = Cursors.Hand;
        }

        public double Fraction
        {
            get => _fraction;
            set
            {
                value = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);
                if (Math.Abs(value - _fraction) < 0.0005) return;
                _fraction = value;
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            var p = e.GetPosition(this);
            e.Handled = true;
            Seek?.Invoke(p.X / Math.Max(1, Width));
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Width, Height));
            double y = Height / 2 - 1.5;
            dc.DrawRoundedRectangle(Theme.W(0.25), null, new Rect(0, y, Width, 3), 1.5, 1.5);
            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(0, y, Math.Max(3, Width * _fraction), 3), 1.5, 1.5);
        }
    }

    /// <summary>Round album art with a slowly turning silver chrome ring.</summary>
    internal sealed class AlbumOrb : FrameworkElement
    {
        private static readonly GradientStopCollection SilverStops = CreateStops();
        private readonly FrameLoop _loop;
        private ImageBrush _art;
        private bool _spinning;
        private double _angle;

        public AlbumOrb(double size)
        {
            Width = size;
            Height = size;
            IsHitTestVisible = false;
            _loop = new FrameLoop(dt =>
            {
                _angle = (_angle + dt * 45) % 360;
                InvalidateVisual();
                return _spinning && IsVisible;
            });
            IsVisibleChanged += (s, e) => UpdateLoop();
        }

        private static GradientStopCollection CreateStops()
        {
            var stops = new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(255, 255, 255), 0.0),
                new GradientStop(Color.FromRgb(138, 141, 147), 0.22),
                new GradientStop(Color.FromRgb(242, 243, 245), 0.48),
                new GradientStop(Color.FromRgb(94, 97, 104), 0.74),
                new GradientStop(Color.FromRgb(255, 255, 255), 1.0),
            };
            stops.Freeze();
            return stops;
        }

        public ImageSource Image
        {
            set
            {
                if (value == null) _art = null;
                else if (_art == null || !ReferenceEquals(_art.ImageSource, value))
                {
                    _art = new ImageBrush(value) { Stretch = Stretch.UniformToFill };
                    _art.Freeze();
                }
                InvalidateVisual();
            }
        }

        public bool Spinning
        {
            set
            {
                _spinning = value;
                UpdateLoop();
            }
        }

        private void UpdateLoop()
        {
            if (_spinning && IsVisible) _loop.Start();
            else _loop.Stop();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double s = Width;
            var c = new Point(s / 2, s / 2);
            const double ring = 2.2;
            double inner = s / 2 - ring - 0.4;
            dc.DrawEllipse((Brush)_art ?? Theme.W(0.18), null, c, inner, inner);

            var chrome = new LinearGradientBrush(SilverStops, new Point(0, 0), new Point(1, 1))
            {
                RelativeTransform = new RotateTransform(_angle, 0.5, 0.5),
            };
            dc.DrawEllipse(null, new Pen(chrome, ring), c, s / 2 - ring / 2, s / 2 - ring / 2);
            dc.DrawEllipse(null, new Pen(Theme.W(0.35), 0.6), c, s / 2 - 0.3, s / 2 - 0.3);
        }
    }

    /// <summary>Rounded-square artwork, or a music note when there's none.</summary>
    internal sealed class ArtTile : FrameworkElement
    {
        private readonly double _radius;
        private ImageBrush _art;

        public ArtTile(double size, double radius)
        {
            Width = size;
            Height = size;
            _radius = radius;
            IsHitTestVisible = false;
        }

        public ImageSource Image
        {
            set
            {
                if (value == null) _art = null;
                else if (_art == null || !ReferenceEquals(_art.ImageSource, value))
                {
                    _art = new ImageBrush(value) { Stretch = Stretch.UniformToFill };
                    _art.Freeze();
                }
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            var rect = new Rect(0, 0, Width, Height);
            if (_art != null)
            {
                dc.DrawRoundedRectangle(_art, null, rect, _radius, _radius);
            }
            else
            {
                dc.DrawRoundedRectangle(Theme.W(0.1), null, rect, _radius, _radius);
                Icons.Note(dc, new Point(Width / 2, Height / 2), Width * 0.42, Theme.W(0.6));
            }
        }
    }

    /// <summary>Just a music note.</summary>
    internal sealed class NoteIcon : FrameworkElement
    {
        private readonly Brush _brush;

        public NoteIcon(double size, Brush brush)
        {
            Width = size;
            Height = size;
            _brush = brush;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc) =>
            Icons.Note(dc, new Point(Width / 2, Height / 2), Width, _brush);
    }
}
