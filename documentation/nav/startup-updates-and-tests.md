# Startup, save data, auto-update, asset refresh and test lanes

Navigation detail extracted from the `AGENTS.md` Repository Navigation Map.
Each section is the full note for one map row; the map keeps the short pointer.

## slow first launch

**Also called:** "slow first launch", "cold launch slow", "Creating window takes forever", "slow once per reboot then fast", "launch faster second time", "character cache slow", "startup stall", "startup_timing.log"

**Status:** Confirmed

Root cause was `CharacterFolder.BuildSourceSignature` (now deleted) doing ~1 filesystem stat per character on the cache-load critical path (`IsCacheCompatible`) — ~30s cold (latency-bound; parallelizing did NOT help), fast warm. Fix: removed the per-character `char.ini` timestamp signature from load (`IsCacheCompatible` in `AOBot-Testing/Structures/CharacterFolder.cs`) AND write (`SaveToJson`); only cheap `Version`/`ConfigPath`/`BaseFolders` checks remain. Per-character edits still caught off-critical-path by existing post-launch `ClientAssetRefreshService.GetTrackedChangePlanForCurrentEnvironment()` (fired from `InitialConfigurationWindow.HandleStartupFunctionalityReadyAsync`), which diffs `asset_refresh_marker.json` vs disk and refreshes live. Cache dir resolution unified in `Common/CacheEnvironment.GetCacheRoot()` (prod `%APPDATA%/OceanyaClient/cache` unchanged; dev `OceanyaClientDev/cache`; tests temp — fixes tests wiping prod cache). Stale per-hash cache files age-pruned (30d) via `Common/CacheFilePruner` in `CharacterFolder`/`Background`/`CharacterFolderVisualizerWindow`; persistent files (`character_folder_tags.json`, `asset_refresh_marker.json`) never pruned. `FullList` is lock-guarded. Timing instrumentation via `OceanyaClient/StartupTimingLogger.cs` → `%AppData%/OceanyaClient/startup_timing.log`; watch `compatibilityCheckMs` (~0 good, tens of seconds = a per-character disk scan regressed onto the load path). Also add Windows Defender exclusions for `%APPDATA%/OceanyaClient(Dev)`. Doc: `Documentation/CharacterCacheColdLaunch.md`.

## update old Oceanya client

**Also called:** "update old Oceanya client", "will savefile migrate", "user deleted old folder", "unit tests wiped production savefile", "Delete Savefile"

**Status:** Confirmed

`Common/SaveFile.cs` + `OceanyaClient/Components/Forms/InitialConfigurationWindow.xaml.cs` + `OceanyaClient/Components/Forms/AdvancedFeatureFlagsWindow.xaml(.cs)` + `OceanyaClient/Features/Startup/SaveFileDeletionService.cs` + `Documentation/SaveFileAndUpdatePersistence.md`; v6.1 and v6.2 use the same `%APPDATA%/OceanyaClient/savefile.json` path. Debug/dev launches use `%APPDATA%/OceanyaClientDev/savefile.json`; NUnit/testhost runs default to temp `OceanyaClientUnitTests`; `--test-savefile=...` is honored before first load. If saved `ConfigIniPath` points into a deleted release folder, startup checks the current app folder for `config.ini`, `<same parent>/config.ini`, and `base/config.ini`. Advanced feature flagging has a red danger-zone `Delete Savefile` button that validates the active path, stops File Hivemind first, unregisters Hivemind autostart, deletes the active save directory, then shuts down. Save-load diagnostics go beside the active savefile; unreadable saves are copied to `savefile.unreadable.<timestamp>.json` before falling back to defaults.

## auto updater

**Also called:** "auto updater", "GitHub Releases updater", "update popup", "Skip this version", "Update to vX.Y.Z", "test update channel"

**Status:** Confirmed

`OceanyaClient/Features/Updates/*` owns strict channel-aware version parsing, public GitHub release/manifest discovery, asset/hash validation, staging, and zip safety validation. `UpdateEnvironment` selects Stable for Release/public builds and Test for Debug/developer builds; Stable state/cache uses `%LOCALAPPDATA%/OceanyaClient/Updates`, Test uses `%LOCALAPPDATA%/OceanyaClientDev/Updates`, and `SaveFile.Data.Updater.Stable/Test` keep skip/last-seen state separate. `InitialConfigurationWindow.xaml(.cs)` starts the async check after load and shows the bottom-left gray update link. Popup: `OceanyaClient/Components/Forms/UpdateAvailableWindow.xaml(.cs)`. External replace-on-restart executable: `OceanyaUpdater/Program.cs`; packaged by `OceanyaHivemindAgent/OceanyaHivemindAgent.csproj`. Build outputs: Release `OceanyaClient/bin/Release/Github Release/Oceanya Client <version>/`; Debug `OceanyaClient/bin/Debug/Github Release Test/Oceanya Client <version>-test/`. Release manifest/checksum workflow: `.github/workflows/release.yml`. Tests: `UnitTests/AutoUpdaterTests.cs`.

