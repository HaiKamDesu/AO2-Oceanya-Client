# Viewport Windows Preview Alt-Tab Issue

## Problem
GM multi-client has two important windows:
- the main GM window, where IC/OOC text focus should remain
- the viewport window, which can be selected as the Windows taskbar/Alt-Tab preview

The desired behavior is:
- Windows taskbar and Alt-Tab show the viewport preview
- clicking or Alt-Tabbing back to Oceanya restores focus to the main GM window text input
- Alt-Tab treats main window plus viewport like one app window, so one Alt-Tab leaves Oceanya and one Alt-Tab returns
- the preview uses the real viewport window size/aspect and stays live

The hard part is that Windows shell identity, live thumbnail rendering, window activation, and keyboard focus are all tied to HWNDs. Making the viewport the shell window gives the right visual preview, but focus redirection back to main can make Alt-Tab remember the main HWND as the last active window, causing the double Alt-Tab problem.

## Current Best Version
As of 2026-05-18, the best-looking version is the native viewport shell version in `MainWindow.ApplyViewportTaskbarPriority()`:
- viewport is the visible taskbar/Alt-Tab HWND
- main GM window is hidden from shell/taskbar while preview mode is active
- viewport remains the foreground/active shell representative after activation
- viewport activation restores/restacks the main GM window visually without activating it
- IC/OOC typing is routed through a focused-input proxy instead of activating the main GM HWND
- `ViewportThumbnailCompositor` is not used for the active preview path, except to deactivate stale DWM state

Result:
- preview looks correct because Windows captures the real viewport HWND
- no custom bitmap scaling, no pillarboxing from the main window frame, no frozen/corrupt thumbnail
- avoids the known cause of the double Alt-Tab loop: activating the hidden/tool main HWND solely to restore text focus
- requires manual Windows verification for one-out/one-back Alt-Tab and held Alt+Tab cycling

Keep this as the visual baseline unless a new approach also proves its preview quality is correct.

## Attempts Log

### 1. Hide Viewport From Shell, Keep Main As Shell
Approach:
- main window stays in taskbar/Alt-Tab
- viewport hidden from taskbar/Alt-Tab via `WS_EX_TOOLWINDOW`
- viewport marked `WS_EX_NOACTIVATE`

Result:
- did not solve the user workflow
- Windows preview remained the main window, not the viewport

Outcome: rejected.

### 2. Viewport As Shell Window, Focus Redirect To Main
Approach:
- viewport shown in taskbar/Alt-Tab
- main hidden from shell
- activation/focus redirected back to main window so IC/OOC typing remains usable

Result:
- viewport preview looked correct
- focus preservation worked
- double Alt-Tab remained: returning from Alt-Tab activates/lands on main, so the next Alt-Tab can first select the viewport shell entry before leaving Oceanya

Outcome: best visual baseline, but original bug remains.

### 3. Main As Only Shell Window With DWM Custom Thumbnail
Approach:
- main stays the only taskbar/Alt-Tab shell HWND
- viewport hidden from taskbar/Alt-Tab
- main handles `WM_DWMSENDICONICTHUMBNAIL` and `WM_DWMSENDICONICLIVEPREVIEWBITMAP`
- `ViewportThumbnailCompositor` feeds viewport pixels through `DwmSetIconicThumbnail`

Result:
- fixed the double Alt-Tab behavior because Windows only saw one shell HWND
- preview initially froze or refreshed too slowly
- preview could be too small because DWM was receiving a fixed or undersized bitmap

Outcome: solved shell identity, broke preview quality.

### 4. DWM Thumbnail Refresh Timer
Approach:
- add `DwmInvalidateIconicBitmaps` on a timer, eventually lowered to 50 ms

Result:
- preview refreshed more often
- user still saw bad/corrupted preview behavior and poor frame rate in some versions

Outcome: not enough.

### 5. DWM Thumbnail From Inner 256x192/256x296 Viewport Surface
Approach:
- render the actual AO2 viewport control/canvas via WPF `VisualBrush`
- try to preserve aspect ratio while fitting into DWM requested max size

Result:
- scaling improved in some cases
- preview showed only part of the desired window, or looked too pixelated because source was the fixed native AO2 surface instead of the resized window area

Outcome: rejected.

### 6. DWM Thumbnail From Full Viewport Window Visual
Approach:
- render the whole viewport host window via WPF `VisualBrush`
- use DWM requested size and letterbox internally to avoid stretch

