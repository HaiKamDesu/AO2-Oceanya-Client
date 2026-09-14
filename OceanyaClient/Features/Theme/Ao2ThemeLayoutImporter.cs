using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Common;

namespace OceanyaClient.Features.Theme
{
    /// <summary>
    /// Result of translating an AO2 theme into an Oceanya panel layout.
    /// </summary>
    /// <param name="Layout">The produced layout, ready to store in the savefile.</param>
    /// <param name="SurfaceWidth">Window width the theme declares, after scaling and the clients strip.</param>
    /// <param name="SurfaceHeight">Window height the theme declares, after scaling and the Oceanya top bar.</param>
    /// <param name="Widgets">Each mapped widget's own AO2 rectangle, for resolving stylesheet selectors.</param>
    /// <param name="ImportedPanelIds">Panels that got a placement from the theme.</param>
    /// <param name="HiddenPanelIds">Panels hidden because the theme has no equivalent.</param>
    /// <param name="UnmappedIdentifiers">AO2 identifiers the theme defines that Oceanya has no panel for.</param>
    public readonly record struct Ao2ThemeImportResult(
        OceanyaThemeLayoutState Layout,
        double SurfaceWidth,
        double SurfaceHeight,
        IReadOnlyList<Ao2StylesheetTranslator.Ao2WidgetGeometry> Widgets,
        IReadOnlyList<string> ImportedPanelIds,
        IReadOnlyList<string> HiddenPanelIds,
        IReadOnlyList<string> UnmappedIdentifiers);

    /// <summary>
    /// Translates an AO2 theme's <c>courtroom_design.ini</c> into an Oceanya panel layout.
    /// </summary>
    /// <remarks>
    /// AO2 stores every widget as an absolute <c>identifier = x, y, w, h</c> entry, which is the same
    /// model the Oceanya theme layout uses, so importing is a mapping job. See
    /// <c>Documentation/AO2ThemeImport.md</c> for the mapping table and the format's traps.
    /// </remarks>
    public static class Ao2ThemeLayoutImporter
    {
        /// <summary>
        /// Width reserved on the left for the clients strip, which AO2 has no equivalent for.
        /// </summary>
        public const double ClientsStripWidth = 54;

        /// <summary>
        /// Height of the Oceanya connection-info bar that sits above the panel canvas.
        /// </summary>
        /// <remarks>
        /// AO2's <c>courtroom</c> rectangle describes the whole window, but in Oceanya that rectangle is
        /// the canvas below the connection bar, so the imported window has to be that much taller or the
        /// bottom row of the theme's widgets falls outside the window.
        /// </remarks>
        public const double TopBarHeight = 24;

        /// <summary>
        /// Points to device independent pixels: AO2 font sizes are Qt point sizes, WPF's are 1/96 inch.
        /// </summary>
        private const double PointsToDeviceIndependentPixels = 96d / 72d;

        /// <summary>Emote button size AO2's default theme declares, used when a theme omits it.</summary>
        private const double DefaultEmoteButtonSize = 40;

        /// <summary>Emote button spacing AO2's default theme declares, used when a theme omits it.</summary>
        private const double DefaultEmoteButtonSpacing = 9;

        /// <summary>Stable id of the imported theme backdrop panel.</summary>
        public const string ThemeBackdropPanelId = "custom_image_theme_backdrop";

        /// <summary>Id prefix of the imported decoration panels.</summary>
        public const string DecorationPanelIdPrefix = "custom_image_theme_";

        /// <summary>Stacking order of the imported backdrop: behind every other panel.</summary>
        private const int ThemeBackdropZOrder = -1000;

        /// <summary>
        /// AO2 image widgets that are pure decoration here, imported as locked click-through image panels
        /// just above the theme backdrop.
        /// </summary>
        /// <remarks>
        /// These are `AOImageDisplay` widgets in AO2 with no Oceanya control behind them. Themes lean on
        /// them for the artwork that ties the window together - GrayGarden paints characters and framing
        /// into a window-sized `music_display.png` - so dropping them loses a chunk of the theme's look.
        /// A decoration is only imported when its artwork exists and it is not already mapped to a panel.
        /// </remarks>
        private static readonly (string Identifier, string Image)[] DecorationImages =
        {
            ("music_display", "music_display")
        };

        /// <summary>
        /// Panel id to the AO2 image name it wears, and the name of its selected/pressed variant.
        /// </summary>
        /// <remarks>
        /// AO2 assigns art by calling <c>setImage("&lt;name&gt;")</c> per widget (`AO2-Client/src/courtroom.cpp`),
        /// resolved through the theme chain with extension probing. Note the names are not the design.ini
        /// identifiers: the shout widgets use `holdit`/`takethat` without the underscore.
        /// </remarks>
        private static readonly (string PanelId, string Image, string? CheckedImage)[] PanelArt =
        {
            (OceanyaPanelCatalog.ShoutHoldItPanelId, "holdit", "holdit_selected"),
            (OceanyaPanelCatalog.ShoutObjectionPanelId, "objection", "objection_selected"),
            (OceanyaPanelCatalog.ShoutTakeThatPanelId, "takethat", "takethat_selected"),
            (OceanyaPanelCatalog.ShoutCustomPanelId, "custom", "custom_selected"),
            (OceanyaPanelCatalog.IcButtonRealizationPanelId, "realization", "realization_pressed"),
            (OceanyaPanelCatalog.IcButtonScreenshakePanelId, "screenshake", "screenshake_pressed"),
            (OceanyaPanelCatalog.IcButtonPairingPanelId, "pair_button", "pair_button_pressed"),
            (OceanyaPanelCatalog.BarButtonSettingsPanelId, "courtroom_settings", "settings"),
            (OceanyaPanelCatalog.BarButtonAreaMusicSwitchPanelId, "switch_area_music", null),
            (OceanyaPanelCatalog.BarButtonMutePanelId, "mute", "mute_pressed"),
            (OceanyaPanelCatalog.BarButtonEvidencePanelId, "evidence_button", null),
            (OceanyaPanelCatalog.BarButtonReloadThemePanelId, "reload_theme", null),
            (OceanyaPanelCatalog.BarButtonChangeCharacterPanelId, "change_character", null),
            (OceanyaPanelCatalog.BarButtonCallModPanelId, "call_mod", null),
            (OceanyaPanelCatalog.IcComboPositionResetPanelId, "evidencex", null),
            (OceanyaPanelCatalog.IcComboCharacterResetPanelId, "evidencex", null),
            (OceanyaPanelCatalog.IcComboSfxResetPanelId, "evidencex", null),
            (OceanyaPanelCatalog.JudgeDefenceMinusPanelId, "defminus", null),
            (OceanyaPanelCatalog.JudgeDefencePlusPanelId, "defplus", null),
            (OceanyaPanelCatalog.JudgeProsecutionMinusPanelId, "prominus", null),
            (OceanyaPanelCatalog.JudgeProsecutionPlusPanelId, "proplus", null),
            (OceanyaPanelCatalog.JudgeWitnessTestimonyPanelId, "witnesstestimony", null),
            (OceanyaPanelCatalog.JudgeCrossExaminationPanelId, "crossexamination", null),
            (OceanyaPanelCatalog.JudgeNotGuiltyPanelId, "notguilty", null),
            (OceanyaPanelCatalog.JudgeGuiltyPanelId, "guilty", null),
            (OceanyaPanelCatalog.IcEmotePreviousPanelId, "arrow_left", null),
            (OceanyaPanelCatalog.IcEmoteNextPanelId, "arrow_right", null)
        };

