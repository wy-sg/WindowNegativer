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

        private const int GWL_EXSTYLE    = -20;
        private const int WM_NCHITTEST   = 0x0084;
        private const int HTTRANSPARENT  = -1;
        private const int WS_EX_LAYERED  = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SWP_NOMOVE    = 0x0002;
        private const uint SWP_NOSIZE    = 0x0001;
        private const uint SWP_NOZORDER  = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        // Win11 path: WPF-hosted magnifier
        private readonly MagnifierHost? _magnifierHost;
        private readonly DispatcherTimer? _refreshTimer;

        // Win7 path: pure native overlay
        private readonly NativeOverlayWindow? _nativeOverlay;
        private bool _nativeOverlayVisible;

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int RtlGetVersion(ref OSVERSIONINFOEX lpVersionInformation);

        [StructLayout(LayoutKind.Sequential)]
        private struct OSVERSIONINFOEX
        {
            public int dwOSVersionInfoSize;
            public int dwMajorVersion;
            public int dwMinorVersion;
            public int dwBuildNumber;
            public int dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
            public ushort wServicePackMajor;
            public ushort wServicePackMinor;
            public ushort wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }

        private static bool GetIsLegacyWindows()
        {
            // RtlGetVersion bypasses compatibility mode shims and always returns the real OS version
            var osInfo = new OSVERSIONINFOEX { dwOSVersionInfoSize = Marshal.SizeOf<OSVERSIONINFOEX>() };
            if (RtlGetVersion(ref osInfo) == 0)
                return osInfo.dwMajorVersion < 10;
            // Fallback: should never reach here on any supported Windows
            return Environment.OSVersion.Version.Major < 10;
        }

        private static readonly bool IsLegacyWindows = GetIsLegacyWindows();

        public NegativeOverlayWindow()
        {
            if (IsLegacyWindows)
            {
                // On older Windows we only use this WPF window for housekeeping;
                // the visible overlay is the pure-native NativeOverlayWindow.
                _nativeOverlay = new NativeOverlayWindow();

                // Make this WPF shell invisible and non-interactive
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = System.Windows.Media.Brushes.Transparent;
                Width = 0;
                Height = 0;
                ShowInTaskbar = false;
                ShowActivated = false;
                Topmost = true;
                Opacity = 0;
                ResizeMode = ResizeMode.NoResize;

                SourceInitialized += (_, _) =>
                {
                    // nothing needed — NativeOverlayWindow already excludes itself
                };

                Closed += (_, _) =>
                {
                    _nativeOverlay!.Hide();
                    _nativeOverlay!.Dispose();
                    _nativeOverlayVisible = false;
                };
            }
            else
            {
                // Win11+: WPF-hosted magnifier with layered transparency
                AllowsTransparency = true;
                WindowStyle = WindowStyle.None;
                Background = System.Windows.Media.Brushes.Transparent;
                ShowInTaskbar = false;
                ShowActivated = false;
                Topmost = true;
                ResizeMode = ResizeMode.NoResize;
                Focusable = false;
                IsHitTestVisible = false;

                _magnifierHost = new MagnifierHost();
                Content = _magnifierHost;

                _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                _refreshTimer.Tick += (_, _) => _magnifierHost.Refresh();

                SourceInitialized += OnSourceInitialized;
                Loaded += (_, _) => _refreshTimer.Start();
                Closed += (_, _) => _refreshTimer.Stop();
            }
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE,
                exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            _magnifierHost!.SetExcludedWindow(hwnd);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                handled = true;
                return (IntPtr)HTTRANSPARENT;
            }
            return IntPtr.Zero;
        }

        public void UpdateFromFrame(Window frame, double titleBarHeight, double framePadding)
        {
            double left   = frame.Left   + framePadding;
            double top    = frame.Top    + titleBarHeight;
            double width  = Math.Max(0, frame.ActualWidth  - (framePadding * 2));
            double height = Math.Max(0, frame.ActualHeight - titleBarHeight - framePadding);

            if (IsLegacyWindows)
            {
                var dpi = VisualTreeHelper.GetDpi(frame);
                _nativeOverlay!.UpdateBounds(left, top, width, height, dpi.DpiScaleX, dpi.DpiScaleY);
                return;
            }

            Left   = left;
            Top    = top;
            Width  = width;
            Height = height;

            var dpiScale = VisualTreeHelper.GetDpi(frame);
            int x = (int)Math.Round(Left   * dpiScale.DpiScaleX);
            int y = (int)Math.Round(Top    * dpiScale.DpiScaleY);
            int w = (int)Math.Round(Width  * dpiScale.DpiScaleX);
            int h = (int)Math.Round(Height * dpiScale.DpiScaleY);
            _magnifierHost!.UpdateSource(x, y, w, h);
        }

        public void ShowOverlay()
        {
            if (IsLegacyWindows)
            {
                if (!_nativeOverlayVisible)
                {
                    _nativeOverlay!.Show();
                    _nativeOverlayVisible = true;
                }

                return;
            }

            if (!IsVisible)
            {
                Show();
            }
        }

        public void HideOverlay()
        {
            if (IsLegacyWindows)
            {
                if (_nativeOverlayVisible)
                {
                    _nativeOverlay!.Hide();
                    _nativeOverlayVisible = false;
                }

                return;
            }

            if (IsVisible)
            {
                Hide();
            }
        }
    }
}
