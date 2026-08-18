# Global UI Scaling

## Purpose
The WPF layouts are fixed-DIP, so on very high resolution monitors (4K at 100% Windows scaling)
every Oceanya window renders physically tiny and users report the client as unusable. Global UI
scaling multiplies the whole shell — header and hosted body content — by one factor, either derived
from the monitor or chosen by the user.

This is Phase 1 of the larger Oceanya theme system work; see `Documentation/OceanyaThemeSystem.md`.

## Where it lives
| Piece | File |
|---|---|
| Pure scale math (no WPF) | `Common/UiScaleMath.cs` (`UiScaleMode`, `ClampScale`, `ResolveAutomaticScale`, `RescaleSavedLength`) |
| Runtime owner + monitor query | `OceanyaClient/Features/Ui/UiScaleManager.cs` |
| Shell scaling | `GenericOceanyaWindow.ContentScale`, `ShellRootGrid` `LayoutTransform`, `GetChromeOffsets(bodyMargin, scale)` |
| Window/content size sync | `OceanyaWindowManager.HostedSizingSyncController` |
| Settings UI | `SettingsWindow.xaml(.cs)` `Interface` page (`SettingsWindowPage.Interface`) |
| Persistence | `SaveData.UiScaleMode`, `SaveData.UiScaleFactor`, `VisualizerWindowState.UiScale` |
| Tests | `UnitTests/UiScaleTests.cs` |

## How it works
Every Oceanya window is an `OceanyaWindowContentControl` hosted in `GenericOceanyaWindow`, so one
hook covers the whole app. The shell's inner grid (header row + body `ContentPresenter`) carries a
`ScaleTransform` as its **LayoutTransform**, so:

- hosted content still lays out in **unscaled logical DIPs** — no existing layout code changes;
- the host window size is `content * scale + chromeOffsets(scale)`.

`HostedSizingSyncController` owns that conversion in both directions and now multiplies/divides by
`GenericOceanyaWindow.ContentScale`. `GetChromeOffsets` scales the header height and body margin but
**not** `SharedFrameBorderThickness`, because the 1px frame border is drawn outside the transform.
`WindowChrome.CaptionHeight` and `ResizeBorderThickness` are scaled too, so the drag area and resize
grips still line up with what is drawn.

Scale is re-resolved on `OnSourceInitialized`, `OnDpiChanged`, `LocationChanged` (moving to another
monitor in automatic mode), and whenever `UiScaleManager.ScaleChanged` fires. A change raises
`GenericOceanyaWindow.ContentScaleChanged`, which makes the sizing controller resize the host window
while the content keeps its logical size.

## Automatic mode
`UiScaleMath.ResolveAutomaticScale(logicalWorkAreaWidth, logicalWorkAreaHeight)`:

- reference size is 1920x1080 (what the current layouts were designed against);
- factor is `min(width/1920, height/1080)`, floored to a 0.25 step, clamped to `[1.0, 3.0]`;
- **never below 1.0** — automatic scaling exists to fix huge displays, not to squeeze small ones.

Monitor size is read per window (`MonitorFromWindow` + `GetMonitorInfo`) and divided by the window's
DPI scale, so it is compared in DIPs. The app manifest already opts into `PerMonitorV2`, so a
4K monitor at 150% Windows scaling reports 2560x1440 DIPs and resolves to 1.25, not 2.0 — correct,
because Windows already did part of the scaling.

## Live preview
`UiScaleManager.SetLivePreview(mode, scale)` / `ClearLivePreview()` mirror the audio-slider pattern:
`Mode`/`ManualScale` prefer the preview over the savefile. The preview is cleared on Save
(`ApplySettings`), Cancel, and the settings window `Closed` handler in `MainWindow` (the title-bar X
runs neither button).

## Saved window sizes: one owner, content space
Two independent systems used to write the same window's size in different units:

- each content control saving its own state (`Width`/`Height` on the control = **content space**), and
- `App.Window_LoadedForPersistence` / `Window_ClosingForPersistence`, which persisted
  `PopupWindowStates` for **every** resizable shell window as raw `window.Width`/`Height`
  (**window space**).

At any scale != 1 those disagree, so restoring the window-space number as a content size rendered the
inner control at the wrong size until the user resized the window by hand (reported for the character
file creator and the File Hivemind window). Both problems are fixed structurally:

- `OceanyaClient/Features/Ui/WindowStatePersistence.cs` is the single implementation.
  `CaptureSize` stores the **hosted content size** (`VisualizerWindowState.IsContentSpace = true`),
  which means the same thing at every scale; `ApplySize` converts legacy window-space states
  (`ResolveContentSize`, using `UiScale` or 1.0) and clamps the result so the scaled window still fits
  the monitor. `App` and the character creator's popup helpers both route through it.
