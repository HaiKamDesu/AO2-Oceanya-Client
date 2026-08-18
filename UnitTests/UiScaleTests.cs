using System;
using System.Windows;
using NUnit.Framework;
using OceanyaClient;

namespace UnitTests
{
    /// <summary>
    /// Covers the global UI scale math and the shell chrome offsets that depend on it.
    /// </summary>
    [TestFixture]
    public class UiScaleTests
    {
        [Test]
        public void AutomaticScale_ReferenceResolutionStaysUnscaled()
        {
            Assert.That(UiScaleMath.ResolveAutomaticScale(1920, 1080), Is.EqualTo(1d));
        }

        [Test]
        public void AutomaticScale_NeverShrinksBelowOneOnSmallDisplays()
        {
            Assert.That(UiScaleMath.ResolveAutomaticScale(1366, 768), Is.EqualTo(1d));
            Assert.That(UiScaleMath.ResolveAutomaticScale(1280, 720), Is.EqualTo(1d));
        }

        [Test]
        public void AutomaticScale_FourKDoublesTheUi()
        {
            Assert.That(UiScaleMath.ResolveAutomaticScale(3840, 2160), Is.EqualTo(2d));
        }

        [Test]
        public void AutomaticScale_SnapsDownToTheQuarterStep()
        {
            // 2560x1440 is 1.333x the reference size, which must not become a jittery 1.33 factor.
            Assert.That(UiScaleMath.ResolveAutomaticScale(2560, 1440), Is.EqualTo(1.25d));
        }

        [Test]
        public void AutomaticScale_UsesTheMoreConstrainedAxis()
        {
            // An ultrawide is wide but not tall, so height must cap the scale.
            Assert.That(UiScaleMath.ResolveAutomaticScale(5120, 1440), Is.EqualTo(1.25d));
        }

        [Test]
        public void AutomaticScale_ClampsAtTheMaximum()
        {
            Assert.That(UiScaleMath.ResolveAutomaticScale(15360, 8640), Is.EqualTo(UiScaleMath.MaximumScale));
        }

        [Test]
        public void AutomaticScale_InvalidMonitorSizeFallsBackToOne()
        {
            Assert.That(UiScaleMath.ResolveAutomaticScale(0, 1080), Is.EqualTo(1d));
            Assert.That(UiScaleMath.ResolveAutomaticScale(double.NaN, double.NaN), Is.EqualTo(1d));
        }

        [Test]
        public void ClampScale_RejectsInvalidAndOutOfRangeValues()
        {
            Assert.That(UiScaleMath.ClampScale(double.NaN), Is.EqualTo(1d));
            Assert.That(UiScaleMath.ClampScale(0), Is.EqualTo(1d));
            Assert.That(UiScaleMath.ClampScale(-3), Is.EqualTo(1d));
            Assert.That(UiScaleMath.ClampScale(0.1), Is.EqualTo(UiScaleMath.MinimumScale));
            Assert.That(UiScaleMath.ClampScale(99), Is.EqualTo(UiScaleMath.MaximumScale));
            Assert.That(UiScaleMath.ClampScale(1.5), Is.EqualTo(1.5d));
        }

        [Test]
        public void RescaleSavedLength_LegacySaveWithoutScaleIsLeftAlone()
        {
            Assert.That(UiScaleMath.RescaleSavedLength(980, 0, 1), Is.EqualTo(980d));
        }

        [Test]
        public void RescaleSavedLength_SameScaleIsLeftAlone()
        {
            Assert.That(UiScaleMath.RescaleSavedLength(980, 2, 2), Is.EqualTo(980d));
        }

        [Test]
        public void RescaleSavedLength_ConvertsBetweenScales()
        {
            Assert.That(UiScaleMath.RescaleSavedLength(980, 1, 2), Is.EqualTo(1960d));
            Assert.That(UiScaleMath.RescaleSavedLength(1960, 2, 1), Is.EqualTo(980d));
        }

        [Test]
        public void MaximumFittingScale_LimitsScaleToWhatTheMonitorCanShow()
        {
            // 676 content + 30 header + 2 border on a 1040 tall work area.
            double fitScale = UiScaleMath.ResolveMaximumFittingScale(676, 30, 2, 1040);

            Assert.That(fitScale, Is.EqualTo((1040d - 2d) / (676d + 30d)).Within(0.0001));
            Assert.That(fitScale, Is.LessThan(2d), "A 2.0 scale must not be allowed to exceed the screen.");
        }