## window built but empty on launch

**Also called:** "window built but empty on launch", "controls dont load for a few seconds", "launch stall sometimes", "snapshot restore slow", "connect slow on local server", "handshake timeout retry", "server didnt respond retrying", "dead first socket"

**Status:** Confirmed

Measured via `startup_timing.log` + `[HANDSHAKE-TIMING]` (Network category debug log): the GM window is `IsEnabled=false` for the whole snapshot restore (`MainWindow.RestoreGmMultiClientSnapshotAsync`), so its duration = how long the window looks built-but-empty. **Root cause (CONFIRMED, not the asset scan):** the internal client's FIRST WebSocket connection intermittently receives ZERO bytes — the server never sends its `decryptor#`/`ID#` greeting on it — so the handshake waits its full timeout (was 5s `decryptor/ID` + 10s `ID` = 15s), throws `TimeoutException "...handshake packet: ID"`, and `ConnectClientAsync` retries after 750ms; the fresh reconnect gets `decryptor#` in ~260ms. So ~16s wasted on a dead first socket, then instant success. NOT network (local `tsuservercc`), NOT the asset scan (deferring it — see gate below — did not help; connect was still 16s). AO2 reference (`AO2-Client`): protocol is "server greets first" (server sends `decryptor#` unprompted on connect; client sends `HI#` only in response — `packet_distribution.cpp`, `demoserver.cpp:88`), and AO2 has NO handshake timeout/retry (waits forever). So Oceanya's wait-first design is correct; sending `HI#` first would violate the protocol. **Fix (A+C):** (C) `AOClient.Connect`/`PerformHandshake` take `handshakeGreetingTimeoutMs`; `ConnectClientAsync` uses a SHORT first attempt (2000ms — recycles a dead socket fast) and a GENEROUS retry (10000ms — full tolerance for a genuinely slow-but-alive server, since a dead socket sends 0 bytes while a slow server still greets). Cuts the freeze from ~16s to ~3-5s. (A) the launch `WaitForm` is held open through the restore (`InitialConfigurationWindow.HandleStartupFunctionalityReadyAsync` awaits `ClientAssetRefreshService.WaitForStartupCriticalPathAsync` before closing it) and `RestoreGmMultiClientSnapshotAsync` sets its subtitle ("Connecting to server...") via `Dispatcher.BeginInvoke(Background)` so it wins over the launch flow's own subtitle; `ConnectClientAsync` updates it to "Server didn't respond — retrying..." on a handshake retry — so a slow/dead connection is visible, not a frozen empty window. Also kept (harmless): the earlier asset-scan deferral gate (`SignalStartupCriticalPathComplete`) so the scan does not run during the connect. Diagnostics: `[HANDSHAKE-TIMING] <phase>=Nms` per handshake packet wait (`AOClient.PerformHandshake`, Network cat); `connect_handshake_timeout_retry` startup mark. NOT-yet-fixed root cause: WHY the first socket gets no greeting (likely stale prior-session connection not reaped by the server, or server first-accept race) — option D would investigate `tsuserverCC` submodule; A+C mitigate it regardless.

## refresh assets warning

**Also called:** "refresh assets warning", "full asset refresh required", "config.ini path changed popup", "asset refresh marker", "unit tests make me refresh characters"

**Status:** Confirmed

`OceanyaClient/ClientAssetRefreshService.cs` owns forced-refresh reason detection and the active profile marker at `<directory of SaveFile.CurrentStoragePath>/cache/asset_refresh_marker.json` (production resolves to `%APPDATA%/OceanyaClient/cache/...`; unit tests resolve to their temp savefile directory). `InitialConfigurationWindow.BuildForcedRefreshPrompt` displays it at startup. Tests live in `UnitTests/GoogleDriveSyncTests.cs` under `ClientAssetRefreshServiceTests` plus marker-path isolation in `UnitTests/TestabilityHardeningTests.cs`.

## refresh all assets crash

