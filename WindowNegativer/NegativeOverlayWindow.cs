using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDC(string lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
            IntPtr hdcSrc, int xSrc, int ySrc, uint dwRop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SRCCOPY = 0x00CC0020;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        private readonly DispatcherTimer _timer;
        private readonly Image _image;

        public NegativeOverlayWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Focusable = false;
            IsHitTestVisible = false;

            _image = new Image
            {
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            };

            Content = _image;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _timer.Tick += (_, _) => CaptureAndInvert();

            SourceInitialized += OnSourceInitialized;
            Loaded += (_, _) => _timer.Start();
            Closed += (_, _) => _timer.Stop();
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        }

        public void UpdateFromFrame(Window frame, double titleBarHeight, double framePadding)
        {
            Left = frame.Left + framePadding;
            Top = frame.Top + titleBarHeight;
            Width = Math.Max(0, frame.ActualWidth - (framePadding * 2));
            Height = Math.Max(0, frame.ActualHeight - titleBarHeight - framePadding);
        }

        private void CaptureAndInvert()
        {
            var dpiScale = VisualTreeHelper.GetDpi(this);
            int x = (int)Math.Round(Left * dpiScale.DpiScaleX);
            int y = (int)Math.Round(Top * dpiScale.DpiScaleY);
            int w = (int)Math.Round(ActualWidth * dpiScale.DpiScaleX);
            int h = (int)Math.Round(ActualHeight * dpiScale.DpiScaleY);

            if (w <= 0 || h <= 0)
            {
                return;
            }

            IntPtr hdcScreen = CreateDC("DISPLAY", null, null, IntPtr.Zero);
            IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
            IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, w, h);
            IntPtr hOld = SelectObject(hdcMem, hBitmap);

            BitBlt(hdcMem, 0, 0, w, h, hdcScreen, x, y, SRCCOPY);
            SelectObject(hdcMem, hOld);

            var bmpSource = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            DeleteObject(hBitmap);
            DeleteDC(hdcMem);
            DeleteDC(hdcScreen);

            var inverted = InvertBitmap(bmpSource);
            inverted.Freeze();
            _image.Source = inverted;
        }

        private static WriteableBitmap InvertBitmap(BitmapSource source)
        {
            var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = formatted.PixelWidth;
            int height = formatted.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            formatted.CopyPixels(pixels, stride, 0);

            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = (byte)(255 - pixels[i]);
                pixels[i + 1] = (byte)(255 - pixels[i + 1]);
                pixels[i + 2] = (byte)(255 - pixels[i + 2]);
            }

            var bitmap = new WriteableBitmap(width, height, source.DpiX, source.DpiY, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            return bitmap;
        }
    }
}