        /// <summary>
        /// Panel id to the `courtroom_fonts.ini` widget whose typography it wears.
        /// </summary>
        private static readonly (string PanelId, string FontWidget)[] PanelFonts =
        {
            (OceanyaPanelCatalog.IcLogPanelId, "ic_chatlog"),
            (OceanyaPanelCatalog.OocLogPanelId, "server_chatlog"),
            (OceanyaPanelCatalog.OocChatPanelId, "server_chatlog"),
            (OceanyaPanelCatalog.OocStreamTextPanelId, "server_chatlog")

            // Deliberately absent: AO2's `showname` and `message` fonts belong to the VIEWPORT chatbox,
            // not to the IC input line, and Oceanya already themes the chatbox through
            // AO2ChatPreviewResolver. Copying them onto the input boxes made their text far too large.
        };

        /// <summary>Image extensions AO2 probes, in its own order.</summary>
        private static readonly string[] ImageExtensions = { ".webp", ".apng", ".gif", ".png" };

        /// <summary>
        /// AO2 identifier to Oceanya panel id. The first identifier present in the theme wins, so
        /// aliases are listed in AO2's own preference order.
        /// </summary>
        private static readonly (string PanelId, string[] Identifiers)[] PanelMappings =
        {
            (OceanyaPanelCatalog.ViewportPanelId, new[] { "viewport" }),
            (OceanyaPanelCatalog.IcLogPanelId, new[] { "ic_chatlog" }),
            (OceanyaPanelCatalog.OocLogPanelId, new[] { "server_chatlog", "ms_chatlog" }),
            (OceanyaPanelCatalog.OocMessagePanelId, new[] { "ooc_chat_message" }),
            (OceanyaPanelCatalog.OocShownamePanelId, new[] { "ooc_chat_name" }),
            (OceanyaPanelCatalog.IcShownamePanelId, new[] { "ao2_ic_chat_name", "ic_chat_name" }),
            (OceanyaPanelCatalog.IcMessagePanelId, new[] { "ao2_ic_chat_message", "ic_chat_message" }),
            (OceanyaPanelCatalog.IcEmoteGridPanelId, new[] { "emotes" }),
            (OceanyaPanelCatalog.IcEmotePreviousPanelId, new[] { "emote_left" }),
            (OceanyaPanelCatalog.IcEmoteNextPanelId, new[] { "emote_right" }),
            (OceanyaPanelCatalog.IcComboCharacterPanelId, new[] { "iniswap_dropdown" }),
            (OceanyaPanelCatalog.IcComboEmotePanelId, new[] { "emote_dropdown" }),
            (OceanyaPanelCatalog.IcComboPositionPanelId, new[] { "pos_dropdown" }),
            (OceanyaPanelCatalog.IcComboTextColorPanelId, new[] { "text_color" }),
            (OceanyaPanelCatalog.IcComboEffectPanelId, new[] { "effects_dropdown" }),
            (OceanyaPanelCatalog.IcComboSfxPanelId, new[] { "sfx_dropdown" }),
            (OceanyaPanelCatalog.ShoutHoldItPanelId, new[] { "hold_it" }),
            (OceanyaPanelCatalog.ShoutObjectionPanelId, new[] { "objection" }),
            (OceanyaPanelCatalog.ShoutTakeThatPanelId, new[] { "take_that" }),
            (OceanyaPanelCatalog.ShoutCustomPanelId, new[] { "custom_objection" }),
            (OceanyaPanelCatalog.IcCheckPreanimPanelId, new[] { "pre" }),
            (OceanyaPanelCatalog.IcCheckFlipPanelId, new[] { "flip" }),
            (OceanyaPanelCatalog.IcCheckAdditivePanelId, new[] { "additive" }),
            (OceanyaPanelCatalog.IcCheckImmediatePanelId, new[] { "immediate", "pre_no_interrupt" }),
            (OceanyaPanelCatalog.IcButtonRealizationPanelId, new[] { "realization" }),
            (OceanyaPanelCatalog.IcButtonScreenshakePanelId, new[] { "screenshake" }),
            (OceanyaPanelCatalog.IcButtonPairingPanelId, new[] { "pair_button" }),
            (OceanyaPanelCatalog.BarButtonSettingsPanelId, new[] { "settings" }),
            // AO2's `music_list` is the LIST ITSELF, not a button that opens it - and BOTH lists live in
            // that one rectangle: `courtroom.cpp` calls `set_size_and_pos(ui_area_list, "music_list")` and
            // then the same for `ui_music_list`, with `switch_area_music` swapping which one is shown. The
            // `area_list` identifier is only a FONT key (`set_font(ui_area_list, "", "area_list", ...)`),
            // so a rectangle under that name in a design file describes nothing AO2 draws.
            (OceanyaPanelCatalog.MusicListPanelId, new[] { "music_list" }),
            (OceanyaPanelCatalog.AreaListPanelId, new[] { "music_list" }),
            (OceanyaPanelCatalog.BarButtonAreaMusicSwitchPanelId, new[] { "switch_area_music" }),
            (OceanyaPanelCatalog.BarButtonMutePanelId, new[] { "mute_button" }),
            (OceanyaPanelCatalog.BarButtonEvidencePanelId, new[] { "evidence_button" }),
            (OceanyaPanelCatalog.BarButtonReloadThemePanelId, new[] { "reload_theme" }),
            (OceanyaPanelCatalog.BarButtonChangeCharacterPanelId, new[] { "change_character" }),
            (OceanyaPanelCatalog.BarButtonCallModPanelId, new[] { "call_mod" }),
            (OceanyaPanelCatalog.SliderMusicVolumePanelId, new[] { "music_slider" }),
            (OceanyaPanelCatalog.SliderSfxVolumePanelId, new[] { "sfx_slider" }),
            (OceanyaPanelCatalog.SliderBlipVolumePanelId, new[] { "blip_slider" }),
            (OceanyaPanelCatalog.SliderMusicLabelPanelId, new[] { "music_label" }),
            (OceanyaPanelCatalog.SliderSfxLabelPanelId, new[] { "sfx_label" }),
            (OceanyaPanelCatalog.SliderBlipLabelPanelId, new[] { "blip_label" }),
            (OceanyaPanelCatalog.IcCheckShownamePanelId, new[] { "showname_enable" }),
            (OceanyaPanelCatalog.AreaMusicSearchPanelId, new[] { "music_search" }),
            (OceanyaPanelCatalog.IcComboPositionResetPanelId, new[] { "pos_remove" }),
            (OceanyaPanelCatalog.IcComboCharacterResetPanelId, new[] { "iniswap_remove" }),
            (OceanyaPanelCatalog.IcComboSfxResetPanelId, new[] { "sfx_remove" }),
            (OceanyaPanelCatalog.JudgeDefenceBarPanelId, new[] { "defense_bar" }),
            (OceanyaPanelCatalog.JudgeProsecutionBarPanelId, new[] { "prosecution_bar" }),
            (OceanyaPanelCatalog.JudgeDefenceMinusPanelId, new[] { "defense_minus" }),
            (OceanyaPanelCatalog.JudgeDefencePlusPanelId, new[] { "defense_plus" }),
            (OceanyaPanelCatalog.JudgeProsecutionMinusPanelId, new[] { "prosecution_minus" }),
            (OceanyaPanelCatalog.JudgeProsecutionPlusPanelId, new[] { "prosecution_plus" }),
            (OceanyaPanelCatalog.JudgeWitnessTestimonyPanelId, new[] { "witness_testimony" }),
            (OceanyaPanelCatalog.JudgeCrossExaminationPanelId, new[] { "cross_examination" }),
            (OceanyaPanelCatalog.JudgeNotGuiltyPanelId, new[] { "not_guilty" }),
            (OceanyaPanelCatalog.JudgeGuiltyPanelId, new[] { "guilty" }),
            (OceanyaPanelCatalog.OocServerConsolePanelId, new[] { "ooc_toggle" })
        };

