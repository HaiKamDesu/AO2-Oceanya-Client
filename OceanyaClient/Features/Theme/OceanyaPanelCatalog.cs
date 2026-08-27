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
        TextToggle = 5,

        /// <summary>
        /// A volume slider: carries groove and handle art plus filled/empty track colours.
        /// </summary>
        Slider = 6
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
            OceanyaPanelKind kind = OceanyaPanelKind.Static,
            bool maintainsAspectRatio = false)
        {
            Id = id;
            DisplayName = displayName;
            Placement = placement;
            MinimumWidth = minimumWidth;
            MinimumHeight = minimumHeight;
            BackdropPlacement = backdropPlacement;
            Kind = kind;
            MaintainsAspectRatio = maintainsAspectRatio;
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
        /// Gets a value indicating whether resizing keeps the panel's aspect ratio. Set for content that
        /// renders uniformly anyway (the viewport), where a free resize would only add dead space.
        /// </summary>
        public bool MaintainsAspectRatio { get; }

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

        /// <summary>Panel id for the emote grid's previous-page arrow.</summary>
        public const string IcEmotePreviousPanelId = "ic_emote_prev";

        /// <summary>Panel id for the emote grid's next-page arrow.</summary>
        public const string IcEmoteNextPanelId = "ic_emote_next";

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
        /// <summary>Panel id for the ooc chat text.</summary>
        public const string OocChatPanelId = "ooc_chat";

        /// <summary>Panel id for the ooc header backdrop.</summary>
        public const string OocStreamBackdropPanelId = "ooc_stream_backdrop";

        /// <summary>Panel id for the ooc header text.</summary>
        public const string OocStreamTextPanelId = "ooc_stream_text";
        /// <summary>Panel id for the ooc message box.</summary>
        public const string OocMessagePanelId = "ooc_message";

        /// <summary>Panel id for the ooc showname.</summary>
        public const string OocShownamePanelId = "ooc_showname";

        /// <summary>Panel id for the server console button.</summary>
        public const string OocServerConsolePanelId = "ooc_server_console";
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

        /// <summary>Panel id for the area list rendered inside the main window.</summary>
        public const string AreaListPanelId = "area_list";

        /// <summary>Panel id for the music list rendered inside the main window.</summary>
        public const string MusicListPanelId = "music_list";

        /// <summary>
        /// Panel id for the button that swaps between the area and music lists.
        /// </summary>
        /// <remarks>
        /// AO2 themes usually stack both lists in one place and switch between them with an "A/M" button,
        /// so Oceanya has the same button. It is hidden by default - the stock layout uses two popups and
        /// has nothing to switch - and a layout that wants it simply un-hides it.
        /// </remarks>
        public const string BarButtonAreaMusicSwitchPanelId = "bar_button_areamusic";

        /// <summary>Panel id for the mute button.</summary>
        public const string BarButtonMutePanelId = "bar_button_mute";

        /// <summary>Panel id for the evidence button.</summary>
        public const string BarButtonEvidencePanelId = "bar_button_evidence";

        /// <summary>Panel id for the reload-theme button.</summary>
        public const string BarButtonReloadThemePanelId = "bar_button_reloadtheme";

        /// <summary>Panel id for the change-character button.</summary>
        public const string BarButtonChangeCharacterPanelId = "bar_button_changecharacter";

        /// <summary>Panel id for the call-moderator button.</summary>
        public const string BarButtonCallModPanelId = "bar_button_callmod";

        /// <summary>Panel id for the music volume slider.</summary>
        public const string SliderMusicVolumePanelId = "slider_music_volume";

        /// <summary>Panel id for the sound effect volume slider.</summary>
        public const string SliderSfxVolumePanelId = "slider_sfx_volume";

        /// <summary>Panel id for the blip volume slider.</summary>
        public const string SliderBlipVolumePanelId = "slider_blip_volume";

        /// <summary>Panel id for the music volume slider's label art.</summary>
        public const string SliderMusicLabelPanelId = "slider_music_label";

        /// <summary>Panel id for the sound effect volume slider's label art.</summary>
        public const string SliderSfxLabelPanelId = "slider_sfx_label";

        /// <summary>Panel id for the blip volume slider's label art.</summary>
        public const string SliderBlipLabelPanelId = "slider_blip_label";

        /// <summary>Panel id for the shared area/music search box.</summary>
        public const string AreaMusicSearchPanelId = "area_music_search";

        /// <summary>Panel id for the position dropdown's reset button.</summary>
        public const string IcComboPositionResetPanelId = "ic_combo_position_reset";

        /// <summary>Panel id for the character dropdown's reset button.</summary>
        public const string IcComboCharacterResetPanelId = "ic_combo_character_reset";

        /// <summary>Panel id for the sound effect dropdown's reset button.</summary>
        public const string IcComboSfxResetPanelId = "ic_combo_sfx_reset";

        /// <summary>Panel id for the "send showname" checkbox.</summary>
        public const string IcCheckShownamePanelId = "ic_check_showname";

        /// <summary>Panel id for the defence health bar.</summary>
        public const string JudgeDefenceBarPanelId = "judge_defence_bar";

        /// <summary>Panel id for the prosecution health bar.</summary>
        public const string JudgeProsecutionBarPanelId = "judge_prosecution_bar";

        /// <summary>Panel id for the defence health minus button.</summary>
        public const string JudgeDefenceMinusPanelId = "judge_defence_minus";

        /// <summary>Panel id for the defence health plus button.</summary>
        public const string JudgeDefencePlusPanelId = "judge_defence_plus";

        /// <summary>Panel id for the prosecution health minus button.</summary>
        public const string JudgeProsecutionMinusPanelId = "judge_prosecution_minus";

        /// <summary>Panel id for the prosecution health plus button.</summary>
        public const string JudgeProsecutionPlusPanelId = "judge_prosecution_plus";

        /// <summary>Panel id for the Witness Testimony button.</summary>
        public const string JudgeWitnessTestimonyPanelId = "judge_witness_testimony";

        /// <summary>Panel id for the Cross Examination button.</summary>
        public const string JudgeCrossExaminationPanelId = "judge_cross_examination";

        /// <summary>Panel id for the Not Guilty verdict button.</summary>
        public const string JudgeNotGuiltyPanelId = "judge_not_guilty";

        /// <summary>Panel id for the Guilty verdict button.</summary>
        public const string JudgeGuiltyPanelId = "judge_guilty";

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
                "Oceanya Logo",
                new OceanyaPanelPlacement(33, 353, 452, 115),
                minimumWidth: 20,
                minimumHeight: 20),
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
                IcEmotePreviousPanelId,
                "Emote Previous Page",
                new OceanyaPanelPlacement(9, 365, 30, 110),
                minimumWidth: 8,
                minimumHeight: 8,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                IcEmoteNextPanelId,
                "Emote Next Page",
                new OceanyaPanelPlacement(474, 365, 30, 110),
                minimumWidth: 8,
                minimumHeight: 8,
                kind: OceanyaPanelKind.ImageButton),
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
                OocChatPanelId,
                "OOC Chat Text",
                new OceanyaPanelPlacement(287, 24, 222, 238),
                minimumWidth: 60,
                minimumHeight: 30,
                kind: OceanyaPanelKind.Static),
            new OceanyaPanelDescriptor(
                OocStreamBackdropPanelId,
                "OOC Header Backdrop",
                new OceanyaPanelPlacement(287, 0, 222, 24),
                minimumWidth: 20,
                minimumHeight: 6,
                kind: OceanyaPanelKind.Static),
            new OceanyaPanelDescriptor(
                OocStreamTextPanelId,
                "OOC Header Text",
                new OceanyaPanelPlacement(287, 0, 222, 24),
                minimumWidth: 20,
                minimumHeight: 10,
                kind: OceanyaPanelKind.TextToggle),
            new OceanyaPanelDescriptor(
                OocMessagePanelId,
                "OOC Message Box",
                new OceanyaPanelPlacement(287, 262, 222, 18),
                minimumWidth: 60,
                minimumHeight: 12,
                kind: OceanyaPanelKind.TextInput),
            new OceanyaPanelDescriptor(
                OocShownamePanelId,
                "OOC Showname",
                new OceanyaPanelPlacement(287, 281, 93, 17),
                minimumWidth: 40,
                minimumHeight: 12,
                kind: OceanyaPanelKind.TextInput),
            new OceanyaPanelDescriptor(
                OocServerConsolePanelId,
                "Server Console Button",
                new OceanyaPanelPlacement(380, 281, 129, 17),
                minimumWidth: 40,
                minimumHeight: 12,
                kind: OceanyaPanelKind.TextToggle),
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
                new OceanyaPanelPlacement(404, 603, 256, 192),
                minimumWidth: 96,
                minimumHeight: 72,
                maintainsAspectRatio: true),
            new OceanyaPanelDescriptor(
                AreaListPanelId,
                "Area List",
                new OceanyaPanelPlacement(431, 603, 282, 296),
                minimumWidth: 120,
                minimumHeight: 100),
            new OceanyaPanelDescriptor(
                MusicListPanelId,
                "Music List",
                new OceanyaPanelPlacement(458, 603, 320, 420),
                minimumWidth: 140,
                minimumHeight: 120),
            new OceanyaPanelDescriptor(
                BarButtonAreaMusicSwitchPanelId,
                "Area/Music Switch Button",
                new OceanyaPanelPlacement(431, 603, 56, 24),
                minimumWidth: 16,
                minimumHeight: 12,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                BarButtonMutePanelId,
                "Mute Button",
                new OceanyaPanelPlacement(431, 603, 52, 24),
                minimumWidth: 16,
                minimumHeight: 12,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                BarButtonEvidencePanelId,
                "Evidence Button",
                new OceanyaPanelPlacement(431, 603, 71, 24),
                minimumWidth: 16,
                minimumHeight: 12,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                BarButtonReloadThemePanelId,
                "Reload Theme Button",
                new OceanyaPanelPlacement(431, 603, 94, 20),
                minimumWidth: 16,
                minimumHeight: 12,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                BarButtonChangeCharacterPanelId,
                "Change Character Button",
                new OceanyaPanelPlacement(431, 603, 117, 20),
                minimumWidth: 16,
                minimumHeight: 12,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                BarButtonCallModPanelId,
                "Call Mod Button",
                new OceanyaPanelPlacement(431, 603, 65, 20),
                minimumWidth: 16,
                minimumHeight: 12,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                SliderMusicVolumePanelId,
                "Music Volume Slider",
                new OceanyaPanelPlacement(431, 603, 168, 30),
                minimumWidth: 40,
                minimumHeight: 16,
                kind: OceanyaPanelKind.Slider),
            new OceanyaPanelDescriptor(
                SliderSfxVolumePanelId,
                "SFX Volume Slider",
                new OceanyaPanelPlacement(431, 603, 168, 30),
                minimumWidth: 40,
                minimumHeight: 16,
                kind: OceanyaPanelKind.Slider),
            new OceanyaPanelDescriptor(
                SliderBlipVolumePanelId,
                "Blip Volume Slider",
                new OceanyaPanelPlacement(431, 603, 168, 30),
                minimumWidth: 40,
                minimumHeight: 16,
                kind: OceanyaPanelKind.Slider),
            new OceanyaPanelDescriptor(
                SliderMusicLabelPanelId,
                "Music Slider Label",
                new OceanyaPanelPlacement(431, 603, 10, 12),
                minimumWidth: 8,
                minimumHeight: 8,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                SliderSfxLabelPanelId,
                "SFX Slider Label",
                new OceanyaPanelPlacement(431, 603, 10, 12),
                minimumWidth: 8,
                minimumHeight: 8,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                SliderBlipLabelPanelId,
                "Blip Slider Label",
                new OceanyaPanelPlacement(431, 603, 370, 12),
                minimumWidth: 8,
                minimumHeight: 8,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                AreaMusicSearchPanelId,
                "Area/Music Search",
                new OceanyaPanelPlacement(431, 603, 128, 23),
                minimumWidth: 40,
                minimumHeight: 14,
                kind: OceanyaPanelKind.TextInput),
            new OceanyaPanelDescriptor(
                IcComboPositionResetPanelId,
                "Position Reset Button",
                new OceanyaPanelPlacement(431, 603, 20, 20),
                minimumWidth: 10,
                minimumHeight: 10,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                IcComboCharacterResetPanelId,
                "Character Reset Button",
                new OceanyaPanelPlacement(431, 603, 20, 20),
                minimumWidth: 10,
                minimumHeight: 10,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                IcComboSfxResetPanelId,
                "Sound Reset Button",
                new OceanyaPanelPlacement(431, 603, 20, 20),
                minimumWidth: 10,
                minimumHeight: 10,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                IcCheckShownamePanelId,
                "Send Showname Checkbox",
                new OceanyaPanelPlacement(431, 603, 83, 15),
                minimumWidth: 40,
                minimumHeight: 12,
                kind: OceanyaPanelKind.TextToggle),
            new OceanyaPanelDescriptor(
                JudgeDefenceBarPanelId,
                "Defence Health Bar",
                new OceanyaPanelPlacement(431, 603, 141, 15),
                minimumWidth: 20,
                minimumHeight: 6,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                JudgeProsecutionBarPanelId,
                "Prosecution Health Bar",
                new OceanyaPanelPlacement(431, 603, 141, 15),
                minimumWidth: 20,
                minimumHeight: 6,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                JudgeDefenceMinusPanelId,
                "Defence Health Minus",
                new OceanyaPanelPlacement(431, 603, 15, 15),
                minimumWidth: 10,
                minimumHeight: 10,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                JudgeDefencePlusPanelId,
                "Defence Health Plus",
                new OceanyaPanelPlacement(431, 603, 15, 15),
                minimumWidth: 10,
                minimumHeight: 10,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                JudgeProsecutionMinusPanelId,
                "Prosecution Health Minus",
                new OceanyaPanelPlacement(431, 603, 15, 15),
                minimumWidth: 10,
                minimumHeight: 10,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                JudgeProsecutionPlusPanelId,
                "Prosecution Health Plus",
                new OceanyaPanelPlacement(431, 603, 15, 15),
                minimumWidth: 10,
                minimumHeight: 10,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                JudgeWitnessTestimonyPanelId,
                "Witness Testimony Button",
                new OceanyaPanelPlacement(431, 603, 42, 25),
                minimumWidth: 16,
                minimumHeight: 12,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                JudgeCrossExaminationPanelId,
                "Cross Examination Button",
                new OceanyaPanelPlacement(431, 603, 42, 25),
                minimumWidth: 16,
                minimumHeight: 12,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                JudgeNotGuiltyPanelId,
                "Not Guilty Button",
                new OceanyaPanelPlacement(431, 603, 42, 25),
                minimumWidth: 16,
                minimumHeight: 12,
                kind: OceanyaPanelKind.ImageButton),
            new OceanyaPanelDescriptor(
                JudgeGuiltyPanelId,
                "Guilty Button",
                new OceanyaPanelPlacement(431, 603, 42, 25),
                minimumWidth: 16,
                minimumHeight: 12,
                kind: OceanyaPanelKind.ImageButton),
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

        /// <summary>
        /// Draw order that reproduces the pre-theme (7.12) stacking exactly. Reparenting the regions
        /// onto one canvas replaced the original XAML child order with dictionary order, which pushed
        /// backdrops in front of the controls they sit behind.
        /// </summary>
        private static readonly Dictionary<string, int> DefaultZOrders = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["viewport"] = 5,

            // Like the viewport, the in-window lists sit behind the widgets a theme draws over them.
            ["area_list"] = 6,
            ["music_list"] = 6,
            ["ooc_log"] = 10,
            ["ooc_chat"] = 12,
            ["ooc_stream_backdrop"] = 14,
            ["ooc_stream_text"] = 16,
            ["ooc_message"] = 20,
            ["ooc_showname"] = 30,
            ["ooc_server_console"] = 40,
            ["ooc_divider"] = 50,
            ["ic_log"] = 60,
            ["shout_backdrop"] = 70,
            ["shout_holdit"] = 80,
            ["shout_objection"] = 90,
            ["shout_takethat"] = 100,
            ["shout_custom"] = 110,
            ["clients_add"] = 120,
            ["clients_remove"] = 130,
            ["clients_list"] = 140,
            ["clients_title"] = 150,
            ["ic_settings"] = 160,
            ["ic_catchphrase"] = 170,
            ["ic_settings_backdrop"] = 180,
            ["ic_loremaster"] = 190,
            ["ic_showname"] = 200,
            ["ic_message"] = 210,
            ["ic_emote_grid"] = 220,
            ["ic_emote_prev"] = 221,
            ["ic_emote_next"] = 221,
            ["ic_check_preanim"] = 230,
            ["ic_check_flip"] = 240,
            ["ic_check_additive"] = 250,
            ["ic_check_immediate"] = 260,
            ["ic_combo_character"] = 270,
            ["ic_combo_emote"] = 280,
            ["ic_combo_position"] = 290,
            ["ic_combo_textcolor"] = 300,
            ["ic_combo_effect"] = 310,
            ["ic_combo_sfx"] = 320,
            ["ic_button_realization"] = 330,
            ["ic_button_screenshake"] = 340,
            ["ic_button_offset"] = 350,
            ["ic_button_pairing"] = 360,
            ["dredd_row"] = 370,
            ["bottom_bar"] = 380,
            ["bar_check_sticky"] = 390,
            ["bar_button_refresh"] = 400,
            ["bar_button_settings"] = 410,
            ["bar_check_switchpos"] = 420,
            ["bar_button_debug"] = 430,
            ["bar_check_invertlog"] = 440,
            ["bar_button_area"] = 450,
            ["bar_button_music"] = 460,
            ["bar_button_areamusic"] = 461,
            ["bar_button_mute"] = 462,
            ["bar_button_evidence"] = 463,
            ["bar_button_reloadtheme"] = 464,
            ["bar_button_changecharacter"] = 465,
            ["bar_button_callmod"] = 466,
            ["slider_music_volume"] = 467,
            ["slider_sfx_volume"] = 468,
            ["slider_blip_volume"] = 469,
            ["slider_music_label"] = 470,
            ["slider_sfx_label"] = 471,
            ["slider_blip_label"] = 472,
            ["ic_check_showname"] = 473,
            ["area_music_search"] = 487,
            ["ic_combo_position_reset"] = 484,
            ["ic_combo_character_reset"] = 485,
            ["ic_combo_sfx_reset"] = 486,
            ["judge_defence_bar"] = 474,
            ["judge_prosecution_bar"] = 475,
            ["judge_defence_minus"] = 476,
            ["judge_defence_plus"] = 477,
            ["judge_prosecution_minus"] = 478,
            ["judge_prosecution_plus"] = 479,
            ["judge_witness_testimony"] = 480,
            ["judge_cross_examination"] = 481,
            ["judge_not_guilty"] = 482,
            ["judge_guilty"] = 483,
            ["bar_button_viewport"] = 470,
            ["bar_button_editlayout"] = 480,
            ["ding_button"] = 500
        };

        /// <summary>
        /// Gets the default stacking order for a panel, reproducing the historic draw order.
        /// </summary>
        /// <param name="panelId">Panel id.</param>
        /// <returns>The z-index to use when the layout does not override it.</returns>
        public static int GetDefaultZOrder(string panelId)
        {
            return DefaultZOrders.TryGetValue(panelId, out int zOrder) ? zOrder : 0;
        }

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
        /// Panels that exist but start hidden, because the stock layout has no use for them.
        /// </summary>
        /// <remarks>
        /// Oceanya deliberately ships more controls than it shows: an AO2 theme can call for a widget the
        /// default layout does not need (the A/M list switch, which only means something once both lists
        /// render in the window). Hiding them by default keeps the stock window unchanged while letting a
        /// theme - or a user editing the layout - simply un-hide one.
        /// </remarks>
        private static readonly HashSet<string> HiddenByDefaultPanelIds =
            new HashSet<string>(StringComparer.Ordinal)
            {
                BarButtonAreaMusicSwitchPanelId,
                BarButtonMutePanelId,
                BarButtonEvidencePanelId,
                BarButtonReloadThemePanelId,
                BarButtonChangeCharacterPanelId,
                BarButtonCallModPanelId,
                SliderMusicVolumePanelId,
                SliderSfxVolumePanelId,
                SliderBlipVolumePanelId,
                SliderMusicLabelPanelId,
                SliderSfxLabelPanelId,
                SliderBlipLabelPanelId,
                IcCheckShownamePanelId,
                AreaMusicSearchPanelId,
                IcComboPositionResetPanelId,
                IcComboCharacterResetPanelId,
                IcComboSfxResetPanelId,
                JudgeDefenceBarPanelId,
                JudgeProsecutionBarPanelId,
                JudgeDefenceMinusPanelId,
                JudgeDefencePlusPanelId,
                JudgeProsecutionMinusPanelId,
                JudgeProsecutionPlusPanelId,
                JudgeWitnessTestimonyPanelId,
                JudgeCrossExaminationPanelId,
                JudgeNotGuiltyPanelId,
                JudgeGuiltyPanelId
            };

        /// <summary>
        /// Panels whose visibility is decided by something other than the layout.
        /// </summary>
        /// <remarks>
        /// A layout may still HIDE one of these - a theme that does not want the viewport is entitled to say
        /// so - but it must never SHOW one, because whether it belongs on screen at all depends on state the
        /// theme knows nothing about: a debug build, test mode, the Dredd feature being on, a list rendering
        /// in the window rather than a popup, standing at a judge position, a dropdown being off its
        /// default. Each of those has its own pass that runs after the layout is applied.
        /// </remarks>
        private static readonly HashSet<string> RuntimeVisibilityPanelIds =
            new HashSet<string>(StringComparer.Ordinal)
            {
                DreddFeatureRowPanelId,
                BarButtonDebugPanelId,
                BarCheckStickyPanelId,
                BarCheckSwitchPosPanelId,
                BarCheckInvertLogPanelId,
                ViewportPanelId,
                AreaListPanelId,
                MusicListPanelId,
                BarButtonViewportPanelId,
                BarButtonAreaPanelId,
                BarButtonMusicPanelId,
                IcComboPositionResetPanelId,
                IcComboCharacterResetPanelId,
                IcComboSfxResetPanelId,
                JudgeWitnessTestimonyPanelId,
                JudgeCrossExaminationPanelId,
                JudgeNotGuiltyPanelId,
                JudgeGuiltyPanelId,
                JudgeDefenceMinusPanelId,
                JudgeDefencePlusPanelId,
                JudgeProsecutionMinusPanelId,
                JudgeProsecutionPlusPanelId
            };

        /// <summary>
        /// Gets a value indicating whether something other than the layout decides a panel's visibility.
        /// </summary>
        /// <param name="id">Panel id.</param>
        /// <returns>True when the layout may hide the panel but never show it.</returns>
        public static bool HasRuntimeControlledVisibility(string id)
        {
            return RuntimeVisibilityPanelIds.Contains(id);
        }

        /// <summary>
        /// Gets a value indicating whether a panel starts hidden when no layout says otherwise.
        /// </summary>
        /// <param name="id">Panel id.</param>
        /// <returns>True when the panel is hidden by default.</returns>
        public static bool IsHiddenByDefault(string id)
        {
            return HiddenByDefaultPanelIds.Contains(id);
        }

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
