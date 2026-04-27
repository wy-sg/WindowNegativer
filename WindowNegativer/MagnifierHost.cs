using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WindowNegativer
{
    internal sealed class MagnifierHost : HwndHost, IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_STYLE = -16;
        private const int WS_CHILD_STYLE = unchecked((int)0x40000000);
        [StructLayout(LayoutKind.Sequential)]
        private struct RectNative
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MagTransform
        {
            public float M00;
            public float M01;
            public float M02;
            public float M10;
            public float M11;
            public float M12;
            public float M20;
            public float M21;
            public float M22;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MagColorEffect
        {
            public float M00;
            public float M01;
            public float M02;
            public float M03;
            public float M04;
            public float M10;
            public float M11;
            public float M12;
            public float M13;
            public float M14;
            public float M20;
            public float M21;
            public float M22;
            public float M23;
            public float M24;
            public float M30;
            public float M31;
            public float M32;
            public float M33;
            public float M34;
            public float M40;
            public float M41;
            public float M42;
            public float M43;
            public float M44;
        }

        [DllImport("Magnification.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MagInitialize();

        [DllImport("Magnification.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MagSetWindowSource(IntPtr hwnd, RectNative rect);

        [DllImport("Magnification.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MagSetWindowTransform(IntPtr hwnd, ref MagTransform transform);

        [DllImport("Magnification.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MagSetColorEffect(IntPtr hwnd, ref MagColorEffect effect);

        [DllImport("Magnification.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MagSetWindowFilterList(IntPtr hwnd, uint mode, int count, IntPtr windows);

        [DllImport("dwmapi.dll", PreserveSig = false, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DwmIsCompositionEnabled();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int exStyle,
            string lpClassName,
            string? lpWindowName,
            int dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr rect, bool erase);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WS_CHILD = unchecked((int)0x40000000);
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_DISABLED = 0x08000000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint MW_FILTERMODE_EXCLUDE = 0;

        private static readonly MagTransform IdentityTransform = new()
        {
            M00 = 1f,
            M11 = 1f,
            M22 = 1f
        };

        private static readonly MagColorEffect NegativeEffect = new()
        {
            M00 = -1f,
            M11 = -1f,
            M22 = -1f,
            M33 = 1f,
            M40 = 1f,
            M41 = 1f,
            M42 = 1f,
            M44 = 1f
        };

        private static bool _initialized;
        private IntPtr _hwnd;
        private IntPtr _excludedWindow;
        private RectNative _sourceRect;

        private void ApplyMagnifierSettings()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            var transform = IdentityTransform;
            MagSetWindowTransform(_hwnd, ref transform);

            var effect = NegativeEffect;
            MagSetColorEffect(_hwnd, ref effect);

            ApplyFilter();
            MagSetWindowSource(_hwnd, _sourceRect);
        }

        public void SetExcludedWindow(IntPtr hwnd)
        {
            _excludedWindow = hwnd;
            ApplyFilter();
        }

        public void UpdateSource(int x, int y, int width, int height)
        {
            _sourceRect = new RectNative
            {
                Left = x,
                Top = y,
                Right = x + Math.Max(0, width),
                Bottom = y + Math.Max(0, height)
            };

            if (_hwnd != IntPtr.Zero)
            {
                ApplyMagnifierSettings();
            }
        }

        public void Refresh()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            ApplyMagnifierSettings();
            InvalidateRect(_hwnd, IntPtr.Zero, false);
            UpdateWindow(_hwnd);
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            if (!_initialized)
            {
                if (Environment.OSVersion.Version.Major < 10 && !DwmIsCompositionEnabled())
                {
                    throw new InvalidOperationException("Desktop composition must be enabled on this version of Windows.");
                }

                _initialized = MagInitialize();
                if (!_initialized)
                {
                    throw new InvalidOperationException("Magnification API initialization failed.");
                }
            }

            _hwnd = CreateWindowEx(
                WS_EX_TRANSPARENT | WS_EX_NOACTIVATE,
                "Magnifier",
                null,
                WS_CHILD | WS_VISIBLE | WS_DISABLED,
                0,
                0,
                Math.Max(1, (int)ActualWidth),
                Math.Max(1, (int)ActualHeight),
                hwndParent.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create magnifier window.");
            }

            ApplyMagnifierSettings();

            return new HandleRef(this, _hwnd);
        }

        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                handled = true;
                return (IntPtr)HTTRANSPARENT;
            }

            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            DestroyWindow(hwnd.Handle);
            _hwnd = IntPtr.Zero;
        }

        protected override void OnWindowPositionChanged(System.Windows.Rect rcBoundingBox)
        {
            base.OnWindowPositionChanged(rcBoundingBox);

            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            SetWindowPos(
                _hwnd,
                IntPtr.Zero,
                0,
                0,
                Math.Max(1, (int)rcBoundingBox.Width),
                Math.Max(1, (int)rcBoundingBox.Height),
                SWP_NOZORDER | SWP_NOACTIVATE);

            ApplyMagnifierSettings();
        }

        public void SetParent(IntPtr parentHwnd)
        {
            if (_hwnd == IntPtr.Zero)
            {
                EnsureCreated(parentHwnd);
            }
            else
            {
                SetParent(_hwnd, parentHwnd);
            }
        }

        public void UpdateChildSize(int width, int height)
        {
            if (_hwnd == IntPtr.Zero) return;
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0,
                Math.Max(1, width), Math.Max(1, height),
                SWP_NOZORDER | SWP_NOACTIVATE);
            ApplyMagnifierSettings();
        }

        private void EnsureCreated(IntPtr parentHwnd)
        {
            if (_hwnd != IntPtr.Zero) return;

            if (!_initialized)
            {
                if (Environment.OSVersion.Version.Major < 10 && !DwmIsCompositionEnabled())
                    throw new InvalidOperationException("Desktop composition must be enabled on this version of Windows.");
                _initialized = MagInitialize();
                if (!_initialized)
                    throw new InvalidOperationException("Magnification API initialization failed.");
            }

            _hwnd = CreateWindowEx(
                WS_EX_TRANSPARENT | WS_EX_NOACTIVATE,
                "Magnifier", null,
                WS_CHILD | WS_VISIBLE | WS_DISABLED,
                0, 0, 1, 1,
                parentHwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create magnifier window.");

            ApplyMagnifierSettings();
        }

        protected override void Dispose(bool disposing)
        {
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            base.Dispose(disposing);
        }

        private void ApplyFilter()
        {
            if (_hwnd == IntPtr.Zero || _excludedWindow == IntPtr.Zero)
            {
                return;
            }

            IntPtr windows = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(windows, _excludedWindow);
                MagSetWindowFilterList(_hwnd, MW_FILTERMODE_EXCLUDE, 1, windows);
            }
            finally
            {
                Marshal.FreeHGlobal(windows);
            }
        }
    }
}
