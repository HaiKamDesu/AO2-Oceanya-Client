# AOHD Wide Viewport Placement Attempt Log

## Issue

Observed flow:

- Config uses the AOHD theme.
- Joining a server shows the viewport correctly: the wide background fills the AOHD viewport and the desk/overlay is sized with it.
- After speaking as a character, the background shifts left and leaves a visible empty gap on the right.
- After another message, the desk/overlay can end up broken the same way.

AO2 reference behavior: `Courtroom::set_scene` sizes the background and desk widgets from the background frame. If the background is not `stretch=true`, AO2 scales by viewport height and centers by the configured position origin. The desk widget is then moved/resized to the exact same rect as the background widget.

## Attempt 1 - inspect resolver placement math

Files checked:

- `AO2-Client/src/courtroom.cpp`
- `AO2-Client/src/animationlayer.cpp`
- `OceanyaClient/Features/Viewport/AO2ViewportAssetResolver.cs`
- `UnitTests/ViewportParityTests.cs`

Result:

- The resolver already matched the AO2 math for the repro class of assets.
- Existing tests covered a 768x576 viewport with a 1024x576 background, expecting `Left=-128`, `Width=1024`, `Height=576`.
- `ResolveDeskPlacement` also already used the background frame placement for the desk/overlay layer.

Conclusion: the parity break was not in the resolver.

## Attempt 2 - inspect render-time layout passes

File checked:

- `OceanyaClient/Features/Viewport/AO2ViewportControl.xaml.cs`

Finding:

- `RenderScene` called `ApplyThemeLayout(chatToken, hasShowname)` before resolving and applying the background placement.
- It then set `BackgroundImage.Width/Height` to the correct placement via `SetPlacedAnimatedImage`.
- Later in the same render, after updating `ChatPreview` properties, it called `ApplyThemeLayout()` again.
- `ApplyThemeLayout()` calls `SetViewportLayerSize(...)`, which resets `BackgroundImage`, `DeskImage`, and other layer sizes to the raw viewport size.

Observed effect:

- Correct AO2 placement: `Left=-128`, `Width=1024`.
- After the second layout pass: `Left=-128`, `Width=768`.
- That produces the exact right-side gap seen in AOHD.

## Fix

Removed the redundant second `ApplyThemeLayout()` call inside `RenderScene`.

Reasoning:

- The first layout call already uses the resolved chat token and showname state through explicit arguments.
- The later call has no new geometry information and can only reapply the same layout while clobbering placed layer sizes.
- `ChatPreview.RefreshPreview()` remains responsible for refreshing chatbox content after the chat properties are assigned.

## Attempt 3 - context-menu chatbox overlap toggle

Finding:

- The viewport context menu toggles `AO2ViewportControl.ChatboxOverlapsViewport`.
- That setter also calls `ApplyThemeLayout()`.
- Toggling overlap therefore reproduced the same size reset after a correctly rendered message: `SetViewportLayerSize(...)` squashed the already placed background/desk back to raw viewport size.

Fix:

- After layout toggles, `ReapplyScenePlacementAfterLayoutChange()` now re-resolves the current background and desk placement using the current background/position and reapplies the AO2 rect.
- It also reapplies height-based character geometry and chat-arrow bounds so the active scene survives the layout mode change.

## Regression Test

Added `AO2ViewportControl_RenderMessage_AndChatboxOverlapToggle_DoNotSquashWideBackground` in `UnitTests/ViewportParityTests.cs`.

The test creates:

- AOHD-style theme geometry: `viewport=0,0,768,576`.
- A 1024x576 `widebg/wit.png`.
- A preview IC message in position `wit`.

Expected result after message render:

- `BackgroundImage.Width == 1024`
- `BackgroundImage.Height == 576`
- `Canvas.Left == -128`
- `Canvas.Top == 0`

The test asserts the same placement immediately after message render, after enabling chatbox overlap, and after disabling it again.

## Test Isolation Note

The test fixture now redirects `SaveFile` to a temp file and restores the original path in teardown before deleting the temp directory. This prevents viewport preference writes from touching a developer/user savefile during unit tests.