Result:
- better source surface than the inner 256x192 control
- Windows still framed the custom bitmap as the main HWND's preview, because the taskbar entry belonged to the main window
- user saw pillarboxing, wrong aspect, horizontal stretch, vertical empty space, and pixelation

Outcome: rejected.

### 7. Revert To Native Viewport Shell Ownership
Approach:
- stop using custom DWM thumbnail for active preview mode
- restore viewport as real shell HWND
- temporarily own/hide main window from shell
- keep focus redirected to main

Result:
- best-looking preview again
- double Alt-Tab issue returned

Outcome: current best version.

### 8. Defer Focus Redirect During Alt-Tab Activation
Approach:
- keep attempt 7's native viewport shell ownership so preview quality stays intact
- detect viewport activation while the Alt key is still held via `GetAsyncKeyState(VK_MENU)`
- do not immediately activate/focus the main window during that Alt-Tab activation
- poll every 15 ms until Alt is released, then restore focus to the main GM window text input
- keep mouse activation behavior unchanged: `WM_MOUSEACTIVATE` still redirects immediately and returns `MA_NOACTIVATE`

Expected:
- Windows may keep the viewport shell HWND as the Alt-Tab MRU entry long enough to avoid the next double Alt-Tab, while still restoring text focus after the user releases Alt

Result:
- still has the double Alt-Tab issue
- preview remains perfect

Outcome: failed for Alt-Tab behavior, but preserved the best visual preview.

### 9. Pre-Activate Viewport Shell HWND Before Main-Window Alt-Tab
Approach:
- keep attempt 7's native viewport shell ownership and attempt 8's Alt-release focus deferral
- when the main window receives Left Alt or Alt+Tab while viewport preview mode is active, temporarily clear `WS_EX_NOACTIVATE` on the viewport
- call `SetForegroundWindow(viewportHwnd)` so Windows starts Alt-Tab from the viewport shell HWND instead of the hidden/owned main HWND
- suppress the normal viewport activation focus redirect during this handoff
- poll every 15 ms until Alt is released, then restore `WS_EX_NOACTIVATE`
- if Oceanya is still foreground after Alt release, focus the main text input again; if another app is foreground, do not steal focus back

Expected:
- pressing Alt while focused in the main GM window should make the viewport the active shell representative before Windows processes Alt-Tab
- one Alt-Tab should then leave Oceanya instead of first selecting the viewport entry
- returning to Oceanya should still restore main-window typing focus after Alt is released

Result:
- preview remains perfect
- double Alt-Tab is fixed: one Alt-Tab from main IC focus goes to the next program
- intermittent bug: sometimes focus goes to the next program, viewport hides, but main window remains visible/focused; pressing Alt brings viewport back, implying Oceanya stole focus back during/after the handoff
- likely cause: the Alt-release cleanup can still refocus main if it sees viewport as foreground during a timing window, even though the user intended to leave Oceanya

Outcome: partial success; keep the pre-activation approach, but prevent focus steal after a real Alt+Tab exit.

### 10. Do Not Refocus Main After Real Alt+Tab Exit
Approach:
- keep attempt 9's pre-activation because it fixed the double Alt-Tab behavior and preserved the perfect preview
- while Alt is held during exit preparation, poll whether Tab was pressed via `GetAsyncKeyState(VK_TAB)`
- when Alt is released:
  - always restore viewport `WS_EX_NOACTIVATE`
  - if Tab was pressed, do not call `FocusMainWindowFromViewportPreview()`, even if foreground timing briefly still reports the viewport
  - if Tab was not pressed and the user only tapped Alt, restore main focus as before

Expected:
- real Alt+Tab exits should not steal focus back to Oceanya
- plain Alt taps should not leave viewport active
- preview and no-double-Alt-Tab behavior should remain as in attempt 9

Result:
- same intermittent result as attempt 9
- Alt-Tab sometimes leaves Oceanya with one press, sometimes behaves like the old double Alt-Tab case
- tracking Tab with `GetAsyncKeyState(VK_TAB)` was not reliable enough to distinguish all real Alt+Tab exits

Outcome: failed; keep the pre-activation idea, but remove the unreliable main-refocus cleanup path completely for exit preparation.

