# Live Asset Loading

How Oceanya learns about characters, backgrounds, blips, chat profiles and effects that appear while the
client is already running, without a wait form and without the user asking for a refresh.

## Why this exists

The asset caches (`CharacterFolder.FullList`, `Background`, `BlipCatalog`, `ChatCatalog`,
`EffectsFolderCatalog`) are built from a scan. Before this feature that scan happened in exactly three
places: a full refresh the user asked for, a deferred one-shot scan at startup, and Google Drive / Hivemind
sync. Everything else read a snapshot. The visible consequences were:

- Extracting a character archive into the AO install mid-session did nothing until the user ran
  "Refresh all assets" by hand.
- A refresh that ran *while* a large extraction was still in flight silently indexed a half-written folder,
  so the character "showed up way later" - on the next refresh, or the next launch.
- Every refresh locked the entire client behind a modal `WaitForm` for as long as the scan took, which on a
  large install is tens of seconds.

The target behaviour is AO2's: assets simply appear.

## The character store: index + lazy parse

`CharacterFolder` no longer keeps every character parsed in memory.

**`CharacterIndexEntry`** is the cheap half: `Name`, `DirectoryPath`, `PathToConfigIni`, `ShowName`,
`OptionsName`, `Category`, `CharIconPath`. That answers essentially every question the client asks about a
character it is not currently using - does it exist, what goes in the dropdown, which character has this
showname, what icon do I draw, what category does it group under. The index is persisted to
`characters_index_<key>.json` (a few hundred KB) and is always resident.

**`CharacterFolder`** - char.ini with every emote - is parsed on first real use and held in an LRU capped at
`ParsedCacheLimit` (128). Parsing one char.ini is ~1 ms, so eviction is cheap; holding thousands is not.

Why: the old design kept one list of fully parsed characters persisted as a single JSON file. On a
2627-character install that file was **26 MB** - 677 ms of `JsonSerializer.Deserialize` on the launch
critical path, a permanently resident graph of ~100k `Emote` objects, and a full 26 MB re-serialise every
time ONE character was upserted, which is exactly what the live asset watcher does on every file change.

API:

| need | use |
|---|---|
| names, existence, icons, categories | `CharacterFolder.Index`, `Exists`, `ExistsByNameOrShowName` |
| one character's emotes | `GetByName`, `GetByDirectory`, `GetByNameOrShowName`, `Get(entry)` |
| genuinely all of them | `FullList` - parses everything, returns a list the CALLER owns |

`FullList` has exactly two production callers left: the Character Database Viewer (it displays every
character) and the AI bot's emote map (the prompt needs every character's emote names). Using it anywhere
else re-parses the whole install on every call.

Diagnostics: `[MEM]` reports `indexedCharacters` and `parsedCharacters` separately.

## The integrity verifier is not part of refreshing

`CharacterIntegrityVerifier` used to run for every character as part of every asset refresh: ~17 s of an
18.2 s full refresh on a 2627-character install, 94% of the work, for a diagnostic that only the Character
Database Viewer displays.

It now runs **only there, and only in the background**:
`CharacterFolderVisualizerWindow.StartBackgroundIntegritySweep()` runs after the item list is built, skips
characters that already have a persisted report, runs off the UI thread across cores, and updates rows as
results land (`FolderVisualizerItem` is `INotifyPropertyChanged`). It is cancelled when the list rebuilds or
the window closes, and nothing ever waits on it. A folder that is mid-write throws per character and is
skipped rather than stopping the sweep. Look for `[INTEGRITY-SWEEP]` in the log.

Refreshing assets no longer touches the verifier at all.

## The three layers

### 1. Live watching - `OceanyaClient/Features/Assets/LiveAssetWatcher.cs`

One `FileSystemWatcher` per watched category per physical mount: `characters/`, `background/`, `misc/`, and
`sounds/blips/`, all with `IncludeSubdirectories`. Only `Globals.PhysicalBaseFolders` is watched - the web
asset mirror is deliberately excluded because it materializes files continuously while streaming and has its
own notification path.

- `ClassifyChange(baseFolder, fullPath)` maps a changed path to `AssetChangeTarget`
  (`Character`/`Background`/`Blips`/`Misc` plus the folder name). A change directly at a category root
  yields an empty entry name, which means "rescan that whole category".
- Changes are coalesced behind a **1500 ms quiet period**, capped at **15 s** of total deferral, so
  extracting an archive of several hundred files produces one refresh after it settles rather than one per
  file - and a slow continuous copy still gets folded in periodically.
- The coalesced set becomes a `TargetedAssetRefreshPlan` applied through
  `ClientAssetRefreshService.RefreshTargetedAssetsInBackgroundAsync`, off the UI thread.
