using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OceanyaClient.Features.Viewport
{
    /// <summary>
    /// Manages DWM custom iconic thumbnail bitmaps that let the main window keep the
    /// shell slot while the viewport supplies its taskbar and Alt-Tab preview image.
    /// </summary>
    internal static class ViewportThumbnailCompositor
    {
        private const int DwmwaForceIconicRepresentation = 7;
        private const int DwmwaHasIconicBitmap = 10;
        private const uint DibRgbColors = 0;

        /// <summary>Enables custom iconic thumbnail for the specified HWND.</summary>
        internal static void Activate(IntPtr hwnd)
        {
            int enabled = 1;
            DwmSetWindowAttribute(hwnd, DwmwaForceIconicRepresentation, ref enabled, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaHasIconicBitmap, ref enabled, sizeof(int));
            DwmInvalidateIconicBitmaps(hwnd);
        }

        /// <summary>Disables custom iconic thumbnail for the specified HWND.</summary>
        internal static void Deactivate(IntPtr hwnd)
        {
            int disabled = 0;
            DwmSetWindowAttribute(hwnd, DwmwaForceIconicRepresentation, ref disabled, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaHasIconicBitmap, ref disabled, sizeof(int));
            DwmInvalidateIconicBitmaps(hwnd);
        }

        /// <summary>Requests that DWM discard cached thumbnails and ask for a fresh bitmap.</summary>
        internal static void Invalidate(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero)
            {
                DwmInvalidateIconicBitmaps(hwnd);
            }
        }

        /// <summary>
        /// Proactively captures and submits a viewport thumbnail to DWM. Call this after
        /// <see cref="Activate"/> so the loading spinner is replaced immediately.
        /// </summary>
        internal static void SubmitIconicThumbnail(IntPtr hwnd, Window? mainWindow, Window? viewportWindow)
        {
            IntPtr hBitmap = CreateScreenshotComposite(mainWindow, viewportWindow, 0, 0, scale: false);
            if (hBitmap == IntPtr.Zero)
            {
                return;
            }

            try
            {
                DwmSetIconicThumbnail(hwnd, hBitmap, 0);
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        /// <summary>
        /// Handles <c>WM_DWMSENDICONICTHUMBNAIL</c>: captures the viewport preview and submits it.
        /// </summary>
        internal static void HandleSendIconicThumbnail(
            IntPtr mainHwnd,
            Window? mainWindow,
            Window? viewportWindow,
            IntPtr wParam)
        {
            int maxWidth = Math.Max(100, (int)(((long)wParam >> 16) & 0xFFFF));
            int maxHeight = Math.Max(100, (int)((long)wParam & 0xFFFF));

            IntPtr hBitmap = CreateScreenshotComposite(mainWindow, viewportWindow, maxWidth, maxHeight, scale: true);
            if (hBitmap == IntPtr.Zero)
            {
                return;
            }

            try
            {
                DwmSetIconicThumbnail(mainHwnd, hBitmap, 0);
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        /// <summary>
        /// Handles <c>WM_DWMSENDICONICLIVEPREVIEWBITMAP</c>: captures at native resolution and submits.
        /// </summary>
        internal static void HandleSendIconicLivePreviewBitmap(
            IntPtr mainHwnd,
            Window? mainWindow,
            Window? viewportWindow)
        {
            IntPtr hBitmap = CreateScreenshotComposite(mainWindow, viewportWindow, 0, 0, scale: false);
            if (hBitmap == IntPtr.Zero)
            {
                return;
            }

            try
            {
                DwmSetIconicLivePreviewBitmap(mainHwnd, hBitmap, IntPtr.Zero, 0);
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        /// <summary>
        /// Renders the viewport window into a 32-bit DIB section.
        /// Falls back to the main window when the viewport is unavailable.
        /// </summary>
        private static IntPtr CreateScreenshotComposite(
            Window? mainWindow,
            Window? viewportWindow,
            int maxWidth,
            int maxHeight,
            bool scale)
        {
            if (mainWindow == null)
            {
                return IntPtr.Zero;
            }

            IntPtr mainHwnd = new WindowInteropHelper(mainWindow).Handle;
            if (mainHwnd == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            Window previewWindow = viewportWindow?.IsVisible == true ? viewportWindow! : mainWindow;
            FrameworkElement previewVisual = ResolvePreviewVisual(previewWindow);
            double sourceWidth = Math.Max(1d, previewVisual.ActualWidth);
            double sourceHeight = Math.Max(1d, previewVisual.ActualHeight);

            int destW;
            int destH;
            int drawX = 0;
            int drawY = 0;
            int drawW;
            int drawH;
            if (scale && maxWidth > 0 && maxHeight > 0)
            {
                double ratio = Math.Min(maxWidth / sourceWidth, maxHeight / sourceHeight);
                destW = maxWidth;
                destH = maxHeight;
                drawW = Math.Max(1, (int)(sourceWidth * ratio));
                drawH = Math.Max(1, (int)(sourceHeight * ratio));
                drawX = (destW - drawW) / 2;
                drawY = (destH - drawH) / 2;
            }
            else
            {
                destW = Math.Max(1, (int)Math.Ceiling(sourceWidth));
                destH = Math.Max(1, (int)Math.Ceiling(sourceHeight));
                drawW = destW;
                drawH = destH;
            }

            byte[] pixels = RenderPreviewPixels(previewVisual, destW, destH, drawX, drawY, drawW, drawH);
            if (pixels.Length == 0)
            {
                return IntPtr.Zero;
            }

            BitmapInfoHeader bmi = new BitmapInfoHeader
            {
                BiSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                BiWidth = destW,
                BiHeight = -destH, // negative = top-down
                BiPlanes = 1,
                BiBitCount = 32,
                BiCompression = 0, // BI_RGB
            };

            IntPtr bitmap = CreateDIBSection(IntPtr.Zero, ref bmi, DibRgbColors, out IntPtr pvBits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || pvBits == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            try
            {
                Marshal.Copy(pixels, 0, pvBits, pixels.Length);
                return bitmap;
            }
            catch
            {
                DeleteObject(bitmap);
                return IntPtr.Zero;
            }
        }

        private static byte[] RenderPreviewPixels(
            FrameworkElement previewVisual,
            int destW,
            int destH,
            int drawX,
            int drawY,
            int drawW,
            int drawH)
        {
            try
            {
                DrawingVisual visual = new DrawingVisual();
                using (DrawingContext context = visual.RenderOpen())
                {
                    context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, destW, destH));
                    VisualBrush brush = new VisualBrush(previewVisual)
                    {
                        Stretch = Stretch.Fill,
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Viewbox = new Rect(0, 0, previewVisual.ActualWidth, previewVisual.ActualHeight)
                    };
                    context.DrawRectangle(brush, null, new Rect(drawX, drawY, drawW, drawH));
                }

                RenderTargetBitmap renderTarget = new RenderTargetBitmap(
                    destW,
                    destH,
                    96,
                    96,
                    PixelFormats.Pbgra32);
                renderTarget.Render(visual);

                int stride = destW * 4;
                byte[] pixels = new byte[stride * destH];
                renderTarget.CopyPixels(pixels, stride, 0);
                return pixels;
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        private static FrameworkElement ResolvePreviewVisual(Window previewWindow)
        {
            return previewWindow;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetIconicThumbnail(IntPtr hwnd, IntPtr hbmp, uint dwSITFlags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetIconicLivePreviewBitmap(IntPtr hwnd, IntPtr hbmp, IntPtr pptClient, uint dwSITFlags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmInvalidateIconicBitmaps(IntPtr hwnd);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(
            IntPtr hdc, ref BitmapInfoHeader pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint BiSize;
            public int BiWidth;
            public int BiHeight;
            public ushort BiPlanes;
            public ushort BiBitCount;
            public uint BiCompression;
            public uint BiSizeImage;
            public int BiXPelsPerMeter;
            public int BiYPelsPerMeter;
            public uint BiClrUsed;
            public uint BiClrImportant;
        }
    }
}
