using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Features.Theme;

namespace UnitTests
{
    /// <summary>
    /// Covers the parts of a Qt stylesheet where the value that wins is not the first one written.
    /// </summary>
    /// <remarks>
    /// Both cases here came out of comparing AAI side by side with AO2: a magenta outline around every
    /// dropdown that AO2 does not draw, and a label painted solid black where AO2 shows the backdrop.
    /// </remarks>
    [TestFixture]
    public class Ao2StylesheetCascadeTests
    {
        private static OceanyaThemeLayoutState LayoutWith(params string[] panelIds)
        {
            OceanyaThemeLayoutState layout = new OceanyaThemeLayoutState();
            foreach (string id in panelIds)
            {
                layout.Panels[id] = new OceanyaPanelPlacementState();
            }

            return layout;
        }

        /// <summary>
        /// AAI's own rule: the shorthand says magenta, the long-hand that follows says black. Black wins.
        /// </summary>
        [Test]
        public void BorderColorLonghandOverridesTheShorthand()
        {
            OceanyaThemeLayoutState layout = LayoutWith(OceanyaPanelCatalog.IcComboCharacterPanelId);

            Ao2StylesheetTranslator.Apply(
                "QComboBox { border: 1px solid rgba(255, 0, 255, 150); background-color: darkgray;"
                + " color: black; border-radius: 3px; border-color: black }",
                layout);

            // Named colours are stored as written - WPF resolves them - so the check is "black, not magenta".
            OceanyaPanelPlacementState state = layout.Panels[OceanyaPanelCatalog.IcComboCharacterPanelId];
            Assert.Multiple(() =>
            {
                Assert.That(state.BorderColor, Is.EqualTo("black"), "The later border-color must win.");
                Assert.That(state.BorderColor, Does.Not.Contain("FF00FF"), "The shorthand's magenta must not survive.");
            });
        }

        [Test]
        public void PerSideBorderColorBeatsTheUniformLonghand()
        {
            OceanyaThemeLayoutState layout = LayoutWith(OceanyaPanelCatalog.IcComboCharacterPanelId);

            Ao2StylesheetTranslator.Apply(
                "QComboBox { border: 1px solid red; border-color: black; border-bottom-color: #00FF00 }",
                layout);

            Assert.That(
                layout.Panels[OceanyaPanelCatalog.IcComboCharacterPanelId].BorderColor,
                Is.EqualTo("#FF00FF00"),
                "The per-side long-hand is the most specific, so it wins.");
        }

        [Test]
        public void BorderWidthLonghandOverridesTheShorthand()
        {
            OceanyaThemeLayoutState layout = LayoutWith(OceanyaPanelCatalog.IcComboCharacterPanelId);

            Ao2StylesheetTranslator.Apply(
                "QComboBox { border: 4px solid black; border-width: 1px }",
                layout);

            Assert.That(
                layout.Panels[OceanyaPanelCatalog.IcComboCharacterPanelId].BorderThickness,
                Is.EqualTo(1).Within(0.001));
        }

        [Test]
        public void ShorthandStillAppliesWhenNoLonghandFollows()
        {
            OceanyaThemeLayoutState layout = LayoutWith(OceanyaPanelCatalog.IcComboCharacterPanelId);

            Ao2StylesheetTranslator.Apply("QComboBox { border: 2px solid #123456 }", layout);

            OceanyaPanelPlacementState state = layout.Panels[OceanyaPanelCatalog.IcComboCharacterPanelId];
            Assert.Multiple(() =>
            {
                Assert.That(state.BorderColor, Is.EqualTo("#FF123456"));
                Assert.That(state.BorderThickness, Is.EqualTo(2).Within(0.001));
            });
        }

        /// <summary>
        /// AAI writes <c>QLabel { background-color: transparent }</c>, which is an instruction, not a value
        /// we failed to read. Dropping it left the stock opaque fill and the label read as solid black.
        /// </summary>
        [Test]
        public void TransparentBackgroundIsHonouredRatherThanIgnored()
        {
            OceanyaThemeLayoutState layout = LayoutWith(OceanyaPanelCatalog.OocStreamTextPanelId);

            Ao2StylesheetTranslator.Apply("QLabel { color: white; background-color: transparent; }", layout);

            Assert.That(
                layout.Panels[OceanyaPanelCatalog.OocStreamTextPanelId].BackgroundColor,
                Is.EqualTo("#00000000"),
                "transparent must reach the panel as a transparent fill.");
        }

