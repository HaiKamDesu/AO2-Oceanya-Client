---
name: change-completion
description: >
  Definition-of-done workflow for this repository. Classifies what changed,
  decides which unit tests, UI/smoke tests, and documentation actions are
  required, invokes sub-rules for specific change types (new WPF windows,
  AO2 packet handlers, AI response parsing, AutomationId coverage), then
  drives verification using the repository's mandatory build/test sequencing.
trigger: >
  Invoke after completing any code change before declaring the task done.
  Can also be invoked explicitly with /change-completion.
---

# change-completion

You are executing the definition-of-done checklist for the AO2-Oceanya-Bot
repository. Work through every phase below in order. Do not skip a phase
because a previous one seemed sufficient — each covers a distinct concern.

---

## Phase 1 — Classify the change

Read the diff (or the files you just modified) and tag the change with every
applicable label from the list below. A change can hold multiple labels.

| Label | Condition |
|---|---|
| `protocol` | Any change in `AOBot-Testing/Agents/AOClient.cs`, `HandleMessage`, packet structs, `ICMessage`, `AOClientTransport` |
| `ai-pipeline` | Any change in `AO2AIBot/` — parser, prompt builder, prompt catalog, response types, `AOClientAgentController` |
| `wpf-window` | A new `.xaml` / `.xaml.cs` file under `OceanyaClient/Components/Forms/`, or a new `OceanyaWindowContentControl` subclass anywhere |
| `wpf-ui` | Changes to existing XAML, styles, bindings, `AutomationProperties.AutomationId` values, control visibility, or layout in existing windows |
| `save-data` | Changes to `Common/SaveFile.cs`, `SaveData`, or any property serialized into `savefile.json` |
| `startup` | Changes to `StartupFunctionalityCatalog`, `StartupFunctionalityIds`, or `StartupFunctionalityOption` |
| `feature-index` | Any change whose behavior, entry points, or packet contract is indexed or should be indexed in `Documentation/FeatureIndex.md` |
| `infra` | Changes to `Directory.Build.props`, `.csproj` files, `.github/workflows/`, `UiAutomationTests/` test infrastructure (not individual tests), fixture assets under `UnitTests/TestAssets/` |
| `test-only` | Only test files changed; no production code changed |

Record your tags — they gate every subsequent phase.

---

## Phase 2 — Unit test requirements

### 2a. `protocol` tag
**Mandatory.** For every packet type added or modified in `HandleMessage`:

1. Add or update test(s) in `UnitTests/NetworkTests.cs`.
2. Follow the existing pattern exactly:
   - Instantiate `AOClient("ws://localhost:10001/")` — no real socket is opened.
   - Call `await client.HandleMessage("PACKET#field1#field2#%")`.
   - Assert on resulting state via the public API or reflection where needed.
3. Tag the test class or method `[Category("NoNetworkCall")]`.
4. If the change involves AO2 symbol escaping (`<percent>`, `<dollar>`, `<num>`, `<and>`),
   add a round-trip test through `Globals.ReplaceTextForSymbols` /
   `Globals.ReplaceSymbolsForText`.

### 2b. `ai-pipeline` tag
**Mandatory.** For every change to `AOClientAgentResponseParser`, action types,
or `AOClientAgentDecision`:

1. Add or update test(s) in `UnitTests/AOClientAgentResponseParserTests.cs`.
2. Use raw-string JSON literals (`"""{...}"""`).
3. Cover: valid parse of the new type, missing required fields, unknown/extra
   field tolerance, action-list ordering where relevant.
4. Tag `[Category("NoAPICall")]` — no real provider calls.

For `AO2AiBotPromptBuilder` changes, add/update cases in
`UnitTests/AO2AiBotPromptBuilderTests.cs` asserting the assembled prompt
contains the correct instruction fragments.

### 2c. `save-data` tag
**Mandatory.** Verify or add a test that round-trips the changed property
through `SaveFile.SaveToDisk()` / `SaveFile.LoadSnapshotFromDisk()`. Confirm
new required properties have sensible defaults that do not break deserialization
of savefiles written before the property existed.

### 2d. `startup` tag
If a new `StartupFunctionalityOption` was added, confirm:
- A matching `StartupFunctionalityIds` constant exists.
- The option's `RequiresServerEndpoint` value is correct.
- The visibility filter in `GetVisibleOptions()` is intentional.

### 2e. `test-only` tag
No new tests required. Still run Phase 4 verification.

---

## Phase 3 — UI / smoke test requirements

### 3a. `wpf-window` tag — new window sub-rule (MANDATORY, all five points)

**3a-1. Class root check.**
The `.cs` file must inherit `OceanyaWindowContentControl`, not `Window`.
`OceanyaWindowIntegrationTests` in `UnitTests` enforces this at test time —
a bare `Window` subclass outside the allowlist (`GenericOceanyaWindow`,
`WaitForm`, `LoadingScreen`) will fail that test.

**3a-2. Readiness signaling.**
The window must call `MarkAutomationReady()` (inherited from
`OceanyaWindowContentControl`) once its deterministic startup work is complete.
`FlaUiSmokeApp.WaitForReadyWindow()` polls for `AutomationId="Oceanya.ReadyMarker"`
with `Name="Ready"` — the window will time out in every test if this is missing.

**3a-3. AutomationId coverage.**
Add `AutomationProperties.AutomationId="WindowName.ElementRole"` to every
interactive element that a test or user workflow will need to locate:
buttons, text inputs, combo boxes, lists, tabs, and any element asserted in
smoke tests. Use the `WindowName.ElementRole` convention already established
(`InitialConfig.Launch`, `Main.AddClient`, `FolderVisualizer.Search`, etc.).
Do NOT use position-based or name-based locators — `FlaUiSmokeApp` uses
`AutomationId` exclusively.

