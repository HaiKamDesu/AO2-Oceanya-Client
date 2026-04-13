## Repository Structure
- **AO2-Client/**: Git submodule pointing to the original Attorney Online client. Reference code only.
- **tsuserver3/**: Git submodule pointing to the main Attorney Online server implementation. Reference code only.
- **tsuserverCC/**: Git submodule pointing to a widely used Attorney Online server fork. Reference code only.
- **AOBot-Testing/**: Core AO2 protocol/client implementation.
- **Common/**: Shared utilities and persistence helpers.
- **OceanyaClient/**: Main WPF client implementation.
- **OceanyaHivemindAgent/**: Standalone tray/background process for File Hivemind sync.
- **AO2AIBot/**: AI agent logic, prompt assembly, parsing, and provider integration.
- **UnitTests/**: NUnit 4 test suite.
- **Documentation/**: Repository documentation for humans and future AI agents.

## Reference Code Usage
The code in `AO2-Client`, `tsuserver3`, and `tsuserverCC` exists for reference only. Use it to understand:
- AO2 network protocols and packet structures
- game mechanics and chat/log behavior
- server-side packet handling, area management, permissions, and moderation behavior
- common AO2 customization patterns
- UI workflows and feature parity targets

Do **not** copy this code directly into the C# projects. Use it to understand behavior, then implement equivalent logic natively in this repository.

## Working with Git Submodules
- The repository includes `AO2-Client`, `tsuserver3`, and `tsuserverCC` as git submodules.
- First-time clone: `git clone --recurse-submodules`
- If already cloned without submodules: `git submodule update --init --recursive`

## Build And Test Verification
Run from the project root. Use this exact dotnet path on WSL:

```bash
/mnt/c/Program\ Files/dotnet/dotnet.exe build "Oceanya Client.sln"
/mnt/c/Program\ Files/dotnet/dotnet.exe test "Oceanya Client.sln"
```

**IMPORTANT — run build and test as separate commands, never chained.**
Chaining them with `&&` (e.g. `build ... && test ...`) causes a race on the
`obj/` output directory: the test runner can lock files that the build just
wrote, producing spurious lock errors and making it look like tests failed
when they did not. Always issue the build command first, wait for it to
succeed, then issue the test command in a separate call.

Run a single test class or method:

```bash
/mnt/c/Program\ Files/dotnet/dotnet.exe test UnitTests/UnitTests.csproj --filter "ClassName=UnitTests.AOClientAgentResponseParserTests"
/mnt/c/Program\ Files/dotnet/dotnet.exe test UnitTests/UnitTests.csproj --filter "FullyQualifiedName~MethodName"
```

Always run build and test after any code change to verify the work.

The app version is centrally defined in `Directory.Build.props` via `OceanyaAppVersion`.

## Project Structure

| Project | Purpose |
|---|---|
| `OceanyaClient` | WPF frontend, the main executable. Multi-client AO2 controller with AI integration. |
| `AOBot-Testing` (`AO2.csproj`) | Core AO2 protocol layer. `AOClient`, packet/message structures, character parsing, and transport logic. |
| `AO2AIBot` | AI agent logic: prompt building, response parsing, client control. Provider-agnostic (OpenAI + Ollama). |
| `Common` | Shared utilities: `SaveFile`/`SaveData`, `Globals`, `CustomConsole`, `CountdownTimer`, sync helpers. |
| `OceanyaHivemindAgent` | Standalone tray/background app for File Hivemind sync. |
| `UnitTests` | NUnit 4 test project referencing the main projects. |

## Architecture

### Startup Flow
`StartupFunctionalityCatalog` defines the launch modes:
- **GM Multi-Client**: multiple `AOClient` instances controlled from `MainWindow`
- **AO2 AI Bot (Dev)**: same UI with `AOClientAgentController` wired to each client; only visible in `DEBUG` builds when `Environment.UserName == "Usuario"`
- **Character Database Viewer / File Creator / File Hivemind**: offline tools

### AO2 Protocol Layer (`AOBot-Testing`)
`AOClient` owns the AO2 transport and packet handling.

Important callbacks/events include:
- `OnICMessageReceived`
- `OnIcActionReceived`
- `OnOOCMessageReceived`
- `OnChangedCharacter`
- `OnBGChange`
- `OnReconnectionAttempt`

Server endpoints are loaded from `OceanyaClient/server.json` via `Globals.LoadServerIPs()`.

AO2 message text uses symbol escaping:
- `<percent>` -> `%`
- `<dollar>` -> `$`
- `<num>` -> `#`
- `<and>` -> `&`

Use `Globals.ReplaceTextForSymbols` and `Globals.ReplaceSymbolsForText` when crossing the protocol boundary.

### AI Agent Pipeline (`AO2AIBot`)
1. `AOClientAgentController` receives `ChatLogEntry` records, queues evaluation work, and calls `IAiChatCompletionService`.
2. `AiChatCompletionService` selects `GPTClient` or `OllamaClient` based on `AiChatProviderSettings.Provider`.
3. `AO2AiBotPromptBuilder` assembles the user prompt from client state and transcript history.
4. `AO2AiBotPromptCatalog` provides the system instruction set.
5. `AOClientAgentResponseParser` parses `SYSTEM_WAIT()` or JSON responses into `AOClientAgentDecision`.

The resulting decision is executed through delegates wired by `MainWindow`.

### Save Data (`Common`)
`SaveFile.Data` is the in-memory singleton.
- `SaveFile.SaveToDisk()` persists it
- `SaveFile.LoadSnapshotFromDisk()` returns a copy without mutating the singleton

### File Hivemind
`OceanyaClient` can launch `OceanyaHivemindAgent.exe` as a subprocess.

The background agent uses the named `EventWaitHandle` `FileHivemindBackgroundAgentCommandLine.AgentStopSignalEventName` so the parent can request shutdown.

### Test Conventions
Framework: NUnit 4 with Moq.

These tests depend on external services and are not expected to pass without configuration:
- `LiveServerConnectionTests`
- `GoogleDriveSyncLiveTests`
- `OllamaClientTests`
- `GPTClientTests`

## Documentation Workflow
To save tokens and reduce repeated broad searches, agents should treat `Documentation/` as a maintained feature index plus focused topic docs, not as a dumping ground.

Before doing a wide code search for a feature:
1. Check `Documentation/FeatureIndex.md`.
2. Open the most relevant linked doc(s).
3. Only then do a targeted code search for the specific classes/files that doc points to.

When an agent investigates, fixes, adds, removes, or significantly refactors a feature:
1. Update `Documentation/FeatureIndex.md` if the feature entry is missing, renamed, or moved.
2. Create or update a focused markdown doc for that feature when the behavior, entry points, packet flow, file ownership, or caveats would help future work.
3. Keep docs concise and high-signal. Prefer one index entry plus one focused doc over large repetitive writeups.
4. Delete or rewrite stale docs when they are actively misleading. Do not leave contradictory documentation behind.
5. If you add, remove, or rename files under `Documentation/`, also update the solution's `Documentation` solution folder so the files stay visible in Visual Studio.

Documentation should usually capture:
- feature purpose
- main entry points/files
- important packet formats or external behavior contracts
- known pitfalls, parity quirks, or gotchas
- test coverage and important missing coverage

## Code Style
- **Naming**: PascalCase for classes, methods, and properties. camelCase for locals and parameters.
- **Formatting**: 4-space indentation. Lines under 120 characters.
- **Types**: Use explicit types. Nullable reference types are enabled.
- **Error handling**: Use `try-catch` for expected exceptions. Log via `CustomConsole.Error(message, exception)`.
- **Async**: Prefer `async`/`await`. Avoid blocking calls.
- **Comments**: XML documentation comments on all public methods and classes.
- **Dependencies**: Inject dependencies rather than constructing them inline when practical.
- **Namespaces**: Follow the existing structure such as `AOBot_Testing.Agents`, `AOBot_Testing.Structures`, etc.

## UI Consistency
- **Dark ComboBoxes**: For dark-themed windows, use the fully themed ComboBox pattern from `InitialConfigurationWindow.xaml` / `CharacterFolderVisualizerWindow.xaml`. Do not ship light/default dropdown popups in dark windows.
- **Auto-complete dropdowns**: Use `AutoCompleteComboBoxBehavior` for editable searchable dropdowns with filter-on-type, arrow navigation, and Enter-to-commit behavior.
