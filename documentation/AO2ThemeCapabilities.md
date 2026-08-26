# What an AO2 theme can actually control

Reference map of the AO2 theme system, so Oceanya's own theme system can mirror or translate it. Facts
come from the `AO2-Client` submodule and from the 61 themes in the reference install
(`<AO install>/base/themes`). Companion documents: `AO2ThemeImport.md` (what we import and how),
`OceanyaThemeSystem.md` (our own system).

## Where a theme lives and how lookups resolve
A theme is a folder under `base/themes/<name>`. A **subtheme** is a folder inside it,
`base/themes/<name>/<subtheme>`.

`AOApplication::get_asset_paths` (`src/path_functions.cpp:175`) builds this candidate list, in order,
for every asset and every config file:

1. `characters/<char>/<element>` - when a character is involved
2. `themes/<theme>/<subtheme>/misc/<misc>/<element>` - subtheme misc
3. `themes/<theme>/misc/<misc>/<element>` - theme misc
4. `themes/<theme>/<subtheme>/<element>` - subtheme
5. `misc/<misc>/<element>` - base misc (shared "chat styles")
6. `themes/<theme>/<element>` - theme
7. `themes/<default theme>/<element>` - default theme
8. `<element>` - the bare path
9. placeholder in theme, then in default theme

Consequences worth copying: **a theme only has to override what it wants**, everything else falls
through to the default theme; `misc/<name>` packs (selected per character via `[Options] chat`) can
override theme art; and the same chain resolves *config files*, so a subtheme can override a single INI
key while inheriting the rest (`get_config_value`, `src/path_functions.cpp:265`).

Every dimension read through `get_element_dimensions` / `get_point` is multiplied by
`Options::themeScalingFactor()`, an integer set in AO2's own settings, not by the theme.

## `courtroom_design.ini` - geometry
`identifier = x, y, width, height` per widget, absolute, in window coordinates. A **missing entry hides
the widget** (`Courtroom::set_size_and_pos`, `src/courtroom.cpp:1333`). Some entries are points
(`x, y`), some are colours (`r, g, b`), some single numbers.

- **Window / containers**: `courtroom` (the window size), `viewport`, `chatbox`, `ao2_chatbox`,
  `music_display`, `evidence_background`, `evidence_overlay`, `char_select`
- **Logs and text**: `ic_chatlog`, `server_chatlog`, `ms_chatlog`, `ic_chat_name`, `ic_chat_message`
  (+ `ao2_` variants), `ooc_chat_name`, `ooc_chat_message`, `showname`, `message`,
  `showname_extra_width`, `area_password`
- **Emotes**: `emotes`, `emote_button_spacing`, `emote_left`, `emote_right`, `emote_dropdown`
- **Dropdowns / pickers**: `iniswap_dropdown` + `iniswap_remove`, `sfx_dropdown` + `sfx_remove`,
  `pos_dropdown` + `pos_remove`, `effects_dropdown` + `effects_icon_size`, `text_color`
- **Shouts**: `hold_it`, `objection`, `take_that`, `custom_objection`
- **Checkboxes**: `pre`, `flip`, `additive`, `immediate`, `pre_no_interrupt`, `showname_enable`,
  `slide_enable`, `casing`
- **Action buttons**: `realization`, `screenshake`, `settings`, `call_mod`, `change_character`,
  `reload_theme`, `back_to_lobby`, `spectator`, `switch_area_music`, `guard`, `casing_button`
- **Pairing**: `pair_button`, `pair_list`, `pair_offset_spinbox`, `pair_vert_offset_spinbox`,
  `pair_order_dropdown`
- **Mute / players**: `mute_button`, `mute_list`, `player_list`
- **Music / areas**: `music_list`, `music_search`, `music_name`, `area_list`
- **Volume**: `music_slider`, `sfx_slider`, `blip_slider`, and their `*_label`s
- **Judge**: `witness_testimony`, `cross_examination`, `guilty`, `not_guilty`, `defense_bar`,
  `prosecution_bar`, `defense_plus/minus`, `prosecution_plus/minus`
