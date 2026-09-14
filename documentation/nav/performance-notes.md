# Measured performance findings and their fixes

Navigation detail extracted from the `AGENTS.md` Repository Navigation Map.
Each section is the full note for one map row; the map keeps the short pointer.

## debug console freezes

**Also called:** "debug console freezes", "checkbox freezes UI", "server console popup lag", "category filter slow"

**Status:** Confirmed

`OceanyaClient/Components/Forms/DebugConsoleWindow.xaml.cs`. Every category toggle calls `RebuildDocument`, which used to add ~5000 entries x ~5 `Run`s to a LIVE `FlowDocument` one at a time - ~25000 layout invalidations. Now the document is detached (`ConsoleTextBox.Document = EmptyPlaceholderDocument`) and inlines collect in `_bulkInlineBuffer`, flushed with one `Inlines.AddRange`. Same batching via `AppendEntriesBatched` for the initial history load and for `ProcessPendingEntries` (log bursts). Timing line `[DEBUGCONSOLE-TIMING] rebuild entries=… rendered=… elapsedMs=…` under System. Do NOT reintroduce per-run `Inlines.Add` on an attached document.

## audio settings not applied at startup

**Also called:** "audio settings not applied at startup", "music plays at 0%", "volume only applies after moving the slider", "saved volume ignored"

**Status:** Confirmed

Two sources of truth had diverged: `SettingsWindow` read AND wrote the selected AO2 `config.ini` (`default_music`/`default_sfx`/`default_blip`, via `GetConfigPercentOrSavefile` + `SetPercent`), while playback (`AudioSettings.MusicVolume`/`SfxVolume`/`BlipVolume`) read ONLY `SaveFile.Data.Audio*Volume`, and nothing reconciled them at startup - so any change made outside our Settings window (AO2 itself, or hand-editing config.ini) left audio on the stale savefile value until a slider write updated both. `AudioSettings.ResolveVolume` now applies the SAME precedence as the Settings window: config.ini wins, savefile is the fallback. Reads go through `Ao2ConfigIniSettings.TryGetCachedInt` - a non-copying accessor added because `Load()` hands out a defensive dictionary copy, far too much allocation for a per-blip lookup. Because config.ini is only written on Save, unsaved slider drags publish through `AudioSettings.SetLivePreviewVolumes(...)` and are cleared by Save, Cancel, AND the settings-window `Closed` handler (title-bar X runs neither button). Test: `UnitTests/TestabilityHardeningTests.cs` `AudioSettings_ConfigIniVolumeWinsOverTheSavefile`.

## main emote grid lag

**Also called:** "main emote grid lag", "Battler Ushiromiya loads slowly", "large emote count UI lag", "virtualize emote buttons"

**Status:** Confirmed

Main GM emote selector is `OceanyaClient/Components/ICMessageSettings.xaml(.cs)` using `PageButtonGrid` and `ImageComboBox`. `ICMessageSettings.SetINI(...)` stores `Emote` models and calls `PageButtonGrid.SetVirtualizedItems(...)`; `CreateEmoteButton(...)` now creates only visible page buttons, preserving context menus/automation IDs. `ImageComboBox.SetItems(...)` bulk-loads emote dropdown rows and the shared `ImageComboBox` template enables recycling virtualization for all component uses.

## emote page per client

**Also called:** "emote page per client", "wrong emote page when switching", "emote grid empty"

**Status:** Confirmed

`PageButtonGrid.GetCurrentPage()`/`SetCurrentPage(int page)` (clamped to valid range); `ICMessageSettings.clientEmotePages` dict keyed by `AOClient` saves/restores page index. Saved before unsub in `SetClient`, restored after `SetINI`.

## message freeze

**Also called:** "message freeze", "IC send freezes", "message doesn't send", "must spam enter to send", "viewport desync", "sub-client viewports show different messages", "log shows message viewport skips", "sprite lags behind text", "single vs multi internal client", "GM rapid profile switch send"

**Status:** Confirmed

