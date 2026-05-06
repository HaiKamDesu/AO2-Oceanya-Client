# AO2 Viewport

## Purpose
The AO2 viewport is the GM multi-client visual surface that mirrors the original AO2 client courtroom viewport at the native 256x192 resolution.

## Entry Points
- `OceanyaClient/MainWindow.xaml`: `Main.Viewport.Open` (`V`) button left of the area navigator `A` button.
- `OceanyaClient/MainWindow.xaml.cs`: opens the viewport as an owned, non-taskbar `GenericOceanyaWindow`.
- `OceanyaClient/Features/Viewport/AO2ViewportWindowContent.xaml`: fixed 256x192 hosted content.
- `OceanyaClient/Features/Viewport/AO2ViewportControl.xaml`: AO2 layer stack.
- `OceanyaClient/Features/Viewport/AO2ViewportAssetResolver.cs`: AO2-style asset lookup for backgrounds, desks, character sprites, and effects.
- `AOBot-Testing/Structures/ICMessage.cs`: parsed incoming `MS#` chat packet fields consumed by the viewport.

## Window Ownership
The viewport is a separate WPF window for movement and closing, but it is owned by the main GM window and has `ShowInTaskbar=false`. This gives Windows the intended grouped behavior: it does not appear as a separate taskbar window, and it follows owner activation/minimize/close behavior.

## Current Layer Order
The WPF layer order follows AO2 `Courtroom` construction:
1. background
2. speedlines placeholder
3. main character
4. paired character placeholder
5. desk
6. effect
7. AO2 chatbox preview
8. shout/objection overlay

`AO2ViewportControl` adjusts Z-order at render time for AO2 pair ordering and effect `layer` metadata.

## Current Implementation Map
- `OceanyaClient/MainWindow.xaml`: `btnViewport` is the `V` button. Its canvas position is maintained near the area navigator controls.
- `OceanyaClient/MainWindow.xaml.cs`: `btnViewport_Click()` calls `OpenViewportWindow()`. `OpenViewportWindow()` creates one owned `GenericOceanyaWindow`, sets `Owner=HostWindow`, `ShowInTaskbar=false`, fixed native dimensions, and attaches the current `AOClient`. `SelectClient()` calls `viewportContent?.AttachClient(currentClient)` so the open viewport follows the selected GM client profile.
- `OceanyaClient/Features/Viewport/AO2ViewportWindowContent.xaml(.cs)`: wraps the viewport in shared Oceanya chrome. Resize is disabled, move and close are enabled. Content is 256x296: 256x192 AO viewport plus the AO2 FullChar-theme 256x104 chatbox below it.
- `OceanyaClient/Features/Viewport/AO2ViewportControl.xaml`: declares the native 256x192 layer stack and a separate below-viewport `AO2ChatPreviewControl`, so chat text does not obstruct the view.
- `OceanyaClient/Features/Viewport/AO2ViewportControl.xaml.cs`: subscribes to `AOClient.OnICMessageReceived` on the actual incoming-message client. It intentionally does not repaint on selected character/emote/background UI changes; incoming server-echoed `ICMessage` packets drive the visible scene, including preanimation phase, shout overlay phase, character/pair sprites, desk state, effects, speedlines, chatbox visibility, and the AO2-style message text crawl. On attach/BG change it renders the current background only, matching AO2's room-entry behavior before the first IC message.
- `OceanyaClient/Features/Viewport/AO2ViewportAssetResolver.cs`: centralizes AO2-compatible lookup and render decisions: background position aliases, `design.ini` origin scaling, desk overlay resolution, packet preanimation resolution, AO2 emote-mod normalization, talk/idle sprite selection, pair ordering, zoom speedline names, shout overlay filenames, and effect `effects.ini` metadata.
- `OceanyaClient/Components/AO2ChatPreviewControl.xaml(.cs)` and `OceanyaClient/Features/ChatPreview/AO2ChatPreviewResolver.cs`: render the below-viewport chatbox from AO2 `courtroom_design.ini`/`courtroom_fonts.ini` values. The viewport intentionally follows the live AO2 install's `(714x688) FullChar` theme with the default subtheme/font fallback: `ao2_chatbox=0,178,256,104`, `showname=1,0,46,15`, `message=10,12,242,89`; font sizes are converted from Qt point sizes to WPF device-independent pixels. `showname_align` is parsed with AO2's `right`/`center`/`justify` handling so FullChar shownames center in the showname field. Live viewport message text uses the incoming `MS#` packet text color, matching AO2's `filter_ic_text(..., TEXT_COLOR)` path, rather than the theme's default message color.
- `OceanyaClient/Components/Ao2AnimationPreview.cs`: provides static previews and animation players. Viewport playback now requests full-fidelity GIF decoding instead of the preview sampler so long AO2 GIF preanimations such as `VydValkKam/Dmc4intro.gif` are not reduced to 180 preview frames.
- `AOBot-Testing/Structures/CharacterAssetPathResolver.cs`: resolves character asset tokens with a case-insensitive fallback to match AO2/Windows behavior for packets whose animation token casing differs from filenames.
- `AOBot-Testing/Structures/ICMessage.cs`: parses AO2 `MS#` fields. `OtherCharIdRaw` preserves CCCC pair ordering suffixes such as `12^1`; `OtherOffsetVertical` preserves vertical pair offsets where present.
- `UnitTests/StructureTests.cs`: unit coverage for resolver behavior and packet fields that the viewport depends on.

