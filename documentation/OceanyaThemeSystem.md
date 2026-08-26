# Oceanya Theme System (roadmap)

## Goal
Users complain that the main window's look is not customizable. AO2 solves this with themes, but AO2
themes cannot drive the Oceanya main window: Oceanya has far more controls than AO2 and no 1-to-1
mapping exists. So the main window carries **two** theme concepts:

- **Viewport theme** — an actual AO2 theme, already supported (`AO2ThemeCatalog`, Settings → Viewport).
- **Oceanya theme** — our own system, described here: photoshop-style dockable panels plus per-panel
  visual and behavioral settings, shipped as a single shareable file.

## Decisions taken (2026-08-17)
| Question | Decision |
|---|---|
| First deliverable | Global UI scaling, shipped independently. See `Documentation/UiScaling.md`. |
| Docking implementation | `Dirkster.AvalonDock` (MIT). Wrapped so the shared theme file stores our own layout schema, never AvalonDock XML. |
| Viewport | Dockable **inside** the main window *and* still available as a separate window / PiP. |
| Per-panel settings | Schema-driven and broad: declarative typed settings (color, image, font, int-range, enum) with auto-generated editor UI, including behavioral options such as emote-grid paging. |

## Design philosophy (read this before changing anything)
Why the system looks the way it does, so later changes do not undo the reasoning:

1. **The stock layout is the contract.** Default placements, stacking and sizes reproduce the pre-theme
   (7.12) window *exactly*. Any refactor that moves a control by a pixel is a regression, however
   reasonable it looks. The numbers live in `OceanyaPanelCatalog` and are pinned by
   `Catalog_DefaultPlacementsMatchTheHistoricFixedLayout` and `DefaultZOrder_ReproducesThe712DrawOrder`;
   the historic values come from tag `1b7051c`, which is the source of truth when in doubt.
2. **Panel ids are a public contract.** They are written into saved layouts and shared theme files.
   Renaming or removing one breaks existing themes and requires a theme-compatibility bump. Adding is
   safe.
3. **Placement is data, never XAML.** Nothing positions a panel from XAML any more. The catalog holds the
   defaults, the savefile holds user overrides, and `OceanyaPanelLayout` is the only thing that writes
   `Canvas.Left/Top/Width/Height`.
4. **Reparent, do not rewrite.** Regions were split by handing their existing controls to the main canvas
   (`ExtractPlaceableControls`), leaving each control's code-behind untouched. That is why 11k lines of
   `MainWindow.xaml.cs` and 1.7k of `ICMessageSettings.xaml.cs` still work. Prefer this over rebuilding a
   control, and remember the two consequences: the emptied host control must collapse itself, and **panel
   XAML must carry its own resources**, because a reparented control no longer sees its old parent's
   `ResourceDictionary`.
5. **Group only what must never separate.** Logs keep their background art; everything else is its own
   panel, down to individual shouts, checkboxes and buttons. When in doubt, split - merging later is
   easier than splitting.
6. **The editor never destroys anything silently.** Every panel has *set default position/size*, hidden
   panels are recoverable from the toolbar, and *Reset all* restores placement, stacking, visibility,
   styling and added panels. Any new styling knob must be resettable the same way (see rule 8).
7. **The user owns the surface.** The window does not resize or reposition itself in response to a drag.
   Edit mode lets the user resize the window, and left/top edge drags trim empty space rather than moving
   the layout.
8. **Styling is applied to live controls, so it must be undoable.** `OceanyaPanelStyleApplier` records a
   baseline of *local dependency-property values* before its first mutation and restores with
   `SetValue`/`ClearValue`. Capturing effective values, or storing null for "unset", both caused visible
   bugs (buttons losing their background, transparent logs turning white). Never write a style value
   without extending the baseline to cover it.
9. **Settings live in reusable popups, not in menus.** One dialog per family (font, image, grid), built
   from `Styles/OceanyaDialogStyles.xaml`. No lists of literal values in a context menu, and every field
   offers a way back to "default".
10. **AO2 compatibility is a translation layer, not a foundation.** Our model is deliberately richer than
    AO2's. The importer maps AO2 identifiers onto our panel ids and hides what a theme does not mention
    (as AO2 does), but nothing in the core system depends on AO2 concepts.
