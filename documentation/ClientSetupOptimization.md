# Client Setup Optimization

**Goal**: Make Oceanya feel faster to start than a bare AO2 installation.  
**Status**: Phase 1 complete. Phase 2 (connection speed) and Phase 3 (advanced caching) still open.

---

## How to read the timing log

Every launch writes `%AppData%\OceanyaClient\startup_timing.log`. It records each phase with absolute and delta milliseconds from app start:

```
=== Oceanya Startup Timing ===
Session: 2025-05-10 10:23:45.000 UTC
App version: 6.0.0

Phase                                 | Abs(ms) | Delta(ms)
---
app_startup_begin                     |       0 |        -
fake_loading_begin                    |      15 |       15
fake_loading_end                      |    7347 |     7332
initial_config_window_shown           |    7520 |      173
launch_clicked                        |   10234 |     2714
preflight_check_done (ok)             |   10256 |       22
main_window_creating                  |   10258 |        2
main_window_loaded                    |   10789 |      531
finished_loading                      |   13003 |     2214
background_tracked_check_begin        |   13004 |        1
background_tracked_check_end (ok)     |   13568 |      564
snapshot_restore_complete (clients=3) |   15211 |     1643

=== Summary ===
App startup → last event:    15211ms (15.2s)
launch_clicked → finished_loading: 2769ms (2.8s)
Fake loading duration: 7332ms
Background asset check: 564ms
```

Key metric to track: **launch\_clicked → finished\_loading**. That is the time the user perceives as "waiting for the app after clicking Launch". The goal is to get it below AO2's equivalent (~1–2 s on a bare install).

---

## Phase 1 — Completed (this PR)

### 1. Skip intro loading screen checkbox

**Problem**: `FakeLoadingAsync()` in `App.xaml.cs` runs 2–9 random steps × 500–1200 ms each, plus a 600 ms "Loading complete!" pause and 800 ms fade-out wait. Total: **1–11+ seconds of pure delay** before the user sees anything.

**Fix**: Added `SaveData.SkipLoadingScreen` bool (default `false`) and a checkbox in `InitialConfigurationWindow` labelled **"Skip intro loading screen"**. When checked and saved via the Launch button, the fake loading phase is bypassed entirely on subsequent starts.

Relevant files: `Common/SaveFile.cs`, `App.xaml.cs`, `InitialConfigurationWindow.xaml/.cs`

---

### 2. Defer asset change scan off the launch critical path

**Problem**: On every launch (even when nothing changed), `ExecuteOkButtonClickAsync` ran `GetTrackedChangePlanForCurrentEnvironment()` which calls `CaptureCurrentAssetStateSnapshot()`. That method recursively enumerates every file in every character and background folder and calls `FileInfo` + `File.GetLastWriteTimeUtc` for each file. On a large AO install (500+ characters) this blocked the "Checking startup requirements…" WaitForm for **2–10+ seconds** before the window even appeared.

**Fix**: Removed the blocking scan from the launch critical path. The preflight step now only:
1. `Globals.UpdateConfigINI` — reads config.ini (~5 ms)
2. `GetRefreshRequirementReasonForCurrentEnvironment` — reads one JSON marker file (~5 ms)

The full `GetTrackedChangePlanForCurrentEnvironment()` now runs **in background after `FinishedLoading`** fires. If it finds changed assets it calls `RefreshTargetedAssetsInBackgroundAsync` silently without a dialog. The user never waits for this.

**Trade-off removed**: Previously there was a "Asset files changed, refresh only affected items?" Yes/No dialog. This dialog is gone — targeted refresh now always happens silently in background. Forced full-refresh detection (version change, config path change, etc.) still prompts as before.

Relevant files: `InitialConfigurationWindow.xaml.cs`

---

### 3. Better WaitForm subtitles during preflight

WaitForm subtitles now say what is actually happening:
- `"Loading configuration..."` while reading `config.ini`
- `"Verifying asset cache..."` while reading the refresh marker

---

### 4. Startup timing log

New `StartupTimingLogger` (`OceanyaClient/StartupTimingLogger.cs`) records wall-clock timestamps for key phases and writes them to `%AppData%\OceanyaClient\startup_timing.log` at `finished_loading` and after the background check completes. Use this to find bottlenecks on any machine.

---

## Remaining bottlenecks (Phase 2+)

Based on what the timing log reveals, the remaining time after Phase 1 comes from:

### A. XAML / WPF initialization (~200–600 ms)
`MainWindow.InitializeComponent()` parses the full XAML tree, binds templates, and instantiates all controls. This is unavoidable but can be reduced by lazy-loading heavy panels (emote grid, IC settings, OOC log) that don't need to exist at startup.

**Approach**: Use `x:Load` or `Visibility.Collapsed` to defer construction of panels the user hasn't switched to yet. Start with the emote grid and IC message settings control as they appear to be the heaviest.

### B. Snapshot restore — sequential client connections (~1–3 s per client)
`RestoreGmMultiClientSnapshotAsync` connects clients one at a time. Each connection does:
1. TCP/WebSocket dial (~50–300 ms depending on network)
2. AO2 handshake (server sends character list, area list, etc.)
3. Character selection
4. Local state application

With 3 clients this is 3–9 s. With 6 clients it can hit 15+ s.

**Approaches**:
- **Parallel first-handshake**: Connect all clients simultaneously in `Task.WhenAll` instead of sequentially. Requires making `AddClientInternalAsync` safe to call in parallel (verify no shared mutable state issues first).
- **Reconnect cache**: Store the server character list from the previous session so the first `ConnectClientAsync` call can skip waiting for the server's `SC#` packet and proceed immediately using the cached list. Validate against the live list in the background.

### C. Config.ini parse + base folder resolution (~10–50 ms, WSL overhead)
`Globals.UpdateConfigINI` re-reads config.ini and resolves mount paths with `Directory.Exists` on each path. On WSL2 accessing Windows filesystem paths, each `Directory.Exists` call carries ~2–5 ms overhead. With many mount paths this adds up.

**Approach**: Cache the resolved base folders in memory after the first successful parse. Only re-parse on explicit refresh or when the config path changes.

### D. Background tracked-change scan (~200 ms – 5 s)
This is now off the critical path (Phase 1) but still runs after launch. On a 500-character install it can take 1–3 s in the background. To speed it up:

**Approach A – Directory mtime fast filter**: Add `DirectoryMtimeTicks` to `AssetTrackedFolderState`. In `CaptureCurrentAssetStateSnapshot`, skip computing the full SHA-256 signature for a folder if its top-level `LastWriteTimeUtc` hasn't changed (valid on NTFS for direct file changes; file-in-subdir changes update the immediate parent dir).

**Approach B – Parallel enumeration**: The `Parallel.ForEach` in `RefreshAllCharacters` already uses `AssetRefreshParallelism.GetDegreeOfParallelism`. Apply the same pattern to `CaptureTrackedFolderStates` so the per-folder `ComputeDirectorySignature` calls run concurrently.

---

## Target benchmarks

| Metric | Before Phase 1 | After Phase 1 | Target |
|---|---|---|---|
| App start → InitialConfig visible | 1–11 s | <0.2 s (skip) | <0.2 s |
| launch\_clicked → finished\_loading | 3–15 s | ~0.5–1.5 s | <1 s |
| Snapshot restore (3 clients) | 3–9 s | 3–9 s (unchanged) | <2 s |
| Background asset check | n/a (was blocking) | 0.2–5 s (background) | <0.5 s |

To continue this work: run a session with real AO assets, look at the timing log, and pick the next largest delta.