**Also called:** "refresh all assets crashes", "client closes when I refresh assets", "crash after downloading a character then refreshing", "refresh kills the app"

**Status:** Confirmed

Structural, not incidental: the IC-panel and background context menus used to hand `CharacterContextMenuBuilder` a `Func<Task>` that fired a plain `Action` event and returned `Task.CompletedTask` immediately, so the builder's own try/catch had nothing to await. `MainWindow`'s handlers were `async () => await ...` lambdas attached to those `Action` events - i.e. async void - so ANY exception thrown during the refresh became an unhandled dispatcher exception and killed the process. The Character Folder Visualizer's copy of the same menu was never affected because it awaits `ClientAssetRefreshService` directly.

Fix has three layers, all required:
1. `ICMessageSettings.OnRefreshCharacterRequested` / `OnRefreshBackgroundRequested` / `OnRefreshAllAssetsRequested` / `OnRefreshAllCharactersRequested` are now `Func<Task>` / `Func<string, Task>` and their results are returned to the menu, which awaits them inside its own try/catch. The background entry in `BuildBackgroundContextMenu` awaits with its own try/catch because it is a raw `Click` handler, not a menu-builder item.
2. `ClientAssetRefreshService` no longer lets a single bad asset abort or escape: `RefreshAllCharacters`'s `Parallel.ForEach` wraps `CharacterIntegrityVerifier.RunAndPersist` per character (a folder still being written by web-asset materialization, an in-flight extraction, or a Drive sync throws there), and `RefreshAssets` collects per-entry failures via `RunCatalogRefresh` / `ThrowIfAnyRefreshFailed` so every other asset still refreshes and the user gets one aggregated message box. `MainWindow.RefreshCharacterAssetsAsync` rebinds clients in a `finally` so partial progress is still applied.
3. `CharacterFolder.FullList` hands out its live backing list to callers that enumerate outside the cache lock. `TryUpsertCharacterFolderInCache` / `TryRemoveCharacterFolderFromCache` used to `RemoveAll`/`Add` that instance in place, throwing "Collection was modified" in whichever reader was mid-enumeration - exactly what a streamed-character materialization racing a refresh produces. Both are now copy-on-write under `fullListLoadLock`, as is `RefreshCharacterList`'s publish.

Tests: `UnitTests/SmokeFixtureCharacterFolderTests.cs` `CharacterFolderUpsert_DoesNotMutateThePreviouslyHandedOutList`, `CharacterFolderRemoval_DoesNotMutateThePreviouslyHandedOutList`.

## debug file logging

**Also called:** "DEBUG.txt", "debug log file", "DebugHistory", "where are the logs", "send me the log", "memory sample", "[MEM] line"

**Status:** Confirmed

Full write-up: `Documentation/DebugFileLogging.md`.

Fast path: `Common/DebugFileLogger.cs`. `DEBUG.txt` next to the executable = the current session only; previous 5 sessions in `DebugHistory/debug_<timestamp>.txt`. Started in `App.OnStartup` (skipped in test mode), stopped in `App.OnExit`. Writes on a dedicated background thread fed by a `BlockingCollection` - never touch the disk from a log call, logging happens on the UI thread, the network read loop and the render path. The queue is recreated per session because `CompleteAdding` is permanent.

Diagnostics to look for in the file: `[STARTUP]` (mirrored `StartupTimingLogger` phases with abs/delta ms), `[MEM]` every 15s (working set, private, managed, GC heap, gen counts, threads, handles, plus per-cache occupancy registered by `App.RegisterDebugMemorySampleProviders`), `[ASSET-REFRESH]`, `[LIVE-ASSETS]`, `[HANDSHAKE-TIMING]`, `[SWITCH-TIMING]`, `[RENDER-TIMING]`, `[WEB-SUMMARY]`.

Related: `CustomConsole` used to hold up to 1,000,000 entries in TWO parallel collections (a formatted string and a `LogEntry` per line) trimmed with an O(n) `RemoveRange`; it is now one `Queue<LogEntry>` capped at `MaxStoredEntries` (25,000), because the full stream lives in `DEBUG.txt`. Tests: `UnitTests/DebugFileLoggerTests.cs`.

## live asset loading

**Also called:** "character I just downloaded does not show up", "dropped a zip in and nothing happened", "had to refresh assets to see it", "showed up way later", "real-time asset loading", "no more refreshing all assets", "refresh required popup gone"

**Status:** Confirmed

Full write-up: `Documentation/LiveAssetLoading.md` - read it before touching any of this.