## Chat Data Flow
1. The network `AOClient` receives an `MS#` packet from the server and raises `OnICMessageReceived`. In single-internal-client mode this is `singleInternalClient`, not the selected GM profile object.
2. `AO2ViewportControl.OnICMessageReceived()` dispatches to the UI thread and calls `RenderMessage()`.
3. `RenderMessage()` resolves packet character/showname/background/side data, then follows AO2's message order:
   - show shout overlay first when `ShoutModifier` is Hold It, Objection, Take That, or Custom;
   - render the preanimation phase when AO2's normalized `EmoteModifier`/`Immediate` behavior requests it and the packet `PreAnim` asset exists;
   - render the speaking/chat phase from the same packet, not from currently selected UI widgets.
4. The chatbox is rendered below the viewport. This intentionally differs from stock AO2's overlay position to avoid covering the scene.
5. Background and desk are resolved from the packet side and current background. Character, pair, flip, offsets, effects, speedlines, and chatbox contents are derived from the same `ICMessage`.
5. Selecting another GM client profile, character, or emote does not repaint the viewport by itself. The next IC message supplies the new visible state, matching AO2's message-owned courtroom update behavior.

## AO2 Reference Behavior
The C# implementation should be compared against these AO2 files before changing viewport behavior:
- `AO2-Client/src/courtroom.cpp`: `set_background`, `handle_ic_message`, `handle_emote_mod`, `play_preanim`, `set_scene`, `initialize_chatbox`
- `AO2-Client/src/courtroom.h`: viewport constants and layer fields
- `AO2-Client/src/animationlayer.*`: frame playback, scaling, flipping, playback-once, and layer composition
- `AO2-Client/src/path_functions.cpp`: `get_pos_path` for `design.ini` origin/background/desk resolution.
- `AO2-Client/src/text_file_functions.cpp`: `get_effect_property` for `effects.ini` metadata.

