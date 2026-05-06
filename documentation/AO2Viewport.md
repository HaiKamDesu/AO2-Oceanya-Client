# AO2 Viewport

## Purpose
The AO2 viewport is the GM multi-client visual surface that mirrors the original AO2 client courtroom viewport at the native 256x192 resolution.

## Entry Points
- `OceanyaClient/MainWindow.xaml`: `Main.Viewport.Open` (`V`) button left of the area navigator `A` button.
- `OceanyaClient/MainWindow.xaml.cs`: opens the viewport as an owned, non-taskbar `GenericOceanyaWindow`.
- `OceanyaClient/Features/Viewport/AO2ViewportWindowContent.xaml`: resizable hosted content that scales the native 256x296 viewport/chatbox surface with a centered, aspect-ratio-preserving `Viewbox`.
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
- `OceanyaClient/MainWindow.xaml`: `btnViewport` is the `V` button, `btnSettings` is the gear icon, and the folder visualizer button sits immediately to their right in the bottom control row.
- `OceanyaClient/MainWindow.xaml.cs`: `btnViewport_Click()` calls `OpenViewportWindow()`. `OpenViewportWindow()` creates one owned `GenericOceanyaWindow`, sets `Owner=HostWindow`, `ShowInTaskbar=false`, fixed native dimensions, and attaches the current `AOClient`. `btnSettings_Click()` opens the shared dark settings window. `SelectClient()` calls `viewportContent?.AttachClient(currentClient)` so the open viewport follows the selected GM client profile.
- `OceanyaClient/Features/Viewport/AO2ViewportWindowContent.xaml(.cs)`: wraps the viewport in shared Oceanya chrome. Resize, move, and close are enabled. The native content surface remains 256x296: 256x192 AO viewport plus the AO2 FullChar-theme 256x104 chatbox below it. The host window scales that surface uniformly and centers it in any extra space.
- `OceanyaClient/Features/Viewport/AO2ViewportControl.xaml`: declares the native 256x192 layer stack and a separate below-viewport `AO2ChatPreviewControl`, so chat text does not obstruct the view.
- `OceanyaClient/Components/Forms/SettingsWindow.xaml(.cs)`: shared dark settings dialog with an `Audio` tab. The three sliders are persisted through `SaveFile.Data.AudioMusicVolume`, `AudioSfxVolume`, and `AudioBlipVolume`.
- `OceanyaClient/Features/Viewport/AO2ViewportControl.xaml.cs`: subscribes to `AOClient.OnICMessageReceived` on the actual incoming-message client. It intentionally does not repaint on selected character/emote/background UI changes; incoming server-echoed `ICMessage` packets drive the visible scene, including preanimation phase, shout overlay phase, character/pair sprites, desk state, effects, speedlines, chatbox visibility, and the AO2-style message text crawl. It also listens to `OnIcActionReceived` for viewport-only AO2 music playback, and it plays blips/SFX only while the viewport window is visible. On attach/BG change it renders the current background only, matching AO2's room-entry behavior before the first IC message.
- `OceanyaClient/Features/Viewport/AO2ViewportAssetResolver.cs`: centralizes AO2-compatible lookup and render decisions: background position aliases, `design.ini` origin scaling, desk overlay resolution, packet preanimation resolution, AO2 emote-mod normalization, talk/idle sprite selection, pair ordering, zoom speedline names, shout overlay filenames, and effect `effects.ini` metadata.
- `OceanyaClient/Features/Viewport/AO2ViewportAudioResolver.cs`, `OceanyaClient/Features/Viewport/AO2ViewportAudioManager.cs`, and `OceanyaClient/AO2BlipPreviewPlayer.cs`: local AO2-style path resolution and playback for viewport music/SFX/blips. The manager uses saved slider volumes and ignores playback when the viewport is not visible. Blips use AO2's five-stream BASS cycle and resume-style `ChannelPlay(..., restart: false)` behavior so rapid text blips overlap like the Qt client instead of restarting a single voice.
- `OceanyaClient/Components/Ao2AnimationPreview.cs`: the viewport animation players now expose frame-index notifications in addition to image updates, which the viewport uses to fire AO2-style frame SFX and screenshake events against the currently resolved emote token.
- `OceanyaClient/Components/AO2ChatPreviewControl.xaml(.cs)` and `OceanyaClient/Features/ChatPreview/AO2ChatPreviewResolver.cs`: render the below-viewport chatbox from AO2 `courtroom_design.ini`/`courtroom_fonts.ini` values. The viewport intentionally follows the live AO2 install's `(714x688) FullChar` theme with the default subtheme/font fallback: `ao2_chatbox=0,178,256,104`, `showname=1,0,46,15`, `message=10,12,242,89`; font sizes are converted from Qt point sizes to WPF device-independent pixels. `showname_align` is parsed with AO2's `right`/`center`/`justify` handling so FullChar shownames center in the showname field, and `showname_extra_width` drives AO2-style `chatmed`/`chatbig` image selection plus wider showname bounds for long names. Live viewport message text uses the incoming `MS#` packet text color, matching AO2's `filter_ic_text(..., TEXT_COLOR)` path, rather than the theme's default message color.
- `AO2-Client/src/options.cpp` and `AO2-Client/src/courtroom.cpp`: AO2 stores audio sliders as `default_music`, `default_sfx`, and `default_blip` in `config.ini` on a 0-100 scale. `music_player` uses the music slider for the main music channel and the SFX slider for the ambient channels; `sfx_player` uses the SFX slider; `blip_player` uses the blip slider. That split is the reference for the Oceanya audio settings UI.
- `OceanyaClient/Components/AO2ChatPreviewControl.xaml(.cs)` and `OceanyaClient/Features/ChatPreview/AO2ChatPreviewResolver.cs`: render the below-viewport chatbox from AO2 `courtroom_design.ini`/`courtroom_fonts.ini` values. The viewport intentionally follows the live AO2 install's `(714x688) FullChar` theme with the default subtheme/font fallback: `ao2_chatbox=0,178,256,104`, `showname=1,0,46,15`, `message=10,12,242,89`; font sizes are converted from Qt point sizes to WPF device-independent pixels. `showname_align` is parsed with AO2's `right`/`center`/`justify` handling so FullChar shownames center in the showname field. Live viewport message text uses AO2-style IC formatting: packet text color index, `chat_config.ini` `c0..c8` colors, `cN_start`/`cN_end` inline color markup, removed markup tokens, `~~` center, `~>` right, `<>` justify, `{`/`}` speed markers, escaped `\n`, `\p`, `\s`, and `\f`. During message crawl, `PreviewText` updates only the text document, caret, and scroll position, mirroring AO2's per-tick `setHtml` + `setTextCursor` + `ensureCursorVisible` path without re-resolving layout/images on every character. The native viewport chat crawl reads AO2's `text_crawl`, `blip_rate`, and `blank_blip` values from the active `config.ini`, uses AO2's integer-truncated delay math, and starts the first character through a zero-interval timer like AO2's `chat_tick_timer->start(0)`. It keeps AO2's one-character-per-tick behavior and schedules each following timer with exactly the computed AO2 `msg_delay`; it does not shorten later intervals to catch up for dispatcher latency. `stay_time` controls AO2's post-message queue hold and `chat_ratelimit` controls outgoing chat submission, so neither changes incoming viewport blip cadence.
- `OceanyaClient/Components/Ao2AnimationPreview.cs`: provides static previews and animation players. Viewport playback now requests full-fidelity GIF decoding instead of the preview sampler so long AO2 GIF preanimations such as `VydValkKam/Dmc4intro.gif` are not reduced to 180 preview frames.
- `OceanyaClient/Components/Ao2AnimationPreview.cs`: provides static previews and animation players. Viewport playback now requests full-fidelity GIF decoding instead of the preview sampler so long AO2 GIF preanimations such as `VydValkKam/Dmc4intro.gif` are not reduced to 180 preview frames. Animation playback is scheduled to the next actual frame boundary instead of being polled on a fixed interval, and late ticks now catch up against the original frame schedule instead of rescheduling from "now", which removes the accumulated slowdown that was dragging long emotes and their SFX behind AO2.
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
- During speaking, AO2 chooses the talking `(b)` sprite while chat text is active unless the packet text color disables talking; otherwise it chooses the idle `(a)` sprite. When `chat_tick()` reaches the end of the message, AO2 switches to a one-shot `(c)` post-emote if it exists, then returns to `(a)` idle. If no `(c)` asset exists, it returns directly to `(a)` idle.
- AO2 starts `chat_tick()` immediately for non-empty messages, reveals text by grapheme, treats `{`/`}` as speed controls, handles escaped `\n`, `\p`, `\s`, and `\f`, delays punctuation, and gates blips through `blip_rate`, `blank_blip`, and anti-spam logic derived from `text_crawl / msg_delay`. Blip token fallback is `[Options] blips`, then `[Options] gender`, then `male`, with per-emote `[OptionsN]`/`[OptionsX]` overrides. The Oceanya viewport now follows those config-driven text/blip rules and triggers `\s` screenshake during the crawl.
- AO2 IC formatting is not plain text. `filter_ic_text()` removes alignment and speed-control tokens from display, converts escaped newline to a soft line break, suppresses escaped pause/shake/flash tokens, applies packet text color as the default color index, and applies `chat_config.ini` color-markup stack rules while preserving non-removed markup tokens.
- AO2 sends a blank/custom showname fallback from the selected INI puppet, not necessarily the locally iniswapped render character. Oceanya mirrors this for outgoing IC packets so a profile using an iniswap such as `VydValkKam` on the `Franziska` server slot sends `Von Karma` when the user left the IC showname field blank.
- Desk mod 5 (`DESK_PRE_ONLY_EX`) shows the desk during preanim, then hides desk/pair and centers the main character for the speaking phase.
- Chatbox font/image behavior is delegated to the existing AO2 chat preview resolver; placement is below the viewport in Oceanya.
- AO2 audio is volume-segmented: music, SFX, and blips are independently configurable and should only be heard in Oceanya when the viewport is actually visible. For parity work, the SFX bucket should include client chirps such as the bell/ding and feature-entry sounds, while viewport message playback should stay tied to the active incoming IC message stream rather than the selected GM UI state.
- The current viewport audio path is still a lightweight local playback bridge rather than a byte-for-byte reimplementation of AO2's full courtroom audio stack. It should be good enough for volume and visibility parity, but there may still be differences in long music looping, ambient channel mixing, and any blip behavior that depends on AO2 rich-text/markdown filtering beyond the current viewport crawl subset. Custom blip tokens now also resolve AO2-style relative paths rooted from `sounds/general`, including `../blips/...` fallback and character-relative custom paths.

