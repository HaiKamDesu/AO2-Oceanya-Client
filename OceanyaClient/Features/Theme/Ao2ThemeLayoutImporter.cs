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
    /// <param name="SurfaceHeight">Window height the theme declares, after scaling.</param>
    /// <param name="ImportedPanelIds">Panels that got a placement from the theme.</param>
    /// <param name="HiddenPanelIds">Panels hidden because the theme has no equivalent.</param>
    /// <param name="UnmappedIdentifiers">AO2 identifiers the theme defines that Oceanya has no panel for.</param>
    public readonly record struct Ao2ThemeImportResult(
        OceanyaThemeLayoutState Layout,
        double SurfaceWidth,
        double SurfaceHeight,
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
            (OceanyaPanelCatalog.IcButtonOffsetPanelId, new[] { "pair_offset_spinbox" }),
            (OceanyaPanelCatalog.BarButtonSettingsPanelId, new[] { "settings" }),
            (OceanyaPanelCatalog.BarButtonMusicPanelId, new[] { "music_list" }),
            (OceanyaPanelCatalog.BarButtonAreaPanelId, new[] { "area_list" }),
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
        /// Panels Oceanya needs regardless of the theme. They are packed into a strip on the left, next
        /// to the clients list, so nothing important disappears just because AO2 has no name for it.
        /// </summary>
        private static readonly string[] FunctionalOnlyPanelIds =
        {
            OceanyaPanelCatalog.ClientsTitlePanelId,
            OceanyaPanelCatalog.ClientsAddPanelId,
            OceanyaPanelCatalog.ClientsRemovePanelId,
            OceanyaPanelCatalog.ClientsListPanelId,
            OceanyaPanelCatalog.BarButtonEditLayoutPanelId,
            OceanyaPanelCatalog.BarButtonRefreshPanelId,
            OceanyaPanelCatalog.BarButtonViewportPanelId,
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
            "music_display",
            "music_name",
            "emote_left",
            "emote_right",
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
            }

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

            (double surfaceWidth, double surfaceHeight) = ResolveSurfaceSize(designEntries, scale, layout);
            PlaceFunctionalPanels(layout, surfaceHeight);

            List<string> unmapped = designEntries.Keys
                .Where(identifier => !IsMappedIdentifier(identifier)
                    && !IdentifiersHandledElsewhere.Contains(identifier)
                    && IsRectangleEntry(designEntries[identifier]))
                .OrderBy(identifier => identifier, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new Ao2ThemeImportResult(layout, surfaceWidth, surfaceHeight, imported, hidden, unmapped);
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

            return Translate(entries, scalingFactor);
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
        private static void PlaceFunctionalPanels(OceanyaThemeLayoutState layout, double surfaceHeight)
        {
            double cursorTop = 2;
            foreach (string panelId in FunctionalOnlyPanelIds)
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
                    // The clients list takes whatever height is left, minus room for thebuttons below.
                    double reserved = 4 + (26 * 4);
                    double listHeight = Math.Max(descriptor.MinimumHeight, surfaceHeight - cursorTop - reserved);
                    layout.Panels[panelId] = ToState(new OceanyaPanelPlacement(2, cursorTop, ClientsStripWidth - 4, listHeight));
                    cursorTop += listHeight + 4;
                    continue;
                }

                double height = Math.Max(descriptor.MinimumHeight, descriptor.Placement.Height);
                layout.Panels[panelId] = ToState(new OceanyaPanelPlacement(2, cursorTop, ClientsStripWidth - 4, height));
                cursorTop += height + 2;
            }
        }

        private static OceanyaPanelPlacementState ToState(OceanyaPanelPlacement placement)
        {
            return new OceanyaPanelPlacementState
            {
                Left = placement.Left,
                Top = placement.Top,
                Width = placement.Width,
                Height = placement.Height
            };
        }
    }
}