**Two GM modes** (field `useSingleInternalClient` in `MainWindow.xaml.cs`): SINGLE-internal-client = N lightweight profile `AOClient` objects (dict `clients`) share ONE real connection `singleInternalClient` (server sees one client, one area = one courtroom, AO2-like); MULTI-internal-client = each profile is its own connected bot (own connection/area/courtroom). AO2 reference (`AO2-Client/src/courtroom.cpp`): send is fire-and-forget, input box cleared only when the server echoes your MS# back matched by `char_id == m_cid`; exactly ONE viewport with ONE global FIFO `chatmessage_queue` playing messages strictly one-at-a-time (typing + `text_queue_timer` stay gate), only objections skip the queue. **Root causes of the "doesn't send" + desync (v7.10 regression from commit 118792e):** (1) echo-clear (`OnSendICMessage`→`OnICMessageReceivedHandler` in `MainWindow.xaml.cs`) matched `icMessage.CharId == singleInternalClient.iniPuppetID`, a SHARED mutable field that every `ApplyProfileToSingleInternalClient`/profile-swap overwrites — so after a swap the earlier send's echo never matched, box never cleared → looked unsent. (2) `AOClient.SelectIniPuppet` used one shared `pendingCharacterSelectionTcs`; two selections in flight on the shared connection let one PV# complete the other's TCS → timeout → real send aborted. (3) the single-client IC log handler used `Dispatcher.BeginInvoke` then resolved `targetClient` LATE and wrote `targetClient.iniPuppetID = puppetIdSnapshot`, so a swap between receipt and dispatch corrupted a profile's `iniPuppetID`; the viewport per-profile filter (`IsViewportMessageForProfile`) keys on that field → panes cross-rendered (desync). (4) per-profile viewport filtering fragments the one shared stream so a pane skipped messages the shared log showed. (5) text reveal wasn't gated on the character sprite decode → text appeared before the sprite. **Fixes (Phases 1-4a):** P1 `AOClient.SelectIniPuppet(int)` wraps the CC#/PV# exchange in a per-connection `SemaphoreSlim iniPuppetSelectionGate`; `MainWindow` send handler serializes single-mode prepare+send with `singleClientSendGate` and matches the echo against `sentCharIdSnapshot`. **IMPORTANT (box-not-clearing fix):** `sentCharIdSnapshot` MUST be the CharId that `SendICMessage` ACTUALLY placed in the packet — `SendICMessage` now returns that CharId (`Task<int>`) and the handler sets the snapshot from the return value, NOT from a value read before the send. `SendICMessage` calls `AlignIniPuppetWithCurrentCharacterIfAvailableAsync` which can change `iniPuppetID` (and thus `msg.CharId`) mid-send when `iniPuppetID` doesn't match `CurrentINI`; a pre-send snapshot would then be stale, the echo would arrive under the new CharId, the match would fail and the input box would never clear (symptom: "message sent but box not cleared"). That same realign does a CC#/PV# round trip whose PV# reply queues behind the inbound flood in the serial read loop — the source of the "press Enter, message appears seconds later" delay under a busy area, and it is inconsistent per-connection because whether `iniPuppetID` lines up with `CurrentINI` after connect varies. Diagnostic logging (category IC) added for user reports: realign-before-build (`SendICMessage`), CC#/PV# `confirmMs`/`gateWaitMs` timing (`SelectIniPuppet`), server-initiated INIPuppet change (`HandlePlayerVariablePacket`), and echo-text-matched-but-CharId-differs warning (send handler). To find the delay root from logs: look for INIPuppet realign/round-trip on EVERY send (means `iniPuppetID` keeps drifting from `CurrentINI` — check for server-initiated PV# changes). P2 `RefreshViewportAttachment` in single mode attaches ONE shared unfiltered viewport keyed on `singleInternalClient` (all profiles see the same courtroom; multi mode keeps per-profile filtered panes). P3 single-client IC handler snapshots the log/pair target at receipt, drops the harmful `iniPuppetID`/`curBG` writes, and uses multi-style self-detection (`clients.Values.Any(p.iniPuppetID==CharId)`); the IC/OOC log is already shared in single mode via `ResolveLogClientKey` mapping every profile → `singleInternalClient`. P4a (superseded — see below) initially gated the speaking text reveal on the sprite; this was WRONG per AO2 and reverted. **AO2 timing facts (verified from `AO2-Client/src/courtroom.cpp`): text is NOT gated on sprite decode — `chat_tick_timer->start(0)` types immediately while the sprite decodes async in parallel; there is NO post-send delay (only `chatRateLimit`≈300ms re-blocks the Enter key, the send hits the wire immediately); queue waits `stay_time` (200ms default) between messages even with a backlog and does not special-case your own echoed message.** So `RenderScene` now starts `StartChatTextReveal` immediately (sprite decodes in parallel, sprite-hold keeps the old frame so no transparent flash), `AOClient.SendICMessage` non-queued path has NO `Task.Delay` after `SendPacket` (the old 500ms, once serialized by `singleClientSendGate`, caused multi-second Enter→display lag under load), and the send handler's `ClearIcInputAndTransientEffects` uses non-blocking `Dispatcher.BeginInvoke` at `Send` priority so the read loop is not parked under a flood. P4b `AO2ViewportControl` now has an AO2-style FIFO chat queue (`chatMessageQueue`, `messageDisplayInProgress`, `queueAdvanceTimer`): `OnICMessageReceived`→`EnqueueChatMessage` plays one message at a time (never skipped); the next dequeues after the current finishes typing (`CompleteChatTextReveal`→`ScheduleQueueAdvance`) plus `stay_time` (`AO2ViewportAssetResolver.GetMessageStayMilliseconds`, config `stay_time` default 200ms). Blank/no-text messages (predicted by `!string.IsNullOrWhiteSpace(message.Message)`, same predicate as `showChat`) never call `CompleteChatTextReveal`, so `StartQueuedMessageDisplay` schedules their advance directly — this is the anti-stall guard. Objections/shouts (`ShoutModifier != Nothing`) flush the queue and play immediately (AO2 `skip_chatmessage_queue`). `ClearScene` calls `ResetMessageQueue` so a client switch drops stale queued messages. `PreviewMessage` still renders directly (bypasses the queue) for previews/tests. Trade-off: under sustained flood the queue paces to real-time via `stay_time` (lower drains faster) — AO2 parity, not "always latest". Config-cache in `Ao2ConfigIniSettings.Load` and background text-log writer in `Ao2TextLogWriter` remain (harmless perf, from 118792e). Tests: 655 pass incl. `UnitTests/ViewportParityTests.cs` `AO2ViewportControl_QueuesRapidMessages_AndObjectionSkipsQueue`; multi-client UI still best verified live / via opt-in FlaUI lanes.

