# UiAutomationTests — Smoke Suite

FlaUI UIA3-based UI automation tests for OceanyaClient. All tests are offline and
deterministic — no real AO2 server connection is made. Tests use seeded fixture
data under `UnitTests/TestAssets/FlaUISmoke/`.

## Category

All tests carry `[Category("Smoke")]`. Use this category filter to run only the smoke
suite without touching the rest of the solution's unit tests.

## Prerequisites

| Requirement | Details |
|---|---|
| OS | Windows with an interactive desktop session. Tests use FlaUI UIA3 which needs a visible desktop. WSL-only headless execution is not supported. |
| .NET SDK | 8.0 |
| Build | OceanyaClient must be built in `Debug` configuration before running tests. The test harness launches `OceanyaClient/bin/Debug/net8.0-windows/OceanyaClient.exe`. |
| Screen resolution | Any; screenshots are full-screen captures of the primary display. |
| No AO2 server | The tests pass `--test-mode` startup args that skip all network connections. |

## Running Locally

**Always build first, then test as a separate command.** Chaining with `&&` can cause
lock contention on `obj/` files.

```powershell
# From the repository root (PowerShell or CMD on Windows):

# 1. Build
dotnet build "Oceanya Client.sln" --configuration Debug

# 2. Run only the smoke suite
dotnet test UiAutomationTests/UiAutomationTests.csproj `
    --configuration Debug `
    --no-build `
    --filter "Category=Smoke"
```

On WSL, replace `dotnet` with the full path:

```bash
/mnt/c/Program\ Files/dotnet/dotnet.exe build "Oceanya Client.sln" --configuration Debug
/mnt/c/Program\ Files/dotnet/dotnet.exe test UiAutomationTests/UiAutomationTests.csproj \
    --configuration Debug \
    --no-build \
    --filter "Category=Smoke"
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

The workflow `.github/workflows/ui-smoke.yml` runs on `windows-latest` (which has an
interactive desktop session) and uploads both TRX results and failure screenshots as
artifacts. Trigger it manually from the Actions tab, or it fires automatically on `push`
to `main` when relevant files change.

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

## Known Constraints

- Requires a real Windows interactive session. Not reliable in pure headless/WSL
  environments.
- Some list-based flows depend on UIA exposing rows as `DataItem` or `ListItem`
  control types.
- OOC/IC Enter-send tests rely on `SendKeys` focus behavior; avoid running other
  interactive windows on top of the test runner.
- Tests do not validate real AO2 transport — the add-client path uses a test-mode
  offline stub in `MainWindow.xaml.cs`.
