# AO2 protocol, areas, music, positions and pairing

Navigation detail extracted from the `AGENTS.md` Repository Navigation Map.
Each section is the full note for one map row; the map keeps the short pointer.

## IC packet parity

**Also called:** "IC packet parity", "MS# packet", "Attorney Online Vidya cannot talk IC", "space IC does not clear", "shouts do not reset after blank IC"

**Status:** Confirmed

Outgoing IC is built in `AOClient.SendICMessage` and serialized by `ICMessage.GetCommand(...)`; UI send/clear/reset wiring lives in `OceanyaClient/MainWindow.xaml.cs` `ICMessageSettingsControl.OnSendICMessage`. AO2 outbound order is core 15 fields, then `CCCC_IC_SUPPORT` adds only `showname`, `other_charid`, `self_offset`, `immediate`; `LOOPING_SFX`, `ADDITIVE`, `EFFECTS`, and `CUSTOM_BLIPS` append their own blocks independently. Do not send receive-only echo fields (`OtherName`, `OtherEmote`, `OtherOffset`, `OtherFlip`) in outbound `MS#`. KFO/Vidya source lives in `KFO-Server/server/network/aoprotocol.py` `net_cmd_ms`: it validates exact field counts, rejects mismatched `cid`, rejects nonempty `showname` when area showname changes are disabled, and may not accept AO2 `CUSTOM_BLIPS` tail despite advertising it on some deployments. For KFO, `AOClient.SendICMessage` suppresses implicit showname fallback and custom-blips/slide fields; before send it aligns INIPuppet to `CurrentINI` when that character exists and is available on the server, preventing stale `Franziska` + `KamLoremaster` packets. Server-selected `iniPuppetID` must come from `PV#<player>#CID#<charId>#%` confirmation after `CC#`; do not trust requested/snapshot char IDs. Tests: `UnitTests/CustomUnitTests.cs` compact AO2 layout tests; `UnitTests/NetworkTests.cs` showname, PV confirmation, KFO packet, and send-time INIPuppet alignment tests.

## Vidya /getarea

**Also called:** "Vidya /getarea", "KFO /getarea", "Clients in [area]", "vanilla /getarea", "pairing studio full server list", "double click area does not move on KFO", "KFO area list gray until clicking Hubs", "Paradise starts on Hubs list", "top area info stale/wrong", "clicking Hubs auto-goes back", "area says IDLE", "area switch popup lags", "tsuserver3 current area not updating", "area not updating on move"

**Status:** Confirmed

