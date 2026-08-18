using System;
using System.Collections.Generic;
using System.Linq;

namespace OceanyaClient.Features.Theme
{
    /// <summary>
    /// Placement of a panel inside the GM main window surface, in unscaled layout units.
    /// </summary>
    /// <param name="Left">Distance from the surface's left edge.</param>
    /// <param name="Top">Distance from the surface's top edge.</param>
    /// <param name="Width">Panel width.</param>
    /// <param name="Height">Panel height.</param>
    public readonly record struct OceanyaPanelPlacement(double Left, double Top, double Width, double Height);

    /// <summary>
    /// The kind of control a panel wraps, which decides how it may be resized and which style
    /// options its editor offers.
    /// </summary>
    public enum OceanyaPanelKind
    {
        /// <summary>Free-form region with no special rules (backgrounds, grouped blocks).</summary>
        Static = 0,

        /// <summary>A button whose face is an image: offers image scaling options.</summary>
        ImageButton = 1,

        /// <summary>A text box: width is user-controlled, height follows the font.</summary>
        TextInput = 2,

        /// <summary>A dropdown: width is user-controlled, height follows the font.</summary>
        Dropdown = 3,

        /// <summary>A paged item grid: resizing changes how many items fit.</summary>
        ItemGrid = 4,

        /// <summary>A checkbox or label: height follows the font.</summary>
        TextToggle = 5
    }

    /// <summary>
    /// Describes one dockable region of the GM main window.
    /// </summary>
    /// <remarks>
    /// Panel ids are part of the shared Oceanya theme file, so they are stable strings: renaming one
    /// breaks every theme that references it and requires a theme compatibility bump. See
    /// <c>Documentation/OceanyaThemeSystem.md</c>.
    /// </remarks>
    public sealed class OceanyaPanelDescriptor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OceanyaPanelDescriptor"/> class.
        /// </summary>
        /// <param name="id">Stable panel id used by theme files.</param>
        /// <param name="displayName">Human readable name shown in the theme editor.</param>
        /// <param name="placement">Placement matching the pre-theme fixed layout.</param>
        /// <param name="minimumWidth">Smallest width the panel stays usable at.</param>
        /// <param name="minimumHeight">Smallest height the panel stays usable at.</param>
        /// <param name="backdropPlacement">Placement of the panel's backdrop element, when it has one.</param>
        /// <param name="kind">Control kind, which decides the resize rules and editor options.</param>
        public OceanyaPanelDescriptor(
            string id,
            string displayName,
            OceanyaPanelPlacement placement,
            double minimumWidth,
            double minimumHeight,
            OceanyaPanelPlacement? backdropPlacement = null,
            OceanyaPanelKind kind = OceanyaPanelKind.Static)
        {
            Id = id;
            DisplayName = displayName;
            Placement = placement;
            MinimumWidth = minimumWidth;
            MinimumHeight = minimumHeight;
            BackdropPlacement = backdropPlacement;
            Kind = kind;
        }

        /// <summary>Gets the stable panel id used by theme files.</summary>
        public string Id { get; }

        /// <summary>Gets the human readable panel name.</summary>
        public string DisplayName { get; }

        /// <summary>Gets the default placement, which reproduces the pre-theme fixed layout.</summary>
        public OceanyaPanelPlacement Placement { get; }

        /// <summary>Gets the smallest width the panel stays usable at.</summary>
        public double MinimumWidth { get; }

        /// <summary>Gets the smallest height the panel stays usable at.</summary>
        public double MinimumHeight { get; }

        /// <summary>Gets the backdrop placement, for panels drawn on top of a separate background element.</summary>
        public OceanyaPanelPlacement? BackdropPlacement { get; }

        /// <summary>Gets the control kind, which decides resize rules and editor options.</summary>
        public OceanyaPanelKind Kind { get; }

        /// <summary>
        /// Gets a value indicating whether the user may change this panel's height directly. Text
        /// controls derive their height from their font instead.
        /// </summary>
        public bool AllowsVerticalResize => Kind is not (OceanyaPanelKind.TextInput or OceanyaPanelKind.Dropdown or OceanyaPanelKind.TextToggle);

