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

**Appearance is imported too, not just geometry** (`ApplyThemeAppearance`):
- **Artwork** per panel from the theme's own files, using AO2's `setImage` names - which are *not* the
  design.ini identifiers: `holdit`, `takethat`, `custom`, `realization`, `screenshake`, `pair_button`,
  `courtroom_settings` (legacy `settings`) - with the `*_selected` / `*_pressed` variant as the panel's
  checked image, probing extensions in AO2's order (`.webp`, `.apng`, `.gif`, `.png`) and falling back
  from the theme to the default theme.
- **Typography** from `courtroom_fonts.ini`: size, family, bold and colour (converted from AO2's
  `r, g, b` form) onto the log, OOC and IC text panels.
- **The backdrop** (`courtroombackground`) as a **locked image panel** at the theme's own rectangle, with
  the lowest stacking order. It deliberately is *not* a surface-wide fill: the Oceanya surface is wider
  than the theme (the clients strip), so a stretched fill stopped lining up with the widgets drawn on it.
  Being a panel also means the user can move, resize, restyle or delete it like anything else.
- **The viewport theme is switched to match** (`config.ini` `theme`), because the chatbox, in-viewport
  art, chat colours and court sounds already come from that setting - leaving it pointed at the old theme
  imported half a look.

**Rule: the importer may only write settings the layout editor can also set by hand.** Panel image and
selected image, font family/size/bold/colour, opacity, background colour, scaling, item size, stacking,
lock state and added image/colour panels all have editor UI. If a future import needs something new, add
the editor control first - an imported theme must never contain something a user could not build
themselves.

### How each control is positioned
Every imported rectangle is shifted right by `ClientsStripWidth` (54) so the strip of Oceanya-only
controls fits on the left. The backdrop panel is shifted by the same amount and keeps the theme's own
size, so relative alignment is preserved exactly.

1. **Direct** - one AO2 rectangle, one Oceanya panel: the rectangle is used as-is (clamped to the panel's
   minimum size). See the mapping table above.
2. **Composite** - one AO2 rectangle, several Oceanya panels (`ApplyCompositeRegions`). AO2 has a single
   `server_chatlog` rectangle while we split the OOC block into background, header bar, header text and
   chat text, so the children are laid out *inside* the imported rectangle: header across the top,
   chat filling the rest, background covering the whole thing. Leaving them at their Oceanya defaults
   tore the block apart, which is why this exists.
3. **Stranded essentials** - a mapped panel the theme does not describe, but that Oceanya cannot lose
   (IC message/showname, emote grid, the six dropdowns, OOC message/showname). AO2 hiding its own
   position dropdown is harmless there; here it would remove the only way to change position, so these
   are parked in the left strip instead of hidden (`EssentialMappedPanelIds`).
4. **Oceanya-only** - no AO2 counterpart at all (clients strip, edit-layout/refresh/debug buttons, the
   option checkboxes): packed into the left strip (`FunctionalOnlyPanelIds`), with the clients list
   taking the free height.
5. **Cosmetic Oceanya-only** - hidden, so the theme's own look comes through (`CosmeticOnlyPanelIds`).
6. **Everything else the theme mentions but we have no panel for** - reported as unmapped and ignored.

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

## Sizing the imported window
AO2's `courtroom = 0,0,W,H` is the whole window. In Oceanya that rectangle is the **canvas**, because the
connection-info bar sits above it, so the imported surface is `H + Ao2ThemeLayoutImporter.TopBarHeight`
(24) tall and `W + ClientsStripWidth` (54) wide. Without the extra 24 the theme's bottom row of widgets
(settings, music, area) landed outside the window and was clipped.

## Fonts: only the widgets AO2 means
`courtroom_fonts.ini`'s `showname` and `message` are the **viewport chatbox** fonts, not the IC input
line - Oceanya already themes those through `AO2ChatPreviewResolver`. They are deliberately *not*
mapped onto `ic_showname`/`ic_message`; doing so made the input boxes render at chatbox size. AO2 itself
gives the input line the application font, and its colours come from the stylesheet's `QLineEdit` rule.