`/getarea` parsing lives in `AOBot-Testing/AO2Parser.cs`; Vidya-style `$H: = Clients in [8] Lounge (users: 1) [IDLE] =`, KFO-style `[8] Lounge (users: 2) [CASING][CM(s): Name]:` with `[GM]`/quoted showname rows, and vanilla-style `AO Official Server (Vanilla): === Basement ===` + `[8 users][IDLE]` are parsed by `ParseGetAreaDetailed`. `AOClient.ApplyAreaInfoFromGetAreaMessage` updates `AreaInfo` users/status/CM/lock from those headers; `ApplyAreaInfosFromAreaListMessage` parses tsuserver3 `=== Areas ===` and KFO `🗺️ Areas 🗺️` OOC rows. Changed-area OOC is handled by `ApplyAreaInfoFromChangedAreaOoc`: tsuserver3/CC sends `Changed area to {name} [{status}].` (no id prefix), KFO sends `🚶Changed to area: [id] name (users: n) [STATUS]...`. Both patterns update `CurrentArea` and `AreaInfo.Status`. `ParseAreaListFromFa` strips KFO `[id] ` prefixes from cached area names but preserves the raw switch token in `areaSwitchTokens`, so `SetArea("Lounge")` can send `MC#[8] Lounge#...#%` back to KFO. `FA/SM` infer initial `CurrentArea` from the first row only when current area is empty and the first row is not KFO `🌐 Hubs`; after that, server `/getarea`/changed-area/area-list confirmation owns current-area changes. `SetArea` sends the switch packet immediately, delays only between chained slash-separated room switches, and sets `pendingAreaSwitchDisplayName` to the target area. When `BN#` is received after an MC#-initiated switch, the `BN#` handler applies the pending display name via `SetCurrentArea` as a fallback for vanilla servers that send no OOC on area switch; if the OOC handler already fired first (tsuserver3/CC/KFO), `SetCurrentArea` skips the no-op update. This ensures current-area tracking works across all server types. KFO initial hub-list workaround uses `kfoInitialHubListAutoExitPending`: only the first `FA#🌐 Hubs 🌐...` or `SM#🌐 Hubs 🌐...` after identifying KFO auto-sends that row back to reach the normal area list; first normal `FA`/`SM` consumes the pending flag, so user-clicked Hubs stays open like AO2. Area navigator UI hides `IDLE` as the default/no-status state, matching AO2; statuses like `GAMING`, `CASING`, `RECESS`, `RP`, and LFP still display/color rows. Area navigator double-click should fire-and-forget `JoinSelectedAreaAsync` so the WPF input event does not wait on socket send or a post-send refresh; server events update the area after confirmation. AO2 accepts `SM#` only during initial courtroom load; `MainWindow.BootstrapAreaNavigatorAsync` and area-popup open should not send `RM#%` once `AvailableAreaInfos` is already populated, because KFO `RM` returns `SM` without fresh ARUP and can downgrade a formatted `FA/ARUP` area list. Tests: `UnitTests/NetworkTests.cs` `ParseGetArea_SupportsVidyaClientsInHeaderOutput`, `ParseGetArea_SupportsKfoAreaHeaderAndQuotedPlayers`, `HandleMessage_KfoChangedAreaOocUpdatesCurrentAreaAndAreaInfo`, `HandleMessage_Tsuserver3ChangedAreaOocUpdatesCurrentAreaAndStatus`, `HandleMessage_Tsuserver3ChangedAreaOocFires_AfterKfoAreaListAlreadySet`, `HandleMessage_VidyaGetAreaUpdatesAreaAndRoster`, `HandleMessage_KfoGetAreaUpdatesAreaInfoAndRoster`, `HandleMessage_KfoAreaListOocUpdatesAreaInfos`, `SetArea_KfoIndexedAreaUsesOriginalSwitchTokenAndWaitsForServerConfirmation`, `SetArea_VanillaServer_BnFallbackSetsCurrentAreaWhenServerSendsNoOoc`, `ParseGetArea_SupportsVanillaAreaHeadingOutput`, and `HandleMessage_VanillaGetAreaUpdatesAreaInfoAndRoster`.

## missing OOC welcome

**Also called:** "missing OOC welcome", "OOC log empty on join", "restored first direct client OOC blank", "handshake OOC"

**Status:** Confirmed

`AOClient.ReceiveMessageAsync` dispatches every packet through `HandleMessage(...)` before `WaitForPacketAsync` checks the handshake predicate, so CT packets received during connect should reach existing OOC handlers. Direct internal clients attach handlers in `MainWindow.AddClientInternalAsync` before `ConnectClientAsync`; direct snapshot restore also attaches handlers to the first preconnected client before `ConnectClientAsync`, and `AttachDirectClientMessageHandlers` is idempotent. Regression tests: `UnitTests/NetworkTests.cs` `Connect_TcpEndpoint_DispatchesOocReceivedDuringHandshake`; `UnitTests/Phase1ReleaseConfidenceTests.cs` `MainWindow_DirectClientReceiveHandlers_AttachOnceAndPreservePreconnectOoc`.

## AO2 text logs

**Also called:** "AO2 text logs", "automatic logging", "logs folder", "AO log parity", "Find in all Logs", "Find in log folder"

**Status:** Confirmed

