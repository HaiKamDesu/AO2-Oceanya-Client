# PiP Viewport Restore And Lag Investigation

## Problem

The normal AO2 viewport and the picture-in-picture viewport have separate saved geometry:

- `SaveFile.Data.GMViewportWindowState`
- `SaveFile.Data.GMPictureInPictureViewportState`

They must restore independently. The normal viewport must never read or write PiP geometry, and PiP must never read or write normal viewport geometry.

Observed failures:

- PiP sometimes restores at the normal viewport's position or size.
- The normal viewport sometimes appears at the PiP position after restart or first restore.
- PiP position can restore correctly while PiP size restores to the default theme size.
- PiP sizes larger than the default theme size restore more reliably than sizes smaller than the default.
- With normal viewport and PiP active, sending an IC message can freeze the UI before the viewport starts rendering the message.
- The IC delay can remain noticeable even after PiP has been toggled off.
- On a two-monitor setup, placing PiP near the edge of the first monitor can restore it at an unexpected spot on the second monitor.

## Attempts So Far

### Save geometry on move and resize

The viewport windows were wired to capture geometry from `SizeChanged`, `LocationChanged`, `WM_SIZE`, and `WM_EXITSIZEMOVE`.

Result:

- Position saving improved.
- PiP size still restored to default in important cases.
- This did not fully prevent normal/PiP geometry confusion.

### Add separate PiP restore state

PiP restore was pointed at `GMPictureInPictureViewportState`, while normal viewport restore stayed pointed at `GMViewportWindowState`.

Result:

- The intended storage keys are separate.
- Confusion still appeared at runtime, implying the issue was not only savefile property selection.

### Avoid saving the PiP context menu toggle

The PiP context menu toggle should be session-only. Persisting it creates surprising startup behavior and makes debugging restore state harder.

Result:

- Current runtime code keeps `AO2ViewportWindowContent.PictureInPictureViewport` as an in-memory toggle.
- `SaveFile.NormalizeData` forces the legacy `GMPictureInPictureViewport` boolean to `false`.

### Lower PiP minimum size

One hypothesis was that PiP had a stricter minimum size than the normal viewport, so the PiP window was clamped to the default theme size.

Result:

- Rejected. PiP and normal viewport should have the same minimum size.
- The real symptom was not "PiP needs a smaller minimum"; it was "PiP should restore saved sizes below default theme size, down to the same minimum the normal viewport supports."

### Restore PiP size after show

PiP gained a pending restore target so it could reapply width, height, left, and top after WPF loaded the hosted window.

Result:

- Some timing issues improved.
- PiP still restored default-sized when the saved size was below the default theme size.

## Current Findings

### PiP was not a true mirror

Before the latest fix, PiP created its own `AO2ViewportWindowContent`, then created/attached its own `AO2ViewportControl` instances for every client. Those controls subscribed to the same AO client events as the normal viewport.

That means PiP was not just displaying the normal viewport. It was independently processing:

- incoming IC messages
- IC actions
- background changes
- music changes
- RT/testimony/verdict packets

This explains the IC lag report: an IC send could cause two live viewport render paths to parse and render the same message on the UI dispatcher.

### Geometry keys were separate, but content lifecycle could still interfere

The normal and PiP host windows save to different savefile properties. However, because PiP had a full independent viewport content tree, surface layout changes from the PiP content could drive PiP window normalization back toward the active theme's default viewport surface instead of preserving the user's smaller saved host size.

## Latest Fix Direction

PiP should be a passive visual mirror:

- normal viewport remains the only live message-processing viewport
- PiP does not attach to AO client events
- PiP does not replay IC messages
- PiP displays the normal viewport's `ViewportHost` through a WPF `VisualBrush`
- PiP keeps only its own host window geometry persistence

This keeps the two concerns cleanly separated:

- render state comes only from the normal viewport
- normal geometry is saved only to `GMViewportWindowState`
- PiP geometry is saved only to `GMPictureInPictureViewportState`

## Files To Audit When This Regresses

- `OceanyaClient/MainWindow.xaml.cs`
  - `OpenViewportWindow`
  - `OpenPictureInPictureViewportWindow`
  - `CaptureViewportWindowState`
  - `CapturePictureInPictureViewportWindowState`
  - `ResolveViewportWindowRestoreState`
  - `ResolveViewportContentRestoreSize`
  - `NormalizeViewportWindowSize`
- `OceanyaClient/Features/Viewport/AO2ViewportWindowContent.xaml.cs`
  - `AttachClient`
  - `EnsureClient`
  - `MirrorFrom`
  - `RefreshHostSurfaceSize`
- `Common/SaveFile.cs`
  - `ViewportWindowState`
  - `GMViewportWindowState`
  - `GMPictureInPictureViewportState`
  - `NormalizeData`
  - `ClampViewportWindowState`

