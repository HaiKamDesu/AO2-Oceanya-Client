using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OceanyaClient.Features.Viewport
{
    /// <summary>
    /// Manages DWM custom iconic thumbnail bitmaps that composite the main and viewport
    /// windows into a single taskbar preview image.
    /// </summary>
    internal static class ViewportThumbnailCompositor
    {
        private const int DwmwaForceIconicRepresentation = 7;
        private const int DwmwaHasIconicBitmap = 10;
        private const uint Srccopy = 0x00CC0020;
        private const uint Blackness = 0x00000042;
        private const uint DibRgbColors = 0;

        /// <summary>Enables custom iconic thumbnail for the specified HWND.</summary>
        internal static void Activate(IntPtr hwnd)
        {
            int enabled = 1;
            DwmSetWindowAttribute(hwnd, DwmwaForceIconicRepresentation, ref enabled, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaHasIconicBitmap, ref enabled, sizeof(int));
        }

        /// <summary>Disables custom iconic thumbnail for the specified HWND.</summary>
        internal static void Deactivate(IntPtr hwnd)
        {
            int disabled = 0;
            DwmSetWindowAttribute(hwnd, DwmwaForceIconicRepresentation, ref disabled, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaHasIconicBitmap, ref disabled, sizeof(int));
        }

        /// <summary>
        /// Proactively captures and submits a composite thumbnail to DWM. Call this after
        /// <see cref="Activate"/> so the loading spinner is replaced immediately.
        /// </summary>
        internal static void SubmitIconicThumbnail(IntPtr hwnd, Window? mainWindow, Window? viewportWindow)
        {
            IntPtr hBitmap = CreateScreenshotComposite(mainWindow, viewportWindow, 800, 600, scale: true);
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
        /// Handles <c>WM_DWMSENDICONICTHUMBNAIL</c>: captures the composite screenshot and submits it.
        /// </summary>
        internal static void HandleSendIconicThumbnail(
            IntPtr mainHwnd,
            Window? mainWindow,
            Window? viewportWindow,
            IntPtr wParam)
        {
            int maxWidth = Math.Max(100, (int)((long)wParam & 0xFFFF));
            int maxHeight = Math.Max(100, (int)(((long)wParam >> 16) & 0xFFFF));

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
        /// Captures the combined bounding box of both windows from the screen using a 32-bit DIB section.
        /// The alpha channel is explicitly set to 255 on all pixels because GDI does not write alpha
        /// during StretchBlt, and DWM treats zero-alpha pixels as fully transparent (invisible).
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

            bool hasViewport = viewportWindow?.IsVisible == true;
            IntPtr viewportHwnd = hasViewport ? new WindowInteropHelper(viewportWindow!).Handle : IntPtr.Zero;

            if (!GetWindowRect(mainHwnd, out NativeRect mainRect))
            {
                return IntPtr.Zero;
            }

            NativeRect viewportRect = mainRect;
            if (hasViewport && !GetWindowRect(viewportHwnd, out viewportRect))
            {
                hasViewport = false;
                viewportRect = mainRect;
            }

            int left = hasViewport ? Math.Min(mainRect.Left, viewportRect.Left) : mainRect.Left;
            int top = hasViewport ? Math.Min(mainRect.Top, viewportRect.Top) : mainRect.Top;
            int right = hasViewport ? Math.Max(mainRect.Right, viewportRect.Right) : mainRect.Right;
            int bottom = hasViewport ? Math.Max(mainRect.Bottom, viewportRect.Bottom) : mainRect.Bottom;

            int srcW = Math.Max(1, right - left);
            int srcH = Math.Max(1, bottom - top);

            int destW;
            int destH;
            if (scale && maxWidth > 0 && maxHeight > 0)
            {
                double ratio = Math.Min((double)maxWidth / srcW, (double)maxHeight / srcH);
                destW = Math.Max(1, (int)(srcW * ratio));
                destH = Math.Max(1, (int)(srcH * ratio));
            }
            else
            {
                destW = srcW;
                destH = srcH;
            }

            // CreateDIBSection gives us direct access to the pixel buffer so we can fix the alpha channel.
            // DWM requires 32bpp BGRA with pre-multiplied alpha; since the content is fully opaque,
            // setting alpha=255 everywhere is correct and no RGB pre-multiplication is needed.
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

            IntPtr screenDC = IntPtr.Zero;
            IntPtr memDC = IntPtr.Zero;
            IntPtr oldObj = IntPtr.Zero;
            bool success = false;

            try
            {
                screenDC = GetDC(IntPtr.Zero);
                if (screenDC == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }

                memDC = CreateCompatibleDC(screenDC);
                oldObj = SelectObject(memDC, bitmap);

                PatBlt(memDC, 0, 0, destW, destH, Blackness);
                StretchBlt(memDC, 0, 0, destW, destH, screenDC, left, top, srcW, srcH, Srccopy);

                // Force alpha=255 on every pixel so DWM sees fully-opaque content.
                int byteCount = destW * destH * 4;
                byte[] pixels = new byte[byteCount];
                Marshal.Copy(pvBits, pixels, 0, byteCount);
                for (int i = 3; i < byteCount; i += 4)
                {
                    pixels[i] = 255;
                }

                Marshal.Copy(pixels, 0, pvBits, byteCount);

                success = true;
                return bitmap;
            }
            catch
            {
                return IntPtr.Zero;
            }
            finally
            {
                if (oldObj != IntPtr.Zero && memDC != IntPtr.Zero)
                {
                    SelectObject(memDC, oldObj);
                }

                if (memDC != IntPtr.Zero)
                {
                    DeleteDC(memDC);
                }

                if (screenDC != IntPtr.Zero)
                {
                    ReleaseDC(IntPtr.Zero, screenDC);
                }

                if (!success && bitmap != IntPtr.Zero)
                {
                    DeleteObject(bitmap);
                }
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetIconicThumbnail(IntPtr hwnd, IntPtr hbmp, uint dwSITFlags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetIconicLivePreviewBitmap(IntPtr hwnd, IntPtr hbmp, IntPtr pptClient, uint dwSITFlags);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(
            IntPtr hdc, ref BitmapInfoHeader pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool StretchBlt(
            IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
            IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, uint rop);

        [DllImport("gdi32.dll")]
        private static extern bool PatBlt(IntPtr hdc, int nXLeft, int nYTop, int nWidth, int nHeight, uint dwRop);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

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