- `WindowStatePersistence.ShouldPersistSize` gives every window exactly **one** size owner. It returns
  false for resize-scaling windows (their size encodes the globally persisted scale) and for content
  that overrides `OceanyaWindowContentControl.ManagesOwnWindowSize`.

### Size owner per window
| Window | Size owner |
|---|---|
| `MainWindow` | itself (`GMMainWindowState`) + resize-scaling; excluded from `PopupWindowStates` |
| `AOCharacterFileCreatorWindow` | itself (`CharacterCreatorWindowState`) |
| `CharacterFolderVisualizerWindow` | itself (`FolderVisualizerWindowState`) |
| `CharacterEmoteVisualizerWindow` | itself (`EmoteVisualizerWindowState`) |
| `TagFilterSelectionWindow` | itself |
| `AO2ViewportWindowContent` (+ PiP) | `MainWindow` (`GMViewportWindowState`); also `IsContentScaleEnabled = false` |
| every other resizable shell (Settings, Debug Console, Find in all Logs, Google Drive Sync, character selector, server selection, text viewer, Dredd database, Hivemind, ...) | `App` `PopupWindowStates`, now content space |
| non-resizable dialogs | not persisted |
| `WaitForm` | scales itself, size derived from its message |
| `LoadingScreen` | scales itself (`LoadingScreenScaleTransform` + `ApplyUiScale`), fixed 318x127 design size |

When adding a window that saves its own size, override `ManagesOwnWindowSize => true` or the generic
persistence will fight it.

## Legacy saved window sizes
Sizes persisted in **window space** are scale dependent, so `VisualizerWindowState.UiScale` records
the scale at capture time and restore rescales via `UiScaleMath.RescaleSavedLength` (a `0` value
means a legacy save and is treated as 1.0). Applies to `PopupWindowStates`
(`App.Window_LoadedForPersistence` / `Window_ClosingForPersistence`, `AOCharacterFileCreatorWindow`
popup state) and the main window capture clamp. `SaveFile` size clamps are multiplied by the stored
scale so a 2.0-scale window is not clamped back down to unscaled minimums.

Sizes owned by an `OceanyaWindowContentControl` (folder visualizer, emote visualizer, character
creator) are already in unscaled content space and need no conversion.

## Fit-to-monitor clamp (lockout guard)
A manual scale big enough to grow a window past the screen edges takes that window's own settings
entry point out of reach. `GenericOceanyaWindow.ClampScaleToMonitor` therefore reduces the resolved
scale to the largest one that still fits the monitor work area, using
`UiScaleMath.ResolveMaximumFittingScale(content, scaledChrome, fixedChrome, available)` per axis
(window length is `content * scale + scaledChrome * scale + fixedChrome`, so the inverse is
`(available - fixedChrome) / (content + scaledChrome)`). Chrome is split by
`GenericOceanyaWindow.GetChromeParts()`. `WaitForm.ResolveContentScale` applies the same clamp.

## Drag to rescale (opt-in per window)
`OceanyaWindowContentControl.IsResizeScalingEnabled` (virtual, default `false`; also on
`OceanyaWindowPresentationOptions`) makes a window resize like the AO2 viewport: dragging an edge
rescales the content instead of stretching the layout. **`MainWindow` opts in** — its layout is
fixed-DIP, so this is the natural resize behavior and doubles as the recovery path from an
oversized scale.

Mechanics in `GenericOceanyaWindow`:
- `WM_SIZING` -> `ApplyResizeScalingSizingRect`: converts the proposed rect into a scale
  (`UiScaleMath.ResolveScaleFromWindowLength`), rewrites the rect to the exact aspect-locked window
  size for that scale (side handles drive their own axis, corners follow the axis pulled further),
  and applies `ContentScale` live.
- `WM_ENTERSIZEMOVE`/`WM_EXITSIZEMOVE` set `IsInteractiveResizeScaling`; the sizing controller skips
  `ApplyContentSizeToWindow` while it is set, or it would snap the window away from the pointer.
- On drag end the scale is published **only if it actually changed during the gesture**
  (`contentScaleAtGestureStart`): `WM_ENTERSIZEMOVE`/`WM_EXITSIZEMOVE` also bracket a plain window
  *move*, so publishing unconditionally turned "I dragged the window by its header" into "switch the
  whole app to manual scale".
- `PublishResizeScaleToSettings` writes the reached scale as the **global manual scale**
  (`UiScaleManager.ApplySettings(Manual, scale)`), so every other window follows and it persists.
- `ApplyWindowSizeToContent` is skipped entirely for resize-scaling windows: their window size means
  "scale", not "content size".

