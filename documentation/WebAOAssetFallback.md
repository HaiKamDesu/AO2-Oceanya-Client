# webAO Asset Fallback

Serves any asset the user does not have locally from the connected server's HTTP asset URL, so a GM
Multi-Client session works against webAO-style servers without a matching local `base/` folder.

**Status: complete and wired end to end** - images, audio, characters, and provenance UX. One live
session against skrapegropen drove a round of fixes; see "Field measurements" below. The fixed build
has not been re-measured live yet.

## Purpose

The normal AO2 client resolves every asset from physical files under `Globals.BaseFolders`. webAO
(`webAO/` submodule) instead streams everything over HTTP from a per-server asset URL. This feature
merges both: **local files always win, web content fills the gaps**, and the user should not be able
to tell which is which unless they go looking.

Scope is GM Multi-Client only. Offline tools (Character Database Viewer, File Creator) stay
physical-only by never installing the service.

## Reference clients

Both are reference-only submodules; do not copy their code.

### webAO (`webAO/webAO/`)

| Concern | File | Behavior |
|---|---|---|
| Asset URL | `client/aoHost.ts` | `AO_HOST` is set from the server's `ASS#` packet, always trailing-slash |
| Layout | `client/setEmote.ts`, `dom/*`, `packets/handlers/handleMC.ts` | Plain AO2 VFS, all-lowercase, `encodeURI` per path |
| Manifests | `client/fetchLists.ts` | `characters.json`, `backgrounds.json`, `evidence.json`, `extensions.json` at the asset root |
| Probing | `utils/fileExists.ts`, `utils/filesExist.ts` | Cached `HEAD` per URL; whole extension chain probed in parallel, first hit wins |
| Preloading | `viewport/utils/preloadMessageAssets.ts` | Every asset of an IC message resolved + preloaded in one `Promise.all` before the timeline starts, 8 s timeout |

Paths: `characters/<name>/<emote><ext>`, `characters/<name>/char.ini`,
`characters/<name>/char_icon.png`, `background/<bg>/<pos>.<ext>`, `sounds/music/<track>`,
`sounds/general/<sfx>.opus`, `sounds/blips/<blip>.opus`, `misc/<misc>/...`, `themes/<theme>/...`,
`evidence/<img>`.

### AsyncAO (`AsyncAO/internal/assets/`)

`resolver.go` plus `docs/adr/0001-zero-fallback-by-default.md`. Its **learned formats** idea is what
keeps probe counts sane: remember the first extension that works per `(host, asset type)` and steady
state becomes one perfectly-aimed request per asset (measured 285 probes on a 200-character server
instead of thousands).

AsyncAO's local mounts are an **either/or mode**, not a local-then-web merge, so its topology does
not apply here - only its resolver and caching ideas do.

## Design

### The web mirror mount

Web assets materialize into a real directory laid out exactly like an AO2 `base/`, appended as the
**last** entry of `Globals.BaseFolders`. Every existing resolver
(`AO2ViewportAssetResolver.ResolveImageVfs` / `ResolveImageStem`, `CharacterFolder.CharacterFolders`,
`Background.RefreshCache`, `AO2ViewportAudioResolver`, `AO2ChatPreviewResolver`, `AO2SoundList`)
already walks the mount list first-hit-wins, so:

- local priority is free, with no resolver changes;
- downstream consumers (`BitmapFileLoader`, `Ao2AnimationPreview`, BASS, the WPF viewport) keep
  receiving plain file paths and need no awareness of the feature at all.

Mirror root: `<CacheEnvironment.GetCacheRoot()>/webassets/<sha256-prefix of asset URL>/`, so prod,
dev, and unit-test runs never share a mirror and two servers can never poison each other.

### Resolution never blocks

