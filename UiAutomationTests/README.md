# UiAutomationTests — Smoke and Online Lanes

FlaUI UIA3-based UI automation tests for OceanyaClient. Tests are organized into
two categories:

| Category | Transport | Fixture | Description |
|---|---|---|---|
| `Smoke` | Offline (stubbed) | `UnitTests/TestAssets/FlaUISmoke/` | Deterministic regression suite; no real server needed |
| `Online` | Real TCP (in-process server) | `UnitTests/TestAssets/FlaUIOnline/` + FlaUISmoke shared | Integration lane; validates real AO2 packet flow, including GM multi client `MS#` packet assertions after UI interactions |
| `OnlineLocalhost` | Real WebSocket (`ws://localhost:50001`) | `UnitTests/TestAssets/FlaUIOnline/` + FlaUISmoke shared | Optional localhost tsuserver3 lane; skips cleanly when the local server is unavailable |

## Categories

| Category | Attribute | Filter |
|---|---|---|
| Smoke | `[Category("Smoke")]` | `--filter "Category=Smoke"` |
| Online | `[Category("Online")]` | `--filter "Category=Online"` |
| OnlineLocalhost | `[Category("OnlineLocalhost")]` | `--filter "Category=OnlineLocalhost"` |
| GmPacket | `[Category("GmPacket")]` | `--filter "Category=GmPacket"` |

## Prerequisites

| Requirement | Smoke | Online |
|---|---|---|
| OS — interactive Windows desktop | Required | Required |
| .NET SDK 8.0 | Required | Required |
| OceanyaClient built in `Debug` | Required | Required |
| Real AO2 server | Not needed | Not needed — uses in-process TCP server |
| Loopback TCP available | Not needed | Required (standard on all platforms) |

`OnlineLocalhost` additionally requires a reachable local tsuserver3 at `ws://localhost:50001`; otherwise those tests are skipped with `Assert.Ignore`. The deterministic `GmPacket` subset does not depend on localhost availability.

## Running Locally

**Always build first, then test as a separate command.** Chaining with `&&` can cause
lock contention on `obj/` files.

```powershell
# From the repository root (PowerShell or CMD on Windows):

# 1. Build
dotnet build "Oceanya Client.sln" --configuration Debug

# 2a. Run only the smoke suite
dotnet test UiAutomationTests/UiAutomationTests.csproj `
    --configuration Debug `
    --no-build `
    --filter "Category=Smoke"

# 2b. Run only the online lane
dotnet test UiAutomationTests/UiAutomationTests.csproj `
    --configuration Debug `
    --no-build `
    --filter "Category=Online"

# 2c. Run the optional localhost tsuserver3 lane
dotnet test UiAutomationTests/UiAutomationTests.csproj `
    --configuration Debug `
    --no-build `
    --filter "Category=OnlineLocalhost"

# 2d. Run only the GM multi client packet-validation subset
dotnet test UiAutomationTests/UiAutomationTests.csproj `
    --configuration Debug `
    --no-build `
    --filter "Category=GmPacket"
```

On WSL, replace `dotnet` with the full path:

```bash
/mnt/c/Program\ Files/dotnet/dotnet.exe build "Oceanya Client.sln" --configuration Debug

# Smoke
/mnt/c/Program\ Files/dotnet/dotnet.exe test UiAutomationTests/UiAutomationTests.csproj \
    --configuration Debug --no-build --filter "Category=Smoke"

# Online
/mnt/c/Program\ Files/dotnet/dotnet.exe test UiAutomationTests/UiAutomationTests.csproj \
    --configuration Debug --no-build --filter "Category=Online"

# Optional localhost tsuserver3
/mnt/c/Program\ Files/dotnet/dotnet.exe test UiAutomationTests/UiAutomationTests.csproj \
    --configuration Debug --no-build --filter "Category=OnlineLocalhost"

# GM multi client packet-validation subset
/mnt/c/Program\ Files/dotnet/dotnet.exe test UiAutomationTests/UiAutomationTests.csproj \
    --configuration Debug --no-build --filter "Category=GmPacket"
```

### Collecting artifacts locally

Pass `--results-directory` to control where TRX results and screenshots land:

