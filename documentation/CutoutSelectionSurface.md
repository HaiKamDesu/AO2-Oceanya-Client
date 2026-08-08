# Cutout Selection Surface (Emote Cutting / Configure Cutouts)

## Purpose
Photoshop-style square-selection editor used by the AO2 character creator's cutting dialogs
("Cut Emote..." single dialog and the "Configure cutouts..." bulk dialog). It replaces the old
draw-only rubber-band canvas: the square can now be adjusted after it is drawn, the preview
behaves like the reusable asset image viewer (zoom/pan/bounds), and every change is undoable.

## Main code
- `OceanyaClient/Utilities/CutoutSelectionSurface.cs` — the reusable control (all interaction logic).
- `OceanyaClient/Components/Forms/AOCharacterFileCreatorWindow.xaml.cs`
  - `ShowEmoteCuttingDialog(...)` — single-emote dialog.
  - `ShowBulkEmoteCuttingDialog(...)` — "Configure Cutouts" batch dialog.
  Both host one `CutoutSelectionSurface` inside their resizable `previewBorder`, and add
  `selectionSurface.Toolbar` right under the resize grip.
- `Common/SaveFile.cs` — `CharacterCreatorViewImageBounds` (shared with `AssetImageViewerDialog`),
  `CharacterCreatorCutoutShowGuides`, `CharacterCreatorCutoutDimOutside`.
- Tests: `UnitTests/CutoutSelectionSurfaceTests.cs` (STA).

## Interaction contract
- **Draw**: drag anywhere outside the current square to create a new one (always square).
  A bare click (drag < 2 px) does **not** destroy the existing selection — it is restored on mouse up.
- **Resize**: drag any of the 8 handles. Corners anchor the opposite corner; edges anchor the
  opposite edge and keep the perpendicular axis centered. The square constraint is always enforced,
  and the result is clamped inside the frame.
- **Alt (resize/draw from center)**: holding Alt during a draw or a handle drag keeps the square's
  center fixed and grows outward from it (`SquareFromCenter`, clamped so it stays inside the frame).
  Evaluated live per mouse move, so Alt can be pressed/released mid-drag like in Photoshop.
- **Space + drag**: pans the view (same as middle-drag). Checked via `Keyboard.IsKeyDown(Key.Space)` on
  mouse-down, so it works without the surface having keyboard focus; `HandleKey` swallows Space while
  the pointer is over the image so a focused dialog button is not activated mid-pan.
- **Move**: drag from inside the square.
- **Undo/redo**: `Ctrl+Z` / `Ctrl+Y` (`Ctrl+Shift+Z` also redoes), plus toolbar `↶`/`↷`.
  One undo entry per completed gesture or discrete action. History is reset on source/emote switch.
- **Nudge**: arrow keys move by 1 px (Shift = 10 px); `+`/`-` resize by the same step.
  Arrow keys only apply while the surface is hovered or focused so dialog focus navigation still works.
- **Zoom/pan**: mouse wheel zooms around the cursor, middle-drag pans, scrollbars available;
  toolbar has `−` / `1:1` / `⤢ fit` / slider / percentage. `NearestNeighbor` scaling kicks in at ≥ 2× so
  pixel art stays crisp.
- **Right-click menu** (standard bold section headers via `ContextMenuSectionHelper`):
  Selection (center to image, fit largest square, snap to visible content, center horizontally/vertically,
  clear), Clipboard (copy/paste cutout square — routed back to the dialog's own copied-selection state),
  History (undo/redo), View (zoom actions, toggle image bounds / thirds guides / dim outside).

## Visuals
- Rule-of-thirds guides: 2 vertical + 2 horizontal lines inside the square at 1/3 and 2/3,
  white at alpha 70 — deliberately faint, they exist only for centering, not decoration.
- Marching-ants dashed selection stroke over a dark backing stroke so it reads on any art.
- Dim overlay outside the selection (EvenOdd geometry), toggleable.
- Dashed orange "ghost" rectangle = previously saved selection for the current source
  (`SetGhostSelection`), same meaning as before the rewrite.

## Geometry model
The surface owns the selection in **source-image pixel coordinates** (`PixelSquareSelection`).
The image element and overlay canvas are both sized to `frame.PixelSize * zoom`, so
display = pixel × zoom and there is no letterboxing math anywhere. This replaced the old
`ComputeImageViewport` / `ProjectPixelSquareSelectionToViewport` /
`TryCreatePixelSquareSelectionFromDisplayBounds` helpers, which were deleted.

## Gotchas
- `SetFrame` keeps zoom and selection when the new frame has the same pixel size (animation playback);
  a different size refits the view and reclamps the selection.
- **Never early-out of `ApplyZoom` without re-running `UpdateZoomDependentLayout()`/`RefreshOverlay()`.**
  The dialogs call `SetFrame(null)` before loading the next emote, which collapses the host to 0×0;
  if the refit then lands on the zoom that was already active (common — sibling emotes are the same
  size), an early return left the image invisible, the bounds rect parked in the corner and the canvas
  unclickable until the user zoomed manually. `ZoomToFit` also retries at most 5 times while the host
  is still unmeasured instead of re-posting forever.
- The dialogs still own persistence (`CutSelectionState`, `savedCutSelectionByEmoteKey`,
  `SaveFile.Data.CharacterCreatorCutSelections`). The surface knows nothing about emotes or sources.
- Dialogs mirror `surface.Selection` into their `currentPixelSelection` through `SelectionChanged`;
  `SelectionCommitted` is where the bulk dialog captures entry state and refreshes navigator tiles.
- `Toolbar` is built in the constructor; host it, do not rebuild it.