## one client swap slow

**Also called:** "one client swap slow", "INI swap 2-3 second delay", "switching sub-clients slow with many characters", "character swap lag scales with character count"

**Status:** Confirmed

Root cause was `AOBot-Testing/Agents/AOClient.cs` `serverCharacterList` being a `Dictionary<string,bool>` used as if it were index-addressable via `Dictionary.ElementAt(i)`, which walks the dictionary from the start every call (O(n) per call). Two call sites looped `ElementAt(i)` inside a `for` over the full count — `AlignIniPuppetWithCurrentCharacterIfAvailableAsync` (runs on every `SendICMessage`, i.e. every IC send right after an INI/character swap) and `SelectFirstAvailableINIPuppet`'s `nameToIndex` build (runs on sub-client/profile selection and reconnect) — turning an O(n) scan into O(n²) against the server character list size (which scales with total mounted character count). Both run synchronously on the calling thread (no `await` inside the hot loop), so on a UI-thread call path this blocks the dispatcher, matching the "2-3 second freeze, worse with more characters" report. Fix: added `serverCharacterOrder` (`List<string>`) mirroring `serverCharacterList` insertion order, kept in sync at the only two population sites (`SC#` handler and `ApplyCharacterAvailabilityForTests`) and cleared in `ResetTransientServerState`; every `ElementAt(i)` on `serverCharacterList` was replaced with `serverCharacterOrder[i]`, turning all of these lookups back to O(1)/O(n) instead of O(n)/O(n²). No test coverage previously existed for character-list scale; all 654 existing `UnitTests` pass unchanged after the fix. **Switch timing instrumentation:** `MainWindow.SelectClient` times each phase via a local `Lap()` stopwatch and, when total ≥ `SwitchTimingLogThresholdMs` (default 4.0ms), logs `[SWITCH-TIMING] total=… | button=… applyProfile=… icSettings=… oocLog=… icLog=… viewport=… areaNav=… musicList=… dredd=… | client=… char=… mode=single/multi` at Info under `LogCategory.Viewport` (`[VPT]`, exportable — same filter as `[RENDER-TIMING]`). Use it to find which phase of a client/character switch freezes the UI (`icSettings`=`ICMessageSettings.SetClient`→`SetINI` emote/combobox load; `viewport`=`RefreshViewportAttachment`; `musicList`/`areaNav`=list refreshes) before drilling in. **Measured (2026-07-25): `icSettings` dominates every switch (~160-390ms of ~180-440ms total); `oocLog` secondary (~15-46ms).** So `ICMessageSettings.SetINI` is sub-instrumented too: `[SETINI-TIMING] total=… | prep=… dropdownSet=… gridSet=… emoteSelect=… soundList=… | char=… emotes=…` (Viewport category), splitting `EmoteDropdown.SetItems`, `EmoteGrid.SetVirtualizedItems`, `SelectEmote`+page, and `AO2SoundList.LoadEntries`+sfx fill. **Measured (2026-07-25): `emoteSelect` (50-114ms) and `gridSet` (25-62ms) dominated, NOT scaling with emote count → fixed per-switch work. Two causes fixed:** (1) the virtualized emote grid was built TWICE per switch — once at the old `EmoteGrid.SetVirtualizedItems` call in `SetINI` and again inside `SelectEmote` (which calls `SetVirtualizedItems` to reflect the selection); the `SetINI` build was removed (grid now built once, only in the `selectedEmote == null` fallback does `SetINI` build it directly). (2) `CreateEmoteButton` decoded each visible button's on/off images via `BitmapFileLoader.LoadFrozen` (which sets `IgnoreImageCache` → fresh disk decode every call), so switching back to a character re-decoded all its button art; now cached via `ICMessageSettings.GetCachedButtonImage` (bounded `EmoteButtonImageCache` keyed by path, validated by last-write-time; also caches `CreateDarkenedImage` results under a `path+"|dark"` key). `dropdownSet`≈0 (ImageComboBox well virtualized). `soundList` (`AO2SoundList.LoadEntries`, was ~15ms warm/82ms cold re-reading the character + shared base `soundlist.ini` from disk every switch) is now cached per file by last-write-time via `AO2SoundList.GetParsedFileCached`. After these, remaining `SetINI` cost is `emoteSelect` (the single virtualized grid build — ControlTemplate + per-button context menu), split in the log into `emoteSelect` (grid build) vs `pageNav` (`SetPageToVirtualizedItem`). **Launch→MainWindow lag** is separately covered by `StartupTimingLogger` (`OceanyaClient/StartupTimingLogger.cs`) → `%AppData%/OceanyaClient/startup_timing.log` (marks: `main_window_ctor_begin`, `main_window_initializecomponent_end`, `main_window_loaded`, `snapshot_restore_*`, `client_connect_*`).

