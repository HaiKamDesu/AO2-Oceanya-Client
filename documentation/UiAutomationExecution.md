# UI Automation Execution (FlaUI / UIA3)

Full execution rules for the opt-in desktop automation lanes. `AGENTS.md` keeps only the summary.

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