- **Evidence**: `evidence_button`, `evidence_buttons`, `evidence_button_spacing`, `evidence_name`,
  `evidence_left/right`, `evidence_present/switch/transfer/load/save`, `evidence_image_name`,
  `evidence_image_button`, `evidence_description`, `evidence_delete/x/ok`,
  `left_evidence_icon`, `right_evidence_icon`
- **Character select**: `char_select`, `char_buttons`, `char_button_spacing`, `char_search`,
  `char_taken`, `char_passworded`, `char_password`, `char_list`, `char_select_left/right`
- **Misc**: `chat_arrow`, `clock_<n>`, `ooc_toggle`
- **Colours declared here** (r, g, b): `found_song_color`, `missing_song_color`, `area_free_color`,
  `area_lfp_color`, `area_casing_color`, `area_recess_color`, `area_rp_color`, `area_gaming_color`,
  `area_locked_color`, `ooc_default_color`, `ooc_server_color`

**Traps.** `showname` and `message` are relative to `chatbox`; `music_name` is relative to
`music_display`. Key casing is inconsistent between themes (`Additive` vs `additive`), so parsing must be
case-insensitive. Comment syntax in real themes is `/* ... */`, `;` and `#`.

## `courtroom_fonts.ini` - typography
Per widget: `<widget> = <size>`, `<widget>_font`, `<widget>_color` (r, g, b), `<widget>_bold` (0/1), and
for logs `<widget>_sender_color`. Widgets covered by real themes: `showname`, `message`, `ic_chatlog`,
`ms_chatlog`, `server_chatlog`, `music_list`, `music_name`, `area_list`, `evidence_name`,
`evidence_description`, `evidence_image_name`.

## `chat_config.ini` - IC text markup
Per colour index `cN`: `cN` (r, g, b), `cN_name`, `cN_start`, `cN_end`, `cN_remove`, `cN_talking`.
Defines the text colours available in the colour dropdown, their inline markup characters, whether the
markup characters are stripped, and whether that colour animates the talking sprite. Resolved through the
misc chain, so a character's `[Options] chat` pack can replace it.

## `courtroom_sounds.ini` - UI sounds
Named sound effects for court events (`testimony1`, `testimony2`, `guilty`, `notguilty`, ...), falling
back to `sounds/general`. Oceanya already reads this (`AO2ViewportAudioResolver.ResolveCourtSfxPath`).

## `courtroom_stylesheets.css` - Qt widget styling
Qt stylesheet applied to the courtroom widgets: colours, borders, scrollbars, selection highlights. **This
cannot be translated** - Oceanya is WPF and has no Qt stylesheet engine. Themes that get most of their
look from CSS will import geometry but lose that styling, which is the single biggest fidelity gap.

## Images a theme can ship
Extensions are probed in order `.webp`, `.apng`, `.gif`, `.png` for animated slots, plus static variants
(`AOApplication::get_image_suffix`); any slot can therefore be animated.

- **Backdrops**: `courtroombackground.png`, `charselect_background.png`, `evidence_background`,
  `evidence_overlay` (+ `_private` variants)
- **Chatbox**: `chat.png` (and `chatmed`/`chatbig` width variants), `chat_arrow`
- **Shouts**: `holdit`, `objection`, `takethat`, `custom` (+ `_selected` / `_pressed` states)
- **Buttons**: one image per action button, with `_pressed` / `_selected` / hover variants; real themes
  also carry whole folders: `buttons/`, `hover/`, `checkboxes/`, `dropdowns/`, `sliders/`
- **Judge**: `defensebar0..10`, `prosecutionbar0..10`, `defplus`/`defminus`, `witnesstestimony`,
  `crossexamination`, `guilty`, `notguilty`
- **Evidence**: icons plus `evidence_*` button art
- **Effects**: an `effects/` folder with `effects.ini` describing each effect (looping, `cull`,
  `max_duration`) and one animation per effect