Fast path: `OceanyaClient/Features/Assets/LiveAssetWatcher.cs` (debounced `FileSystemWatcher` per physical mount over `characters`/`background`/`misc`/`sounds/blips`, 1500 ms quiet period capped at 15 s, buffer overflow escalates to a full refresh, web mirror deliberately NOT watched), `ClientAssetRefreshService.AssetsRefreshed` (raised on a background thread after every refresh; `MainWindow.OnAssetsRefreshedInBackground` marshals + debounces 600 ms before rebinding clients and rebuilding the IC UI), and `CharacterFolder.ResolveOnDemand(name)` (probes the mounts once for a character missing from the cache, negative-cached 10 s, wired into `AO2ViewportAssetResolver.ResolveCharacter` and `AOClient.ResolveCharacterByName` so an unknown character renders on the very next message).

**Launch contention (regression, fixed):** a startup refresh must never be STARTED on the launch path, not even fire-and-forget. The scan is heavy single-threaded disk I/O over every character folder, and running it alongside window creation and the GM snapshot restore's server connect starves that connect - the exact contention `ClientAssetRefreshService.StartupCriticalPathGate` exists to prevent, seen as a window that stays empty for a long time after pressing Launch. `InitialConfigurationWindow` therefore records `pendingStartupFullAssetRefresh` and the deferred post-launch block (which runs after `WaitForStartupCriticalPathAsync`) owns both the full refresh and the tracked-change scan. Offline tools signal the gate themselves so the deferred work does not sit behind its timeout.

**The manual refresh checkbox is gone.** `InitialConfig.RefreshAssets` ("Refresh character and background info") was removed from `InitialConfigurationWindow.xaml`: every case its tooltip named (adding, removing, renaming asset folders) is now handled by the watcher, the tracked-change scan, the forced-refresh check, or the on-demand probe. `ShouldRunStartupAssetRefresh(forcedRefreshReason, skipAssetRefreshPrompts)` no longer takes a user-request flag. The explicit override still exists in-app as the character context menus' "Refresh All Assets" / "Refresh All Characters", which no longer block.

Behaviour changes to be aware of: `ClientAssetRefreshService.RefreshCharactersAndBackgroundsAsync` / `RefreshTargetedAssetsAsync` no longer show `WaitForm` (progress goes to the debug console as `[ASSET-REFRESH]`, `owner` parameters are retained but unused), and the startup "a full asset refresh is required, continue?" message box is gone - `InitialConfigurationWindow.ShouldRunStartupAssetRefresh(...)` decides silently and the refresh runs in the background while the client launches.

## File Hivemind

**Also called:** "File Hivemind", "background sync agent", "exit background sync", "stop Hivemind before deleting/updating"

**Status:** Confirmed

`OceanyaHivemindAgent/` plus launcher integration in `OceanyaClient`; stop signal name lives on `FileHivemindBackgroundAgentCommandLine.AgentStopSignalEventName`, stopped ack lives on `AgentStoppedSignalEventName`, and `FileHivemindAgentStopCoordinator` waits for confirmed stop before save-folder deletion. `OceanyaUpdater/Program.cs` sends the same stop signal and now also waits for the stopped ack before replacing files, with force-stop only as updater fallback.

## UI automation

**Also called:** "UI automation", "FlaUI", "desktop smoke tests", "UI tests failing", "release gate", "ui-smoke.yml", "ui-online.yml"

**Status:** Confirmed

