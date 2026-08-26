using System.IO;
using System.Linq;
using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Features.Theme;

namespace UnitTests
{
    /// <summary>
    /// Covers translating an AO2 Qt stylesheet into Oceanya panel style settings.
    /// </summary>
    [TestFixture]
    public class Ao2StylesheetTranslatorTests
    {
        [Test]
        public void Apply_TranslatesClassRulesOntoThePanelsOfThatKind()
        {
            OceanyaThemeLayoutState layout = new OceanyaThemeLayoutState();

            int changed = Ao2StylesheetTranslator.Apply(
                "/*comment*/ QComboBox { background-color: #5A595A; color: white; }"
                + "QLineEdit { background-color: rgb(20, 20, 20); font-family: Consolas; font-size: 13px; font-weight: bold; }",
                layout);

            Assert.That(changed, Is.GreaterThan(0));
            Assert.That(layout.Panels["ic_combo_sfx"].BackgroundColor, Is.EqualTo("#FF5A595A"));
            Assert.That(layout.Panels["ic_combo_sfx"].TextColor, Is.EqualTo("white"));
            Assert.That(layout.Panels["ic_message"].BackgroundColor, Is.EqualTo("#FF141414"));
            Assert.That(layout.Panels["ic_message"].FontFamily, Is.EqualTo("Consolas"));
            Assert.That(layout.Panels["ic_message"].FontSize, Is.EqualTo(13));
            Assert.That(layout.Panels["ic_message"].IsBold, Is.True);
        }

        [Test]
        public void Apply_IgnoresRulesWithNoOceanyaEquivalent()
        {
            OceanyaThemeLayoutState layout = new OceanyaThemeLayoutState();

            // Pseudo-states, sub-controls and coordinate selectors have no field to write into, and must
            // not be flattened onto every panel of that class.
            int changed = Ao2StylesheetTranslator.Apply(
                "QComboBox::down-arrow { image: url(x.png); }"
                + "QCheckBox::indicator:checked { background-color: #FF0000; }"
                + "QPushButton[x=\"1001\"] { background-color: #00FF00; }"
                + "QComboBox:hover { background-color: #0000FF; }",
                layout);

            Assert.That(changed, Is.EqualTo(0));
            Assert.That(layout.Panels, Is.Empty);
        }

        [Test]
        public void Apply_DoesNotTreatQtIndicatorSizingAsATextSize()
        {
            OceanyaThemeLayoutState layout = new OceanyaThemeLayoutState();

            // GrayGarden's real rule: the label text is hidden and the huge font only exists to grow the
            // Qt checkbox indicator, which is an image here rather than glyph-sized text.
            int changed = Ao2StylesheetTranslator.Apply(
                "QCheckBox { color: transparent; background-color: transparent; font-size: 50px; }"
                + "QLineEdit { color: transparent; font-size: 40px; }",
                layout);

            Assert.That(changed, Is.EqualTo(0));
            Assert.That(layout.Panels.Values.Any(state => state.FontSize > 0), Is.False);
        }