        /// <summary>
        /// Panels Oceanya has that AO2 cannot describe, and that are only decoration: hidden on import
        /// so the theme's own look comes through.
        /// </summary>
        private static readonly string[] CosmeticOnlyPanelIds =
        {
            OceanyaPanelCatalog.IcSettingsPanelId,
            OceanyaPanelCatalog.IcSettingsBackdropPanelId,
            OceanyaPanelCatalog.IcLoremasterPanelId,
            OceanyaPanelCatalog.IcCatchphrasePanelId,
            OceanyaPanelCatalog.ShoutBackdropPanelId,
            OceanyaPanelCatalog.OocDividerPanelId,
            OceanyaPanelCatalog.DingButtonPanelId,
            OceanyaPanelCatalog.BottomBarPanelId
        };

        /// <summary>
        /// Panels that have an AO2 counterpart but that Oceanya cannot usefully lose. If the theme does
        /// not mention them, they go to the left strip rather than being hidden: AO2 hiding its own
        /// position dropdown is fine there, but here it would take away the only way to change position.
        /// </summary>
        private static readonly string[] EssentialMappedPanelIds =
        {
            OceanyaPanelCatalog.IcMessagePanelId,
            OceanyaPanelCatalog.IcShownamePanelId,
            OceanyaPanelCatalog.IcEmoteGridPanelId,
            OceanyaPanelCatalog.IcEmotePreviousPanelId,
            OceanyaPanelCatalog.IcEmoteNextPanelId,
            OceanyaPanelCatalog.IcComboCharacterPanelId,
            OceanyaPanelCatalog.IcComboEmotePanelId,
            OceanyaPanelCatalog.IcComboPositionPanelId,
            OceanyaPanelCatalog.IcComboTextColorPanelId,
            OceanyaPanelCatalog.IcComboEffectPanelId,
            OceanyaPanelCatalog.IcComboSfxPanelId,
            OceanyaPanelCatalog.OocMessagePanelId,
            OceanyaPanelCatalog.OocShownamePanelId
        };

        /// <summary>
        /// Panels Oceanya needs regardless of the theme. They are packed into a strip on the left, next
        /// to the clients list, so nothing important disappears just because AO2 has no name for it.
        /// </summary>
        private static readonly string[] FunctionalOnlyPanelIds =
        {
            OceanyaPanelCatalog.ClientsTitlePanelId,
            OceanyaPanelCatalog.ClientsAddPanelId,
            OceanyaPanelCatalog.ClientsRemovePanelId,
            OceanyaPanelCatalog.ClientsListPanelId,
            // The offset button is deliberately NOT taken from `pair_offset_spinbox`: in AO2 that widget
            // only exists while the pairing panel is open, so themes place it in ways that make no sense
            // for a control Oceanya shows all the time. It goes in the strip at its default size instead.
            OceanyaPanelCatalog.IcButtonOffsetPanelId,
            OceanyaPanelCatalog.BarButtonEditLayoutPanelId,
            OceanyaPanelCatalog.BarButtonRefreshPanelId,
            OceanyaPanelCatalog.BarButtonViewportPanelId,
            OceanyaPanelCatalog.BarButtonAreaPanelId,
            OceanyaPanelCatalog.BarButtonMusicPanelId,
            OceanyaPanelCatalog.BarButtonDebugPanelId,
            OceanyaPanelCatalog.BarCheckStickyPanelId,
            OceanyaPanelCatalog.BarCheckSwitchPosPanelId,
            OceanyaPanelCatalog.BarCheckInvertLogPanelId
        };

        /// <summary>
        /// Identifiers that are not widgets Oceanya could place, or that Oceanya already honours through
        /// its viewport/chatbox theming, so they are not reported as unmapped.
        /// </summary>
        private static readonly HashSet<string> IdentifiersHandledElsewhere = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "courtroom",
            "chatbox",
            "ao2_chatbox",
            "chat_arrow",
            "showname",
            "message",
            "area_list",
            "char_button_spacing",
            "emote_button_spacing",
            "effects_icon_size",
            "showname_extra_width"
        };

