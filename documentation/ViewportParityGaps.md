# Viewport Rendering Parity Gaps

**Generated:** 2026-05-14  
**Last updated:** 2026-05-14 (gaps 2–8 implemented; gap 9 verified not a gap)  
**Branch:** release/6-2  
**Scope:** Visual and audio rendering only — can OceanyaClient display incoming packets identically to AO2's viewport?

This document covers cases where a server or another client sends a packet that OceanyaClient's viewport renders differently from AO2's reference client, or doesn't render at all. It does **not** cover UI features the user can't activate (those are in `AO2ParityGaps.md`).

The following were audited and confirmed **working correctly** before this document was written:
- All 6 DeskMod states (visibility and desk show/hide timing)
- All EmoteModifier values 0–6 (idle / preanim / zoom / preanim+zoom)
- All text escape sequences (`\n`, `\p`, `\s`, `\f`)
- Text speed tokens `{`/`}` with all 7 multiplier levels
- Text color markup and `remove` flag from `chat_config.ini`
- Text alignment prefixes (`~~`, `~>`, `<>`)
- Additive mode
- Screenshake affecting all 4 layers (background, character, pair, chatbox)
- Frame-based effects (`FramesShake`, `FramesRealization`, `FramesSfx`)
- Post-animation `(c)` emote after text reveal
- Panoramic backgrounds with origin-based panning
- Character-specific shout sounds from character folder
- Per-character `chat_config.ini` colors loaded and applied
- Music fade-in / fade-out / sync-position effects
- Evidence left/right positioning (`def` + `hlp` → right, all others → left)

---

## Summary Table

| # | Gap | Priority | Status |
|---|-----|----------|--------|
| 1 | HP penalty bars not rendered | **High** | Open — `AOClient.cs` has no HP# handler |
| 2 | Ambient music channels (>0) silently dropped | **High** | **Implemented** |
| 3 | Music loop regions (loop_start/loop_end) not read | Medium | **Implemented** |
| 4 | No audio plays with WTCE/testimony/verdict overlays | Medium | **Implemented** |
| 5 | Verdict overlay display duration wrong (1500ms vs 3000ms) | Medium | **Implemented** |
| 6 | Effects `cull` property not parsed | Low | **Implemented** |
| 7 | Effects `max_duration` property not parsed | Low | **Implemented** |
| 8 | Per-color talking flag ignored (hardcoded Blue only) | Low | **Implemented** |
| 9 | DeskMods 4/5 EX variants hide pair; AO2 keeps pair visible | Low | **Not a gap** — verified against AO2 source; behavior already correct |

---

## 1. HP Penalty Bars Not Rendered

### What AO2 Does
Two horizontal bars — one for the defense side, one for the prosecution — are displayed in the courtroom viewport. The server sends `HP#type&value#%` (type 1 = defense, type 2 = prosecution, value 0–10) whenever a judge or moderator changes the bar. Each state maps to a distinct image (`defensebar0.png` through `defensebar10.png`). When a bar value increases, AO2 also plays a configurable SFX token (`hp_increased_sfx`) and fires an effect (`hp_increased_effect`, e.g., screenshake or flash); decreases play their own SFX/effect.

### What Oceanya Does
Nothing. `AOClient.cs` has no handler for `HP#`. The `OceanyaClient/Features/Viewport/` directory has no HP bar elements. If a judge/mod sends HP# updates during a session, Oceanya users see no visual change.

### How to Fix

**1. Parse HP# in AOClient:**
Add a case for `"HP"` in the packet dispatcher in `AOBot-Testing/Agents/AOClient.cs`. Parse `type` (int, 1 or 2) and `value` (int, 0–10). Add `OnHpChanged` event: `Action<int type, int value>?`.

**2. Add bar elements to viewport:**
In `AO2ViewportControl.xaml`, add two `Image` elements (`DefenseBarImage`, `ProsecutionBarImage`) — suggest placing them above the chatbox area. Dimensions and position should be read from `courtroom_design.ini` (keys `hp_defense` and `hp_prosecution`).

**3. Resolve bar assets:**
In `AO2ViewportAssetResolver.cs`, add `ResolveHpBarImage(bool defense, int value)` → looks up `defensebar{value}` or `prosecutionbar{value}` from the background's misc folder (same chain as other overlay assets).

**4. Handle HP change effects:**
On value change, read `hp_increased_sfx` / `hp_decreased_sfx` from `courtroom_config.ini` and play via `audioManager.PlaySfx(token)`. Read `hp_increased_effect` / `hp_decreased_effect` and fire the appropriate effect (screenshake/flash) through the existing `StartScreenShake`/`DoFlash` paths.

**5. Wire in MainWindow:**
Subscribe to `OnHpChanged` and forward to `viewportWindow?.UpdateHpBar(type, value)` / `mainViewport.UpdateHpBar(type, value)`.

---

## 2. Ambient Music Channels (>0) Silently Dropped

**Status: Implemented.**