```powershell
dotnet test UiAutomationTests/UiAutomationTests.csproj `
    --configuration Debug `
    --no-build `
    --filter "Category=Smoke" `
    --logger "trx;LogFileName=ui-smoke-results.trx" `
    --results-directory TestResults/UiSmoke
```

Screenshots on test failure are written to `<results-directory>/UiAutomationArtifacts/Screenshots/`.

## Running in CI

**Smoke suite** — see `.github/workflows/ui-smoke.yml`. Runs on `windows-latest` and
uploads TRX results and failure screenshots. Trigger manually or on `push` to `main`.

**Online lane** — see `.github/workflows/ui-online.yml`. Runs on a self-hosted Windows
runner (labels: `self-hosted`, `windows`). Trigger manually via `workflow_dispatch`.
The runner must be in an interactive desktop session; see the workflow comments for
full prerequisites. TRX results and failure screenshots are uploaded as artifacts.

## Test Infrastructure Notes

- **Readiness marker**: `WaitForReadyWindow` polls for a descendant with `AutomationId`
  `Oceanya.ReadyMarker` whose `Name` equals `Ready`. Windows signal readiness through
  `OceanyaWindowContentControl`.
- **Locators**: All element lookups use `AutomationId`. Do not use position-based or
  name-based locators.
- **Startup args**: Test mode args disable fake loading screens, wait forms, server
  validation, and asset refresh prompts. The save file, config.ini, and server.json
  paths are overridden to point at `UnitTests/TestAssets/FlaUISmoke/`.
- **No parallelism**: `[NonParallelizable]` on the fixture; tests run sequentially.
- **App cleanup**: `FlaUiSmokeApp.Dispose()` attempts a graceful close, waits up to
  2 seconds, then kills the process tree if needed.
- **GM packet assertions**: the GM multi client coverage uses the repository’s real
  `AOClient.SendICMessage()` path and parses captured `MS#` packets with
  `AOBot-Testing/Structures/ICMessage.cs`. The loopback server advertises
  `CCCC_IC_SUPPORT`, `LOOPING_SFX`, `ADDITIVE`, `EFFECTS`, `CUSTOM_BLIPS`,
  `Y_OFFSET`, `FLIPPING`, `PREZOOM`, `DESKMOD`, `EXPANDED_DESK_MODS`, and
  `CUSTOMOBJECTIONS` so the full packet shape is exercised.

## Soak Procedure (Online Lane)

Run the soak script to validate pass rate on the target machine before
relying on the self-hosted CI gate:

```powershell
# From the repo root — build once first, then soak-run 20 iterations.
dotnet build "Oceanya Client.sln" --configuration Debug

.\UiAutomationTests\soak-online.ps1 -Iterations 20 -Configuration Debug
```

Results land in `TestResults\OnlineSoak\`. Each iteration gets its own
timestamped sub-directory with a TRX file and any failure screenshots.

**Target threshold**: 20/20 passes in two separate soak sessions (different
machines or times of day).

### Soak history

| Session | Machine / context | Result |
|---|---|---|
| 2026-04-13 | dev machine, interactive desktop | 20/20 PASS |

## Promotion Criteria (Online → Self-Hosted CI Gate)

| Criterion | Status |
|---|---|
| ≥ 20 consecutive passes in two soak runs | One session complete (20/20); second session pending |
| No timing-related failures on target runner hardware | Met (soak session 1) |
| Runner is interactive-desktop | Required — UIA3 and `SendKeys` need a visible desktop |
| Dedicated runner (no focus-steal from other apps) | Required — enforce via runner configuration |
| Separate `ui-online.yml` workflow (not merged into `ui-smoke.yml`) | **Done** — see `.github/workflows/ui-online.yml` |

## Known Constraints

- Requires a real Windows interactive session. Not reliable in pure headless/WSL
  environments.
- Some list-based flows depend on UIA exposing rows as `DataItem` or `ListItem`
  control types.
- OOC/IC Enter-send tests rely on `SendKeys` focus behavior; avoid running other
  interactive windows on top of the test runner.
- The Online lane exercises real TCP transport; the Smoke lane uses a test-mode
  offline stub (`UseSingleInternalClient: true` in the smoke save file).