## launch cost and memory growth

**Also called:** "RAM stacks up to 2gb", "memory keeps growing", "fuckton of CPU for a chatroom client", "window takes ages after clicking launch", "app gets slower the longer it runs"

**Status:** Confirmed (measured from `DEBUG.txt`, 2627 characters, tsuservercc)

Everything here came from one instrumented session; see [debug file logging](startup-updates-and-tests.md#debug-file-logging) for how to get another one.

**Measured launch breakdown (cold, click to usable ≈ 8.7 s):**

| phase | ms |
|---|---|
| launch click → window shown | 2080 |
| `CharacterFolder.FullList` cache load (26 MB JSON) | 708 (628 = `JsonSerializer.Deserialize`) |
| handshake timeout + reconnect after a `BD#` refusal | 4100 |
| rest of snapshot restore | ~2500 |
| **then, post-launch:** tracked-change scan | **16 346** |
| **and, concurrently:** music local-file scan (4448 tracks) | **4236** |

**Fixed in this pass:**

1. **Log flood.** 15 932 of 16 488 lines (96.6%) were `[ASSET-REFRESH]` per-character progress, three lines per character (`onParsedCharacter` and `onParsedCharacterProgress` both reported the same event, plus an integrity line). Each costs a string format, `Console.WriteLine`, `Debug.WriteLine`, an event dispatch and a file write - during the refresh it is reporting. `ReportRefreshProgress` now throttles to one line/second, `ReportRefreshMilestone` logs start/end unthrottled, and the duplicate callback is gone.
2. **Relaunch bypassed the startup gate.** `StartupCriticalPathGate` was a one-shot `TaskCompletionSource`, but closing the GM window reopens the config window IN THE SAME PROCESS. Every launch after the first saw a completed gate (`launch_waitform_hold_begin`/`end` 1 ms apart) and started its deferred asset work immediately - the full refresh began 635 ms BEFORE the connect finished. `ClientAssetRefreshService.BeginStartupCriticalPath()` re-arms it per launch, called from `ExecuteOkButtonClickAsync`.
3. **16.3 s tracked-change scan.** `ComputeDirectorySignature` did TWO full recursive walks per character (directories, then files) plus `new FileInfo(...)` and `File.GetLastWriteTimeUtc(...)` per file - two extra syscalls each, across every file of 2627 characters, every launch. Now one `EnumerateFileSystemInfos(AllDirectories)` walk using the size/timestamp already carried in the directory entry, and `CaptureTrackedFolderStates` hashes folders in parallel (mount-order filtering stays serial so duplicate-name priority is unchanged). `[ASSET-REFRESH] Asset state snapshot captured in Nms` reports the new cost.
4. **Animation frame cache had no byte budget.** `animCacheMB=305` from **two entries / 326 frames**; the limit was 96 *entries*, so it permitted gigabytes. Decoded `BitmapSource` buffers are UNMANAGED, which is why `managedMB` oscillated 86-398 while `privateMB` climbed 654 → 1404 - the GC neither sees nor reclaims them. `AnimationFrameCacheByteLimit` (192 MB) with insertion-order eviction and running byte accounting; an entry larger than the whole budget is never cached.
5. **`BD#` during handshake.** The server answered "Please wait before connecting another client" immediately; the client waited out the full handshake timeout anyway and then retried - 4.1 s spent waiting for a packet that was never coming, discarding the reason the server had already given. `WaitForPacketAsync` now throws `ServerRefusedConnectionException` on `BD#`; `MainWindow`'s connect retry handles it alongside the timeout case and surfaces the server's own wording.
6. **Music scan on the connect's disk.** `RefreshLocalMusicAssetsAndRefreshAsync` was kicked off from the snapshot restore's `SelectClient`. It now waits on `WaitForStartupCriticalPathAsync` like the asset scan; nothing needs it until the music list is opened.

**Verified after the fixes (second instrumented session, same install):**

| measurement | before | after |
|---|---|---|
| launch click → usable | 8674 ms | **3709 ms** |
| connect + snapshot restore | ~6600 ms | **1685 ms** |
| asset state snapshot (tracked-change scan) | 16 346 ms | **334 ms** |
| music local-file scan | 4236 ms | **575 ms** |
| handshake | 4100 ms timeout + retry | **701 ms**, no retry |
| log lines in a ~2 min session | 16 488 | **380** |
| animation frame cache | 305 MB, unbounded | **70 MB / 192 MB cap** |

The full asset refresh itself is still 18.2 s, but it is now entirely off the launch path (it starts after
`launch_waitform_hold_end`). Of that, ~17 s is `CharacterIntegrityVerifier` - parsing all 2627 characters
takes about 2 s. The verifier is the obvious next target for a full refresh: it is a correctness tool, not
something a refresh needs.

**Also found in that session (`example.com` placeholder asset URL, so every request was a miss):**
`WebCharacterIconResolver.PrefetchEmoteButtons` queued **376 web requests for one fully local character**
and re-queued them on every emote-grid rebuild - 227 HTTP attempts in ~40 s. It now skips any button whose
art already resolves to a local file and reports `skippedLocal` alongside `queued`.

**The index/parse split (done):** `CharacterFolder` used to conflate a cheap index with an expensive parse, so anything that only needed names forced all 2627 characters to be parsed and held resident - a 26 MB cache file, 677 ms of deserialize on the launch path, a permanently resident graph of ~100k `Emote` objects, AND a full 26 MB re-serialise on every single-character upsert (which is what the live watcher does per file change). It is now `CharacterIndexEntry` (name, paths, showname, options name, category, icon; persisted as a few hundred KB) plus an LRU of at most 128 parsed characters. `FullList` still exists but has only two production callers left - the Character Database Viewer and the AI bot's emote map - and re-parses the install on every call, so it must not be used anywhere else. Full write-up: `Documentation/LiveAssetLoading.md`.

**Other memory bounds added:** decoded-bitmap caches are all bounded by BYTES now, not entry count, because `BitmapSource` pixel buffers are unmanaged and the GC neither sees nor reclaims them - `Ao2AnimationPreview` 192 MB, `ICMessageSettings` emote buttons 64 MB, `CharacterSelectorWindow` icons 64 MB. Each reports `used/limit` in `[MEM]`.

## RAM jumps when a heavy character speaks

**Also called:** "ram spikes 300mb when I send a message", "heavy characters eat memory", "animCacheMB", "[ANIM-DECODE]"

**Status:** Confirmed (measured)

Viewport character sprites are streamed through `Ao2AnimationPreview.TryStreamWebPFrames` /
`TryStreamGifFrames` at **viewport height**, NOT at `MaxAnimatedPreviewDimension` - AO2 scales sprites by
viewport height and decoding smaller would visibly soften them. So a "heavy" character is genuinely large:
measured at **70 MB for one 75-frame animation** (~933 KB per frame, i.e. roughly 360x648 at 4 bytes/px),
and `animCacheMB` climbed 70 → 137 → 155 as more were played.

Two separate things hold that memory, and only one of them was a bug:

1. The **live player** (`BitmapFrameAnimationPlayer`) holds every frame while it is on screen, because a
   looping animation needs them. `Stop()` clears `frames`/`frameDurations`, and `AO2ViewportControl.StopAnimation`
   calls it, so this is released when the sprite changes. Not a leak.
2. The **shared `AnimationFrameCache`** retained them afterwards. It is bounded by bytes
   (`AnimationFrameCacheByteLimit`), but a single 70 MB animation could take a third of the budget and
   evict everything else - which is what "send one message, RAM jumps and stays" actually was.

Fix: budget lowered to 64 MB, plus a **per-animation admission cap** `MaxCachedAnimationBytes` (24 MB).
Anything larger still plays at full fidelity; it is just re-decoded next time instead of being retained.
Animations over `HeavyAnimationLogThresholdBytes` (8 MB) log `[ANIM-DECODE] <file> frames=N decodedMB=X
cached=yes/no path=...` under `LogCategory.Viewport`, so a heavy character can be identified by name.

Do NOT "fix" this by lowering the decode dimension - that is the sprite's real display resolution.

## where the unaccounted memory actually is

**Also called:** "privateMB is 1gb but the caches are empty", "unmanaged memory", "livePlayerMB", "235mb background"

**Status:** Confirmed (found by the `[ANIM-DECODE]` instrumentation)

After the caches were bounded, `privateMB` still sat at ~1.0 GB while `managedMB` was ~40 and every tracked
cache totalled ~21 MB. The `[ANIM-DECODE]` line (added to name heavy animations) answered it immediately:

```
judgestand.webp    frames=251  decodedMB=235   background/PLvsPWCourtroomLabyrinthiaFire
enter.webp         frames=147  decodedMB=137   characters/Atishon/anim/palanquin
startup.webp       frames=105  decodedMB=98    characters/AthenaSOJ/anim/def/matrix
witnessempty.webp  frames=75   decodedMB=70    background/PLvsPWCourtroomLabyrinthiaFire
```

These are **live animation players**, not caches. A looping animation needs every frame, so
`BitmapFrameAnimationPlayer`/`GifAnimationPlayer` hold them for as long as the animation is on screen - and a
BACKGROUND stays on screen for the whole session. That memory was invisible to every counter until
`AnimationMemoryTracker` was added; `[MEM]` now reports `livePlayers` and `livePlayerMB`.

Note the multiplier: 235 MB / 251 frames = ~937 KB per frame, which is a frame scaled to **viewport height**
and baked as a bitmap. The source webp is far smaller. AO2 keeps the source frame and lets the widget scale
at draw time; Oceanya bakes the scaled bitmap per frame, so memory scales with the square of the viewport
scale factor for no visual difference (WPF can scale on the GPU, and `RenderOptions.BitmapScalingMode`
reproduces the nearest-neighbour behaviour `ApplyAo2GifInterpolation` applies today). That is the next
optimisation, and it needs visual verification before it lands.

`enter.webp` also appears twice in one session: over `MaxCachedAnimationBytes` it is not retained, so it is
re-decoded on each use. That is the intended trade (memory over CPU), but it is why heavy characters cost
time as well as RAM.

## character database first-page loading

**Also called:** "character database takes ages to open", "wait form while the grid builds", "CHARDB-LOAD"

**Status:** Confirmed

`CharacterFolderVisualizerWindow.BuildCharacterItemsProgressivelyAsync` projects grid items in chunks:
`FirstVisibleChunkItemCount` (120) before the grid is shown, then `BackgroundChunkItemCount` (400) per batch
behind the user, yielding to `DispatcherPriority.Background` between batches.

Chunking is only correct because the ORDER is known without parsing - the grid sorts by name and
`CharacterFolder.Index` already has names - so the first chunk really is the first page. Items, order and
derived fields are identical to the old all-at-once build.

Gotcha: `allItems` is a plain `List<>` behind a `CollectionViewSource` default view, so appended batches need
`GetOrCreateItemsView().Refresh()`. Refresh resets scroll, so the offset is captured before and restored
after each batch - otherwise a batch landing mid-scroll yanks the user back to the top. The wait form closes
after the FIRST chunk, not at the end. Timing lands in the log as `[CHARDB-LOAD]`.
