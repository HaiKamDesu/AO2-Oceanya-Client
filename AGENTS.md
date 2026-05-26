## Repository Structure
- **AO2-Client/**: Git submodule pointing to the original Attorney Online client. Reference code only.
- **tsuserver3/**: Git submodule pointing to the main Attorney Online server implementation. Reference code only.
- **tsuserverCC/**: Git submodule pointing to a widely used Attorney Online server fork. Reference code only.
- **KFO-Server/**: Git submodule pointing to the KFO server used by Attorney Online Vidya. Reference code only.
- **AOBot-Testing/**: Core AO2 protocol/client implementation.
- **Common/**: Shared utilities and persistence helpers.
- **OceanyaClient/**: Main WPF client implementation.
- **OceanyaHivemindAgent/**: Standalone tray/background process for File Hivemind sync.
- **AO2AIBot/**: AI agent logic, prompt assembly, parsing, and provider integration.
- **UnitTests/**: NUnit 4 test suite.
- **Documentation/**: Repository documentation for humans and future AI agents.

## Agent Communication
- Always use the repository `caveman` skill for agent responses unless the user explicitly says to stop caveman mode or normal mode is required for safety/clarity.

## Reference Code Usage
The code in `AO2-Client`, `tsuserver3`, `tsuserverCC`, and `KFO-Server` exists for reference only. Use it to understand:
- AO2 network protocols and packet structures
- game mechanics and chat/log behavior
- server-side packet handling, area management, permissions, and moderation behavior
- common AO2 customization patterns
- UI workflows and feature parity targets

Do **not** copy this code directly into the C# projects. Use it to understand behavior, then implement equivalent logic natively in this repository.

## Working with Git Submodules
- The repository includes `AO2-Client`, `tsuserver3`, `tsuserverCC`, and `KFO-Server` as git submodules.
- First-time clone: `git clone --recurse-submodules`
- If already cloned without submodules: `git submodule update --init --recursive`

## Build And Test Verification
Run from the project root. Use this exact dotnet path on WSL:

```bash
/mnt/c/Program\ Files/dotnet/dotnet.exe build "Oceanya Client.sln"
/mnt/c/Program\ Files/dotnet/dotnet.exe test UnitTests/UnitTests.csproj --filter "FullyQualifiedName~UnitTests"
```

**IMPORTANT — run build and test as separate commands, never chained.**
Chaining them with `&&` (e.g. `build ... && test ...`) causes a race on the
`obj/` output directory: the test runner can lock files that the build just
wrote, producing spurious lock errors and making it look like tests failed
when they did not. Always issue the build command first, wait for it to
succeed, then issue the test command in a separate call.

Run a single test class or method:

```bash
/mnt/c/Program\ Files/dotnet/dotnet.exe test UnitTests/UnitTests.csproj --filter "ClassName=UnitTests.AOClientAgentResponseParserTests"
/mnt/c/Program\ Files/dotnet/dotnet.exe test UnitTests/UnitTests.csproj --filter "FullyQualifiedName~MethodName"
```

Always run build and test after any code change to verify the work.

For normal agent verification, run the `UnitTests` project only. Do **not**
run the live FlaUI/UI automation test project unless the user explicitly asks
for it.

The app version is centrally defined in `Directory.Build.props` via `OceanyaAppVersion`.

## UI Automation Execution
- UI automation is **opt-in only** for agents. Do **not** run `UiAutomationTests`
  or other live desktop tests unless the user specifically requests them.
- FlaUI/UIA3 tests must be run on the **Windows side** in an **active interactive desktop session**. Do not treat WSL-only execution as a valid runtime check for these tests.
- Verified desktop check: `query session` should show the target user (usually `Usuario`) on `console` or another `Active` session.
- Preferred invocation method from this repo when working through WSL/Codex:

```bash
/bin/bash -lc '"/mnt/c/Windows/System32/query.exe" session'
/bin/bash -lc '"/mnt/c/Program Files/dotnet/dotnet.exe" build "Oceanya Client.sln" --configuration Debug'
/bin/bash -lc '"/mnt/c/Program Files/dotnet/dotnet.exe" test "UiAutomationTests/UiAutomationTests.csproj" --configuration Debug --no-build --filter "Category=Smoke" --logger "trx;LogFileName=ui-smoke-results.trx" --results-directory TestResults/UiSmoke'
/bin/bash -lc '"/mnt/c/Program Files/dotnet/dotnet.exe" test "UiAutomationTests/UiAutomationTests.csproj" --configuration Debug --no-build --filter "Category=GmPacket" --logger "trx;LogFileName=ui-gmpacket-results.trx" --results-directory TestResults/UiGmPacket'
```

- The same Windows-native commands can also be run directly from PowerShell/CMD on Windows if preferred.
- Results and artifacts:
  - TRX files go under the `--results-directory` you pass, e.g. `TestResults/UiGmPacket/`.
  - Failure screenshots go under `<results-directory>/UiAutomationArtifacts/Screenshots/`.
- Agent execution rules for FlaUI runs:
  - Treat every FlaUI `dotnet test` invocation as **exclusive interactive desktop work**. Never run it in parallel with another FlaUI lane, another UI automation tool, or any other action that can steal focus, keyboard input, mouse input, or window z-order.
  - Run FlaUI categories **sequentially only**. Never overlap `Smoke`, `GmPacket`, or `Online`, even if they target different result directories.
  - Build and FlaUI test execution must always be **separate commands**, never chained with `&&` or any equivalent shell composition.
  - Before launching FlaUI, verify an active interactive Windows desktop session with `query session`.
  - After launching `dotnet test`, treat the run as **still active until that specific process exits and returns an exit code**. A long pause in console output does **not** mean the run is done.
  - Do **not** infer completion from silence, partial TRX creation, screenshots appearing, or app windows opening/closing. Those are intermediate side effects only.
  - Preferred start/finish rule for agents: the run starts when the launched `dotnet test UiAutomationTests/UiAutomationTests.csproj ...` process is alive, and it finishes only when that same process terminates cleanly. If the agent loses confidence about process state, separately verify that no lingering `dotnet.exe`, `testhost.exe`, or `OceanyaClient.exe` from that run remains before declaring the lane finished.
  - Practical repo-safe check from WSL: wait on the actual launched command until it exits, then, if needed, run Windows-side process checks such as `tasklist.exe /FI "IMAGENAME eq dotnet.exe"`, `tasklist.exe /FI "IMAGENAME eq testhost.exe"`, and `tasklist.exe /FI "IMAGENAME eq OceanyaClient.exe"` to confirm nothing from that lane is still alive.
  - While FlaUI is running, do nothing else that interacts with the desktop. Do not start another test command, do not click/type into the desktop, and do not launch another GUI app.
  - If a FlaUI run stops making progress and the launched process does not exit, report it as a **hang/timeout**. Do not pretend the lane passed or failed until the process actually terminates and you have its exit status, or you intentionally stop it and report that interruption honestly.
- Do not do these:
  - Do **not** chain build and test with `&&`.
  - Do **not** rely on the plain WSL `dotnet test` path for FlaUI validation.
  - Do **not** run UIA3/SendKeys tests in headless/service-only/non-interactive sessions.
  - Do **not** run other interactive apps over the desktop while the tests are active; they can steal focus and break `SendKeys`.
- Category targeting:
  - `Smoke`: deterministic offline suite
  - `Online`: generic loopback integration suite
  - `GmPacket`: deterministic GM multi client packet-validation subset

## Project Structure

| Project | Purpose |
|---|---|
| `OceanyaClient` | WPF frontend, the main executable. Multi-client AO2 controller with AI integration. |
| `AOBot-Testing` (`AO2.csproj`) | Core AO2 protocol layer. `AOClient`, packet/message structures, character parsing, and transport logic. |
| `AO2AIBot` | AI agent logic: prompt building, response parsing, client control. Provider-agnostic (OpenAI + Ollama). |
| `Common` | Shared utilities: `SaveFile`/`SaveData`, `Globals`, `CustomConsole`, `CountdownTimer`, sync helpers. |
| `OceanyaHivemindAgent` | Standalone tray/background app for File Hivemind sync. |
| `UnitTests` | NUnit 4 test project referencing the main projects. |

## Repository Navigation Map
Use this map to avoid repeated broad searches. Before running wide `rg`, `find`, or directory exploration for a feature, check this section and `Documentation/FeatureIndex.md`. If the map or linked docs identify the relevant files/classes clearly enough, use those paths directly. If the needed information is missing, incomplete, outdated, or ambiguous, inspect the repository as needed; before finishing, update this map with any navigation knowledge that would help a future agent use fewer tokens.

Keep entries brief and lookup-friendly. Record:
- user-language mappings: what the user calls a feature versus the actual project, window, class, or file names
- repository locations, module ownership, important forms/windows, classes, scripts, tests, and workflows
- naming conventions or search terms that quickly narrow future work
- status markers: `Confirmed`, `Likely`, `Outdated?`, or `Needs verification`

When updating the map, prefer exact paths and identifiers over prose. Distinguish confirmed facts from guesses. Delete or correct stale entries when work proves them wrong.

### Current Map
| User phrase / topic | Code meaning and fast path | Status |
|---|---|---|
| "AO2 protocol", "packets", "client transport" | `AOBot-Testing/` (`AO2.csproj`); start with `AOClient` and structures under `AOBot-Testing/Structures/`. | Confirmed |
| "IC packet parity", "MS# packet", "Attorney Online Vidya cannot talk IC" | Outgoing IC is built in `AOClient.SendICMessage` and serialized by `ICMessage.GetCommand(...)`. AO2 outbound order is core 15 fields, then `CCCC_IC_SUPPORT` adds only `showname`, `other_charid`, `self_offset`, `immediate`; `LOOPING_SFX`, `ADDITIVE`, `EFFECTS`, and `CUSTOM_BLIPS` append their own blocks independently. Do not send receive-only echo fields (`OtherName`, `OtherEmote`, `OtherOffset`, `OtherFlip`) in outbound `MS#`. KFO/Vidya source lives in `KFO-Server/server/network/aoprotocol.py` `net_cmd_ms`: it validates exact field counts, rejects mismatched `cid`, rejects nonempty `showname` when area showname changes are disabled, and may not accept AO2 `CUSTOM_BLIPS` tail despite advertising it on some deployments. For KFO, `AOClient.SendICMessage` suppresses implicit showname fallback and custom-blips/slide fields; before send it aligns INIPuppet to `CurrentINI` when that character exists and is available on the server, preventing stale `Franziska` + `KamLoremaster` packets. Server-selected `iniPuppetID` must come from `PV#<player>#CID#<charId>#%` confirmation after `CC#`; do not trust requested/snapshot char IDs. Tests: `UnitTests/CustomUnitTests.cs` compact AO2 layout tests; `UnitTests/NetworkTests.cs` showname, PV confirmation, KFO packet, and send-time INIPuppet alignment tests. | Confirmed |
| "Vidya /getarea", "Clients in [area]", "vanilla /getarea", "pairing studio full server list" | `/getarea` parsing lives in `AOBot-Testing/AO2Parser.cs`; Vidya-style `$H: = Clients in [8] Lounge (users: 1) [IDLE] =` and vanilla-style `AO Official Server (Vanilla): === Basement ===` + `[8 users][IDLE]` are parsed by `ParseGetAreaDetailed`, while `AOClient.ApplyAreaInfoFromGetAreaMessage` updates `AreaInfo` users/status from the same headers. Tests: `UnitTests/NetworkTests.cs` `ParseGetArea_SupportsVidyaClientsInHeaderOutput`, `HandleMessage_VidyaGetAreaUpdatesAreaAndRoster`, `ParseGetArea_SupportsVanillaAreaHeadingOutput`, and `HandleMessage_VanillaGetAreaUpdatesAreaInfoAndRoster`. | Confirmed |
| "missing OOC welcome", "OOC log empty on join", "restored first direct client OOC blank", "handshake OOC" | `AOClient.ReceiveMessageAsync` dispatches every packet through `HandleMessage(...)` before `WaitForPacketAsync` checks the handshake predicate, so CT packets received during connect should reach existing OOC handlers. Direct internal clients attach handlers in `MainWindow.AddClientInternalAsync` before `ConnectClientAsync`; direct snapshot restore also attaches handlers to the first preconnected client before `ConnectClientAsync`, and `AttachDirectClientMessageHandlers` is idempotent. Regression tests: `UnitTests/NetworkTests.cs` `Connect_TcpEndpoint_DispatchesOocReceivedDuringHandshake`; `UnitTests/Phase1ReleaseConfidenceTests.cs` `MainWindow_DirectClientReceiveHandlers_AttachOnceAndPreservePreconnectOoc`. | Confirmed |
| "AO2 text logs", "automatic logging", "logs folder", "AO log parity", "Find in all Logs", "Find in log folder" | `OceanyaClient/Features/Chat/Ao2TextLogWriter.cs`; hooked from `MainWindow.AddLoggedIcMessageWithContext`, `AddLoggedIcActionMessage`, and `AddLoggedOocMessage`; reads selected `config.ini` via `Ao2ConfigIniSettings` and writes under `<AO install>/logs/<server>/`. If selected config is `<AO install>/base/config.ini`, log root is `<AO install>/logs` like AO2, not `base/logs`. Text-log session path is created when `automatic_logging_enabled` or `demo_logging_enabled` is true. Settings UI page: `OceanyaClient/Components/Forms/SettingsWindow.xaml(.cs)` `Logging` page exposes visible log maximum, IC inversion, `automatic_logging_enabled`, `demo_logging_enabled`, Open Log Folder, and `Find in all Logs...`. IC/OOC log right-click menus expose `Find in log...` and `Find in log folder...`. All-log search UI: `OceanyaClient/Components/Forms/FindInAllLogsWindow.xaml(.cs)`; file scanner: `OceanyaClient/Features/Chat/AllLogSearchService.cs`; shared matcher: `OceanyaClient/Components/LogTextMatcher.cs`. The popup reports total `.log` files, total text lines scanned, elapsed search time, matching-file count, and total matches. Large selected logs use a bounded match-window preview to avoid WPF freezes. Result rows have right-click actions to open in Notepad or reveal in Explorer. OOC text-file rows use `[OOC][timestamp] name: message`; writes and searches use read/write file sharing for AO2 coexistence. | Confirmed |
| "main client", "WPF app", "GM multi-client" | `OceanyaClient/`; primary UI is `MainWindow`; startup modes are in `StartupFunctionalityCatalog`. | Confirmed |
| "GM snapshot restore", "restored clients", "stale viewport background", "wrong INIPuppet after reconnect" | `OceanyaClient/MainWindow.xaml.cs` owns `CaptureGmMultiClientSnapshot`, `RestoreGmMultiClientSnapshotAsync`, `ApplySnapshotStateToClient`, and `ResolveSnapshotRequestedPuppet`. `Common/SaveFile.cs` `GmMultiClientSnapshot` stores `ServerEndpoint`/`ServerName`; restore never reapplies saved `Background` to live clients and prefers `LocalCharacterName` over stale saved `IniPuppetName` when that local character exists on the active server. `AOBot-Testing/Agents/AOClient.cs` reconnect uses `RestoreIniPuppetAfterReconnectAsync` so old local character/emote is not paired with a newly auto-selected server INIPuppet; `SendICMessage` also realigns to current local character before send when possible. Tests: `UnitTests/TestabilityHardeningTests.cs` snapshot endpoint/puppet cases and `UnitTests/NetworkTests.cs` send-time INIPuppet alignment. | Confirmed |
| "AI bot", "agent", "prompt", "AI response parser" | `AO2AIBot/`; pipeline uses `AOClientAgentController`, `AiChatCompletionService`, `AO2AiBotPromptBuilder`, `AO2AiBotPromptCatalog`, and `AOClientAgentResponseParser`. | Confirmed |
| "save data", "settings persistence" | `Common/`; start with `SaveFile`, `SaveData`, and `Globals`. | Confirmed |
| "update old Oceanya client", "will savefile migrate", "user deleted old folder", "unit tests wiped production savefile", "Delete Savefile" | `Common/SaveFile.cs` + `OceanyaClient/Components/Forms/InitialConfigurationWindow.xaml.cs` + `OceanyaClient/Components/Forms/AdvancedFeatureFlagsWindow.xaml(.cs)` + `OceanyaClient/Features/Startup/SaveFileDeletionService.cs` + `Documentation/SaveFileAndUpdatePersistence.md`; v6.1 and v6.2 use the same `%APPDATA%/OceanyaClient/savefile.json` path. Debug/dev launches use `%APPDATA%/OceanyaClientDev/savefile.json`; NUnit/testhost runs default to temp `OceanyaClientUnitTests`; `--test-savefile=...` is honored before first load. If saved `ConfigIniPath` points into a deleted release folder, startup checks the current app folder for `config.ini`, `<same parent>/config.ini`, and `base/config.ini`. Advanced feature flagging has a red danger-zone `Delete Savefile` button that validates the active path, stops File Hivemind first, unregisters Hivemind autostart, deletes the active save directory, then shuts down. Save-load diagnostics go beside the active savefile; unreadable saves are copied to `savefile.unreadable.<timestamp>.json` before falling back to defaults. | Confirmed |
| "auto updater", "GitHub Releases updater", "update popup", "Skip this version", "Update to vX.Y.Z", "test update channel" | `OceanyaClient/Features/Updates/*` owns strict channel-aware version parsing, public GitHub release/manifest discovery, asset/hash validation, staging, and zip safety validation. `UpdateEnvironment` selects Stable for Release/public builds and Test for Debug/developer builds; Stable state/cache uses `%LOCALAPPDATA%/OceanyaClient/Updates`, Test uses `%LOCALAPPDATA%/OceanyaClientDev/Updates`, and `SaveFile.Data.Updater.Stable/Test` keep skip/last-seen state separate. `InitialConfigurationWindow.xaml(.cs)` starts the async check after load and shows the bottom-left gray update link. Popup: `OceanyaClient/Components/Forms/UpdateAvailableWindow.xaml(.cs)`. External replace-on-restart executable: `OceanyaUpdater/Program.cs`; packaged by `OceanyaHivemindAgent/OceanyaHivemindAgent.csproj`. Build outputs: Release `OceanyaClient/bin/Release/Github Release/Oceanya Client <version>/`; Debug `OceanyaClient/bin/Debug/Github Release Test/Oceanya Client <version>-test/`. Release manifest/checksum workflow: `.github/workflows/release.yml`. Tests: `UnitTests/AutoUpdaterTests.cs`. | Confirmed |
| "refresh assets warning", "full asset refresh required", "config.ini path changed popup", "asset refresh marker", "unit tests make me refresh characters" | `OceanyaClient/ClientAssetRefreshService.cs` owns forced-refresh reason detection and the active profile marker at `<directory of SaveFile.CurrentStoragePath>/cache/asset_refresh_marker.json` (production resolves to `%APPDATA%/OceanyaClient/cache/...`; unit tests resolve to their temp savefile directory). `InitialConfigurationWindow.BuildForcedRefreshPrompt` displays it at startup. Tests live in `UnitTests/GoogleDriveSyncTests.cs` under `ClientAssetRefreshServiceTests` plus marker-path isolation in `UnitTests/TestabilityHardeningTests.cs`. | Confirmed |
| "File Hivemind", "background sync agent", "exit background sync", "stop Hivemind before deleting/updating" | `OceanyaHivemindAgent/` plus launcher integration in `OceanyaClient`; stop signal name lives on `FileHivemindBackgroundAgentCommandLine.AgentStopSignalEventName`, stopped ack lives on `AgentStoppedSignalEventName`, and `FileHivemindAgentStopCoordinator` waits for confirmed stop before save-folder deletion. `OceanyaUpdater/Program.cs` sends the same stop signal and now also waits for the stopped ack before replacing files, with force-stop only as updater fallback. | Confirmed |
| "tests", "unit tests" | `UnitTests/`; normal verification runs only this project unless the user explicitly asks for UI automation. | Confirmed |
| "UI automation", "FlaUI", "desktop smoke tests" | `UiAutomationTests/`; opt-in only, Windows interactive desktop required, run categories sequentially. Fixture args live in `SmokeFixturePaths` / `OnlineFixturePaths` and disable GM snapshot + viewport persistence for isolation. | Confirmed |
| "AO2 reference client/server behavior", "KFO/Vidya server behavior" | Reference-only submodules: `AO2-Client/`, `tsuserver3/`, `tsuserverCC/`, `KFO-Server/`. Read for behavior, do not copy code directly. | Confirmed |
| "image asset viewer", "asset preview dialog", "animation preview" | `OceanyaClient.Utilities.AssetImageViewerDialog.Show(...)`; animation playback uses `AnimationTimelinePreviewController`. | Confirmed |
| "dark ComboBox", "searchable dropdown" | Copy the dark ComboBox pattern from `InitialConfigurationWindow.xaml` / `CharacterFolderVisualizerWindow.xaml`; use `AutoCompleteComboBoxBehavior` for editable searchable dropdowns. | Confirmed |
| "viewport lag", "background loading slow", "character sprite freeze", "async loading" | BG/desk: `SetPlacedAnimatedImage` in `AO2ViewportControl.xaml.cs` — async `Task.Run` + `Dispatcher.BeginInvoke` when not cached. Character/pair: `SetCharacterAnimatedImageAsync` — same pattern with an `onPlayerReady` callback that chains into the pre-animation → speaking flow. Pre-animation timing is driven by `handlePreAnimationPlayer` in `RenderPreAnimationThenSpeaking`; the callback fires synchronously on cache hit or via dispatcher after background decode. Cache check via `Ao2AnimationPreview.IsAnimationCached`. | Confirmed |
| "testimony overlay", "WITNESS TESTIMONY", "RT packet", "judgeruling", "WT/CE" | `AO2ViewportControl.xaml.cs` — `HandleRtPacket`, `ShowTestimonyOverlay`, `ShowWtceOverlay`; asset resolver `ResolveTestimonyOverlayImage` / `ResolveWtceOverlayImage`; triggered by `AOClient.OnRtReceived` which fires on RT# packets. | Confirmed |
| "sticker", "character sticker" | `ShowSticker` in `AO2ViewportControl.xaml.cs`; asset resolver `ResolveStickerImage`; resolves `sticker/{characterName}` from theme roots. | Confirmed |
| "evidence overlay", "evidence presentation" | `ShowEvidenceOverlay` in `AO2ViewportControl.xaml.cs`; uses `GetEvidenceImagePath` from `AOClient`; asset resolver `ResolveEvidencePresentationImage` / `ResolveEvidenceIconImage`; driven by `ICMessage.EvidenceID`. | Confirmed |
| "chat arrow" | `ShowChatArrow` / `StopChatArrow` in `AO2ViewportControl.xaml.cs`; `ChatArrowImage` in XAML row 1; shown after `CompleteChatTextReveal`; asset resolver `ResolveChatArrowImage(miscToken)`. Position comes from `chat_arrow` in `courtroom_design.ini` via `AO2ChatPreviewControl.GetChatArrowBounds()` (default FullChar: 245, 84, 11, 9). Art follows the current message `[Options] chat` misc token before theme/default art, case-insensitively; Akihiko/P4 intentionally resolves `misc/p4/Chat_Arrow.*`, a transparent 1x1 arrow. `AO2ViewportControl.ReloadThemeLayout()` refreshes a visible arrow image, not just bounds, so theme swaps do not leave stale default-theme arrow art. | Confirmed |
| "custom chatbox", "chatbox P5", "Open in file explorer chatbox", "character chat misc", "chat=default" | `AO2ViewportAssetResolver.ResolveCharacterChatToken(...)` reads `[Options] chat`; missing/empty chat stays an empty AO2 misc token and must not become `misc/default`. Oceanya intentionally diverges from AO2 for `[Options] chat=default`: treat literal `default` like no override so the active theme default chatbox is used instead of `misc/default`. `AO2ChatPreviewResolver.Resolve(...)` reads selected `config.ini` `theme`/`subtheme`/`theme_scaling_factor` and resolves chatbox art/fonts/design/config through AO2 `get_asset_paths(...)` priority; custom art without custom `courtroom_design.ini`/`courtroom_fonts.ini` falls back through the active theme chain, not preferred-theme guesses or unrelated `misc/default` geometry. WPF message text keeps a 4 px document margin to match Qt placement. `AO2ChatPreviewResolver.ResolveChatboxDirectoryPath(...)` backs the viewport Chatbox context-menu explorer target. | Confirmed |
| "slide transition", "background slide", "position change animation" | `AnimateBackgroundSlide` in `AO2ViewportControl.xaml.cs`; triggered when `ICMessage.Slide == true` and position changes; animates `Canvas.LeftProperty` on BG and Desk images with 500ms InOutCubic easing. | Confirmed |
| "viewport tests", "viewport parity tests", "RT# test", "LE# test" | `UnitTests/ViewportParityTests.cs` — `AO2ViewportParityPacketTests` (RT#/LE# AOClient events) and `AO2ViewportParityAssetResolverTests` (testimony/wtce/sticker/chat-arrow/evidence resolver filesystem tests). | Confirmed |
| "viewport settings", "change theme", "AO2 theme selector", "theme/subtheme dropdown" | Settings page: `OceanyaClient/Components/Forms/SettingsWindow.xaml(.cs)` `Viewport` page. Theme discovery: `OceanyaClient/Features/Viewport/AO2ThemeCatalog.cs` scans selected config folder first, then `Globals.BaseFolders`, with AO2-style subtheme options (`server`, `default`, excluding `server/default/effects/misc`). Context menu entry and live reload are in `OceanyaClient/Features/Viewport/AO2ViewportWindowContent.xaml.cs`; control layout reload is `AO2ViewportControl.ReloadThemeLayout()`. `MainWindow.OnViewportSurfaceLayoutChanged()` must preserve the user-resized outer viewport window on theme or chatbox-overlap layout changes; only first-time/no-saved-state layout snaps to native surface size. | Confirmed |
| "PiP viewport restores main size", "viewport takes PiP size", "picture in picture state" | `Common/SaveFile.cs` `ViewportWindowState.WindowKind` tags saved states as `main_viewport` or `picture_in_picture_viewport`; normalization repairs swapped/mislabeled states and resets a legacy main state that exactly duplicates a small PiP state. Runtime captures live in `MainWindow.CaptureViewportWindowState()` and `CapturePictureInPictureViewportWindowState(...)`; size/location/WndProc handlers guard sender/HWND identity so normal and PiP windows cannot save through each other's callbacks. Tests: `UnitTests/TestabilityHardeningTests.cs` viewport/PiP normalization cases. | Confirmed |
| "music packet tests", "MC# test", "OnMusicChanged test", "music not looping like AO2" | `UnitTests/MusicPacketTests.cs` — `MusicPacketHandlingTests`; covers MC# → `OnMusicChanged`, `OnIcActionReceived`, loop/channel/effects fields, stop, server-initiated, missing-fields guards, and escaped URL query strings. Incoming loop parity lives in `AOBot-Testing/Agents/AOClient.HandleMusicPacket`: mimic AO2 `Courtroom::handle_song`, where missing loop field does not loop and only literal loop field `1` loops; tsuserver3/tsuserverCC/KFO normalize server-side song length/loop state to `1` when they want client-side looping. HTTP/HTTPS/FTP playback goes through `AO2BlipPreviewPlayer.TrySetBlip`; URL streams need `BassFlags.Loop` plus end-sync restart to loop like AO2. | Confirmed |
| "music effect toggles", "Fade Out Previous", "Fade In", "Synchronize", "/play music" | `OceanyaClient/MainWindow.xaml.cs` (`MusicContextMenu_Opened`, `PlayMusicItemAsync`, `TryResolveServerRecognizedMusicToken`) + `OceanyaClient/Features/Viewport/AO2ViewportAudioManager.cs`; AO2 effect flags are user-controllable only for rows that resolve to server-recognized music tokens and send direct `MC#`. Arbitrary `LOCAL FILES`, unrecognized `FREQUENTLY USED`, and `CUSTOM COMMANDS` use `/play` or OOC, so selected effect flags cannot be sent for them. | Confirmed |
| "area navigator", "A button freezes", "area visualizer log" | `OceanyaClient/MainWindow.xaml(.cs)` — `RefreshAreaNavigatorForCurrentClient`, `ScheduleAreaNavigatorRefresh`, `btnAreaNavigator_Click`; popup opens immediately, then area rows rebuild off-thread. Debug category is `AreaVisualizer`. | Confirmed |
| "music list freezes", "M button freezes", "Music List log" | `OceanyaClient/MainWindow.xaml(.cs)` — `RefreshMusicListForCurrentClient`, `ScheduleMusicListRefresh`, `BuildMusicListItems`, `RefreshLocalMusicAssetsAndRefreshAsync`; local music scan and tree row construction stay off the UI thread, and server-row rendering uses the local music index instead of disk probing. Debug category is `MusicList`. | Confirmed |
| "debug console export", "server console debug log export" | `OceanyaClient/Components/Forms/DebugConsoleWindow.xaml(.cs)`; `BtnExportLog_Click` saves the currently visible filtered console document through a Windows `.txt` save dialog. Categories include `MusicList` and `AreaVisualizer`. | Confirmed |
| "add client test", "CharacterSelectorWindow", "CharacterSelector.FirstSelectableCard" | Add-client UI tests now use `CharacterSelectorWindow`, not `InputDialog`. Automation anchors: `CharacterSelector.Cancel`, `CharacterSelector.ClientName`, `CharacterSelector.FirstSelectableCard`, and `CharacterSelector.Character.{Name}`. | Confirmed |
| "viewport FlaUI test", "viewport smoke test", "Viewport.Host" | `UiAutomationTests/ViewportSmokeTests.cs` — Category Smoke; opens viewport via `Main.Viewport.Open` and waits for `Viewport.Host` automation anchor in `AO2ViewportWindowContent`. `AO2ViewportWindowContent.xaml.cs` marks automation ready in constructor and on Loaded; test mode disables persisted viewport auto-restore. | Confirmed |
| "GM packet FlaUI tests", "GmPacket", "Online category duplicates GM" | `UiAutomationTests/GmMultiClientPacketTests.cs` + `GmPacketTestInfrastructure.cs`; deterministic loopback packet tests use explicit selector character anchors and a controllable loopback server for both TCP and WebSocket endpoints. `Category=Online` also includes these GM packet tests plus `OnlineLaneTests`. | Confirmed |
| "position dropdown", "default pos", "all possible positions", "undeclared positions" | `OceanyaClient/Components/ICMessageSettings.xaml.cs` builds `PositionDropdown`; `Background.GetAo2PositionOptions()` returns AO2 default positions + `design.ini` positions + undeclared image-backed positions; `AOClient.curPos == ""` means `default (<current character side>)` and follows character changes unless `switchPosWhenChangingINI` is enabled. | Confirmed |
| "pairing studio", "pair button", "pair with character", "pair offset", "pair layer order" | IC panel button: `OceanyaClient/Components/ICMessageSettings.xaml` `btnPairingStudio` uses `Resources/Buttons/pair_button.png`; dialog: `OceanyaClient/Components/Forms/CharacterPairingStudioWindow.cs`; outgoing AO2-compatible state lives on `AOClient.PairTargetCharId`, `PairTargetCharacterName`, `PairLayerOrder`, and `SelfOffset`; `AOClient.SendICMessage` serializes pair target into compact `MS#` `OtherCharIdRaw` only when `CCCC_IC_SUPPORT` is advertised, adding `^0/^1` layer order only when `EFFECTS` is advertised. The Self row should show the selected INIPuppet slot/name (`iniPuppetID`/`iniPuppetName`), not the local folder/current client character. GM snapshot fields live in `Common/SaveFile.cs` `GmMultiClientSnapshotClient`. | Confirmed |
| "background priority", "mount path priority", "duplicate background name", "refresh bg softlock" | `AOBot-Testing/Structures/Background.cs`; `Background.RefreshCache()` and `TryUpsertBackgroundInCache()` preserve AO2 mount priority by keeping the first matching `Globals.BaseFolders` background name. Cache writes must use `GetDistinctBackgroundsForCache()` so aliases/direct paths cannot serialize duplicate `Background.Name` values and break snapshot restore. | Confirmed |
| "taskbar preview", "viewport Windows preview", "viewport in taskbar", "Use viewport as Windows preview", "double alt tab", "frozen viewport preview" | Right-click context menu on `AO2ViewportWindowContent` (`ViewportContextMenu_Opened` dynamically builds sections); both viewport/background and chatbox right-clicks should open this same menu. Chatbox background color actions live in the shared Chatbox section and call `AO2ViewportControl.PickChatBackgroundColor()` / `SetChatBackgroundColor(null)`; color picking reuses `AOCharacterFileCreatorWindow.ShowSolidColorPickerDialog(...)`. The Background section opens `design.ini`; the Chatbox section opens the active `Globals.PathToConfigINI`. `UseAsWindowsPreview` bool property fires `UseAsWindowsPreviewChanged`; saved to `SaveFile.Data.GMViewportWindowPreviewPriority`; `MainWindow.ApplyViewportTaskbarPriority()` uses native viewport shell preview: viewport becomes the visible taskbar/Alt-Tab HWND, main GM window is hidden from shell/taskbar and kept `WS_EX_NOACTIVATE` while preview mode is active. On viewport shell return, `RestoreMainWindowVisualForViewportReturn()` shows/restacks the main GM HWND with `ShowWindow(SW_SHOWNOACTIVATE)` / `SetWindowPos(... SWP_NOACTIVATE ...)` without focusing it, then `viewportPreviewInputProxyActive` routes viewport `PreviewKeyDown`/`TextInput` into logical IC/OOC textboxes. Main-window `WM_MOUSEACTIVATE` in preview mode forces the viewport shell foreground/restack path while keeping the main HWND no-activate, so clicking/dragging the main window brings the viewport/main pair above external windows. `EnsureViewportIsForegroundShellRepresentative(...)` returns foreground/active shell identity to the viewport when Oceanya is already foreground, including after main input clicks/focus. `ProxyKeyboardFocusVisual.IsProxyKeyboardFocusTarget` gives the logical IC/OOC proxy target a focused border and blinking caret adorner while real WPF keyboard focus remains on the viewport. Main-window mouse target tracking uses a safe visual/logical ancestor walk because log clicks can originate from `FlowDocument`, not a `Visual`. Do not re-enable `ViewportThumbnailCompositor` for the active path; it only deactivates stale DWM iconic thumbnail state. Old low-level keyboard hook/reinject Alt-Tab attempts are disabled. Attempt log and next ideas live in `documentation/ViewportWindowsPreviewAltTab.md`. | Confirmed |
| "DPI", "second monitor too big", "mixed DPI monitor", "per monitor DPI" | `OceanyaClient/app.manifest` opts into `dpiAwareness` `PerMonitorV2, PerMonitor`; main GM UI is fixed-DIP WPF layout in `MainWindow.xaml`, so monitor moves should remeasure/render at the target monitor DPI instead of retaining primary-monitor bitmap scale. | Confirmed |
| "context menu sections", "standard context menu format", "right-click menu titles" | Use `OceanyaClient.Utilities.ContextMenuSectionHelper.AddHeader(...)`; required format is bold disabled category title rows matching `CharacterFolderVisualizerWindow` / `MusicContextMenu_Opened`. Viewport dynamic menu lives in `AO2ViewportWindowContent.ViewportContextMenu_Opened`; main IC character/background dropdown menus live in `ICMessageSettings`. | Confirmed |
| "standard character context menu", "character right-click menu", "Character section in viewport", "character combo right-click", "delete character folder crash" | `OceanyaClient.Utilities.CharacterContextMenuBuilder` owns the shared character menu categories/actions and formatted submenu headers via `BuildSubmenu(...)`. Reused by `CharacterFolderVisualizerWindow.BuildContextMenuForItem`, `CharacterEmoteVisualizerWindow.BuildContextMenuForItem`, `ICMessageSettings.BuildCharacterDropdownContextMenu`, and `AO2ViewportWindowContent.AddCharacterMenuSection`; viewport exposes it as a bold `Character (<name>)` submenu. Default shared actions include verifier run/results, editor actions, folder open/copy, and delete; main-window delete routes through `MainWindow.DeleteCharacterFolderFromContextAsync`, first `ReleaseCharacterFolderBeforeDelete(...)` to clear active client/viewport/UI image references, then targeted character refresh instead of full asset refresh. UI-loaded character button images must use `BitmapFileLoader.LoadFrozen(...)` so Windows file handles do not block deletion. | Confirmed |
| "message box", "custom popup", "OceanyaMessageBox", "popup too small", "large warning text" | `OceanyaClient/Components/Forms/OceanyaMessageBox.xaml(.cs)`; hosted by `GenericOceanyaWindow` through `OceanyaWindowManager`. Size is computed in `OceanyaMessageBox.CalculateContentSizeForMessage(...)` from wrapped text + visible buttons, clamped to a screen-aware max; `ScrollViewer` is fallback only after max size. Tests in `UnitTests/TestabilityHardeningTests.cs`. | Confirmed |
| "copy to clipboard", "context menu copy crashes", "clipboard locked" | Use `OceanyaClient.ClipboardUtilities.TrySetText(...)` / `TryGetText(...)`, not raw `Clipboard.SetText`/`GetText`; the helper uses `Clipboard.SetDataObject` and catches COM clipboard-open failures already solved in message box/wait form copy flows. | Confirmed |
| "ctrl drag", "synchronized window move", "move both windows together" | `GenericOceanyaWindow.SynchronizedMovePartner` property + `WM_MOVING` handler in `GenericOceanyaWindow.WndProc` (`HandleWindowMovingSynchronize`); wired from `MainWindow.SetupViewportSynchronizedMove` / `TeardownViewportSynchronizedMove`. Hold Ctrl while dragging either window's title bar to move both together. | Confirmed |
| "character database viewer lags", "viewer grid virtualization", "character folder visualizer performance", "GeneratorPositionFromIndex crash" | `CharacterFolderVisualizerWindow` icon/normal mode uses `OceanyaClient.Utilities.VirtualizingWrapPanel` through `IconItemsPanelTemplate`; table mode uses `VirtualizingStackPanel`. `VirtualizingWrapPanel.MeasureOverride` must tolerate a null/stale `ItemContainerGenerator` during detail-to-grid view swaps and skip that stale layout pass instead of crashing. Progressive image residency stays in `UpdateViewportImageResidency` / `StartProgressiveImageLoading`. | Confirmed |
| "character emote visualizer lags", "emote viewer virtualization", "right-click emote" | `CharacterEmoteVisualizerWindow` icon grid uses `OceanyaClient.Utilities.VirtualizingWrapPanel`; visible-item image residency is managed by `RefreshViewportAnimationResidency` / `StartViewportPreviewLoading`. Emote right-click builds an `Emote` section for `Set as folder display`, then appends the shared `CharacterContextMenuBuilder` menu for character-level actions. | Confirmed |
| "parity gaps", "what's missing", "AO2 features we don't have", "pairing button", "evidence management", "mod call", "spectator", "typing indicator", "timer TI#" | `Documentation/AO2ParityGaps.md` — 19-gap catalog (2026-05-14, release/6-2) with user descriptions, current state, and full technical specs per gap. High priority: Pairing UI (#1), Evidence management (#2), Judge controls/HP bars (#3). Medium: Timer (#4), Spectator (#5), Typing indicator (#6), Mute (#8), Mod call (#9), RT# sending (#19). | Confirmed |
| "viewport rendering gap", "can't see HP bars", "ambient music silent", "testimony no sound", "verdict wrong duration", "guilty overlay", "effects stack", "talking sprite wrong color", "DeskMod 4 5", "music loops wrong" | `Documentation/ViewportParityGaps.md` — 9 receive-side rendering gaps (2026-05-14). Only gap #1 (HP# bars) remains open. Gaps 2–8 implemented; gap 9 verified not a gap. | Confirmed |
| "AOHD viewport gap", "wide theme background shifts left", "background gap after talking", "overlay breaks after talking", "show chatbox on top of viewport" | `AO2ViewportControl.RenderScene` applies theme layout before resolving AO2 placement; do not run another `ApplyThemeLayout()` after `SetPlacedAnimatedImage` because `SetViewportLayerSize` resets placed BG/desk width to raw viewport size. `ChatboxOverlapsViewport` toggles must call `ReapplyScenePlacementAfterLayoutChange()` after layout. Attempt log: `Documentation/AOHDViewportWideThemePlacement.md`; regression: `UnitTests/ViewportParityTests.cs` `AO2ViewportControl_RenderMessage_AndChatboxOverlapToggle_DoNotSquashWideBackground`. | Confirmed |
| "character looks too small", "W first emote bigger in AO2", "character sprite scaling" | `AO2ViewportControl.SetCharacterAnimatedImageAsync` + `ApplyHeightBasedCharacterGeometry`; AO2 reference `AO2-Client/src/animationlayer.cpp::calculateFrameGeometry` scales non-stretched character frames by viewport height and clips over-wide sprites instead of width-constraining them with Uniform. | Confirmed |
| "ambient channels", "ambient music", "MC# channel 1", "MC# channel 2" | `AO2ViewportAudioManager.PlayAmbientMusic(channel, songPath, loop)` + `StopAmbientChannel` + `StopAllAmbient`; `Dictionary<int, AO2BlipPreviewPlayer> ambientPlayers` per channel. Routed in viewport `OnMusicChanged` and `MainWindow.OnMusicChanged` instead of early-returning. | Confirmed |
| "music loop sidecar", "loop_start", "loop_end", "loop_length", "loop region" | `AO2ViewportAudioResolver.ParseMusicLoopSidecar(resolvedPath)` reads `<audioPath>.txt`; `AO2BlipPreviewPlayer.ApplyLoopRegion(startBytes, endBytes)` registers BASS_SYNC_POS|BASS_SYNC_MIXTIME callback; `ConvertLoopValueToBytes(isSeconds, value)` converts sample frames (×4) or seconds. Called from `AO2ViewportAudioManager.PlayMusic` after `TrySetBlip`. | Confirmed |
| "WTCE audio", "testimony SFX", "cross examination SFX", "guilty SFX", "court room SFX", "courtroom_sounds.ini" | `AO2ViewportAudioManager.PlayCourtSfx(key)` — calls `AO2ViewportAudioResolver.ResolveCourtSfxPath(key)` which reads `courtroom_sounds.ini` then falls back to `sounds/general/`. Called in `ShowTestimonyOverlay` ("testimony1") and `ShowWtceOverlay` (crossexamination_bubble→"testimony2", notguilty_bubble→"notguilty", guilty_bubble→"guilty"). | Confirmed |
| "effects cull", "effect stacking", "cull=true", "effects max_duration", "looping effect stop" | `ViewportEffect.Cull` (bool) and `ViewportEffect.MaxDurationMs` (int?) parsed in `AO2ViewportAssetResolver.ResolveEffect` from `effects.ini`. In `RenderEffect`: cull stops existing EffectImage before new one starts; max_duration fires a guarded `DispatcherTimer` to stop looping effects. | Confirmed |
| "talking sprite color", "per-character talking flag", "c4_talking", "blue non-talking" | `AO2ChatPreviewStyle.ChatMarkupTalking[]` resolved from `chat_config.ini` via `AO2ChatPreviewResolver.Resolve(...)`. Used in `RenderScene` to determine `useTalkingSprite` instead of old hardcoded Blue-only check. | Confirmed |
| "character off to side", "sprite not centered", "emote position wrong", "character x offset timing" | `AO2ViewportControl.ApplyCharacterOffset` stores offset in `characterPendingOffset`/`pairCharacterPendingOffset`; `ApplyHeightBasedCharacterGeometry` (non-static) re-applies stored offset after setting `image.Width`, fixing race on the slow async decode path where width is NaN when offset is first applied. `ApplyCharacterOffsetNow` is the static core. | Confirmed |
| "main emote grid lag", "Battler Ushiromiya loads slowly", "large emote count UI lag", "virtualize emote buttons" | Main GM emote selector is `OceanyaClient/Components/ICMessageSettings.xaml(.cs)` using `PageButtonGrid` and `ImageComboBox`. `ICMessageSettings.SetINI(...)` stores `Emote` models and calls `PageButtonGrid.SetVirtualizedItems(...)`; `CreateEmoteButton(...)` now creates only visible page buttons, preserving context menus/automation IDs. `ImageComboBox.SetItems(...)` bulk-loads emote dropdown rows and the shared `ImageComboBox` template enables recycling virtualization for all component uses. | Confirmed |
| "emote page per client", "wrong emote page when switching", "emote grid empty" | `PageButtonGrid.GetCurrentPage()`/`SetCurrentPage(int page)` (clamped to valid range); `ICMessageSettings.clientEmotePages` dict keyed by `AOClient` saves/restores page index. Saved before unsub in `SetClient`, restored after `SetINI`. | Confirmed |
| "callword notification", "callword flash", "taskbar orange flash", "callword taskbar" | `CallwordAudioNotifier.TryNotify` returns `bool` (true when a rule fired); `MainWindow.FlashTaskbar()` calls `FlashWindowEx` (`FLASHW_TRAY | FLASHW_TIMERNOFG`) when `TryNotify` returns true and the window is not active. If viewport taskbar preview priority is enabled, it flashes the viewport window handle instead of the hidden main-window taskbar handle. | Confirmed |
| "find in log", "search IC/OOC log", "ctrl+f log", "find performance", "Search IC/Search OOC" | Right-click on IC or OOC log → "Find in log..." → opens `FindInLogWindow` (modeless, `OceanyaWindowContentControl`). `ICLog` and `OOCLog` implement `ILogFindTarget`; `MainWindow.GetCurrentLogFindTargets()` supplies both current documents. `FindInLogWindow` has Search IC/Search OOC scope checkboxes, debounces and cancels in-flight search/highlight work on text/options changes. `LogDocumentSearch` snapshots the visible `FlowDocument` on the dispatcher, then `FindOffsets(...)` matches plain text off-thread before resolving offsets back to `TextPointer`s. `LogTextMatcher` owns the shared plain/regex/case/whole-word semantics and is reused by all-log file search. IC/OOC `HighlightMatchesAsync(...)` batches highlight application with dispatcher yields; navigation updates only old/current active highlights. Tests: `UnitTests/LogDocumentSearchTests.cs`, `UnitTests/AllLogSearchServiceTests.cs`; manual 10k-line benchmark is `[Explicit]` test `Find_TenThousandLineManualBenchmark`. | Confirmed |
| "chatbox message scrolls", "long message not visible", "chatbox text reveal scroll" | `AO2ChatPreviewControl.xaml` `MessageTextBox` uses `ScrollViewer.VerticalScrollBarVisibility="Hidden"` (was `Disabled`); `ScrollMessageToCurrentTextEnd()` (called from `ApplyPreviewTextOnly` on each reveal tick) scrolls to end so long messages auto-scroll as text is revealed. | Confirmed |
| "forcepos", "poslock ignores added client", "SP# applies to all GM clients" | Server `/forcepos` in `tsuserver3/server/commands/character.py` and `tsuserverCC/server/commands/character.py` calls `change_position`, which sends `SP#pos#%` to targeted network clients. `AOClient.OnServerPositionReceived` fires on incoming `SP#`; in single-internal-client GM mode, `MainWindow.ApplyServerPositionToAllSingleInternalProfiles` copies that forced server position to every profile sharing the one internal network client. | Confirmed |

### Map Maintenance Rule
Whenever an agent has to look up where a feature lives, how the user names a concept, which files own a workflow, or which tests cover it, and that discovery would help future agents navigate faster, add or update a map row before finishing. Do this for bug fixes, feature work, refactors, documentation updates, and investigations.

### Example Workflow Only
The following names are illustrative only and may not exist in this repository:
1. User asks about "the Billing module's adjustment dialog."
2. Agent checks this map and `Documentation/FeatureIndex.md` first.
3. If missing, agent searches just enough to identify that "Billing" means `Accounting/Billing/`, "module" means the billing workflow service, and "adjustment dialog" means `BillingAdjustmentWindow.xaml`.
4. Before finishing, agent updates the map with exact paths, relevant classes/tests, and notes that the user phrase "adjustment dialog" maps to `BillingAdjustmentWindow`.
5. Future agents use that map entry directly instead of repeating the broad search.

## Architecture

### Startup Flow
`StartupFunctionalityCatalog` defines the launch modes:
- **GM Multi-Client**: multiple `AOClient` instances controlled from `MainWindow`
- **AO2 AI Bot (Dev)**: same UI with `AOClientAgentController` wired to each client; only visible in `DEBUG` builds when `Environment.UserName == "Usuario"`
- **Character Database Viewer / File Creator / File Hivemind**: offline tools

### AO2 Protocol Layer (`AOBot-Testing`)
`AOClient` owns the AO2 transport and packet handling.

Important callbacks/events include:
- `OnICMessageReceived`
- `OnIcActionReceived`
- `OnOOCMessageReceived`
- `OnChangedCharacter`
- `OnBGChange`
- `OnReconnectionAttempt`

Server endpoints are loaded from `OceanyaClient/server.json` via `Globals.LoadServerIPs()`.

AO2 message text uses symbol escaping:
- `<percent>` -> `%`
- `<dollar>` -> `$`
- `<num>` -> `#`
- `<and>` -> `&`

Use `Globals.ReplaceTextForSymbols` and `Globals.ReplaceSymbolsForText` when crossing the protocol boundary.

### AI Agent Pipeline (`AO2AIBot`)
1. `AOClientAgentController` receives `ChatLogEntry` records, queues evaluation work, and calls `IAiChatCompletionService`.
2. `AiChatCompletionService` selects `GPTClient` or `OllamaClient` based on `AiChatProviderSettings.Provider`.
3. `AO2AiBotPromptBuilder` assembles the user prompt from client state and transcript history.
4. `AO2AiBotPromptCatalog` provides the system instruction set.
5. `AOClientAgentResponseParser` parses `SYSTEM_WAIT()` or JSON responses into `AOClientAgentDecision`.

The resulting decision is executed through delegates wired by `MainWindow`.

### Save Data (`Common`)
`SaveFile.Data` is the in-memory singleton.
- `SaveFile.SaveToDisk()` persists it
- `SaveFile.LoadSnapshotFromDisk()` returns a copy without mutating the singleton

### File Hivemind
`OceanyaClient` can launch `OceanyaHivemindAgent.exe` as a subprocess.

The background agent uses the named `EventWaitHandle` `FileHivemindBackgroundAgentCommandLine.AgentStopSignalEventName` so the parent can request shutdown.

### Test Conventions
Framework: NUnit 4 with Moq.

These tests depend on external services and are not expected to pass without configuration:
- `LiveServerConnectionTests`
- `GoogleDriveSyncLiveTests`
- `OllamaClientTests`
- `GPTClientTests`

## Documentation Workflow
To save tokens and reduce repeated broad searches, agents should treat `Documentation/` as a maintained feature index plus focused topic docs, not as a dumping ground.

Before doing a wide code search for a feature:
1. Check the `Repository Navigation Map` in this file.
2. Check `Documentation/FeatureIndex.md`.
3. Open the most relevant linked doc(s).
4. Only then do a targeted code search for the specific classes/files that the map or docs point to.

When an agent investigates, fixes, adds, removes, or significantly refactors a feature:
1. Update `Documentation/FeatureIndex.md` if the feature entry is missing, renamed, or moved.
2. Create or update a focused markdown doc for that feature when the behavior, entry points, packet flow, file ownership, or caveats would help future work.
3. Keep docs concise and high-signal. Prefer one index entry plus one focused doc over large repetitive writeups.
4. Delete or rewrite stale docs when they are actively misleading. Do not leave contradictory documentation behind.
5. If you add, remove, or rename files under `Documentation/`, also update the solution's `Documentation` solution folder so the files stay visible in Visual Studio.

Documentation should usually capture:
- feature purpose
- main entry points/files
- important packet formats or external behavior contracts
- known pitfalls, parity quirks, or gotchas
- test coverage and important missing coverage

## Agent Communication And Search
- Use caveman mode for agent replies unless the user explicitly asks to stop.
- Use the repo-local `grep-and-read` skill where applicable for feature exploration and bug tracing.

## Code Style
- **Naming**: PascalCase for classes, methods, and properties. camelCase for locals and parameters.
- **Formatting**: 4-space indentation. Lines under 120 characters.
- **Types**: Use explicit types. Nullable reference types are enabled.
- **Error handling**: Use `try-catch` for expected exceptions. Log via `CustomConsole.Error(message, exception)`.
- **Async**: Prefer `async`/`await`. Avoid blocking calls.
- **Comments**: XML documentation comments on all public methods and classes.
- **Dependencies**: Inject dependencies rather than constructing them inline when practical.
- **Namespaces**: Follow the existing structure such as `AOBot_Testing.Agents`, `AOBot_Testing.Structures`, etc.

## Reusable Dialogs And Controls
- **Image Asset Viewer**: `OceanyaClient.Utilities.AssetImageViewerDialog.Show(Window? owner, IReadOnlyList<AssetEntry> entries, int initialIndex = 0)` — the full-featured dark-themed image viewer (zoom, animated timeline with play/pause/seek/loop, prev/next navigation, image-bounds overlay). Used by the character creator file organizer and the IC emote grid context menu. Call it anywhere you need to preview one or more image/animation assets by absolute path.
  - `AssetEntry(string? AbsolutePath, string Label, string? MetaText = null, ImageSource? FallbackPreview = null)`
  - Animation playback is powered by `OceanyaClient.Utilities.AnimationTimelinePreviewController` (frame-accurate, supports APNG/GIF/WebP via the same decoder stack as the viewport).

## UI Consistency
- **Dark ComboBoxes**: For dark-themed windows, use the fully themed ComboBox pattern from `InitialConfigurationWindow.xaml` / `CharacterFolderVisualizerWindow.xaml`. Do not ship light/default dropdown popups in dark windows.
- **Auto-complete dropdowns**: Use `AutoCompleteComboBoxBehavior` for editable searchable dropdowns with filter-on-type, arrow navigation, and Enter-to-commit behavior.
- **Custom context menus**: Every custom WPF context menu must use the section-title format from the Character Folder Visualizer and Music List menus: disabled bold title rows grouped by understandable umbrella categories, with separators only between categories. Prefer `OceanyaClient.Utilities.ContextMenuSectionHelper.AddHeader(...)` for new or touched C#-built menus.