        [Test]
        public void TransparentBorderColorIsHonoured()
        {
            OceanyaThemeLayoutState layout = LayoutWith(OceanyaPanelCatalog.IcComboCharacterPanelId);

            Ao2StylesheetTranslator.Apply("QComboBox { border: 1px solid black; border-color: transparent }", layout);

            Assert.That(
                layout.Panels[OceanyaPanelCatalog.IcComboCharacterPanelId].BorderColor,
                Is.EqualTo("#00000000"));
        }

        /// <summary>
        /// Text colour must NOT be swept up by the transparent handling: AO2 themes use
        /// <c>color: transparent</c> on a checkbox to hide its label and show only the indicator image.
        /// </summary>
        [Test]
        public void TransparentTextColourDoesNotBecomeATransparentForeground()
        {
            OceanyaThemeLayoutState layout = LayoutWith(OceanyaPanelCatalog.IcCheckPreanimPanelId);

            Ao2StylesheetTranslator.Apply("QCheckBox { color: transparent; }", layout);

            Assert.That(
                layout.Panels[OceanyaPanelCatalog.IcCheckPreanimPanelId].TextColor,
                Is.Not.EqualTo("#00000000"),
                "A transparent foreground would erase the label rather than leave it to the indicator art.");
        }

        /// <summary>
        /// Qt matches on the widget's real class, and an AO2 bar button is a QPushButton, so a theme's
        /// QLabel rule must not touch it.
        /// </summary>
        /// <remarks>
        /// AAI writes <c>QLabel { background-color: transparent }</c>. Once transparent backgrounds started
        /// being honoured, that rule leaking onto push buttons erased the fill of every bar button and
        /// shout in the theme - they rendered as bare text on the backdrop.
        /// </remarks>
        [Test]
        public void LabelRulesDoNotReachPushButtons()
        {
            OceanyaThemeLayoutState layout = LayoutWith(
                OceanyaPanelCatalog.BarButtonSettingsPanelId,
                OceanyaPanelCatalog.ShoutHoldItPanelId,
                OceanyaPanelCatalog.OocStreamTextPanelId);

            Ao2StylesheetTranslator.Apply("QLabel { background-color: transparent; color: white; }", layout);

            Assert.Multiple(() =>
            {
                Assert.That(
                    layout.Panels[OceanyaPanelCatalog.BarButtonSettingsPanelId].BackgroundColor,
                    Is.Empty,
                    "A push button keeps its own fill when only QLabel is styled.");
                Assert.That(
                    layout.Panels[OceanyaPanelCatalog.ShoutHoldItPanelId].BackgroundColor,
                    Is.Empty,
                    "A shout is an AOButton too.");
                Assert.That(
                    layout.Panels[OceanyaPanelCatalog.OocStreamTextPanelId].BackgroundColor,
                    Is.EqualTo("#00000000"),
                    "A real label still takes the rule.");
            });
        }

        [Test]
        public void PushButtonRulesStillReachPushButtons()
        {
            OceanyaThemeLayoutState layout = LayoutWith(OceanyaPanelCatalog.BarButtonSettingsPanelId);

            Ao2StylesheetTranslator.Apply("QPushButton { background-color: #112233; }", layout);

            Assert.That(
                layout.Panels[OceanyaPanelCatalog.BarButtonSettingsPanelId].BackgroundColor,
                Is.EqualTo("#FF112233"));
        }

        [Test]
        public void NamedColoursThemesActuallyUseAreUnderstood()
        {
            foreach (string named in new[] { "darkgray", "dimgray", "darkgreen", "white", "black" })
            {
                Assert.That(
                    Ao2StylesheetTranslator.TryConvertColor(named, out string converted),
                    Is.True,
                    named + " is used by shipped themes and must convert.");
                Assert.That(converted, Is.Not.Empty, named + " must produce a usable value.");
            }
        }

        [Test]
        public void QtStyleAlphaIsReadAsZeroToTwoFiftyFive()
        {
            Assert.That(
                Ao2StylesheetTranslator.TryConvertColor("rgba(0, 0, 0, 150)", out string converted),
                Is.True);
            Assert.That(converted, Is.EqualTo("#96000000"), "Qt writes alpha 0-255, not 0-1.");
        }
    }
}
