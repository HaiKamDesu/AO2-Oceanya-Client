# Testing Roadmap

## Goal
Establish a repo-specific release gate strong enough that, for a full new release, passing tests means the build is trustworthy with only minimal manual app testing left.

This file is the durable source of truth for testing progress, priority, and remaining work. Future agents should resume from here after context reset.

Current implementation target: **Phase 1 - now we can trust releases**

## Current State
- The repository already has meaningful unit, STA/in-process WPF, and FlaUI coverage.
- Coverage quality is uneven across feature areas.
- `GM multi client` is the closest to release-confidence because it already has:
  - AO2 protocol and packet-shape unit coverage in `UnitTests/NetworkTests.cs`
  - packet-validation FlaUI coverage in `UiAutomationTests/GmMultiClientPacketTests.cs`
  - smoke startup/navigation coverage in `UiAutomationTests/FirstWaveSmokeTests.cs`
- `Character creator`, `database viewer`, and `hivemind` each have useful tests, but mostly for internals or startup behavior rather than the user workflows that decide release confidence.
- `AI bot` has strong parser/validator/prompt tests, but lighter protection for the `MainWindow` execution/wiring layer.
- The repo has checked-in UI workflows for Smoke and Online FlaUI, but no equivalent checked-in always-on unit-test workflow for the main release gate.

## Feature Priorities
Priority order is fixed and should remain stable unless explicitly changed by the user.

1. GM multi client
2. Character creator
3. Database viewer
4. Hivemind
5. AI bot

## Phase Breakdown

### Phase 1 - now we can trust releases
Purpose: add the smallest missing regression protections needed to start trusting release gates.

Scope is limited to these six items:

- [x] GM selected-client isolation tests
- [x] GM failed connect/add-client recovery tests
- [x] GM receive-path rendering tests
- [x] Character creator export end-to-end tests
- [x] Database viewer search/filter tests
- [x] Hivemind settings persistence tests

Notes:
- Before implementing any item, verify whether current repo state already covers the risk at the correct layer.
- If a Phase 1 risk is already fully covered, document that here and mark it complete without duplicating work.
- Prefer the smallest reliable layer:
  - unit first
  - STA/in-process WPF second
  - FlaUI only where true workflow coverage is required and the repo is already ready for that exact case

### Phase 2 - strengthen core feature confidence
Not in scope for the current pass.

- GM reconnect state regression tests
- Database viewer integrity-results/apply-fix coverage
- Character creator edit-existing-folder and duplication workflow coverage
- Character creator file-organization mutation coverage
- Hivemind import/export/delete connection workflow coverage
- AI `MainWindow` action execution and response-tagging coverage

### Phase 3 - broaden selective confidence
Not in scope for the current pass.

- Targeted FlaUI for viewer workflows that truly need end-to-end UI coverage
- Additional AI wiring coverage
- Hivemind agent-process integration coverage
- CI workflow hardening and category strategy refinements

## Prioritized Backlog
This backlog preserves the original roadmap ordering across the whole repo.

1. GM selected-client isolation tests
2. GM failed-connect/add-client recovery tests
3. GM receive-path rendering tests
4. GM reconnect state regression tests
5. Character creator export end-to-end tests
6. Character creator edit-existing-folder tests
7. Character creator duplicate-folder tests
8. Database viewer search/filter/tag tests
9. Database viewer integrity-results/apply-fix tests
10. Hivemind window settings persistence tests
11. Hivemind import/export/delete connection tests
12. Add always-on unit-test CI workflow
13. Character creator file-organization mutation tests
14. Character creator emote reorder/delete tests
15. AI `MainWindow` action executor tests
16. AI raw-response tagging/log-link tests
17. Hivemind agent process/stop-signal integration tests
18. Targeted FlaUI for database viewer search/open workflows
19. Add creator AutomationIds and narrow test hooks where justified
20. Add Hivemind AutomationIds and narrow test hooks where justified

