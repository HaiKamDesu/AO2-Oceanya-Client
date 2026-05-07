# Feature Index

Use this file as the first stop before broad repository searches. It should point future work at the right files and focused docs fast.

## AO2 Chat And Log Behavior
- Doc: `Documentation/AO2-Chat-Config-Coloring-Guide.md`
- Main code: `AOBot-Testing/Agents/AOClient.cs`, `AOBot-Testing/Structures/ICMessage.cs`, `OceanyaClient/MainWindow.xaml.cs`, `OceanyaClient/Components/ICLog.xaml.cs`
- Notes: Covers IC/OOC packet handling, log formatting, and AO2-facing display behavior.

## AO2 Viewport
- Doc: `Documentation/AO2Viewport.md`
- Main code: `OceanyaClient/Features/Viewport/*`, `OceanyaClient/MainWindow.xaml`, `OceanyaClient/MainWindow.xaml.cs`
- AO2 reference: `AO2-Client/src/courtroom.cpp`, `AO2-Client/src/courtroom.h`, `AO2-Client/src/animationlayer.*`
- Notes: GM multi-client `V` button opens an owned 256x192 AO2 render surface for background, character, desk, effect, and chatbox layers.

## Client Settings And Audio
- Doc: `Documentation/AO2Viewport.md`
- Main code: `OceanyaClient/Components/Forms/SettingsWindow.xaml(.cs)`, `OceanyaClient/AudioSettings.cs`, `Common/SaveFile.cs`, `OceanyaClient/MainWindow.xaml`, `OceanyaClient/MainWindow.xaml.cs`
- AO2 reference: `AO2-Client/src/options.cpp`, `AO2-Client/src/courtroom.cpp`
- Notes: The shared dark settings dialog persists AO2-style music/SFX/blip sliders, client toggles, selected config.ini values, callword rules, and extra audio rules. The viewport/audio playback bridge uses the saved volumes/rules when the viewport is visible.

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

## UI Automation Tests (Smoke + Online)
- Doc: `UiAutomationTests/README.md`
- Main code: `UiAutomationTests/FirstWaveSmokeTests.cs`, `UiAutomationTests/OnlineLaneTests.cs`, `UiAutomationTests/FlaUiSmokeApp.cs`, `UiAutomationTests/SmokeFixturePaths.cs`, `UiAutomationTests/OnlineFixturePaths.cs`
- Fixture assets: `UnitTests/TestAssets/FlaUISmoke/` (smoke + shared), `UnitTests/TestAssets/FlaUIOnline/` (online savefile)
- CI: `.github/workflows/ui-smoke.yml` (Smoke only; Online is local/self-hosted only)
- Notes: Categories are now **Smoke** (offline deterministic), **Online** (generic loopback transport integration), **GmPacket** (deterministic GM multi client packet-validation subset, also part of `Online`), and **OnlineLocalhost** (optional real local `ws://localhost:50001`). Requires interactive Windows desktop for UIA. Readiness polling via `Oceanya.ReadyMarker` descendant. Screenshots on failure in `<results-dir>/UiAutomationArtifacts/Screenshots/`.
