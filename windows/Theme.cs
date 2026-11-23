using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NotchZero
{
    /// <summary>One colour (white), a few fonts, and small layout helpers.</summary>
    internal static class Theme
    {
        public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Display, Segoe UI");
        public static readonly FontFamily TextFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public const string GlyphSettings = "";
        public const string GlyphShare = "";

        private static readonly Dictionary<int, SolidColorBrush> WhiteCache = new Dictionary<int, SolidColorBrush>();
        private static readonly Dictionary<int, SolidColorBrush> BlackCache = new Dictionary<int, SolidColorBrush>();

        public static byte Alpha(double opacity) => (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);

        public static Color WC(double opacity) => Color.FromArgb(Alpha(opacity), 255, 255, 255);

        public static Color Grey(double white, double opacity = 1)
        {
            byte v = (byte)Math.Round(Math.Clamp(white, 0, 1) * 255);
            return Color.FromArgb(Alpha(opacity), v, v, v);
        }

        /// <summary>White at the given opacity (cached, frozen).</summary>
        public static SolidColorBrush W(double opacity)
        {
            int a = Alpha(opacity);
            if (!WhiteCache.TryGetValue(a, out var brush))
            {
                brush = new SolidColorBrush(Color.FromArgb((byte)a, 255, 255, 255));
                brush.Freeze();
                WhiteCache[a] = brush;
            }
            return brush;
        }

        /// <summary>Black at the given opacity (cached, frozen).</summary>
        public static SolidColorBrush K(double opacity)
        {
            int a = Alpha(opacity);
            if (!BlackCache.TryGetValue(a, out var brush))
            {
                brush = new SolidColorBrush(Color.FromArgb((byte)a, 0, 0, 0));
                brush.Freeze();
                BlackCache[a] = brush;
            }
            return brush;
        }

        public static SolidColorBrush Solid(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static Pen RoundPen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            pen.Freeze();
            return pen;
        }

        public static TextBlock Text(string text, double size, FontWeight weight, double opacity = 1,
                                     bool tabular = false, FontFamily family = null)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = weight,
                Foreground = W(opacity),
                FontFamily = family ?? Font,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                IsHitTestVisible = false,
            };
            if (tabular) Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular);
            return tb;
        }

        public static FormattedText Formatted(string text, double size, FontWeight weight, Brush brush,
                                              double pixelsPerDip, FontFamily family = null)
        {
            return new FormattedText(text ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                     new Typeface(family ?? Font, FontStyles.Normal, weight, FontStretches.Normal),
                                     size, brush, pixelsPerDip);
        }

        public static double Dpi(Visual v)
        {
            try { return VisualTreeHelper.GetDpi(v).PixelsPerDip; }
            catch { return 1.0; }
        }

        /// <summary>Adds an element to a canvas at (x, y).</summary>
        public static T Place<T>(Panel parent, T element, double x, double y) where T : UIElement
        {
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
            parent.Children.Add(element);
            return element;
        }

        public static void Move(UIElement element, double x, double y)
        {
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
        }

        public static Geometry Polygon(params double[] xy)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(xy[0], xy[1]), true, true);
                for (int i = 2; i + 1 < xy.Length; i += 2) c.LineTo(new Point(xy[i], xy[i + 1]), true, true);
            }
            g.Freeze();
            return g;
        }

        /// <summary>
        /// Fades a view in or out. Hidden views are collapsed once the fade ends so they cost nothing.
        /// IsHitTestVisible doubles as "should be shown".
        /// </summary>
        public static void Fade(UIElement el, bool show, double ms = 180, double delayMs = 0)
        {
            if (show == el.IsHitTestVisible) return; // already shown, or already on its way out
            el.IsHitTestVisible = show;
            var anim = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(ms))
            {
                BeginTime = TimeSpan.FromMilliseconds(show ? delayMs : 0),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            if (show)
            {
                el.Visibility = Visibility.Visible;
            }
            else
            {
                anim.Completed += (s, e) =>
                {
                    if (!el.IsHitTestVisible) el.Visibility = Visibility.Collapsed;
                };
            }
            el.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }

    /// <summary>A spring like SwiftUI's .spring(response:dampingFraction:).</summary>
    internal sealed class Spring
    {
        public double Value;
        public double Velocity;
        public double Target;
        private double _stiffness;
        private double _damping;
        private readonly double _epsilon;

        public Spring(double value, double response = 0.4, double dampingFraction = 0.85, double epsilon = 0.05)
        {
            Value = Target = value;
            _epsilon = epsilon;
            Configure(response, dampingFraction);
        }

        public void Configure(double response, double dampingFraction)
        {
            double w = 2 * Math.PI / Math.Max(response, 0.05);
            _stiffness = w * w;
            _damping = 2 * dampingFraction * w;
        }

        /// <summary>Advances the spring; returns false once it has settled.</summary>
        public bool Step(double dt)
        {
            if (Value == Target && Velocity == 0) return false;
            int n = Math.Max(1, (int)Math.Ceiling(dt / (1.0 / 240)));
            double h = dt / n;
            for (int i = 0; i < n; i++)
            {
                double a = _stiffness * (Target - Value) - _damping * Velocity;
                Velocity += a * h;
                Value += Velocity * h;
            }
            if (Math.Abs(Target - Value) < _epsilon && Math.Abs(Velocity) < _epsilon * 4)
            {
                Value = Target;
                Velocity = 0;
                return false;
            }
            return true;
        }
    }

    /// <summary>Runs a step function on every rendered frame (vsync), only while needed.</summary>
    internal sealed class FrameLoop
    {
        private readonly Func<double, bool> _step;
        private TimeSpan _last;
        private bool _running;

        public FrameLoop(Func<double, bool> step) => _step = step;

        public bool IsRunning => _running;

        public void Start()
        {
            if (_running) return;
            _running = true;
            _last = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        private void OnRendering(object sender, EventArgs e)
        {
            var t = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.Zero;
            if (t == _last && t != TimeSpan.Zero) return; // Rendering can fire twice per frame
            double dt = _last == TimeSpan.Zero ? 1.0 / 60 : (t - _last).TotalSeconds;
            _last = t;
            dt = Math.Clamp(dt, 0, 0.05);
            bool keep;
            try
            {
                keep = _step(dt);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Frame loop");
                keep = false;
            }
            if (!keep) Stop();
        }
    }
}
