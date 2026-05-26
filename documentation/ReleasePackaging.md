# Release Packaging

## Purpose
Release packaging collects the built client output, copies the Hivemind agent and external updater into that output, and produces the distributable zip.

## Main Entry Points
- `Directory.Build.props`
- `OceanyaClient/OceanyaClient.csproj`
- `OceanyaHivemindAgent/OceanyaHivemindAgent.csproj`
- `OceanyaUpdater/OceanyaUpdater.csproj`
- `.github/workflows/release.yml`

## Current Release Flow
1. Build `OceanyaClient`.
2. Build `OceanyaHivemindAgent`.
3. Build `OceanyaUpdater`.
4. Copy the Hivemind executable, updater executable, updater managed DLL, and their runtime files into `OceanyaClient/bin/<Configuration>/net8.0-windows/`.
5. Copy that output into a package folder.
6. Stage only that outer release folder into a temporary zip-staging directory.
7. Zip the staging directory to the channel-specific GitHub release folder.
8. Compute SHA-256 from that exact zip and write `update-manifest.json` beside it.

## Stable vs Test Outputs
- Release builds produce stable/public assets:
  - `OceanyaClient/bin/Release/Github Release/Oceanya Client <version>/`
  - `Oceanya.Client.win-x64.v<version>.zip`
  - `update-manifest.json` with `channel: "stable"` and `tag: "v<version>"`
- Debug builds produce test-only assets:
  - `OceanyaClient/bin/Debug/Github Release Test/Oceanya Client <version>-test/`
  - `Oceanya.Client.win-x64.test-v<version>.zip`
  - `update-manifest.json` with `channel: "test"` and `tag: "test-v<version>"`
- Debug packaging uses the same zip/stage/hash logic as Release packaging. Production clients reject `channel: "test"` manifests and test asset names.
- The generated test package uses the current `OceanyaAppVersion`. To test update detection, build or upload a test prerelease whose version is greater than the local Debug app version.

## Zip Layout Requirement
- The zip must contain the outer folder:
  - `Oceanya Client <version>/` for stable, or `Oceanya Client <version>-test/` for Debug/test
  - `OceanyaClient.exe`, `OceanyaUpdater.exe`, `OceanyaUpdater.dll`, `OceanyaHivemindAgent.exe`, and the rest of the release files inside that folder
- The zip should not flatten directly to the files from `net8.0-windows/`.
- Packaging excludes transient local runtime folders such as `OceanyaClient.exe.WebView2/` and duplicate RID output under `win-x64/`.

## GitHub Release Assets
- `OceanyaHivemindAgent/OceanyaHivemindAgent.csproj` produces updater assets during Debug and Release builds.
- `.github/workflows/release.yml` consumes those generated assets instead of rebuilding a separate manifest format.
- Stable assets:
  - `Oceanya.Client.win-x64.v<version>.zip`
  - `Oceanya.Client.win-x64.v<version>.zip.sha256`
  - `update-manifest.json`
- Test assets:
  - `Oceanya.Client.win-x64.test-v<version>.zip`
  - `Oceanya.Client.win-x64.test-v<version>.zip.sha256`
  - `update-manifest.json`
- The in-app updater requires `update-manifest.json` for automatic updates. It does not infer an installable update from arbitrary release assets.
- The manifest includes version, tag, channel, `win`/`x64`, asset name, SHA-256, entry executable, and release-notes source.
- The workflow uses the repository-scoped `GITHUB_TOKEN`; no client-side GitHub credentials are embedded in the app.

## Creating a Test Prerelease
1. Bump `OceanyaAppVersion` to the test target version or build from a branch with the intended test version.
2. Build Debug from Visual Studio or run `dotnet build "Oceanya Client.sln" --configuration Debug`.
3. Open `OceanyaClient/bin/Debug/Github Release Test/Oceanya Client <version>-test/`.
4. Upload the zip and `update-manifest.json` to a GitHub prerelease tagged `test-v<version>`.
5. Run the Debug app from Visual Studio. Debug builds use the Test channel and separate `OceanyaClientDev` updater state/cache paths.