        /// <summary>Gets a value indicating whether the user may change this panel's width directly.</summary>
        public bool AllowsHorizontalResize => true;
    }

    /// <summary>
    /// Registry of the GM main window's dockable panels.
    /// </summary>
    /// <remarks>
    /// This is the first step of the Oceanya theme system: placement moves out of MainWindow.xaml and
    /// into data, so a later dock host (and the theme file) can drive it. The values here reproduce the
    /// historic fixed layout exactly, so registering a panel changes nothing visually.
    /// </remarks>
    public static class OceanyaPanelCatalog
    {
        /// <summary>Panel id for the IC chat log.</summary>
        public const string IcLogPanelId = "ic_log";

        /// <summary>Panel id for the OOC chat log.</summary>
        public const string OocLogPanelId = "ooc_log";

        /// <summary>
        /// Panel id for the client strip (header, add/remove buttons and the paged client buttons).
        /// </summary>
        /// <remarks>
        /// The control behind it is a <c>PageButtonGrid</c> historically named "EmoteGrid" in
        /// MainWindow.xaml, but it holds CLIENT buttons; the emote grid lives inside ICMessageSettings.
        /// </remarks>
        public const string ClientsListPanelId = "clients_list";

        /// <summary>Panel id for the IC message settings strip.</summary>
        public const string IcSettingsPanelId = "ic_settings";

        /// <summary>Panel id for the artwork behind the shout buttons.</summary>
        public const string ShoutBackdropPanelId = "shout_backdrop";

        /// <summary>Panel id for the Hold It shout button.</summary>
        public const string ShoutHoldItPanelId = "shout_holdit";

        /// <summary>Panel id for the Objection shout button.</summary>
        public const string ShoutObjectionPanelId = "shout_objection";

        /// <summary>Panel id for the Take That shout button.</summary>
        public const string ShoutTakeThatPanelId = "shout_takethat";

        /// <summary>Panel id for the Custom shout button.</summary>
        public const string ShoutCustomPanelId = "shout_custom";

        /// <summary>Panel id for the ic showname.</summary>
        public const string IcShownamePanelId = "ic_showname";

        /// <summary>Panel id for the ic message.</summary>
        public const string IcMessagePanelId = "ic_message";

        /// <summary>Panel id for the emote grid.</summary>
        public const string IcEmoteGridPanelId = "ic_emote_grid";

        /// <summary>Panel id for the preanim checkbox.</summary>
        public const string IcCheckPreanimPanelId = "ic_check_preanim";

        /// <summary>Panel id for the flip checkbox.</summary>
        public const string IcCheckFlipPanelId = "ic_check_flip";

        /// <summary>Panel id for the additive checkbox.</summary>
        public const string IcCheckAdditivePanelId = "ic_check_additive";

        /// <summary>Panel id for the immediate checkbox.</summary>
        public const string IcCheckImmediatePanelId = "ic_check_immediate";

        /// <summary>Panel id for the character dropdown.</summary>
        public const string IcComboCharacterPanelId = "ic_combo_character";

        /// <summary>Panel id for the emote dropdown.</summary>
        public const string IcComboEmotePanelId = "ic_combo_emote";

        /// <summary>Panel id for the position dropdown.</summary>
        public const string IcComboPositionPanelId = "ic_combo_position";

        /// <summary>Panel id for the text color dropdown.</summary>
        public const string IcComboTextColorPanelId = "ic_combo_textcolor";

        /// <summary>Panel id for the effect dropdown.</summary>
        public const string IcComboEffectPanelId = "ic_combo_effect";

        /// <summary>Panel id for the sfx dropdown.</summary>
        public const string IcComboSfxPanelId = "ic_combo_sfx";

        /// <summary>Panel id for the realization button.</summary>
        public const string IcButtonRealizationPanelId = "ic_button_realization";

        /// <summary>Panel id for the screenshake button.</summary>
        public const string IcButtonScreenshakePanelId = "ic_button_screenshake";

        /// <summary>Panel id for the offset button.</summary>
        public const string IcButtonOffsetPanelId = "ic_button_offset";

        /// <summary>Panel id for the pairing studio button.</summary>
        public const string IcButtonPairingPanelId = "ic_button_pairing";
        /// <summary>Panel id for the ding button.</summary>
        public const string DingButtonPanelId = "ding_button";

        /// <summary>Panel id for the ooc divider.</summary>
        public const string OocDividerPanelId = "ooc_divider";

        /// <summary>Panel id for the catchphrase.</summary>
        public const string IcCatchphrasePanelId = "ic_catchphrase";

        /// <summary>Panel id for the ic settings backdrop.</summary>
        public const string IcSettingsBackdropPanelId = "ic_settings_backdrop";

        /// <summary>Panel id for the loremaster art.</summary>
        public const string IcLoremasterPanelId = "ic_loremaster";
        /// <summary>Panel id for the viewport rendered inside the main window.</summary>
        public const string ViewportPanelId = "viewport";

        /// <summary>Panel id for the bottom status bar.</summary>
        public const string BottomBarPanelId = "bottom_bar";

        /// <summary>Panel id for the sticky effects checkbox.</summary>
        public const string BarCheckStickyPanelId = "bar_check_sticky";

        /// <summary>Panel id for the switch pos checkbox.</summary>
        public const string BarCheckSwitchPosPanelId = "bar_check_switchpos";

        /// <summary>Panel id for the invert ic log checkbox.</summary>
        public const string BarCheckInvertLogPanelId = "bar_check_invertlog";

        /// <summary>Panel id for the edit layout button.</summary>
        public const string BarButtonEditLayoutPanelId = "bar_button_editlayout";

        /// <summary>Panel id for the refresh characters button.</summary>
        public const string BarButtonRefreshPanelId = "bar_button_refresh";

        /// <summary>Panel id for the viewport button.</summary>
        public const string BarButtonViewportPanelId = "bar_button_viewport";

        /// <summary>Panel id for the area navigator button.</summary>
        public const string BarButtonAreaPanelId = "bar_button_area";

        /// <summary>Panel id for the debug button.</summary>
        public const string BarButtonDebugPanelId = "bar_button_debug";

        /// <summary>Panel id for the music list button.</summary>
        public const string BarButtonMusicPanelId = "bar_button_music";

        /// <summary>Panel id for the settings button.</summary>
        public const string BarButtonSettingsPanelId = "bar_button_settings";

        /// <summary>Panel id for the clients title.</summary>
        public const string ClientsTitlePanelId = "clients_title";

        /// <summary>Panel id for the add client button.</summary>
        public const string ClientsAddPanelId = "clients_add";

        /// <summary>Panel id for the remove client button.</summary>
        public const string ClientsRemovePanelId = "clients_remove";
        /// <summary>Panel id for the optional Dredd background overlay row.</summary>
        public const string DreddFeatureRowPanelId = "dredd_row";

        private static readonly IReadOnlyList<OceanyaPanelDescriptor> PanelList = new[]
        {
            new OceanyaPanelDescriptor(
                IcLogPanelId,
                "IC Log",
                new OceanyaPanelPlacement(55, 0, 232, 323),
                minimumWidth: 120,
                minimumHeight: 80),
            new OceanyaPanelDescriptor(
                OocLogPanelId,
                "OOC Log",
                new OceanyaPanelPlacement(287, 0, 222, 333),
                minimumWidth: 120,
                minimumHeight: 80),
            new OceanyaPanelDescriptor(
                ClientsListPanelId,
                "Clients Grid",
                new OceanyaPanelPlacement(2, 48, 50, 250),
                minimumWidth: 44,
                minimumHeight: 120,
                kind: OceanyaPanelKind.ItemGrid),
            new OceanyaPanelDescriptor(
                ShoutBackdropPanelId,
                "Shout Backdrop",
                new OceanyaPanelPlacement(2, 296, 428, 42),
                minimumWidth: 20,
                minimumHeight: 10),
            new OceanyaPanelDescriptor(
                ShoutHoldItPanelId,
                "Hold It",
                new OceanyaPanelPlacement(4, 298, 102, 40),
                minimumWidth: 24,
                minimumHeight: 16,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                ShoutObjectionPanelId,
                "Objection",
                new OceanyaPanelPlacement(111, 298, 102, 40),
                minimumWidth: 24,
                minimumHeight: 16,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                ShoutTakeThatPanelId,
                "Take That",
                new OceanyaPanelPlacement(218, 298, 102, 40),
                minimumWidth: 24,
                minimumHeight: 16,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                ShoutCustomPanelId,
                "Custom",
                new OceanyaPanelPlacement(324, 298, 102, 40),
                minimumWidth: 24,
                minimumHeight: 16,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                IcSettingsPanelId,
                "IC Message Settings",
                new OceanyaPanelPlacement(0, 343, 509, 260),
                minimumWidth: 320,
                minimumHeight: 160),
            new OceanyaPanelDescriptor(
                IcShownamePanelId,
                "IC Showname",
                new OceanyaPanelPlacement(4, 343, 78, 17),
                minimumWidth: 40,
                minimumHeight: 14,
                kind: OceanyaPanelKind.TextInput),
            new OceanyaPanelDescriptor(
                IcMessagePanelId,
                "IC Message",
                new OceanyaPanelPlacement(87, 343, 422, 17),
                minimumWidth: 80,
                minimumHeight: 14,
                kind: OceanyaPanelKind.TextInput),
            new OceanyaPanelDescriptor(
                IcEmoteGridPanelId,
                "Emote Grid",
                new OceanyaPanelPlacement(4, 360, 505, 120),
                minimumWidth: 60,
                minimumHeight: 40,
                kind: OceanyaPanelKind.ItemGrid),
            new OceanyaPanelDescriptor(
                IcCheckPreanimPanelId,
                "Preanim Checkbox",
                new OceanyaPanelPlacement(4, 537, 58, 16),
                minimumWidth: 40,
                minimumHeight: 14,
                kind: OceanyaPanelKind.TextToggle),
            new OceanyaPanelDescriptor(
                IcCheckFlipPanelId,
                "Flip Checkbox",
                new OceanyaPanelPlacement(67, 537, 37, 16),
                minimumWidth: 30,
                minimumHeight: 14,
                kind: OceanyaPanelKind.TextToggle),
            new OceanyaPanelDescriptor(
                IcCheckAdditivePanelId,
                "Additive Checkbox",
                new OceanyaPanelPlacement(109, 537, 56, 16),
                minimumWidth: 40,
                minimumHeight: 14,
                kind: OceanyaPanelKind.TextToggle),
            new OceanyaPanelDescriptor(
                IcCheckImmediatePanelId,
                "Immediate Checkbox",
                new OceanyaPanelPlacement(170, 537, 73, 16),
                minimumWidth: 40,
                minimumHeight: 14,
                kind: OceanyaPanelKind.TextToggle),
            new OceanyaPanelDescriptor(
                IcComboCharacterPanelId,
                "Character Dropdown",
                new OceanyaPanelPlacement(4, 485, 140, 21),
                minimumWidth: 60,
                minimumHeight: 18,
                kind: OceanyaPanelKind.Dropdown),
            new OceanyaPanelDescriptor(
                IcComboEmotePanelId,
                "Emote Dropdown",
                new OceanyaPanelPlacement(149, 485, 140, 21),
                minimumWidth: 60,
                minimumHeight: 18,
                kind: OceanyaPanelKind.Dropdown),
            new OceanyaPanelDescriptor(
                IcComboPositionPanelId,
                "Position Dropdown",
                new OceanyaPanelPlacement(294, 485, 140, 21),
                minimumWidth: 60,
                minimumHeight: 18,
                kind: OceanyaPanelKind.Dropdown),
            new OceanyaPanelDescriptor(
                IcComboTextColorPanelId,
                "Text Color Dropdown",
                new OceanyaPanelPlacement(4, 511, 140, 21),
                minimumWidth: 60,
                minimumHeight: 18,
                kind: OceanyaPanelKind.Dropdown),
            new OceanyaPanelDescriptor(
                IcComboEffectPanelId,
                "Effect Dropdown",
                new OceanyaPanelPlacement(149, 511, 140, 21),
                minimumWidth: 60,
                minimumHeight: 18,
                kind: OceanyaPanelKind.Dropdown),
            new OceanyaPanelDescriptor(
                IcComboSfxPanelId,
                "SFX Dropdown",
                new OceanyaPanelPlacement(294, 511, 140, 21),
                minimumWidth: 60,
                minimumHeight: 18,
                kind: OceanyaPanelKind.Dropdown),
            new OceanyaPanelDescriptor(
                IcButtonRealizationPanelId,
                "Realization Button",
                new OceanyaPanelPlacement(12, 558, 42, 42),
                minimumWidth: 16,
                minimumHeight: 16,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                IcButtonScreenshakePanelId,
                "Screenshake Button",
                new OceanyaPanelPlacement(59, 558, 42, 42),
                minimumWidth: 16,
                minimumHeight: 16,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                IcButtonOffsetPanelId,
                "Offset Button",
                new OceanyaPanelPlacement(106, 558, 42, 42),
                minimumWidth: 16,
                minimumHeight: 16,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                IcButtonPairingPanelId,
                "Pairing Studio Button",
                new OceanyaPanelPlacement(153, 558, 42, 42),
                minimumWidth: 16,
                minimumHeight: 16,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                DingButtonPanelId,
                "Ding Button",
                new OceanyaPanelPlacement(322, 532, 12, 12),
                minimumWidth: 6,
                minimumHeight: 6,
                kind: OceanyaPanelKind.Static),
            new OceanyaPanelDescriptor(
                OocDividerPanelId,
                "OOC Divider",
                new OceanyaPanelPlacement(426, 318, 83, 18),
                minimumWidth: 8,
                minimumHeight: 4,
                kind: OceanyaPanelKind.Static),
            new OceanyaPanelDescriptor(
                IcCatchphrasePanelId,
                "Catchphrase",
                new OceanyaPanelPlacement(4, 446, 505, 34),
                minimumWidth: 40,
                minimumHeight: 12,
                kind: OceanyaPanelKind.TextToggle),
            new OceanyaPanelDescriptor(
                IcSettingsBackdropPanelId,
                "IC Settings Backdrop",
                new OceanyaPanelPlacement(0, 480, 528, 123),
                minimumWidth: 40,
                minimumHeight: 20,
                kind: OceanyaPanelKind.Static),
            new OceanyaPanelDescriptor(
                IcLoremasterPanelId,
                "Loremaster Art",
                new OceanyaPanelPlacement(332, 486, 195, 118),
                minimumWidth: 20,
                minimumHeight: 20,
                kind: OceanyaPanelKind.Static),
            new OceanyaPanelDescriptor(
                ViewportPanelId,
                "Viewport",
                new OceanyaPanelPlacement(0, 0, 256, 192),
                minimumWidth: 96,
                minimumHeight: 72),
            new OceanyaPanelDescriptor(
                BottomBarPanelId,
                "Bottom Bar",
                new OceanyaPanelPlacement(0, 603, 509, 24),
                minimumWidth: 320,
                minimumHeight: 24),
            new OceanyaPanelDescriptor(
                BarCheckStickyPanelId,
                "Sticky Effects Checkbox",
                new OceanyaPanelPlacement(10, 607, 82, 16),
                minimumWidth: 40,
                minimumHeight: 14,
                kind: OceanyaPanelKind.TextToggle),
            new OceanyaPanelDescriptor(
                BarCheckSwitchPosPanelId,
                "Switch Pos Checkbox",
                new OceanyaPanelPlacement(97, 607, 127, 16),
                minimumWidth: 40,
                minimumHeight: 14,
                kind: OceanyaPanelKind.TextToggle),
            new OceanyaPanelDescriptor(
                BarCheckInvertLogPanelId,
                "Invert IC Log Checkbox",
                new OceanyaPanelPlacement(229, 607, 127, 16),
                minimumWidth: 40,
                minimumHeight: 14,
                kind: OceanyaPanelKind.TextToggle),
            new OceanyaPanelDescriptor(
                BarButtonEditLayoutPanelId,
                "Edit Layout Button",
                new OceanyaPanelPlacement(356, 605, 20, 20),
                minimumWidth: 16,
                minimumHeight: 16),
            new OceanyaPanelDescriptor(
                BarButtonRefreshPanelId,
                "Refresh Characters Button",
                new OceanyaPanelPlacement(377, 603, 24, 24),
                minimumWidth: 16,
                minimumHeight: 16,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                BarButtonViewportPanelId,
                "Viewport Button",
                new OceanyaPanelPlacement(404, 603, 24, 24),
                minimumWidth: 16,
                minimumHeight: 16,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                BarButtonAreaPanelId,
                "Area Navigator Button",
                new OceanyaPanelPlacement(431, 603, 24, 24),
                minimumWidth: 16,
                minimumHeight: 16,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                BarButtonDebugPanelId,
                "Debug Button",
                new OceanyaPanelPlacement(442, 607, 38, 18),
                minimumWidth: 16,
                minimumHeight: 14),
            new OceanyaPanelDescriptor(
                BarButtonMusicPanelId,
                "Music List Button",
                new OceanyaPanelPlacement(458, 603, 24, 24),
                minimumWidth: 16,
                minimumHeight: 16,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                BarButtonSettingsPanelId,
                "Settings Button",
                new OceanyaPanelPlacement(485, 603, 24, 24),
                minimumWidth: 16,
                minimumHeight: 16,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                ClientsTitlePanelId,
                "Clients Title",
                new OceanyaPanelPlacement(4, 2, 46, 24),
                minimumWidth: 20,
                minimumHeight: 12,
                kind: OceanyaPanelKind.TextToggle),
            new OceanyaPanelDescriptor(
                ClientsAddPanelId,
                "Add Client Button",
                new OceanyaPanelPlacement(2, 24, 24, 24),
                minimumWidth: 12,
                minimumHeight: 12,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                ClientsRemovePanelId,
                "Remove Client Button",
                new OceanyaPanelPlacement(28, 24, 24, 24),
                minimumWidth: 12,
                minimumHeight: 12,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                DreddFeatureRowPanelId,
                "Dredd Overlay Row",
                new OceanyaPanelPlacement(0, 603, 509, 30),
                minimumWidth: 320,
                minimumHeight: 30)
        };

        private static readonly Dictionary<string, OceanyaPanelDescriptor> CustomPanels =
            new Dictionary<string, OceanyaPanelDescriptor>(StringComparer.Ordinal);

        /// <summary>
        /// Gets every registered panel, built-in and user-added.
        /// </summary>
        public static IReadOnlyList<OceanyaPanelDescriptor> Panels =>
            PanelList.Concat(CustomPanels.Values).ToList();

        /// <summary>
        /// Gets the built-in panels only, ignoring user-added ones.
        /// </summary>
        public static IReadOnlyList<OceanyaPanelDescriptor> BuiltInPanels => PanelList;

        /// <summary>
        /// Registers a user-added panel so the layout editor and layout persistence recognise it.
        /// </summary>
        /// <param name="descriptor">Descriptor describing the added panel.</param>
        public static void RegisterCustomPanel(OceanyaPanelDescriptor descriptor)
        {
            CustomPanels[descriptor.Id] = descriptor;
        }

        /// <summary>
        /// Removes a user-added panel from the registry.
        /// </summary>
        /// <param name="id">Panel id to remove.</param>
        public static void UnregisterCustomPanel(string id)
        {
            CustomPanels.Remove(id);
        }

        /// <summary>
        /// Gets a value indicating whether a panel id belongs to a user-added panel.
        /// </summary>
        /// <param name="id">Panel id to test.</param>
        /// <returns>True when the panel was added by the user.</returns>
        public static bool IsCustomPanel(string id)
        {
            return CustomPanels.ContainsKey(id);
        }

        /// <summary>
        /// Gets a panel descriptor by its stable id.
        /// </summary>
        /// <param name="id">Panel id.</param>
        /// <returns>The descriptor.</returns>
        /// <exception cref="KeyNotFoundException">The id is not registered.</exception>
        public static OceanyaPanelDescriptor Get(string id)
        {
            return TryGet(id) ?? throw new KeyNotFoundException($"Unknown Oceanya panel id '{id}'.");
        }

        /// <summary>
        /// Finds a panel descriptor by its stable id.
        /// </summary>
        /// <param name="id">Panel id.</param>
        /// <returns>The descriptor, or null when the id is not registered.</returns>
        public static OceanyaPanelDescriptor? TryGet(string id)
        {
            if (CustomPanels.TryGetValue(id, out OceanyaPanelDescriptor? custom))
            {
                return custom;
            }

            return PanelList.FirstOrDefault(panel => string.Equals(panel.Id, id, StringComparison.Ordinal));
        }
    }
}