### 11. Never Refocus Main From Exit Preparation Cleanup
Approach:
- keep attempt 9's pre-activation because it can produce the desired single Alt-Tab behavior
- when the main window receives Left Alt/Alt+Tab and starts exit preparation, restore viewport `WS_EX_NOACTIVATE` after Alt release but never call `FocusMainWindowFromViewportPreview()` from that exit-preparation timer
- keep normal viewport activation focus restore for returning to Oceanya

Expected:
- leaving Oceanya should stop racing against a cleanup path that can steal focus back to main
- all real Alt+Tab exits should be single Alt-Tab
- possible tradeoff: a plain Alt tap while focused in main may leave viewport as the foreground HWND until the user interacts again

Result:
- intermittent bug still occurs, but user reports the likelihood seems lower
- repeated Alt-Tab cycles about half a second apart may make the issue easier to trigger
- preview remains perfect
- single Alt-Tab often works, but not consistently enough

Outcome: partial improvement, not reliable enough.

### 12. Only Pre-Activate On Actual Alt+Tab, Not Plain Left Alt
Approach:
- keep the attempt 11 behavior of never refocusing main from exit-preparation cleanup
- stop pre-activating the viewport on bare Left Alt
- only call `PrepareViewportForAltTabExit()` when Tab is pressed while Alt is already down
- this reduces the time window where Oceanya temporarily makes the viewport foreground before Windows receives the actual Alt+Tab command

Expected:
- fewer races during repeated Alt-Tab cycles
- no plain-Alt side effects
- if Tab-key timing is still early enough, single Alt-Tab behavior should remain

Result:
- double Alt-Tab returned
- only pre-activating on Tab is too late for Windows Alt-Tab MRU ordering

Outcome: failed; early Left Alt handoff appears necessary.

### 13. Early Handoff Plus Temporarily Hide Main During Switch-Out
Approach:
- restore the early Left Alt handoff from attempts 9-11 because that is what fixed the double Alt-Tab path
- when preparing the viewport shell HWND for Alt-Tab exit, temporarily hide the main GM window so it cannot remain visibly stuck if focus moves to another program
- if Alt is released and Oceanya is still foreground, show main again and refocus text input
- if another app is foreground, keep main hidden until the viewport/Oceanya is activated again
- when returning to Oceanya through the viewport shell preview, show main and restore text focus

Expected:
- single Alt-Tab should leave Oceanya consistently
- the intermittent "Visual Studio focused but main window still visible" failure should be blocked because main is hidden during the handoff
- possible tradeoff: main window may briefly hide during plain Alt taps or rapid Alt-Tab, but should come back when Oceanya remains/returns foreground

Result:
- failed badly
- pressing/holding Alt before Tab hides the main window even though no switch has happened yet
- viewport remains visible by itself while Alt is held
- after switching to Visual Studio, main window can reappear on top of Visual Studio without the viewport
- repeated Alt presses make main hide/show and viewport show/hide in a janky sequence
- not an acceptable smooth single Alt-Tab experience

Outcome: rejected; do not hide the main window during Alt-Tab preparation.

### 14. Early Handoff With Native Alt+Tab Detection, No Hiding
Approach:
- remove attempt 13's main-window hiding
- keep early Left Alt viewport shell handoff, because late Tab-only handoff failed
- track actual Alt+Tab via native `WM_KEYDOWN`/`WM_SYSKEYDOWN` for `VK_TAB` in both the main HWND and viewport HWND hooks
- after Alt is released:
  - if native Tab was seen, restore viewport `WS_EX_NOACTIVATE` but do not refocus main
  - if no native Tab was seen, treat it as a plain Alt tap and refocus main

Expected:
- preserve single Alt-Tab behavior from the early handoff
- avoid attempt 13's visible hide/show jank
- make the "real Alt+Tab happened" flag more reliable than WPF `OnPreviewKeyDown` or timer-only `GetAsyncKeyState`

Result:
- failed badly
- Alt-Tab from the main window leaves the main window visible at the end of the swap every time
- repeated Alt-Tab cannot escape the main window cleanly
- pressing Alt again brings viewport back, implying focus/shell state is still cycling inside Oceanya

Outcome: rejected; native Tab detection without hiding does not prevent main from staying visible/focused.

### 15. Early Handoff, Hide Main Only After Real Tab
Approach:
- keep early Left Alt viewport shell handoff, because late Tab-only handoff returned the original double Alt-Tab
- do not hide main on plain Left Alt
- when Tab is actually detected while Alt is held, hide the main GM window so it cannot remain visible on top of the target app
- after Alt is released:
  - if viewport/Oceanya is still foreground, show main and restore text focus
  - if another app is foreground, keep main hidden until Oceanya/viewport is activated again