Unauthenticated GitHub API clients cannot see draft releases. Test update feeds should use GitHub prereleases, not drafts.

## Updater Troubleshooting
- Stable updater logs: `%LOCALAPPDATA%/OceanyaClient/Updates/logs/updater.log`
- Test/Debug updater logs: `%LOCALAPPDATA%/OceanyaClientDev/Updates/logs/updater.log`
- Client-to-updater handoff files:
  - Stable: `%LOCALAPPDATA%/OceanyaClient/Updates/handoffs/`
  - Test/Debug: `%LOCALAPPDATA%/OceanyaClientDev/Updates/handoffs/`
- The handoff JSON records the staged package root, target install path, client executable path, parent process id, channel, version, updater executable path, log path, and exact updater arguments.
- Before closing the client, the in-app launcher now verifies that `OceanyaUpdater.exe`, `OceanyaUpdater.dll`, `OceanyaUpdater.deps.json`, and `OceanyaUpdater.runtimeconfig.json` exist beside `OceanyaClient.exe` and after copying them into the LocalAppData runner folder. It also verifies that `OceanyaUpdater.runtimeconfig.json` references `Microsoft.WindowsDesktop.App`, which is required by the visible WPF updater window. If any file is missing or the runtime config is stale, the client stays open and shows a visible error.
- Before downloading, the client stops File Hivemind with the shared stop coordinator. If the stop fails, the client cancels the update and stays open. If the stop succeeds but update preparation fails before updater handoff, the client starts File Hivemind again when an eligible connection exists.
- The updater opens a small `Oceanya Updater` status window for user-initiated updates. It logs startup before waiting for the old client process, logs each apply phase, times out if the old client does not exit within 45 seconds, backs up current files, rolls back on apply failure where possible, and relaunches the exact `OceanyaClient.exe` path passed by the client.
- The updater clears the install directory before copying the staged package, so files removed from a future release are removed from the installed folder too. If any file cannot be deleted or copied, the update fails and rollback is attempted from the backup.
- After a successful relaunch, the updater deletes the downloaded zip, staged extraction folder, and successful backup folder. It keeps logs and handoff JSON for diagnostics. The LocalAppData `runner/` folder is reused and overwritten on future updates because the updater runs from that folder during the apply phase.
- To retry the current Debug test against `test-v7.0`, build Debug, run the Debug client, click the bottom-left test update link, click `Update`, and watch for:
  - download progress in the client wait form
  - `Applying update... Oceanya will close and reopen automatically.`
  - the separate `Oceanya Updater` status window
  - automatic relaunch of `OceanyaClient.exe`
- If the client closes and does not reopen, inspect `updater.log` first. If there is no new log entry, inspect the latest handoff file and confirm the runner folder contains the updater `.exe`, `.dll`, `.deps.json`, and `.runtimeconfig.json`.
- Replacing `bin/Debug/net8.0-windows` while running under Visual Studio can fail if the debugger or another process keeps files locked. That should now fail visibly and log the locked file path. For a cleaner end-to-end test, run `OceanyaClient.exe` directly from `OceanyaClient/bin/Debug/net8.0-windows/` after building Debug instead of launching under the debugger.

## Creating a Production Release
1. Build Release or run the release workflow for a `v<version>` tag.
2. Open `OceanyaClient/bin/Release/Github Release/Oceanya Client <version>/`.
3. Upload the stable zip, `.sha256`, and `update-manifest.json` to the public non-prerelease GitHub release tagged `v<version>`.
4. Confirm the manifest has `channel: "stable"` and an asset name beginning `Oceanya.Client.win-x64.v`.

Do not upload the Debug-generated `channel: "test"` manifest to a production release. Stable clients reject it, but publishing it to production would still confuse human release management.

## Known Pitfall
- Zipping `$(ReleasePackageDir)` directly flattens the archive contents and drops the outer release folder.
