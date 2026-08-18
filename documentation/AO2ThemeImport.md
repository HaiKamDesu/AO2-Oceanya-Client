# Importing AO2 themes into Oceanya

## Question
Can an existing AO2 theme be reused as an Oceanya main-window layout, so users do not have to build
an Oceanya theme from scratch?

**Verdict: yes, for the layout, fonts and button art.** AO2's theme format is an absolute
`x, y, width, height` placement list, which is the same model `OceanyaPanelCatalog` /
`OceanyaThemeLayoutState` already use. About 25 AO2 widgets map onto Oceanya panels directly, another
handful are already consumed by Oceanya's viewport/chatbox theming, and the rest is either absent from
Oceanya or Oceanya-only and needs a placement policy.

## How AO2 themes work
- `AO2-Client/src/courtroom.cpp` positions every widget with
  `set_size_and_pos(widget, "identifier")`, which reads `identifier = x, y, w, h` from
  `courtroom_design.ini` (`AOApplication::get_element_dimensions`,
  `src/text_file_functions.cpp:218`). Missing entry ⇒ **AO2 hides the widget**, which is a useful
  precedent for Oceanya-only panels.
- Every value is multiplied by `Options::themeScalingFactor()`, so an importer must apply the same
  factor (Oceanya already reads `theme_scaling_factor` for the viewport).
- `courtroom = 0, 0, W, H` declares the whole window size. Measured over the 61 themes in this
  install: 55 define it, sizes range from `450x256` to `1918x982` (most common `714x668`, 11 themes).
  Oceanya's surface auto-grows, so any of these is representable.
- `courtroom_fonts.ini` carries `<widget> = size`, `<widget>_font`, `<widget>_color`,
  `<widget>_bold` per widget.
- `chat_config.ini`, `courtroom_sounds.ini` and the chatbox/viewport art are **already** read by
  Oceanya (`AO2ChatPreviewResolver`, `AO2ViewportAssetResolver`), so a theme's chatbox, colours and
  in-viewport look already apply today. Importing only has to cover the main-window widgets.
- Widget art comes from files named after the widget (`hold_it.png` + `hold_it_selected.png`,
  `realization.png` + `realization_pressed.png`, `emote_left.png`, ...), which is what Oceanya's
  per-panel `ImagePath` override consumes.
- Coverage is consistent: of 61 themes, 55 define `viewport`, `ic_chatlog`, `server_chatlog`,
  `emotes`, `text_color`, `hold_it`, `settings`, `pair_button`, `music_list` and `realization`.
- Caveats in the format: a few identifiers are **relative to a parent** (`showname` and `message` are
  relative to `chatbox`; `music_name` is relative to `music_display`) and keys are not consistently
  cased (`Additive` in GrayGarden), so parsing must be case-insensitive.

## Mapping
### Direct (import as-is)
| AO2 identifier | Oceanya panel |
|---|---|
| `viewport` | `viewport` (needs "render viewport in main window") |
| `ic_chatlog` | `ic_log` |
| `server_chatlog` / `ms_chatlog` | `ooc_log` |
| `ic_chat_name` / `ao2_ic_chat_name` | `ic_showname` |
| `ic_chat_message` / `ao2_ic_chat_message` | `ic_message` |
| `emotes` (+ `emote_button_spacing`) | `ic_emote_grid` (spacing ⇒ item size) |
| `iniswap_dropdown` | `ic_combo_character` |
| `emote_dropdown` | `ic_combo_emote` |
| `pos_dropdown` | `ic_combo_position` |
| `text_color` | `ic_combo_textcolor` |
| `effects_dropdown` | `ic_combo_effect` |
| `sfx_dropdown` | `ic_combo_sfx` |
| `hold_it`, `objection`, `take_that`, `custom_objection` | `shout_holdit`, `shout_objection`, `shout_takethat`, `shout_custom` |
| `pre`, `flip`, `additive`, `immediate` | `ic_check_preanim`, `ic_check_flip`, `ic_check_additive`, `ic_check_immediate` |
| `realization`, `screenshake` | `ic_button_realization`, `ic_button_screenshake` |
| `pair_button` | `ic_button_pairing` |
| `settings` | `bar_button_settings` |
| `music_list` | `bar_button_music` (button only - see below) |
| `area_list` | `bar_button_area` (button only - see below) |

### Approximate
- `pair_offset_spinbox` / `pair_vert_offset_spinbox` / `pair_order_dropdown` → Oceanya folds all of
  these into the pairing studio and the `ic_button_offset` button. Import the *position* of the first
  spinbox for the offset button and ignore the rest.
- `music_list` / `area_list` are inline lists in AO2 but popups in Oceanya. Only the opening button
  can be placed from the theme; the popup surfaces would need their own panels to honour the list
  rectangle (worth doing later, since those rectangles are usually large and load-bearing in a theme).
- `ooc_chat_message` / `ooc_chat_name` are separate widgets in AO2 but live **inside** Oceanya's
  `OOCLog` control, so they cannot be placed independently until that control is split the way
  `ICMessageSettings` was.

### In AO2, absent from Oceanya (ignore on import)
`evidence_*`, `witness_testimony`, `cross_examination`, `guilty`, `not_guilty`, `defense_bar`,
`prosecution_bar`, `defense_plus/minus`, `prosecution_plus/minus`, `mute_button`, `mute_list`,
`call_mod`, `change_character`, `reload_theme`, `back_to_lobby`, `spectator`, `char_select*`,
`char_list`, `char_buttons`, `player_list`, `switch_area_music`, `area_password`, `casing*`,
`showname_enable`, `music_slider`, `sfx_slider`, `blip_slider`, `music_label`, `sfx_label`,
`blip_label`, `clock_*`, `guard`. Several of these are tracked in `Documentation/AO2ParityGaps.md`
and `Documentation/ViewportParityGaps.md`; when they arrive they simply gain a mapping entry.