## Emote buttons
AO2 does not stretch emote buttons to fill the emote area. `AO2-Client/src/emotes.cpp` reads
`emote_button_size` and `emote_button_spacing` from `courtroom_design.ini` (falling back through the
theme chain to the default theme's `40,40` / `9,9`) and fits `((area - button) / (spacing + button)) + 1`
buttons per axis. The importer translates that into the emote grid's **item size** - the field the grid
settings popup already exposes - and `PageButtonGrid` derives rows and columns from it. GrayGarden ships
no `emote_button_size`, so it inherits 40x40 and its `400x120` area holds exactly 10x3.

Two related fixes made that visible: the emote button face was hard-coded to 40x40 inside its control
template (so buttons overlapped once a cell was smaller), and `PageButtonGrid`'s grid area was inside a
vertically scrollable `ScrollViewer`, which handed the grid infinite height and collapsed its star-sized
rows to content size. The face now fills the button and the area no longer scrolls.

## State artwork: the trick most CSS-heavy themes use
Several themes ship a **1x1 transparent PNG** as a widget's base image, paint the button's resting
appearance into `courtroombackground`, and supply the real artwork only for the hover and pressed states
through the stylesheet. GrayGarden does exactly this for `realization`, `screenshake`, `pair_button`,
`holdit`, `objection` and `takethat` (81-byte 1x1 PNGs) - which is why those buttons "invert while
pressed" in AO2: that is `hover/topleft.png`, not a colour effect.

So a panel carries three images, all three editable by hand in the Image settings popup:
`ImagePath` (resting), `CheckedImagePath` (AO2's `*_selected`, `:pressed`, `:checked`, `:on`) and
`HoverImagePath` (`:hover`). WPF template triggers cannot be rewritten from data, so
`OceanyaPanelStyleApplier.ApplyStateArt` swaps the face with `MouseEnter`/`MouseLeave`/`Checked`
handlers and registers an undo, because handlers are not dependency properties and the visual baseline
cannot restore them on its own.

`ResolveThemeImage` probes **every extension inside one theme before falling back to the default
theme**, matching AO2's `get_image_suffix(get_theme_path(...))`. Probing extension-first instead handed
GrayGarden's `holdit.png` over to `default/holdit.gif`.

## Hover versus selected
AO2 leaves a checked button alone on hover - its selected art stays put - so hover art is only painted
while the panel is not checked. Painting it regardless looked like the button un-pressing itself under
the pointer.

## Emote paging arrows
AO2 places `emote_left` and `emote_right` itself and skins them from its stylesheet, so they are panels
here too (`ic_emote_prev`, `ic_emote_next`), handed over by `PageButtonGrid.ExtractPagingControls()`.
Their default placements are the exact rectangles they occupied inside the grid in 7.12, and the grid
keeps that inset unless a theme turns it off - `OceanyaPanelPlacementState.ReservePagingSpace`, a
checkbox in the grid settings popup, which an import clears because AO2's `emotes` rectangle is nothing
but buttons.

Two traps came out of that split:
- The arrows wear `PageButtonGrid`'s **implicit** `Button` style, and an implicit style is resolved
  through the ancestor chain - which they leave when they are reparented onto the main canvas.
  `ExtractPagingControls` pins the style locally so their stock look survives the move.
- The reservation is applied by **spanning** the item area across the grid, never by resizing row and
  column definitions. `UpdateButtonVisibility` adds and removes definitions as the scroll mode changes,
  so index 1 is not reliably the content cell; zeroing the wrong one left the item area 0px tall and the
  emote grid disappeared entirely. Pinned by `UnitTests/PageButtonGridLayoutTests.cs`.

An imported arrow gets the theme's own art through the normal panel image field, and a themed face clears
a glyph label like `<` so the art is not covered by text (tracked in the baseline, so a reset brings the
label back).

## What the stylesheet can reach
`Ao2StylesheetTranslator` now understands the three things GrayGarden-style themes actually rely on:

| Selector form | Meaning here |
|---|---|
| `QComboBox { ... }` | every panel of the matching kind |
| `QPushButton[x="176"][y="657"]` | the single panel whose AO2 rectangle matches |
| `:hover`, `:pressed` / `:checked` / `:on` | that panel's hover / selected image |
| `image: url(base/themes/.../x.png)` | the panel's image, resolved against the install root |

Geometry matching needs the theme's own rectangles, which `Translate` collects into
`Ao2ThemeImportResult.Widgets`; a stylesheet picked by hand from the editor has none, so coordinate
selectors are skipped rather than applied to every button of that class. Note AO2 matches Qt's *runtime*
widget properties, so a theme with `theme_scaling_factor > 1` breaks its own coordinate selectors there
too.

`QListWidget`/`QListView` are deliberately **not** mapped: AO2's list widgets are its music, area, pair
and mute lists, none of which is a panel here, and mapping them onto our `ItemGrid` kind painted the
emote grid and clients list with the pair list's background.

## Dropdowns
AO2 colours the closed box with `QComboBox` and the popup list with `QComboBox QAbstractItemView`. Our
popup surface is opaque white by default while its rows inherit the control's foreground, so a theme
asking for white dropdown text produced white-on-white and an unusable list. The popup now follows the
panel's own background whenever one is set (`ApplyDropdownPopupColours`), which keeps one setting for the
whole control and leaves the stock look untouched.

## Stylesheet quirks worth knowing
Qt sizes a `QCheckBox`'s indicator from its font, so a theme that hides the label (`color: transparent`)
uses a huge `font-size` purely to grow the indicator image - GrayGarden asks for `50px`. Read literally
that is enormous text on a panel whose indicator is not glyph-sized, so `Ao2StylesheetTranslator` drops
font declarations from any rule that hides its own text, and from checkbox-kind panels entirely.

## Fonts are points, not pixels
`courtroom_fonts.ini` sizes go through `QFont::setPointSize`, so they are Qt **point** sizes, while WPF
font sizes are device independent pixels (1/96 inch). The importer multiplies by 96/72; copying the number
straight across rendered text a quarter too small, which is what clipped the OOC header - its panel height
hugs the font size, so the glyphs no longer fit.

## Surfaces, borders and scrollbars
A Qt widget draws no frame unless the stylesheet asks for one, while several of our stock controls do (the
emote grid's grey outline, the dropdown borders). So an import sets `BorderThickness = 0` on every mapped
panel and lets the stylesheet put back exactly the borders the theme asks for. That is why the field is
tri-state: **negative** means "leave the control's own border alone", **zero** genuinely removes it.

| Theme asks for | Panel field | Where it lands |
|---|---|---|
| `background-color` / `background` | `BackgroundColor` | the panel *and* the control inside it |
| `border` / `border-<side>` | `BorderColor`, `BorderThickness` | every control and border in the panel |
| `QScrollBar` background | `ScrollbarTrackColor` | themed scrollbar style, per panel |
| `QScrollBar::handle` background | `ScrollbarHandleColor` | same |
| `QScrollBar` border colour | `ScrollbarBorderColor` | same |
| `QCheckBox::indicator` (`:checked`) | `IndicatorImagePath`, `CheckedIndicatorImagePath` | generated checkbox template |
| `QCheckBox { image: url(...) }` | `ImagePath` | the checkbox's caption, drawn as artwork |
| `QComboBox::down-arrow` | `IndicatorImagePath` | the dropdown's arrow button |

Text panels are a container plus the real control, each with its own background, so a background colour is
pushed onto the `TextBox`/`ComboBox`/`CheckBox` inside as well - painting only the container left the
theme's colour hidden behind the control's own.

Scrollbars are themed by putting `Styles/OceanyaThemedScrollBar.xaml`'s style and three colour brushes
into the **panel's own resource scope**, so the change is per-panel and the undo is removing them again
(registered through `PanelStateOverrides`, since resources are not dependency properties).

**Sub-controls only mean what their widget class says.** `::down-arrow` is a dropdown's arrow on a
`QComboBox` and a scrollbar's arrow on a `QScrollBar`; treating them alike put scroll arrows on the logs.
`IsSubControlOfClass` gates that pairing.

## Glyph button faces
A button's face can be an `Image`, a masked `Rectangle`, or an **icon-font glyph** (the settings and
refresh buttons). `SetFaceArt` tries all three in that order; the glyph case paints the artwork as the
face's background and hides the glyph, which is why the settings button ignored its theme artwork before.

## Baseline trap: Background is one shared property
`Control.Background`, `Border.Background` and `Panel.Background` are the **same** `DependencyProperty`
(WPF shares it through `AddOwner`). Excluding "Panel.Background" from a control's baseline therefore drops
that control's background from the baseline entirely, and a reset stops restoring it.

## The OOC header, and when the music display is one
In AO2's **default** theme `music_display = 490, 0, 224, 26` sits directly on top of
`server_chatlog = 490, 1, 224, 277` - same x, same width - and `music_name` is the label drawn inside it
(its coordinates are **relative to the display**, as AO2's own design file comments). That is exactly the
shape of our OOC header: a backdrop bar with one line of text on it, and its artwork
(`music_display.webp`) is the black bar itself.

But a theme can also repurpose the same widget as a plain decoration: GrayGarden's is `0, 0, 1262, 700`
with a window-sized overlay image. Importing that as "the OOC header backdrop" produced a window-sized
black bar, which is nonsense. So the header mapping is guarded by `IsHeaderShaped`: the rectangle must be
short (at most a third of the OOC log's height, or 32px) and overlap the log's column.

| Theme's music display | `ooc_stream_backdrop` | `ooc_stream_text` | `ooc_chat` |
|---|---|---|---|
| header-shaped (default theme) | the display's rect + its artwork | `music_name`, offset by the display | the whole `server_chatlog` rect |
| anything else (GrayGarden) | **hidden** - nothing describes it | strip along the log's top edge | the rect minus that strip |

When it is not a header, the artwork is still used - as a decoration (below).

## Area and music lists live in the window
AO2's `area_list` and `music_list` are the **lists themselves**, not buttons that open them: a theme draws
both, usually in the same rectangle, and switches between them with `switch_area_music` (the "A/M"
button). Oceanya now matches that shape:

**Both lists share ONE rectangle - AO2's `music_list`.** `courtroom.cpp` calls
`set_size_and_pos(ui_area_list, "music_list")` and then the same for `ui_music_list`, so the area list is
drawn exactly on top of the music list and `switch_area_music` decides which is visible. The `area_list`
identifier is only a **font** key (`set_font(ui_area_list, "", "area_list", ...)`); a rectangle under that
name in a design file describes nothing AO2 draws, and importing it as the area list put the list in the
wrong place entirely. Both our list panels therefore take the `music_list` rectangle.

- `area_list` and `music_list` are panels (`AreaListPanelHost`, `MusicListPanelHost`), each with a
  **Render area/music list in main window** toggle in its right-click menu - same pattern as the viewport.
  Turning one on REPARENTS the popup's content into the panel, so every handler, binding and refresh path
  keeps working, and hides the bottom-bar button that used to open it. Three things have to be undone for
  the content to fit a panel instead of sizing itself: its fixed `Width`/`Height`, its **minimums** (a
  300px-tall minimum inside a 209px panel simply overflowed and was clipped, so the list never scrolled),
  and the popup's own **resize grips** - dragging one handed the surface a fixed size again.
- `bar_button_areamusic` is the A/M switch. It is **hidden by default** (`IsHiddenByDefault` in the
  catalog) because the stock layout uses two popups and has nothing to switch; a theme that describes
  `switch_area_music`, or a user editing the layout, simply un-hides it. This is the general pattern for
  "Oceanya ships more controls than it shows".
- An import turns on in-window rendering for whichever lists the theme placed, and the two bottom-bar
  buttons go to the left strip.

The switch only means something while **both** lists render in the window; with one of them still on its
button, the other simply stays visible.

## Optional controls a theme can summon
The A/M switch is one of a set of panels that exist but start hidden, listed in
`OceanyaPanelCatalog.IsHiddenByDefault`: `bar_button_areamusic`, `bar_button_mute`,
`bar_button_evidence`, `bar_button_reloadtheme`, `bar_button_changecharacter`, `bar_button_callmod` and the
three `slider_*_volume` panels. Each maps to an AO2 identifier, so importing a theme that places one makes
it appear, and a user editing the stock layout can un-hide it from the toolbar's *Hidden controls* list.

Two notes on behaviour: the evidence button deliberately just reports that Oceanya does not support
evidence (that is its whole job), and `call_mod` mirrors `Courtroom::on_call_mod_clicked` - a bare `ZZ#%`,
or `ZZ#<reason>#-1#%` when the server advertises `modcall_reason`, which is the only case where AO2 asks
for a reason first. Servers rate limit mod calls and answer over OOC.

Every one of these needs a control template containing an `Image` (`ThemeableFaceButton` in
`MainWindow.xaml`): a default WPF `Button` template has no `Image`, no `Shape` and no `Panel`, so an image
override has nothing to paint and the theme's artwork silently did not apply.

## Themed art replaces the control's own chrome
An AO2 button has no fill or frame of its own - the theme's artwork *is* the button, and it is often a
transparent 1x1 with the real look painted into `courtroombackground`. So when an image override lands on a
panel, `ClearThemedButtonChrome` drops the control's background and border (unless the theme asked for a
background explicitly). Without it, our own default fill drew a coloured square behind - or instead of -
the theme's art, which is what the mute and evidence buttons looked like.

## Sliders
AO2 skins `QSlider` through its stylesheet, not through named images, in four parts:

| Qt sub-control | GrayGarden | Panel field |
|---|---|---|
| `::groove` | `sliders/meter.png` (168x30 - the tick marks) | `ImagePath` |
| `::handle` | `sliders/handle.png` (15x15) | `IndicatorImagePath` |
| `::sub-page` | solid white, 5px tall, centred | `FillColor` |
| `::add-page` | transparent with a 1px border | `BackgroundColor` + `BorderColor` |

The groove art is only the tick marks, so without the two pages the slider looked empty - the fill *is*
`sub-page`. Qt draws the pages as two halves of one bar, which reads as a single bordered rectangle with a
filled left side, so the track is drawn **once** at full width with the fill inside it; bordering each half
separately left a seam at the handle. The fill also runs half a handle past the thumb, so it reaches the
middle of the handle art instead of stopping at its edge.

`ApplySliderArt` puts `Styles/OceanyaThemedSlider.xaml` plus its brushes into the panel's own resource
scope - same mechanism as the scrollbars, so the undo is removing them again.

**A slider skips the generic background and border passes**, because it expresses both through its own
template: the background is the *empty half* of its track and the border frames that track. Running the
generic passes as well painted over the whole widget and then cleared the frame the template had just drawn,
which is why the track had no outline. Pinned by `ThemedSliderKeepsItsTrackFrame`.

The slider *text* is artwork too: GrayGarden draws all three captions as one 370x12 `sliders/labels.png`
placed on `blip_label`. `slider_music_label`, `slider_sfx_label` and `slider_blip_label` are therefore
image panels of their own (hidden by default), mapped from `music_label`/`sfx_label`/`blip_label`. That is
also why `QLabel` maps to both text-toggle *and* image-button kinds: a Qt label is used for text and as a
plain image display, and our two kinds split exactly along that line.

## Judge controls and the showname toggle
The health bars are plain image panels: AO2's `Courtroom::set_hp_bar` picks the asset from the value
(`defensebar0`..`defensebar10`), so the artwork is resolved per change through the theme chain rather than
imported once, driven by `HP#<bar>#<value>#%` packets (`AOClient.OnHealthChanged`). **Those packets arrive
on the CONNECTED client**, which in single-internal-client mode is never a profile - so the handler is
attached where that client is created, and separately per profile in multi-client mode, or the bars simply
never repaint. Their plus and minus
buttons only *ask* - `AOClient.SetHealth` sends the packet and the bar moves when the server echoes it,
because the server may refuse. The four testimony and verdict buttons send `RT#` the way AO2 does:
`testimony1`, `testimony2`, `judgeruling#0`, `judgeruling#1`.

**Judge controls follow the position, the bars do not.** `Courtroom::show_judge_controls` toggles exactly
the four verdict buttons and the four health steppers; the bars stay visible everywhere. Whether a position
counts as a judge position comes from the background's `design.ini` `judges=` list
(`AOApplication::get_pos_is_judge`), with the hard-coded `jud` only as a fallback - that is
`Background.IsJudgePosition`. A panel the layout hides stays hidden either way. AO2 also lets a server
override the whole thing (`judge_state`); we always follow the position.

`showname_enable` maps to `ic_check_showname`. Like AO2 it does not clear the showname box - it stops the
field being sent (`AOClient.SendCustomShowname`), so everyone sees the character's own name instead.

## Decorations
`DecorationImages` lists AO2 `AOImageDisplay` widgets that have no Oceanya control behind them. Each is
imported as a **locked, click-through image panel** at its own rectangle, stacked just above the theme
backdrop - the same machinery as the courtroom background, so nothing here is import-only. Themes lean on
these for the artwork that ties the window together (GrayGarden paints characters and framing into a
window-sized `music_display.png`), so dropping them loses a visible chunk of the theme. `music_display` is
skipped when the header mapping already claimed it, or its artwork would appear twice.

## Checkboxes are layered, not side by side
Qt draws a checkbox's two graphics on top of each other: the widget's own `image` covers the whole widget
(caption art that already includes an empty tick box) and `::indicator` paints the interactive box over it
at its own pixel size, aligned left. GrayGarden's `checkboxes/pre.png` is 65x15 - the full widget - and
`checkboxes/indicator.png` is 15x15. Laying them out side by side showed the symbol twice.

## Label padding clips text
A WPF `Label` defaults to 5px padding on every side; a Qt label has none. On a panel the theme sized, that
padding eats 10px of height, and with a smaller theme font the bottom half of the glyphs was clipped - the
OOC header being the visible case. Setting a font on a panel therefore drops the padding of any `Label`
inside it (baseline-tracked through `Control.PaddingProperty`, so a reset brings it back).

## Fonts never resize a themed panel
In AO2 a widget's geometry comes from the design file and owes nothing to its font. Our text panels hug
their font size so hand-editing cannot clip them, but that must only ever **grow** a panel: shrinking one
clipped the text of every panel a theme had sized deliberately (the OOC header, for one).

## Dropdown internals
The dropdown's icon cell had a fixed 14x14 size and 3px padding, which boxed a themed icon inside dead
space; it now fills the row height. The arrow button's width is themeable through
`OceanyaPanelPlacementState.IndicatorWidth` (an editor field - Qt has no theme value for it), applied to
the template's `columnDropdown`. Neither a `ColumnDefinition`'s width nor a hidden glyph's visibility is
part of the element walk the visual baseline does, so both register their own undo.

## Not baseline-tracked on purpose
`Visibility` and `Content` are managed at runtime - placeholder captions appear and disappear, hosts are
filled in code - so restoring a value captured at some earlier moment fights that code. It put the
"OOCName" placeholder caption back on top of a real showname. Styling that touches either registers an
explicit undo instead (`PanelStateOverrides`), and that is the rule for any future runtime-owned property.

## The OOC header shows the song
Our header bar is AO2's music display, so it carries what AO2 puts there: the **currently playing song**,
not the client. AO2's label is a `ScrollText` (`AO2-Client/src/scrolltext.cpp`), and `OOCLog.SetNowPlaying`
reproduces it:

- The text is AO2's, from `AOMusicPlayer::playStream`: `"None"` for the stop token, `[MISSING] name` when
  the file cannot be opened, `[STREAM] name` for an http song, the bare name otherwise - where the name is
  the file name without its extension. (AO2 also flashes `[LOADING] name` while BASS opens the file; we
  resolve the path before showing anything, so there is no loading window to report.)
- Scrolls only when the name is wider than the widget minus a margin of **height/3** on each side.
- One **continuous loop** - the name plus a `"   ---   "` separator, drawn twice so the wrap is seamless -
  at 2px every 50ms, after holding still for the first 64 steps. Not a bounce, and never trimmed.
- The text sits in a **Canvas**, not a Grid: a Grid measures its child against its own width, so the name
  was only laid out as wide as the widget and everything past that was never rendered - it appeared to end
  mid-word however far it scrolled. A Canvas measures unconstrained and `ClipToBounds` still hides the
  overflow. The Canvas has to be told to stretch **on both axes**, since it has no desired size of its own
  and a `Label` aligns its content - horizontally left and vertically centred - so the viewport measured
  0px wide, and then 0px tall, and the header showed nothing at all. The rebuild also needs a re-entrancy
  guard - it rewrites the text, which resizes
  the track, which can raise the very size change that called it (the label flickered between "None" and
  its scrolling form).
- Faded at both edges while scrolling. Two traps here: AO2's alpha channel is painted over fixed strips at
  each side with the text moving *under* it, so the mask uses **absolute** mapping - a relative gradient is
  mapped onto the bounding box of what is rendered, which includes the translated text, and the fade slid
  along with the scroll. And it is measured from the **viewport**, whose size settles a layout pass after
  the label's; measuring too early pinned the fade partway across the widget. AO2 fades a fixed 15px, which
  it can afford on a 224px music display, so on a narrower label the fade is capped to a fraction of the
  width.

## The shared search box
AO2 has one search box (`music_search`) that filters whichever list is on screen. That is the
`area_music_search` panel, hidden by default like the other optional widgets. Only our music list filters,
so the box filters both: the music tree through its existing filter, and the area rows through
`ApplyAreaNavigatorFilter` (which keeps the unfiltered rows, so clearing the box costs nothing). While the
box is on screen with the music list rendered in the window, the list's **own** search box steps aside so
the rows get that space back. This is the general shape for "the theme provides the control we already have inside a popup".

## Who owns a panel's visibility
`ApplyHiddenPanels` writes `Visible` as well as `Collapsed`, because only collapsing meant a panel hidden by
one layout stayed hidden when the next layout wanted it - which is why resetting to defaults and importing a
theme **in the same session** lost the optional widgets (the A/M switch, mute, evidence...) until the client
was restarted, where the XAML's own visibility applied instead.

But a set of panels are only on screen in certain states - a debug build, test mode, the Dredd feature being
on, a list rendering in the window rather than a popup, standing at a judge position, a dropdown being off
its default - and showing those from a layout revealed everything the app deliberately keeps hidden. So they
are listed in `OceanyaPanelCatalog.HasRuntimeControlledVisibility`, where **a layout may hide but never
show**, and `MainWindow.ApplyConditionalPanelVisibility` re-applies their real rules after every layout
apply. That is the general rule: one owner per panel's visibility, and the layout is not always it.

Pinned by `ApplyHiddenPanels_ShowsPanelsALaterLayoutNoLongerHides`.

## Replacing a layout drops its queued work
Artwork and colours are applied on **deferred** callbacks, because a panel's control template does not
exist before it renders. Resetting a theme and importing again inside one session left those callbacks
queued from the old layout, and they landed *after* the new one had been applied - and worse, the fresh
baseline then recorded the themed value as the original, so the damage survived. Each callback now captures
a generation stamp (`OceanyaPanelStyleApplier.InvalidatePendingWork`, called from
`RestorePanelStyleBaselines`) and does nothing if it is no longer current. Pinned by
`StylingQueuedByAReplacedLayoutIsDropped`.

## Small panels
An AO2 theme can give a control a fraction of the space Oceanya designed it at - GrayGarden's music list is
182x209 against a 320x420 popup - so three things now shrink rather than break:

- **The lists** shed chrome in order of how little it is worth at that size (`ApplyHostedListCompactMode`):
  decoration first (title, current area, now-playing block), then the command row, and the music search box
  is the *last* thing to go because it is how a big list stays navigable. Padding tightens at the same time.
  This beats scaling everything down equally, which just makes every row unreadable at once.
- **Dropdowns** scale their row icon, row text and arrow button with the control
  (`ImageComboBox.RefreshCompactMetrics`), clamped to their stock values so a normal-sized dropdown is
  untouched, and the arrow may never take more than a third of the width. A theme's own arrow width goes
  through `SetArrowWidthOverride` so the two do not fight.
- The dropdown's arrow button has its own template, because the stock `ToggleButton` chrome painted a solid
  square over the arrow while the popup was open - something AO2 never does.

## Dropdown reset buttons
AO2 puts a small X to the left of the iniswap, sfx and pos dropdowns (`iniswap_remove`, `sfx_remove`,
`pos_remove`, all drawn with the `evidencex` art) and shows it **only while that dropdown is off its
default** (`Courtroom::on_pos_dropdown_changed`); clicking it resets the dropdown. Those are
`ic_combo_*_reset`, hidden by default like the rest of the optional widgets, and their visibility follows
`ICMessageSettings.DropdownDefaultStateChanged`.

## Log colours and backgrounds
A log is more than one colour in AO2. `courtroom_fonts.ini` carries a body colour plus separate colours for
names and timestamps, and `Courtroom::append_ic_text` / `append_server_chatmessage` paint each run with
them:

| AO2 key | Panel field | Used for |
|---|---|---|
| `ic_chatlog_color`, `server_chatlog_color` | `TextColor` | body text |
| `ic_chatlog_showname_color`, `ms_chatlog_sender_color` | `SenderColor` | other people's names |
| `server_chatlog_sender_color` | `ServerNameColor` | the name on a server message |
| `ic_chatlog_selfname_color` | `SelfNameColor` | your own name |
| `ic_chatlog_selftimestamp_color`, else `ic_chatlog_timestamp_color` | `TimestampColor` | the `[GM]` marker, which only appears on your own lines |

**`courtroom_fonts.ini` is merged with the default theme per key**, because AO2 resolves each key through
the theme chain. That matters more than it sounds: of the 61 themes in the reference install, 51 set
`ic_chatlog_color` but only **two** set any of the name or timestamp colours - the default theme is where
those live, so reading a single file left every other theme falling back to *our* colours instead of AO2's.

AO2 picks an OOC name colour by **where the message came from**, not by which log it lands in: a plain
player message carries colour flag `"0"` on its `CT#` packet and uses `ms_chatlog_sender_color`, while a
server one carries `"1"` and uses `server_chatlog_sender_color` (`Courtroom::append_server_chatmessage`).

Messages already in a log were written with the colours in force at the time, so each run carries its role
in `Tag` (`LogRunRole`) and `LogRunRecolorer` repaints the whole backlog when the colours change - otherwise
applying a theme leaves a log in two colour schemes at once. Clearing the colours repaints it back, which is
what a reset does. IC message colours (green, red, blue) are tagged `None` and never repainted.

Our logs write their runs with **explicit brushes**, so an inherited `Foreground` never reaches them - the
colours are pushed in through `ICLog.SetThemeColors` / `OOCLog.SetThemeColors` by
`MainWindow.ApplyLogThemeColors`. Every field is nullable and null means "keep the control's own colour",
which is what makes a reset free: an empty layout hands the logs nothing.

The IC message colours themselves (green, red, blue...) are deliberately **not** themed: those belong to
the protocol, and AO2 keeps them in `chat_config.ini`, which the viewport already consumes.

AO2's logs are transparent widgets with the courtroom art showing through, so an import clears the
backgrounds our log panels paint. That has to reach whatever is **actually painting** - the log
panels put their artwork on a Grid several levels down, behind the UserControl's own template border, so
painting the control's brush (or the first container found) left the picture untouched and a theme asking
for a transparent log got it anyway. Both that and the four colours are editable by hand: a static panel's
font dialog carries them, along with a background image and its scaling.

## The OOC chat keeps the whole chatlog
A strip is only carved off the top of the chat when the header text actually sits there. When the theme
puts that text somewhere else - GrayGarden draws its music name beside the realization button - the chat
takes the entire `server_chatlog` rectangle, which is what AO2 does.

## No flash before the theme lands
Panel artwork and colours are applied on deferred callbacks, so the first frame would otherwise show the
stock layout with the theme arriving on top of it. `MainWindow` keeps the surface hidden until that first
pass has run (revealed by a callback queued after them, at the same priority), which happens while the
launch wait form is still up.

## Judge controls: the whole rule
AO2 decides in two steps (`Courtroom::show_judge_controls`):

1. The `JD#` packet is a server override - `1` shows the controls wherever you stand, `0` hides them, and
   `-1` hands the decision back to the client.
2. Otherwise it tests `current_or_default_side()` against the background's `judges=` list. **Current or
   default**: a character whose own side is a judge position gets the controls without picking anything
   from the dropdown.

Both are implemented: `AOClient.JudgeControlsState` / `CurrentOrDefaultSide` and
`MainWindow.UpdateJudgeControlVisibility`. Only the four verdict buttons and the four health steppers are
affected; the bars stay visible everywhere.

## Capturing the comparison screenshots
Theme parity is judged by putting an AO2 screenshot of a theme next to an Oceanya screenshot of the same
theme. The AO2 half is fixed (that client never changes), so only the Oceanya half is re-captured as the
theme system improves - which is why it is a first-class, in-process feature rather than UI automation.

```bash
OceanyaClient.exe --test-mode --test-auto-launch-startup \
  --test-startup-functionality=gm_multi_client \
  --test-savefile="<fixture with a one-client GM snapshot>" \
  --test-config-ini="<AO2 base>/config.ini" \
  --test-server-endpoint=tcp://127.0.0.1:27080 \
  --test-capture-themes=@themes.txt \
  --test-capture-output=<dir> \
  --test-capture-exit-when-done
```

- `--test-capture-themes` takes `*` (every theme), `@file` (one name per line) or a comma-separated list.
  Prefer `@file`: theme folder names contain commas, spaces and accents.
- `--test-capture-settle-ms` (default 900) is the pause between apply, IC line and capture.
- `--test-capture-message` is the IC line sent before each capture; `{theme}` is substituted. Empty sends
  nothing and captures bare chrome.
- Output: one PNG per theme plus `capture_manifest.tsv`, written per theme as it happens so a stalled sweep
  still says which theme it reached.

Owner: `OceanyaClient/Features/Theme/Ao2ThemeCaptureRunner.cs`; the savefile/config writes are shared with
the Settings button through `Ao2ThemeLayoutApplier` so a capture always shows what the button produces.

**Do not rewrite the sweep with `async`/`await`.** Measured during development: while a sweep runs, startup
asset work saturates the thread pool, so an awaited `Task.Delay` - and equally an awaited
`TaskCompletionSource` - never resumes, and the sweep hangs on its first theme. A Normal-priority
`DispatcherTimer` keeps ticking throughout, so the runner is a timer state machine with no `await` in it.
Waiting on the dispatcher queue does not work either: an animated theme keeps the UI thread busy at Render
priority (7), which outranks Loaded (6), Background (4) and both idle levels.

Use a fixture savefile carrying a one-client `GMMultiClientSnapshot` so startup restores a character instead
of opening the character selector; do **not** pass `--test-disable-gm-snapshot-persistence` for a capture run.

### The comparison viewer
`ThemeParity/index.html` (gitignored; ~20MB of PNGs beside it). Open it in a browser - it is a plain local
file, no server needed.

It **blink-compares** rather than showing the pair side by side: both frames occupy the same box, one
visible at a time, so swapping them makes a real difference jump out where two images an inch apart hide it.

- <kbd>&larr;</kbd>/<kbd>&rarr;</kbd> or click the image: swap AO2 &harr; Oceanya
- <kbd>Space</kbd> (held): peek at Oceanya, release to snap back
- <kbd>&uarr;</kbd>/<kbd>&darr;</kbd>: previous/next theme
- <kbd>A</kbd> align, <kbd>Z</kbd> fit/1:1, <kbd>S</kbd> side-by-side, <kbd>Alt</kbd>+arrows nudge the offset

**Alignment is the thing that makes it work.** Oceanya adds a 54px clients strip on the left and a 24px
connection bar on top, so the theme's own content starts at (54, 24) in our capture and at (0, 0) in AO2's.
The viewer shifts the Oceanya frame by `-54, -24` so the two line up; without it every blink reads as "the
whole window moved" and the actual differences are invisible. The rail shows each theme's delta and flags
any theme that is not exactly `+54,+24`.

Re-capture the Oceanya half with `ThemeParity/refresh-oceanya.cmd` (needs the local test server on
127.0.0.1:27080), then refresh the browser. The AO2 half is never regenerated.

## AO2's own defaults are part of the theme
The trap this section exists to prevent: when a theme does **not** style something, the answer is not
"leave Oceanya's own look on it" - it is "use AO2's look". AO2 is a Qt app on the native Windows style, so
an un-skinned widget is a plain platform control, and `AOButton::setImage` says so outright: with no art it
calls `setStyleSheet(QString())` and `setIcon(QIcon())` and falls back to the platform push button, text
label and all. Sliders and fonts work the same way - AO2 only calls `set_font` for the widgets in its own
`set_fonts()` list, so everything else draws in the application UI font.

- `OceanyaPanelPlacementState.UseAo2DefaultChrome` is set on every panel an import touches.
- `OceanyaClient/Styles/Ao2DefaultChrome.xaml` holds the stock button and slider looks. **The colours in it
  are sampled from AO2 captures, not invented** - the slider groove is `#E7EAEA` on `#D6D6D6` and the handle
  is the Windows accent `#007AD9`. If they ever look wrong, re-sample rather than guess.
- `OceanyaPanelStyleApplier.ApplyAo2DefaultChrome` applies it first, so anything the theme *did* say still
  wins on top; button captions come from `courtroom.cpp`'s own `setText` calls.

## Qt stylesheet traps
- **Qt matches on the widget's real class.** A theme's `QLabel` rule never touches a `QPushButton`. Our
  class-to-kind map lumps labels and image buttons together, so `OceanyaPanelCatalog.Ao2PushButtonPanelIds`
  is what keeps label rules off real buttons - without it AAI's `QLabel { background-color: transparent }`
  erases the fill of every bar button and shout in the theme.
- **Declarations later in a rule win.** AAI writes `border: 1px solid rgba(255,0,255,150); ... ;
  border-color: black`, which draws a BLACK border. Reading only the shorthand put magenta outlines around
  every AAI dropdown. `ApplyBorderLonghandOverrides` handles the long-hands.
- **`transparent` is an instruction, not a parse failure.** Treating it as unreadable left our opaque fill
  in place, so a label the theme wanted see-through rendered solid black.
- **Alpha in Qt stylesheets is 0-255**, not CSS's 0-1. Already handled; do not "fix" it.
- **Named colours pass through verbatim** (`darkgray`) for WPF to resolve, so do not assert hex in tests.

## Applying style to a panel
Anything that walks a panel's visual tree must be queued at `DispatcherPriority.Loaded` as well as run
inline. Several panels' inner controls are re-parented into the theme surface after the style pass, so a
purely synchronous walk finds an empty tree and silently does nothing - that is what left themed text boxes
wearing our font and foreground while their themed background, which was already deferred, applied fine.

## Animated theme art
AO2 drives **every** piece of button art through a `QMovie`, so animated shouts are normal, not exotic -
AAI ships `holdit.gif`/`objection.gif`/`takethat.gif` next to static PNGs and AO2 probes `.webp/.apng/.gif/
.png` in that order, so the animated file is the one it picks. `Ao2ThemeArtAnimation` plays them and
`Ao2ThemeArtAnimations.StopAll()` (called from `InvalidatePendingWork`) stops the previous pass's players -
a live player retains its decoded frames, so skipping that leaks memory on every layout pass.

## Round 2 rules (from the AAI comparison)
- **The chatbox overlaps the viewport.** Every AO2 theme places its chatbox rectangle on top of the
  viewport, so `Ao2ThemeLayoutApplier` turns `GMViewportChatboxOverlapsViewport` on with the import. With
  it off the whole translation is wrong, not just the chatbox.
- **A theme must not style Oceanya's own controls.** `Ao2ThemeLayoutImporter.OceanyaOnlyPanelIds` (the
  functional-only strip) is passed to the translator as an exclusion set. Keep it to genuinely
  Oceanya-only panels: several panels are theme-driven WITHOUT appearing in `PanelMappings` - the
  composite regions place the OOC header from `music_display`/`music_name` - and excluding those stripped
  the colour that made them visible at all.
- **`UseAo2DefaultChrome` is for AO2 widgets only.** Oceanya's own controls (clients strip, add/remove,
  the strip's icon buttons) pass `useAo2DefaultChrome: false`; their faces are icon glyphs and AO2's stock
  button chrome turns them into blank platform buttons. A *stranded essential* - a real AO2 widget parked
  in the strip because the theme gave it no rectangle - still keeps AO2 chrome.
- **A stylesheet rule styles the widget, not its template parts.** Border and background application stops
  at the first `Control`; descending further drew the theme's 1px frame on the combo box, its text area and
  its drop-down button, which read as one thick border.
- **Un-skinned checkboxes and the slider labels are AO2 widgets too.** `Ao2DefaultCheckBoxStyle` draws Qt's
  white tick box, and the three volume labels carry AO2's own "Music"/"Sfx"/"Blips" text (they are `Label`s,
  not bare `Image`s, so the text shows when the theme ships no art and is cleared when it does).
- **Image names can have a fallback chain.** `PanelArt` takes an array: AO2 tries `courtroom_settings` and
  falls back to the pre-2.10 `settings`, which is the only art AOHD and several other themes ship.
- **Qt text metrics.** Themed panels get `TextFormattingMode.Display` (WPF's default `Ideal` puts glyphs on
  fractional pixels and renders heavier than Qt) and Qt's `AlignVCenter` default for labels. This narrows
  the gap; it cannot close it, because the two renderers hint differently.
- **A layout that hides a panel outranks test mode.** `MainWindow.AreOptionCheckBoxesAllowed()` - otherwise
  the capture sweep, which runs in test mode, forced Oceanya's option toggles into every themed shot.
- **Watch static field order.** `OceanyaOnlyPanelIds` is a lazy property, not a field initializer: it is
  declared above `PanelMappings`, and initializing eagerly threw in the type initializer and took 19 tests
  with it.

## Round 3 rules
- **The A/M pair opens on the MUSIC list.** `courtroom.cpp` calls `ui_area_list->hide()` at construction,
  so the import sets `GMAreaMusicSlotShowsMusic`. Both lists correctly share one rectangle - that part was
  always right; only the default was wrong.
- **Never blanket-set `VerticalContentAlignment`.** Qt's vertical centring applies to controls whose
  content is TEXT. Applying it to every control collapsed the ones whose content must stretch: the music
  name label hosts a marquee `Canvas`, which has no desired height, so centring it made the label render
  nothing at all - and the IC log's document ended up floating in the middle of its box.
- **A log anchors to the end its messages grow from.** AO2's chat log is a QTextEdit and always lays its
  document from the top; inverted, the newest line is at the bottom. `ICLog.ApplyLogAnchor`.
- **Un-themed text is 9pt (12 DIP).** AO2 only sizes the widgets in its own `set_fonts()` list; everything
  else uses the application font. Measured: AO2's "Music" slider label spans 8px of glyph where ours spanned
  9, and that extra size is what made the labels overlap their sliders.
- **Decorative labels must not take input.** The volume labels are `IsClickThrough` - text even slightly
  wider than the theme's rectangle overhangs the slider beside it and swallows the clicks meant for the
  handle. Note `ApplyPanelCatalogPlacements` drives `IsHitTestVisible` from panel state, so setting it in
  XAML alone is not enough.
- **A checkbox indicator can be skinned with COLOURS, not just art.** AOHD paints its checked tick box
  `#328CBD` with a white border and ships no image. `IndicatorBackgroundColor` / `IndicatorBorderColor` and
  their checked counterparts carry it.
- **`DynamicResource` cannot reach a detached dictionary.** `Ao2DefaultChrome.xaml` is loaded standalone and
  only its Style is taken out, so a key the template resolves dynamically MUST also be written onto the
  control's own `Resources` - otherwise it resolves to null and the element renders with no brush, which is
  how the tick boxes vanished. Always write every key, defaulting to the stock colour.
- **Template-bind chrome that a theme may set.** `ImageComboBox` hardcoded `BorderThickness="1"`, so a theme
  asking for a different frame could not get one, and the un-snapped rounded border read as two pixels.
- **Qt's drop-down is narrower than ours** (~18px against 30). On a dropdown sized to the theme's rectangle
  those pixels come out of the text, which clipped the character name.

## Round 4 rules
- **Qt object-name selectors are supported** (`QPushButton#ui_ooc_toggle`). AOHD does almost all of its
  per-widget styling this way, and ignoring it left those widgets wearing our own look. AO2 names widgets
  `ui_` + the design.ini identifier, so `Ao2ThemeLayoutImporter.TryResolvePanelForIdentifier` answers it
  from the mapping table already there - do not add a second list.
- **Theme asset URLs are relative to the INSTALL ROOT** (`url(base/themes/<theme>/x.png)`), so
  `ResolveAssetRoots` walks every ancestor of the stylesheet as well as asking the theme catalog. Deriving
  the root from the catalog alone silently resolved to nothing whenever the catalog was unconfigured.
- **A slider handle can be skinned with COLOURS, not just art**: `SliderHandleColor` /
  `SliderHandleBorderColor`, from `QSlider::handle`. AOHD paints its handle `#328CBD` with a white border
  and ships no image.
- **The dropdown arrow glyph takes the control's Foreground**, the way Qt's does, so
  `QComboBox { color: white }` gives a white arrow.
- **AO2 hides a control that has nothing to do.** `set_emote_page` hides each emote arrow unless that
  direction exists, and `on_pos_dropdown_changed` hides `pos_remove` when the position equals
  `default_side()` - which is the CHARACTER'S `[Options] side` (falling back to `wit`), not "no position".
  Comparing against empty put a stray X beside the pos dropdown in every theme that places one.
- **Casing is present but inert.** `bar_button_casing` / `ic_check_casing` exist so an imported layout has
  no holes; both say Oceanya does not support casing and do nothing, exactly like the evidence button.
- **The OOC toggle is called the Debug Console** in Oceanya's UI. The panel id stays `ooc_server_console`
  so saved layouts keep working - renaming an id needs a theme compatibility bump.
- **New panels must be registered in three places**, or the catalog guard tests fail: the descriptor list,
  the default z-order table, and `UnitTests/OceanyaPanelCatalogTests` id list.

## The sub-control rule (learned three times, write it down)
A Qt rule styles the **widget**; its sub-controls (`QComboBox::drop-down`, `QCheckBox::indicator`,
`QSlider::handle`) have selectors of their own. Every generic pass that walks a panel's visual tree must
therefore STOP at a nested control, or it silently destroys what a sub-control rule just set:

- borders: `ApplyBorderRecursive` stops at the first `Control` (otherwise a 1px frame is drawn three times
  over and reads as a thick one),
- fills: `ApplyInnerSurfaceRecursive` stops at the widget, and `PaintExistingBackgrounds` skips
  `ButtonBase`/`ComboBox`/`Slider` children entirely - descending repainted the Grid inside the dropdown's
  toggle button and wiped the theme's arrow artwork, which is why GrayGarden's diamond rendered as a plain
  grey box even though the art loaded correctly,
- the checkbox tick box is named `PART_Ao2CheckBoxIndicator` so the same walk skips it.

Two more details the diamond needed: a sub-control `image:` is drawn at its NATURAL size (`Stretch.None`),
not stretched to the slot - GrayGarden's is a 10x10 asset - and the art replaces our own arrow frame, since
the theme says `QComboBox::drop-down { border: hidden }`.

**Debugging note:** `DEBUG.txt` is not reliably flushed by a capture sweep (the run ends via
`Application.Shutdown`). When instrumenting a sweep, append to a dedicated file instead - that is what
turned this from guesswork into a five-minute find.

## The other half of that rule: a placed panel's visibility
The layout pass re-applies every placed panel's visibility from its saved state, so a control that decides
its own visibility at runtime has that decision overwritten the moment a theme places it as a panel. That is
why the emote arrows still showed on both sides after `PageButtonGrid` had already hidden them.

Anything whose visibility depends on RUNTIME state, not on the layout, therefore belongs in
`MainWindow.ApplyConditionalPanelVisibility` (and must respect `IsHiddenByLayout`): the dropdown reset
buttons, the judge controls, the option toggles, and now the emote arrows via
`UpdateEmotePagingVisibility` plus `PageButtonGrid.PagingStateChanged` for the page-turn case.

## Sliders and dropdowns a theme colours
- **A rounded handle is a CIRCLE.** Qt clamps `border-radius` to half the handle box, so AOHD's
  `border-radius: 5px` renders as a dot. Reproducing it needs the handle sized from the slider's height as
  well as the radius applied - a radius on our stock 15px-wide handle only gives a rounded rectangle.
- **A theme that styles only the handle still wants a track.** AOHD says nothing about its groove's fill,
  and AO2 then draws the stock Windows groove; defaulting the two pages to transparent left the handle
  floating on nothing. Groove ARTWORK still wins, because that art is the whole track.
- **The stock groove is a flat 4px, not a fraction of the widget** - measured off an AO2 capture as
  `#D6D6D6` edge, `#E7EAEA` fill, `#E7EAEA` fill, `#D6D6D6` edge. Our `height/6` heuristic gave 2-3px on a
  short slider and read as a grey hairline; it is still used when the theme supplies groove art.
- **A panel minimum that exceeds the theme's rectangle silently overrides the theme.** AOHD declares
  `music_slider` 12px tall, our slider panels carried `minimumHeight: 16`, and the importer takes the max -
  so the handle came out 16px where AO2 draws 12. When a parity gap is "ours is bigger", check the
  descriptor's minimums before touching the drawing code.
- **`QComboBox { background }` paints the drop-down slot too** - it is part of the same widget in Qt.
  `ApplyInnerSurfaceRecursive` paints the combo AND its `btnDropdown`, which is why AOHD's arrow slot is
  blue rather than our stock grey. Arrow ARTWORK still wins: it is applied later and replaces that brush.
