using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Features.Theme;

namespace UnitTests
{
    /// <summary>
    /// Covers the Oceanya theme panel registry and the placement it applies to the main window surface.
    /// </summary>
    [TestFixture]
    public class OceanyaPanelCatalogTests
    {
        [Test]
        public void Catalog_PanelIdsAreUniqueAndStable()
        {
            IReadOnlyList<OceanyaPanelDescriptor> panels = OceanyaPanelCatalog.BuiltInPanels;

            Assert.That(panels.Select(panel => panel.Id).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(panels.Count),
                "Panel ids are referenced by shared theme files and must be unique.");
            Assert.That(
                panels.Select(panel => panel.Id),
                Is.EquivalentTo(new[]
                {
                    "ic_log", "ooc_log", "clients_list", "clients_title", "clients_add", "clients_remove", "shout_backdrop",
                    "shout_holdit", "shout_objection",
                    "shout_takethat", "shout_custom", "ic_settings", "ic_showname", "ic_message",
                    "ic_emote_grid", "ic_check_preanim", "ic_check_flip", "ic_check_additive",
                    "ic_check_immediate", "ic_combo_character", "ic_combo_emote", "ic_combo_position",
                    "ic_combo_textcolor", "ic_combo_effect", "ic_combo_sfx", "ic_button_realization",
                    "ic_button_screenshake", "ic_button_offset", "ic_button_pairing", "bottom_bar",
                    "bar_check_sticky", "bar_check_switchpos", "bar_check_invertlog",
                    "bar_button_editlayout", "bar_button_refresh", "bar_button_viewport",
                    "bar_button_area", "bar_button_debug", "bar_button_music", "bar_button_settings", "viewport", "ding_button", "ooc_divider", "ic_catchphrase",
                    "ic_settings_backdrop", "ic_loremaster",
                    "dredd_row"
                }),
                "Renaming or removing a panel id breaks existing themes and needs a theme compatibility bump.");
        }

        [Test]
        public void Catalog_EveryPanelHasANameAndUsableMinimums()
        {
            foreach (OceanyaPanelDescriptor panel in OceanyaPanelCatalog.Panels)
            {
                Assert.That(panel.DisplayName, Is.Not.Empty, panel.Id);
                Assert.That(panel.MinimumWidth, Is.GreaterThan(0), panel.Id);
                Assert.That(panel.MinimumHeight, Is.GreaterThan(0), panel.Id);
                Assert.That(panel.Placement.Width, Is.GreaterThanOrEqualTo(panel.MinimumWidth), panel.Id);
                Assert.That(panel.Placement.Height, Is.GreaterThanOrEqualTo(panel.MinimumHeight), panel.Id);
            }
        }

        [Test]
        public void Catalog_DefaultPlacementsMatchTheHistoricFixedLayout()
        {
            // These are the exact Canvas values MainWindow.xaml used before placement became data.
            // Log panels own their background art, so their bounds are the full visual.
            AssertPlacement("ic_log", 55, 0, 232, 323);
            AssertPlacement("ooc_log", 287, 0, 222, 333);
            AssertPlacement("clients_list", 2, 48, 50, 250);
            AssertPlacement("shout_backdrop", 2, 296, 428, 42);
            AssertPlacement("shout_holdit", 4, 298, 102, 40);
            AssertPlacement("shout_custom", 324, 298, 102, 40);
            AssertPlacement("ic_settings", 0, 343, 509, 260);
            // IC sub-panels are placed relative to the main canvas (IC settings sits at y=343).
            AssertPlacement("ic_message", 87, 343, 422, 17);
            AssertPlacement("ic_emote_grid", 4, 360, 505, 120);
            AssertPlacement("ic_combo_sfx", 294, 511, 140, 21);
            AssertPlacement("bottom_bar", 0, 603, 509, 24);
            AssertPlacement("dredd_row", 0, 603, 509, 30);

        }

