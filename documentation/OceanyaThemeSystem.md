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

## Phases
1. **UI scaling** — done, see `Documentation/UiScaling.md`.
2. **Panel extraction** — turn each `MainWindow.xaml` region into a self-contained `UserControl`
   with no sibling reach-in, *behind the current layout* (no visual change), tests green. Already
   self-contained: `ICMessageSettings`, `ICLog`, `OOCLog`, `AO2ViewportWindowContent`. The rest is
   inline XAML plus `MainWindow.xaml.cs` code-behind touching named elements directly, and that
   extraction is the bulk of the work. Highest regression risk: the single/multi internal client
   logic, viewport attachment, snapshot restore, and the send/echo gating.
3. **Dock host** — swap the fixed layout for the dock host. Panels register in a catalog
   (`{ Id, DisplayName, MinSize, SettingsSchema, ContentFactory }`), mirroring how
   `StartupFunctionalityCatalog` registers launch modes. Panel IDs are stable strings
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
