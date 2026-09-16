using System;

namespace OceanyaClient.Features.Theme
{
    /// <summary>
    /// Decides whether a hosted list surface (area navigator, music list) shows its full decoration or a
    /// compact version, without the two states being able to oscillate.
    /// </summary>
    /// <remarks>
    /// The decision is driven by the surface's own measured size from its <c>SizeChanged</c> handler, and
    /// acting on it changes that same size - collapsing the title rows and dropping the frame padding from
    /// 10 to 2 moves the surface by ~16px per axis. With a single bare threshold that is a feedback loop:
    /// at a size just above the threshold the surface un-compacts, the padding change pushes it back under,
    /// it compacts again, and the panel visibly flickers between two layouts forever (usually noticed as the
    /// vertical scrollbar flashing in and out). The hysteresis band below has to be wider than the size
    /// change that toggling causes, so a surface that just left compact mode cannot immediately fall back in.
    /// </remarks>
    public static class HostedListCompactMode
    {
        /// <summary>
        /// How far past a threshold a surface must grow before it leaves compact mode.
        /// </summary>
        /// <remarks>
        /// Must exceed the largest size swing that toggling compact mode causes. The frame padding accounts
        /// for 16px per axis; 32 leaves the same margin again on top.
        /// </remarks>
        public const double HysteresisPixels = 32d;

        /// <summary>Surface height below which decoration (title, current-area line) is hidden.</summary>
        public const double DecorationHeightThreshold = 260d;

        /// <summary>Surface width below which decoration is hidden.</summary>
        public const double DecorationWidthThreshold = 220d;

        /// <summary>Surface height below which the command row is hidden.</summary>
        public const double CommandsHeightThreshold = 180d;

        /// <summary>Surface width below which the command row is hidden.</summary>
        public const double CommandsWidthThreshold = 170d;

        /// <summary>
        /// Resolves whether a surface of this size should be compact.
        /// </summary>
        /// <param name="isCurrentlyCompact">Whether the surface is compact right now.</param>
        /// <param name="height">Measured surface height.</param>
        /// <param name="width">Measured surface width.</param>
        /// <param name="heightThreshold">Height below which the surface compacts.</param>
        /// <param name="widthThreshold">Width below which the surface compacts.</param>
        /// <returns>True when the surface should be compact.</returns>
        public static bool ShouldBeCompact(
            bool isCurrentlyCompact,
            double height,
            double width,
            double heightThreshold,
            double widthThreshold)
        {
            if (isCurrentlyCompact)
            {
                // Leaving compact mode needs clear headroom on BOTH axes: it is the act of leaving that
                // grows the content, and either axis falling back under its threshold restarts the flicker.
                return !(height >= heightThreshold + HysteresisPixels
                    && width >= widthThreshold + HysteresisPixels);
            }

            return height < heightThreshold || width < widthThreshold;
        }
    }
}
