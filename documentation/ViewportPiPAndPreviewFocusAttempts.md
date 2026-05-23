# Viewport PiP And Preview Focus Attempts

## Scope
This document tracks two active MainWindow/viewport regressions reported during the Picture-in-Picture viewport and "Use viewport as Windows preview" work.

Code areas:
- `OceanyaClient/MainWindow.xaml.cs`
- `OceanyaClient/Features/Viewport/AO2ViewportWindowContent.xaml.cs`
- `OceanyaClient/Features/Viewport/AO2ViewportControl.xaml.cs`
- persistence fields in `Common/SaveFile.cs`

Related older context:
- `Documentation/ViewportWindowsPreviewAltTab.md`

## Active Problems

### A. PiP Viewport Does Not Reappear After Taskbar Minimize Cycle
Expected behavior:
- User enables `Picture in Picture Viewport` from the normal viewport context menu.
- PiP opens and stays topmost.
- When Oceanya is focused, PiP hides because the normal viewport is visible.
- When Oceanya is minimized, Alt-Tabbed away from, or otherwise loses focus, PiP should reappear at its previous position and size.
- Closing the PiP window should disable the PiP toggle.

Observed behavior as of 2026-05-23:
- Toggle on creates PiP correctly.
- First taskbar minimize: PiP remains visible. Good.
- Taskbar restore: PiP hides. Good.
- Second taskbar minimize: PiP remains gone. Bad.
- The regular viewport context menu still shows `Picture in Picture Viewport` checked, so the setting remains enabled but the PiP window is not visible.

### B. Fake-Focused IC Field Does Not Receive Typing After Clicking Main Window
Expected behavior:
- With `Use viewport as Windows preview` enabled, the viewport remains the foreground shell representative.
- The IC/OOC textbox in the main window can be logically/fake focused through the proxy visual.
- If another application is on top, clicking anywhere in Oceanya should restore the input proxy so typing goes to the fake-focused IC/OOC field.

Observed behavior as of 2026-05-23:
- IC field visually remains fake-focused.
- Clicking a random Oceanya area, such as the IC log, brings Oceanya to the front.
- The field still looks focused, but typing does not enter the field.
- User must click the IC field again before typing works.

## Attempts Log

### 1. Initial PiP Window Implementation
Approach:
- Added `Picture in Picture Viewport` context-menu toggle in `AO2ViewportWindowContent`.
- Added a second `AO2ViewportWindowContent` and `Window` in `MainWindow`.
- PiP window is topmost, hidden when the host/main window becomes active, and shown when Oceanya loses focus.
- PiP close path disables `SaveFile.Data.GMPictureInPictureViewport`.

Result:
- PiP could be created and stay topmost.
- It hid when returning to Oceanya.
- It did not reliably reappear after returning to Oceanya and leaving again.
- PiP did not initially enforce the same aspect ratio until a resize occurred.

Outcome: partial; not acceptable.

### 2. PiP Aspect Ratio And Close-State Patch
Approach:
- Added native `WM_GETMINMAXINFO` / `WM_SIZING` handling for the PiP window using the same viewport sizing helpers as the normal viewport.
- Normalized PiP size before showing.
- Made PiP close count as untoggling the context-menu option.
- Added delayed foreground tracking through `pictureInPictureActivationTimer`.

Result:
- Aspect behavior improved after resize, but user still saw an incorrect initial frame/size.
- PiP still failed to show again after the first hide/show cycle.

Outcome: partial; not acceptable.

### 3. PiP Carbon-Copy Seed And Context Menu Disable
Approach:
- Added `AO2ViewportControl.LastRenderedMessage`.
- Added `AO2ViewportWindowContent.GetActiveLastRenderedMessage()` and `ReplayMessageForActiveClient(...)`.
- On PiP open, replay the normal viewport's last rendered message into the PiP content.
- Disabled the viewport right-click context menu in the PiP content.
- Opened PiP immediately when toggled on so the user can position/resize it before leaving Oceanya.

Result:
- PiP appears immediately and can be positioned.
- PiP no longer has the right-click context menu.
- PiP visually seeds from the last rendered message instead of starting empty.
- This is not a true shared render surface; it replays the latest viewport message/state into a second viewport host.

Outcome: partial; user still reports PiP visibility cycle failure.

### 4. Preserve Normal Viewport Logical Open State While PiP Hides It
Approach:
- Added `isNormalViewportHiddenByPictureInPicture`.
- When PiP hides the normal viewport, `CaptureViewportWindowState()` now persists `IsVisible = true` if it was hidden by PiP rather than explicitly closed.
- Startup restores the normal viewport when PiP is enabled, even if a previous bad persisted state said hidden.

Result:
- Fixed a startup regression where the normal viewport disappeared entirely after PiP had hidden it.

