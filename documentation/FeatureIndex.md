# Feature Index

Use this file as the first stop before broad repository searches. It should point future work at the right files and focused docs fast.

## AO2 Chat And Log Behavior
- Doc: `Documentation/AO2-Chat-Config-Coloring-Guide.md`
- Main code: `AOBot-Testing/Agents/AOClient.cs`, `AOBot-Testing/Structures/ICMessage.cs`, `OceanyaClient/MainWindow.xaml.cs`, `OceanyaClient/Components/ICLog.xaml.cs`, `OceanyaClient/Features/Chat/Ao2TextLogWriter.cs`
- Notes: Covers IC/OOC packet handling, log formatting, AO2-facing display behavior, and AO2-compatible text-log files under the selected AO install `logs/<server>/` folder.

## AO2 Viewport
- Doc: `Documentation/AO2Viewport.md`
- Main code: `OceanyaClient/Features/Viewport/*`, `OceanyaClient/MainWindow.xaml`, `OceanyaClient/MainWindow.xaml.cs`
- AO2 reference: `AO2-Client/src/courtroom.cpp`, `AO2-Client/src/courtroom.h`, `AO2-Client/src/animationlayer.*`
- Notes: GM multi-client `V` button opens an owned 256x192 AO2 render surface for background, character, desk, effect, and chatbox layers. Each GM profile owns its own hidden/live viewport state; selecting a profile only changes which state is shown. The viewport right-click menu is dynamically sectioned in `AO2ViewportWindowContent` and exposes viewport, background, character, and chatbox actions. Chatbox art/geometry/font lookup is owned by `AO2ChatPreviewResolver`; it reads selected `config.ini` `theme`/`subtheme`/`theme_scaling_factor` and mirrors AO2 `get_asset_paths(...)` priority. Custom `[Options] chat=<token>` art and config use that AO2 misc/theme priority, while missing/empty `[Options] chat` stays empty and skips `misc/default`; custom art without custom geometry falls back through the active theme chain rather than preferred-theme guesses.

## Custom Context Menus
- Main code: `OceanyaClient/Utilities/ContextMenuSectionHelper.cs`
- Examples: `OceanyaClient/Components/Forms/CharacterFolderVisualizerWindow.xaml.cs`, `OceanyaClient/MainWindow.xaml.cs` (`MusicContextMenu_Opened`), `OceanyaClient/Features/Viewport/AO2ViewportWindowContent.xaml.cs`, `OceanyaClient/Components/ICMessageSettings.xaml.cs`
- Notes: Custom WPF context menus use disabled bold category title rows with separators between categories. New/touched C#-built menus should call `ContextMenuSectionHelper.AddHeader(...)`.

## GM Multi-Client Snapshot Restore
- Doc: `Documentation/GmMultiClientSnapshotRestore.md`
- Main code: `OceanyaClient/MainWindow.xaml.cs`, `OceanyaClient/Components/ICMessageSettings.xaml.cs`, `OceanyaClient/Components/Forms/CharacterSelectorWindow.xaml.cs`, `OceanyaClient/Components/Forms/OceanyaMessageBox.xaml.cs`, `Common/SaveFile.cs`
- Notes: Saves GM client profiles, selected client, INI puppets, local render character/emote, shownames, OOC names, position/effect/message properties, and restores the profile set after launching against a new server. Restore resolves occupied INI puppets before creating clients, treats other snapshot puppets as reserved choices, uses shared custom-button message boxes for conflicts, preserves the local iniswap character when the server puppet must change, and documents startup wait reductions around server probes, targeted asset refresh, and direct snapshot restore.

## GM Multi-Client Area Navigator
- Doc: `Documentation/AreaNavigator.md`
- Main code: `OceanyaClient/MainWindow.xaml`, `OceanyaClient/MainWindow.xaml.cs`, `AOBot-Testing/Agents/AOClient.cs`, `AOBot-Testing/Structures/AreaInfo.cs`
- AO2/server reference: `AO2-Client/src/courtroom.cpp`, `AO2-Client/src/packet_distribution.cpp`, `tsuserver3/server/area_manager.py`, `tsuserver3/server/client_manager.py`, `tsuserverCC/server/area_manager.py`, `tsuserverCC/server/client_manager.py`
- Notes: `FA`/`SM` define visible area rows; `ARUP` updates player counts/status/CM/lock by current row index. `RM` refreshes `FA` without a fresh ARUP snapshot, so the AO client preserves known row state by area name and treats new area counts as unknown until ARUP, `/getarea`, or `=== Areas ===` OOC data arrives. The popup is dark themed and its dimensions persist across sessions.