        [Test]
        public void MaximumFittingScale_UnusableInputDoesNotConstrainAnything()
        {
            Assert.That(UiScaleMath.ResolveMaximumFittingScale(0, 0, 0, 1080), Is.EqualTo(double.PositiveInfinity));
            Assert.That(UiScaleMath.ResolveMaximumFittingScale(676, 30, 2, 0), Is.EqualTo(double.PositiveInfinity));
            Assert.That(UiScaleMath.ResolveMaximumFittingScale(676, 30, 2000, 1040), Is.EqualTo(double.PositiveInfinity));
        }

        [Test]
        public void ScaleFromWindowLength_IsTheInverseOfTheWindowSizeFormula()
        {
            double contentLength = 510;
            double scaledChrome = 8;
            double fixedChrome = 2;
            double scale = 1.75;
            double windowLength = ((contentLength + scaledChrome) * scale) + fixedChrome;

            double resolved = UiScaleMath.ResolveScaleFromWindowLength(
                windowLength, contentLength, scaledChrome, fixedChrome);

            Assert.That(resolved, Is.EqualTo(scale).Within(0.0001));
        }

        [Test]
        public void ScaleFromWindowLength_UnusableInputFallsBackToOne()
        {
            Assert.That(UiScaleMath.ResolveScaleFromWindowLength(0, 510, 8, 2), Is.EqualTo(1d));
            Assert.That(UiScaleMath.ResolveScaleFromWindowLength(900, 0, 0, 2), Is.EqualTo(1d));
        }

        [Test]
        public void ChromeOffsets_ScaleHeaderAndBodyMarginButNotTheFrameBorder()
        {
            Thickness bodyMargin = new Thickness(4, 6, 4, 6);
            (double unscaledHorizontal, double unscaledVertical) =
                GenericOceanyaWindow.GetChromeOffsets(bodyMargin, 1d);
            (double scaledHorizontal, double scaledVertical) =
                GenericOceanyaWindow.GetChromeOffsets(bodyMargin, 2d);

            double frameBorderOffset = GenericOceanyaWindow.SharedFrameBorderThickness * 2;
            Assert.That(unscaledHorizontal, Is.EqualTo(frameBorderOffset + 8));
            Assert.That(
                unscaledVertical,
                Is.EqualTo(GenericOceanyaWindow.SharedHeaderHeight + frameBorderOffset + 12));

            // The frame border is drawn outside the scaled shell, so only the scaled parts double.
            Assert.That(scaledHorizontal, Is.EqualTo(frameBorderOffset + 16));
            Assert.That(
                scaledVertical,
                Is.EqualTo((GenericOceanyaWindow.SharedHeaderHeight * 2) + frameBorderOffset + 24));
        }

        [Test]
        public void MainWindowStateClamp_DoesNotShrinkAScaledWindowBackToUnscaledMinimums()
        {
            SaveData data = new SaveData
            {
                GMMainWindowState = new VisualizerWindowState
                {
                    Width = 1020,
                    Height = 1352,
                    UiScale = 2d
                }
            };

            SaveData normalized = InvokeNormalize(data);

            Assert.That(normalized.GMMainWindowState.Width, Is.EqualTo(1020d));
            Assert.That(normalized.GMMainWindowState.Height, Is.EqualTo(1352d));
            Assert.That(normalized.GMMainWindowState.UiScale, Is.EqualTo(2d));
        }

        [Test]
        public void SaveDataNormalization_ClampsAnOutOfRangeUiScaleFactor()
        {
            SaveData data = new SaveData { UiScaleFactor = 50d };

            SaveData normalized = InvokeNormalize(data);

            Assert.That(normalized.UiScaleFactor, Is.EqualTo(UiScaleMath.MaximumScale));
        }

        private static SaveData InvokeNormalize(SaveData data)
        {
            System.Reflection.MethodInfo? normalizeMethod = typeof(SaveFile).GetMethod(
                "NormalizeLoadedData",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(normalizeMethod, Is.Not.Null, "SaveFile normalization entry point was renamed.");

            normalizeMethod!.Invoke(null, new object?[] { data });
            return data;
        }
    }
}
