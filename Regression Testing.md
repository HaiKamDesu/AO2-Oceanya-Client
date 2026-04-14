# Regression Testing — Remaining Backlog

## Summary

- `P0` remaining unit-test items: none.
- `P1` remaining unit-test items: none.
- The remaining backlog consists of lower-priority items or items whose best layer is STA/in-process rather than a plain unit test.

## Remaining Regression Items

### [R-006] AO2-style extra IC action lines (shout, evidence, music)

- **Feature area:** IC chat log / event action lines
- **Priority:** P2
- **Current likely best layer:** Unit test
- **Status:** Remaining
- **Why still open:** Evidence and music action coverage exists, but the full AO2-style action-line backlog entry is not yet retired here because the shout-specific regression protection still needs to be reviewed and/or added explicitly before this item can be removed.
- **Target coverage:** `IcChatLog_ShoutModifier_ProducesActionLine`, `IcChatLog_MusicPacket_ProducesActionLine`

### [R-011] Startup refresh prompt: "No" was treated as "Yes"

- **Feature area:** Startup sequence / asset refresh prompt
- **Priority:** P2
- **Current likely best layer:** STA/in-process WPF test
- **Status:** Remaining
- **Why still open:** This needs an STA/in-process prompt-flow test; it is not part of the P0/P1 plain unit-test pass.
- **Target coverage:** `StartupRefreshPrompt_WhenUserDeclines_DoesNotRefreshAssets`

### [R-017] WebP/APNG transparent frame: black background on "Happy" emote

- **Feature area:** Character emote visualizer / WebP animation
- **Priority:** P2
- **Current likely best layer:** STA unit test
- **Status:** Remaining
- **Why still open:** Existing diagnostics are asset-dependent and skip when the Akechi assets are absent. This still needs a synthetic always-available transparency regression test.
- **Target coverage:** `WebpAnimation_SyntheticTransparentFrame_PreservesTransparency`

### [R-022] Server probe: duplicate endpoints probe twice instead of once

- **Feature area:** Server endpoint catalog
- **Priority:** P2
- **Current likely best layer:** Unit test
- **Status:** Remaining
- **Why still open:** Probe parsing tests exist, but duplicate-endpoint deduplication is still only partially covered.
- **Target coverage:** `ProbeCatalog_DuplicateEndpoints_TriggersOnlyOneSingleProbe`

## Verification Basis For Removed Items

The removed backlog items were retired only after checking existing or newly added tests at the stated layer. In particular, the `P0`/`P1` unit-test backlog is now covered by explicit regression tests for:

- `R-001` `GetCommand_CharIdField_UsesIniPuppetId_NotPlayerId`
- `R-002` `SendCharacterSelect_EmitsCorrectCcPacketFormat`
- `R-004` `FeatureFlags_ClearedOnReconnect_ProducesMinimalIcPacket`
- `R-005` `ShowName_BlankShowname_FallsBackToCharIniShowname`
- `R-005` `ShowName_BlankCharIniShowname_FallsBackToCharacterName`
- `R-009` `FromConsoleLine_CompactLayout_ParsesEffectAndScreenshakeFields`
- `R-012` `GenerateFiles_DuplicateAssetFilenames_AreSuffixDisambiguated`
- `R-014` `PersistStoredSecrets_SavesTypedSecretWithoutClearingSelectedAccountData`
- `R-015` `BackgroundLogStore_ConcurrentAppends_DoNotThrowOrLoseEntries`
- `R-016` `PullFromDriveAsync_CancellationDuringDownload_ThrowsWithoutHanging`
- `R-019` `GetCommand_RedTextColor_WrapsMessageWithTilde`
- `R-023` `ButtonIconGeneration_NonSquareInput_ProducesSquareOutput`

