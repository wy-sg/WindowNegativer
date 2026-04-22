using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WindowNegativer
{
    internal sealed class NegativeOverlayWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private readonly MagnifierHost _magnifierHost;
        private readonly DispatcherTimer _refreshTimer;

        public NegativeOverlayWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = Environment.OSVersion.Version.Major >= 10;
            Background = System.Windows.Media.Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Focusable = false;
            IsHitTestVisible = false;

            _magnifierHost = new MagnifierHost();
            Content = _magnifierHost;

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _refreshTimer.Tick += (_, _) => _magnifierHost.Refresh();

            SourceInitialized += OnSourceInitialized;
            Loaded += (_, _) => _refreshTimer.Start();
            Closed += (_, _) => _refreshTimer.Stop();
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            int layeredStyle = AllowsTransparency ? WS_EX_LAYERED : 0;
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | layeredStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            _magnifierHost.SetExcludedWindow(hwnd);
        }

        public void UpdateFromFrame(Window frame, double titleBarHeight, double framePadding)
        {
            Left = frame.Left + framePadding;
            Top = frame.Top + titleBarHeight;
            Width = Math.Max(0, frame.ActualWidth - (framePadding * 2));
            Height = Math.Max(0, frame.ActualHeight - titleBarHeight - framePadding);

            var dpiScale = VisualTreeHelper.GetDpi(frame);
            int x = (int)Math.Round(Left * dpiScale.DpiScaleX);
            int y = (int)Math.Round(Top * dpiScale.DpiScaleY);
            int width = (int)Math.Round(Width * dpiScale.DpiScaleX);
            int height = (int)Math.Round(Height * dpiScale.DpiScaleY);
            _magnifierHost.UpdateSource(x, y, width, height);
        }
    }
}
