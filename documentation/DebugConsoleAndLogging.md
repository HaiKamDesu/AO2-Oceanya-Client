# Debug Console And Logging

## Purpose
`CustomConsole` is the structured diagnostic log used by the WPF client and shared AO2 code. It keeps backward-compatible formatted strings, but new work should prefer structured `CustomConsole.Info`, `Warning`, `Error`, or `Debug` calls with an explicit `LogCategory`.

## Entry Points
- `Common/CustomConsole.cs`: log levels, categories, structured entries, and event fan-out.
- `OceanyaClient/Components/Forms/DebugConsoleWindow.xaml(.cs)`: live debug console with category filters.
- `Common/SaveFile.cs`: persists `EnabledLogCategories`.

## Categories
Current categories are:
- `System`: startup, app state, connection lifecycle, settings.
- `Network`: raw AO2 packets.
- `IC`: in-character send/receive and GM client IC state.
- `OOC`: out-of-character messages.
- `Viewport`: AO2 viewport rendering, asset fallback, and playback diagnostics.
- `MusicList`: music list packet, tree-build, local-scan, and playback diagnostics.
- `AreaVisualizer`: area navigator popup rebuild and refresh diagnostics.
- `SFX`: SFX, blip, and shout playback diagnostics.

Use a real category instead of embedding tags such as `[Viewport]` in the message text. Keep `Network` for packet text and use `IC` or `Viewport` for higher-level interpretation around those packets.

## Export
The debug console has an `Export` button next to `Clear`. It opens a Windows save-file dialog and writes the currently
visible console document, honoring the active category filters and any manual clear done in the window.

## GM Multi-Client Diagnostics
IC send now logs the selected profile, target network client, connection state, selected INI puppet id/name, local render character, emote, and message length before sending. `AOClient.SendICMessage` also logs packet metadata before the raw `MS#` packet. These logs are intended to diagnose cases where manual INI puppet selection leaves the local render character different from the selected server character slot.

## Development Storage
When the app starts under an attached debugger, the default save file path moves from `%APPDATA%/OceanyaClient/savefile.json` to `%APPDATA%/OceanyaClientDev/savefile.json`. This keeps Visual Studio development runs from mutating the user's normal productive client settings. `OCEANYA_CLIENT_PROFILE=Dev` or `Development` forces the same development profile for non-debugger launches.