### Oceanya-only (no AO2 entry - needs a policy)
- **Functional, must stay reachable:** `clients_title`, `clients_add`, `clients_remove`,
  `clients_list` (GM multi-client has no AO2 equivalent at all), `bar_button_editlayout`,
  `bar_button_refresh`, `bar_button_debug`, `bar_check_*`.
- **Cosmetic, safe to hide:** `ic_settings_backdrop`, `ic_loremaster`, `ic_catchphrase`,
  `shout_backdrop`, `ooc_divider`, `ding_button`, `bottom_bar`, `dredd_row` (already feature-gated).

## Implementation (shipped)
`OceanyaClient/Features/Theme/Ao2ThemeLayoutImporter.cs`:
- `ParseDesignFile` reads `courtroom_design.ini` (skips `/*...*/`, `;` and `#` comments, keys are
  case-insensitive because themes are inconsistent - GrayGarden writes `Additive`).
- `Translate(entries, scalingFactor)` produces an `OceanyaThemeLayoutState`, applying the AO2 theme
  scaling factor and returning what was imported, hidden and left unmapped.
- `ImportTheme(themeName, scalingFactor)` resolves the design file across `AO2ThemeCatalog`'s theme
  folders and translates it. **Theme roots are `<base folder>/themes/<name>`** -
  `AO2ThemeCatalog.GetAo2ThemeScanFolders()` yields the *base* folders, so the `themes` segment has to
  be added (matching `AO2ThemeCatalog.ResolveThemeRoot`); without it no theme is ever found.

Policy decisions baked in:
- **Unmentioned panels are hidden**, mirroring AO2's own behaviour, and written as real hidden entries
  (a missing entry would otherwise leave the panel at its Oceanya default, visible and misplaced).
- **The clients strip stays.** GM multi-client has no AO2 equivalent, so every imported rectangle is
  shifted right by `ClientsStripWidth` (54) and the clients title/add/remove/list plus the remaining
  Oceanya-only buttons are packed into that left strip, with the client list taking the free height.
  The surface is widened by the same amount. Users can move it afterwards like any other panel.
- **Cosmetic Oceanya-only panels are hidden** (`ic_settings*`, `ic_loremaster`, `ic_catchphrase`,
  `shout_backdrop`, `ooc_divider`, `ding_button`, `bottom_bar`) so the theme's own look comes through.
- Importing turns on **render viewport in main window**, since every AO2 theme places `viewport`.
- Identifiers that are not placeable widgets, or that Oceanya already honours through its
  viewport/chatbox theming (`courtroom`, `chatbox`, `chat_arrow`, `showname`, `message`,
  `music_display`, `music_name`, spacings), are not reported as unmapped.

UI: Settings → **Interface** → *Panel Layout*: pick any AO2 theme and **Import layout from AO2 theme**
(confirms first), or **Reset layout to Oceanya default**. `SettingsWindow.PanelLayoutChanged` tells
`MainWindow` to re-place everything.

**Reset is a full theme reset**, owned by `MainWindow.ResetThemeLayoutToDefaults` (the editor's *Reset
all* delegates to it through `OceanyaPanelEditModeController.FullResetHandler`, and so does the
settings button). It clears placement, stacking, visibility and styling (restoring the live controls
from `OceanyaPanelStyleApplier` baselines), deletes user-added panels, and turns off
`GMViewportRenderInPanel`. The AO2 *viewport* theme is deliberately untouched - that is a separate
setting, not part of the Oceanya theme.

### Measured against the 61 installed themes
Every theme with a design file imports 20-30 panels. GrayGarden (the most heavily customised example)
imports 30: viewport, both logs, OOC message/showname/console, IC showname/message, emote grid, all six
dropdowns, all four shouts, all four checkboxes, realization, screenshake, pairing, offset, and the
settings/music/area buttons. Its unmapped remainder is entirely evidence, judge/HP bars, char select,
mute, sliders and other features Oceanya does not have.

## Original proposed design
1. `Ao2ThemeLayoutImporter` (new, under `OceanyaClient/Features/Theme/`): parse
   `courtroom_design.ini` for a chosen theme through the existing theme-chain resolver
   (`AO2ThemeCatalog` + the same subtheme/default fallback AO2 uses), apply
   `theme_scaling_factor`, and produce an `OceanyaThemeLayoutState`.
2. Apply `courtroom_fonts.ini` to the mapped panels' `FontSize` / `FontFamily` / `IsBold`. Colour
   would need a new `Foreground` field on `OceanyaPanelPlacementState`.
3. Apply theme button art to `ImagePath` for the mapped image buttons; shout buttons also need a
   "checked image" field for `*_selected.png`.
4. Oceanya-only panels: hide the cosmetic ones and pack the functional ones into a strip in the
   largest empty region of the imported layout (computed from the union of imported rectangles).
5. Turn on "render viewport in main window" automatically, since every AO2 theme places `viewport`
   inside the window.
6. UI: a theme picker on the Settings **Interface** page listing `AO2ThemeCatalog.GetThemes()`, with
   an explicit *Import layout from AO2 theme* action (destructive to the current layout, so it
   confirms first and is undoable via *Reset all*).

## Risks
- Themes are built for AO2's widget set; a large theme leaves big holes where Oceanya has no
  equivalent (evidence, judge, HP bars). The result is faithful but sparse.
- Sizes up to `1918x982` mean the imported surface can exceed the user's monitor. The UI-scale
  fit clamp already handles that by scaling down.
- Any theme relying on `courtroom_stylesheets.css` will not translate: Oceanya has no Qt stylesheet
  engine, so colours/borders coming from CSS are lost.