Outcome: fixed startup visibility, but not the repeated taskbar minimize PiP reappear bug.

### 5. Handle PiP In `HostWindow_StateChanged`
Approach:
- Changed `HostWindow_StateChanged` to process PiP mode before exiting due to `viewportWindow?.IsVisible != true`.
- If PiP is enabled and host state becomes minimized, call `ShowPictureInPictureViewportForExternalFocus()`.
- If PiP is enabled and host state becomes non-minimized, call `ShowNormalViewportForMainFocus()`.

Expected:
- The second taskbar minimize should show the PiP again even though the normal viewport is hidden by PiP at that moment.

Result:
- User reports no behavior change. The PiP still disappears after the restore/minimize cycle and the context-menu toggle remains checked.

Outcome: failed.

### 6. Handle PiP From Native Main `WM_SIZE`
Approach:
- `MainWindowHost_WndProc` now calls `HandlePictureInPictureHostWindowMessage(...)` before the viewport-preview early return.
- This means PiP minimize/restore handling is no longer dependent on `Use viewport as Windows preview`.
- On host `WM_SIZE` with `SIZE_MINIMIZED`, dispatch `ShowPictureInPictureViewportForExternalFocus()`.
- On host `WM_SIZE` with `SIZE_RESTORED` or `SIZE_MAXIMIZED`, dispatch `ShowNormalViewportForMainFocus()`.

Expected:
- Taskbar minimize should show/reopen the PiP even when WPF `StateChanged` or `Deactivated` does not fire in the useful order.
- Repeated minimize/restore cycles should keep working while the PiP toggle remains enabled.

Result:
- User reports PiP still works only for the first part of the cycle.
- After returning to Oceanya, the normal viewport and main window split into separate Windows taskbar entries even though `Use viewport as Windows preview` is enabled.
- After that split, PiP no longer works.

Outcome: failed; native `WM_SIZE` handling reached the scenario but did not preserve viewport-preview shell ownership.

### 7. Preserve Viewport-Preview Shell State While PiP Hides Normal Viewport
Approach:
- Removed `ApplyViewportTaskbarPriority()` from the path where `ShowPictureInPictureViewportForExternalFocus()` hides the normal viewport.
- Added a guard in `ApplyViewportTaskbarPriority()`:
  - if the normal viewport is temporarily hidden by PiP,
  - and `Use viewport as Windows preview` is enabled,
  - and the viewport window still exists,
  - then preserve the current shell/taskbar state instead of recalculating it from `IsViewportUsingWindowsPreview()`.
- On `ShowNormalViewportForMainFocus()`, after reopening/showing the normal viewport, dispatch a fresh `ApplyViewportTaskbarPriority()` so the main window and viewport rejoin under the viewport-preview shell entry.

Expected:
- PiP hiding the normal viewport should no longer make `ApplyViewportTaskbarPriority()` think preview mode is disabled.
- Returning to Oceanya should not split main and viewport into separate taskbar entries.
- Repeated PiP hide/show cycles should keep working.

Result:
- User reports the taskbar split is fixed.
- Remaining failure: after PiP is created, leaving Oceanya and returning hides PiP as expected, but leaving Oceanya again does not bring PiP back.
- User also observed the viewport appears to update/flicker twice for a few frames, suggesting the normal viewport and PiP visibility paths may be racing or sharing shell identity assumptions.

Outcome: partial; fixed shell/taskbar split, did not fix repeated PiP show.

### 8. Keep Foreground Tracker Alive While Normal Viewport Is Hidden By PiP
Approach:
- Added `IsViewportPreviewForegroundTrackingActive()`:
  - returns true for normal `IsViewportUsingWindowsPreview()`,
  - also returns true when the normal viewport is hidden specifically by PiP while viewport-preview mode remains configured.
- The viewport external foreground tracking timer now uses this broader predicate instead of stopping as soon as `viewportWindow.IsVisible == false`.
- Added `SynchronizePictureInPictureVisibilityWithForeground()`:
  - if Oceanya owns foreground, hide PiP and show the normal viewport,
  - if an external process owns foreground, hide the normal viewport and show PiP.
- The foreground tracker now calls this sync method on every tick.

Expected:
- Returning to Oceanya can hide PiP, but the foreground tracker stays alive.
- Leaving Oceanya again should show PiP again because foreground ownership changes to an external process.
- The normal viewport and PiP should no longer depend only on one-shot host `Activated`/`Deactivated` or taskbar `WM_SIZE` events.

Result:
- TBD.

Outcome: TBD.

### 9. Initial Fake-Focus Restore On Main Activation
Approach:
- Added `RestoreMainInputFocusAfterExternalActivation()`.
- On host activation, if the last main focused element is an eligible IC/OOC `TextBox`, restore focus.
- In viewport-preview mode, avoid real main focus and instead reactivate the viewport proxy target.

