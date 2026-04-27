using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace WindowNegativer
{
    /// <summary>
    /// A purely native Win32 overlay window used as the click-through host on older Windows.
    /// No WPF involvement means no solid background that blocks hit-testing.
    /// </summary>
    internal sealed class NativeOverlayWindow : IDisposable
    {
        #region Win32

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle, string lpClassName, string? lpWindowName, int dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool repaint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int cbSize;
            public int style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        private const int WS_EX_LAYERED   = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOPMOST    = 0x00000008;
        private const int WS_POPUP         = unchecked((int)0x80000000);
        private const int WS_VISIBLE       = 0x10000000;
        private const uint LWA_ALPHA       = 0x00000002;
        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_HIDE           = 0;
        private const uint SWP_NOZORDER    = 0x0004;
        private const uint SWP_NOACTIVATE  = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint WM_NCHITTEST    = 0x0084;
        private const int HTTRANSPARENT    = -1;

        private static readonly string ClassName = "WindowNegativerOverlay_" + Guid.NewGuid().ToString("N");

        #endregion

        private readonly WndProcDelegate _wndProc;
        private readonly MagnifierHost _magnifierHost;
        private readonly DispatcherTimer _refreshTimer;
        private IntPtr _hwnd;
        private bool _disposed;

        public IntPtr Handle => _hwnd;

        public NativeOverlayWindow()
        {
            _wndProc = NativeWndProc;

            // Register a unique window class
            var wc = new WNDCLASSEX
            {
                cbSize        = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance     = GetModuleHandle(null),
                hbrBackground = IntPtr.Zero, // no background brush
                lpszClassName = ClassName
            };
            RegisterClassEx(ref wc);

            // Create a layered + transparent popup window
            _hwnd = CreateWindowEx(
                WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
                ClassName,
                null,
                WS_POPUP,
                0, 0, 1, 1,
                IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create native overlay window.");

            // Fully opaque layered window — click-through comes from WS_EX_TRANSPARENT, not low alpha
            SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);

            _magnifierHost = new MagnifierHost();
            _magnifierHost.SetParent(_hwnd);
            // Exclude the overlay itself from its own magnifier capture to prevent feedback
            _magnifierHost.SetExcludedWindow(_hwnd);

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _refreshTimer.Tick += (_, _) => _magnifierHost.Refresh();
            _refreshTimer.Start();
        }

        private IntPtr NativeWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_NCHITTEST)
                return (IntPtr)HTTRANSPARENT;

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        public void UpdateBounds(double left, double top, double width, double height, double dpiX, double dpiY)
        {
            if (_hwnd == IntPtr.Zero) return;

            int x = (int)Math.Round(left * dpiX);
            int y = (int)Math.Round(top * dpiY);
            int w = (int)Math.Round(width * dpiX);
            int h = (int)Math.Round(height * dpiY);

            if (w <= 0 || h <= 0) return;

            MoveWindow(_hwnd, x, y, w, h, false);
            _magnifierHost.UpdateSource(x, y, w, h);
            _magnifierHost.UpdateChildSize(w, h);
        }

        public void Show() => ShowWindow(_hwnd, SW_SHOWNOACTIVATE);

        public void Hide() => ShowWindow(_hwnd, SW_HIDE);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _refreshTimer.Stop();
            _magnifierHost.Dispose();
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
    }
}
