using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace OceanyaClient.Features.Ui
{
    /// <summary>
    /// Owns the global Oceanya UI scale factor used by <see cref="GenericOceanyaWindow"/> shells.
    /// Automatic mode derives the factor from the monitor a window lives on so very high resolution
    /// displays do not render the fixed-DIP layouts unusably small.
    /// </summary>
    public static class UiScaleManager
    {
        /// <summary>
        /// Raised when the effective scale settings change and every shell must re-apply its scale.
        /// </summary>
        public static event EventHandler? ScaleChanged;

        private static UiScaleMode? livePreviewMode;
        private static double? livePreviewManualScale;

        /// <summary>
        /// Gets the currently selected scale mode, including any unsaved live preview.
        /// </summary>
        public static UiScaleMode Mode => livePreviewMode ?? SaveFile.Data.UiScaleMode;

        /// <summary>
        /// Gets the currently selected manual scale factor, including any unsaved live preview.
        /// </summary>
        public static double ManualScale => UiScaleMath.ClampScale(livePreviewManualScale ?? SaveFile.Data.UiScaleFactor);

        /// <summary>
        /// Applies unsaved scale settings so the user can see them while dragging a settings slider.
        /// </summary>
        /// <param name="mode">Previewed scale mode.</param>
        /// <param name="manualScale">Previewed manual scale factor.</param>
        public static void SetLivePreview(UiScaleMode mode, double manualScale)
        {
            livePreviewMode = mode;
            livePreviewManualScale = UiScaleMath.ClampScale(manualScale);
            RaiseScaleChanged();
        }

        /// <summary>
        /// Drops any live preview and returns every shell to the persisted scale settings.
        /// </summary>
        public static void ClearLivePreview()
        {
            if (livePreviewMode == null && livePreviewManualScale == null)
            {
                return;
            }

            livePreviewMode = null;
            livePreviewManualScale = null;
            RaiseScaleChanged();
        }

        /// <summary>
        /// Gets a scale that can be used before a specific window exists, based on the primary monitor.
        /// </summary>
        public static double GlobalScale => Mode == UiScaleMode.Manual
            ? ManualScale
            : ResolveAutomaticScaleForPrimaryMonitor();

        /// <summary>
        /// Persists new scale settings and notifies every shell window to re-apply them.
        /// </summary>
        /// <param name="mode">Scale mode to use.</param>
        /// <param name="manualScale">Manual scale factor, used when <paramref name="mode"/> is Manual.</param>
        public static void ApplySettings(UiScaleMode mode, double manualScale)
        {
            livePreviewMode = null;
            livePreviewManualScale = null;
            SaveFile.Data.UiScaleMode = mode;
            SaveFile.Data.UiScaleFactor = UiScaleMath.ClampScale(manualScale);
            SaveFile.Save();
            RaiseScaleChanged();
        }

        /// <summary>
        /// Notifies every shell window to re-apply the current scale without persisting anything.
        /// Use for live previews while a settings slider is being dragged.
        /// </summary>
        public static void RaiseScaleChanged()
        {
            ScaleChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Resolves the effective scale factor for a specific window.
        /// </summary>
        /// <param name="window">Window whose monitor should drive automatic scaling.</param>
        /// <returns>The scale factor to apply to the window shell.</returns>
        public static double ResolveScaleForWindow(Window? window)
        {
            if (Mode == UiScaleMode.Manual)
            {
                return ManualScale;
            }

            if (window == null)
            {
                return ResolveAutomaticScaleForPrimaryMonitor();
            }

            if (!TryGetLogicalWorkAreaSize(window, out double logicalWidth, out double logicalHeight))
            {
                return ResolveAutomaticScaleForPrimaryMonitor();
            }

            return UiScaleMath.ResolveAutomaticScale(logicalWidth, logicalHeight);
        }

        /// <summary>
        /// Gets the usable (work area) size of the monitor hosting a window, in device independent pixels.
        /// Falls back to the primary monitor work area before the window has a handle.
        /// </summary>
        /// <param name="window">Window whose monitor should be measured.</param>
        /// <returns>Work area width and height in device independent pixels.</returns>
        public static (double Width, double Height) GetLogicalWorkAreaSize(Window? window)
        {
            if (window != null && TryGetLogicalWorkAreaSize(window, out double logicalWidth, out double logicalHeight))
            {
                return (logicalWidth, logicalHeight);
            }

            Rect workArea = SystemParameters.WorkArea;
            return (workArea.Width, workArea.Height);
        }

        private static double ResolveAutomaticScaleForPrimaryMonitor()
        {
            Rect workArea = SystemParameters.WorkArea;
            return UiScaleMath.ResolveAutomaticScale(workArea.Width, workArea.Height);
        }

        private static bool TryGetLogicalWorkAreaSize(Window window, out double logicalWidth, out double logicalHeight)
        {
            logicalWidth = 0;
            logicalHeight = 0;

            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            MonitorInfo monitorInfo = new MonitorInfo();
            monitorInfo.cbSize = Marshal.SizeOf(monitorInfo);
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return false;
            }

            double devicePixelWidth = Math.Abs(monitorInfo.rcWork.right - monitorInfo.rcWork.left);
            double devicePixelHeight = Math.Abs(monitorInfo.rcWork.bottom - monitorInfo.rcWork.top);
            if (devicePixelWidth <= 0 || devicePixelHeight <= 0)
            {
                return false;
            }

            (double dpiScaleX, double dpiScaleY) = GetWindowDpiScale(window);
            logicalWidth = devicePixelWidth / dpiScaleX;
            logicalHeight = devicePixelHeight / dpiScaleY;
            return true;
        }

        private static (double ScaleX, double ScaleY) GetWindowDpiScale(Window window)
        {
            PresentationSource? source = PresentationSource.FromVisual(window);
            if (source?.CompositionTarget != null)
            {
                Matrix transform = source.CompositionTarget.TransformToDevice;
                double scaleX = transform.M11 > 0 ? transform.M11 : 1d;
                double scaleY = transform.M22 > 0 ? transform.M22 : 1d;
                return (scaleX, scaleY);
            }

            DpiScale dpi = VisualTreeHelper.GetDpi(window);
            double fallbackX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1d;
            double fallbackY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1d;
            return (fallbackX, fallbackY);
        }

        private const int MonitorDefaultToNearest = 0x00000002;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int cbSize;
            public NativeRect rcMonitor;
            public NativeRect rcWork;
            public int dwFlags;
        }
    }
}
