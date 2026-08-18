using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Features.Ui;

namespace UnitTests
{
    /// <summary>
    /// Covers scale independent window size persistence. These construct WPF windows but never show them.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class WindowStatePersistenceTests
    {
        [Test]
        public void ApplySize_ContentSpaceState_IsAppliedUnchangedAtAnyScale()
        {
            (GenericOceanyaWindow window, StubContent content) = CreateHostedPair(scale: 1.5);
            VisualizerWindowState state = new VisualizerWindowState
            {
                Width = 600,
                Height = 400,
                IsContentSpace = true,
                UiScale = 1.0
            };

            WindowStatePersistence.ApplySize(window, state);

            Assert.That(content.Width, Is.EqualTo(600).Within(0.5));
            Assert.That(content.Height, Is.EqualTo(400).Within(0.5));
        }

        [Test]
        public void ApplySize_LegacyWindowSpaceState_IsConvertedInsteadOfShrinkingTheContent()
        {
            // A pre-scaling save: the numbers are the outer window size at scale 1.0.
            (GenericOceanyaWindow window, StubContent content) = CreateHostedPair(scale: 1.5);
            (double horizontalOffset, double verticalOffset) =
                GenericOceanyaWindow.GetChromeOffsets(window.BodyMargin, 1.0);
            VisualizerWindowState state = new VisualizerWindowState
            {
                Width = 600 + horizontalOffset,
                Height = 400 + verticalOffset,
                IsContentSpace = false,
                UiScale = 0
            };

            WindowStatePersistence.ApplySize(window, state);

            // Applying the raw window numbers as content size is the bug this guards: it would have
            // written 602x432 as content and rendered the inner control at the wrong size until a resize.
            Assert.That(content.Width, Is.EqualTo(600).Within(0.5));
            Assert.That(content.Height, Is.EqualTo(400).Within(0.5));
        }

        [Test]
        public void CaptureSize_RecordsContentSpaceForHostedWindows()
        {
            (GenericOceanyaWindow window, StubContent content) = CreateHostedPair(scale: 1.5);
            content.Width = 640;
            content.Height = 480;
            (double horizontalOffset, double verticalOffset) =
                GenericOceanyaWindow.GetChromeOffsets(window.BodyMargin, 1.5);
            VisualizerWindowState state = new VisualizerWindowState();

            WindowStatePersistence.CaptureSize(
                window,
                state,
                (640 * 1.5) + horizontalOffset,
                (480 * 1.5) + verticalOffset);

            Assert.That(state.IsContentSpace, Is.True);
            Assert.That(state.Width, Is.EqualTo(640).Within(0.5));
            Assert.That(state.Height, Is.EqualTo(480).Within(0.5));
            Assert.That(state.UiScale, Is.EqualTo(1.5).Within(0.01));
        }

        [Test]
        public void CaptureThenApply_RoundTripsAcrossAScaleChange()
        {
            (GenericOceanyaWindow captureWindow, StubContent captureContent) = CreateHostedPair(scale: 1.0);
            captureContent.Width = 400;
            captureContent.Height = 300;
            VisualizerWindowState state = new VisualizerWindowState();
            WindowStatePersistence.CaptureSize(captureWindow, state, 402, 332);

            // 1.25 keeps the restored window inside any supported monitor, so this asserts the
            // round trip rather than the fit-to-monitor clamp (which has its own coverage).
            (GenericOceanyaWindow restoreWindow, StubContent restoreContent) = CreateHostedPair(scale: 1.25);
            WindowStatePersistence.ApplySize(restoreWindow, state);

            Assert.That(restoreContent.Width, Is.EqualTo(400).Within(0.5));
            Assert.That(restoreContent.Height, Is.EqualTo(300).Within(0.5));
        }

        [Test]
        public void ShouldPersistSize_IsFalseWhenAnotherOwnerAlreadyManagesTheSize()
        {
            (GenericOceanyaWindow resizeScalingWindow, _) = CreateHostedPair(scale: 1.0);
            resizeScalingWindow.IsResizeScalingEnabled = true;

            (GenericOceanyaWindow selfManagedWindow, StubContent selfManagedContent) = CreateHostedPair(scale: 1.0);
            selfManagedContent.ManagesOwnWindowSizeOverride = true;

            (GenericOceanyaWindow plainWindow, _) = CreateHostedPair(scale: 1.0);

            Assert.That(WindowStatePersistence.ShouldPersistSize(resizeScalingWindow), Is.False);
            Assert.That(WindowStatePersistence.ShouldPersistSize(selfManagedWindow), Is.False);
            Assert.That(WindowStatePersistence.ShouldPersistSize(plainWindow), Is.True);
        }

        private static (GenericOceanyaWindow Window, StubContent Content) CreateHostedPair(double scale)
        {
            StubContent content = new StubContent { Width = 600, Height = 400 };
            GenericOceanyaWindow window = new GenericOceanyaWindow
            {
                BodyContent = content,
                ContentScale = scale
            };

            return (window, content);
        }

        private sealed class StubContent : OceanyaWindowContentControl
        {
            public StubContent()
            {
                Content = new Grid();
            }

            public bool ManagesOwnWindowSizeOverride { get; set; }

            public override string HeaderText => "STUB";

            public override bool IsUserResizeEnabled => true;

            public override bool ManagesOwnWindowSize => ManagesOwnWindowSizeOverride;
        }
    }
}
