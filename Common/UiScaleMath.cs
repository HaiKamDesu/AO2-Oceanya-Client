using System;

namespace OceanyaClient
{
    /// <summary>
    /// Scaling mode used for the Oceanya window shell.
    /// </summary>
    public enum UiScaleMode
    {
        /// <summary>
        /// Derive the scale from the monitor the window currently lives on.
        /// </summary>
        Automatic = 0,

        /// <summary>
        /// Use the user-selected fixed scale factor.
        /// </summary>
        Manual = 1
    }

    /// <summary>
    /// Pure scaling math shared by the WPF shell and persistence layers.
    /// Kept free of WPF dependencies so it stays unit testable.
    /// </summary>
    public static class UiScaleMath
    {
        /// <summary>
        /// Reference logical width the current fixed-DIP layouts were designed against.
        /// </summary>
        public const double ReferenceLogicalWidth = 1920d;

        /// <summary>
        /// Reference logical height the current fixed-DIP layouts were designed against.
        /// </summary>
        public const double ReferenceLogicalHeight = 1080d;

        /// <summary>
        /// Smallest scale a user may select.
        /// </summary>
        public const double MinimumScale = 0.75d;

        /// <summary>
        /// Largest scale a user may select.
        /// </summary>
        public const double MaximumScale = 3d;

        /// <summary>
        /// Granularity automatic scaling snaps to, so small monitor differences do not cause churn.
        /// </summary>
        public const double AutomaticScaleStep = 0.25d;

        /// <summary>
        /// Clamps an arbitrary scale value into the supported range, falling back to 1.0 for invalid input.
        /// </summary>
        /// <param name="scale">Requested scale factor.</param>
        /// <returns>A finite scale within the supported range.</returns>
        public static double ClampScale(double scale)
        {
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            {
                return 1d;
            }

            return Math.Clamp(scale, MinimumScale, MaximumScale);
        }

        /// <summary>
        /// Derives an automatic scale from the usable logical (DIP) size of the monitor hosting a window.
        /// Never shrinks below 1.0: automatic scaling exists to make the client usable on very large
        /// displays, not to squeeze it on small ones.
        /// </summary>
        /// <param name="logicalWorkAreaWidth">Monitor work-area width in device independent pixels.</param>
        /// <param name="logicalWorkAreaHeight">Monitor work-area height in device independent pixels.</param>
        /// <returns>The automatic scale factor for that monitor.</returns>
        public static double ResolveAutomaticScale(double logicalWorkAreaWidth, double logicalWorkAreaHeight)
        {
            if (!IsUsableLength(logicalWorkAreaWidth) || !IsUsableLength(logicalWorkAreaHeight))
            {
                return 1d;
            }

            double widthRatio = logicalWorkAreaWidth / ReferenceLogicalWidth;
            double heightRatio = logicalWorkAreaHeight / ReferenceLogicalHeight;
            double rawScale = Math.Min(widthRatio, heightRatio);
            double steppedScale = Math.Floor(rawScale / AutomaticScaleStep) * AutomaticScaleStep;
            return ClampScale(Math.Max(1d, steppedScale));
        }

        /// <summary>
        /// Resolves the largest scale whose resulting window length still fits the available space.
        /// Window length is <c>content * scale + scaledChrome * scale + fixedChrome</c>, so the
        /// inverse is <c>(available - fixedChrome) / (content + scaledChrome)</c>.
        /// </summary>
        /// <param name="contentLength">Unscaled content length along this axis.</param>
        /// <param name="scaledChromeLength">Chrome that scales with the content (header, body margin).</param>
        /// <param name="fixedChromeLength">Chrome that never scales (the frame border).</param>
        /// <param name="availableLength">Usable monitor length along this axis.</param>
        /// <returns>The maximum fitting scale, or positive infinity when the axis cannot constrain anything.</returns>
        public static double ResolveMaximumFittingScale(
            double contentLength,
            double scaledChromeLength,
            double fixedChromeLength,
            double availableLength)
        {
            double scalableLength = contentLength + scaledChromeLength;
            if (!IsUsableLength(scalableLength) || !IsUsableLength(availableLength))
            {
                return double.PositiveInfinity;
            }

            double usableLength = availableLength - fixedChromeLength;
            if (usableLength <= 0)
            {
                return double.PositiveInfinity;
            }

            return usableLength / scalableLength;
        }

        /// <summary>
        /// Resolves the scale a window length represents, used when a drag resize rescales the
        /// content instead of stretching it.
        /// </summary>
        /// <param name="windowLength">Window length along this axis.</param>
        /// <param name="contentLength">Unscaled content length along this axis.</param>
        /// <param name="scaledChromeLength">Chrome that scales with the content.</param>
        /// <param name="fixedChromeLength">Chrome that never scales.</param>
        /// <returns>The scale that window length corresponds to, or 1.0 when it cannot be derived.</returns>
        public static double ResolveScaleFromWindowLength(
            double windowLength,
            double contentLength,
            double scaledChromeLength,
            double fixedChromeLength)
        {
            double scalableLength = contentLength + scaledChromeLength;
            if (!IsUsableLength(scalableLength) || !IsUsableLength(windowLength))
            {
                return 1d;
            }

            return (windowLength - fixedChromeLength) / scalableLength;
        }

        /// <summary>
        /// Converts a persisted window length captured under one scale into the equivalent length
        /// for the currently active scale.
        /// </summary>
        /// <param name="savedLength">Persisted window length in device independent pixels.</param>
        /// <param name="savedScale">Scale that was active when the length was captured. Values &lt;= 0 mean "unknown" (legacy save) and are treated as 1.0.</param>
        /// <param name="currentScale">Scale that is active now.</param>
        /// <returns>The rescaled length, or the original value when rescaling is not applicable.</returns>
        public static double RescaleSavedLength(double savedLength, double savedScale, double currentScale)
        {
            if (!IsUsableLength(savedLength))
            {
                return savedLength;
            }

            double normalizedSavedScale = savedScale > 0 && !double.IsNaN(savedScale) && !double.IsInfinity(savedScale)
                ? savedScale
                : 1d;
            double normalizedCurrentScale = ClampScale(currentScale);

            if (Math.Abs(normalizedSavedScale - normalizedCurrentScale) < 0.0001d)
            {
                return savedLength;
            }

            return savedLength / normalizedSavedScale * normalizedCurrentScale;
        }

        private static bool IsUsableLength(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }
    }
}
