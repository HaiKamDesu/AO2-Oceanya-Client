# Window shell, UI scaling, dialogs and shared controls

Navigation detail extracted from the `AGENTS.md` Repository Navigation Map.
Each section is the full note for one map row; the map keeps the short pointer.

## waitform on top of popup

**Also called:** "waitform on top of popup", "wait form above message box", "loading popup blocks dialog", "Connecting to server and restoring clients covers the prompt"

**Status:** Confirmed

`WaitForm` runs on its OWN STA thread (`StartFormOnNewThread`), so WPF cannot own it from the main window and cannot keep a main-thread modal above it; it also calls `Activate()` on show. Fix is suspend-not-restack: `WaitForm.SuspendForDialog()` (nesting counter, `Hide()`/`Show()`) plus `WaitForm.HookThreadModalDialogs()` wired in `App.OnStartup`, which subscribes `ComponentDispatcher.EnterThreadModal`/`LeaveThreadModal` so EVERY `Window.ShowDialog()` and `MessageBox.Show()` on the UI thread hides it (there are ~226 direct dialog call sites; hooking each was not viable). `OceanyaWindowManager.ShowDialog` also suspends explicitly, which covers every `OceanyaMessageBox`. Suspend/Resume use `InvokeAsync`, never blocking `Invoke` - this runs inside a modal-entry callback and a blocking cross-UI-thread call there deadlocks. `Resume` re-checks `Showing` so a close that landed during the dialog stays closed. `SetSubtitleAsync` is deliberately NOT gated on `IsVisible` so the subtitle is current when the form returns. **"App minimizes itself" / "window disappears when a popup closes":** the wait form is a top-level window on its own thread with NO Win32 owner and it `Activate()`s on show, so when it closes/hides while holding the foreground, Windows hands activation to the next window in the GLOBAL Z-order — often another process or one of WPF's zero-sized per-thread helper HWNDs — and the Oceanya window drops behind everything, which users report as "it minimized". Fix: `WaitForm.TryRestoreOwnerForeground()` (`SetForegroundWindow` on the owner HWND captured at `ShowFormAsync` time via `ResolveOwnerHandle`) is called BEFORE `Close()` in `CloseFormAsync` and before `Hide()` in `Suspend()`; it no-ops unless the wait form is currently `GetForegroundWindow()`, so it can never steal focus from a dialog or from an app the user switched to deliberately. Cross-thread `SetForegroundWindow` is allowed there because the process owns the foreground at that moment. Also prefer `await WaitForm.CloseFormAsync()` over fire-and-forget `CloseForm()` when a dialog is opened right after, or the close races the dialog.

## server quickselect combobox

**Also called:** "server quickselect combobox", "recent servers dropdown", "server history", "most used servers", "SelectedServerComboBox"

**Status:** Confirmed

