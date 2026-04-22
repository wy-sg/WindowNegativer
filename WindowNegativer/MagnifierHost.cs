using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WindowNegativer
{
    internal sealed class MagnifierHost : HwndHost
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool MagImageScalingCallback(
            IntPtr hwnd,
            IntPtr srcdata,
            MagImageHeader srcheader,
            IntPtr destdata,
            MagImageHeader destheader,
            RectNative unclipped,
            RectNative clipped,
            IntPtr dirty);

        [StructLayout(LayoutKind.Sequential)]
        private struct RectNative
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MagImageHeader
        {
            public uint Width;
            public uint Height;
            public Guid Format;
            public uint Stride;
            public uint Offset;
            public UIntPtr cbSize;
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

        [DllImport("Magnification.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool MagInitialize();

        [DllImport("Magnification.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool MagSetWindowSource(IntPtr hwnd, RectNative rect);

        [DllImport("Magnification.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool MagSetWindowTransform(IntPtr hwnd, ref MagTransform transform);

        [DllImport("Magnification.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool MagSetColorEffect(IntPtr hwnd, ref MagColorEffect effect);

        [DllImport("Magnification.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool MagSetImageScalingCallback(IntPtr hwnd, MagImageScalingCallback callback);

        [DllImport("Magnification.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool MagSetWindowFilterList(IntPtr hwnd, uint mode, int count, IntPtr[] windows);

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

        private const int WS_CHILD = unchecked((int)0x40000000);
        private const int WS_VISIBLE = 0x10000000;
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
        private static readonly MagImageScalingCallback ScalingCallback = OnMagImageScaling;
        private IntPtr _hwnd;
        private IntPtr _excludedWindow;
        private RectNative _sourceRect;
        private readonly bool _useScalingCallback = Environment.OSVersion.Version.Major < 10;

        private void ApplyMagnifierSettings()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            var transform = IdentityTransform;
            MagSetWindowTransform(_hwnd, ref transform);

            if (_useScalingCallback)
            {
                MagSetImageScalingCallback(_hwnd, ScalingCallback);
            }
            else
            {
                var effect = NegativeEffect;
                MagSetColorEffect(_hwnd, ref effect);
            }

            ApplyFilter();
            MagSetWindowSource(_hwnd, _sourceRect);
        }

        private static bool OnMagImageScaling(
            IntPtr hwnd,
            IntPtr srcdata,
            MagImageHeader srcheader,
            IntPtr destdata,
            MagImageHeader destheader,
            RectNative unclipped,
            RectNative clipped,
            IntPtr dirty)
        {
            int sourceSize = checked((int)srcheader.cbSize.ToUInt64());
            int destinationSize = checked((int)destheader.cbSize.ToUInt64());

            if (sourceSize <= 0 || destinationSize <= 0)
            {
                return false;
            }

            byte[] source = new byte[sourceSize];
            byte[] destination = new byte[destinationSize];
            Marshal.Copy(srcdata, source, 0, sourceSize);

            int width = (int)Math.Min(srcheader.Width, destheader.Width);
            int height = (int)Math.Min(srcheader.Height, destheader.Height);
            int srcStride = (int)srcheader.Stride;
            int destStride = (int)destheader.Stride;
            int rowBytes = Math.Min(width * 4, Math.Min(srcStride, destStride));

            for (int y = 0; y < height; y++)
            {
                int srcRow = y * srcStride;
                int destRow = y * destStride;

                for (int x = 0; x < rowBytes; x += 4)
                {
                    destination[destRow + x] = (byte)(255 - source[srcRow + x]);
                    destination[destRow + x + 1] = (byte)(255 - source[srcRow + x + 1]);
                    destination[destRow + x + 2] = (byte)(255 - source[srcRow + x + 2]);
                    destination[destRow + x + 3] = source[srcRow + x + 3];
                }
            }

            Marshal.Copy(destination, 0, destdata, destinationSize);
            return true;
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
                _initialized = MagInitialize();
                if (!_initialized)
                {
                    throw new InvalidOperationException("Magnification API initialization failed.");
                }
            }

            _hwnd = CreateWindowEx(
                0,
                "Magnifier",
                null,
                WS_CHILD | WS_VISIBLE,
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

        private void ApplyFilter()
        {
            if (_hwnd == IntPtr.Zero || _excludedWindow == IntPtr.Zero)
            {
                return;
            }

            MagSetWindowFilterList(_hwnd, MW_FILTERMODE_EXCLUDE, 1, new[] { _excludedWindow });
        }
    }
}
