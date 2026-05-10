# GM Multi-Client Snapshot Restore

The GM multi-client saves its client profile set in `SaveFile.Data.GMMultiClientSnapshot` so a later launch can rebuild the same working layout against the newly selected server.

## Entry Points

- `OceanyaClient/MainWindow.xaml.cs`
  - captures snapshots from client add/remove/select, rename, INI puppet selection, OOC shownames, IC shownames, emotes, text color, effects, preanim, flip, additive, immediate, SFX, position, and send actions.
  - restores the snapshot after `MainWindow_Loaded` when no clients already exist.
  - resolves unavailable INI puppets before creating profiles.
- `Common/SaveFile.cs`
  - stores and normalizes `GmMultiClientSnapshot` and `GmMultiClientSnapshotClient`.
- `OceanyaClient/Components/ICMessageSettings.xaml.cs`
  - raises `OnClientStateChanged` when selected client message state changes.
- `AOBot-Testing/Agents/AOClient.cs`
  - parses `FA`, `SM`, and `ARUP` area packets. If a server does not send an explicit current-area packet on initial connect, the client treats the first area in the area list as the server default, matching tsuserver3/tsuserverCC startup behavior.
- `OceanyaClient/Components/Forms/CharacterSelectorWindow.xaml.cs`
  - remains the shared INI puppet picker; restore passes an adjusted availability map where other snapshot puppets are treated as taken.
- `OceanyaClient/Components/Forms/OceanyaMessageBox.xaml(.cs)`
  - provides the shared modal shell and supports custom button labels/results for restore conflict choices.
- `OceanyaClient/Components/Forms/InitialConfigurationWindow.xaml.cs`
  - handles launch preflight. GM multi-client and AO2 AI Bot defer live server probing to the real AOClient connection when the endpoint is syntactically valid, avoiding a duplicate connection attempt before the main window opens.
- `OceanyaClient/ClientAssetRefreshService.cs`
  - keeps forced/full asset refresh blocking when required, but allows accepted targeted refresh work to continue in the background after the startup window is ready.

## Saved State

Each profile stores client name, planned server INI puppet, local render character, selected emote, IC/OOC shownames, side, background, SFX, desk/emote/shout modifiers, flip, effect, screenshake, text color, preanim, immediate, additive, self offset, and switch-position-on-iniswap.

`SelectedClientIndex` and `SelectedClientName` identify the profile to reselect after restore.

Client order is the saved list order. Manual `Move UP` / `Move DOWN` actions in the client context menu reorder the live button grid and immediately recapture the snapshot so cross-session restore uses the same order.

## Restore Behavior

On GM launch, the window connects only as much as needed to inspect server character availability. If a saved INI puppet is unavailable, missing from the server, missing locally, or reserved by another profile in the same snapshot, restore shows a modal choice:

- `Select INIPuppet`: opens the normal character selector with the rest of the snapshot's planned puppets marked unavailable.
- `Delete Client`: skips that saved profile for this restore and updates the saved snapshot afterward.

Direct multi-client mode connects each restored profile separately and selects its saved server puppet. Missing, occupied, missing-local, or duplicate saved puppets are treated as conflicts. The first direct-mode connection is reused as the first restored client after conflict resolution, so restore does not pay for a throwaway availability-only handshake before creating the real clients. If a conflict forces the user to pick a different server puppet, restore only changes the planned server INI puppet; the saved local render character is preserved and applied immediately after the client connects. For example, a profile restored as local `KamLoremaster` on a newly selected `April` server slot still renders/sends as `KamLoremaster`.

Single-internal-client mode restores all profiles while keeping one live internal connection. Only the first accepted restored profile must resolve to an available server puppet because that is the one connection being opened. Later profiles are restored as local profiles without requiring their saved puppets to be currently available, so switching from a previously separate-client snapshot into single-internal mode does not force unnecessary conflict prompts for every saved client. Each profile still keeps a separate planned puppet name so local iniswaps do not erase the server puppet plan, and selecting profiles does not overwrite their OOC name or server-puppet plan from the current single internal connection.

Restore conflict prompts name the planned server INI puppet, not the local render character. Legacy snapshots that accidentally saved the local render character in the INI puppet field are recovered from the saved server puppet ID when the server list order still allows it. The dialog distinguishes taken server slots from server-missing puppets so the user can tell whether `Franziska` is occupied or absent from the current server's character list.

Manual INI puppet changes from the client context menu are server-puppet changes only. The selector highlights the currently used INI puppet even when the server reports it as occupied by this client, and confirming a different puppet reapplies the profile's existing local render character/emote/showname/position afterward so local iniswaps are preserved.

When the main window closes, it cancels the close once, closes owned viewport/audio state, and asks every live AO2 connection to close cleanly before allowing the host window to close. This uses the same websocket close path as AO2's `CloseCodeGoingAway` behavior and marks the client as intentionally closed so the reconnect loop does not fight application shutdown.

Connection handshakes that time out while waiting for `ID` are retried once after closing the websocket and waiting briefly. Connect calls that do not auto-select a character skip AOClient's old post-area/INI wait delays, which keeps snapshot restore responsive while still opening direct-mode clients sequentially. Snapshot restore uses one shared progress wait form instead of opening and closing one wait form per profile.

## Main Window Status And Clipboard

The top connection strip is fed by the selected profile's live network client. It shows server, current area, user count, and CM/status/lock details from `ARUP` when the server supports that data. It also parses tsuserver `/getarea` OOC output so the area header, not the people-count line, becomes `CurrentArea`. Opening the area navigator sends `RM#%` first so the server can return a fresh `FA`/`ARUP` view before the list is shown.

Main-window text fields handle Ctrl+X/C/V through `ClipboardUtilities` instead of relying on WPF's single-attempt clipboard commands. This is a local robustness layer for Windows clipboard contention (`CLIPBRD_E_CANT_OPEN`), but the default path does not sleep/retry on the UI thread. If another process owns the global clipboard, the operation fails fast instead of freezing the app.

## Startup Wait Optimizations

The launch path avoids these waits for server-backed modes:

- duplicate server probe before the actual AOClient connection, when the endpoint has a valid `ws://`, `wss://`, or `tcp://` shape.
- blocking targeted asset refresh after the user accepts the "changed assets" prompt. The refresh runs after the main window reports ready.
- throwaway direct-mode snapshot availability connection.
- per-client wait-form churn during snapshot restore.

Forced refresh remains blocking. Skipping it can leave `CharacterFolder.FullList` empty or pointed at a different config/base folder, which breaks the character selector and snapshot conflict resolution. Targeted refresh is safer to defer because the existing cache is still compatible with the active config; the tradeoff is that newly changed local assets may appear a little later while the background refresh finishes. Backgrounds already have lazy disk resolution paths, while character selection depends more directly on the cached character list.

## Caveats

The snapshot is a profile restore, not a transcript restore. IC/OOC log contents are intentionally not persisted.