        [Test]
        public void Get_UnknownPanelIdThrows()
        {
            Assert.That(() => OceanyaPanelCatalog.Get("not_a_panel"), Throws.TypeOf<KeyNotFoundException>());
            Assert.That(OceanyaPanelCatalog.TryGet("not_a_panel"), Is.Null);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ApplyDefaultPlacements_PlacesPanelAndBackdropOnTheCanvas()
        {
            Border icLog = new Border();
            Border icLogBackdrop = new Border();
            Border clientsList = new Border();
            Canvas canvas = new Canvas();
            canvas.Children.Add(icLogBackdrop);
            canvas.Children.Add(icLog);
            canvas.Children.Add(clientsList);

            int placedCount = OceanyaPanelLayout.ApplyDefaultPlacements(new Dictionary<string, OceanyaPanelElements>
            {
                ["ic_log"] = new OceanyaPanelElements(icLog, icLogBackdrop),
                ["clients_list"] = new OceanyaPanelElements(clientsList),
                ["not_a_panel"] = new OceanyaPanelElements(new Border())
            });

            Assert.That(placedCount, Is.EqualTo(2), "Unknown ids are skipped, not thrown on.");
            Assert.That(Canvas.GetLeft(icLog), Is.EqualTo(55));
            Assert.That(Canvas.GetTop(icLog), Is.EqualTo(0));
            Assert.That(icLog.Width, Is.EqualTo(232));
            Assert.That(icLog.Height, Is.EqualTo(323));
            Assert.That(Canvas.GetLeft(clientsList), Is.EqualTo(2));
            Assert.That(Canvas.GetTop(clientsList), Is.EqualTo(48));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ApplyLayout_SavedPlacementOverridesTheDefault()
        {
            Border icLog = new Border();
            Canvas canvas = new Canvas();
            canvas.Children.Add(icLog);

            OceanyaThemeLayoutState layout = new OceanyaThemeLayoutState();
            layout.Panels["ic_log"] = new OceanyaPanelPlacementState { Left = 100, Top = 40, Width = 260, Height = 320 };

            OceanyaPanelLayout.ApplyLayout(
                new Dictionary<string, OceanyaPanelElements> { ["ic_log"] = new OceanyaPanelElements(icLog) },
                layout);

            Assert.That(Canvas.GetLeft(icLog), Is.EqualTo(100));
            Assert.That(Canvas.GetTop(icLog), Is.EqualTo(40));
            Assert.That(icLog.Width, Is.EqualTo(260));
            Assert.That(icLog.Height, Is.EqualTo(320));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ApplyLayout_PanelsMissingFromTheSavedLayoutKeepTheirDefault()
        {
            Border icLog = new Border();
            Canvas canvas = new Canvas();
            canvas.Children.Add(icLog);

            OceanyaPanelLayout.ApplyLayout(
                new Dictionary<string, OceanyaPanelElements> { ["ic_log"] = new OceanyaPanelElements(icLog) },
                new OceanyaThemeLayoutState());

            Assert.That(Canvas.GetLeft(icLog), Is.EqualTo(55));
            Assert.That(icLog.Width, Is.EqualTo(232));
        }

        [Test]
        public void SanitizePlacement_ClampsSizeButKeepsNegativeCoordinatesForSurfaceGrowth()
        {
            OceanyaPanelDescriptor descriptor = OceanyaPanelCatalog.Get("ic_log");

            OceanyaPanelPlacement tooSmall = OceanyaPanelLayout.SanitizePlacement(
                new OceanyaPanelPlacement(-50, -20, 10, 10), descriptor);

            // Negative coordinates are legal: the surface grows left/up and the layout shifts back
            // into view, so a panel dragged past an edge stays reachable instead of being blocked.
            Assert.That(tooSmall.Left, Is.EqualTo(-50));
            Assert.That(tooSmall.Top, Is.EqualTo(-20));
            Assert.That(tooSmall.Width, Is.EqualTo(descriptor.MinimumWidth));
            Assert.That(tooSmall.Height, Is.EqualTo(descriptor.MinimumHeight));

            OceanyaPanelPlacement unusable = OceanyaPanelLayout.SanitizePlacement(
                new OceanyaPanelPlacement(double.NaN, 10, double.NaN, 400), descriptor);

            Assert.That(unusable.Left, Is.EqualTo(descriptor.Placement.Left));
            Assert.That(unusable.Width, Is.EqualTo(descriptor.Placement.Width));
            Assert.That(unusable.Top, Is.EqualTo(10));
            Assert.That(unusable.Height, Is.EqualTo(400));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void CalculateBoundsAndOffsetAll_SupportPanelsDraggedPastTheEdges()
        {
            Border panel = new Border();
            Canvas canvas = new Canvas();
            canvas.Children.Add(panel);
            OceanyaPanelLayout.ApplyPlacement(panel, new OceanyaPanelPlacement(-30, -10, 100, 50));
            Dictionary<string, OceanyaPanelElements> panels = new Dictionary<string, OceanyaPanelElements>
            {
                ["ic_log"] = new OceanyaPanelElements(panel)
            };

            Rect bounds = OceanyaPanelLayout.CalculateBounds(panels);
            Assert.That(bounds.Left, Is.EqualTo(-30));
            Assert.That(bounds.Top, Is.EqualTo(-10));

            OceanyaPanelLayout.OffsetAll(panels, -bounds.Left, -bounds.Top);
            Rect shifted = OceanyaPanelLayout.CalculateBounds(panels);

            Assert.That(shifted.Left, Is.EqualTo(0));
            Assert.That(shifted.Top, Is.EqualTo(0));
            Assert.That(shifted.Right, Is.EqualTo(100), "Shifting must not resize the panel.");
            Assert.That(shifted.Bottom, Is.EqualTo(50));
        }

        [Test]
        public void Catalog_KindsDriveResizeRules()
        {
            // Text-like controls take their height from the font, so vertical resize is disabled.
            Assert.That(OceanyaPanelCatalog.Get("ic_message").Kind, Is.EqualTo(OceanyaPanelKind.TextInput));
            Assert.That(OceanyaPanelCatalog.Get("ic_message").AllowsVerticalResize, Is.False);
            Assert.That(OceanyaPanelCatalog.Get("ic_message").AllowsHorizontalResize, Is.True);

            Assert.That(OceanyaPanelCatalog.Get("ic_combo_sfx").AllowsVerticalResize, Is.False);
            Assert.That(OceanyaPanelCatalog.Get("bar_check_sticky").AllowsVerticalResize, Is.False);

            // Image buttons and grids resize freely; their kind only changes the editor options.
            Assert.That(OceanyaPanelCatalog.Get("shout_holdit").Kind, Is.EqualTo(OceanyaPanelKind.ImageButton));
            Assert.That(OceanyaPanelCatalog.Get("shout_holdit").AllowsVerticalResize, Is.True);
            Assert.That(OceanyaPanelCatalog.Get("ic_emote_grid").Kind, Is.EqualTo(OceanyaPanelKind.ItemGrid));
            Assert.That(OceanyaPanelCatalog.Get("ic_log").Kind, Is.EqualTo(OceanyaPanelKind.Static));
        }

        [Test]
        public void TextPanelHeight_FollowsTheFontSize()
        {
            double small = OceanyaPanelStyleApplier.ResolveTextPanelHeight(10);
            double large = OceanyaPanelStyleApplier.ResolveTextPanelHeight(20);

            Assert.That(small, Is.GreaterThan(10));
            Assert.That(large, Is.GreaterThan(small));
        }

        [Test]
        public void CustomPanels_RegisterAndUnregisterWithoutTouchingTheBuiltIns()
        {
            int builtInCount = OceanyaPanelCatalog.BuiltInPanels.Count;
            OceanyaCustomPanelDefinition definition = OceanyaCustomPanelFactory.CreateDefinition(
                OceanyaCustomPanelFactory.ColorKind,
                new OceanyaPanelPlacement(10, 20, 120, 80));

            Assert.That(definition.Id, Does.StartWith("custom_color_"));

            OceanyaPanelCatalog.RegisterCustomPanel(new OceanyaPanelDescriptor(
                definition.Id,
                definition.DisplayName,
                new OceanyaPanelPlacement(10, 20, 120, 80),
                minimumWidth: 8,
                minimumHeight: 8));
            try
            {
                Assert.That(OceanyaPanelCatalog.IsCustomPanel(definition.Id), Is.True);
                Assert.That(OceanyaPanelCatalog.TryGet(definition.Id), Is.Not.Null);
                Assert.That(OceanyaPanelCatalog.BuiltInPanels.Count, Is.EqualTo(builtInCount));
                Assert.That(OceanyaPanelCatalog.Panels.Count, Is.EqualTo(builtInCount + 1));
            }
            finally
            {
                OceanyaPanelCatalog.UnregisterCustomPanel(definition.Id);
            }

            Assert.That(OceanyaPanelCatalog.IsCustomPanel(definition.Id), Is.False);
            Assert.That(OceanyaPanelCatalog.Panels.Count, Is.EqualTo(builtInCount));
        }

        private static void AssertPlacement(string id, double left, double top, double width, double height)
        {
            Assert.That(
                OceanyaPanelCatalog.Get(id).Placement,
                Is.EqualTo(new OceanyaPanelPlacement(left, top, width, height)),
                id);
        }
    }
}