- when viewport activation/focus restore runs on return, show main before focusing text

Expected:
- no plain-Alt hide/show jank
- single Alt-Tab should still leave Oceanya
- main window should not remain on top of Visual Studio after the switch

Result:
- failed badly
- same unescapable behavior as attempt 14
- Alt-Tab does not behave like a clean single swap out of and back to Oceanya
- hiding main only after actual Tab detection did not fix the shell/focus loop

Outcome: rejected; the early viewport handoff family is not reliable enough and should not be extended with more timing tweaks.

### 16. Native Alt+Tab-Only Handoff
Approach:
- abandon the early Left Alt viewport handoff from attempts 9-15
- do not change foreground, `WS_EX_NOACTIVATE`, or main visibility when the user only presses/holds Alt
- when native `WM_KEYDOWN`/`WM_SYSKEYDOWN` for `VK_TAB` is observed while Alt is held:
  - temporarily allow viewport activation
  - call `SetForegroundWindow(viewportHwnd)` so the viewport remains the shell representative
  - hide the main GM window so it cannot stay visibly on top of the target app
  - keep the main hidden if another app becomes foreground
  - show/focus main if Oceanya is still foreground after Alt release or when returning through the viewport

Expected:
- plain Alt should have no visual side effect
- one Alt+Tab should still have a chance to leave through the viewport shell HWND
- if Windows refuses the switch and viewport remains foreground, main should be restored instead of leaving the app in a half-hidden state

Result:
- failed
- behavior returned to the original problem:
  - double Alt-Tab is required to leave Oceanya from main-window focus
  - one Alt-Tab returns to Oceanya
  - viewport preview still looks correct

Outcome: failed; native Alt+Tab-only handoff is too late to change Windows MRU ordering.

### 17. Viewport Shell Without Main-Window Ownership
Approach:
- keep the best-looking native viewport preview path:
  - viewport is the visible taskbar/Alt-Tab HWND
  - main GM window is hidden from taskbar/Alt-Tab
  - viewport still uses `WS_EX_NOACTIVATE` and redirects activation to main for typing
- remove the owner relationship where main was owned by viewport
- keep main and viewport as separate top-level windows from Win32 ownership's point of view, while only the viewport is visible to the shell

Expected:
- Windows should no longer treat the focused main window as the last active popup of the viewport shell entry
- Alt-Tab from the hidden-from-shell main HWND may go directly to the previous external app instead of first selecting the viewport
- preview should remain perfect because the viewport remains the real shell HWND

Risk:
- minimize/restore or z-order grouping may be worse because Win32 no longer knows main and viewport are an owner group

Result:
- failed
- preview remains good
- double Alt-Tab is still required to leave Oceanya from main-window focus
- one Alt-Tab returns to Oceanya

Outcome: failed; removing the Win32 owner relationship did not change shell MRU behavior.

### 18. Intercept Main-Window Alt+Tab And Activate Previous External Window
Approach:
- keep the good native viewport shell preview path
- stop trying to make Windows' MRU pick the right item when the hidden-from-shell main window has focus
- when the main HWND sees `Alt+Tab`, handle the key before the shell performs its normal switch
- enumerate top-level windows in z-order and activate the first eligible external window that is not part of the Oceanya process
- do not use this for Alt+Shift+Tab or held Alt-Tab cycling; only handle the simple quick-swap case

Expected:
- one Alt+Tab from main focus should leave Oceanya by directly activating the previous external app
- one normal Alt+Tab from the external app should return to the viewport shell entry, then restore main text focus
- preview should remain perfect because viewport remains the real shell HWND

Risk:
- this emulates the common one-press quick-swap case, not the full held-Alt task switcher UI
- window eligibility filtering must avoid desktop/taskbar/tool windows

Result:
- failed
- same behavior as attempt 17:
  - preview remains good
  - double Alt-Tab is still required to leave Oceanya
  - one Alt-Tab returns to Oceanya

Outcome: failed; WPF/main-HWND interception was not early or reliable enough to override the shell Alt-Tab path.