        /// <summary>
        /// Translates a parsed design file into an Oceanya layout.
        /// </summary>
        /// <param name="designEntries">Design entries, keyed by AO2 identifier (case-insensitive).</param>
        /// <param name="scalingFactor">AO2 theme scaling factor; values below 1 are treated as 1.</param>
        /// <returns>The import result.</returns>
        public static Ao2ThemeImportResult Translate(
            IReadOnlyDictionary<string, string> designEntries,
            int scalingFactor = 1)
        {
            if (designEntries == null)
            {
                throw new ArgumentNullException(nameof(designEntries));
            }

            int scale = Math.Max(1, scalingFactor);
            OceanyaThemeLayoutState layout = new OceanyaThemeLayoutState();
            List<string> imported = new List<string>();
            List<string> hidden = new List<string>();
            List<Ao2StylesheetTranslator.Ao2WidgetGeometry> widgets =
                new List<Ao2StylesheetTranslator.Ao2WidgetGeometry>();

            // Every imported rectangle shifts right to make room for the clients strip, which AO2 has
            // no concept of; keeping it on the left means it is always present and always reachable.
            double horizontalShift = ClientsStripWidth;

            foreach ((string panelId, string[] identifiers) in PanelMappings)
            {
                OceanyaPanelDescriptor? descriptor = OceanyaPanelCatalog.TryGet(panelId);
                if (descriptor == null)
                {
                    continue;
                }

                if (!TryResolveRectangle(designEntries, identifiers, scale, out OceanyaPanelPlacement placement))
                {
                    // AO2 hides widgets its theme does not mention, so Oceanya does the same - and that
                    // needs a real hidden entry, otherwise the panel just stays at its Oceanya default.
                    OceanyaPanelPlacementState hiddenState = ToState(descriptor.Placement);
                    hiddenState.IsHidden = true;
                    layout.Panels[panelId] = hiddenState;
                    hidden.Add(panelId);
                    continue;
                }

                OceanyaPanelPlacement shifted = new OceanyaPanelPlacement(
                    placement.Left + horizontalShift,
                    placement.Top,
                    Math.Max(descriptor.MinimumWidth, placement.Width),
                    Math.Max(descriptor.MinimumHeight, placement.Height));

                layout.Panels[panelId] = ToState(shifted);
                imported.Add(panelId);

                // The theme's own numbers, unshifted and unscaled: its stylesheet addresses widgets by
                // exactly these coordinates.
                widgets.Add(new Ao2StylesheetTranslator.Ao2WidgetGeometry(
                    panelId,
                    placement.Left / scale,
                    placement.Top / scale,
                    placement.Width / scale,
                    placement.Height / scale));
            }

            ApplyCompositeRegions(designEntries, scale, horizontalShift, layout, hidden);
            ApplyEmoteGridItemSize(designEntries, scale, layout);

            foreach (string panelId in CosmeticOnlyPanelIds)
            {
                OceanyaPanelPlacementState state = layout.Panels.TryGetValue(panelId, out OceanyaPanelPlacementState? existing)
                    ? existing
                    : ToState(OceanyaPanelCatalog.Get(panelId).Placement);
                state.IsHidden = true;
                layout.Panels[panelId] = state;
                if (!hidden.Contains(panelId))
                {
                    hidden.Add(panelId);
                }
            }

            (double surfaceWidth, double canvasHeight) = ResolveSurfaceSize(designEntries, scale, layout);
            double surfaceHeight = canvasHeight + TopBarHeight;
            List<string> strandedEssentials = EssentialMappedPanelIds
                .Where(panelId => hidden.Contains(panelId))
                .ToList();
            PlaceFunctionalPanels(layout, canvasHeight, strandedEssentials);
            foreach (string panelId in strandedEssentials)
            {
                hidden.Remove(panelId);
            }

            // The theme's own window size has to travel with the layout, or the import lands inside the
            // stock 509x628 surface and everything past that is clipped out of sight.
            layout.SurfaceWidth = surfaceWidth;
            layout.SurfaceHeight = surfaceHeight;

            List<string> unmapped = designEntries.Keys
                .Where(identifier => !IsMappedIdentifier(identifier)
                    && !IdentifiersHandledElsewhere.Contains(identifier)
                    && IsRectangleEntry(designEntries[identifier]))
                .OrderBy(identifier => identifier, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new Ao2ThemeImportResult(layout, surfaceWidth, surfaceHeight, widgets, imported, hidden, unmapped);
        }

        /// <summary>
        /// Finds a theme's <c>courtroom_design.ini</c> across the AO2 theme folders.
        /// </summary>
        /// <param name="themeName">Theme folder name.</param>
        /// <returns>The design file path, or null when the theme has none.</returns>
        public static string? ResolveDesignFilePath(string themeName)
        {
            if (string.IsNullOrWhiteSpace(themeName))
            {
                return null;
            }

            foreach (string baseFolder in OceanyaClient.Features.Viewport.AO2ThemeCatalog.GetAo2ThemeScanFolders())
            {
                // Theme folders live under <base>/themes/<name>, matching AO2ThemeCatalog.ResolveThemeRoot.
                string candidate = Path.Combine(baseFolder, "themes", themeName, "courtroom_design.ini");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Imports a named AO2 theme's layout.
        /// </summary>
        /// <param name="themeName">Theme folder name.</param>
        /// <param name="scalingFactor">AO2 theme scaling factor.</param>
        /// <returns>The import result, or null when the theme has no design file.</returns>
        public static Ao2ThemeImportResult? ImportTheme(string themeName, int scalingFactor = 1)
        {
            string? designPath = ResolveDesignFilePath(themeName);
            if (designPath == null)
            {
                return null;
            }

            Dictionary<string, string> entries = ParseDesignFile(designPath);
            if (entries.Count == 0)
            {
                return null;
            }

            Ao2ThemeImportResult result = Translate(entries, scalingFactor);
            ApplyThemeAppearance(
                themeName,
                result.Layout,
                result.SurfaceWidth - ClientsStripWidth,
                result.SurfaceHeight - TopBarHeight,
                result.Widgets);
            return result;
        }

        /// <summary>
        /// Fills in the appearance side of an import: the theme's own artwork, typography and backdrop.
        /// </summary>
        /// <remarks>
        /// Everything written here uses fields the layout editor can also set by hand (panel image and
        /// selected image, font family/size/bold/colour, surface background image and colour), so an
        /// imported theme never contains something a user could not have built themselves.
        /// </remarks>
        /// <param name="themeName">Theme being imported.</param>
        /// <param name="layout">Layout to fill in.</param>
        public static void ApplyThemeAppearance(
            string themeName,
            OceanyaThemeLayoutState layout,
            double themeWidth,
            double themeHeight,
            IReadOnlyList<Ao2StylesheetTranslator.Ao2WidgetGeometry>? widgets = null)
        {
            foreach ((string panelId, string image, string? checkedImage) in PanelArt)
            {
                string? imagePath = ResolveThemeImage(themeName, image);
                if (imagePath == null)
                {
                    continue;
                }

                OceanyaPanelPlacementState state = ResolveState(layout, panelId);
                state.ImagePath = imagePath;
                state.CheckedImagePath = checkedImage != null ? ResolveThemeImage(themeName, checkedImage) ?? string.Empty : string.Empty;

                // AO2 scales button art to the widget with Qt::IgnoreAspectRatio (AOButton::updateIcon),
                // which is Fill here; anything else letterboxes the art inside the widget rect.
                state.ImageScaling = "Fill";
            }

            Dictionary<string, string> fonts = ParseMergedThemeFile(themeName, "courtroom_fonts.ini");
            foreach ((string panelId, string fontWidget) in PanelFonts)
            {
                ApplyFontWidget(layout, panelId, fonts, fontWidget);
            }

            ApplyLogColours(layout, fonts);

            ApplyThemeDecorations(themeName, layout);

            // The theme's Qt stylesheet carries most of its colours; translated into the same per-panel
            // fields the editor exposes.
            Ao2StylesheetTranslator.ApplyThemeStylesheet(themeName, layout, widgets);

            string? background = ResolveThemeImage(themeName, "courtroombackground");
            if (background != null)
            {
                // A locked image panel rather than a surface fill: the theme's backdrop has to occupy the
                // theme's own rectangle, not the whole Oceanya surface, or it stretches across the extra
                // clients strip and stops lining up with the widgets drawn on it.
                layout.CustomPanels.RemoveAll(definition =>
                    string.Equals(definition.Id, ThemeBackdropPanelId, StringComparison.Ordinal));
                layout.CustomPanels.Add(OceanyaCustomPanelFactory.CreateDefinition(
                    OceanyaCustomPanelFactory.ImageKind,
                    new OceanyaPanelPlacement(ClientsStripWidth, 0, themeWidth, themeHeight),
                    imagePath: background,
                    id: ThemeBackdropPanelId,
                    displayName: "Theme Backdrop",
                    isLocked: true,
                    zOrder: ThemeBackdropZOrder,
                    isClickThrough: true));
            }
        }

        /// <summary>
        /// Imports the theme's decorative image widgets as locked image panels.
        /// </summary>
        /// <param name="themeName">Theme being imported.</param>
        /// <param name="layout">Layout to fill in.</param>
        private static void ApplyThemeDecorations(string themeName, OceanyaThemeLayoutState layout)
        {
            string? designPath = ResolveDesignFilePath(themeName);
            if (designPath == null)
            {
                return;
            }

            Dictionary<string, string> entries = ParseDesignFile(designPath);
            int order = 1;
            foreach ((string identifier, string imageName) in DecorationImages)
            {
                if (!TryResolveRectangle(entries, new[] { identifier }, 1, out OceanyaPanelPlacement rect))
                {
                    continue;
                }

                string? imagePath = ResolveThemeImage(themeName, imageName);
                if (imagePath == null)
                {
                    continue;
                }

                // `music_display` doubles as our OOC header when the theme uses it as one; then the art
                // belongs to that panel and importing it a second time as decoration would double it up.
                if (string.Equals(identifier, "music_display", StringComparison.OrdinalIgnoreCase)
                    && layout.Panels.TryGetValue(OceanyaPanelCatalog.OocStreamBackdropPanelId, out OceanyaPanelPlacementState? header)
                    && header != null
                    && !header.IsHidden)
                {
                    header.ImagePath = imagePath;
                    header.ImageScaling = "Fill";
                    continue;
                }

                string panelId = DecorationPanelIdPrefix + identifier;
                layout.CustomPanels.RemoveAll(definition => string.Equals(definition.Id, panelId, StringComparison.Ordinal));
                layout.CustomPanels.Add(OceanyaCustomPanelFactory.CreateDefinition(
                    OceanyaCustomPanelFactory.ImageKind,
                    new OceanyaPanelPlacement(rect.Left + ClientsStripWidth, rect.Top, rect.Width, rect.Height),
                    imagePath: imagePath,
                    id: panelId,
                    displayName: "Theme " + identifier.Replace('_', ' '),
                    isLocked: true,
                    zOrder: ThemeBackdropZOrder + order,
                    isClickThrough: true));
                order++;
            }
        }

        /// <summary>
        /// Reads a theme file with the default theme merged in **per key**, the way AO2 resolves them.
        /// </summary>
        /// <remarks>
        /// AO2 looks each key up through the theme chain (`get_design_element`), so a theme that sets only
        /// some of them inherits the rest from the default theme. Reading one file instead meant the log
        /// colours a theme leaves out - and most leave out all of the name and timestamp ones, which only
        /// the default theme defines - fell back to ours rather than to AO2's.
        /// </remarks>
        /// <param name="themeName">Theme being imported.</param>
        /// <param name="fileName">File to read, e.g. `courtroom_fonts.ini`.</param>
        /// <returns>The merged entries.</returns>
        private static Dictionary<string, string> ParseMergedThemeFile(string themeName, string fileName)
        {
            Dictionary<string, string> merged = ParseDesignFile(ResolveInTheme("default", fileName) ?? string.Empty);
            foreach (KeyValuePair<string, string> entry in ParseDesignFile(ResolveInTheme(themeName, fileName) ?? string.Empty))
            {
                merged[entry.Key] = entry.Value;
            }

            return merged;
        }

        /// <summary>
        /// Imports the per-log colours AO2 keeps beside the fonts.
        /// </summary>
        /// <remarks>
        /// A log is more than one colour in AO2: `ic_chatlog_color` is the body, with separate colours for
        /// other people's shownames, your own, and the timestamp; the OOC log has a body colour and a sender
        /// colour. The logs also have no background of their own there - the courtroom art shows through -
        /// so the backgrounds our log panels paint are cleared to transparent, which the panel settings can
        /// put back by hand.
        /// </remarks>
        /// <param name="layout">Layout to fill in.</param>
        /// <param name="fonts">Parsed `courtroom_fonts.ini`.</param>
        private static void ApplyLogColours(OceanyaThemeLayoutState layout, IReadOnlyDictionary<string, string> fonts)
        {
            if (fonts.Count == 0)
            {
                return;
            }

            OceanyaPanelPlacementState icLog = ResolveState(layout, OceanyaPanelCatalog.IcLogPanelId);
            icLog.SenderColor = ReadColour(fonts, "ic_chatlog_showname_color") ?? icLog.SenderColor;
            icLog.SelfNameColor = ReadColour(fonts, "ic_chatlog_selfname_color") ?? icLog.SelfNameColor;
            // Our only timestamp-ish detail is the marker on your own lines, so it takes AO2's SELF
            // timestamp colour, falling back to the general one.
            icLog.TimestampColor = ReadColour(fonts, "ic_chatlog_selftimestamp_color")
                ?? ReadColour(fonts, "ic_chatlog_timestamp_color")
                ?? icLog.TimestampColor;

            // A plain OOC message is coloured with the MASTER-server key and a server one with the server
            // key - AO2 picks by the colour flag on the CT packet, not by which log it lands in.
            OceanyaPanelPlacementState oocChat = ResolveState(layout, OceanyaPanelCatalog.OocChatPanelId);
            oocChat.SenderColor = ReadColour(fonts, "ms_chatlog_sender_color") ?? oocChat.SenderColor;
            oocChat.ServerNameColor = ReadColour(fonts, "server_chatlog_sender_color") ?? oocChat.ServerNameColor;

            // AO2's logs are transparent widgets over the courtroom art.
            icLog.BackgroundColor = TransparentColour;
            ResolveState(layout, OceanyaPanelCatalog.OocLogPanelId).BackgroundColor = TransparentColour;
        }

        private static string? ReadColour(IReadOnlyDictionary<string, string> fonts, string key)
        {
            return fonts.TryGetValue(key, out string? value) ? ConvertRgbTripletToHex(value) : null;
        }

        /// <summary>Fully transparent colour, for panels a theme wants to see through.</summary>
        private const string TransparentColour = "#00000000";

        private static void ApplyFontWidget(
            OceanyaThemeLayoutState layout,
            string panelId,
            IReadOnlyDictionary<string, string> fonts,
            string fontWidget)
        {
            if (fonts.Count == 0 || OceanyaPanelCatalog.TryGet(panelId) == null)
            {
                return;
            }

            OceanyaPanelPlacementState state = ResolveState(layout, panelId);
            if (fonts.TryGetValue(fontWidget, out string? sizeText)
                && double.TryParse(sizeText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double size)
                && size > 0)
            {
                // AO2 sets QFont::setPointSize, so these are POINTS; WPF font sizes are device independent
                // pixels. Copying the number straight across rendered text a quarter too small, which is
                // what clipped the OOC header - its panel hugs the font size and the glyphs did not fit.
                state.FontSize = Math.Round(size * PointsToDeviceIndependentPixels, 2);
            }

            if (fonts.TryGetValue(fontWidget + "_font", out string? family) && !string.IsNullOrWhiteSpace(family))
            {
                state.FontFamily = family.Trim();
            }

            if (fonts.TryGetValue(fontWidget + "_bold", out string? bold))
            {
                state.IsBold = bold.Trim() == "1";
            }

            if (fonts.TryGetValue(fontWidget + "_color", out string? color))
            {
                string? parsed = ConvertRgbTripletToHex(color);
                if (parsed != null)
                {
                    state.TextColor = parsed;
                }
            }
        }

        /// <summary>
        /// Converts AO2's `r, g, b` colour form into the #AARRGGBB string our layout stores.
        /// </summary>
        /// <param name="triplet">Colour triplet.</param>
        /// <returns>The colour string, or null when the value is not a triplet.</returns>
        public static string? ConvertRgbTripletToHex(string triplet)
        {
            string[] parts = (triplet ?? string.Empty).Split(',');
            if (parts.Length < 3)
            {
                return null;
            }

            if (!TryParseInt(parts[0], out int r) || !TryParseInt(parts[1], out int g) || !TryParseInt(parts[2], out int b))
            {
                return null;
            }

            return $"#FF{Math.Clamp(r, 0, 255):X2}{Math.Clamp(g, 0, 255):X2}{Math.Clamp(b, 0, 255):X2}";
        }

        /// <summary>
        /// Finds a file inside a theme, honouring the theme then default-theme fallback.
        /// </summary>
        /// <param name="themeName">Theme to search.</param>
        /// <param name="fileName">File name inside the theme folder.</param>
        /// <returns>The path, or null when no theme provides it.</returns>
        public static string? ResolveThemeFilePath(string themeName, string fileName)
        {
            return ResolveInTheme(themeName, fileName) ?? ResolveInTheme("default", fileName);
        }

        /// <summary>
        /// Finds a file inside one specific theme, with no fallback.
        /// </summary>
        /// <param name="themeName">Theme folder name.</param>
        /// <param name="fileName">File name inside the theme folder.</param>
        /// <returns>The path, or null.</returns>
        private static string? ResolveInTheme(string themeName, string fileName)
        {
            if (string.IsNullOrWhiteSpace(themeName))
            {
                return null;
            }

            foreach (string baseFolder in OceanyaClient.Features.Viewport.AO2ThemeCatalog.GetAo2ThemeScanFolders())
            {
                string candidate = Path.Combine(baseFolder, "themes", themeName, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a theme image by name, probing AO2's extension order.
        /// </summary>
        /// <param name="themeName">Theme to search.</param>
        /// <param name="imageName">Image name without extension, as passed to AO2's setImage.</param>
        /// <returns>The path, or null when no theme provides it.</returns>
        public static string? ResolveThemeImage(string themeName, string imageName)
        {
            // Every extension is probed inside the theme itself before the default theme is considered,
            // which is AO2's order (`get_image_suffix` over one `get_theme_path` at a time). Probing
            // extension-first instead handed GrayGarden's `holdit.png` over to `default/holdit.gif`.
            foreach (string theme in new[] { themeName, "default" })
            {
                foreach (string extension in ImageExtensions)
                {
                    string? path = ResolveInTheme(theme, imageName + extension);
                    if (path != null)
                    {
                        return path;
                    }
                }
            }

            return null;
        }

        private static OceanyaPanelPlacementState ResolveState(OceanyaThemeLayoutState layout, string panelId)
        {
            if (layout.Panels.TryGetValue(panelId, out OceanyaPanelPlacementState? existing) && existing != null)
            {
                return existing;
            }

            OceanyaPanelDescriptor? descriptor = OceanyaPanelCatalog.TryGet(panelId);
            OceanyaPanelPlacementState state = descriptor != null
                ? ToState(descriptor.Placement)
                : new OceanyaPanelPlacementState();
            layout.Panels[panelId] = state;
            return state;
        }

        /// <summary>
        /// Places the panels that make up one AO2 widget together, derived from that widget's rectangle.
        /// </summary>
        /// <remarks>
        /// AO2 describes the OOC block as a single `server_chatlog` rectangle, while Oceanya splits it into
        /// a background, chat text and a header bar. Leaving the children at their Oceanya defaults tore the
        /// block apart on import, so they are laid out inside the imported rectangle instead.
        /// </remarks>
        /// <param name="entries">Design entries.</param>
        /// <param name="scale">Theme scaling factor.</param>
        /// <param name="horizontalShift">Shift applied for the clients strip.</param>
        /// <param name="layout">Layout being built.</param>
        /// <param name="hidden">Hidden panel ids, updated when a child is no longer hidden.</param>
        private static void ApplyCompositeRegions(
            IReadOnlyDictionary<string, string> entries,
            int scale,
            double horizontalShift,
            OceanyaThemeLayoutState layout,
            List<string> hidden)
        {
            if (!TryResolveRectangle(entries, new[] { "server_chatlog", "ms_chatlog" }, scale, out OceanyaPanelPlacement oocRect))
            {
                return;
            }

            double left = oocRect.Left + horizontalShift;

            // AO2's own counterpart of our OOC header is the music display: an image widget
            // (`music_display`) with a text label inside it (`music_name`, whose coordinates are relative
            // to the display - AO2's design file says so in a comment). When the theme describes those, the
            // header goes where the theme puts it and the OOC chat gets the whole `server_chatlog` rect.
            bool headerPlacedByTheme = TryPlaceMusicDisplayHeader(entries, scale, horizontalShift, layout, hidden, oocRect);
            OceanyaPanelPlacement? musicNameRect = headerPlacedByTheme
                ? null
                : ResolveMusicNamePlacement(entries, scale, horizontalShift);

            // The chat only gives up a strip when the header text actually sits on top of it. When the theme
            // puts that text somewhere else - GrayGarden draws it beside the realization button - the chat
            // takes the whole rectangle, the way AO2's server chatlog does.
            double headerHeight = headerPlacedByTheme || musicNameRect != null
                ? 0
                : Math.Min(24 * scale, Math.Max(12, oocRect.Height / 4));

            if (!headerPlacedByTheme)
            {
                // Nothing in the theme describes our own header bar, so it is hidden rather than invented;
                // the label still needs somewhere to live, and the theme does say where its music name goes.
                OceanyaPanelPlacementState backdrop = ResolveState(layout, OceanyaPanelCatalog.OocStreamBackdropPanelId);
                backdrop.IsHidden = true;
                if (!hidden.Contains(OceanyaPanelCatalog.OocStreamBackdropPanelId))
                {
                    hidden.Add(OceanyaPanelCatalog.OocStreamBackdropPanelId);
                }

                PlaceComposite(
                    layout,
                    hidden,
                    OceanyaPanelCatalog.OocStreamTextPanelId,
                    musicNameRect ?? new OceanyaPanelPlacement(left, oocRect.Top, oocRect.Width, headerHeight));
            }

            PlaceComposite(layout, hidden, OceanyaPanelCatalog.OocChatPanelId,
                new OceanyaPanelPlacement(
                    left,
                    oocRect.Top + headerHeight,
                    oocRect.Width,
                    Math.Max(1, oocRect.Height - headerHeight)));
        }

        /// <summary>
        /// Places the OOC header from the theme's music display, when the theme uses it as one.
        /// </summary>
        /// <remarks>
        /// In AO2's default theme `music_display` is a 26px image bar sitting on top of the server chatlog,
        /// at the same x and the same width, with `music_name` as the label drawn inside it (its coordinates
        /// are RELATIVE to the display - AO2's design file says so in a comment). That is exactly our OOC
        /// header: a backdrop with one line of text on it.
        ///
        /// A theme can also repurpose the widget as a plain decoration - GrayGarden's is `0,0,1262,700`
        /// with a window-sized overlay image - and importing that as an "OOC header backdrop" produced a
        /// window-sized black bar. So the header mapping is only used when the rectangle is actually shaped
        /// like a header for this OOC log; otherwise the artwork is imported as a decoration and our own
        /// backdrop is hidden, because nothing in the theme describes it.
        /// </remarks>
        /// <param name="entries">Design entries.</param>
        /// <param name="scale">Theme scaling factor.</param>
        /// <param name="horizontalShift">Clients strip offset.</param>
        /// <param name="layout">Layout being built.</param>
        /// <param name="hidden">Hidden panel list to update.</param>
        /// <param name="oocRect">The OOC log rectangle the header would sit on.</param>
        /// <returns>True when the theme described the header.</returns>
        private static bool TryPlaceMusicDisplayHeader(
            IReadOnlyDictionary<string, string> entries,
            int scale,
            double horizontalShift,
            OceanyaThemeLayoutState layout,
            List<string> hidden,
            OceanyaPanelPlacement oocRect)
        {
            if (!TryResolveRectangle(entries, new[] { "music_display" }, scale, out OceanyaPanelPlacement display))
            {
                return false;
            }

            if (!IsHeaderShaped(display, oocRect))
            {
                return false;
            }

            PlaceComposite(layout, hidden, OceanyaPanelCatalog.OocStreamBackdropPanelId,
                new OceanyaPanelPlacement(display.Left + horizontalShift, display.Top, display.Width, display.Height));

            OceanyaPanelPlacement label = TryResolveRectangle(entries, new[] { "music_name" }, scale, out OceanyaPanelPlacement name)
                ? new OceanyaPanelPlacement(
                    display.Left + name.Left + horizontalShift,
                    display.Top + name.Top,
                    name.Width,
                    name.Height)
                : new OceanyaPanelPlacement(display.Left + horizontalShift, display.Top, display.Width, display.Height);

            PlaceComposite(layout, hidden, OceanyaPanelCatalog.OocStreamTextPanelId, label);
            return true;
        }

        /// <summary>
        /// Resolves where the theme draws its music name, which is where the OOC header text goes.
        /// </summary>
        /// <param name="entries">Design entries.</param>
        /// <param name="scale">Theme scaling factor.</param>
        /// <param name="horizontalShift">Clients strip offset.</param>
        /// <returns>The label's absolute rectangle, or null when the theme has no music name.</returns>
        private static OceanyaPanelPlacement? ResolveMusicNamePlacement(
            IReadOnlyDictionary<string, string> entries,
            int scale,
            double horizontalShift)
        {
            if (!TryResolveRectangle(entries, new[] { "music_display" }, scale, out OceanyaPanelPlacement display)
                || !TryResolveRectangle(entries, new[] { "music_name" }, scale, out OceanyaPanelPlacement name))
            {
                return null;
            }

            // music_name's coordinates are relative to the display, as AO2's design file comments.
            return new OceanyaPanelPlacement(
                display.Left + name.Left + horizontalShift,
                display.Top + name.Top,
                name.Width,
                name.Height);
        }

        /// <summary>
        /// Gets a value indicating whether a rectangle is shaped like a header bar for the OOC log.
        /// </summary>
        /// <param name="candidate">Rectangle to test.</param>
        /// <param name="oocRect">The OOC log rectangle.</param>
        /// <returns>True when it is a short bar overlapping the log's column.</returns>
        private static bool IsHeaderShaped(OceanyaPanelPlacement candidate, OceanyaPanelPlacement oocRect)
        {
            bool shortEnough = candidate.Height <= Math.Max(32, oocRect.Height / 3);
            bool sameColumn = candidate.Left < oocRect.Left + oocRect.Width
                && candidate.Left + candidate.Width > oocRect.Left;
            return shortEnough && sameColumn;
        }

        private static void PlaceComposite(
            OceanyaThemeLayoutState layout,
            List<string> hidden,
            string panelId,
            OceanyaPanelPlacement placement)
        {
            OceanyaPanelDescriptor? descriptor = OceanyaPanelCatalog.TryGet(panelId);
            if (descriptor == null)
            {
                return;
            }

            OceanyaPanelPlacementState state = ResolveState(layout, panelId);
            state.Left = placement.Left;
            state.Top = placement.Top;
            state.Width = Math.Max(descriptor.MinimumWidth, placement.Width);
            state.Height = Math.Max(descriptor.MinimumHeight, placement.Height);
            state.IsHidden = false;
            hidden.Remove(panelId);
        }

        /// <summary>
        /// Parses an AO2 design file into identifier/value pairs.
        /// </summary>
        /// <param name="filePath">Path of a <c>courtroom_design.ini</c>.</param>
        /// <returns>Entries keyed case-insensitively, or an empty dictionary when the file is unreadable.</returns>
        public static Dictionary<string, string> ParseDesignFile(string filePath)
        {
            Dictionary<string, string> entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return entries;
            }

            try
            {
                foreach (string rawLine in File.ReadLines(filePath))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("/", StringComparison.Ordinal)
                        || line.StartsWith(";", StringComparison.Ordinal)
                        || line.StartsWith("#", StringComparison.Ordinal)
                        || line.StartsWith("[", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    if (key.Length > 0)
                    {
                        entries[key] = value;
                    }
                }
            }
            catch (IOException exception)
            {
                CustomConsole.Warning($"AO2 theme design file could not be read: {filePath}", exception);
            }

            return entries;
        }

        private static bool IsMappedIdentifier(string identifier)
        {
            return PanelMappings.Any(mapping =>
                mapping.Identifiers.Any(mapped => string.Equals(mapped, identifier, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool IsRectangleEntry(string value)
        {
            return TryParseRectangle(value, 1, out _);
        }

        private static bool TryResolveRectangle(
            IReadOnlyDictionary<string, string> entries,
            IEnumerable<string> identifiers,
            int scale,
            out OceanyaPanelPlacement placement)
        {
            foreach (string identifier in identifiers)
            {
                if (entries.TryGetValue(identifier, out string? value)
                    && TryParseRectangle(value, scale, out placement))
                {
                    return true;
                }
            }

            placement = default;
            return false;
        }

        private static bool TryParseRectangle(string value, int scale, out OceanyaPanelPlacement placement)
        {
            placement = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string[] parts = value.Split(',');
            if (parts.Length < 4)
            {
                return false;
            }

            if (!TryParseInt(parts[0], out int x)
                || !TryParseInt(parts[1], out int y)
                || !TryParseInt(parts[2], out int width)
                || !TryParseInt(parts[3], out int height))
            {
                return false;
            }

            if (width <= 0 || height <= 0)
            {
                return false;
            }

            placement = new OceanyaPanelPlacement(x * scale, y * scale, width * scale, height * scale);
            return true;
        }

        private static bool TryParseInt(string value, out int parsed)
        {
            return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
        }

        /// <summary>
        /// Translates AO2's emote button metrics into the emote grid's item size.
        /// </summary>
        /// <remarks>
        /// AO2 does not stretch emote buttons to fill their area: it reads `emote_button_size` and
        /// `emote_button_spacing` and fits as many fixed-size buttons as the area allows
        /// (`AO2-Client/src/emotes.cpp`). Our grid does the same from one item size, which is the field
        /// the grid settings popup already exposes.
        /// </remarks>
        /// <param name="entries">Design entries of the theme being imported.</param>
        /// <param name="scale">Theme scaling factor.</param>
        /// <param name="layout">Layout being built.</param>
        private static void ApplyEmoteGridItemSize(
            IReadOnlyDictionary<string, string> entries,
            int scale,
            OceanyaThemeLayoutState layout)
        {
            (double width, double height) = ResolveButtonMetric(
                entries,
                "emote_button_size",
                DefaultEmoteButtonSize,
                DefaultEmoteButtonSize);
            (double spacingX, double spacingY) = ResolveButtonMetric(
                entries,
                "emote_button_spacing",
                DefaultEmoteButtonSpacing,
                DefaultEmoteButtonSpacing);
            if (width <= 0 || height <= 0)
            {
                return;
            }

            OceanyaPanelPlacementState state = ResolveState(layout, OceanyaPanelCatalog.IcEmoteGridPanelId);
            state.ItemSize = width * scale;
            state.ItemHeight = height * scale;
            state.ItemSpacingX = spacingX * scale;
            state.ItemSpacingY = spacingY * scale;

            // AO2's `emotes` rectangle is nothing but buttons: the arrows are separate widgets placed by
            // the theme, so the grid must not keep the inset it uses in the stock layout.
            state.ReservePagingSpace = false;
        }

        /// <summary>
        /// Reads an AO2 `w, h` metric pair.
        /// </summary>
        /// <param name="entries">Design entries.</param>
        /// <param name="identifier">Metric identifier.</param>
        /// <param name="fallbackX">Horizontal value AO2's default theme declares, when the theme omits it.</param>
        /// <param name="fallbackY">Vertical value AO2's default theme declares, when the theme omits it.</param>
        /// <returns>The resolved metric pair.</returns>
        private static (double X, double Y) ResolveButtonMetric(
            IReadOnlyDictionary<string, string> entries,
            string identifier,
            double fallbackX,
            double fallbackY)
        {
            if (!entries.TryGetValue(identifier, out string? value))
            {
                return (fallbackX, fallbackY);
            }

            string[] parts = value.Split(',');
            if (parts.Length < 2 || !TryParseInt(parts[0], out int x) || !TryParseInt(parts[1], out int y))
            {
                return (fallbackX, fallbackY);
            }

            return (x, y);
        }

        private static (double Width, double Height) ResolveSurfaceSize(
            IReadOnlyDictionary<string, string> entries,
            int scale,
            OceanyaThemeLayoutState layout)
        {
            if (TryResolveRectangle(entries, new[] { "courtroom" }, scale, out OceanyaPanelPlacement courtroom))
            {
                return (courtroom.Width + ClientsStripWidth, courtroom.Height);
            }

            // No window size in the theme: fall back to the extent of what was imported.
            double width = layout.Panels.Values.Where(state => !state.IsHidden).Select(state => state.Left + state.Width).DefaultIfEmpty(0).Max();
            double height = layout.Panels.Values.Where(state => !state.IsHidden).Select(state => state.Top + state.Height).DefaultIfEmpty(0).Max();
            return (Math.Max(width, ClientsStripWidth), height);
        }

        /// <summary>
        /// Places the Oceanya-only functional panels in a left-hand strip: the clients list fills the
        /// height, with the remaining buttons and checkboxes stacked underneath its header.
        /// </summary>
        /// <param name="layout">Layout being built.</param>
        /// <param name="surfaceHeight">Height of the imported surface.</param>
        /// <param name="strandedEssentials">
        /// Mapped panels the theme did not describe, which are parked in the strip instead of hidden.
        /// </param>
        private static void PlaceFunctionalPanels(
            OceanyaThemeLayoutState layout,
            double surfaceHeight,
            IReadOnlyList<string> strandedEssentials)
        {
            List<string> stripPanels = FunctionalOnlyPanelIds.Concat(strandedEssentials).ToList();
            double cursorTop = 2;
            foreach (string panelId in stripPanels)
            {
                OceanyaPanelDescriptor? descriptor = OceanyaPanelCatalog.TryGet(panelId);
                if (descriptor == null)
                {
                    continue;
                }

                if (string.Equals(panelId, OceanyaPanelCatalog.ClientsTitlePanelId, StringComparison.Ordinal))
                {
                    layout.Panels[panelId] = ToState(new OceanyaPanelPlacement(2, cursorTop, ClientsStripWidth - 4, 20));
                    cursorTop += 22;
                    continue;
                }

                if (string.Equals(panelId, OceanyaPanelCatalog.ClientsAddPanelId, StringComparison.Ordinal))
                {
                    layout.Panels[panelId] = ToState(new OceanyaPanelPlacement(2, cursorTop, 24, 24));
                    continue;
                }

                if (string.Equals(panelId, OceanyaPanelCatalog.ClientsRemovePanelId, StringComparison.Ordinal))
                {
                    layout.Panels[panelId] = ToState(new OceanyaPanelPlacement(28, cursorTop, 24, 24));
                    cursorTop += 26;
                    continue;
                }

                if (string.Equals(panelId, OceanyaPanelCatalog.ClientsListPanelId, StringComparison.Ordinal))
                {
                    // The clients list takes whatever height is left, minus room for everything stacked
                    // below it. Reserving a fixed four rows was why the last few panels ended up pushed off
                    // the bottom of the window once more controls moved into the strip.
                    double reserved = 4 + ResolveStripTailHeight(stripPanels, panelId);
                    double listHeight = Math.Max(descriptor.MinimumHeight, surfaceHeight - cursorTop - reserved);
                    layout.Panels[panelId] = ToState(new OceanyaPanelPlacement(2, cursorTop, ClientsStripWidth - 4, listHeight));
                    cursorTop += listHeight + 4;
                    continue;
                }

                double height = Math.Max(descriptor.MinimumHeight, descriptor.Placement.Height);

                // A theme that describes almost nothing sends every essential panel to the strip, which can
                // ask for more height than the window has. Clamping keeps the overflow stacked at the bottom
                // edge - overlapping is ugly but reachable, whereas off the edge is lost.
                double top = Math.Min(cursorTop, Math.Max(0, surfaceHeight - height - 2));
                OceanyaPanelPlacementState parked = ResolveState(layout, panelId);
                parked.Left = 2;
                parked.Top = top;
                parked.Width = ClientsStripWidth - 4;
                parked.Height = height;
                parked.IsHidden = false;
                cursorTop += height + 2;
            }
        }

        /// <summary>
        /// Adds up the height of the strip entries that come after a given panel.
        /// </summary>
        /// <param name="stripPanels">Every panel packed into the strip, in order.</param>
        /// <param name="afterPanelId">Panel to measure from, exclusive.</param>
        /// <returns>Total height including the gaps between entries.</returns>
        private static double ResolveStripTailHeight(IReadOnlyList<string> stripPanels, string afterPanelId)
        {
            double total = 0;
            bool counting = false;
            foreach (string panelId in stripPanels)
            {
                if (!counting)
                {
                    counting = string.Equals(panelId, afterPanelId, StringComparison.Ordinal);
                    continue;
                }

                OceanyaPanelDescriptor? descriptor = OceanyaPanelCatalog.TryGet(panelId);
                if (descriptor != null)
                {
                    total += Math.Max(descriptor.MinimumHeight, descriptor.Placement.Height) + 2;
                }
            }

            return total;
        }

        private static OceanyaPanelPlacementState ToState(OceanyaPanelPlacement placement)
        {
            return new OceanyaPanelPlacementState
            {
                Left = placement.Left,
                Top = placement.Top,
                Width = placement.Width,
                Height = placement.Height,

                // A Qt widget has no frame unless the theme's stylesheet draws one, while several of our
                // stock controls do (the emote grid's grey outline, the dropdown borders). Clearing them
                // here lets the stylesheet put back exactly the borders the theme actually asks for.
                BorderThickness = 0
            };
        }
    }
}