11. **The importer may only use features the editor exposes.** Every setting an import writes has to be
    reachable by hand: panel image *and* selected image, fonts (family/size/bold/italic/underline/colour),
    opacity, background colour, scaling, item size, stacking, lock state, and added image/colour panels.
    When AO2 turns out to theme something we cannot yet edit, **add the editor control first**, then teach
    the importer to fill it. An imported theme must never contain something a user could not have built
    themselves. This is also why a themed backdrop is a locked image panel rather than a bespoke
    "surface background" setting - it reuses machinery the user already has.

### Safe-change checklist
- Changing a default placement or z-order? Update the pinning tests and check against tag `1b7051c`.
- Adding a panel? Give it a catalog entry (id, name, minimums, placement, kind), a `DefaultZOrder`, a map
  entry in `MainWindow.BuildPanelElementMap`, and add its id to the catalog test's expected list.
- Splitting a control? Use `ExtractPlaceableControls` + reparenting, collapse the emptied host, move any
  `StaticResource` styles into the panel, and update tests that resolve controls by name.
- Adding a styling option? Add the field to `OceanyaPanelPlacementState`, apply it in
  `OceanyaPanelStyleApplier`, extend the baseline's tracked properties, persist it in `PersistLayout`, and
  surface it in the matching settings dialog.
- Touching edit mode? Check the four traps that have bitten before: overlays are built from visibility at
  activation (call `RebuildOverlays` when visibility changes), the toolbar must outrank overlay z-index,
  `Deactivate` persists by default (pass `persistLayout: false` when replacing a layout), and
  `ApplySurfaceSizeFromLayout` must not fight the user's window size while editing.

## Phases
1. **UI scaling** — done, see `Documentation/UiScaling.md`.
2. **Panel extraction** — in progress. Two halves:
   - *Placement becomes data* (done for the already self-contained regions): `OceanyaPanelCatalog`
     owns each panel's id, display name, minimum size and default placement; `OceanyaPanelLayout`
     applies it to the controls on `MainCanvas`. `MainWindow` calls `ApplyPanelCatalogPlacements()`
     right after `InitializeComponent()`, and the corresponding `Canvas.Left/Top/Width/Height`
     attributes are gone from `MainWindow.xaml`. Registered so far: `ic_log`, `ooc_log`,
     `emote_grid`, `ic_settings` (all already `UserControl`s: `ICLog`, `OOCLog`, `PageButtonGrid`,
     `ICMessageSettings`). Default placements reproduce the historic fixed layout exactly, so nothing
     moves on screen.
   - *Regions extracted into self-contained controls so far* (under `OceanyaClient/Components/Panels/`):
     `ConnectionInfoPanel` (server/area/users/status/lock/CM chips; host pushes values through
     `SetConnectionInfo`, panel owns chip visibility and `GetStatusChipBrush`), `DreddFeatureRowPanel`
     (overlay selector, sticky checkbox, Config and View Changes buttons; raises `OverlaySelected` /
     `StickyOverlayChanged` / `ConfigRequested` / `ViewChangesRequested` while `MainWindow` keeps the
     overlay logic and savefile handling), `ClientsListPanel` (the "Clients" header, add/remove
     buttons and the paged client button grid; raises `AddClientRequested` / `RemoveClientRequested`,
     exposes `ButtonGrid` and `SetHeaderContextMenuFactory`), and `ShoutRowPanel` (Hold It /
     Objection / Take That / Custom as a radio group behind a single `SelectedShoutModifier`
     property). `DreddOverlaySelectionItem` moved out of `MainWindow` into the panels namespace.
     Together these removed ~560 lines from `MainWindow.xaml`/`.xaml.cs`.
   - *Naming correction*: `MainWindow`'s `PageButtonGrid` was named `EmoteGrid` but holds **client**
     buttons (the emote grid lives inside `ICMessageSettings`). It is now `ClientsListPanel.ButtonGrid`
     and the panel id is `clients_list`, not `emote_grid`.
     `BottomStatusBarPanel` (option checkboxes plus refresh/settings/debug/area/music/viewport
     buttons; exposes `AreaNavigatorAnchor` / `MusicListAnchor` because WPF popups need a visual to
     position against, so `MainWindow` sets `Popup.PlacementTarget` in code instead of by
     `ElementName` binding).
   - *Still inline in MainWindow*: the area navigator and music list **popups**. They are overlay
     surfaces anchored to bottom bar buttons rather than layout panels, and their contents are
     logic-heavy (area list, music tree), so they stay put until the dock host needs them.
   - **Panel XAML must carry its own resources.** A panel cannot see `MainWindow.xaml`'s resource
     dictionary: moving markup that used `{StaticResource CompactIconButtonStyle}` without moving the
     style threw `XamlParseException` at MainWindow construction. The `MainWindow`-constructing tests
     in `Phase1/2/3ReleaseConfidenceTests` catch this.