`InitialConfigurationWindow.xaml(.cs)` replaced the old read-only `SelectedServerTextBox` with `SelectedServerComboBox` (`AutomationId InitialConfig.SelectedServerCombo`), a non-editable rich ComboBox showing previously-launched servers (Name/Players/Ping columns, same bound properties as `ServerSelectionDialog`'s GridView) ordered by connect count descending. Backing data: `Common/SaveFile.cs` `SaveData.ServerConnectionHistory` (`Dictionary<string, ServerConnectionHistoryEntry>` keyed by endpoint). `InitialConfigurationWindow.RecordServerConnectionUsage(...)` increments the count exactly once per actual launch (called in `ExecuteOkButtonClickAsync` right before `SaveConfiguration(...)`, gated on `selectedFunctionality.RequiresServerEndpoint`) — not per `AOClient.Connect()`, so GM multi-client sessions with several internal clients only count once. `PopulateServerHistoryComboBox()` builds the ordered `ObservableCollection<ServerEndpointDefinition>` and fire-and-forget probes each entry via `ServerEndpointCatalog.ProbeEndpointAsync` (same live ping/player pattern as `ServerSelectionDialog.ProbeServersAsync`, but per-endpoint, no full catalog load). `ServerEndpointSource.ConnectionHistory` is the new enum case for these rows. `ApplySelectedServer(...)` / `UpdateSelectedServerDisplay()` keep the combobox selection in sync whether the pick came from the "Select..." big-selector dialog or the history combobox itself; the big-selector dialog remains the path for anything new/uncommon. Tests: `UnitTests/TestabilityHardeningTests.cs` `RecordServerConnectionUsage_IncrementsCountAndPersistsAcrossReload`, `InitialConfigurationWindow_ServerHistoryComboBox_OrdersByConnectCountDescending`.

## taskbar preview

**Also called:** "taskbar preview", "viewport Windows preview", "viewport in taskbar", "Use viewport as Windows preview", "double alt tab", "frozen viewport preview"

**Status:** Confirmed

Right-click context menu on `AO2ViewportWindowContent` (`ViewportContextMenu_Opened` dynamically builds sections); both viewport/background and chatbox right-clicks should open this same menu. Chatbox background color actions live in the shared Chatbox section and call `AO2ViewportControl.PickChatBackgroundColor()` / `SetChatBackgroundColor(null)`; color picking reuses `AOCharacterFileCreatorWindow.ShowSolidColorPickerDialog(...)`. The Background section opens `design.ini`; the Chatbox section opens the active `Globals.PathToConfigINI`. `UseAsWindowsPreview` bool property fires `UseAsWindowsPreviewChanged`; saved to `SaveFile.Data.GMViewportWindowPreviewPriority`; `MainWindow.ApplyViewportTaskbarPriority()` uses native viewport shell preview: viewport becomes the visible taskbar/Alt-Tab HWND, main GM window is hidden from shell/taskbar and kept `WS_EX_NOACTIVATE` while preview mode is active. On viewport shell return, `RestoreMainWindowVisualForViewportReturn()` shows/restacks the main GM HWND with `ShowWindow(SW_SHOWNOACTIVATE)` / `SetWindowPos(... SWP_NOACTIVATE ...)` without focusing it, then `viewportPreviewInputProxyActive` routes viewport `PreviewKeyDown`/`TextInput` into logical IC/OOC textboxes. Main-window `WM_MOUSEACTIVATE` in preview mode forces the viewport shell foreground/restack path while keeping the main HWND no-activate, so clicking/dragging the main window brings the viewport/main pair above external windows. `EnsureViewportIsForegroundShellRepresentative(...)` returns foreground/active shell identity to the viewport when Oceanya is already foreground, including after main input clicks/focus. `ProxyKeyboardFocusVisual.IsProxyKeyboardFocusTarget` gives the logical IC/OOC proxy target a focused border and blinking caret adorner while real WPF keyboard focus remains on the viewport. Main-window mouse target tracking uses a safe visual/logical ancestor walk because log clicks can originate from `FlowDocument`, not a `Visual`. Do not re-enable `ViewportThumbnailCompositor` for the active path; it only deactivates stale DWM iconic thumbnail state. Old low-level keyboard hook/reinject Alt-Tab attempts are disabled. Attempt log and next ideas live in `documentation/ViewportWindowsPreviewAltTab.md`.

**"Client shows up as two windows sometimes" / "have to toggle the setting off and on to fix it":** `ApplyViewportTaskbarPriority()` only runs on discrete user actions (7 call sites), but WPF re-asserts its own extended window styles whenever the HWND state changes underneath it - minimize/restore, a monitor or DPI move, and the `Window.ShowInTaskbar` setter's internal hide/show. That wipes the manual `WS_EX_TOOLWINDOW` bit `SetWindowShellVisibility` put on the main GM window, so the main window pops back into the taskbar beside the viewport. `MainWindow.ReassertViewportPreviewShellVisibility()` re-applies ONLY the taskbar/alt-tab ex-styles for the current mode and is called from both `MainWindowHost_WndProc` and `ViewportWindow_WndProc` on `WM_STYLECHANGED` (0x007D), `WM_SHOWWINDOW` (0x0018) and `WM_DPICHANGED` (0x02E0). It is style-only and idempotent (no owner changes, no timers, no focus work), and `SetWindowShellVisibility` no-ops when the styles already match, so it cannot recurse through `WM_STYLECHANGED`; a `isReassertingViewportPreviewShellVisibility` flag guards reentrancy anyway. The main-window call sits BEFORE that hook's `IsViewportUsingWindowsPreview()` early return so both directions self-heal.

## client too small on 4K

**Also called:** "client too small on 4K", "UI scale", "unusable on high resolution", "scaling setting for windows", "everything tiny", "make the client bigger"

**Status:** Confirmed

Doc: `Documentation/UiScaling.md`. Math: `Common/UiScaleMath.cs` (`UiScaleMode`, `ResolveAutomaticScale` = min(dipW/1920, dipH/1080) floored to 0.25, clamped [1.0,3.0], never <1.0; `RescaleSavedLength`). Runtime: `OceanyaClient/Features/Ui/UiScaleManager.cs` (per-window monitor query, `ScaleChanged`, `SetLivePreview`/`ClearLivePreview`, `ApplySettings`). Shell: `GenericOceanyaWindow.ContentScale` drives a `ScaleTransform` **LayoutTransform** on `ShellRootGrid`, plus scaled `WindowChrome.CaptionHeight`/`ResizeBorderThickness` and `HeaderOffsetBackdropRectangle.Margin`; static `GenericOceanyaWindow.GetChromeOffsets(bodyMargin, scale)` scales header+body margin but NOT `SharedFrameBorderThickness` (border sits outside the transform). Hosted content keeps laying out in UNSCALED DIPs — `OceanyaWindowManager.HostedSizingSyncController` converts (`window = content*scale + offsets`, and back), and re-syncs on `ContentScaleChanged`. Settings: `SettingsWindow` `Interface` page (`SettingsWindowPage.Interface`) with Automatic/Manual + 75-300% slider and live preview; preview cleared on Save/Cancel and in `MainWindow`'s settings `Closed` handler. Persistence: `SaveData.UiScaleMode`/`UiScaleFactor`; window-space saved sizes stamp `VisualizerWindowState.UiScale` and rescale on restore (`App.Window_LoadedForPersistence`, `AOCharacterFileCreatorWindow.ApplyPopupState`/`CapturePopupWindowState`, `MainWindow.CaptureMainWindowState`); `SaveFile` clamps multiply floors by the stored scale. **GOTCHA:** viewport + PiP windows opt OUT (`OceanyaWindowPresentationOptions.IsContentScaleEnabled = false`) because `MainWindow.GetViewportWindow*Offset` aspect math already derives their size from AO2 surface geometry; `WaitForm`/`LoadingScreen` are standalone Windows and unscaled. **GOTCHA 2:** `WM_GETMINMAXINFO` must raise `ptMaxTrackSize` (Windows defaults it to ~one monitor, which silently clipped scaled windows taller than the screen — symptom was "width updates, height does not"); `GenericOceanyaWindow.WmGetMinMaxInfo` raises it to the virtual screen size when no explicit Max size is set. **Settings UX:** slider commits on mouse-up/`LostMouseCapture`/`KeyUp` only, never on drag (rescaling mid-drag moves the slider under the pointer and the value jumps); the percent readout is an editable TextBox (`CommitUiScaleFromTextBox`, Enter/lost-focus). Diagnostic `[UISCALE]` (System category) logs scale, content size, requested vs ACTUAL window size, min/max — `actual != requested` means the OS refused the size. **Lockout guard:** `GenericOceanyaWindow.ClampScaleToMonitor` + `UiScaleMath.ResolveMaximumFittingScale` reduce the resolved scale so a window never grows past the monitor work area (a too-big manual scale used to put Settings out of reach). **`LocationChanged` MUST NOT re-resolve the scale during a resize gesture** — dragging the LEFT/TOP edge moves the origin, so it fired mid-drag, threw away the scale the drag computed and restored the old one from settings, leaving the window at the dragged size with content at the previous scale (empty band); skipped while `IsInteractiveResizeScaling`. **Drag-to-rescale:** `OceanyaWindowContentControl.IsResizeScalingEnabled` (virtual, default false; MainWindow overrides true along with `IsUserResizeEnabled => true`) makes `WM_SIZING` rescale like the viewport — `ApplyResizeScalingSizingRect` converts the rect to a scale (`UiScaleMath.ResolveScaleFromWindowLength`), aspect-locks the rect, applies `ContentScale` live, and `WM_EXITSIZEMOVE` publishes it as the GLOBAL manual scale; `HostedSizingSyncController` skips `ApplyWindowSizeToContent` for such windows entirely and `ApplyContentSizeToWindow` while `IsInteractiveResizeScaling`. **Standalone windows** `WaitForm` (`WaitFormScaleTransform` + `ResizeWindow`/`ResolveContentScale`, own STA thread) and `LoadingScreen` (`LoadingScreenScaleTransform` + `ApplyUiScale`, fixed 318x127 design size) scale themselves with a root-grid LayoutTransform + scaled window size, since they are outside the GenericOceanyaWindow shell. **SYNC DIRECTION RULE (ratchet bug):** `HostedSizingSyncController.ApplyWindowSizeToContent` (window->content) runs ONLY for user-driven resizes — never from `OnWindowContentScaleChanged`/`OnContentConstraintsChanged`, because right after a resize request WPF has not laid out and `ActualWidth/Height` still describe the OLD scale; dividing that by the NEW scale inflated content every scale change (symptom: "close main window, initial config comes back super tall"). `isDrivingWindowSize` (set in `BeginDrivingWindowSize`, cleared at `DispatcherPriority.Loaded`) also makes `OnWindowSizeChanged` ignore the echo of our own write so an OS-clamped size cannot rewrite content size. Clamp re-resolved on `OnWindowLoaded` and on content size change; restored popup sizes clamped to work area in `App.Window_LoadedForPersistence`. **Re-apply points:** the controller's ctor sizing pass runs with no HWND (per-monitor scale falls back to primary), `ActualWidth/Height`=0, and auto-sized content unable to constrain the clamp, so `HostedSizingSyncController.ReapplySizing` re-runs refresh+constraints+content->window on `Loaded`, `ContentRendered`, AND `IsVisibleChanged`->visible (skipped during an interactive rescale drag). The IsVisible one matters because `StartupWindowLauncher` HIDES the initial config window while a functionality runs and `OceanyaWindowContentControl.Show()` re-shows the SAME host window — no second Loaded/ContentRendered (symptom: "close main window, initial config comes back broken, only on that path"). **Move != resize:** `WM_ENTERSIZEMOVE`/`EXITSIZEMOVE` bracket plain MOVES too, so drag-rescale publishes the global manual scale only when `ContentScale` changed during the gesture (`contentScaleAtGestureStart`), else dragging a window by its header silently switched the app to Manual. **ONE SIZE OWNER RULE:** window sizes persist through `OceanyaClient/Features/Ui/WindowStatePersistence.cs` in CONTENT space (`VisualizerWindowState.IsContentSpace`; legacy window-space states converted via `ResolveContentSize`), and `ShouldPersistSize` excludes resize-scaling windows plus any content overriding `OceanyaWindowContentControl.ManagesOwnWindowSize` (MainWindow, AOCharacterFileCreatorWindow, CharacterFolderVisualizerWindow, CharacterEmoteVisualizerWindow, TagFilterSelectionWindow, AO2ViewportWindowContent). Before this, `App.Window_LoadedForPersistence` wrote raw window.Width/Height for EVERY resizable shell while the control also saved its own content-space size — at scale != 1 the window-space number landed as a wrong content size (symptom: "creator/Hivemind inner control not scaled until I resize it"). New window that saves its own size => override `ManagesOwnWindowSize`. Tests: `UnitTests/UiScaleTests.cs`, `UnitTests/WindowStatePersistenceTests.cs`, and the Explicit real-window harness `UnitTests/UiScaleHostingTests.cs` (shows windows; interactive desktop only).

## DPI

**Also called:** "DPI", "second monitor too big", "mixed DPI monitor", "per monitor DPI"

**Status:** Confirmed

`OceanyaClient/app.manifest` opts into `dpiAwareness` `PerMonitorV2, PerMonitor`; main GM UI is fixed-DIP WPF layout in `MainWindow.xaml`, so monitor moves should remeasure/render at the target monitor DPI instead of retaining primary-monitor bitmap scale.

## standard character context menu

**Also called:** "standard character context menu", "character right-click menu", "Character section in viewport", "character combo right-click", "delete character folder crash"

**Status:** Confirmed

`OceanyaClient.Utilities.CharacterContextMenuBuilder` owns the shared character menu categories/actions and formatted submenu headers via `BuildSubmenu(...)`. Reused by `CharacterFolderVisualizerWindow.BuildContextMenuForItem`, `CharacterEmoteVisualizerWindow.BuildContextMenuForItem`, `ICMessageSettings.BuildCharacterDropdownContextMenu`, and `AO2ViewportWindowContent.AddCharacterMenuSection`; viewport exposes it as a bold `Character (<name>)` submenu. Default shared actions include verifier run/results, editor actions, folder open/copy, and delete; main-window delete routes through `MainWindow.DeleteCharacterFolderFromContextAsync`, first `ReleaseCharacterFolderBeforeDelete(...)` to clear active client/viewport/UI image references, then targeted character refresh instead of full asset refresh. UI-loaded character button images must use `BitmapFileLoader.LoadFrozen(...)` so Windows file handles do not block deletion.

## message box

**Also called:** "message box", "custom popup", "OceanyaMessageBox", "popup too small", "large warning text"

**Status:** Confirmed

`OceanyaClient/Components/Forms/OceanyaMessageBox.xaml(.cs)`; hosted by `GenericOceanyaWindow` through `OceanyaWindowManager`. Size is computed in `OceanyaMessageBox.CalculateContentSizeForMessage(...)` from wrapped text + visible buttons, clamped to a screen-aware max; `ScrollViewer` is fallback only after max size. Tests in `UnitTests/TestabilityHardeningTests.cs`.

## ctrl drag

**Also called:** "ctrl drag", "synchronized window move", "move both windows together"

**Status:** Confirmed

`GenericOceanyaWindow.SynchronizedMovePartner` property + `WM_MOVING` handler in `GenericOceanyaWindow.WndProc` (`HandleWindowMovingSynchronize`); wired from `MainWindow.SetupViewportSynchronizedMove` / `TeardownViewportSynchronizedMove`. Hold Ctrl while dragging either window's title bar to move both together.

## character database viewer lags

**Also called:** "character database viewer lags", "viewer grid virtualization", "character folder visualizer performance", "GeneratorPositionFromIndex crash"

**Status:** Confirmed

`CharacterFolderVisualizerWindow` icon/normal mode uses `OceanyaClient.Utilities.VirtualizingWrapPanel` through `IconItemsPanelTemplate`; table mode uses `VirtualizingStackPanel`. `VirtualizingWrapPanel.MeasureOverride` must tolerate a null/stale `ItemContainerGenerator` during detail-to-grid view swaps and skip that stale layout pass instead of crashing. Progressive image residency stays in `UpdateViewportImageResidency` / `StartProgressiveImageLoading`.

## character emote visualizer lags

**Also called:** "character emote visualizer lags", "emote viewer virtualization", "right-click emote"

**Status:** Confirmed

`CharacterEmoteVisualizerWindow` icon grid uses `OceanyaClient.Utilities.VirtualizingWrapPanel`; visible-item image residency is managed by `RefreshViewportAnimationResidency` / `StartViewportPreviewLoading`. Emote right-click builds an `Emote` section for `Set as folder display`, then appends the shared `CharacterContextMenuBuilder` menu for character-level actions.

## find in log

**Also called:** "find in log", "search IC/OOC log", "ctrl+f log", "find performance", "Search IC/Search OOC"

**Status:** Confirmed

Right-click on IC or OOC log → "Find in log..." → opens `FindInLogWindow` (modeless, `OceanyaWindowContentControl`). `ICLog` and `OOCLog` implement `ILogFindTarget`; `MainWindow.GetCurrentLogFindTargets()` supplies both current documents. `FindInLogWindow` has Search IC/Search OOC scope checkboxes, debounces and cancels in-flight search/highlight work on text/options changes. `LogDocumentSearch` snapshots the visible `FlowDocument` on the dispatcher, then `FindOffsets(...)` matches plain text off-thread before resolving offsets back to `TextPointer`s. `LogTextMatcher` owns the shared plain/regex/case/whole-word semantics and is reused by all-log file search. IC/OOC `HighlightMatchesAsync(...)` batches highlight application with dispatcher yields; navigation updates only old/current active highlights. Tests: `UnitTests/LogDocumentSearchTests.cs`, `UnitTests/AllLogSearchServiceTests.cs`; manual 10k-line benchmark is `[Explicit]` test `Find_TenThousandLineManualBenchmark`.

## dropdown search finds nothing

**Also called:** "typed saber but it didn't find (pb)saber pendragon", "combobox search only matches the start", "searchable dropdown misses characters", "contains search"

**Status:** Confirmed

`OceanyaClient/Utilities/DropdownSearchMatcher.cs` owns the shared semantics: **prefix matches first, then substring matches**, ordinal case-insensitive.

Prefix-only filtering made half a roster unreachable by its own name - AO character folders are full of bracketed source tags, so "saber" found nothing for `(pb)saber pendragon`. Pure substring would be worse in the other direction (typing "phoenix" would rank `Hobo_Phoenix` next to `Phoenix`), hence the ranking.

Cost: one pass, one ordinal `IndexOf` per candidate, no sorting, no per-candidate allocation, and **the scan stops as soon as prefix matches alone fill the result** - so a 10,000-entry roster only inspects as far as it needs to (pinned by `Filter_StopsScanningOncePrefixMatchesFillTheResult`).

Users: `ImageComboBox.FilterDropdown` and `ImageComboBox.FindItemByPrefixText` (the IC character/background dropdowns - this was the reported bug), and `AutoCompleteDropdownField`'s commit resolution (its suggestion list already matched on substring, so committing had to resolve the same way or pressing Enter on a visible substring match kept the raw text).

Deliberately NOT changed: `CharacterFolderVisualizerWindow`'s tag box (~line 2425) stays prefix-only because it is an INLINE autocomplete that rewrites the textbox and selects the completion - substring matching there would replace what the user typed with an unrelated string. `CharacterSelectorWindow.SearchBox_TextChanged` and `AutoCompleteComboBoxBehavior` already matched on substring.

Tests: `UnitTests/DropdownSearchMatcherTests.cs`.

## color picker window is broken

Reported as "the color picker controls don't adapt to resize correctly, I had to make the window way
bigger to see all the controls". The Solid Color Picker opened with its Save/Cancel row *below the bottom
edge*, unreachable.

Two independent defects, both in how a shell dialog is sized. Neither is specific to the colour picker.

### 1. A size restored onto a body nothing syncs

`OceanyaWindowManager` attaches a `HostedSizingSyncController` that keeps a shell's window size and its
`OceanyaWindowContentControl` body's size in step. **Only that path attaches one.** Dialogs built by
`AOCharacterFileCreatorWindow.CreateEmoteDialog` (and `AssetImageViewerDialog`, the IC emote preview, the
panel settings dialogs) construct a `GenericOceanyaWindow` directly with a plain `Border` body, so they
have no controller.

`App.Window_LoadedForPersistence` still restores their saved size for them, through
`WindowStatePersistence.ApplySize`, which writes it onto `BodyContent`. With no controller that is a
one-way write: the body renders at 972x665 inside a window that stays 760x680, the overflow is clipped,
and resizing the window changes nothing because nothing propagates either way. Measured live: saved state
`Solid Color Picker -> {Width: 972.19, Height: 665.18, IsContentSpace: true}` matched the body's explicit
`Width`/`Height` in the visual tree exactly.

Fix: `GenericOceanyaWindow.HasHostedContentSizing` (set by the controller's constructor) tells
`WindowStatePersistence` which of the two it may size. Without a controller the saved size goes on the
window, via `ContentSizeRequest`.

### 2. Content-space numbers used as window sizes

`Width`/`Height` size the WINDOW, around a body the shell scales by `ContentScale`. A dialog asking for
760x680 was therefore getting `760/scale` of content - at the reporter's 1.16 UI scale about 100px short
vertically, which is what pushed the buttons off the bottom. The caller cannot pre-compute this: the scale
is resolved later, from a monitor the caller does not know.

Fix: `GenericOceanyaWindow.ContentSizeRequest` / `MinimumContentSizeRequest` state the size in CONTENT
units, and the shell converts them (`content * scale + GetChromeOffsets(...)`, clamped to the work area)
every time its scale changes. **New shell dialogs should use these, not `Width`/`Height`.**

Once it fits, `BuildStyledDialogContent`'s `Viewbox` (Uniform, `StretchDirection.DownOnly`) does the
adapting it was always meant to: 1:1 at or above natural size, shrink-to-fit below it, with the button row
outside the Viewbox so it never scales away. Verified at 1130x808, 884x826, 640x1100 and 620x430 - all
controls reachable at every size.

Tests: `UnitTests/DialogSizePersistenceTests.cs`, `UnitTests/WindowStatePersistenceTests.cs` (whose
`CreateHostedPair` now sets `HasHostedContentSizing`, since it stands in for a manager-hosted window).

## editor opens with wrongly sized controls, and closing it drops the app behind

Two separate defects reported together on the character editor.

### Controls sized for the restore bounds inside a maximized window

`AOCharacterFileCreatorWindow.ManagesOwnWindowSize` is true, so it applies its own saved state
(`SaveData.CharacterCreatorWindowState`) - including `WindowState = Maximized` plus an explicit
`Width`/`Height` from its *restore* bounds.

`HostedSizingSyncController.ReapplySizing` (Loaded / ContentRendered / re-show) only ever pushed
content -> window, and `ApplyContentSizeToWindow` returns early while maximized. So nothing sized the
content to the window it was actually in: measured 1420x1040 of content inside a 1920x1040 maximized
window, with dead bands down both sides, until any resize finally ran the window -> content sync.

Fix: `ReapplySizing` picks a direction. Normal state, the content drives the window; otherwise the
window's size is the monitor's and the content must follow it (`ApplyWindowSizeToContent`).

### Closing it programmatically drops the whole app behind

Windows hands activation back to the owner when the USER closes a window. It does not when code does:
a programmatic `Close()` of an owned window can leave nothing activated, so the app falls behind
whatever is underneath - reported as "the Oceanya windows all minimize instead of going back to the
window that was below". It only surfaced once the editor started closing itself after a successful
commit; a manual close was always fine.

Measured on the real path (owner shell -> modal editor -> modal success box -> close):

| | owner `IsActive` | owner is foreground |
|---|---|---|
| close directly | false | false |
| close deferred to `DispatcherPriority.Background` | false | false |
| close + `owner.Activate()` | true | true |

**Deferring does not help. Re-activating the owner does.** `OceanyaWindowManager`'s `OnCloseRequested`
now re-activates the owner after a `RequestHostClose`, guarded on the closing window having been active
(so a background modeless close never steals focus). This covers every hosted content that closes
itself, not just the editor.
