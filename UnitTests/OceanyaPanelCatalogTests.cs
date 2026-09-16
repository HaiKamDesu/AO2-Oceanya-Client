using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
                    "ic_emote_grid", "ic_emote_prev", "ic_emote_next", "ic_check_preanim", "ic_check_flip", "ic_check_additive",
                    "ic_check_immediate", "ic_check_casing", "ic_combo_character", "ic_combo_emote", "ic_combo_position",
                    "ic_combo_textcolor", "ic_combo_effect", "ic_combo_sfx", "ic_button_realization",
                    "ic_button_screenshake", "ic_button_offset", "ic_button_pairing", "bottom_bar",
                    "bar_check_sticky", "bar_check_switchpos", "bar_check_invertlog",
                    "bar_button_editlayout", "bar_button_refresh", "bar_button_viewport",
                    "area_list", "music_list", "bar_button_areamusic",
                    "bar_button_mute", "bar_button_evidence", "bar_button_casing", "bar_button_reloadtheme",
                    "bar_button_changecharacter", "bar_button_callmod",
                    "slider_music_volume", "slider_sfx_volume", "slider_blip_volume",
                    "slider_music_label", "slider_sfx_label", "slider_blip_label",
                    "ic_check_showname",
                    "ic_combo_position_reset", "ic_combo_character_reset", "ic_combo_sfx_reset",
                    "area_music_search",
                    "judge_defence_bar", "judge_prosecution_bar",
                    "judge_defence_minus", "judge_defence_plus",
                    "judge_prosecution_minus", "judge_prosecution_plus",
                    "judge_witness_testimony", "judge_cross_examination",
                    "judge_not_guilty", "judge_guilty",
                    "bar_button_area", "bar_button_debug", "bar_button_music", "bar_button_settings", "viewport", "ooc_chat", "ooc_stream_backdrop", "ooc_stream_text", "ooc_message", "ooc_showname", "ooc_server_console", "ding_button", "ooc_divider", "ic_catchphrase",
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
            AssertPlacement("ic_settings", 33, 353, 452, 115);
            // IC sub-panels are placed relative to the main canvas (IC settings sits at y=343).
            AssertPlacement("ic_message", 87, 343, 422, 17);
            AssertPlacement("ic_emote_grid", 4, 360, 505, 120);
            // The paging arrows became panels of their own. These are the exact rectangles they occupied
            // INSIDE the grid in 7.12 (5px control margin, 30px arrow columns, star-sized middle row), and
            // the grid keeps that inset by default, so the stock window is unchanged.
            AssertPlacement("ic_emote_prev", 9, 365, 30, 110);
            AssertPlacement("ic_emote_next", 474, 365, 30, 110);
            AssertPlacement("ic_combo_sfx", 294, 511, 140, 21);
            AssertPlacement("bottom_bar", 0, 603, 509, 24);
            // The viewport panel replaces the viewport button, so it starts at the button spot.
            AssertPlacement("viewport", 404, 603, 256, 192);
            AssertPlacement("dredd_row", 0, 603, 509, 30);
            // The OOC input controls were extracted from the log; these are the exact spots they
            // occupied inside it in 7.12 (36px input strip at the bottom of a 298-tall log).
            AssertPlacement("ooc_message", 287, 262, 222, 18);
            AssertPlacement("ooc_showname", 287, 281, 93, 17);
            // LastChildFill gave the console button the width left over next to the 93px showname.
            AssertPlacement("ooc_server_console", 380, 281, 129, 17);

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
        public void SanitizePlacement_ClampsSizeAndKeepsPanelsOnTheSurface()
        {
            OceanyaPanelDescriptor descriptor = OceanyaPanelCatalog.Get("ic_log");

            OceanyaPanelPlacement tooSmall = OceanyaPanelLayout.SanitizePlacement(
                new OceanyaPanelPlacement(-50, -20, 10, 10), descriptor);

            // The surface no longer auto-grows to swallow negative coordinates; the user resizes the
            // window in edit mode instead, so panels are kept on the surface.
            Assert.That(tooSmall.Left, Is.EqualTo(0));
            Assert.That(tooSmall.Top, Is.EqualTo(0));
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
        public void CalculateBoundsAndOffsetAll_MeasureAndShiftPanelRectangles()
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

        [Test]
        public void DefaultZOrder_ReproducesThe712DrawOrder()
        {
            // Backdrops must sit behind the controls they were drawn behind in MainWindow.xaml.
            Assert.That(
                OceanyaPanelCatalog.GetDefaultZOrder("ic_settings_backdrop"),
                Is.LessThan(OceanyaPanelCatalog.GetDefaultZOrder("ic_message")),
                "The IC settings backdrop was drawn before the IC controls.");
            Assert.That(
                OceanyaPanelCatalog.GetDefaultZOrder("ic_loremaster"),
                Is.LessThan(OceanyaPanelCatalog.GetDefaultZOrder("ic_emote_grid")));
            Assert.That(
                OceanyaPanelCatalog.GetDefaultZOrder("shout_backdrop"),
                Is.LessThan(OceanyaPanelCatalog.GetDefaultZOrder("shout_holdit")));
            Assert.That(
                OceanyaPanelCatalog.GetDefaultZOrder("bottom_bar"),
                Is.LessThan(OceanyaPanelCatalog.GetDefaultZOrder("bar_button_settings")));
            Assert.That(
                OceanyaPanelCatalog.GetDefaultZOrder("ooc_log"),
                Is.LessThan(OceanyaPanelCatalog.GetDefaultZOrder("ooc_message")));

            // Every built-in panel needs an order, or it silently lands at 0 behind everything.
            foreach (OceanyaPanelDescriptor panel in OceanyaPanelCatalog.BuiltInPanels)
            {
                Assert.That(OceanyaPanelCatalog.GetDefaultZOrder(panel.Id), Is.GreaterThan(0), panel.Id);
            }
        }

        [Test]
        public void ViewportPanel_ResizesUniformly()
        {
            // Viewport content always renders uniformly, so a free resize would only add dead space.
            Assert.That(OceanyaPanelCatalog.Get("viewport").MaintainsAspectRatio, Is.True);
            Assert.That(OceanyaPanelCatalog.Get("ic_log").MaintainsAspectRatio, Is.False);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void CancellingEditModeRollsTheLayoutBack()
        {
            OceanyaThemeLayoutState? original = SaveFile.Data.OceanyaThemeLayout;
            try
            {
                SaveFile.Data.OceanyaThemeLayout = new OceanyaThemeLayoutState();
                SaveFile.Data.OceanyaThemeLayout.Panels["ic_log"] = new OceanyaPanelPlacementState
                {
                    Left = 10,
                    Top = 20,
                    Width = 100,
                    Height = 50
                };

                Canvas surface = new Canvas { Width = 500, Height = 400 };
                Grid element = new Grid();
                surface.Children.Add(element);
                Dictionary<string, OceanyaPanelElements> panels = new Dictionary<string, OceanyaPanelElements>(StringComparer.Ordinal)
                {
                    ["ic_log"] = new OceanyaPanelElements(element)
                };

                Window host = new Window { Content = surface, ShowActivated = false, Width = 520, Height = 420 };
                host.Show();

                OceanyaPanelEditModeController controller = new OceanyaPanelEditModeController(surface, panels, () => { });
                controller.Activate();

                // Edits persist as they happen, so cancelling has to restore the copy taken at activation
                // rather than undo steps.
                OceanyaPanelLayout.ApplyPlacement(element, new OceanyaPanelPlacement(400, 300, 120, 60));
                controller.PersistLayout();
                Assert.That(SaveFile.Data.OceanyaThemeLayout.Panels["ic_log"].Left, Is.EqualTo(400));

                controller.CancelEditing();

                Assert.That(SaveFile.Data.OceanyaThemeLayout.Panels["ic_log"].Left, Is.EqualTo(10));
                Assert.That(SaveFile.Data.OceanyaThemeLayout.Panels["ic_log"].Width, Is.EqualTo(100));
                Assert.That(controller.IsActive, Is.False);

                host.Close();
            }
            finally
            {
                SaveFile.Data.OceanyaThemeLayout = original;
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ApplyHiddenPanels_ShowsPanelsALaterLayoutNoLongerHides()
        {
            Grid element = new Grid { Visibility = Visibility.Collapsed };
            Dictionary<string, OceanyaPanelElements> panels = new Dictionary<string, OceanyaPanelElements>(StringComparer.Ordinal)
            {
                ["bar_button_areamusic"] = new OceanyaPanelElements(element)
            };

            OceanyaThemeLayoutState layout = new OceanyaThemeLayoutState();
            layout.Panels["bar_button_areamusic"] = new OceanyaPanelPlacementState { IsHidden = false };

            OceanyaPanelEditModeController.ApplyHiddenPanels(panels, layout);

            // Only collapsing meant a panel hidden by an earlier layout stayed hidden when the next one
            // wanted it - which is why resetting and importing in one session lost the optional widgets.
            Assert.That(element.Visibility, Is.EqualTo(Visibility.Visible));

            layout.Panels["bar_button_areamusic"].IsHidden = true;
            OceanyaPanelEditModeController.ApplyHiddenPanels(panels, layout);

            Assert.That(element.Visibility, Is.EqualTo(Visibility.Collapsed));

            // With no layout at all, the catalog decides: this one is hidden by default.
            element.Visibility = Visibility.Visible;
            OceanyaPanelEditModeController.ApplyHiddenPanels(panels, new OceanyaThemeLayoutState());

            Assert.That(element.Visibility, Is.EqualTo(Visibility.Collapsed));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ScrollbarColours_ApplyToThePanelAndAreRemovedOnReset()
        {
            // A descriptor of its own, not a catalog one: baselines are keyed by panel id and other tests in
            // this assembly build real windows on their own STA threads, whose elements cannot be touched
            // from here.
            OceanyaPanelDescriptor descriptor = new OceanyaPanelDescriptor(
                "probe_scrollbars",
                "Probe",
                new OceanyaPanelPlacement(0, 0, 100, 100),
                minimumWidth: 10,
                minimumHeight: 10,
                kind: OceanyaPanelKind.Static);
            Grid panel = new Grid();
            OceanyaPanelPlacementState state = new OceanyaPanelPlacementState
            {
                ScrollbarTrackColor = "#FFFFFFFF",
                ScrollbarHandleColor = "#FFAEAFAE",
                ScrollbarBorderColor = "#FF282728"
            };

            OceanyaPanelStyleApplier.Apply(descriptor, panel, state);

            // The themed style and its brushes live in the panel's own resource scope, so the change is
            // per-panel and undoing it is a matter of removing them again.
            Assert.That(panel.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)], Is.InstanceOf<Style>());
            Assert.That(panel.Resources["OceanyaScrollBarHandleBrush"], Is.Not.Null);

            OceanyaPanelStyleApplier.RestoreBaseline(descriptor.Id, panel);

            Assert.That(panel.Resources.Contains(typeof(System.Windows.Controls.Primitives.ScrollBar)), Is.False);
            Assert.That(panel.Resources.Contains("OceanyaScrollBarHandleBrush"), Is.False);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void StyleBaseline_RoundTripsLocalNullsAndLeavesStyleValuesAlone()
        {
            OceanyaPanelDescriptor descriptor = OceanyaPanelCatalog.Get("ic_message");

            // A local Background="{x:Null}" (what the chat logs use to stay transparent) must come back
            // as null, not be cleared to the control's default brush.
            TextBox transparent = new TextBox { Background = null };
            OceanyaPanelStyleApplier.CaptureBaseline("probe_transparent", transparent);
            transparent.Background = Brushes.Red;
            OceanyaPanelStyleApplier.RestoreBaseline("probe_transparent", transparent);
            Assert.That(transparent.Background, Is.Null);

            // A value that came from a style must not be turned into a local value or wiped.
            Style style = new Style(typeof(TextBox));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Green));
            TextBox styled = new TextBox { Style = style };
            OceanyaPanelStyleApplier.CaptureBaseline("probe_styled", styled);
            styled.Background = Brushes.Red;
            OceanyaPanelStyleApplier.RestoreBaseline("probe_styled", styled);
            Assert.That(styled.Background, Is.EqualTo(Brushes.Green));
            Assert.That(styled.ReadLocalValue(Control.BackgroundProperty), Is.EqualTo(DependencyProperty.UnsetValue));
            Assert.That(descriptor.Kind, Is.EqualTo(OceanyaPanelKind.TextInput));
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
