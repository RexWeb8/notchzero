using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace NotchZero
{
    internal enum MascotMood
    {
        Awake,    // blinking now and then
        Alert,    // a prayer is coming up
        Sleeping, // you're away, or it's late
        Praying,  // it's prayer time: sits on the sejadah
        Vibing,   // music is playing: dancing
    }

    /// <summary>Which mascot lives in the island.</summary>
    internal enum MascotKind { Cat, Rabbit, Dragon, Owl, MouseDeer }

    internal static class MascotInfo
    {
        public static readonly MascotKind[] All =
            { MascotKind.Cat, MascotKind.Rabbit, MascotKind.Dragon, MascotKind.Owl, MascotKind.MouseDeer };

        public static string Name(MascotKind k) => k switch
        {
            MascotKind.Rabbit => "Gebu",
            MascotKind.Dragon => "Bara",
            MascotKind.Owl => "Pungguk",
            MascotKind.MouseDeer => "Cil",
            _ => "Kapas",
        };

        public static string Species(MascotKind k) => k switch
        {
            MascotKind.Rabbit => "Rabbit",
            MascotKind.Dragon => "Dragon",
            MascotKind.Owl => "Owl",
            MascotKind.MouseDeer => "Mouse Deer",
            _ => "Cat",
        };

        public static string MenuTitle(MascotKind k) => $"{Name(k)} the {Species(k)}";
    }

    /// <summary>
    /// The mascot, drawn in a 100-unit design space (108 tall with the sejadah), with its little
    /// behaviours: blinks, dances to music, sleeps, prays.
    /// </summary>
    internal sealed class MascotView : FrameworkElement
    {
        private static readonly Brush White = Brushes.White;
        private static readonly Brush Ink = Brushes.Black;
        private static readonly Brush Soft = Theme.Solid(Theme.Grey(0.84));
        private static readonly Brush MatBrush = Theme.Solid(Theme.Grey(0.36));
        private static readonly Brush DiscBrush = Theme.Solid(Theme.Grey(0.82));
        private static readonly Brush HornBrush = Theme.Solid(Theme.Grey(0.88));
        private static readonly Brush BeakBrush = Theme.Solid(Theme.Grey(0.2));
        private static readonly Brush ChestBrush = Theme.Solid(Theme.Grey(0.75));
        private static readonly Brush ZBrush = Theme.W(0.7);

        // Static shapes, in design units.
        private static readonly Geometry CatLeftEar = Theme.Polygon(16, 54, 21, 12, 47, 35);
        private static readonly Geometry CatRightEar = Theme.Polygon(53, 35, 79, 12, 84, 54);
        private static readonly Geometry CatLeftInner = Theme.Polygon(22, 44, 25, 22, 39, 36);
        private static readonly Geometry CatRightInner = Theme.Polygon(61, 36, 75, 22, 78, 44);
        private static readonly Geometry CatNose = Theme.Polygon(46.5, 71, 53.5, 71, 50, 75);
        private static readonly Geometry RabbitNose = Theme.Polygon(47, 74, 53, 74, 50, 77.5);
        private static readonly Geometry DragonLeftFin = Theme.Polygon(17, 46, 3, 40, 10, 52, 1, 58, 10, 63, 4, 72, 17, 70);
        private static readonly Geometry DragonRightFin = Theme.Polygon(83, 46, 97, 40, 90, 52, 99, 58, 90, 63, 96, 72, 83, 70);
        private static readonly Geometry DragonCrest = Theme.Polygon(44, 33, 50, 20, 56, 33);
        private static readonly Geometry DragonLeftHorn = Horn(new Point(27, 38), new Point(19, 24), new Point(23, 6), new Point(33, 18), new Point(41, 33));
        private static readonly Geometry DragonRightHorn = Horn(new Point(73, 38), new Point(81, 24), new Point(77, 6), new Point(67, 18), new Point(59, 33));
        private static readonly Geometry OwlLeftTuft = Theme.Polygon(16, 44, 9, 8, 24, 22, 27, 12, 40, 30);
        private static readonly Geometry OwlRightTuft = Theme.Polygon(84, 44, 91, 8, 76, 22, 73, 12, 60, 30);
        private static readonly Geometry OwlBeak = Theme.Polygon(45.5, 68, 54.5, 68, 50, 77);

        private readonly double _size;
        private readonly DispatcherTimer _blink;
        private readonly FrameLoop _loop;
        private readonly Random _random = new Random();
        private MascotKind _kind = MascotKind.Cat;
        private MascotMood _mood = MascotMood.Awake;
        private bool _mat;
        private double _eyeOpen = 1;

        public MascotView(double size, bool mat = false)
        {
            _size = size;
            _mat = mat;
            Width = size;
            Height = size * (mat ? 1.08 : 1);
            IsHitTestVisible = false;

            _loop = new FrameLoop(_ =>
            {
                InvalidateVisual();
                return _mood == MascotMood.Vibing && IsVisible;
            });
            _blink = new DispatcherTimer();
            _blink.Tick += OnBlink;
            IsVisibleChanged += (s, e) => UpdateAnimation();
        }

        public MascotKind Kind
        {
            get => _kind;
            set
            {
                if (_kind == value) return;
                _kind = value;
                InvalidateVisual();
            }
        }

        public MascotMood Mood
        {
            get => _mood;
            set
            {
                if (_mood == value) return;
                _mood = value;
                _eyeOpen = 1;
                UpdateAnimation();
                InvalidateVisual();
            }
        }

        public bool Mat
        {
            get => _mat;
            set
            {
                if (_mat == value) return;
                _mat = value;
                Height = _size * (value ? 1.08 : 1);
                InvalidateVisual();
            }
        }

        private void UpdateAnimation()
        {
            if (IsVisible && _mood == MascotMood.Vibing) _loop.Start();
            else _loop.Stop();

            if (IsVisible && (_mood == MascotMood.Awake || _mood == MascotMood.Alert))
            {
                if (!_blink.IsEnabled)
                {
                    _blink.Interval = TimeSpan.FromMilliseconds(_random.Next(2500, 5500));
                    _blink.Start();
                }
            }
            else
            {
                _blink.Stop();
                _eyeOpen = 1;
            }
        }

        private void OnBlink(object sender, EventArgs e)
        {
            if (_eyeOpen >= 1)
            {
                _eyeOpen = 0.1;
                _blink.Interval = TimeSpan.FromMilliseconds(120);
            }
            else
            {
                _eyeOpen = 1;
                _blink.Interval = TimeSpan.FromMilliseconds(_random.Next(2500, 5500));
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double size = _size;
            double height = size * (_mat ? 1.08 : 1);
            var dance = Dance.At(Environment.TickCount64 / 1000.0, _mood == MascotMood.Vibing);

            dc.PushTransform(new TranslateTransform(dance.Sway * size, -dance.Lift * size));
            dc.PushTransform(new RotateTransform(dance.Tilt, size / 2, height));
            dc.PushTransform(new ScaleTransform(dance.StretchX, dance.StretchY, size / 2, height));
            double k = size / 100;
            dc.PushTransform(new ScaleTransform(k, k));
            DrawFace(dc, k, dance.Ears);
            dc.Pop();
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        // MARK: Drawing (design units)

        private bool Detailed => _size >= 30;

        private static Geometry Horn(Point start, Point c1, Point tip, Point c2, Point end)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(start, true, true);
                ctx.QuadraticBezierTo(c1, tip, true, true);
                ctx.QuadraticBezierTo(c2, end, true, true);
            }
            g.Freeze();
            return g;
        }

        private static Geometry Curve(Point from, Point control, Point to)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(from, false, false);
                ctx.QuadraticBezierTo(control, to, true, true);
            }
            g.Freeze();
            return g;
        }

        /// <summary>A stroke `width` pixels wide (at least `min`), expressed in design units.</summary>
        private static Pen UnitPen(Brush brush, double width, double min, double k) =>
            Theme.RoundPen(brush, Math.Max(min, width * k) / k);

        private static void RRect(DrawingContext dc, Brush b, double x, double y, double w, double h, double r) =>
            dc.DrawRoundedRectangle(b, null, new Rect(x, y, w, h), r, r);

        private static void Oval(DrawingContext dc, Brush b, double x, double y, double w, double h) =>
            dc.DrawEllipse(b, null, new Point(x + w / 2, y + h / 2), w / 2, h / 2);

        private static void PushRotation(DrawingContext dc, double degrees, double x, double y) =>
            dc.PushTransform(new RotateTransform(degrees, x, y));

        private void DrawEyes(DrawingContext dc, double k, double cy, double left, double right, double sc = 1)
        {
            switch (_mood)
            {
                case MascotMood.Sleeping:
                case MascotMood.Praying:
                {
                    var pen = UnitPen(Ink, 2.4, 1, k);
                    foreach (var cx in new[] { left, right })
                        dc.DrawGeometry(null, pen, Curve(new Point(cx - 6 * sc, cy), new Point(cx, cy + 6 * sc), new Point(cx + 6 * sc, cy)));
                    break;
                }
                case MascotMood.Vibing:
                {
                    var pen = UnitPen(Ink, 2.6, 1, k);
                    foreach (var cx in new[] { left, right })
                        dc.DrawGeometry(null, pen, Curve(new Point(cx - 6 * sc, cy + 3 * sc), new Point(cx, cy - 6 * sc), new Point(cx + 6 * sc, cy + 3 * sc)));
                    break;
                }
                default:
                {
                    double baseHeight = _mood == MascotMood.Alert ? 7.5 : 6.5;
                    double rx = 5.5 * sc;
                    double ry = baseHeight * sc * Math.Max(_eyeOpen, 0.12);
                    foreach (var cx in new[] { left, right })
                    {
                        dc.DrawEllipse(Ink, null, new Point(cx, cy), rx, ry);
                        if (Detailed && _eyeOpen > 0.5)
                            Oval(dc, White, cx - rx * 0.58, cy - ry * 0.75, 2.8 * sc, 2.8 * sc);
                    }
                    break;
                }
            }
        }

        private void DrawSmile(DrawingContext dc, double k, double y)
        {
            if (!Detailed) return;
            var pen = UnitPen(Ink, 1.6, 0.8, k);
            dc.DrawGeometry(null, pen, Curve(new Point(44, y), new Point(47, y + 4), new Point(50, y)));
            dc.DrawGeometry(null, pen, Curve(new Point(50, y), new Point(53, y + 4), new Point(56, y)));
        }

        private void DrawFace(DrawingContext dc, double k, double earWiggle)
        {
            // Sejadah
            if (_mat)
            {
                RRect(dc, MatBrush, 4, 89, 92, 10, 3);
                var fringe = UnitPen(MatBrush, 1.4, 0.8, k);
                foreach (var x in new double[] { 7, 11, 89, 93 })
                    dc.DrawLine(fringe, new Point(x, 99), new Point(x, 106));
            }

            switch (_kind)
            {
                case MascotKind.Cat:
                    PushRotation(dc, -earWiggle, 30, 46);
                    dc.DrawGeometry(White, null, CatLeftEar);
                    if (Detailed) dc.DrawGeometry(Soft, null, CatLeftInner);
                    dc.Pop();
                    PushRotation(dc, earWiggle, 70, 46);
                    dc.DrawGeometry(White, null, CatRightEar);
                    if (Detailed) dc.DrawGeometry(Soft, null, CatRightInner);
                    dc.Pop();
                    RRect(dc, White, 14, 32, 72, 60, 28);
                    DrawEyes(dc, k, 62, 37, 63);
                    dc.DrawGeometry(Ink, null, CatNose);
                    DrawSmile(dc, k, 75);
                    break;

                case MascotKind.Rabbit:
                {
                    // Long ears: upright when alert, droopy when asleep.
                    double rest = _mood == MascotMood.Sleeping ? 40 : (_mood == MascotMood.Alert ? 2 : 8);
                    double tilt = rest + earWiggle;
                    PushRotation(dc, -tilt, 35, 48);
                    RRect(dc, White, 28, 4, 14, 48, 7);
                    if (Detailed) RRect(dc, Soft, 31.5, 10, 7, 34, 3.5);
                    dc.Pop();
                    PushRotation(dc, tilt, 65, 48);
                    RRect(dc, White, 58, 4, 14, 48, 7);
                    if (Detailed) RRect(dc, Soft, 61.5, 10, 7, 34, 3.5);
                    dc.Pop();
                    RRect(dc, White, 18, 40, 64, 54, 27);
                    DrawEyes(dc, k, 66, 39, 61);
                    dc.DrawGeometry(Ink, null, RabbitNose);
                    DrawSmile(dc, k, 79);
                    break;
                }

                case MascotKind.Dragon:
                {
                    // Side fins flap while dancing.
                    PushRotation(dc, -earWiggle, 17, 58);
                    dc.DrawGeometry(White, null, DragonLeftFin);
                    dc.Pop();
                    PushRotation(dc, earWiggle, 83, 58);
                    dc.DrawGeometry(White, null, DragonRightFin);
                    dc.Pop();

                    var horn = Detailed ? HornBrush : White;
                    dc.DrawGeometry(horn, null, DragonLeftHorn);
                    dc.DrawGeometry(horn, null, DragonRightHorn);
                    dc.DrawGeometry(White, null, DragonCrest);

                    RRect(dc, White, 16, 30, 68, 62, 26);
                    DrawEyes(dc, k, 60, 37, 63);
                    foreach (var x in new double[] { 45, 55 })
                        dc.DrawEllipse(Ink, null, new Point(x, 75), 1.9, 1.9);
                    if (Detailed)
                        dc.DrawGeometry(null, UnitPen(Ink, 1.8, 0.8, k), Curve(new Point(41, 81), new Point(50, 88), new Point(59, 81)));
                    break;
                }

                case MascotKind.Owl:
                {
                    // Feathery ear tufts flick while dancing.
                    PushRotation(dc, -earWiggle, 26, 36);
                    dc.DrawGeometry(White, null, OwlLeftTuft);
                    dc.Pop();
                    PushRotation(dc, earWiggle, 74, 36);
                    dc.DrawGeometry(White, null, OwlRightTuft);
                    dc.Pop();
                    RRect(dc, White, 14, 26, 72, 68, 30);
                    // Facial discs around the big eyes.
                    Oval(dc, DiscBrush, 20, 43, 30, 30);
                    Oval(dc, DiscBrush, 50, 43, 30, 30);
                    DrawEyes(dc, k, 58, 35, 65, 1.3);
                    dc.DrawGeometry(BeakBrush, null, OwlBeak);
                    if (Detailed)
                    {
                        var pen = UnitPen(ChestBrush, 1.4, 0.8, k);
                        var chest = new StreamGeometry();
                        using (var c = chest.Open())
                        {
                            foreach (var x in new double[] { 40, 50, 60 })
                            {
                                c.BeginFigure(new Point(x - 3, 84), false, false);
                                c.LineTo(new Point(x, 87), true, true);
                                c.LineTo(new Point(x + 3, 84), true, true);
                            }
                        }
                        chest.Freeze();
                        dc.DrawGeometry(null, pen, chest);
                    }
                    break;
                }

                case MascotKind.MouseDeer:
                {
                    // Big side ears: perk up when alert, droop when asleep, flick while dancing.
                    double droop = _mood == MascotMood.Sleeping ? 35 : (_mood == MascotMood.Alert ? -8 : 0);
                    PushRotation(dc, -(droop + earWiggle), 32, 52);
                    PushRotation(dc, -38, 21, 38);
                    Oval(dc, White, 12, 19, 18, 38);
                    if (Detailed) Oval(dc, Soft, 16.5, 26, 9, 24);
                    dc.Pop();
                    dc.Pop();
                    PushRotation(dc, droop + earWiggle, 68, 52);
                    PushRotation(dc, 38, 79, 38);
                    Oval(dc, White, 70, 19, 18, 38);
                    if (Detailed) Oval(dc, Soft, 74.5, 26, 9, 24);
                    dc.Pop();
                    dc.Pop();
                    RRect(dc, White, 24, 30, 52, 54, 24);
                    Oval(dc, White, 33, 62, 34, 32);
                    DrawEyes(dc, k, 56, 39, 61);
                    Oval(dc, Ink, 45, 79, 10, 6);
                    DrawSmile(dc, k, 86);
                    break;
                }
            }

            if (Detailed && _mood == MascotMood.Sleeping)
            {
                var z = Theme.Formatted("z", 15, FontWeights.Bold, ZBrush, Theme.Dpi(this) * k);
                dc.DrawText(z, new Point(94 - z.Width / 2, 16 - z.Height / 2));
            }
        }
    }

    /// <summary>
    /// The mascot's little dance, as a pure function of time (about 112 bpm):
    /// sways side to side, bops on every beat, flicks its ears, and hops on every 8th beat.
    /// </summary>
    internal readonly struct Dance
    {
        public double Sway { get; init; }
        public double Lift { get; init; }
        public double Tilt { get; init; }
        public double Ears { get; init; }
        public double StretchX { get; init; }
        public double StretchY { get; init; }

        public static Dance At(double time, bool active)
        {
            if (!active) return new Dance { StretchX = 1, StretchY = 1 };

            double beats = time * 112 / 60;
            double beat = beats % 8;
            double phase = beats % 1;

            // Side-to-side sway over two beats, head tilting with it.
            double swing = Math.Sin(beats * Math.PI);
            double sway = swing * 0.05;
            double tilt = swing * 8;

            // Bop: a small dip on each beat.
            double lift = Math.Pow(Math.Sin(phase * Math.PI), 2) * 0.04;

            // Ears flick on the off-beat.
            double ears = Math.Max(0, Math.Sin(phase * 2 * Math.PI)) * 14;

            // Squash a touch as each bop lands.
            double squash = Math.Pow(Math.Cos(phase * Math.PI), 8) * 0.06;
            double sx = 1 + squash;
            double sy = 1 - squash;

            // Hop on beat 8: up and down over one beat, stretched in the air.
            if (beat >= 7)
            {
                double hop = Math.Sin(phase * Math.PI);
                lift += hop * 0.2;
                tilt *= 0.3;
                sx = 1 - hop * 0.05;
                sy = 1 + hop * 0.07;
            }

            return new Dance { Sway = sway, Lift = lift, Tilt = tilt, Ears = ears, StretchX = sx, StretchY = sy };
        }
    }
}
