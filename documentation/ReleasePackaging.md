# Release Packaging

## Purpose
Release packaging collects the built client output, copies the Hivemind agent into that output, and produces the distributable zip.

## Main Entry Points
- `Directory.Build.props`
- `OceanyaClient/OceanyaClient.csproj`
- `OceanyaHivemindAgent/OceanyaHivemindAgent.csproj`

## Current Release Flow
1. Build `OceanyaClient`.
2. Build `OceanyaHivemindAgent`.
3. Copy the Hivemind executable and its runtime files into `OceanyaClient/bin/<Configuration>/net8.0-windows/`.
4. Copy that output into `OceanyaClient/bin/<Configuration>/Oceanya Client v<version>/`.
5. Stage only that outer release folder into a temporary zip-staging directory.
6. Zip the staging directory to `OceanyaClient/bin/<Configuration>/Oceanya Client v<version>.zip`.

## Zip Layout Requirement
- The zip must contain the outer folder:
  - `Oceanya Client v<version>.zip`
  - `Oceanya Client v<version>/`
  - `OceanyaClient.exe` and the rest of the release files inside that folder
- The zip should not flatten directly to the files from `net8.0-windows/`.

## Known Pitfall
- Zipping `$(ReleasePackageDir)` directly flattens the archive contents and drops the outer release folder.