## Standalone windows (WaitForm, LoadingScreen)
`WaitForm` and `LoadingScreen` are plain `Window`s outside the shell (the wait form even lives on its
own STA thread), so each scales itself with a `LayoutTransform` on its root grid plus a scaled window
size, both fit-clamped:

- `WaitForm.ResizeWindow` measures its text unscaled, then multiplies the final size and the minimums
  by `ResolveContentScale`.
- `LoadingScreen.ApplyUiScale` scales its fixed 318x127 design size in the constructor. Its progress
  clip math keeps working untouched because it measures inside the scaled visual tree, in unscaled
  units.

## Settings interaction rules
The slider does **not** apply while dragging. Rescaling mid-drag resizes the settings window itself,
which moves the slider out from under the pointer and makes the value jump again. `ValueChanged` only
updates the readout; the scale is committed on `PreviewMouseLeftButtonUp`, `LostMouseCapture`,
`KeyUp`, or a mode-radio change (`CommitUiScaleSelection`, which no-ops when the percentage did not
actually change). The percentage next to the slider is an editable `TextBox` committed on Enter or
lost focus (`CommitUiScaleFromTextBox`, clamped through `UiScaleMath.ClampScale`).

## Sync direction rules (the "window comes back super tall" ratchet)
`ApplyWindowSizeToContent` (window -> content) may run **only** for user-driven window size changes.
It is deliberately NOT called from `OnWindowContentScaleChanged` or `OnContentConstraintsChanged`:
right after requesting a resize, WPF has not laid out yet, so `ActualWidth`/`ActualHeight` still
describe the *previous* scale. Dividing that stale size by the *new* scale rewrote the content size
every time the scale changed, and the inflated content then produced an even bigger window on the
next pass — the reported "open main window, close it, initial config comes back super tall".

For the same reason `isDrivingWindowSize` (set by `BeginDrivingWindowSize`, cleared at
`DispatcherPriority.Loaded`) makes `OnWindowSizeChanged` ignore the SizeChanged echo of our own
content -> window write; otherwise an OS-clamped size (work area, max track size) would be written
back into the content size and ratchet the layout down instead of up.

The clamp is re-resolved when it can actually be computed: `OnWindowLoaded` (before layout,
auto-sized content reports 0 and cannot constrain anything) and on content size changes
(`OnContentSizeChanged` calls `RefreshContentScaleFromSettings` first). Restored popup sizes are
additionally clamped to the monitor work area in `App.Window_LoadedForPersistence`, so an oversized
size already sitting in a savefile does not come back off-screen.

## Re-apply points
The controller's constructor sizing pass runs before the window exists on screen: no HWND (so the
per-monitor scale falls back to the primary monitor), `ActualWidth`/`ActualHeight` are 0, and
auto-sized content cannot constrain the fit clamp. `HostedSizingSyncController.ReapplySizing`
(refresh scale, constraints, content -> window - exactly what a scale change does) therefore runs on:

- `Loaded` and `ContentRendered` - first real layout. Symptom without it: "the main window opens
  broken and fixes itself as soon as I change the scale".
- `IsVisibleChanged` -> visible - **`StartupWindowLauncher` hides the initial configuration window
  while a functionality runs and `OceanyaWindowContentControl.Show()` re-shows the SAME host window**,
  so `Loaded`/`ContentRendered` never fire again and a scale that changed while it was hidden was
  never applied. Symptom without it: "close the main window, the initial config form comes back
  broken, but only on that path".

The re-apply is skipped during an interactive rescale drag.

## Gotchas
- **Viewport windows opt out.** The AO2 viewport and PiP windows pass
  `OceanyaWindowPresentationOptions.IsContentScaleEnabled = false`, because their size is derived
  from AO2 theme surface geometry (`MainWindow.GetViewportWindow*Offset`, aspect-ratio math), and
  scaling would multiply on top of that. Revisit when the viewport becomes a dockable panel.
- Do not add a scale factor to content-space `Width`/`Height` writes. The transform already handles
  it; double-scaling shows up as a window that grows every time the scale changes.
- `LoadingScreen` is a standalone `Window` outside this shell and is not scaled yet.
- **`WM_GETMINMAXINFO` must raise `ptMaxTrackSize`.** Windows defaults it to roughly one monitor, so a
  scaled window taller than the screen was silently clipped — the reported symptom was "width updates
  but height does not" (a 510x676 main window at 2.0 wants 1352 tall, which a 1080p desktop refused
  while the 1020 width passed). `GenericOceanyaWindow.WmGetMinMaxInfo` now raises the track size to
  the virtual screen size when the window has no explicit `MaxWidth`/`MaxHeight`.
- Diagnostic: `[UISCALE]` (category System, Debug Console) logs `scale`, content size, requested vs
  **actual** window size, and window min/max on every scale change. `actual != requested` means
  something outside our code refused the size — check that first on any scaling report.