### 19. Low-Level Keyboard Hook Quick-Swap
Approach:
- keep the good native viewport shell preview path
- poll foreground HWND while preview mode is active and remember the last eligible external top-level window
- install a low-level keyboard hook only while viewport preview mode is active
- when the hook sees a simple `Alt+Tab` while Oceanya owns foreground:
  - suppress the shell's normal Alt+Tab key
  - restore/activate the remembered external foreground window directly
- do not handle Alt+Shift+Tab or held Alt cycling

Expected:
- one Alt+Tab from main focus should leave Oceanya because the hook runs before the shell consumes the key
- one normal Alt+Tab from the external app should return to the viewport shell preview
- preview should remain perfect because viewport remains the real shell HWND

Risk:
- this is a scoped global hook while preview mode is enabled, so it must be installed/uninstalled cleanly
- only the common quick-swap gesture is emulated

Result:
- partial success, but not acceptable
- one Alt-Tab leaves Oceanya and one Alt-Tab returns
- preview remains good
- however, the real Windows Alt-Tab menu does not appear when leaving
- holding Alt after pressing Alt+Tab cannot cycle through other windows
- behavior feels like Oceanya is being minimized/direct-switched rather than native Alt-Tab switching

Outcome: partial but rejected; direct activation fixes quick-swap count but does not preserve native Alt-Tab UI/selection behavior.

### 20. Low-Level Hook On Alt Release Only
Approach:
- keep the scoped low-level keyboard hook and external foreground tracking from attempt 19
- do not suppress the initial Alt+Tab key-down, so Windows can show the real Alt-Tab switcher and allow held-Alt cycling
- only intervene later if needed, after Alt is released and Windows lands back on Oceanya/viewport instead of an external app

Expected:
- holding Alt after Alt+Tab should still show the native Windows switcher
- one quick Alt+Tab may still be corrected if Windows chooses the viewport/main loop
- preview should remain perfect

Risk:
- if Windows commits the double-step selection before Alt release, this may be too late to fix quick-swap
- focus correction after Alt release can race with normal shell activation

Result:
- failed
- behavior returned to the original problem:
  - two Alt-Tabs are required to leave Oceanya
  - one Alt-Tab returns to Oceanya
- native Alt-Tab UI is preserved, but quick-swap is not fixed

Outcome: failed; waiting until Alt release preserves the native switcher but is too late to correct the double Alt-Tab path.

### 21. Quick Tap Suppression With Held-Alt Reinjection
Approach:
- keep the scoped low-level keyboard hook and external foreground tracking
- on simple Alt+Tab from Oceanya foreground, suppress the original Tab key-down so Windows does not immediately choose the viewport/main loop
- start a short hold timer:
  - if Alt is released quickly, activate the remembered external window directly
  - if Alt remains held long enough, temporarily disable our suppression and re-inject Alt+Tab with `keybd_event` so the native Windows Alt-Tab switcher appears for cycling
- do not handle Alt+Shift+Tab

Expected:
- quick tap should be one Alt-Tab to leave and one to return
- held Alt+Tab should still show the native Windows switcher after a short delay and allow cycling
- preview should remain perfect

Risk:
- synthetic Alt+Tab may interact poorly with Windows' secure input handling or hook recursion
- there may be a small delay before the native switcher appears on held Alt+Tab

Result:
- back to the window being two alt tabs to leave and one alt tab to go back in

Root cause identified (held-Alt path): the 160ms reinject timer fires and injects Tab while the MAIN window (WS_EX_TOOLWINDOW, hidden from shell) is still foreground. Windows' Alt-Tab switcher then cycles from the main window's perspective: first Tab lands on the viewport (the only Oceanya shell entry visible in Alt-Tab, and it is treated as the "current" app), so a second Tab is needed to reach the external app. Quick-tap path was correct; held-Alt path was wrong because the injection source window was wrong.

Outcome: failed for held-Alt path; quick-tap path logic was sound.

### 22. Pre-Activate Viewport Before Held-Alt Tab Injection
Approach:
- keep all of attempt 21 (Tab suppression, direct activation for quick-tap, 160ms held-reinject timer)
- before the timer fires and injects the Tab, make the viewport foreground first:
  - set `suppressViewportActivationFocusRedirect = true`
  - remove `WS_EX_NOACTIVATE` from viewport
  - call `SetForegroundWindow(viewportHwnd)`