        [Test]
        public void Apply_TargetsOneWidgetByItsGeometryAndItsState()
        {
            string folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "custom.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(folder, "shouts.png"), new byte[] { 1 });

            try
            {
                OceanyaThemeLayoutState layout = new OceanyaThemeLayoutState();
                var widgets = new[]
                {
                    new Ao2StylesheetTranslator.Ao2WidgetGeometry("shout_custom", 685, 461, 73, 25),
                    new Ao2StylesheetTranslator.Ao2WidgetGeometry("shout_holdit", 762, 461, 73, 25)
                };

                // GrayGarden's real shape: one button gets its own art, the whole row shares hover art.
                int changed = Ao2StylesheetTranslator.Apply(
                    "QPushButton[x=\"685\"][y=\"461\"] { image: url(custom.png); }"
                    + "QPushButton[y=\"461\"]:hover { image: url(shouts.png); }",
                    layout,
                    widgets,
                    new[] { folder });

                Assert.That(changed, Is.EqualTo(2));
                Assert.That(layout.Panels["shout_custom"].ImagePath, Is.EqualTo(Path.Combine(folder, "custom.png")));
                Assert.That(layout.Panels["shout_custom"].ImageScaling, Is.EqualTo("Fill"));
                Assert.That(layout.Panels["shout_custom"].HoverImagePath, Is.EqualTo(Path.Combine(folder, "shouts.png")));

                // The row rule reaches the other shout too, and only its hover art.
                Assert.That(layout.Panels["shout_holdit"].HoverImagePath, Is.EqualTo(Path.Combine(folder, "shouts.png")));
                Assert.That(layout.Panels["shout_holdit"].ImagePath, Is.Empty);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Test]
        public void Apply_WithoutWidgetGeometryIgnoresCoordinateSelectors()
        {
            OceanyaThemeLayoutState layout = new OceanyaThemeLayoutState();

            // A hand-picked stylesheet has no rectangles to match, and styling every button of the class
            // would be worse than doing nothing.
            int changed = Ao2StylesheetTranslator.Apply(
                "QPushButton[x=\"685\"][y=\"461\"] { image: url(custom.png); }",
                layout);

            Assert.That(changed, Is.EqualTo(0));
            Assert.That(layout.Panels, Is.Empty);
        }

        [Test]
        public void Apply_LeavesTheGridsAloneForQtListRules()
        {
            OceanyaThemeLayoutState layout = new OceanyaThemeLayoutState();

            // AO2's QListView is its pair/mute/music list, none of which is a panel here; mapping it onto
            // our item grids painted the emote grid and clients list with that list's background.
            int changed = Ao2StylesheetTranslator.Apply("QListView { background-color: #5A595A; color: white; }", layout);

            Assert.That(changed, Is.EqualTo(0));
            Assert.That(layout.Panels.ContainsKey("ic_emote_grid"), Is.False);
            Assert.That(layout.Panels.ContainsKey("clients_list"), Is.False);
        }

        [Test]
        public void Apply_TranslatesBordersIncludingTheirRemoval()
        {
            OceanyaThemeLayoutState layout = new OceanyaThemeLayoutState();

            int changed = Ao2StylesheetTranslator.Apply(
                "QLineEdit { border: 1px solid rgb(40, 39, 40); }"
                + "QComboBox { border: hidden; }",
                layout);

            Assert.That(changed, Is.GreaterThan(0));
            Assert.That(layout.Panels["ic_message"].BorderColor, Is.EqualTo("#FF282728"));
            Assert.That(layout.Panels["ic_message"].BorderThickness, Is.EqualTo(1));

            // "hidden" is a real instruction, not a missing value: AO2 widgets have no frame of their own.
            Assert.That(layout.Panels["ic_combo_sfx"].BorderThickness, Is.EqualTo(0));
        }

        [Test]
        public void Apply_TranslatesScrollbarColours()
        {
            OceanyaThemeLayoutState layout = new OceanyaThemeLayoutState();

            int changed = Ao2StylesheetTranslator.Apply(
                "QScrollBar:vertical { background: white; border-left: 1px solid #282728; }"
                + "QScrollBar::handle:vertical { background: #AEAFAE; }",
                layout);

            Assert.That(changed, Is.GreaterThan(0));
            Assert.That(layout.Panels["ic_log"].ScrollbarTrackColor, Is.EqualTo("white"));
            Assert.That(layout.Panels["ic_log"].ScrollbarHandleColor, Is.EqualTo("#FFAEAFAE"));
            Assert.That(layout.Panels["ic_log"].ScrollbarBorderColor, Is.EqualTo("#FF282728"));
        }

        [Test]
        public void Apply_TranslatesIndicatorArtworkPerWidgetClass()
        {
            string folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(folder);
            foreach (string name in new[] { "indicator.png", "checked.png", "arrow.png", "scrollarrow.png" })
            {
                File.WriteAllBytes(Path.Combine(folder, name), new byte[] { 1 });
            }

            try
            {
                OceanyaThemeLayoutState layout = new OceanyaThemeLayoutState();
                int changed = Ao2StylesheetTranslator.Apply(
                    "QCheckBox::indicator:unchecked { image: url(indicator.png); }"
                    + "QCheckBox::indicator:checked { image: url(checked.png); }"
                    + "QComboBox::down-arrow { image: url(arrow.png); }"
                    + "QScrollBar::down-arrow:vertical { image: url(scrollarrow.png); }",
                    layout,
                    widgets: null,
                    assetRoots: new[] { folder });

                Assert.That(changed, Is.GreaterThan(0));
                Assert.That(layout.Panels["ic_check_preanim"].IndicatorImagePath, Is.EqualTo(Path.Combine(folder, "indicator.png")));
                Assert.That(layout.Panels["ic_check_preanim"].CheckedIndicatorImagePath, Is.EqualTo(Path.Combine(folder, "checked.png")));
                Assert.That(layout.Panels["ic_combo_sfx"].IndicatorImagePath, Is.EqualTo(Path.Combine(folder, "arrow.png")));

                // A scrollbar's arrow is not a dropdown's arrow: reading it as one put scroll arrows on the
                // logs and every other panel that owns a scrollbar.
                Assert.That(layout.Panels.ContainsKey("ic_log"), Is.False);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Test]
        public void Apply_SkinsSlidersFromTheirGrooveAndHandle()
        {
            string folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "meter.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(folder, "handle.png"), new byte[] { 1 });

            try
            {
                OceanyaThemeLayoutState layout = new OceanyaThemeLayoutState();
                int changed = Ao2StylesheetTranslator.Apply(
                    "QSlider::groove:horizontal { image: url(meter.png); height: 30px; }"
                    + "QSlider::handle:horizontal { image: url(handle.png); width: 15px; }",
                    layout,
                    widgets: null,
                    assetRoots: new[] { folder });

                Assert.That(changed, Is.GreaterThan(0));

                // The groove is the widget-wide background art; the handle is the small overlay.
                Assert.That(layout.Panels["slider_music_volume"].ImagePath, Is.EqualTo(Path.Combine(folder, "meter.png")));
                Assert.That(layout.Panels["slider_music_volume"].IndicatorImagePath, Is.EqualTo(Path.Combine(folder, "handle.png")));

                // QSlider is named panel-by-panel, so mapping it never reaches the logs.
                Assert.That(layout.Panels.ContainsKey("ic_log"), Is.False);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Test]
        public void TryConvertColor_HandlesTheFormsQtStylesheetsUse()
        {
            Assert.That(Ao2StylesheetTranslator.TryConvertColor("#5A595A", out string hex), Is.True);
            Assert.That(hex, Is.EqualTo("#FF5A595A"));

            Assert.That(Ao2StylesheetTranslator.TryConvertColor("#abc", out string shortHex), Is.True);
            Assert.That(shortHex, Is.EqualTo("#FFAABBCC"));

            Assert.That(Ao2StylesheetTranslator.TryConvertColor("rgba(10, 20, 30, 0.5)", out string rgba), Is.True);
            Assert.That(rgba, Is.EqualTo("#801E140A").Or.EqualTo("#800A141E"));

            Assert.That(Ao2StylesheetTranslator.TryConvertColor("transparent", out _), Is.False);
            Assert.That(Ao2StylesheetTranslator.TryConvertColor("", out _), Is.False);
        }

        [Test]
        public void ParseRules_SplitsSelectorsAndDropsComments()
        {
            var rules = Ao2StylesheetTranslator.ParseRules("/* skip */ QWidget { color: white; } QLabel { color: #000; }");

            Assert.That(rules, Has.Exactly(2).Items);
        }
    }
}