Synchronous resolution must never touch the network. A resolver that misses locally returns `null`
immediately and fires a non-blocking `WebAssetService.RequestIfActive(...)`. The download surfaces
later through the static `WebAssetService.AnyMaterialized` event; `AO2ViewportControl` debounces those
(60 ms) and repaints the current scene through `RenderScene(..., visualRefreshOnly: true)`, which skips
audio, screen shake, slides, sticker/evidence re-triggers, and the text reveal. Combined with
`SetCharacterAnimatedImageAsync` holding the previous frame, a late asset is a frame swap, not a flash.

Three request shapes:

| Method | Use |
|---|---|
| `Request` / `RequestIfActive` | fire-and-forget from a synchronous resolver |
| `RequestAsync` | await the download (prefetch batches) |
| `RequestWithGraceAsync` | await up to a hard cap, then give up and render without it |

### Probe economy

1. **Manifests** turn "does this exist?" into a dictionary lookup. Fetched in the background at
   install; probing works before they land and only gets cheaper after.
2. **Learned formats** move the known-good extension to the front of the probe order. Unlike AsyncAO
   the rest of the chain is kept as fallback, so mixed-format packs still resolve - one request when
   the guess is right, unchanged behavior when it is wrong.
3. **Persistent negative cache** (24 h TTL) keeps a genuinely missing asset at one probe.
4. **Single GET per candidate**, not `HEAD`-then-`GET`, so a successful probe is one round trip.
5. **Two concurrency lanes** so speculative prefetch can never delay the message on screen.
6. **Circuit breaker**: 3 consecutive transport failures (refusals *or* timeouts) disable fallback
   for the session, degrading to exactly the pre-feature behavior.

### Latency contract

| Case | Cost |
|---|---|
| Local asset | untouched code path, zero added cost |
| Previously fetched web asset | a real file in the mirror; identical to a physical file, forever |
| First-ever web asset | one GET, typically 100-400 ms, appearing as a pop-in - never a stall |

The message itself never waits: text reveal is already not gated on sprite decode (AO2 parity), and
`SetCharacterAnimatedImageAsync` holds the previous frame rather than blanking.

## Files

| File | Responsibility |
|---|---|
| `Common/WebAssets/WebAssetKind.cs` | asset kinds and default extension probe orders |
| `Common/WebAssets/WebAssetSource.cs` | URL/VPath normalization, mirror root, `IsWebAsset` provenance, `TryGetRemoteUrl` |
| `Common/WebAssets/WebAssetFormatMemory.cs` | learned formats + negative cache, persisted to `format-memory.json` in the mirror |
| `Common/WebAssets/WebAssetManifest.cs` | fetch/parse the four root manifests, `IsKnownAbsent` suppression |
| `Common/WebAssets/WebAssetFetchQueue.cs` | throttled probe/download, atomic write, dedup, circuit breaker, `Materialized` |
| `Common/WebAssets/WebAssetStats.cs` | lock-free counters and the `[WEB-SUMMARY]` line |
| `Common/WebAssets/WebAssetService.cs` | facade, `Install`/`Uninstall`/`ReapplyMount`, static `Current` |
| `OceanyaClient/Features/WebAssets/WebAssetPrefetcher.cs` | per-message / roster / emote-page / background warm-up |
| `OceanyaClient/Features/WebAssets/WebCharacterMirror.cs` | on-demand `char.ini` mirroring for one character (`EnsureCharacterAsync`) |
| `OceanyaClient/Utilities/WebAssetMenuDecorator.cs` | context-menu provenance for streamed assets |

`Globals.UpdateConfigINI` calls `WebAssetService.ReapplyMount()` because rebuilding `BaseFolders`
from `config.ini` would otherwise drop the mirror.

### Where it hooks into existing code

