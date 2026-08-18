using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Features.Ui;

namespace UnitTests
{
    /// <summary>
    /// Hosts real content controls through <see cref="OceanyaWindowManager"/> and checks that the shell
    /// window geometry matches the applied UI scale. These tests show real windows, so they run on the
    /// interactive Windows desktop only.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [Explicit("Shows real windows; run manually on an interactive Windows desktop.")]
    public class UiScaleHostingTests
    {
        private UiScaleMode originalMode;
        private double originalFactor;

        [SetUp]
        public void SetUp()
        {
            originalMode = SaveFile.Data.UiScaleMode;
            originalFactor = SaveFile.Data.UiScaleFactor;
            SaveFile.Data.UiScaleMode = UiScaleMode.Manual;
            SaveFile.Data.UiScaleFactor = 1.0;
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Data.UiScaleMode = originalMode;
            SaveFile.Data.UiScaleFactor = originalFactor;
            UiScaleManager.ClearLivePreview();
        }

        [Test]
        public void ShownWindow_WithExplicitContentSize_MatchesScaleAfterSettingsChange()
        {
            ProbeContent content = new ProbeContent { Width = 600, Height = 400, MinWidth = 300, MinHeight = 200 };
            Window window = OceanyaWindowManager.CreateWindow(content);
            try
            {
                window.Show();
                DrainDispatcher();

                AssertWindowMatchesScale(window, content, 1.0, "initial show");

                SaveFile.Data.UiScaleFactor = 1.5;
                UiScaleManager.RaiseScaleChanged();
                DrainDispatcher();

                AssertWindowMatchesScale(window, content, 1.5, "after scale change");
            }
            finally
            {
                window.Close();
                DrainDispatcher();
            }
        }

        [Test]
        public void ShownWindow_ScaledBeforeFirstShow_MatchesScaleOnOpen()
        {
            // This is the launcher path: the scale is already set when the window is created.
            SaveFile.Data.UiScaleFactor = 1.5;

            ProbeContent content = new ProbeContent { Width = 600, Height = 400, MinWidth = 300, MinHeight = 200 };
            Window window = OceanyaWindowManager.CreateWindow(content);
            try
            {
                window.Show();
                DrainDispatcher();

                AssertWindowMatchesScale(window, content, 1.5, "opened at an already-scaled setting");
            }
            finally
            {
                window.Close();
                DrainDispatcher();
            }
        }

        [Test]
        public void ShownWindow_ContentSizeAppliedAfterLoad_MatchesScale()
        {
            // Mirrors windows that restore a saved size from their own Loaded handler.
            SaveFile.Data.UiScaleFactor = 1.5;

            ProbeContent content = new ProbeContent { Width = 600, Height = 400, MinWidth = 300, MinHeight = 200 };
            Window window = OceanyaWindowManager.CreateWindow(content);
            try
            {
                window.Show();
                DrainDispatcher();

                content.Width = 720;
                content.Height = 480;
                DrainDispatcher();

                AssertWindowMatchesScale(window, content, 1.5, "content size applied after load");
            }
            finally
            {
                window.Close();
                DrainDispatcher();
            }
        }

        [Test]
        public void RealStartupWindows_ReportTheirGeometryAfterShow()
        {
            SaveFile.Data.UiScaleFactor = 1.5;

            // Reproduce the reported condition: a legacy window-space popup state saved before UI
            // scaling existed. Restoring that as-is is what left the inner control unscaled.
            SaveFile.Data.PopupWindowStates["GenericOceanyaWindow|Character File Creator"] =
                new VisualizerWindowState { Width = 1222, Height = 792, IsContentSpace = false, UiScale = 0 };

            foreach (Func<OceanyaWindowContentControl> factory in new Func<OceanyaWindowContentControl>[]
            {
                () => new AOCharacterFileCreatorWindow(),
                () => new OceanyanFileHivemindWindow(manageStartupWaitForm: false)
            })
            {
                OceanyaWindowContentControl content;
                try
                {
                    content = factory();
                }
                catch (Exception exception)
                {
                    TestContext.Out.WriteLine($"construction failed: {exception.GetType().Name}: {exception.Message}");
                    continue;
                }

                Window window = OceanyaWindowManager.CreateWindow(content);
                try
                {
                    window.Show();
                    DrainDispatcher();

                    GenericOceanyaWindow shellWindow = (GenericOceanyaWindow)window;
                    (double horizontalOffset, double verticalOffset) =
                        GenericOceanyaWindow.GetChromeOffsets(shellWindow.BodyMargin, shellWindow.ContentScale);
                    TestContext.Out.WriteLine(
                        $"{content.GetType().Name}: scale={shellWindow.ContentScale:0.000} "
                        + $"content={content.Width:0}x{content.Height:0} "
                        + $"contentActual={content.ActualWidth:0}x{content.ActualHeight:0} "
                        + $"contentMin={content.MinWidth:0}x{content.MinHeight:0} "
                        + $"window={window.ActualWidth:0}x{window.ActualHeight:0} "
                        + $"expectedWindow={(content.Width * shellWindow.ContentScale) + horizontalOffset:0}"
                        + $"x{(content.Height * shellWindow.ContentScale) + verticalOffset:0} "
                        + $"windowMin={window.MinWidth:0}x{window.MinHeight:0} state={window.WindowState}");
                }
                finally
                {
                    window.Close();
                    DrainDispatcher();
                }
            }
        }

        private static void AssertWindowMatchesScale(Window window, FrameworkElement content, double expectedScale, string phase)
        {
            GenericOceanyaWindow shellWindow = (GenericOceanyaWindow)window;
            (double horizontalOffset, double verticalOffset) =
                GenericOceanyaWindow.GetChromeOffsets(shellWindow.BodyMargin, expectedScale);
            double expectedWidth = (content.Width * expectedScale) + horizontalOffset;
            double expectedHeight = (content.Height * expectedScale) + verticalOffset;

            Assert.Multiple(() =>
            {
                Assert.That(shellWindow.ContentScale, Is.EqualTo(expectedScale).Within(0.01), $"ContentScale ({phase})");
                Assert.That(window.ActualWidth, Is.EqualTo(expectedWidth).Within(2), $"window width ({phase})");
                Assert.That(window.ActualHeight, Is.EqualTo(expectedHeight).Within(2), $"window height ({phase})");
            });
        }

        private static void DrainDispatcher()
        {
            for (int i = 0; i < 8; i++)
            {
                System.Windows.Threading.DispatcherFrame frame = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    new Action(() => frame.Continue = false),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
        }

        private sealed class ProbeContent : OceanyaWindowContentControl
        {
            public ProbeContent()
            {
                Content = new Grid { Background = System.Windows.Media.Brushes.DarkSlateGray };
            }

            public override string HeaderText => "PROBE";

            public override bool IsUserResizeEnabled => true;
        }
    }
}