- **`misc/<pack>/`**: a full alternative art set (chatbox, arrow, shout art, `chat_config.ini`) selected
  per character

## Not part of a theme
Character art (`characters/<name>`), backgrounds (`background/<name>`), music, and the user's own
`config.ini` (theme name, subtheme, scaling factor, volumes, callwords) live outside the theme folder.
A theme cannot change gameplay behaviour - only geometry, typography, colours, art and sounds.

## Fidelity summary for translation
| Layer | Translatable to Oceanya | Notes |
|---|---|---|
| Widget geometry | Yes | Same absolute model as our panel placements |
| Window size | Yes | `courtroom` entry becomes the surface size |
| Fonts (family/size/bold/colour) | Yes | Our panels already carry these fields |
| Design colours (area/OOC/song) | Partly | We have equivalents for some; others have no consumer yet |
| Button/shout/backdrop art | Yes | Per-panel image override |
| Chatbox, chat markup, sounds, effects, misc packs | Already | Consumed today by the viewport/chatbox resolvers |
| Qt stylesheet (`.css`) | Partly | Translated field-by-field, not interpreted - see the gap list below |
| Widgets for features Oceanya lacks | N/A | Evidence, judge/HP, mute, char select, sliders |

## What we still cannot reproduce
The state of the import as of 2026-08-19, after the geometry, artwork, typography, stylesheet and
in-window list work. Everything here is a **known** gap: nothing on this list is silently wrong, and each
entry says what it would take. Ordered by how visible the difference is.

### Widgets for features Oceanya does not have
No amount of theming work fixes these - the feature has to exist first. The theme's rectangles and
artwork for them are read and ignored.