| Site | Change |
|---|---|
| `AOClient` `ASS#` handler | new `OnServerAssetUrlReceived` event carrying the asset URL |
| `MainWindow.AttachWebAssetFallback` | installs the service per GM session. **Only** GM Multi-Client calls this, which is what keeps the Character Database Viewer physical-only. Deliberately does NOT bulk-mirror the roster - see Field measurements |
| `AO2ViewportAssetResolver.ResolveImageVfs` / `ResolveImageStem` | optional `WebAssetKind` parameter; a miss requests the web. `null` (the default) means "layout probe, do not generate traffic" |
| `AO2ViewportAssetResolver.ResolveImageVfsCandidates` | new: walks the whole AO2 candidate list locally **before** requesting anything, so a theme default present on disk never triggers a download of the character-specific override that sits earlier in the list |
| `AO2ViewportAssetResolver.RequestWebBackgroundFallback` | a background with no local folder at all gets a small targeted request set, because `Background.FromBGPath` needs a folder before any per-position probing can happen |
| `AO2ViewportAudioResolver` | SFX, blips, and character shout SFX request on miss. Music is untouched (BASS already streams the URL directly) |
| `AO2ViewportControl` | materialize subscription + debounced repaint, per-message prefetch, and the grace window |
| `Globals.PhysicalBaseFolders` | new: mount list minus the mirror, used by every cache signature |

## Diagnostics

Category `CustomConsole.LogCategory.WebAssets`, badge `[WEB]`, on by default, exportable from the
Debug Console like every other category.

| Line | Meaning |
|---|---|
| `[WEB] Fallback installed for <url> \| mirror=… mirrorMs=… mountMs=… learned=… knownMisses=…` | install timing and warm-cache state |
| `[WEB] Manifests for <url>: characters=… backgrounds=… evidence=… extensionOverrides=…` | what the server published |
| `[WEB] hit <vpath> kind=… ext=… probes=… bytes=… queueMs=… totalMs=… prio=…` | per-asset download timing |
| `[WEB] miss <vpath> kind=… tried=[…] probes=… totalMs=…` | every candidate 404'd |
| `[WEB] transport failure n/3 …` | circuit-breaker progress |
| `[WEB-SUMMARY] materialized=… misses=… mirrorHits=… skipManifest=… skipNegCache=… http=… reqPerAsset=… bytes=… avgMs=… maxMs=…` | every 25 completions and on shutdown |
| `[WEB-PREFETCH] message char=… emote=… queued=… elapsedMs=…` | what one incoming IC message warmed |
| `[WEB-PREFETCH] roster character=…` / `emote buttons character=… queued=…` | speculative warm-ups |
| `[WEB-GRACE] stem=… landed=… waitedMs=… budgetMs=…` | whether the grace window caught the sprite |
| `[WEB-REFRESH] repaint coalesced=… elapsedMs=… hadScene=…` | late-asset repaint cost, and how many arrivals it batched |
| `[WEB] Character sync starting/progress/finished` | `char.ini` mirroring for server-published characters |

`reqPerAsset` is the headline health number: it should trend toward 1.0 as learned formats settle.
`mirrorHits` rising while `http` stays flat means the warm cache is doing its job.

Three numbers to reach for when tuning:

- **`[WEB-GRACE] landed=false` dominating** - the 150 ms window is too tight for this server, or the
  prefetch is not getting a head start. `WebAssetService.DefaultGraceWindowMilliseconds` is the single
  constant to move.
- **`[WEB-REFRESH] elapsedMs` high** - late-asset repaints are costing real frames. Cross-check
  `[RENDER-TIMING]`, which breaks the same pass down per phase.
- **`[WEB] miss` for assets that clearly exist** - the probe order is wrong for this server; check
  whether its `extensions.json` was parsed (`extensionOverrides=` in the manifest line).

## Field measurements (skrapegropen, 3564 missing characters)

First live run against a real asset server. What the numbers said and what changed because of them:

| Observation | Verdict |
|---|---|
| `reqPerAsset=1.07`, `skipNegCache=3691`, no transport failures | Learned formats, negative cache, and the circuit breaker all behaving. |
| `avgMs=13149 maxMs=52832` | **Bad.** Queue wait, not network. Caused by bulk-mirroring 3564 `char.ini` files at connect, which held the prefetch lane for 2+ minutes and starved on-screen assets. Bulk mirroring removed - configs are on-demand only now. |
| 512 renders, 488 of them `[WEB-REFRESH]`, 16-20 ms each | **Bad.** Every `char.ini` arrival repainted the whole scene (~9 s of UI stalls). `IsSceneRelevantWebAsset` now drops non-visual kinds and assets belonging to a character/background that is not on screen. |
| Web-only characters showed a permanent placeholder icon | **Bug.** Two causes: background icon loading only iterated cards with a local icon path, and nothing applied an icon that arrived later. Both fixed; `CharacterFolder` also resolves its icon path once at `Create` time, so a character registered before its icon downloaded needs a mirror lookup. |
| `skipManifest=0` | This server publishes no `characters.json`, so manifest suppression never engages there. Expected, not a defect. |

## Field measurements, round 2 (miku.pizza / Nyathena)

| Observation | Verdict |
|---|---|
| `materialized=2 misses=23 skipNegCache=335`, sprites showing as placeholders | **Two compounding bugs.** (1) The server publishes `emote_extensions: [".webp", ".webp.static"]`; collapsing `.webp.static` into `.webp` left a single candidate, so `(a)brokentalk.webp` 404'd and the real file `brokentalk.webp` was never tried. (2) The previous session's misses, recorded under that broken list, were still suppressing everything from the 24 h negative cache. |
| `MC#Trash Pit#0#%` sent, server never responds | **Protocol bug.** AO2 puts `m_cid` (character id) in that field; we sent the connection id, which is 0. Nyathena validates it and drops the packet. |
| Streamed character had an icon in the selector but none in the clients panel, and blank emote buttons | **Cache-shape bug.** `CharacterFolder` bakes icon/button paths at parse time, so a character registered before its art downloaded keeps empty paths forever. |
| `[WEB-REFRESH]` down to ~7 ms and only on real scene changes; `[WEB]` lines 6016 -> 162 | Round-1 fixes held. |

## Field measurements, round 3 (miku.pizza)

| Observation | Verdict |
|---|---|
| `avgMs=424 maxMs=1119`, `[WEB]` lines 6016 -> 162 | Round-2 fixes held; latency is now network-shaped, not queue-shaped. |
| Own character rendered, everyone else drew `placeholder.gif` | **Bug.** `Render character char="(null)"` - sprite resolution needs a `CharacterFolder` for the folder name, and only the user's own character had been mirrored. The sprites themselves had already downloaded. Fixed by mirroring the `char.ini` of characters seen speaking. |
| `reqPerAsset=2.90` | Higher than the 1.0 ideal because the session was mostly first-time probes across many characters; expect it to fall as learned formats and the mirror warm up. |

## Pitfalls

- **Never add the mirror to `CharacterFolder.IsCacheCompatible`'s signature.** `BaseFolders` is part
  of that signature today; including the mirror would force a full cold character rescan on every
  connect - the ~30 s regression documented in `CharacterCacheColdLaunch.md`.
- **The mirror must be written lowercase.** webAO lowercases every segment, and lowercase keeps
  `ResolvePathCaseInsensitive`'s Windows fast path matching.
- **URLs are escaped like `encodeURI`, not `EscapeDataString`.** AO2 emote filenames are full of
  parentheses; encoding them as `%28`/`%29` 404s on hosts that match the raw path.
  `WebAssetSource.EncodeUriPassthrough` holds the exact character set.
- **`.webp.static` is a URL shape, not a decode hint.** webAO's `setEmote.ts` builds
  `<emote>.webp` for that entry with the `(a)`/`(b)` prefix **removed** - the single-sprite layout.
  Collapsing it to `.webp` silently deletes a candidate and breaks every such character. (AsyncAO
  abolished it in ADR-0001, but AsyncAO builds its own URLs and does not have to interoperate with a
  server that advertises the marker.)
