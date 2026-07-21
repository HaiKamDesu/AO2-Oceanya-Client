# Character Cache Cold-Launch Performance

## Problem

GM Multi-Client launch was dramatically slower on the **first launch after a machine reboot**
(30–90 s "Creating window..." stall), then fast (~1 s) on every relaunch until the next reboot.
The cost varied per machine and scaled with installed character count.

## Root cause (measured, not theorized)

The stall was isolated with split timers in `StartupTimingLogger` (see the `ic_settings_character_fulllist_touched`
log line, phases `character_*` and `compatibilityCheckMs`). Hard facts from the reboot A/B logs:

- File read of the 26 MB `characters_<hash>.json` cache: ~110 ms cold (fast).
- JSON deserialize of that cache: ~700 ms cold (fast, CPU-bound, cold ≈ warm).
- **`IsCacheCompatible` → `BuildSourceSignature`: ~30,000 ms cold vs ~40 ms warm.**

`BuildSourceSignature` looped over every character folder and did `File.Exists` + `File.GetLastWriteTimeUtc`
on each `char.ini` — ~5,200+ individual filesystem metadata calls for ~2,600 characters, on the load
critical path, **every launch including cache hits**. Cold, each stat hit the disk one at a time
(~11 ms each → ~30 s); warm, they were served from the OS metadata cache (~40 ms). Parallelizing the
scan did **not** help (30 s → 30 s), proving the disk is latency-bound (HDD seeks / filter-driver
serialization), not throughput-bound. Windows Defender real-time scanning accounted for roughly another
half of the original cost before that; excluding the `%APPDATA%\OceanyaClient` and
`%APPDATA%\OceanyaClientDev` folders is still recommended.

The scan was a **cache-invalidation check**: it did thousands of cold-disk stats purely to decide whether
the cache it had already read (in <1 s) was still valid — defeating the point of the cache.

## Fix

`AOBot-Testing/Structures/CharacterFolder.cs`:
- **Removed the per-character `char.ini` timestamp signature from the load path** (`IsCacheCompatible`
  no longer calls `BuildSourceSignature`; the method was deleted). The cheap validity checks remain:
  cache `Version`, `ConfigPath`, and `BaseFolders` (mount list) — all instant string comparisons.
- **Removed the signature from the write path** too (`SaveToJson` sets `SourceSignature = string.Empty`
  instead of recomputing it — that recompute cost the same ~30 s cold on every refresh-write).
- Result: cold character load is now just file-read + deserialize (~850 ms), i.e. **cold ≈ warm**.

**Functionality is preserved** because per-character edits/adds/removes are still detected — off the
launch critical path — by the existing post-launch background check
`ClientAssetRefreshService.GetTrackedChangePlanForCurrentEnvironment()` (fired from
`InitialConfigurationWindow.HandleStartupFunctionalityReadyAsync`). It compares the saved
`asset_refresh_marker.json` asset-state snapshot against current disk state and refreshes changed
characters live via `RefreshTargetedAssetsInBackgroundAsync`. The only behavioral change: an edited
`char.ini` is reflected a few seconds after launch instead of blocking launch by 30 s.

## Supporting changes

- `FullList` getter is now guarded by `fullListLoadLock` so concurrent access (e.g. background refresh
  vs. UI) cannot race or double-load; a caller hitting it mid-load joins the in-progress result.
- Cache directory resolution moved to `Common/CacheEnvironment.GetCacheRoot()`, which follows the same
  production/dev/unit-test isolation as `SaveFile`. **Production path is unchanged**
  (`%APPDATA%\OceanyaClient\cache`); dev uses `OceanyaClientDev\cache`; unit tests use a temp dir. This
  fixed a latent bug where a unit-test run wrote/pruned the real user's cache directory.
- `Common/CacheFilePruner.PruneStaleCacheFiles(cacheRoot, prefix, currentFilePath)` deletes abandoned
  per-config-hash cache files older than 30 days (best-effort). Wired into `CharacterFolder`
  (`characters_`), `Background` (`backgrounds_`), and `CharacterFolderVisualizerWindow`
  (`folder_visualizer_`). This stops the cache dir from accumulating one file per config/mount change
  forever (it had grown to 32k+ files / 380 MB). Persistent single-name files
  (`character_folder_tags.json`, `asset_refresh_marker.json`) use no prefix match and are never pruned.

## Diagnostics

`StartupTimingLogger` writes `%AppData%\OceanyaClient\startup_timing.log` on each launch. The
`ic_settings_character_fulllist_touched` line reports `cacheHit`, `loadMs`, `fileBytes`, `fileReadMs`,
`deserializeMs`, `compatibilityCheckMs`, `count`. `compatibilityCheckMs` should stay ~0; a regression
back to tens of seconds means a per-character disk scan crept back onto the load path.

## Not the cause (ruled out with data)

- Cache file living in `%TEMP%` — it never did; `EnsureCacheFilePath` overrides the placeholder path.
- Cache directory bloat alone — cleaning 32k files changed cold time by <10%.
- File read / JSON deserialize size — both fast cold.
- The 26 MB cache size is large but not the bottleneck; shrinking it is a possible future optimization,
  not required for cold ≈ warm.
