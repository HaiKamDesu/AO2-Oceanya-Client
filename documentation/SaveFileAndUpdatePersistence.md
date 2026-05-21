# Save File And Update Persistence

## Purpose
This document records what happens to user data when someone replaces an old Oceanya Client release folder with a newer one.

## Storage Location
Normal launches store the save file at:

`%APPDATA%/OceanyaClient/savefile.json`

When a debugger is attached, or `OCEANYA_CLIENT_PROFILE=Dev` / `Development` is set, the path changes to:

`%APPDATA%/OceanyaClientDev/savefile.json`

Unit tests do not use the productive AppData path. `Common/SaveFile.cs` detects NUnit/testhost processes before its first load and uses a temp path under `OceanyaClientUnitTests` so test setup/teardown cannot overwrite `%APPDATA%/OceanyaClient/savefile.json`. UI automation and other explicit test launches that pass `--test-savefile=<path>` use that path immediately, including during `SaveFile` static initialization.

The release folder itself is not the default save location. If a user deletes `Oceanya Client v6.1` and unzips a new release folder, their normal save file remains in AppData.

Updater downloads and logs follow the same profile split, but under LocalAppData:

- Stable/Release updates: `%LOCALAPPDATA%/OceanyaClient/Updates`
- Debug/Test updates: `%LOCALAPPDATA%/OceanyaClientDev/Updates`

## Preserved User State
`Common/SaveFile.cs` persists and normalizes the state users are likely to care about, including:

- selected `config.ini`, launch mode, selected server, custom servers, OOC name, sticky/effect toggles, and log limits
- GM multi-client snapshot: profiles, selected profile, INI puppet choices, local render character/emote, shownames, positions, effects, offsets, and message flags
- area/music popup sizes, music collapsed categories, custom music commands, custom music names, frequently used music, and music section order
- callword rules, extra audio rules, audio volumes, viewport/chat state, visualizer settings, character tags, creator presets, and window states
- File Hivemind, Google Drive sync, AI bot, and advanced-feature settings
- updater state under `SaveFile.Data.Updater.Stable` and `SaveFile.Data.Updater.Test`, including skipped release tag/version and last seen/check/failure timestamps

## Migration Behavior
There is no explicit integer schema version. Migration is handled by normal JSON deserialization plus `NormalizeLoadedData`:

- missing new properties receive their `SaveData` defaults
- known legacy values are translated, such as `google_drive_sync` to `oceanyan_file_hivemind`, legacy custom-server endpoint lists, and renamed debug log categories
- malformed or out-of-range values are clamped or removed
- unknown old JSON properties are ignored by `System.Text.Json` and are not written back on the next save

This is enough for ordinary upgrades where the AppData save file is kept. It does not protect a user who manually deletes `%APPDATA%/OceanyaClient/savefile.json`.

## Gotchas
If the initial configuration window looks reset after an update even though `%APPDATA%/OceanyaClient/savefile.json` still exists, check `%APPDATA%/OceanyaClient/savefile_load.log`. It records the exact save path, whether the file existed, profile environment variables, debugger state, and any load exception. If a load exception occurs, the original file is copied to `savefile.unreadable.<timestamp>.json` before the app falls back to defaults.

If a release-folder replacement leaves `ConfigIniPath` pointing inside the deleted old folder, startup tries to heal the common layout by checking the current app folder for `config.ini`, `<same parent folder name>/config.ini`, and `base/config.ini`. If none exist, the user still needs to select the new `config.ini` path manually. That should not erase unrelated AppData save state; it only means the old AO install path no longer exists.
