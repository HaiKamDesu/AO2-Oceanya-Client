using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Features.Ui;

namespace UnitTests
{
    /// <summary>
    /// A shell window's saved size must land on whichever of the window and its body actually drives the
    /// other.
    /// </summary>
    /// <remarks>
    /// Only <see cref="OceanyaWindowManager"/> attaches a sizing controller, and only for an
    /// <see cref="OceanyaWindowContentControl"/>. A shell built directly - every dialog from the character
    /// creator's <c>CreateEmoteDialog</c> - has none, so a size written onto its body is never propagated to
    /// the window: the body renders at that size inside a window that keeps its own, and the overflow is
    /// clipped. That is how the Solid Color Picker opened with its Save/Cancel row below the bottom edge,
    /// unreachable until the window was dragged much larger.
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class DialogSizePersistenceTests
    {
        [SetUp]
        public void SetUp()
        {
            _ = WpfTestApplicationContext.EnsureCreated();
        }

        [Test]
        public void ApplySize_OnShellWithoutSizingController_SizesTheWindowAndLeavesTheBodyFree()
        {
            GenericOceanyaWindow window = new GenericOceanyaWindow
            {
                Title = "Size Probe",
                BodyMargin = new Thickness(0)
            };
            Border body = new Border();
            window.BodyContent = body;

            try
            {
                Assert.That(
                    window.HasHostedContentSizing,
                    Is.False,
                    "a directly constructed shell has no sizing controller");

                WindowStatePersistence.ApplySize(
                    window,
                    new VisualizerWindowState { Width = 400, Height = 300, IsContentSpace = true, UiScale = 1d });

                Assert.That(
                    double.IsNaN(body.Width) && double.IsNaN(body.Height),
                    Is.True,
                    "sizing the body would only make it overflow a window that never follows it");

                (double horizontalOffset, double verticalOffset) =
                    GenericOceanyaWindow.GetChromeOffsets(window.BodyMargin, window.ContentScale);
                Assert.That(window.Width, Is.EqualTo((400 * window.ContentScale) + horizontalOffset).Within(0.5));
                Assert.That(window.Height, Is.EqualTo((300 * window.ContentScale) + verticalOffset).Within(0.5));
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void ApplySize_OnHostedShell_StillSizesTheContent()
        {
            SizeProbeContent content = new SizeProbeContent { Width = 500, Height = 320 };
            GenericOceanyaWindow window = (GenericOceanyaWindow)OceanyaWindowManager.CreateWindow(content);

            try
            {
                Assert.That(window.HasHostedContentSizing, Is.True);

                WindowStatePersistence.ApplySize(
                    window,
                    new VisualizerWindowState { Width = 400, Height = 300, IsContentSpace = true, UiScale = 1d });

                Assert.That(content.Width, Is.EqualTo(400).Within(0.5));
                Assert.That(content.Height, Is.EqualTo(300).Within(0.5));
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>
        /// A dialog states the size of its CONTENT; the shell owes it a window big enough to hold that
        /// content at the scale the shell itself resolves, which the caller cannot know up front.
        /// </summary>
        [Test]
        public void ContentSizeRequest_ConvertsToWindowSizeAtTheCurrentScale()
        {
            GenericOceanyaWindow window = new GenericOceanyaWindow
            {
                Title = "Scale Probe",
                BodyMargin = new Thickness(0)
            };
            window.BodyContent = new Border();

            try
            {
                window.MinimumContentSizeRequest = new Size(200, 100);
                window.ContentSizeRequest = new Size(600, 400);

                AssertMatchesRequest(window, 600, 400, 200, 100);

                window.ContentScale = 1.25;

                AssertMatchesRequest(window, 600, 400, 200, 100);
            }
            finally
            {
                window.Close();
            }
        }

        private static void AssertMatchesRequest(
            GenericOceanyaWindow window,
            double contentWidth,
            double contentHeight,
            double minimumContentWidth,
            double minimumContentHeight)
        {
            double scale = window.ContentScale;
            (double horizontalOffset, double verticalOffset) =
                GenericOceanyaWindow.GetChromeOffsets(window.BodyMargin, scale);

            Assert.That(
                window.Width,
                Is.EqualTo((contentWidth * scale) + horizontalOffset).Within(0.5),
                $"window width at scale {scale}");
            Assert.That(
                window.Height,
                Is.EqualTo((contentHeight * scale) + verticalOffset).Within(0.5),
                $"window height at scale {scale}");
            Assert.That(
                window.MinWidth,
                Is.EqualTo((minimumContentWidth * scale) + horizontalOffset).Within(0.5),
                $"window minimum width at scale {scale}");
            Assert.That(
                window.MinHeight,
                Is.EqualTo((minimumContentHeight * scale) + verticalOffset).Within(0.5),
                $"window minimum height at scale {scale}");
        }

        private sealed class SizeProbeContent : OceanyaWindowContentControl
        {
            public SizeProbeContent()
            {
                Content = new Grid();
            }

            public override string HeaderText => "SIZE PROBE";

            public override bool IsUserResizeEnabled => true;
        }
    }
}