## Current Coverage
Unit coverage exists for:
- background and desk asset resolution
- desk visibility decisions for common post-preanimation states
- preanimation-phase desk visibility decisions
- AO2 `design.ini` origin scaling
- effect layer metadata from `effects.ini`
- pair ordering suffix parsing
- shout overlay and zoom speedline names
- AO2 viewport chat geometry and `chat_config.ini` color-markup metadata

## Known Parity Gaps
- Preanimation timing now waits for animated playback completion first, matching AO2's receive-time `play_preanim(false)` behavior. `[stay_time]` fallback values use AO2's `time_mod=40`; `[Time]` remains only a static/unsupported-animation fallback, not a hard cutoff for normal animated GIF playback.
- Objection/shout overlays are displayed with animated frame playback when the asset format is supported, but their continuation timing is still approximated by a timer.
- Currently implemented viewport overlay/layer families: background, speedlines, main character, pair character, desk/position overlay, AO2 effect layer metadata, shout/objection bubble, and below-viewport chatbox. Still missing or incomplete compared to AO2: testimony overlay, WT/CE/verdict overlay, evidence presentation overlay, character sticker faces, chat arrow, realization flash overlay, background slide transitions, and exact AO2 queue/drain behavior around these overlays.
- Packet-level screenshake now matches AO2's main trigger points more closely: it respects `config.ini` `shake`, uses the AO2 300 ms / 20 ms shake window, triggers immediate shake only for AO2 idle/zoom speaking messages, triggers preanim shake at SFX time, and also responds to frame-triggered `_FrameScreenshake` entries.
- Frame-timed emote SFX are now driven from the resolved animation token and local/packet `_FrameSFX` data instead of only from the coarse packet `SoundT` delay, but realization flashes and a few edge-case animation paths still need parity work.
- Character, pair, effect, speedline, and shout images use the shared WPF animation player for GIF/APNG/WebP where supported. Viewport GIF playback bypasses preview frame caps; background and desk playback still use static placement.
- Character, pair, effect, speedline, and shout images use the shared WPF animation player for GIF/APNG/WebP where supported. Viewport GIF playback bypasses preview frame caps; background and desk playback still use static placement. The player now re-arms itself at the next due frame time instead of polling on a coarse fixed interval, which should reduce the slight slowdown versus AO2's own animation timers.
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