- inject Tab immediately after; Windows now sees Alt+Tab starting FROM the viewport, so first Tab = external app
- on Alt-up, if the held pre-activation ran, restore viewport `WS_EX_NOACTIVATE` and clear `suppressViewportActivationFocusRedirect`
- track the pre-activation with a `viewportAltTabHeldPreActivated` flag
- in `MarkViewportAltTabKeyIfNeeded`, guard with `viewportAltTabHeldPreActivated` so the viewport WndProc skips `PrepareViewportForAltTabExit` and `HideHostWindowForViewportAltTabExit` when the hook's held path is active
- also remove `HideHostWindowForViewportAltTabExit` from `MarkViewportAltTabKeyIfNeeded` entirely; hiding main during exit caused the main window to stay hidden on return

Expected:
- quick tap (Alt released before 160ms): same as attempt 21 (direct activation, one switch)
- held Alt+Tab (Alt held past 160ms): viewport becomes foreground, injected Tab starts from viewport, native switcher shows external as first selection, releasing Alt lands on external — one switch
- returning from external: viewport activated, `FocusMainWindowFromViewportPreview` runs, main is activated and text focus restored
- preview should remain perfect

Risk:
- slight visual flicker or activity indicator on viewport taskbar button during the held path
- main window remains visible on screen when user switches to external app (not hidden); this is cosmetic and acceptable

Result:
- rejected
- either breaks the native held Alt+Tab switcher or reintroduces the wrong-source HWND problem
- pre-activating/reinjecting still leaves too much behavior dependent on timing and synthetic input

Outcome: rejected; keep the native viewport shell visual baseline but disable the low-level hook/reinjection family.

### 23. Viewport Foreground With Focused Input Proxy
Approach:
- keep the native viewport shell visual baseline:
  - viewport is the only visible taskbar/Alt-Tab HWND
  - main GM window is hidden from shell/taskbar
  - DWM custom thumbnail composition remains inactive
- stop activating/focusing the main GM HWND from viewport activation paths
- let viewport `WM_MOUSEACTIVATE` follow default activation and do not redirect it to main
- make the main GM HWND `WS_EX_NOACTIVATE` while preview mode is active so clicks do not make it the shell MRU foreground window
- add a focused-input proxy while preview mode is active:
  - viewport `PreviewKeyDown` and `TextInput` route text/editing commands to the last logical main IC/OOC `TextBox`
  - supports normal text, Backspace/Delete, Enter send, Tab between IC/OOC fields, arrows, Home/End, Ctrl+A/C/V/X/Z/Y
  - Escape/Alt/system keys are not swallowed, preserving native Alt+Tab
- add `[VPT-ALT]` viewport debug logs with foreground HWND, active HWND, main/viewport styles, and proxy route status
- leave the old low-level keyboard hook/reinject code present but disabled by removing installation/call paths

Expected:
- one Alt-Tab leaves Oceanya because Windows starts from the real foreground viewport shell HWND
- one Alt-Tab returns to the viewport shell HWND
- after return, typed IC/OOC text reaches the selected main input without making main foreground
- held native Alt+Tab cycling remains native because the app no longer suppresses or injects Alt+Tab
- preview remains the live real viewport HWND with correct aspect

Risk:
- WPF text routing is now custom for the proxied fields; edge editing behavior may need follow-up for less common shortcuts
- `WS_EX_NOACTIVATE` on the main window may affect direct keyboard interaction with main-window controls while preview mode is active, though mouse clicks still update the logical target field

Result:
- implementation complete; requires manual Windows verification against the regression checklist
- 2026-05-18 crash follow-up: clicking IC/OOC log/document content can report a `FlowDocument` as the mouse source; `MainWindow_PreviewMouseDown` now uses a safe visual/logical ancestor walk and logs/skips target tracking failures instead of crashing while updating the logical input target

Outcome: pending manual verification.

### 24. Non-Activating Main Visual Restore On Viewport Return
Approach:
- keep attempt 23's viewport-shell/input-proxy architecture
- when viewport activation fires on Alt-Tab/taskbar/shell return, call `RestoreMainWindowVisualForViewportReturn()` before enabling the input proxy
- restore the main GM HWND visually without activating it:
  - clear `isHostWindowHiddenByViewportAltTabExit`
  - set the main HWND `WS_EX_NOACTIVATE`
  - if hidden/minimized/cloaked, call `ShowWindow(..., SW_SHOWNOACTIVATE)`
  - call `SetWindowPos(main, viewportHwnd, ..., SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE)`
  - call `SetWindowPos(viewport, HWND_TOP, ..., SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE)` so viewport remains the active shell representative
