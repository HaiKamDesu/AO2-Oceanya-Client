---
name: verify
description: >
  Run the repository's mandatory build-then-test sequence as two separate
  commands. Never chains them with && (obj/ race condition). Excludes tests
  that require live credentials or a real AO2 server connection.
trigger: >
  Invoked by change-completion Phase 5. Also invoke directly when you want
  to check whether the solution builds and unit tests pass without running
  the full change-completion workflow.
---

# verify

Run these two commands **sequentially**. Issue the second only after the
first succeeds. Never combine them with `&&`, `;`, or pipe — the `obj/`
output race produces spurious lock errors that make tests look broken when
they are not (see AGENTS.md).

```bash
/mnt/c/Program\ Files/dotnet/dotnet.exe build "Oceanya Client.sln"
```

Wait for exit 0. Then:

```bash
/mnt/c/Program\ Files/dotnet/dotnet.exe test UnitTests/UnitTests.csproj \
    --filter "Category!=RequiresCredentials&Category!=RequiresConnection"
```

**Do not run `UiAutomationTests` here.** Those require an interactive Windows
desktop session. Running them from WSL produces misleading failures. They run
via CI (`ui-smoke.yml`) on push to main, or manually for the Online lane.

Report: build pass/fail, test pass count, any failing test names and messages.
If a test fails, diagnose the root cause before retrying.