`OceanyaClient/Features/Chat/Ao2TextLogWriter.cs`; hooked from `MainWindow.AddLoggedIcMessageWithContext`, `AddLoggedIcActionMessage`, and `AddLoggedOocMessage`; reads selected `config.ini` via `Ao2ConfigIniSettings` and writes under `<AO install>/logs/<server>/`. If selected config is `<AO install>/base/config.ini`, log root is `<AO install>/logs` like AO2, not `base/logs`. Text-log session path is created when `automatic_logging_enabled` or `demo_logging_enabled` is true. Settings UI page: `OceanyaClient/Components/Forms/SettingsWindow.xaml(.cs)` `Logging` page exposes visible log maximum, IC inversion, `automatic_logging_enabled`, `demo_logging_enabled`, Open Log Folder, and `Find in all Logs...`. IC/OOC log right-click menus expose `Find in log...` and `Find in log folder...`. All-log search UI: `OceanyaClient/Components/Forms/FindInAllLogsWindow.xaml(.cs)`; file scanner: `OceanyaClient/Features/Chat/AllLogSearchService.cs`; shared matcher: `OceanyaClient/Components/LogTextMatcher.cs`. The popup reports total `.log` files, total text lines scanned, elapsed search time, matching-file count, and total matches. Large selected logs use a bounded match-window preview to avoid WPF freezes. Result rows have right-click actions to open in Notepad or reveal in Explorer. OOC text-file rows use `[OOC][timestamp] name: message`; writes and searches use read/write file sharing for AO2 coexistence.

## GM snapshot restore

**Also called:** "GM snapshot restore", "restored clients", "stale viewport background", "wrong INIPuppet after reconnect"

**Status:** Confirmed

`OceanyaClient/MainWindow.xaml.cs` owns `CaptureGmMultiClientSnapshot`, `RestoreGmMultiClientSnapshotAsync`, `ApplySnapshotStateToClient`, and `ResolveSnapshotRequestedPuppet`. `Common/SaveFile.cs` `GmMultiClientSnapshot` stores `ServerEndpoint`/`ServerName`; restore never reapplies saved `Background` to live clients and prefers `LocalCharacterName` over stale saved `IniPuppetName` when that local character exists on the active server. `AOBot-Testing/Agents/AOClient.cs` reconnect uses `RestoreIniPuppetAfterReconnectAsync` so old local character/emote is not paired with a newly auto-selected server INIPuppet; `SendICMessage` also realigns to current local character before send when possible. Tests: `UnitTests/TestabilityHardeningTests.cs` snapshot endpoint/puppet cases and `UnitTests/NetworkTests.cs` send-time INIPuppet alignment.

## cannot change area

**Also called:** "cannot change area", "area switch does nothing", "Nyathena", "double click area no effect on this server", "MC# ignored"

**Status:** Confirmed

`AOClient.SetArea` built `MC#<area>#{playerID}#%`. AO2 (`AO2-Client/src/courtroom.cpp on_area_list_double_clicked`) appends **`m_cid`, the CHARACTER id**, not the connection id. `playerID` comes from `ID#<n>#...` and is usually 0, so servers that validate the field silently drop the switch - observed on Nyathena (`ID#0#Nyathena#v1.0.2`), where the log shows `MC#Trash Pit#0#%` going out and nothing coming back, while vanilla AO2 moves fine. tsuserver3/CC/KFO ignore the field, which is why it went unnoticed. Fixed via `AOClient.BuildAreaSwitchCharacterId()` (`iniPuppetID`, falling back to `playerID` when spectating); also applied to the two KFO hub auto-exit sends.

## music packet tests

**Also called:** "music packet tests", "MC# test", "OnMusicChanged test", "music not looping like AO2", "red server music streams in AO2", "[STREAM]"

**Status:** Confirmed

`UnitTests/MusicPacketTests.cs` — covers MC# → `OnMusicChanged`, `OnIcActionReceived`, loop/channel/effects fields, stop, server-initiated, missing-fields guards, escaped URL query strings, ASS# server asset URL fallback, and network packet log classification. Incoming loop parity lives in `AOBot-Testing/Agents/AOClient.HandleMusicPacket`: mimic AO2 `Courtroom::handle_song`, where missing loop field does not loop and only literal loop field `1` loops; tsuserver3/tsuserverCC/KFO normalize server-side song length/loop state to `1` when they want client-side looping. HTTP/HTTPS/FTP playback goes through `AO2BlipPreviewPlayer.TrySetBlip`; missing local server-list music uses `ASS#<asset_url>#%` from `AOClient.ServerAssetUrl` and `AO2ViewportAudioResolver.ResolveMusicPath(token, serverAssetUrl)` to stream `<asset_url>/sounds/music/<token>` like AO2 `Courtroom::handle_song`.