## Release-Gate Strategy

### Full release gate
Run these for every full release:

1. Build solution
2. Main unit/STA suite excluding live/credential categories
3. Windows interactive-session check
4. Debug build for UI automation
5. FlaUI `Smoke`
6. FlaUI `Online`

Recommended commands:

```bash
/mnt/c/Program\ Files/dotnet/dotnet.exe build "Oceanya Client.sln"
/mnt/c/Program\ Files/dotnet/dotnet.exe test UnitTests/UnitTests.csproj --filter "Category!=RequiresConnection&Category!=RequiresCredentials"
/bin/bash -lc '"/mnt/c/Windows/System32/query.exe" session'
/bin/bash -lc '"/mnt/c/Program Files/dotnet/dotnet.exe" build "Oceanya Client.sln" --configuration Debug'
/bin/bash -lc '"/mnt/c/Program Files/dotnet/dotnet.exe" test "UiAutomationTests/UiAutomationTests.csproj" --configuration Debug --no-build --filter "Category=Smoke" --logger "trx;LogFileName=ui-smoke-results.trx" --results-directory TestResults/UiSmoke'
/bin/bash -lc '"/mnt/c/Program Files/dotnet/dotnet.exe" test "UiAutomationTests/UiAutomationTests.csproj" --configuration Debug --no-build --filter "Category=Online" --logger "trx;LogFileName=ui-online-results.trx" --results-directory TestResults/UiOnline'
```

### Change-triggered lanes
- `Category=GmPacket` for GM packet/UI changes
- focused unit/STA classes for creator, viewer, hivemind, or AI when those files change

### Optional/manual lanes
- `Category=OnlineLocalhost`
- `LiveServerConnectionTests`
- `LiveServerPollingTests`
- `GoogleDriveSyncLiveTests`
- live GPT/Ollama credential-backed tests

## Manual-Testing Leftovers
Even after the roadmap is complete, these still require manual validation:

- Real third-party behavior:
  - Google Drive OAuth/account-selection flows
  - real Drive permission edge cases
  - desktop toasts and tray behavior across Windows shell conditions
- Real server compatibility across non-loopback AO2 variants and tsuserver forks
- Visual/layout checks on large creator and viewer surfaces
- Performance and soak behavior:
  - very large character libraries
  - long-running Hivemind sessions
  - prolonged reconnect churn
- Packaging/install sanity for release artifacts

## Progress Checklist

### Phase 1 status
- [x] GM selected-client isolation tests
- [x] GM failed connect/add-client recovery tests
- [x] GM receive-path rendering tests
- [x] Character creator export end-to-end tests
- [x] Database viewer search/filter tests
- [x] Hivemind settings persistence tests

### Completion criteria for each item
- Do not mark complete unless the named regression risk is protected by assertions.
- If only partial coverage exists, leave the item open and describe what remains.

### Implementation notes
- This file should be updated whenever:
  - a Phase 1 task is completed
  - a task is verified as already covered
  - scope or rationale changes
  - a blocker is found that prevents completion

### Current pass notes
- Completed in repo:
  - `UnitTests/Phase1ReleaseConfidenceTests.cs` now covers all six Phase 1 risks with explicit regression assertions.
  - `MainWindow.SelectClient` now restores the selected client's OOC showname textbox when operators switch clients.
  - `MainWindow` and `AOCharacterFileCreatorWindow` expose narrow internal test hooks for connection failure and creator success/error message interception.
  - `OceanyanFileHivemindWindow` constructor supports injected runtime/credential/background-agent collaborators so settings persistence can be tested without live Windows integration.
- Verification notes:
  - Phase 1 items were verified with focused `dotnet test UnitTests/UnitTests.csproj --no-build --filter ...` runs for each risk area.
  - Character creator and database viewer coverage stay in-process/STA; FlaUI was not required for Phase 1.
