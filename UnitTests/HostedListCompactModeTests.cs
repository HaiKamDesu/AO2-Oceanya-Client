using NUnit.Framework;
using OceanyaClient.Features.Theme;

namespace UnitTests
{
    /// <summary>
    /// Covers the compact-mode decision for hosted list panels (area navigator, music list).
    /// </summary>
    /// <remarks>
    /// The bug these guard against: the decision runs from the surface's own SizeChanged and toggling it
    /// resizes that surface, so a bare threshold made the panel flicker between two layouts forever at
    /// certain sizes.
    /// </remarks>
    [TestFixture]
    public class HostedListCompactModeTests
    {
        private const double DecorationHeight = HostedListCompactMode.DecorationHeightThreshold;
        private const double DecorationWidth = HostedListCompactMode.DecorationWidthThreshold;

        [Test]
        public void ComfortablySizedSurfaceIsNotCompact()
        {
            bool compact = HostedListCompactMode.ShouldBeCompact(
                isCurrentlyCompact: false, height: 400, width: 400, DecorationHeight, DecorationWidth);

            Assert.That(compact, Is.False);
        }

        [Test]
        public void SmallSurfaceBecomesCompact()
        {
            bool compact = HostedListCompactMode.ShouldBeCompact(
                isCurrentlyCompact: false, height: 209, width: 182, DecorationHeight, DecorationWidth);

            Assert.That(compact, Is.True, "GrayGarden's 182x209 area list must compact.");
        }

        [Test]
        public void EitherAxisBeingTooSmallCompacts()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    HostedListCompactMode.ShouldBeCompact(false, DecorationHeight - 1, 400, DecorationHeight, DecorationWidth),
                    Is.True,
                    "Short but wide still compacts.");
                Assert.That(
                    HostedListCompactMode.ShouldBeCompact(false, 400, DecorationWidth - 1, DecorationHeight, DecorationWidth),
                    Is.True,
                    "Narrow but tall still compacts.");
            });
        }

        [Test]
        public void CompactSurfaceStaysCompactJustPastTheThreshold()
        {
            // The size a surface lands on right after un-compacting: over the raw threshold, but only by less
            // than the layout change that un-compacting itself caused.
            bool compact = HostedListCompactMode.ShouldBeCompact(
                isCurrentlyCompact: true,
                height: DecorationHeight + 4,
                width: DecorationWidth + 4,
                DecorationHeight,
                DecorationWidth);

            Assert.That(compact, Is.True, "Without hysteresis this is where the two layouts alternate.");
        }

        [Test]
        public void CompactSurfaceLeavesCompactOnceClearlyLargeEnough()
        {
            bool compact = HostedListCompactMode.ShouldBeCompact(
                isCurrentlyCompact: true,
                height: DecorationHeight + HostedListCompactMode.HysteresisPixels,
                width: DecorationWidth + HostedListCompactMode.HysteresisPixels,
                DecorationHeight,
                DecorationWidth);

            Assert.That(compact, Is.False);
        }

        [Test]
        public void LeavingCompactNeedsHeadroomOnBothAxes()
        {
            bool compact = HostedListCompactMode.ShouldBeCompact(
                isCurrentlyCompact: true,
                height: DecorationHeight + HostedListCompactMode.HysteresisPixels,
                width: DecorationWidth + 2,
                DecorationHeight,
                DecorationWidth);

            Assert.That(compact, Is.True, "One axis still tight must keep the surface compact.");
        }

        /// <summary>
        /// Reproduces the reported flicker: a surface that toggles mode changes its own measured size, and
        /// feeding that changed size straight back in must settle instead of alternating.
        /// </summary>
        [Test]
        public void ResizeFeedbackSettlesInsteadOfOscillating()
        {
            // Toggling compact mode swings the frame padding by 16px per axis.
            const double ToggleSwing = 16d;
            double height = DecorationHeight + 8;
            double width = DecorationWidth + 8;
            bool compact = true;

            bool[] observed = new bool[8];
            for (int i = 0; i < observed.Length; i++)
            {
                bool next = HostedListCompactMode.ShouldBeCompact(compact, height, width, DecorationHeight, DecorationWidth);
                if (next != compact)
                {
                    // Un-compacting eats padding back out of the surface; compacting gives it back.
                    height += next ? ToggleSwing : -ToggleSwing;
                    width += next ? ToggleSwing : -ToggleSwing;
                }

                compact = next;
                observed[i] = compact;
            }

            Assert.That(observed, Is.All.EqualTo(observed[0]), "The state must settle, not alternate.");
        }
    }
}