Result:
- User still reports the same fake-focus failure.

Outcome: failed.

### 10. Retain Proxy On Main Non-Input Mouse Click
Approach:
- In `MainWindow_PreviewMouseDown`, if the click is not on a textbox but viewport preview mode is active and the last focused element was an eligible IC/OOC textbox:
  - set `viewportPreviewInputProxyActive = true`
  - call `SetViewportPreviewInputProxyTarget(...)`
  - call `EnsureViewportIsForegroundShellRepresentative(..., allowExternalForegroundOverride: true)`

Expected:
- Clicking an IC log or other random main-window location should keep the fake-focused IC/OOC input as the active proxy target.

Result:
- User still reports the same fake-focus failure.

Outcome: failed.

### 11. Fallback To Last Fake-Focused Textbox During Main Visual Restore
Approach:
- In `RestoreMainWindowVisualForViewportReturn(...)`, if `GetViewportPreviewInputProxyTarget()` returns null, fall back to `lastMainWindowFocusedElement` when it is an eligible IC/OOC `TextBox`.
- Then call `SetViewportPreviewInputProxyTarget(...)` and keep the viewport as shell representative.

Expected:
- If the proxy visual target was lost during activation/focus transitions, the last fake-focused textbox should still be recovered.

Result:
- User reports no behavior change. The IC field still looks fake-focused but does not receive typing after clicking a random Oceanya area from behind another app.

Outcome: failed.

### 12. Native Main-HWND Keyboard Fallback For Preview Proxy
Approach:
- Added native routing in `MainWindowHost_WndProc` before the old preview-mode mouse handling.
- While `Use viewport as Windows preview` is active:
  - `WM_CHAR` received by the main HWND inserts text into the current proxy textbox.
  - `WM_KEYDOWN` received by the main HWND routes editing keys through the same `RouteViewportPreviewKeyToTextBox(...)` path used by the viewport proxy.
  - Alt/system keys are ignored so native Alt-Tab behavior is preserved.
- `WM_MOUSEACTIVATE` now also schedules a deferred restore/foreground handoff back to the viewport shell representative, in case the immediate handoff loses the race with Windows activation.

Expected:
- If the random main-window click leaves keyboard messages going to the main HWND instead of the viewport HWND, typing should still reach the fake-focused IC/OOC input.
- If the issue is a foreground race, the deferred handoff should give the viewport another chance to become the shell/input representative after the click.

Result:
- TBD.

Outcome: TBD.

### 13. Split PiP Foreground From Main-Viewport Foreground
Approach:
- Added an initial PiP placement state so toggling `Picture in Picture Viewport` on can leave the PiP visible while Oceanya is still foreground.
- Treated the PiP window foreground as its own state instead of generic current-process foreground.
- Foreground sync now restores the normal viewport only when the foreground belongs to Oceanya but is not the PiP window.
- When PiP hides the normal viewport while `Use viewport as Windows preview` is configured, the main Oceanya host temporarily regains the shell/taskbar representative; PiP remains a tool/PiP window with no taskbar item.
- Hiding/restoring the normal viewport removes that temporary host taskbar override.
- The PiP window is shown with `ShowActivated=false`, `WS_EX_NOACTIVATE`, and `WM_MOUSEACTIVATE -> MA_NOACTIVATE` so showing/interacting with PiP does not bring Oceanya back to foreground.
- The temporary host taskbar override also keeps `WS_EX_NOACTIVATE`; clearing it caused an activation loop where leaving Oceanya showed PiP and immediately reactivated the main Oceanya frame.
- The foreground synchronizer now only treats the actual main host or visible normal viewport HWND as a return-to-Oceanya signal. Generic current-process foreground was too broad and could flip PiP back off during shell/style churn.
- After PiP is shown for external focus, a short guard ignores immediate host activation/foreground churn so the PiP cannot enter a hide/show loop on the first leave.
- Foreground tracking is now observational only. Passive timer/WM_SIZE/state sync paths may hide the normal viewport and show PiP, but they must not call foreground-stealing helpers while `GetForegroundWindow()` belongs to another process.
- `EnsureViewportIsForegroundShellRepresentative(...)` now requires an explicit Oceanya return marker before it can override external foreground. Explicit markers are user click/activation paths such as main `WM_MOUSEACTIVATE`, main mouse down, viewport activation, and taskbar restore when the foreground is not external.
- Added debug logging for attempted foreground/focus operations with operation, reason, foreground HWND/process, target HWND, passive-vs-explicit mode, and allow/skip result.

