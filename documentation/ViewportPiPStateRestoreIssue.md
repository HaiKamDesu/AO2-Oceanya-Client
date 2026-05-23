# Viewport And PiP Window State Restore Issue

## Problem

The GM viewport has two independent windows:

- Normal viewport: `SaveFile.Data.GMViewportWindowState`
- Picture-in-picture viewport: `SaveFile.Data.GMPictureInPictureViewportState`

They must never read from or write to each other's saved position or size. The only valid case where they appear at the same bounds is when the user explicitly placed both windows there.

Current observed behavior:

- PiP position often restores correctly.
- PiP size restores correctly when it was saved larger than the theme/native default size.
- PiP size does not restore correctly when it was saved smaller than the theme/native default size but still above the viewport minimum. It opens at the default theme size instead.
- Sometimes the normal viewport appears to restore at the PiP spot, or PiP appears to open at the normal viewport spot, which suggests a state separation or restore-time contamination issue.

Example user description:

- If the theme/default viewport size is `2` and PiP is resized to `1`, PiP should restore to `1`.
- Instead, PiP restores to `2`.

## Relevant Code Paths

Normal viewport creation and restore:

- `MainWindow.OpenViewportWindow()`
- reads `SaveFile.Data.GMViewportWindowState`
- calls `ResolveViewportWindowRestoreState(...)`
- calls `ResolveViewportContentRestoreSize(...)`
- writes `viewportContent.Width/Height`
- creates `viewportWindow`
- calls `ShowViewportWindowAfterRestore()`

Normal viewport save:

- `CaptureViewportWindowState()`
- writes `SaveFile.Data.GMViewportWindowState`
- uses `viewportWindow`
- uses `lastViewportWindowWidth/Height`
- uses `viewportContent.CurrentSurfaceWidth/Height`

PiP creation and restore:

- `MainWindow.OpenPictureInPictureViewportWindow()`
- reads `SaveFile.Data.GMPictureInPictureViewportState`
- calls `ResolveViewportWindowRestoreState(...)`
- calls `ResolveViewportContentRestoreSize(...)`
- writes `pictureInPictureViewportContent.Width/Height`
- creates `pictureInPictureViewportWindow`
- calls `ShowPictureInPictureViewportWindowAfterRestore()`

PiP save:

- `CapturePictureInPictureViewportWindowState(...)`
- writes `SaveFile.Data.GMPictureInPictureViewportState`
- uses `pictureInPictureViewportWindow`
- uses `lastPictureInPictureViewportWindowWidth/Height`
- uses `pictureInPictureViewportContent.CurrentSurfaceWidth/Height`

Shared sizing infrastructure:

- `OceanyaWindowManager.HostedSizingSyncController`
- syncs hosted content size to host window size
- syncs host window actual size back to hosted content size
- this can run during `Show()` and WPF layout

Surface/layout events:

- `AO2ViewportWindowContent.RefreshHostSurfaceSize(...)`
- raises `ViewportSurfaceLayoutChanged`
- normal handler: `OnViewportSurfaceLayoutChanged()`
- PiP handler: `OnPictureInPictureViewportSurfaceLayoutChanged()`

## Attempts So Far

### 1. Separate PiP Toggle Persistence From PiP Bounds

Change:

- Stopped persisting the context-menu "Picture in Picture Viewport" toggle across sessions.
- `AO2ViewportWindowContent.PictureInPictureViewport` became runtime-only.
- Legacy `SaveFile.Data.GMPictureInPictureViewport` is normalized to `false`.

Result:

- Correct direction. The PiP visibility toggle should not persist.
- Did not solve PiP size restore.

### 2. Separate Normal And PiP Save State Objects

Change:

- Audited and adjusted save/restore paths so normal viewport uses `GMViewportWindowState`.
- PiP uses `GMPictureInPictureViewportState`.
- Added separate last-size caches:
  - `lastViewportWindowWidth/Height`
  - `lastPictureInPictureViewportWindowWidth/Height`

Result:

- Improved position behavior in some cases.
- Did not fully eliminate reports of normal/PiP position confusion.
- Did not solve PiP smaller-than-default size restore.

### 3. Save Content Bounds And Surface Metadata

Change:

- `CreateViewportWindowStateFromHostBounds(...)` saves content bounds, not raw outer window bounds.
- Save state includes `SurfaceWidth` and `SurfaceHeight`.
- Restore converts content bounds back to outer window bounds.

Result:

- Normal viewport restore works better.
- PiP still restores smaller-than-default sizes to theme/default size.

### 4. Incorrect Attempt: PiP-Specific Smaller Minimum Size

Change:

- Added PiP-specific minimum content size below the normal viewport minimum.
- Routed PiP min-track sizing and save/restore normalization through that smaller minimum.

Result:

- Wrong fix.
- User explicitly wants PiP and normal viewport to have the exact same minimum size.
- This was reverted.

### 5. Attempt: Use Remembered PiP Bounds During PiP Post-Show Normalization

Change:

- Changed PiP post-show/theme-layout normalization to use `lastPictureInPictureViewportWindowWidth/Height` through `ResolveCapturedWindowWidth/Height(...)`.

Result:

- Did not fix the issue.
- Likely reason: `SizeChanged` can update the remembered PiP dimensions during restore/layout before the restore target is fully applied, so the remembered size can already be contaminated by default/native layout.

## Current Working Hypothesis

The PiP restore path has a valid saved target size, but transient WPF layout events during `Show()` and `ViewportSurfaceLayoutChanged` can overwrite the in-memory remembered PiP size before restore completes.

Specifically:

- `ShowPictureInPictureViewportWindowAfterRestore()` sets `isRestoringPictureInPictureViewportWindow = true`.
- `pictureInPictureViewportWindow.Show()` can trigger `SizeChanged`.
- `PictureInPictureViewportWindow_SizeChanged(...)` always calls `RememberPictureInPictureViewportWindowSize(...)`, even while restore is in progress.
- Capture is guarded during restore, but remembering is not guarded.
- Later normalization can use the contaminated remembered value.
- If the transient layout size is the theme/native default, PiP visually restores to that default.

The fix should make restore target bounds explicit and immutable during restore:

- Store the intended restore width/height/left/top before showing the window.
- While restore is active, ignore `SizeChanged`/`LocationChanged` as sources for remembered state.
- During restore/layout callbacks, reapply the restore target rather than reading potentially stale/current WPF window dimensions.
- Once restore has completed, commit the restore target to the remembered cache and then allow normal resize/move saves again.

## Required Invariants

- Normal viewport restore must only read `GMViewportWindowState`.
- Normal viewport save must only write `GMViewportWindowState`.
- PiP restore must only read `GMPictureInPictureViewportState`.
- PiP save must only write `GMPictureInPictureViewportState`.
- PiP and normal viewport must share the same minimum size.
- A saved PiP size smaller than the theme/native default but not smaller than the viewport minimum must restore exactly.
- Restore-time layout events must not overwrite saved state or remembered size before restore completes.

## Implemented Follow-Up Fix

After documenting the audit, `MainWindow` was updated so PiP restore keeps explicit pending restore bounds:

- `pendingPictureInPictureViewportRestoreWidth`
- `pendingPictureInPictureViewportRestoreHeight`
- `pendingPictureInPictureViewportRestoreLeft`
- `pendingPictureInPictureViewportRestoreTop`

PiP restore now:

- Sets those pending bounds from `GMPictureInPictureViewportState` before creating/showing the PiP window.
- Uses pending restore bounds during post-show normalization and surface-layout callbacks.
- Ignores PiP `SizeChanged` and `LocationChanged` as remembered/save-state inputs while `isRestoringPictureInPictureViewportWindow` is true.
- Commits the intended restore size to the PiP remembered-size cache only after applying the restore target.
- Clears the pending restore target after restore completion or PiP teardown.

This is intended to prevent transient WPF/theme layout from replacing a smaller saved PiP size with the native/default viewport size during restore.