## GM Multi-Client Position Dropdown
- Doc: `Documentation/AO2Viewport.md`
- Main code: `OceanyaClient/Components/ICMessageSettings.xaml.cs`, `AOBot-Testing/Structures/Background.cs`, `AOBot-Testing/Agents/AOClient.cs`
- Notes: The IC position dropdown displays `default (<character side>)` as a value-backed empty position, then AO2 default positions, `design.ini` positions, and undeclared image-backed positions. Empty `AOClient.curPos` remains default mode across character changes; manual positions remain manual. Background resolution/cache refresh preserves AO2 mount priority: first matching `Globals.BaseFolders` background name wins when multiple mounts contain the same background folder name.

## GM Multi-Client Music List
- Doc: `Documentation/MusicList.md`
- Main code: `OceanyaClient/MainWindow.xaml`, `OceanyaClient/MainWindow.xaml.cs`, `AOBot-Testing/Agents/AOClient.cs`
- AO2/server reference: `AO2-Client/src/courtroom.cpp`, `AO2-Client/src/packet_distribution.cpp`, `AO2-Client/src/aomusicplayer.cpp`, `tsuserver3/server/network/aoprotocol.py`, `tsuserverCC/server/network/aoprotocol.py`
- Notes: `SM` carries areas followed by music; the first file-like entry marks the split and the preceding non-file entry is the first music category. `FM` refreshes music only. `MC` supports play/stop plus AO2 effect flags (`Fade Out Previous`, `Fade In`, `Synchronize`), but no network seek/timeline command. Those user-controlled effect toggles apply only to rows that resolve to server-recognized music tokens and can send direct `MC#`; arbitrary local/custom-command rows route through `/play` or OOC and cannot carry selected effect flags. The popup persists dimensions, renders `SERVER LIST` plus async-scanned `LOCAL FILES`, remembers collapsed categories, colors local found/missing tracks green/red, and can show full local asset paths. Popup refreshes now snapshot state and build the tree off the UI thread; per-row server music rendering must use the async local-music index instead of probing disk.

## Debug Console And Logging
- Doc: `Documentation/DebugConsoleAndLogging.md`
- Main code: `Common/CustomConsole.cs`, `OceanyaClient/Components/Forms/DebugConsoleWindow.xaml(.cs)`
- Notes: Structured log entries carry level and category metadata. The debug console filters System, Network, IC, OOC, Viewport, Music List, Area visualizer, and SFX logs. The Export button writes the currently visible filtered console document to a `.txt` file.

## Client Settings And Audio
- Doc: `Documentation/AO2Viewport.md`
- Main code: `OceanyaClient/Components/Forms/SettingsWindow.xaml(.cs)`, `OceanyaClient/Components/Forms/CallwordRuleEditorWindow.xaml(.cs)`, `OceanyaClient/Components/Forms/ExtraAudioRuleEditorWindow.xaml(.cs)`, `OceanyaClient/AudioSettings.cs`, `Common/SaveFile.cs`, `OceanyaClient/MainWindow.xaml`, `OceanyaClient/MainWindow.xaml.cs`
- AO2 reference: `AO2-Client/src/options.cpp`, `AO2-Client/src/courtroom.cpp`
- Notes: The shared dark settings dialog persists AO2-style music/SFX/blip sliders plus inverse `suppress_audio` as Unfocused Volume, client toggles, selected config.ini values, callword trigger rules, and extra audio rules. Callwords support AO2-compatible config.ini sync plus Oceanya-only message, showname, character, and emote triggers with optional custom/default SFX, plus optional whole-word matching for text triggers. Extra audio rule volume overrides matching blip/SFX/music volume directly, with a 0-200% slider and a positive numeric field for higher values. Rule creation uses modal editors, file pickers, audio preview, the reusable `AutoCompleteDropdownField` for finite dark dropdown choices, the reusable main-client character INI selector, database-viewer character lookup, editable blip/SFX/music dropdowns, and local catalogs instead of raw path/token typing. The viewport/audio playback bridge uses the saved volumes/rules when the viewport is visible.

## Save File And Update Persistence
- Doc: `Documentation/SaveFileAndUpdatePersistence.md`
- Main code: `Common/SaveFile.cs`, `OceanyaClient/App.xaml.cs`, `Common/OceanyaTestMode.cs`
- Notes: Normal builds store user settings in `%APPDATA%/OceanyaClient/savefile.json`, not beside the release executable. Deleting an old release folder and unpacking a new one preserves user state unless the user deletes `%APPDATA%/OceanyaClient`.

## Character File Creator
- Doc: `Documentation/CharacterFileCreator.md`
- Main code: `OceanyaClient/Components/Forms/AOCharacterFileCreatorWindow.xaml.cs`, `OceanyaClient/Features/CharacterCreator/AOCharacterFileCreatorBuilder.cs`, `OceanyaClient/Features/CharacterCreator/GeneratedAssetPathCollisionResolver.cs`
- Notes: Includes file organization, generated assets, duplicate-name collision handling, emote-tile interactions, and built-in asset viewers.

