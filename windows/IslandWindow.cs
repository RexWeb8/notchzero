using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace NotchZero
{
    /// <summary>
    /// The island itself: black when compact, dark glass when open. Also paints a nearly invisible
    /// hover zone around the compact pill, so it is easy to reach and to drop files onto.
    /// </summary>
    internal sealed class IslandSurface : FrameworkElement
    {
        private static readonly Brush HotBrush = Theme.Solid(Color.FromArgb(1, 0, 0, 0));
        private static readonly Color Closed = Color.FromArgb(255, 0, 0, 0);
        private static readonly Color OpenTop = Color.FromArgb(246, 22, 23, 26);
        private static readonly Color OpenBottom = Color.FromArgb(246, 10, 10, 12);

        private Rect _rect;
        private double _radius;
        private double _glass;
        private Rect _hot = Rect.Empty;

        public void SetShape(Rect rect, double radius, double glass, Rect hot)
        {
            _rect = rect;
            _radius = radius;
            _glass = glass;
            _hot = hot;
            InvalidateVisual();
        }

        private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        protected override void OnRender(DrawingContext dc)
        {
            if (_rect.Width <= 0 || _rect.Height <= 0) return;
            if (!_hot.IsEmpty) dc.DrawRectangle(HotBrush, null, _hot);

            double r = _radius;
            double g = _glass;

            // Soft shadow under the open panel.
            if (g > 0.01)
            {
                for (int i = 1; i <= 4; i++)
                {
                    var s = _rect;
                    s.Inflate(i * 3, i * 3);
                    s.Offset(0, 4 + i);
                    dc.DrawRoundedRectangle(Theme.K(0.07 * g / i), null, s, r + i * 3, r + i * 3);
                }
            }

            var fill = new LinearGradientBrush(Mix(Closed, OpenTop, g), Mix(Closed, OpenBottom, g), 90);
            dc.DrawRoundedRectangle(fill, null, _rect, r, r);

            if (g > 0.01)
            {
                // A little top sheen, like light on glass.
                dc.PushClip(new RectangleGeometry(_rect, r, r));
                var sheen = new LinearGradientBrush(Theme.WC(0.07 * g), Theme.WC(0), 90);
                dc.DrawRectangle(sheen, null, new Rect(_rect.X, _rect.Y, _rect.Width, Math.Min(64, _rect.Height)));
                dc.Pop();
            }

            var edge = _rect;
            edge.Inflate(-0.5, -0.5);
            if (edge.Width > 0 && edge.Height > 0)
            {
                double edgeRadius = Math.Max(0, r - 0.5);
                dc.DrawRoundedRectangle(null, new Pen(Theme.W(0.15 - 0.03 * g), 1), edge, edgeRadius, edgeRadius);
            }
        }
    }

    /// <summary>
    /// A transparent, always-on-top, click-through-where-empty window across the top centre of the
    /// main screen. The island shape morphs between states with a spring.
    /// </summary>
    internal sealed class IslandWindow : Window
    {
        public const double TopGap = 8;
        public const double WinW = 960;
        public const double WinH = TopGap + OpenPanel.PanelHeight + 30;

        private readonly Grid _host;
        private readonly Canvas _root;
        private readonly IslandSurface _surface;
        private readonly Canvas _content;
        private readonly Spring _w = new Spring(96);
        private readonly Spring _h = new Spring(8);
        private readonly Spring _r = new Spring(4);
        private readonly Spring _glass = new Spring(0, 0.3, 1, 0.002);
        private readonly FrameLoop _loop;
        private bool _compact = true;
        private IntPtr _hwnd;

        public event Action SurfaceClicked;
        public event Action<string[]> FilesDropped;
        public event Action<bool> DragHover;

        public IslandWindow()
        {
            Title = "NotchZero";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            AllowDrop = true;
            Focusable = false;

            _root = new Canvas { Width = WinW, Height = WinH };
            _surface = new IslandSurface { Width = WinW, Height = WinH };
            _content = new Canvas { Width = WinW, Height = WinH };
            _root.Children.Add(_surface);
            _root.Children.Add(_content);
            _host = new Grid();
            _host.Children.Add(_root);
            Content = _host;

            _loop = new FrameLoop(Step);
            _surface.MouseLeftButtonDown += (s, e) => SurfaceClicked?.Invoke();

            DragEnter += OnDragOver;
            DragOver += OnDragOver;
            DragLeave += (s, e) => DragHover?.Invoke(false);
            Drop += OnDrop;

            SourceInitialized += OnSourceInitialized;
            DpiChanged += (s, e) => Reposition();
            SystemEvents.DisplaySettingsChanged += (s, e) => Dispatcher.InvokeAsync(Reposition);

            Reposition();
            ApplyGeometry();
        }

        public IntPtr Handle => _hwnd;

        /// <summary>The island's current rectangle, in window units.</summary>
        public Rect CurrentRect { get; private set; }

        public void AddView(FrameworkElement view)
        {
            Canvas.SetLeft(view, Math.Round(WinW / 2 - view.Width / 2));
            Canvas.SetTop(view, TopGap);
            _content.Children.Add(view);
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
            ex |= Native.WS_EX_TOOLWINDOW;       // keep it out of Alt+Tab
            ex &= ~Native.WS_EX_APPWINDOW;
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Clicking the island shouldn't steal focus from the app you're working in.
            if (msg == Native.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);
            }
            return IntPtr.Zero;
        }

        public void Reposition()
        {
            double screen = SystemParameters.PrimaryScreenWidth;
            double scale = screen < WinW + 20 ? Math.Max(0.6, (screen - 20) / WinW) : 1;
            _host.LayoutTransform = scale == 1 ? Transform.Identity : new ScaleTransform(scale, scale);
            Width = WinW * scale;
            Height = WinH * scale;
            Left = Math.Round((screen - Width) / 2);
            Top = 0;
        }

        public void KeepOnTop()
        {
            if (_hwnd == IntPtr.Zero || !IsVisible) return;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        public void BringToFront()
        {
            if (_hwnd != IntPtr.Zero) Native.SetForegroundWindow(_hwnd);
        }

        // MARK: Shape animation

        public void AnimateTo(double width, double height, double radius, double glass, bool compact, bool bouncy)
        {
            _compact = compact;
            double response = bouncy ? 0.42 : 0.34;
            double damping = bouncy ? 0.8 : 0.95;
            _w.Configure(response, damping);
            _h.Configure(response, damping);
            _r.Configure(response, damping);
            _w.Target = width;
            _h.Target = height;
            _r.Target = radius;
            _glass.Target = glass;
            _loop.Start();
        }

        public void SnapTo(double width, double height, double radius, double glass, bool compact)
        {
            _compact = compact;
            _w.Value = _w.Target = width;
            _h.Value = _h.Target = height;
            _r.Value = _r.Target = radius;
            _glass.Value = _glass.Target = glass;
            _w.Velocity = _h.Velocity = _r.Velocity = _glass.Velocity = 0;
            ApplyGeometry();
        }

        private bool Step(double dt)
        {
            bool moving = _w.Step(dt) | _h.Step(dt) | _r.Step(dt) | _glass.Step(dt);
            ApplyGeometry();
            return moving;
        }

        private void ApplyGeometry()
        {
            double w = Math.Max(1, _w.Value);
            double h = Math.Max(1, _h.Value);
            var rect = new Rect(WinW / 2 - w / 2, TopGap, w, h);
            double r = Math.Clamp(_r.Value, 0, Math.Min(w, h) / 2);
            var hot = _compact ? new Rect(rect.X - 12, 0, rect.Width + 24, rect.Bottom + 8) : Rect.Empty;
            _surface.SetShape(rect, r, Math.Clamp(_glass.Value, 0, 1), hot);
            _content.Clip = new RectangleGeometry(rect, r, r);
            CurrentRect = rect;
        }

        // MARK: Hover

        /// <summary>Is the mouse over the island (grown by `inflate`), or above it at the top edge?</summary>
        public bool IsCursorNear(double inflate)
        {
            if (!IsVisible || PresentationSource.FromVisual(_root) == null) return false;
            if (!Native.GetCursorPos(out var pt)) return false;
            Point p;
            try
            {
                p = _root.PointFromScreen(new Point(pt.X, pt.Y));
            }
            catch
            {
                return false;
            }
            var r = CurrentRect;
            var zone = new Rect(r.X - inflate, 0, r.Width + inflate * 2, r.Bottom + inflate);
            return zone.Contains(p);
        }

        // MARK: Drag & drop

        private void OnDragOver(object sender, DragEventArgs e)
        {
            bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            if (files) DragHover?.Invoke(true);
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                FilesDropped?.Invoke(paths);
            else
                DragHover?.Invoke(false);
        }
    }
}