## Layout edit mode (shipped)
The pencil toggle at the right of the bottom bar (`Main.EditLayout`) turns on
`OceanyaPanelEditModeController`: a translucent overlay per visible panel, drag anywhere to move,
drag the bottom-right grip to resize. The overlay sits above the panels, so panel buttons cannot fire
mid-drag. Placements are clamped by each descriptor's minimums and kept at non-negative coordinates
(`OceanyaPanelLayout.SanitizePlacement`), and a panel's backdrop keeps its offset and size delta.

Layout persists to `SaveData.OceanyaThemeLayout` (panel id -> Left/Top/Width/Height) on every drop
and on leaving edit mode; `OceanyaPanelLayout.ApplyLayout` applies catalog defaults first and the
saved layout on top, so panels missing from the save keep their default.
`OceanyaPanelEditModeController.ResetLayout()` clears it back to defaults - including **styling**:
fonts, swapped images, scaling and item sizes are applied straight onto the live controls, so the
applier snapshots each panel's untouched appearance (as *local* dependency-property values, with
`UnsetValue` marking "no local value" so a deliberate `{x:Null}` is not confused with an absent one) on
first restyle
(`OceanyaPanelStyleApplier.CaptureBaseline`) and reset restores from it (`RestoreBaseline`), then
un-hides every panel and re-applies the default placements. User-added panels survive a reset.

Right-clicking a panel in edit mode offers **Set default position**, **Set default size**, **Set
default position and size**, and **Set all panels to default**.

### Nothing stays out of reach
A panel dragged or imported past the surface edge is invisible *and* unclickable, which used to mean
hunting for it or resetting the whole layout. Edit mode therefore treats **fully off-surface as hidden**:
such a panel joins the toolbar's *Hidden controls* list (labelled "(off-screen)"), gets no overlay, and
restoring one re-centres it on the surface and brings it to the front, because putting it back where it was
would hide it again.

That list is re-derived (`RefreshUnreachablePanels`) whenever anything could have moved a panel out of
view - edit mode starting, a drop, an overlay rebuild after a layout or visibility change, and a surface
resize - not only when edit mode starts. A panel listed *only* because it is off-surface
(`offSurfacePanelIds`) leaves the list again by itself when it comes back into view, so this can never
swallow a deliberate hide.

That is the general rule behind rule 6: every way a control can disappear needs a way back that does not
require knowing where it went.

### Locking and click-through
- **Lock in place** (`IsLocked`) means "I am finished with this panel": in edit mode it renders **no
  edges, no grip and no hover highlight**, so it stops adding visual noise, and it cannot be dragged or
  resized. Its right-click menu still opens, which is how it gets unlocked again. This is what makes a
  full-surface backdrop workable *as a panel* instead of needing a bespoke "surface background".
- **Click-through** (`IsClickThrough`) sets `IsHitTestVisible = false`, so the panel never intercepts
  clicks meant for whatever is behind it. Backdrops want this; the imported theme backdrop arrives locked
  *and* click-through.
- Imported theme backdrops also arrive at the lowest stacking order, and the `viewport` panel's default
  order is low (5) because AO2 draws the viewport behind its widgets - with a high order it covered the IC
  message and showname and swallowed their clicks.