- `FileSystemWatcher.Error` (buffer overflow) escalates to a full refresh, because the individual events are
  gone at that point.
- Editor scratch paths (`.oceanya_character_edit_staging_*`, `*.oceanya_edit_backup_*`) and shell junk
  (`desktop.ini`, `thumbs.db`, `.oceanya_folder_icon.ico`) are ignored. Integrity reports and the character
  cache are written under `%AppData%/OceanyaClient/cache`, not into the asset folders, so a refresh cannot
  feed itself back into the watcher.

Started from `InitialConfigurationWindow`'s startup-ready handler, after the one-shot tracked-change scan so
the two do not fight over the disk during launch; stopped in `App.OnExit`.

### 2. Change notification - `ClientAssetRefreshService.AssetsRefreshed`

Raised on a **background thread** after any refresh completes, carrying
`AssetRefreshCompletedEventArgs` (whether all characters/backgrounds were reparsed, or which names were).

This is what closes the "refreshed but nothing changed on screen" gap: refreshes happen from places the UI
cannot see (startup scan, live watcher, Drive sync), and before this event the newly parsed assets sat in
the cache until something happened to rebuild a dropdown.

`MainWindow.OnAssetsRefreshedInBackground` marshals to the dispatcher and debounces for **600 ms** before
running `RebindClientsToRefreshedCharacters()` + `OnAssetsRefreshedFromVisualizer()`; the debounce matters
because the rebuild re-runs `SelectClient`, the heaviest thing on the switch path.

### 3. On-demand resolution - `CharacterFolder.ResolveOnDemand(name)`

The self-heal for anything the watcher cannot see (a mount it could not watch, a network path, a change that
arrived between scans). When a lookup misses, the mounted folders are probed once for
`characters/<name>/char.ini`; a hit is parsed and upserted into the cache immediately.

A miss is negative-cached for **10 seconds** (`ClearOnDemandMissCache()` clears it, and every refresh does).
This matters: most servers reference characters the user does not have, so without the negative cache an
unknown character would cost a disk probe per message and per rendered frame.

Wired into the two lookups that run per message:
- `AO2ViewportAssetResolver.ResolveCharacter(...)` - the render path, so a character that appeared after the
  last scan renders on the very next message instead of as a placeholder.
- `AOClient.ResolveCharacterByName(...)` - the packet path.

## No more blocking refresh

- `ClientAssetRefreshService.RefreshCharactersAndBackgroundsAsync` and `RefreshTargetedAssetsAsync` no
  longer show `WaitForm`. They still run off the UI thread and are still awaitable; progress goes to the
  debug console as `[ASSET-REFRESH]` lines (`LogCategory.System`). The `owner` parameters are retained so
  call sites keep their contract, but are unused.
- The startup "a full asset refresh is required, continue?" prompt is gone. A forced refresh (mount list or
  app version changed) now just logs its reason and runs in the background while the client launches on the
  cache it already has.
- The **"Refresh character and background info" checkbox is gone** from the initial configuration window.
  Its tooltip promised a rebuild "after adding, removing, or renaming AO asset folders", and all three are
  now automatic: the live watcher covers changes while the client runs, the post-launch tracked-change scan
  covers changes made while it was closed, `GetRefreshRequirementReasonForCurrentEnvironment` forces a full
  rebuild when the mount list or app version changes, and `ResolveOnDemand` self-heals anything those miss.
  `ShouldRunStartupAssetRefresh` therefore only reacts to a forced reason.
- The manual "Refresh all assets" / "Refresh all characters" / "Refresh <character>" menu entries remain as
  an explicit override; they no longer freeze the client.

## Gotchas

- `AssetsRefreshed` fires on a background thread. Subscribers must marshal.
- `CharacterFolder.FullList` returns the live backing list. All cache mutations are copy-on-write under
  `fullListLoadLock` precisely because readers enumerate it outside that lock - do not reintroduce in-place
  `RemoveAll`/`Add`.
- Watching the web mirror would produce a refresh storm. Keep the watcher on `PhysicalBaseFolders`.
- The quiet period is a trade-off: raising it makes very large extractions cheaper but delays the "it just
  appears" feel. The on-demand probe is what keeps the perceived latency at zero regardless.

## Tests

`UnitTests/LiveAssetLoadingTests.cs` - change classification (including the ignore list and the
category-root case), on-demand resolution of a character missing from the cache, negative result for a
character that is not installed, and that a full refresh raises `AssetsRefreshed`.

`UnitTests/SmokeFixtureCharacterFolderTests.cs` - the copy-on-write invariant for upsert and removal.

`UnitTests/TestabilityHardeningTests.cs`
`StartupForcedRefresh_RunsWithoutPromptingAndDoesNotBlockLaunch` - a forced refresh must start without a
prompt and must not block launch.
