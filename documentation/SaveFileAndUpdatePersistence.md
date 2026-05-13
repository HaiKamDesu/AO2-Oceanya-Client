# Save File And Update Persistence

## Purpose
This document records what happens to user data when someone replaces an old Oceanya Client release folder with a newer one.

## Storage Location
Normal launches store the save file at:

`%APPDATA%/OceanyaClient/savefile.json`

When a debugger is attached, or `OCEANYA_CLIENT_PROFILE=Dev` / `Development` is set, the path changes to:

`%APPDATA%/OceanyaClientDev/savefile.json`

The release folder itself is not the default save location. If a user deletes `Oceanya Client v6.1` and unzips a new release folder, their normal save file remains in AppData.

## Preserved User State
`Common/SaveFile.cs` persists and normalizes the state users are likely to care about, including:

- selected `config.ini`, launch mode, selected server, custom servers, OOC name, sticky/effect toggles, and log limits
- GM multi-client snapshot: profiles, selected profile, INI puppet choices, local render character/emote, shownames, positions, effects, offsets, and message flags
- area/music popup sizes, music collapsed categories, custom music commands, custom music names, frequently used music, and music section order
- callword rules, extra audio rules, audio volumes, viewport/chat state, visualizer settings, character tags, creator presets, and window states
- File Hivemind, Google Drive sync, AI bot, and advanced-feature settings

## Migration Behavior
There is no explicit integer schema version. Migration is handled by normal JSON deserialization plus `NormalizeLoadedData`:

- missing new properties receive their `SaveData` defaults
- known legacy values are translated, such as `google_drive_sync` to `oceanyan_file_hivemind`, legacy custom-server endpoint lists, and renamed debug log categories
- malformed or out-of-range values are clamped or removed
- unknown old JSON properties are ignored by `System.Text.Json` and are not written back on the next save

This is enough for ordinary upgrades where the AppData save file is kept. It does not protect a user who manually deletes `%APPDATA%/OceanyaClient/savefile.json`.

## Gotchas
If `ConfigIniPath` points inside a folder the user deletes during update, the app still preserves the save file but will need the user to pick a valid AO `config.ini` again. AO asset caches are allowed to be rebuilt after update.