Important AO2 contracts:
- Native viewport size is 256x192.
- Background position aliases include `def`, `hld`, `jud`, `hlp`, `pro`, `wit`, `jur`, and `sea`.
- Legacy background filenames such as `defenseempty` and `prosecutorempty` remain valid.
- Desk visibility depends on packet `deskMod`; `chat` means position-driven desk visibility.
- Background and desk move/scale together when a background position has an AO2 design origin.
- Effects may be behind the character, under the desk, over the viewport, or in the chat overlay layer depending on effect metadata.
- Pair characters use packet pair id, pair emote, flip, offset, and `^order` metadata.
- Zoom and preanim-zoom hide the desk/pair and choose prosecution or defense speedlines from packet side.
- Objection/Hold It/Take That/Custom shouts gate the normal message path until the shout overlay finishes.
- AO2 does not apply the new chat packet scene before a shout finishes; it overlays the shout on the previous viewport scene, then continues into the same message's preanim/speaking path.
- Preanimations use packet field `PRE_EMOTE` (`ICMessage.PreAnim`) directly. They are not derived from the selected emote's configured preanimation name during receive-time rendering.
- Character talking/idle sprites use packet field `EMOTE` as AO2's raw animation token. The resolver may use the emote table when the packet value matches an emote name/id, but it must fall back to resolving `(b)<packet emote>`, `(a)<packet emote>`, and raw packet token paths because AO2 sends and loads the animation token.
- Character and preanimation asset lookup is case-insensitive in practice because AO2 is normally running on Windows and because content often differs in casing, e.g. `preexplosionrev` packet token vs `PreExplosionRev.gif`.
- AO2 normalizes legacy emote modifiers in `handle_emote_mod`: `4` is treated as preanim zoom, `2` is treated as preanimation, and unknown values fall back to idle/no-preanim.
- AO2 `handle_emote_mod` sends `PREANIM`/`PREANIM_ZOOM` through `play_preanim(false)`, which blocks the chat/speaking phase until `text_delay_timer` or `finishedPreOrPostEmotePlayback`. `IDLE`/`ZOOM` with the immediate flag go through `play_preanim(true)`, then immediately enter `handle_ic_speaking`.
- In this AO2 submodule, `play_preanim(false)` reads `[Time]`, `[stay_time]`, and SFX delay from `char.ini`, but only `stay_time` and SFX delay are multiplied by `time_mod=40` and actively scheduled. The `[Time]` value is passed as a duration limit into `CharacterAnimationLayer::loadCharacterEmote`, but the duration timer is not started by the referenced code path, so animated preanims normally advance by their own playback completion. Oceanya mirrors that by waiting for the preanimation player's `PlaybackFinished` event; `[Time]` is only a static/fallback estimate now.
- During speaking, AO2 chooses the talking `(b)` sprite while chat text is active unless the packet text color disables talking; otherwise it chooses the idle `(a)` sprite.
- AO2 starts `chat_tick()` immediately for non-empty messages, reveals text by grapheme, treats `{`/`}` as speed controls, handles escaped `\n`, `\p`, `\s`, and `\f`, and delays punctuation. Oceanya implements the visible crawl subset without blips, rich text colors, or frame-triggered screen effects.
- Desk mod 5 (`DESK_PRE_ONLY_EX`) shows the desk during preanim, then hides desk/pair and centers the main character for the speaking phase.
- Chatbox font/image behavior is delegated to the existing AO2 chat preview resolver; placement is below the viewport in Oceanya.

## Current Coverage
Unit coverage exists for:
- background and desk asset resolution
- desk visibility decisions for common post-preanimation states
- preanimation-phase desk visibility decisions
- AO2 `design.ini` origin scaling
- effect layer metadata from `effects.ini`
- pair ordering suffix parsing
- shout overlay and zoom speedline names

## Known Parity Gaps
- Preanimation timing now waits for animated playback completion first, matching AO2's receive-time `play_preanim(false)` behavior. `[stay_time]` fallback values use AO2's `time_mod=40`; `[Time]` remains only a static/unsupported-animation fallback, not a hard cutoff for normal animated GIF playback.
- Objection/shout overlays are displayed with animated frame playback when the asset format is supported, but their continuation timing is still approximated by a timer.
- WT/CE overlays, evidence presentation overlays, sticker faces, blips, frame-triggered realization/screenshake/SFX, and real screenshake motion are still not implemented.
- Character, pair, effect, speedline, and shout images use the shared WPF animation player for GIF/APNG/WebP where supported. Viewport GIF playback bypasses preview frame caps; background and desk playback still use static placement.
- Effects read `layer`, `stretch`, `respect_flip`, and `respect_offset`; loop/cull/max-duration/scaling are documented but not fully animated.
- Background slide transitions are not animated yet; origin scaling is applied to the resolved static scene.
- AO2's full chat queue lifecycle is still approximated: incoming messages replace the current viewport state instead of waiting on exact text completion and queue drain conditions.

## Exact Verification Commands
Run these from the repository root. Build and test must be separate commands.

```bash
/mnt/c/Program\ Files/dotnet/dotnet.exe build "Oceanya Client.sln"
/mnt/c/Program\ Files/dotnet/dotnet.exe test UnitTests/UnitTests.csproj --filter "FullyQualifiedName~UnitTests"
```

Do not run UI automation tests for viewport unit-only work unless explicitly requested.
