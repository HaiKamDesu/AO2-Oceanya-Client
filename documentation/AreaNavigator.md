# GM Multi-Client Area Navigator

## Purpose
The GM multi-client area navigator in `MainWindow` shows the selected network client's current area and the server's
known area list, including ARUP player counts, status, case manager, and lock state when the server provides them.

## Main Entry Points
- `OceanyaClient/MainWindow.xaml`: connection info bar and area navigator popup.
- `OceanyaClient/MainWindow.xaml.cs`: snapshots `AOClient.AvailableAreaInfos`, builds popup rows off the UI thread, and sends area-switch requests.
- `AOBot-Testing/Agents/AOClient.cs`: parses `SM`, `FA`, `ARUP`, and `/getarea`-style OOC responses.
- `AOBot-Testing/Structures/AreaInfo.cs`: row model for area name, player count, status, CM, and lock state.
- `Common/SaveFile.cs`: persists the navigator popup width and height.

## UI Behavior
- The popup uses the same dark surface styling as the main client controls.
- The bottom-right resize handle saves the chosen width and height across sessions.
- Opening the popup shows the current cached state immediately, then requests a fresh server area list without holding
  the popup closed. Area row construction is cancellable and runs off the UI thread; only the finished item source is
  assigned on the dispatcher.
- Debug timing for area row rebuilds is logged under the `AreaVisualizer` category in the debug console.

## Packet Flow
- `SM` can contain area names before music entries during initial AO2 handshake.
- `FA` is the area-only list. Tsuserver3 and tsuserverCC send this for `RM`; tsuserverCC can also send contextual
  hub/subarea lists when a player changes area.
- `ARUP#0#...#%` updates player counts by area-list index.
- `ARUP#1#...#%` updates area statuses by area-list index.
- `ARUP#2#...#%` updates case managers by area-list index.
- `ARUP#3#...#%` updates lock states by area-list index.
- Server `/getarea` OOC output can update the current area's player count, status, and lock state from its header.
- Tsuserver `=== Areas ===` OOC snapshots update all listed area rows by area name and can mark the current row with
  `[*]`.

## Pitfalls
- `RM` refreshes `FA` but does not request a fresh ARUP snapshot. Rebuilding area info rows from `FA` alone erases
  live status and causes misleading `0 users` rows.
- `FA` can reorder or narrow the visible area list. Preserve known `AreaInfo` values by area name, then apply later
  ARUP packets by the current list index.
- Empty ARUP fields are meaningful placeholders. Do not remove empty entries while parsing ARUP, or later areas shift
  onto the wrong row.

## Test Coverage
- `UnitTests/NetworkTests.cs` covers default current-area inference from `FA`, `/getarea` header parsing, empty ARUP
  slot preservation, and preserving known ARUP state across later `FA` refreshes.