### Stylesheets
`Ao2StylesheetTranslator` reads a Qt stylesheet (AO2's `courtroom_stylesheets.css` form) and writes the
declarations it can represent - `background-color`, `color`, `font-family`, `font-size`, `font-weight` -
into the **same per-panel fields the settings popups expose**, mapping Qt classes onto panel kinds
(`QLineEdit` -> text inputs, `QComboBox` -> dropdowns, `QCheckBox`/`QLabel` -> text toggles,
`QTextEdit`/`QPlainTextEdit` -> the logs, `QListWidget`/`QListView` -> item grids, `QWidget` -> all).
It is a bulk edit, not a second styling engine, which is why importing one cannot produce a look the user
could not build by hand. Reachable as **Stylesheet...** on the edit toolbar, and used by the AO2 import.

It also understands `image: url(...)`, the `:hover` and `:pressed`/`:checked`/`:on` states (which write
the panel's hover and selected images), and coordinate selectors like `[x="176"][y="657"]`, matched
against the theme's own widget rectangles. Every one of those writes a field the Image settings popup
also exposes.

Deliberately ignored, because no panel field represents them yet: sub-controls (`::drop-down`,
`::indicator`), the remaining pseudo-states, borders and padding. Add the field first, then extend the
translator.

### Three-state artwork
Image-button panels carry a resting, a hover and a selected image. WPF template triggers cannot be
rewritten from data, so the hover/checked swap is done with event handlers
(`OceanyaPanelStyleApplier.ApplyStateArt`). Handlers are not dependency properties, so they register an
undo through `PanelStateOverrides` - the same mechanism grid paging uses. **Any new setting that is not a
dependency property must do the same, or a theme reset cannot undo it** (rule 8's second half).

### Surface size
The surface does **not** chase the panels. Auto-growing meant one drag moved every other panel on
screen, which read as the whole layout jumping. Instead:

- **While edit mode is active the window resizes freely** (`ApplyEditModeWindowResizing` turns off
  `IsResizeScalingEnabled` for the duration), so dragging an edge adds or removes empty space. The size
  the user settles on is stored in `OceanyaThemeLayoutState.SurfaceWidth/Height` and re-applied by
  `MainWindow.ApplySurfaceSizeFromLayout`. That method early-returns while edit mode is active (it
  persists the current window size instead): it runs after every panel drop, and re-applying the stored
  size there snapped the window back to stock mid-edit.
- **Resizing from the left or top edge does not move the panels.** Canvas coordinates are relative to
  the window, so `MainWindow.EditModeShellWindowMoved` shifts every panel by however far the window's
  origin moved (scaled by `ContentScale`), which turns a left-edge drag into "add/remove empty space on
  the left" instead of sliding the whole layout across the screen. A plain title-bar move changes the
  origin without changing the size and is left alone, so the layout travels with the window as expected.
- Outside edit mode a window drag rescales the whole UI as before.
- Panels are kept on the surface (`SanitizePlacement` clamps position to non-negative); a panel dragged
  past an edge is simply clipped until the user makes room.

### Panels adapt to their size
Panels are responsive, not fixed art at a fixed spot:
- `IcLogPanel` / `OocLogPanel` own their background art and stretch the log to fill it. Previously the
  log sat next to a separate, taller backdrop that drew outside the panel bounds and ignored resizing;
  `ICLog`/`OOCLog` also had hard-coded `Width`/`Height` on their roots, which are now removed.
- `ClientsListPanel` is a 3-row grid: centered title, centered +/- buttons, and the client grid
  filling the rest. `PageButtonGrid.EnableAutomaticPageSize(itemSize)` recomputes rows/columns from
  the available space on every SizeChanged, so a taller panel simply shows more clients.

### Leaving edit mode
The pencil that turns edit mode on ends up *underneath* the bottom bar's own overlay, so edit mode
provides its own exits: a floating toolbar (**Done**, **Reset all**) drawn above the overlays at
z-index 10001 and pinned to the top-right of the occupied area, plus **Escape**.

### Panel kinds and per-type editors
`OceanyaPanelDescriptor.Kind` (`OceanyaPanelKind`) says what a panel wraps, which decides both its
resize rules and the options its right-click menu offers:

| Kind | Resize | Right-click opens |
|---|---|---|
| `TextInput`, `Dropdown`, `TextToggle` | width only - height comes from the font | **Font settings...** (family, size, bold/italic/underline, colour) |
| `ImageButton` | free | **Image settings...** (replacement image, scaling, opacity, background colour) |
| `ItemGrid` | free | **Grid settings...** (item size, empty = default) |
| `Static` | free | **Image settings...** for user-added panels, otherwise nothing kind-specific |

Every dialog is built from the same two-column field grid (`CreateFieldGrid`/`AddField`) with shared
colour-picker and opacity rows, so they read consistently: a swatch plus Pick/Clear for colours (Clear
returns to the control's own colour) and a slider with a live percentage for opacity.

Each family of settings is one reusable popup in `OceanyaPanelSettingsDialogs`, not a list of literal
values in the menu: menus of "Font size 8 / 10 / 12..." do not scale and cannot express combinations
(family + size + bold), and left no way back to "default". Settings persist per panel on
`OceanyaPanelPlacementState` (`FontSize`, `FontFamily`, `IsBold`, `IsItalic`, `IsUnderlined`,
`TextColor`, `ImageScaling`, `ImagePath`, `Opacity`, `BackgroundColor`, `ItemSize`, `ZOrder`) and are applied by `OceanyaPanelStyleApplier` at startup and on every change.
Stretching a text box vertically only added dead space, which is why those panels derive their height.

### Stacking order
`OceanyaPanelPlacementState.ZOrder` feeds `Panel.SetZIndex`, and every panel menu carries **Bring to
front / Bring forward / Send backward / Send to back**. Overlays are z-ordered by the same value
(`ResolveOverlayZIndex` = 10000 + panel order) so they stack in the same order as their panels, and they
are **outline-only** (transparent fill, highlight on hover) - stacked translucent fills made overlapping
panels unreadable and hid the controls underneath. The panel name is the overlay's tooltip rather than
painted text, and the resize grip is 5px.

### Viewport in the main window
**Render viewport in main window** is a *checkable* entry on the viewport button's (and the viewport
panel's) right-click menu. While it is on, the viewport **replaces** the button: the
`bar_button_viewport` element is collapsed and `AO2ViewportWindowContent` moves out of its own window
into `ViewportPanelHost` on the main canvas (panel id `viewport`), which is then the panel being laid
out - so the button's own image settings are simply not reachable any more. Turning it off returns the
content to the viewport window and brings the button back.

Because there is no separate viewport window to represent while this is on, PiP mode is disabled and
**taskbar preview mode is forced off** (`GMViewportWindowPreviewPriority = false`). The choice persists
as `SaveData.GMViewportRenderInPanel` and is re-applied at startup. Host-specific menu entries like this
come from `OceanyaPanelEditModeController.ExtraPanelMenuItemsProvider`, which returns
`OceanyaPanelMenuEntry` records (an `IsChecked` value makes the entry checkable).

### User-added panels
**Add panel** on the toolbar, or right-click empty space in edit mode, adds an **image** panel (file
picker) or a **solid colour** panel (the character creator's colour picker).

**Gotcha:** the empty-space handler must not mark `MouseRightButtonUp` as handled for clicks that land
on a panel overlay. WPF opens a `ContextMenu` on that very event, so doing it unconditionally swallowed
every panel's own menu and showed the add-panel menu instead. `IsOverlayElement` walks the visual tree
to tell the two cases apart. Definitions live in
`OceanyaThemeLayoutState.CustomPanels` and are realised by `OceanyaCustomPanelFactory`, which also
registers a descriptor with `OceanyaPanelCatalog.RegisterCustomPanel` so the editor treats them like
any other panel. Their right-click menu adds **Delete this panel**. `OceanyaPanelCatalog.Panels`
includes them; `BuiltInPanels` is the static set (tests assert against that).

### Hiding panels
Right-click a panel to **Hide panel**: it disappears immediately, in edit mode too. Restoring happens
through the toolbar's **Hidden controls** button, which lists every hidden panel (with a count) plus
*Show all hidden controls*. Hidden state persists via `OceanyaPanelPlacementState.IsHidden` and is
applied at startup by `OceanyaPanelEditModeController.ApplyHiddenPanels`. Panels hidden for other
reasons (an advanced feature being off) are left alone by the editor.

### OOC block layers
The OOC log is split into layers so each can be moved, restyled or dropped: `ooc_log` is just the grey
background, `ooc_chat` the transparent chat text, `ooc_stream_backdrop` the translucent black header bar
(previously the label's own `Background`, now a separate rectangle), `ooc_stream_text` the header text
(`[0] Franziska ("Client1")`), plus `ooc_message`, `ooc_showname` and `ooc_server_console`. The `OOCLog`
control itself is collapsed after handing everything over; it stays in the tree because it owns the
code-behind. `ic_settings` went the same way and is now literally the Oceanya logo.

### Panel granularity
Grouped only where separation makes no sense (IC log, OOC log, clients, viewport). Everything else is
individually placeable, down to the leftovers: the bell button (`ding_button`), the OOC divider strip
(`ooc_divider`) and the IC settings decorations (`ic_catchphrase`, `ic_settings_backdrop`,
`ic_loremaster`). The bottom bar is split into its three checkboxes and seven buttons
(`bar_check_*`, `bar_button_*`), and the clients strip into `clients_title`,
`clients_list` (the paged grid keeps its navigation buttons, which cannot be separated). Each shout is
its own `ShoutButtonPanel` (`shout_holdit`, `shout_objection`,
`shout_takethat`, `shout_custom`) with its own images, plus `shout_backdrop` for the art behind them.
`ShoutSelectionGroup` keeps one-checked-at-a-time now that they are no longer siblings in one control.

**IC settings is split into 17 individual panels.** `ICMessageSettings.ExtractPlaceableControls()`
hands its showname, message box, emote grid, four checkboxes, six dropdowns and four buttons to
`MainWindow.AdoptIcSettingsPlaceableControls`, which **reparents** them onto `MainCanvas`. Reparenting
rather than rewriting means every field, handler and code path inside `ICMessageSettings` keeps
working - only the layout parent changes. What remains in `ic_settings` is the background art. The IC
sub-panel default placements are the control's inner canvas coordinates plus its own (0, 343) origin.

Known gap: `UpdateDreddFeatureVisibility` still repositions the bottom bar by the Dredd row height, so
toggling that advanced feature overrides a custom bottom bar position.

   **Extraction pattern to follow:** the panel owns visuals and raises events; the host keeps the
   logic and pushes state in through methods. No sibling reach-in, and no exposing raw child elements.
3. **Dock host** — swap the fixed layout for the dock host, reading placement from the theme file
   instead of `OceanyaPanelCatalog`'s defaults. The catalog descriptor grows `SettingsSchema` and
   `ContentFactory` at that point; today it carries id, display name, minimums and default placement. Panel IDs are stable strings
   (`viewport`, `ic_log`, `ooc_log`, `clients_list`, `emote_grid`, `ic_settings`, `music_list`,
   `area_nav`, `sound_list`, ...) because the theme file references them.
4. **Theme package + editor** — schema-driven per-panel settings, palette/fonts, images.

## Planned package format
Single shareable file `.oceanyatheme`, a zip:

```
manifest.json      // id, name, author, createdUtc, appVersion, layoutCompat
layout.json        // dock tree: splits, tabs, sizes, floating windows
panels.json        // per-panel settings values
styles.json        // global palette, fonts, borders
assets/*.png|ttf   // embedded images/fonts, referenced by relative path
```

Reuse the zip-safety validation already written for the auto-updater (`OceanyaClient/Features/Updates/*`)
so a shared theme cannot path-traverse out of its extraction directory.

## Compatibility versioning
Two separate fields:

- `appVersion` — informational ("made in 7.12"), shown in the theme picker.
- `layoutCompat` — compared against a hardcoded current value, bumped **only** when a change breaks
  themes (new mandatory panel, renamed panel ID, changed setting semantics).

A theme loads when its `layoutCompat` equals the current value. A lower value means "needs update":
open in the editor, fill in what is new, resave. Adding a release that breaks nothing keeps old
themes loading. A migration table (`"8.3" -> "8.4"` places the new panel at a default dock position)
should auto-migrate wherever possible, so a bump only forces manual editing when no automatic
placement makes sense.