## music effect toggles

**Also called:** "music effect toggles", "Fade Out Previous", "Fade In", "Synchronize", "/play music"

**Status:** Confirmed

`OceanyaClient/MainWindow.xaml.cs` (`MusicContextMenu_Opened`, `PlayMusicItemAsync`, `TryResolveServerRecognizedMusicToken`) + `OceanyaClient/Features/Viewport/AO2ViewportAudioManager.cs`; AO2 effect flags are user-controllable only for rows that resolve to server-recognized music tokens and send direct `MC#`. Arbitrary `LOCAL FILES`, unrecognized `FREQUENTLY USED`, and `CUSTOM COMMANDS` use `/play` or OOC, so selected effect flags cannot be sent for them.

## position dropdown

**Also called:** "position dropdown", "default pos", "all possible positions", "undeclared positions"

**Status:** Confirmed

`OceanyaClient/Components/ICMessageSettings.xaml.cs` builds `PositionDropdown`; `Background.GetAo2PositionOptions()` returns AO2 default positions + `design.ini` positions + undeclared image-backed positions; `AOClient.curPos == ""` means `default (<current character side>)` and follows character changes unless `switchPosWhenChangingINI` is enabled.

## pairing studio

**Also called:** "pairing studio", "pair button", "pair with character", "pair offset", "pair layer order"

**Status:** Confirmed

IC panel button: `OceanyaClient/Components/ICMessageSettings.xaml` `btnPairingStudio` uses `Resources/Buttons/pair_button.png`; dialog: `OceanyaClient/Components/Forms/CharacterPairingStudioWindow.cs`; outgoing AO2-compatible state lives on `AOClient.PairTargetCharId`, `PairTargetCharacterName`, `PairLayerOrder`, and `SelfOffset`; `AOClient.SendICMessage` serializes pair target into compact `MS#` `OtherCharIdRaw` only when `CCCC_IC_SUPPORT` is advertised, adding `^0/^1` layer order only when `EFFECTS` is advertised. The Self row should show the selected INIPuppet slot/name (`iniPuppetID`/`iniPuppetName`), not the local folder/current client character. GM snapshot fields live in `Common/SaveFile.cs` `GmMultiClientSnapshotClient`.

## forcepos

**Also called:** "forcepos", "poslock ignores added client", "SP# applies to all GM clients", "position combobox not per-client", "position not per-client", "all clients share position", "each client own pos"

**Status:** Confirmed

Server `/forcepos` in `tsuserver3/server/commands/character.py` and `tsuserverCC/server/commands/character.py` calls `change_position`, which sends `SP#pos#%` to targeted network clients. `AOClient.OnServerPositionReceived` fires on incoming `SP#`. **Per-client position:** each GM profile has its own `AOClient.curPos`; the position combobox (`ICMessageSettings.PositionDropdown`, restored per profile in `UpdatePosDropdown` on `SetClient`) must reflect it. Picking a position runs `ICMessageSettings.PositionDropdown_OnConfirm`→`curClient.SetPos`→`MainWindow.OnPositionConfirmed`→`AOClient.SetServerPositionAsync` which sends `/pos <pos>` OOC; the server ECHOES `SP#<pos>#%`. Regression fix: `SP#` must NOT be broadcast to every profile — `MainWindow.ApplyServerPositionToActiveSingleInternalProfile` (renamed from `...ToAllSingleInternalProfiles`) applies the incoming position ONLY to the active/bound profile (`boundSingleClientProfile ?? currentClient`). Broadcasting it to all profiles made setting one client's pos clobber every other client's (each shared one position). The `singleInternalClient.OnSideChange` handler independently already updates the active profile, and `AOClient.SetPos` is guarded (`if (curPos == newPos) return`) so the self-echo is a no-op. A real external `/forcepos` now moves the active profile only (the server has one connection in single mode, so per-profile forcing is not representable anyway).