`UiAutomationTests/`; opt-in only, Windows interactive desktop required, run categories sequentially. Fixture args live in `SmokeFixturePaths` / `OnlineFixturePaths` and disable GM snapshot + viewport persistence for isolation. `SmokeFixturePaths.AppExePath` resolves the exe via `OCEANYA_TEST_APP_EXE` env override, then probes `bin/Debug` then `bin/Release` (`net8.0-windows`), so Smoke can run against a Release build. Release gate wiring: `release.yml` runs UnitTests **and** `Category=Smoke` (per channel; `OCEANYA_TEST_APP_EXE` points Smoke at that channel's exe) before packaging; `ui-smoke.yml` auto-runs Smoke on push→main; `ui-online.yml` is manual/self-hosted only. **`Category=Smoke` = 12/12 green.** Two GmPacket parity anchors that broke on UI drift were fixed: (1) `InitialConfig.SelectedServerCombo` items now set `AutomationProperties.Name="{Binding Name}"` in `ServerHistoryComboBoxItemStyle` (was reading `ToString()` = type name) — also a real screen-reader fix; (2) the in-test loopback servers were stale vs the current handshake contract. `GmPacketLoopbackServer` and `OnlineLaneTests.RunServerAsync` now send `PV#<conn>#CID#<charId>#%` after `CC#` (client gates IC sends on `iniPuppetID >= 0`, confirmed only by `PV#`) and `CharsCheck#`; `OnlineLaneTests.RunServerAsync` now **accepts connections in a loop** (was single-accept) so the client's transient first-attempt-then-retry connection does not starve the real connection. `Category=Online` (incl `GmPacket`) went 0/15 → **15/15 green**. The last 2 were corrected test-side (not app bugs): GM client IC `ShowName` assertions now expect the character's configured showname (`ResolveShowNameForPacket` falls back to it when `ICShowname` is unset — AO2-correct), and connection assertions now check *distinct* connections (`ConnectionId > 0`, first != second) instead of exact accept-order ids. The client's first handshake attempt transiently retries and opens an extra loopback connection (why exact ids were unstable); treated as a test-lab artifact — production v7.9 connects to real servers fine.

## empty window while launching

**Also called:** "shows the background main window completely empty", "broken state on launch", "ghost/see-through window on launch", "startup_window_revealed"

**Status:** Confirmed (verified by driving the app and screenshotting the launch)

`InitialConfigurationWindow` shows the launched window at `Opacity = 0` and calls
`RevealStartupWindowAsync()` once the launch-critical path is done, before the wait form closes. The window
must be SHOWN for WPF to raise Loaded, and Loaded is what starts the GM snapshot restore, so it cannot just
be created hidden. `startupRevealSafetyTimer` (`StartupRevealSafetySeconds`, 45 s) guarantees it can never
stay invisible, and the error and closed paths reveal too.

**The ordering trap:** the readiness callback fires RE-ENTRANTLY from inside `Show()` - Loaded runs the
restore, the restore signals the critical path, and the ready handler runs, all before `Show()` returns.
Setting `Opacity = 1` there produced a ghost: chrome painted, body not yet rendered, desktop visible through
it. Measured as `startup_window_revealed` at 104992 ms against `startup_window_show_end` at 105139 ms.

The reveal is therefore posted at `DispatcherPriority.ContextIdle`, which is a LOWER priority than the
`DispatcherPriority.Loaded` that `MainWindow.RevealSurfaceAfterFirstLayout` uses for `MainCanvas`, so it
always runs after both that reveal and the render pass. Verified: `show_end` 10137 → `revealed` 10560, and
on a profile with a saved GM snapshot the first frame the window is on screen already has the client
restored, the server connected, the area list, the OOC log and the viewport rendered.

Do not "simplify" this back to setting `Opacity` inline - that is the ghost-window bug.

## X needs two presses / empty client list on launch

**Also called:** "have to hit X twice to close the client", "closing reopens the launcher", "no clients when I open the client", "auto add client"

**Status:** Confirmed (verified by driving the app)

**Two presses:** not a hang. `HandleStartupFunctionalityClosedAsync` called `ReopenConfigurationWindow()` for
EVERY launched tool, so closing the GM client brought the launcher back and a second press on the launcher
was what actually exited. Confirmed by enumerating top-level windows after the close: the process was alive
with `Initial Configuration` on screen (`offscreen=False`).

Closing now exits outright when `selectedFunctionality.RequiresServerEndpoint` (GM Multi-Client and the AI
Bot) via `Application.Current.Shutdown()`. The three offline tools (Character Database Viewer, File Creator,
File Hivemind) still return to the launcher, since switching between those without relaunching the exe is
what it is for. Verified: one `CloseMainWindow()` and the process is gone inside a second.

Separately, `MainWindow.HookHostWindowClosing`'s handler cancels the close, runs an async shutdown, then
calls `Close()` again. Disabling the window was the only feedback, so a slow disconnect looked like the X had
done nothing. It now hides the host window immediately, always closes even if the disconnect throws, and logs
`[SHUTDOWN] clean close took Nms` (measured 58 ms - the disconnect was never the slow part).

**Empty client list:** `MainWindow.AddFirstClientIfNoneRestored` opens the add-client flow when the snapshot
restored no clients, so the window is never usable-but-empty. It connects one client and still lets the user
pick the INI puppet. Posted at `DispatcherPriority.ApplicationIdle`, deliberately a LOWER priority than the
`ContextIdle` the launch flow reveals the window at, so the character selector never opens over a window the
user cannot see yet. Guarded by `hasAutoAddedFirstClient` so it can only fire once.