| AO2 widget group | Identifiers | Blocked on |
|---|---|---|
| Evidence | `evidence_*` except the button, `left/right_evidence_icon` | Evidence management (parity gap #2) - the `evidence_button` exists and reports that we do not support evidence |
| Mute list | `mute_list` | Mute (parity gap #8) - the `mute_button` exists and says so |
| Casing / guard | `casing`, `casing_button`, `guard` | Casing announcements |
| Character select screen | `char_select`, `char_buttons`, `char_search`, `char_list`, `char_passworded`, `char_password`, `char_select_left/right`, `back_to_lobby`, `spectator`, `char_taken` | Our character selector is its own window, not a themed courtroom screen |
| Pair list and order | `pair_list`, `pair_order_dropdown`, `pair_vert_offset_spinbox` | Pairing lives in the Pairing Studio dialog |
| Clocks | `clock_0`..`clock_4` | Timer (parity gap #4) |
| Area password | `area_password` | Not implemented |
| Music search / iniswap and sfx remove buttons | `music_search`, `iniswap_remove`, `sfx_remove`, `pos_remove` | Search is inside our music panel; the remove buttons have no equivalent |

### Optional widgets that now exist
These are real panels, **hidden by default** (`OceanyaPanelCatalog.IsHiddenByDefault`) and un-hidden by a
theme that places them or by a user editing the layout.

| AO2 identifier | Panel | Behaviour |
|---|---|---|
| `switch_area_music` | `bar_button_areamusic` | Swaps which of the two in-window lists is shown |
| `mute_button` | `bar_button_mute` | Reports that muting is not implemented |
| `evidence_button` | `bar_button_evidence` | Reports that Oceanya is not compatible with evidence |
| `reload_theme` | `bar_button_reloadtheme` | Re-reads the selected AO2 theme |
| `change_character` | `bar_button_changecharacter` | Same flow as the client menu's *Select INIPuppet (Manual)* |
| `call_mod` | `bar_button_callmod` | `ZZ#%`, or `ZZ#<reason>#-1#%` when the server advertises `modcall_reason` |
| `music_slider`, `sfx_slider`, `blip_slider` | `slider_*_volume` | Live preview while dragging, persisted to both config.ini and the savefile on release; groove and handle art come from the stylesheet's `QSlider::groove`/`::handle` |
| `music_label`, `sfx_label`, `blip_label` | `slider_*_label` | Image panels for the caption artwork themes draw beside the sliders |
| `showname_enable` | `ic_check_showname` | Unticked, IC messages carry no showname, so everyone sees the character's own name |
| `defense_bar`, `prosecution_bar` | `judge_*_bar` | Image panels painted from `defensebar0..10` / `prosecutionbar0..10` as `HP#` packets arrive |
| `defense_plus/minus`, `prosecution_plus/minus` | `judge_*_plus/minus` | Send `HP#<bar>#<value>#%`; the bar moves when the server echoes it |
| `witness_testimony`, `cross_examination`, `not_guilty`, `guilty` | `judge_*` | Send `RT#testimony1`, `RT#testimony2`, `RT#judgeruling#0`, `RT#judgeruling#1` |

### Widgets we have but deliberately do not take from the theme
| Widget | Why |
|---|---|
| `pair_offset_spinbox` | AO2 only shows it while the pairing panel is open, so themes place it in ways that make no sense for a control we show permanently. Parked in the left strip. |
| `showname`, `message` fonts | Those are the **viewport chatbox** fonts, already applied by `AO2ChatPreviewResolver`. Copying them onto the IC input line rendered it at chatbox size. |

### Stylesheet features with no field yet
`Ao2StylesheetTranslator` writes into the same per-panel fields the editor exposes, so anything without a
field is skipped rather than approximated. Adding one means adding the editor control first.

| Qt feature | Example | What it would need |
|---|---|---|
| Per-side borders | `border-left: hidden` | Four-sided `BorderThickness` instead of one number |
| Padding / margin | `padding-left: 5px` | A padding field per panel |
| Corner radius | `border-radius: 0px` | A corner-radius field |
| Selection colours | `selection-background-color` | Selection brushes on text panels |
| Sub-controls other than the tick box, dropdown arrow and scroll handle | `::drop-down`, `::add-page`, `::sub-line` | A field per part |
| `:disabled`, `:focus`, `:!hover` | - | A state slot per pseudo-class |
| Descendant / ID selectors | `QComboBox QAbstractItemView`, `#objectName` | A real selector engine; today only class, geometry and state selectors resolve |
| Qt property selectors other than geometry | `[class="..."]` | Only `x`/`y`/`width`/`height` are matched, because those are what our panels know about themselves |

### Widgets whose feature exists but is not exposed
| AO2 widget | Identifier | Note |
|---|---|---|
| Slide toggle, guard, casing | `slide_enable`, `guard`, `casing` | Same shape: no Oceanya behaviour behind them yet. |

### Structural differences that cannot be represented
| AO2 behaviour | Ours |
|---|---|
| A missing `courtroom_design.ini` entry **hides** the widget | Same, deliberately - but our extra panels have no AO2 name, so they are packed into a left strip that AO2 has no concept of, which shifts every imported rectangle right by 54px |
| `theme_scaling_factor` multiplies every dimension **and** the Qt widget properties its own stylesheet matches on | We scale dimensions; a theme whose stylesheet uses coordinate selectors *and* asks for a scaling factor cannot match both (AO2 has the same problem) |
| Subtheme inheritance per INI **key** | We resolve whole files through the chain, not individual keys, everywhere except the chatbox resolver |
| Animated widget art (`.gif`/`.apng` on buttons) | Panel images are static; only viewport-side art animates |
| `courtroom_sounds.ini` UI sounds for our own extra controls | Only AO2's own sound keys exist; our extra buttons are silent |

### Things that look like gaps but are not
- **1x1 transparent artwork** for a button is faithful: the theme paints that button into
  `courtroombackground` and gives it real art only on `:hover`.
- **`music_display` as a full-window image** is a decoration, not an OOC header - see
  `Documentation/AO2ThemeImport.md`.
- **Chatbox, IC text markup, effects, shouts, stickers and misc packs** are already consumed by the
  viewport and chatbox resolvers, so they need no import at all.