- **A negative-cache entry is only valid for the probe list that produced it.** The cache key includes
  the sorted configured extension set. Do not fold the learned-format ORDER into that key: every
  `InvalidateLearned` would flip it and absent assets would be re-probed forever.
- **Sprite resolution requires a `CharacterFolder`.** A downloaded sprite is invisible without one,
  because `ResolveCharacterImageAsset` builds its VFS path from the folder's name. Any character that
  can appear on screen must have its `char.ini` mirrored first.
- **`CharacterFolder` bakes asset paths at parse time.** Anything resolved once and cached (icon,
  emote buttons) is empty forever for a character registered before its art downloaded. Display code
  must go through `WebCharacterIconResolver`, not the cached property.
- **A hanging host must trip the breaker.** `HttpClient` timeouts arrive as
  `TaskCanceledException`; only cancellations from the queue's own shutdown token are exempt.
- **Never bulk-fetch a whole roster.** Both concurrency lanes are small on purpose; a few thousand
  queued prefetches turn every on-screen asset into a 10+ second wait. Fetch what is selected or
  visible.
- **Repaints are not free.** Anything subscribing to `AnyMaterialized` must filter hard. A repaint is
  a full `RenderScene` pass; at 16-20 ms it costs a frame every time.

## Tests

`UnitTests/WebAssetFallbackTests.cs` (33 tests) runs the fetch queue against an in-process
`HttpListener` stub that counts requests, so probe economy is asserted rather than assumed:

- URL/VPath normalization, `encodeURI` escaping, traversal rejection, disjoint mirrors per server
- mount ordering (mirror always last), idempotent mount, unmount, `ReapplyMount`
- learned-format reordering, per-kind scoping, persistence across reload, negative-cache TTL
- manifest parsing, `.webp.static` collapse, `IsKnownAbsent` only for covered roots
- download + atomic write + `Materialized`, no temp files left behind
- **learned format collapses the second asset to exactly one request**
- **warm mirror issues zero requests**
- **negative cache suppresses the re-probe**
- **manifest suppresses probing for an unlisted character**
- concurrent requests share one download; grace window expires but the download continues
- circuit breaker trips on an unreachable host and then issues nothing

`UnitTests/WebAssetIntegrationTests.cs` (12 tests) covers the seams with existing systems:

- **a local file always wins over the same asset in the mirror**, and the mirror serves what the user lacks
- **`Globals.PhysicalBaseFolders` excludes the mirror** and is unchanged by connect/disconnect - the
  guard against the cold-rescan regression
- physical path to VFS mapping, including nested mounts and paths outside every mount
- menu provenance: rewritten only for web assets, untouched for local ones, copyable URL added
- per-message prefetch is a no-op with fallback off, and queues the expected asset set with it on

Run build and test as separate commands per `AGENTS.md`.

## Not yet done

- **Live verification is partial.** One session against skrapegropen produced the measurements above
  and the fixes that followed; the fixed build has not been re-measured live yet. To re-run: connect with the local `base/`
  temporarily renamed and watch the `[WEB]` category for probe counts, `reqPerAsset`, and grace-window
  hit rate. Perf gates to confirm at the same time: `[RENDER-TIMING]` shows no new UI-thread cost on a
  miss, `[SWITCH-TIMING]` is unchanged on client switch, and `startup_timing.log`
  `compatibilityCheckMs` stays ~0 after connecting (proving the mirror stayed out of the cache
  signature).
- **Music is unchanged.** It already streamed from the asset URL through BASS and was deliberately left
  alone; it does not participate in the mirror, learned formats, or the negative cache.
- **`soundlist.ini` is not mirrored** for web-only characters, so their SFX dropdown falls back to the
  shared base list.
- **Evidence and background manifests are parsed but only backgrounds/characters drive
  `IsKnownAbsent`.** `evidence.json` is currently informational.
- **No mirror size cap.** Pruning relies on the existing 30-day `CacheFilePruner` policy; a very active
  user on many servers will accumulate mirrors until then.