Expected:
- First PiP toggle leaves the PiP visible so the user can resize/reposition it.
- Leaving Oceanya hides the normal viewport and keeps PiP visible.
- The taskbar still has an Oceanya representative while the normal viewport is hidden.
- PiP itself does not get a taskbar item.
- Clicking or resizing the PiP does not count as returning to Oceanya and does not instantly hide the PiP.
- Showing PiP after leaving Oceanya does not steal foreground from the external app.
- PiP does not flicker between hidden and visible after the first leave.
- Returning to the main Oceanya window still hides PiP and restores the normal viewport/preview shell state.
- Alt-Tab or clicking another application must leave Oceanya unfocused; passive sync should never pull the viewport/main HWND back to foreground.
- Clicking Oceanya again should restore the fake IC/OOC proxy and route typing without stealing focus during external-focus observation.

Result:
- TBD.

Outcome: TBD.

### 14. Stable Preview Shell HWND Architecture
Approach:
- Split the preview roles so the normal viewport is only the live viewport, PiP is only a topmost non-activating tool window, and a persistent preview-shell window is the Windows taskbar/Alt-Tab/DWM preview representative while `Use viewport as Windows preview` is enabled.
- Added a `ViewportPreviewShellState` state machine:
  - `PreviewDisabled`
  - `PreviewEnabled_NormalVisible`
  - `PreviewEnabled_PiPVisibleExternalFocus`
  - `PreviewEnabled_ReturningToMain`
- The preview shell is created once for preview mode, kept alive across PiP show/hide cycles, kept out of normal desktop layout, and fed by a duplicated `AO2ViewportWindowContent` attached to the same current viewport state. The live viewport visual is not moved between windows.
- PiP show/hide now controls only presentation: hide/show normal viewport and show/hide PiP. It no longer moves taskbar ownership to the main host and does not restyle the normal viewport as the shell representative.
- Preview shell DWM iconic thumbnails render from the best available viewport source: visible normal viewport, visible PiP, or the shell's duplicate viewport content.
- Fake IC/OOC input routing remains independent of making the normal viewport foreground. Main HWND and preview-shell HWND both route `WM_CHAR` and editing `WM_KEYDOWN` to the proxy textbox while preview mode is active.
- Every state transition logs `[VPT-SM]` with foreground HWND/process, main HWND, normal viewport HWND, preview shell HWND, PiP HWND, `ShowInTaskbar`, extended styles, owner HWND, visibility/minimize state, and passive-vs-explicit transition mode.
- Passive foreground observation is still limited to PiP presentation changes. It must not call activation/focus/foreground helpers.

Expected:
- Preview mode has exactly one stable shell/taskbar representative: the preview shell HWND.
- Normal viewport and PiP never become taskbar representatives during PiP transitions.
- PiP can be created, positioned, hidden on return to Oceanya, and shown again on later external-focus/minimize cycles.
- Alt-Tab/clicking another app should leave Oceanya unfocused because passive tracking no longer restyles or activates the live viewport/main host.
- Closing PiP still disables the PiP toggle.

Result:
- Build passed after introducing the shell split.
- Focused unit test run passed: 586 passed, 1 skipped.
- Runtime/manual behavior still needs desktop verification.

Outcome: TBD.

## Current Hypotheses

### PiP
The latest observed behavior indicates that binding taskbar ownership to the normal viewport is too fragile. PiP belongs to the same process, so clicking/resizing it must not trigger the "Oceanya returned" path. The stable fix is a separate preview shell HWND that remains the shell/taskbar representative while the normal viewport and PiP swap presentation state.

### Fake Focus
The main window may be coming to the front visually without the viewport receiving `PreviewKeyDown` / `TextInput`, so the proxy target exists visually but the actual keyboard input is landing on the main HWND or nowhere useful.

Likely next diagnostic:
- log foreground HWND and active HWND after the random main-window click
- log whether `ViewportWindow_PreviewKeyDown` / `ViewportWindow_TextInput` fires when the next character is typed
- log current proxy target, `viewportPreviewInputProxyActive`, and `IsViewportUsingWindowsPreview()`

If key events do not reach the viewport after the click, the fix likely needs native keyboard forwarding from the main HWND while preview mode is active, or a stronger foreground handoff back to the viewport HWND after main `WM_MOUSEACTIVATE`.

## Regression Checklist For Next Manual Test
- PiP toggle creates a visible topmost PiP.
- PiP has same aspect ratio immediately on first frame.
- PiP seeds from the normal viewport's current rendered state.
- PiP hides when returning to Oceanya.
- PiP reappears after taskbar minimize, including the second and later minimize cycles.
- PiP close button disables the context-menu toggle.
- PiP has no right-click viewport context menu.
- With viewport preview mode enabled, fake-focused IC field accepts typing after:
  - Alt-Tab out and back.
  - External app covers Oceanya, then user clicks the IC log.
  - External app covers Oceanya, then user clicks any non-input main-window area.
- Native Alt-Tab behavior remains acceptable.
