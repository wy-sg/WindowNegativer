using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shapes;

namespace WindowNegativer
{
    public partial class MainWindow : Window
    {
        #region Win32

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_SYSCOMMAND = 0x0112;

        #endregion

        private NegativeOverlayWindow? _overlayWindow;

        internal const double TitleBarHeight = 28;
        internal const double FramePadding = 4;

        private Rect? _restoreBounds;
        private bool _titleBarPressed;
        private Point _titleBarPressedPoint;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += OnLoaded;
            Closed += OnClosed;
            LocationChanged += (_, _) => UpdateOverlayBounds();
            SizeChanged += (_, _) => UpdateOverlayBounds();
            IsVisibleChanged += (_, _) => UpdateOverlayVisibility();
            StateChanged += (_, _) => UpdateOverlayBounds();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            EnsureOverlayWindow();
            UpdateOverlayBounds();
            UpdateOverlayVisibility();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (_overlayWindow is null)
            {
                return;
            }

            _overlayWindow.Close();
            _overlayWindow = null;
        }

        private void EnsureOverlayWindow()
        {
            if (_overlayWindow is not null)
            {
                return;
            }

            _overlayWindow = new NegativeOverlayWindow
            {
                Owner = this
            };

            _overlayWindow.Show();
        }

        private void UpdateOverlayVisibility()
        {
            if (_overlayWindow is null)
            {
                return;
            }

            if (IsVisible)
            {
                if (!_overlayWindow.IsVisible)
                {
                    _overlayWindow.Show();
                }
            }
            else if (_overlayWindow.IsVisible)
            {
                _overlayWindow.Hide();
            }
        }

        private void UpdateOverlayBounds()
        {
            _overlayWindow?.UpdateFromFrame(this, TitleBarHeight, FramePadding);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            _titleBarPressed = true;
            _titleBarPressedPoint = e.GetPosition(this);

            if (_restoreBounds.HasValue)
            {
                if (sender is IInputElement inputElement)
                {
                    Mouse.Capture(inputElement);
                }

                return;
            }

            DragMove();
        }

        private void TitleBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_titleBarPressed || e.LeftButton != MouseButtonState.Pressed || !_restoreBounds.HasValue)
            {
                return;
            }

            var currentPoint = e.GetPosition(this);
            if (Math.Abs(currentPoint.X - _titleBarPressedPoint.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(currentPoint.Y - _titleBarPressedPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var restoreBounds = _restoreBounds.Value;
            var screenPoint = PointToScreen(currentPoint);
            double horizontalRatio = ActualWidth <= 0 ? 0.5 : currentPoint.X / ActualWidth;
            horizontalRatio = Math.Max(0.0, Math.Min(1.0, horizontalRatio));

            _restoreBounds = null;
            Left = screenPoint.X - (restoreBounds.Width * horizontalRatio);
            Top = Math.Max(0, screenPoint.Y - (TitleBarHeight / 2));
            Width = restoreBounds.Width;
            Height = restoreBounds.Height;

            UpdateOverlayBounds();

            if (sender is IInputElement inputElement)
            {
                Mouse.Capture(null);
            }

            _titleBarPressed = false;
            DragMove();
        }

        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _titleBarPressed = false;

            if (sender is IInputElement)
            {
                Mouse.Capture(null);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void ToggleMaximize()
        {
            if (_restoreBounds.HasValue)
            {
                var r = _restoreBounds.Value;
                _restoreBounds = null;
                Left = r.Left;
                Top = r.Top;
                Width = r.Width;
                Height = r.Height;
            }
            else
            {
                _restoreBounds = new Rect(Left, Top, Width, Height);
                var area = SystemParameters.WorkArea;
                Left = area.Left;
                Top = area.Top;
                Width = area.Width;
                Height = area.Height;
            }

            UpdateOverlayBounds();
        }

        private void Resize_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Rectangle rect || rect.Tag is not string dir) return;

            var hwnd = new WindowInteropHelper(this).Handle;
            IntPtr resizeDir = dir switch
            {
                "Left" => (IntPtr)0xF001,
                "Right" => (IntPtr)0xF002,
                "Top" => (IntPtr)0xF003,
                "TopLeft" => (IntPtr)0xF004,
                "TopRight" => (IntPtr)0xF005,
                "Bottom" => (IntPtr)0xF006,
                "BottomLeft" => (IntPtr)0xF007,
                "BottomRight" => (IntPtr)0xF008,
                _ => IntPtr.Zero
            };

            if (resizeDir != IntPtr.Zero)
            {
                ReleaseCapture();
                SendMessage(hwnd, WM_SYSCOMMAND, resizeDir, IntPtr.Zero);
            }
        }
    }
}