## Character Tagging
- Docs:
  - `Documentation/TaggingCharacters/CharacterTaggingMigrationGuide.md`
  - `Documentation/TaggingCharacters/CharacterFolderIdentitySummary.md`
  - `Documentation/TaggingCharacters/TaggingForWindowsTags.md`
- Main code: tagging-related workflows under `OceanyaClient` plus supporting data in `Documentation/TaggingCharacters`

## Release Packaging
- Doc: `Documentation/ReleasePackaging.md`
- Main code: `Directory.Build.props`, `OceanyaHivemindAgent/OceanyaHivemindAgent.csproj`, `OceanyaClient/OceanyaClient.csproj`
- Notes: Release builds package the client output as `Oceanya Client v<version>` and the zip now preserves that outer folder.

## File Hivemind
- Doc: `Documentation/AgentDocumentationWorkflow.md`
- Main code: `OceanyaClient/Features/FileHivemind/*`, `OceanyaHivemindAgent/Program.cs`, `Common/SaveFile.cs`
- Notes: Background agent lifecycle, saved connection state, and parent/child shutdown signaling live here.

## Client Setup Optimization (Startup Speed)
- Doc: `Documentation/ClientSetupOptimization.md`
- Main code: `OceanyaClient/App.xaml.cs`, `OceanyaClient/Components/Forms/InitialConfigurationWindow.xaml(.cs)`, `OceanyaClient/ClientAssetRefreshService.cs`, `OceanyaClient/StartupTimingLogger.cs`, `Common/SaveFile.cs`
- Notes: Phase 1 removed the fake loading screen (optional via checkbox) and deferred the asset-change file scan off the launch critical path. A timing log at `%AppData%\OceanyaClient\startup_timing.log` records per-phase milliseconds every session. See doc for remaining Phase 2+ bottlenecks (parallel client connections, XAML lazy-load, directory-mtime fast filter).

## Viewport Rendering Parity Gaps
- Doc: `Documentation/ViewportParityGaps.md`
- Notes: 9 gaps where incoming packets render differently (or not at all) in OceanyaClient vs AO2 reference viewport (2026-05-14, release/6-2). High: HP bars (#1), ambient music channels (#2). Medium: music loop regions (#3), WTCE audio (#4), verdict duration (#5), effects cull/max_duration (#6,#7). Low: talking flag hardcoded (#8), DeskMods 4/5 pair visibility (#9). Does NOT cover send-side gaps (those are in AO2ParityGaps.md).

## AO2 Parity Gaps
- Doc: `Documentation/AO2ParityGaps.md`
- Notes: Point-in-time (2026-05-14, branch release/6-2) catalog of every user-facing AO2 feature absent or incomplete in OceanyaClient. Covers 19 gaps with user descriptions, current state, technical specs (packets, files, implementation notes). High-priority gaps: Pairing UI (#1), Evidence management (#2), Judge controls/HP bars (#3). Medium: Timer TI# (#4), Spectator (#5), Typing indicator (#6), Mute (#8), Mod call (#9), RT# sending (#19).

## UI Automation Tests (Smoke + Online)
- Doc: `UiAutomationTests/README.md`
- Main code: `UiAutomationTests/FirstWaveSmokeTests.cs`, `UiAutomationTests/OnlineLaneTests.cs`, `UiAutomationTests/FlaUiSmokeApp.cs`, `UiAutomationTests/SmokeFixturePaths.cs`, `UiAutomationTests/OnlineFixturePaths.cs`
- Fixture assets: `UnitTests/TestAssets/FlaUISmoke/` (smoke + shared), `UnitTests/TestAssets/FlaUIOnline/` (online savefile)
- CI: `.github/workflows/ui-smoke.yml` (Smoke only; Online is local/self-hosted only)
- Notes: Categories are now **Smoke** (offline deterministic), **Online** (loopback transport integration), and **GmPacket** (deterministic GM multi client packet-validation subset, also part of `Online`). Requires interactive Windows desktop for UIA. Readiness polling via `Oceanya.ReadyMarker` descendant. Add-client flows use `CharacterSelectorWindow` anchors (`CharacterSelector.Cancel`, `CharacterSelector.ClientName`, `CharacterSelector.FirstSelectableCard`, `CharacterSelector.Character.{Name}`), not the old `InputDialog`. `GmPacketLoopbackServer` controls fixture characters and supports both TCP and WebSocket endpoints, avoiding dependency on a real localhost tsuserver3 installation. Test fixture args disable GM snapshot and viewport window persistence to prevent cross-test restore state. Screenshots on failure in `<results-dir>/UiAutomationArtifacts/Screenshots/`.
