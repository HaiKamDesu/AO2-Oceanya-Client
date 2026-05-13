# GM Multi-Client Music List

## Purpose
The GM multi-client music list in `MainWindow` exposes AO2-style music playback from the bottom-row `M` button.
It mirrors the useful AO2 client behavior: categories are collapsible, double-click a song to play it, right-click for
stop/random/effect flags, and use the current-playing footer to see the last channel-0 music packet seen by the client.
The tree has two top-level categories: `SERVER LIST` for server-advertised entries and `LOCAL FILES` for playable
music files under local `sounds/music` asset folders.

## Main Entry Points
- `OceanyaClient/MainWindow.xaml`: bottom-row Music List button, popup list, search, right-click menu, current-playing footer.
- `OceanyaClient/MainWindow.xaml.cs`: snapshots `AOClient.AvailableMusic`, builds display rows off the UI thread, sends play/stop packets, tracks current song.
- `AOBot-Testing/Agents/AOClient.cs`: parses `SM`/`FM`, exposes `AvailableMusic`, sends `MC` music packets, raises `OnMusicChanged`.
- `OceanyaClient/Features/Viewport/AO2ViewportAudioResolver.cs`: resolves music tokens for playback and full local asset paths.
- `Common/SaveFile.cs`: persists popup dimensions and the music-list "show asset paths" preference.

## Packet Flow
- `SM#...#%` starts as an area list and then switches to music when the first file-like token appears. Like AO2, the
  immediately previous non-file token is treated as the first music category, not an area.
- `RM#%` is used for an on-demand AO2 refresh because the tsuserver forks answer with `SM` music data and/or `FA` area
  data. `FM#...#%` can also refresh only the music list when a server sends it.
- `ASS#<asset_url>#%` is stored for server asset awareness. Local playback still resolves from configured local base
  folders first.
- Playing a song sends `MC#<song>#<charId>#...#%`. With AO2 effects support, the last client-sent field is the AO2
  music effect flag bitmask: `FADE_IN=1`, `FADE_OUT=2`, `SYNC_POS=4`.
- Stopping sends `MC#~stop.mp3#<charId>#...#%`.

## UI Behavior
- Area Navigator and Music List popup widths/heights are user-resizable from their bottom-right handles and saved across
  sessions.
- Music entries use AO2's category model under `SERVER LIST`: non-file rows are categories and file rows below them
  become children until the next category.
- `LOCAL FILES` scans configured base folders' `sounds/music` trees asynchronously. Folder paths become nested
  categories, and supported local files use their relative AO2 music token.
- Opening, filtering, packet refresh, and current-song refresh schedule cancellable background tree rebuilds. The UI
  thread only snapshots current state and assigns the finished tree, so slow disks or large local music folders should
  not freeze interaction.
- Music list row building must not call `AO2ViewportAudioResolver.ResolveMusicPath` or other disk-probing helpers for
  every server token. It uses the already-built local music index when available; otherwise it shows the expected
  `base/sounds/music/...` path until the async local scan completes.
- Collapsed/expanded music categories are persisted by stable category keys across sessions.
- Song titles are green when the local file resolves and red when the local file is missing. This matches AO2's
  found/missing visual cue; missing entries are still selectable so the server can receive the same `MC` token.
- The context menu's `Show Asset Paths` option defaults off and toggles the full resolved local path shown below each
  song and in the currently-playing footer.
- If the viewport window is not visible, `MainWindow` owns a fallback music player so area-entry/current-music packets
  still produce local audio.
- Debug timing for tree rebuilds and local scans is logged under the `MusicList` category in the debug console.

## Pitfalls
- AO2 and the tsuserver forks do not expose a client command for seeking to an exact timestamp. The UI therefore does
  not show a timeline or five-second skip controls.
- `SYNC_POS` is an AO2 transition effect. It asks the local player to align a new stream with the old stream position;
  it is not a general-purpose seek command.
- Server playlists/jukebox queues are not sent as a structured live state in the AO2 packet flow. The footer can infer a
  category from the known music list, but not a server-side queue.

## Test Coverage
- `UnitTests/NetworkTests.cs` covers AO2-compatible `SM` area/music splitting, `FM` music refresh parsing, and `ASS`
  asset URL parsing.
