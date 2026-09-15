# Debug File Logging

`DEBUG.txt` next to the executable always holds **exactly the current session**. The previous five
sessions are rotated into `DebugHistory/`.

## How to use it

1. Run the client and reproduce whatever is being investigated.
2. Close it (or leave it running - the file is flushed per line).
3. Read `<app folder>/DEBUG.txt`. Older sessions are `<app folder>/DebugHistory/debug_<timestamp>.txt`.

In a development build that is:

```
OceanyaClient/bin/Debug/net8.0-windows/DEBUG.txt
OceanyaClient/bin/Debug/net8.0-windows/DebugHistory/
```

## What is in it

Everything the in-app debug console shows, plus:

- **Session header** - app version, OS, runtime, base directory, command line.
- **`[STARTUP]` lines** - every `StartupTimingLogger` phase with absolute and delta milliseconds, mirrored
  into the log as it happens. (`%AppData%/OceanyaClient/startup_timing.log` still gets the standalone
  table.)
- **`[MEM]` samples every 15 seconds**, plus one at session end:
  `workingSetMB`, `privateMB`, `managedMB`, `gcHeapMB`, gen0/1/2 collection counts, thread and handle
  counts, and the per-cache occupancy counters registered by
  `App.RegisterDebugMemorySampleProviders()`:
  - `logEntries` - in-memory console entries
  - `cachedCharacters` - parsed characters held by `CharacterFolder`
  - `animCacheEntries` / `animCacheFrames` / `animCacheMB` - decoded animation frames
    (`Ao2AnimationPreview`)
  - `staticPreviewEntries` / `staticPreviewAlive` - weak-referenced static previews
  - `emoteButtonCacheEntries` / `emoteButtonCacheMB` - IC emote button bitmaps
  - `charIconCacheEntries` / `charIconCacheMB` - character selector icons
  - `imageSizeCache` / `designIniCache` / `positionCache` - viewport resolver caches
  - `openWindows`
- **`[ASSET-REFRESH]`** - asset refresh progress, which no longer has a wait form to display it.
- The existing tagged streams: `[HANDSHAKE-TIMING]`, `[SWITCH-TIMING]`, `[RENDER-TIMING]`,
  `[LIVE-ASSETS]`, `[WEB-SUMMARY]`, `[AUDIO]`.

## Designed to be sent to you after something breaks

The goal is that a user can hand over `DEBUG.txt` and it answers "what were they running and what
happened" without a follow-up interrogation.

- **Session header** - app version, **build type (Debug/Release)**, command line, OS, runtime, cores, total
  RAM, server GC, culture, **WPF render tier** (0 = software, 1 = partial GPU, 2 = full GPU), primary and
  virtual screen size, savefile path plus whether it is the development profile, the savefile load
  diagnostic, `config.ini` path, and the full mount list.
- **`[LAUNCH-CONFIG]` block** (`Features/Startup/LaunchConfigurationReport.cs`) - written once per launch:
  functionality, server, single-vs-multi internal client, skip-loading-screen, OOC name, showname/iniswap/
  sticky-effect toggles, IC log inversion and cap, UI scale mode and factor, viewport and panel placement
  toggles, audio volumes and music effect flags, the saved GM snapshot (client names and INI puppets),
  enabled advanced feature flags, and indexed/parsed character counts.
- **`icQueues=` in every `[MEM]` sample** - per viewport pane: `queued`, `busy`, `advanceTimer`,
  `sinceLastAdvanceMs`. `busy=true` with a rising `queued` IS the "IC is frozen" state.
- **`[IC-QUEUE-STALL]`** - the chat queue watchdog fired, naming the character, emote, preanim, text length
  and queue depth of the message that wedged it.

**Secrets are deliberately excluded**: no Google Drive tokens, no API keys, no credential paths. Only
settings that change behaviour.

`CustomConsole.Debug` is no longer `#if DEBUG`. It is a runtime switch
(`CustomConsole.IsDebugLoggingEnabled`), because a log collected from a user on a release build was
previously missing ~30 diagnostic sites - including `[WEB-GRACE]`, which sits directly on the chat-queue
path.

## Implementation

`Common/DebugFileLogger.cs`.

- Subscribes to `CustomConsole.OnWriteLine` and pushes formatted lines onto a `BlockingCollection`; a
  dedicated background thread does the file writing. Logging happens on the UI thread, the network read
  loop and the viewport render path, so a log call must never touch the disk on the calling thread.
- `Start(logDirectory, sessionHeaderLines)` rotates the previous `DEBUG.txt` into
  `DebugHistory/debug_<lastWriteTime>.txt`, prunes to `MaxHistoryFiles` (5), and opens a fresh file.
  Called from `App.OnStartup`, skipped in test mode.
- `Stop()` writes a final `[MEM]` sample and a session-end marker, then drains and closes. Called from
  `App.OnExit`.
- Hard cap `MaxLiveLogBytes` (256 MB) on the live file; lines past it are dropped with a marker so a
  runaway session cannot fill the disk.
- `MemorySampleProviders` is the extension point for counters this assembly cannot see. A provider that
  throws is dropped rather than breaking the sample.
- Every failure path is swallowed: logging must never affect app behaviour.

## Related memory change

`CustomConsole` used to keep up to **1,000,000 entries in two parallel collections** - a formatted string
*and* a `LogEntry` record per line - trimmed with `RemoveRange(0, n)` (an O(n) copy of the whole buffer).
On a long session with packet logging that is hundreds of megabytes of log text that is never released.

Now that the full stream is on disk, memory only holds what the console window can realistically show:
a single `Queue<LogEntry>` capped at `MaxStoredEntries` (25,000). The duplicate string buffer is gone.
Anything older is read from `DEBUG.txt`.

## Tests

`UnitTests/DebugFileLoggerTests.cs` - session file contents, history rotation and cap, memory-sample
contents including custom providers, and the `CustomConsole` buffer bound.