`AO2ViewportAudioManager` now holds a `Dictionary<int, AO2BlipPreviewPlayer> ambientPlayers` keyed by channel number (1+). `PlayAmbientMusic(channel, songPath, loop)` creates per-channel players on demand. `StopAll()` now calls `StopAllAmbient()`. Both the viewport's `OnMusicChanged` and `MainWindow.xaml.cs`'s `OnMusicChanged` route channel-1+ packets to `PlayAmbientMusic` instead of dropping them. Channel 0 (master music) continues to update the music label UI; ambient channels do not.

---

## 3. Music Loop Regions (loop_start / loop_end) Not Read

**Status: Implemented.**

`AO2ViewportAudioResolver.ParseMusicLoopSidecar(resolvedPath)` now reads `<audioPath>.txt` (e.g. `trial.mp3.txt`) and parses `loop_start`, `loop_end`/`loop_length`, and `seconds` keys. `AO2BlipPreviewPlayer.ApplyLoopRegion(startBytes, endBytes)` registers a `BASS_SYNC_POS | BASS_SYNC_MIXTIME` callback that seeks the stream back to `startBytes` when playback reaches `endBytes`. `AO2BlipPreviewPlayer.ConvertLoopValueToBytes(isSeconds, value)` converts from sample frames (AO2 parity: `frames × 4` for stereo 16-bit) or seconds via `Bass.ChannelSeconds2Bytes`. `AO2ViewportAudioManager.PlayMusic` calls these after a successful `TrySetBlip`. When no sidecar exists, the full-file `BassFlags.Loop` path is unchanged.

---

## 4. No Audio Plays With WTCE/Testimony/Verdict Overlays

**Status: Implemented.**

`AO2ViewportAudioManager.PlayCourtSfx(key)` resolves via `AO2ViewportAudioResolver.ResolveCourtSfxPath(key)` (reads `courtroom_sounds.ini`, falls back to `sounds/general/`). `ShowTestimonyOverlay` now calls `audioManager.PlayCourtSfx("testimony1")`. `ShowWtceOverlay` maps its `assetStem` parameter to the token: `crossexamination_bubble` → `testimony2`, `notguilty_bubble` → `notguilty`, `guilty_bubble` → `guilty`.

---

## 5. Verdict Overlay Display Duration Wrong (1500ms vs 3000ms)

**Status: Implemented.**

`ShowWtceOverlay` now accepts an optional `TimeSpan? staticDuration` parameter (default null → 1500ms). `HandleRtPacket` passes `TimeSpan.FromMilliseconds(3000)` for the `judgeruling` case. The 1500ms fallback remains for cross-examination and other WTCE calls.

---

## 6. Effects `cull` Property Not Parsed

**Status: Implemented.**

`ViewportEffect` record now includes `bool Cull = false`. `ResolveEffect` reads `cull` from `effects.ini` via `IsTrue(GetProperty(properties, "cull"))`. In `RenderEffect`, if `Cull=true` and `EffectImage` is currently visible, `StopAnimation` + `Visibility = Collapsed` runs before `SetAnimatedImage` so the previous effect is cancelled before the new one starts.

---

## 7. Effects `max_duration` Property Not Parsed

**Status: Implemented.**

`ViewportEffect` record now includes `int? MaxDurationMs = null`. `ResolveEffect` reads `max_duration` from `effects.ini` and parses it as a positive int. In `RenderEffect`, when `MaxDurationMs.HasValue && Loop`, a `DispatcherTimer` fires after that many milliseconds and calls `StopAnimation(EffectImage)` — guarded by a captured `messageSequence` so late-firing timers from prior messages are no-ops.

---

## 8. Per-Color Talking Flag Hardcoded to Blue Only

**Status: Implemented.**

`RenderScene` now resolves `AO2ChatPreviewStyle` via `AO2ChatPreviewResolver.Resolve(characterChatToken, hasShowname, preferViewportTheme: true)` before computing `useTalkingSprite`, then uses `talkingStyle.ChatMarkupTalking[(int)message.TextColor]` instead of the old hardcoded `IsTextColorTalking`. Per-character `c{i}_talking=0` overrides in `chat_config.ini` now propagate to the talking-sprite decision.

---

## 9. DeskMods 4/5 — Pair Character Wrongly Hidden

**Status: Not a gap — verified correct.**

A full reading of `AO2-Client/src/courtroom.cpp` (DeskMod switch blocks at lines 3996–4073) confirmed that Oceanya's behavior already matches AO2:
- DeskMod 4 preanim: pair hidden, char centered → `ShouldCenterAndHidePairDuringPreAnimation` returns true for value 4 ✓
- DeskMod 4 speaking: pair visible, SELF_OFFSET applied → `ShouldCenterAndHidePairDuringSpeaking` does NOT return true for value 4 ✓
- DeskMod 5 preanim: pair visible → `ShouldCenterAndHidePairDuringPreAnimation` does NOT return true for value 5 ✓
- DeskMod 5 speaking: pair hidden, char centered → `ShouldCenterAndHidePairDuringSpeaking` returns true for value 5 ✓

The original gap description was based on a misread of the enum comments. No code change needed.