- do not call `MainWindow.Activate()`, `SetForegroundWindow(mainHwnd)`, `Focus()`, or `FocusMainWindowFromViewportPreview()` from this path
- after visual restore, reselect the logical IC/OOC target and enable `viewportPreviewInputProxyActive`
- expand `[VPT-ALT]` diagnostics with main visible/minimized/cloaked state, viewport visibility, restack results, and proxy state

Expected:
- one Alt-Tab leaves Oceanya
- one Alt-Tab returns and visually brings back both viewport and main as the app group
- viewport remains the foreground/active shell HWND
- IC/OOC typing still routes through the viewport input proxy
- main does not steal focus from external apps when switching away

Risk:
- exact Z-order can still vary by shell policy, but all restacking is non-activating and scoped to viewport return

Result:
- implementation complete; requires manual Windows verification against the regression checklist

Outcome: pending manual verification.

## DWM Notes
`DwmSetIconicThumbnail` and `WM_DWMSENDICONICTHUMBNAIL` are useful for replacing the thumbnail bitmap of a specific HWND, but they do not change which HWND owns the shell preview. When the main window owns the taskbar entry, Windows still applies shell preview behavior around the main HWND. This is why custom viewport bitmaps on the main HWND produced pillarboxing/stretching.

Windows ownership/MRU notes:
- `GetLastActivePopup(ownerHwnd)` returns the most recently active owned popup; this matters because the viewport owns main in preview mode, so the shell can keep bouncing activation back to the main popup even though the viewport is the visible taskbar HWND.
- Microsoft documents that ownership groups are intended to produce a single taskbar button, but owner/owned activation can still be surprising if more than one window in the group can become active.

Important references:
- `DwmSetIconicThumbnail`: https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmseticonicthumbnail
- `WM_DWMSENDICONICTHUMBNAIL`: https://learn.microsoft.com/windows/win32/dwm/wm-dwmsendiconicthumbnail
- `GetLastActivePopup`: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastactivepopup
- Microsoft owner/taskbar activation note: https://learn.microsoft.com/en-us/troubleshoot/windows/win32/owner-window-activated-taskbar-buttons-created-both-windows
- Raymond Chen custom thumbnail example: https://devblogs.microsoft.com/oldnewthing/20130225-00/?p=5153

## Next Ideas To Try
These are unproven. Log every attempt and result in this document.

### A. Delay Focus Redirect After Alt-Tab Return
Hypothesis:
- keep viewport as the shell/Alt-Tab HWND
- when Windows activates viewport from Alt-Tab, do not immediately activate main
- wait until Alt is released or a short dispatcher delay passes, then focus main text input

Risk:
- typing immediately after Alt-Tab may briefly target the viewport unless focus handoff is carefully timed
- might still update Windows' MRU list to main once focus moves

### B. Detect Alt-Tab Activation Path Separately From Mouse Activation
Hypothesis:
- keep `WM_MOUSEACTIVATE` redirect for clicks
- treat keyboard shell activation differently, allowing viewport to remain the active shell HWND long enough for Windows MRU ordering
- manually focus main text box without activating the main HWND if possible

Risk:
- WPF keyboard focus usually requires the containing window to be active
- may not preserve normal typing behavior

### C. Owner Chain Variant
Hypothesis:
- viewport remains shell HWND
- main is owned by viewport only while both are visible, but activation redirection is adjusted so the owner shell HWND remains last active from Windows' perspective

Risk:
- owned-window activation behavior is finicky and can affect minimize/restore

### D. Invisible Shell Proxy Window
Hypothesis:
- create a third small/transparent shell/proxy HWND that owns both main and viewport
- proxy appears in Alt-Tab/taskbar and supplies a native or custom preview of viewport

Risk:
- high complexity
- may reproduce DWM custom thumbnail aspect problems
- may introduce taskbar/minimize bugs

## Regression Checklist
For every new attempt, record:
- shell entry shown in Alt-Tab/taskbar
- whether one Alt-Tab leaves Oceanya from main focus
- whether one Alt-Tab returns to Oceanya
- whether text focus returns to IC/OOC input
- preview aspect/size
- preview refresh/fps
- whether preview is frozen, corrupted, pixelated, pillarboxed, stretched, or cropped
- minimize/restore behavior for both windows
- click behavior on viewport and main
