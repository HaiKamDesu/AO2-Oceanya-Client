using System;
using System.Windows;

namespace OceanyaClient.Features.Ui
{
    /// <summary>
    /// Saves and restores window sizes in a scale independent way.
    /// </summary>
    /// <remarks>
    /// Hosted Oceanya windows are sized as <c>content * scale + chrome(scale)</c>, so persisting the
    /// raw window size means the saved number only makes sense at the scale that produced it. Restoring
    /// such a value at another scale wrote a wrong content size into the hosted control, which showed up
    /// as "the scale is not applied to the inner control until I resize the window". These helpers store
    /// the hosted content size instead, which is the same number at every scale, and convert legacy
    /// window-space states on read.
    /// </remarks>
    public static class WindowStatePersistence
    {
        /// <summary>
        /// Gets a value indicating whether a window's size should be persisted at all.
        /// </summary>
        /// <param name="window">Window to test.</param>
        /// <returns>False for windows whose size is derived from the UI scale rather than chosen by the user.</returns>
        public static bool ShouldPersistSize(Window window)
        {
            if (window is not GenericOceanyaWindow shellWindow)
            {
                return true;
            }

            // Resize-scaling windows encode "scale" in their size, and the scale is already persisted
            // globally. Restoring a size here would fight that and double-apply the scale.
            if (shellWindow.IsResizeScalingEnabled)
            {
                return false;
            }

            // Windows with their own saved state must have exactly one owner for their size.
            return shellWindow.BodyContent is not OceanyaWindowContentControl content || !content.ManagesOwnWindowSize;
        }

        /// <summary>
        /// Records the current size of a window into a state object, in content space when possible.
        /// </summary>
        /// <param name="window">Window being captured.</param>
        /// <param name="state">State object to fill in. Left, Top and IsMaximized stay the caller's job.</param>
        /// <param name="windowWidth">Window width to record.</param>
        /// <param name="windowHeight">Window height to record.</param>
        public static void CaptureSize(Window window, VisualizerWindowState state, double windowWidth, double windowHeight)
        {
            if (window is GenericOceanyaWindow shellWindow && shellWindow.BodyContent is FrameworkElement content)
            {
                double scale = UiScaleMath.ClampScale(shellWindow.ContentScale);
                (double horizontalOffset, double verticalOffset) =
                    GenericOceanyaWindow.GetChromeOffsets(shellWindow.BodyMargin, scale);

                double contentWidth = IsUsableLength(content.Width)
                    ? content.Width
                    : (windowWidth - horizontalOffset) / scale;
                double contentHeight = IsUsableLength(content.Height)
                    ? content.Height
                    : (windowHeight - verticalOffset) / scale;

                state.Width = contentWidth;
                state.Height = contentHeight;
                state.IsContentSpace = true;
                state.UiScale = scale;
                return;
            }

            state.Width = windowWidth;
            state.Height = windowHeight;
            state.IsContentSpace = false;
            state.UiScale = 1d;
        }

        /// <summary>
        /// Applies a saved size to a window, converting legacy window-space states and clamping the
        /// result so the scaled window still fits the monitor.
        /// </summary>
        /// <param name="window">Window to resize.</param>
        /// <param name="state">Saved state to apply.</param>
        public static void ApplySize(Window window, VisualizerWindowState state)
        {
            if (window is GenericOceanyaWindow shellWindow && shellWindow.BodyContent is FrameworkElement content)
            {
                double scale = UiScaleMath.ClampScale(shellWindow.ContentScale);
                (double contentWidth, double contentHeight) = ResolveContentSize(shellWindow, state);
                (double horizontalOffset, double verticalOffset) =
                    GenericOceanyaWindow.GetChromeOffsets(shellWindow.BodyMargin, scale);
                (double availableWidth, double availableHeight) = UiScaleManager.GetLogicalWorkAreaSize(window);

                double maximumContentWidth = (availableWidth - horizontalOffset) / scale;
                double maximumContentHeight = (availableHeight - verticalOffset) / scale;

                if (IsUsableLength(contentWidth))
                {
                    double resolvedWidth = Math.Min(contentWidth, Math.Max(content.MinWidth, maximumContentWidth));
                    content.SetCurrentValue(FrameworkElement.WidthProperty, Math.Max(content.MinWidth, resolvedWidth));
                }

                if (IsUsableLength(contentHeight))
                {
                    double resolvedHeight = Math.Min(contentHeight, Math.Max(content.MinHeight, maximumContentHeight));
                    content.SetCurrentValue(FrameworkElement.HeightProperty, Math.Max(content.MinHeight, resolvedHeight));
                }

                return;
            }

            (double availableWindowWidth, double availableWindowHeight) = UiScaleManager.GetLogicalWorkAreaSize(window);
            if (IsUsableLength(state.Width))
            {
                window.Width = Math.Max(window.MinWidth, Math.Min(state.Width, availableWindowWidth));
            }

            if (IsUsableLength(state.Height))
            {
                window.Height = Math.Max(window.MinHeight, Math.Min(state.Height, availableWindowHeight));
            }
        }

        /// <summary>
        /// Resolves the size a saved state describes, in hosted content space.
        /// </summary>
        /// <param name="shellWindow">Host window the state belongs to.</param>
        /// <param name="state">Saved state.</param>
        /// <returns>Content width and height in device independent pixels.</returns>
        public static (double Width, double Height) ResolveContentSize(
            GenericOceanyaWindow shellWindow,
            VisualizerWindowState state)
        {
            if (state.IsContentSpace)
            {
                return (state.Width, state.Height);
            }

            // Legacy state: the numbers are window sizes captured at state.UiScale (0 means pre-scaling,
            // which was always 1.0).
            double savedScale = state.UiScale > 0 ? UiScaleMath.ClampScale(state.UiScale) : 1d;
            (double horizontalOffset, double verticalOffset) =
                GenericOceanyaWindow.GetChromeOffsets(shellWindow.BodyMargin, savedScale);

            return (
                (state.Width - horizontalOffset) / savedScale,
                (state.Height - verticalOffset) / savedScale);
        }

        private static bool IsUsableLength(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }
    }
}
