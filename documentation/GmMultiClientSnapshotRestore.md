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
- `OceanyaClient/Components/Forms/CharacterSelectorWindow.xaml.cs`
  - remains the shared INI puppet picker; restore passes an adjusted availability map where other snapshot puppets are treated as taken.
- `OceanyaClient/Components/Forms/OceanyaMessageBox.xaml(.cs)`
  - provides the shared modal shell and supports custom button labels/results for restore conflict choices.

## Saved State

Each profile stores client name, planned server INI puppet, local render character, selected emote, IC/OOC shownames, side, background, SFX, desk/emote/shout modifiers, flip, effect, screenshake, text color, preanim, immediate, additive, self offset, and switch-position-on-iniswap.

`SelectedClientIndex` and `SelectedClientName` identify the profile to reselect after restore.

## Restore Behavior

On GM launch, the window connects only as much as needed to inspect server character availability. If a saved INI puppet is unavailable, missing from the server, missing locally, or reserved by another profile in the same snapshot, restore shows a modal choice:

- `Select INIPuppet`: opens the normal character selector with the rest of the snapshot's planned puppets marked unavailable.
- `Delete Client`: skips that saved profile for this restore and updates the saved snapshot afterward.

Direct multi-client mode connects each restored profile separately and selects its saved server puppet. Missing, occupied, missing-local, or duplicate saved puppets are treated as conflicts. If a conflict forces the user to pick a different server puppet, restore only changes the planned server INI puppet; the saved local render character is preserved and applied immediately after the client connects. For example, a profile restored as local `KamLoremaster` on a newly selected `April` server slot still renders/sends as `KamLoremaster`.

Single-internal-client mode restores all profiles while keeping one live internal connection. Only the first accepted restored profile must resolve to an available server puppet because that is the one connection being opened. Later profiles are restored as local profiles without requiring their saved puppets to be currently available, so switching from a previously separate-client snapshot into single-internal mode does not force unnecessary conflict prompts for every saved client. Each profile still keeps a separate planned puppet name so local iniswaps do not erase the server puppet plan, and selecting profiles does not overwrite their OOC name or server-puppet plan from the current single internal connection.

Connection handshakes that time out while waiting for `ID` are retried once after closing the websocket and waiting briefly. Connect calls that do not auto-select a character skip AOClient's old post-area/INI wait delays, which keeps snapshot restore responsive while still opening direct-mode clients sequentially.

## Caveats

The snapshot is a profile restore, not a transcript restore. IC/OOC log contents are intentionally not persisted.