**3a-4. Smoke test.**
Add at minimum a `Launch_<WindowName>_IsReady()` test to
`UiAutomationTests/FirstWaveSmokeTests.cs`:
```csharp
[Test]
public void Launch_<WindowName>_IsReady()
{
    app = FlaUiSmokeApp.Launch(SmokeFixturePaths.BuildAutoLaunchArguments(
        StartupFunctionalityIds.<RelevantId>));
    Window w = app.WaitForReadyWindow("<WindowName>.PrimaryElement");
    Assert.Multiple(() =>
    {
        Assert.That(app.WaitForDescendantById(w, "<WindowName>.PrimaryElement"), Is.Not.Null);
        // add one assertion per critical interactive element
    });
}
```
Tag the fixture `[Category("Smoke")]`. If the window requires a server
connection, use `SmokeFixturePaths.BuildAutoLaunchArguments` with
`UseSingleInternalClient: true` already set in the smoke savefile.

**3a-5. Fixture assets.**
If the window needs a character, savefile state, or config value not already
present in `UnitTests/TestAssets/FlaUISmoke/`, add the minimal asset there.
Do not add assets that depend on external paths or live network data.

### 3b. `wpf-ui` tag — AutomationId audit (MANDATORY on renames/removals)

If any `AutomationProperties.AutomationId` value was renamed or removed,
grep `UiAutomationTests/` for the old value and update every call site in
`FirstWaveSmokeTests.cs` and `OnlineLaneTests.cs`. A mismatched id causes
`WaitForDescendantById` to time out silently without a helpful error.

If new interactive elements were added without AutomationIds, add them now
using the `WindowName.ElementRole` convention.

### 3c. `startup` tag — smoke test for new launch mode

If a new `StartupFunctionalityOption` was added and it opens a window,
apply rule 3a-4 for that window.

### 3d. Online lane consideration

The Online lane (`UiAutomationTests/OnlineLaneTests.cs`) covers real AO2
TCP transport. If you changed anything in `AOClient.Connect()`, the
handshake packet sequence, or OOC send transport, note that the Online tests
should be run manually on an interactive Windows desktop before merging. Do
NOT add this to the standard `verify` step — it requires a self-hosted runner.

---

## Phase 4 — Documentation

### 4a. `feature-index` tag (or any `wpf-window`, `protocol`, `ai-pipeline` tag)

1. Open `Documentation/FeatureIndex.md`.
2. Find or create the entry for the affected feature.
3. Update: main code file paths, linked doc filename, and any notes that
   describe packets, behavior contracts, or known pitfalls.

### 4b. Focused feature doc

If the changed feature already has a linked doc (see `FeatureIndex.md`),
update it. Specifically update:
- Entry points / file ownership if files were added, removed, or renamed.
- Packet formats or external behavior contracts if changed.
- Known pitfalls section if the change introduced or resolved one.
- Test coverage section: list the test class(es) and categories that cover it.

If the feature has no linked doc and the change is substantial enough that
future work would benefit from a written record of the behavior (non-obvious
packet flow, AO2 parity quirks, tricky lifecycle), create a focused doc and
link it from `FeatureIndex.md`.

### 4c. Solution folder sync

If any `.md` files were added to or removed from `Documentation/`, open
`Oceanya Client.sln` and update the `Documentation` solution folder's
`<None Include="..."/>` entries so the docs remain visible in Visual Studio.
Stale entries cause "file not found" warnings in the IDE.

### 4d. `infra` tag

If CI workflow files (`.github/workflows/ui-smoke.yml`, `ui-online.yml`) were
changed, confirm the `paths:` trigger list still covers the right projects and
that build and test remain as **separate steps** (never chained with `&&` or
`; then`).

---

## Phase 5 — Verification

**This is not optional and must always be the final step.**

Run these two commands **sequentially as separate calls**. Never chain them
with `&&` — the `obj/` output race will produce spurious lock errors.

```bash
# Step 1 — build
/mnt/c/Program\ Files/dotnet/dotnet.exe build "Oceanya Client.sln"
```

Wait for success. If the build fails, fix it before proceeding.

```bash
# Step 2 — unit tests (excludes live-server and credential-requiring tests)
/mnt/c/Program\ Files/dotnet/dotnet.exe test UnitTests/UnitTests.csproj \
    --filter "Category!=RequiresCredentials&Category!=RequiresConnection"
```

**Do NOT run `UiAutomationTests` here.** Those require an interactive Windows
desktop session. Running them from WSL produces misleading failures. Smoke
tests are run by CI (`ui-smoke.yml`) on push to main. Online tests require a
self-hosted runner (see `ui-online.yml`).

If any unit test fails:
1. Read the failure message — do not retry blindly.
2. Fix the root cause.
3. Re-run build then test as separate commands.

Only declare the task complete after both commands exit cleanly.

---

## Phase 6 — Completion summary

Report to the user:

1. **Labels applied:** list of tags from Phase 1.
2. **Tests written or updated:** list each file and test method name.
3. **Smoke test actions:** what was added/updated in `FirstWaveSmokeTests.cs`,
   or "none required."
4. **Documentation actions:** what was updated in `FeatureIndex.md` and/or
   a feature doc, or "none required."
5. **Build result:** pass or fail (with error if failed).
6. **Unit test result:** pass/fail counts, or filtered category note.
7. **Any deferred actions:** things that require an interactive Windows desktop
   (Online lane, manual smoke run) that were intentionally skipped.
