## Build & Test

Run from the project root. Use this exact dotnet path on WSL:

```bash
/mnt/c/Program\ Files/dotnet/dotnet.exe build "Oceanya Client.sln"
/mnt/c/Program\ Files/dotnet/dotnet.exe test "Oceanya Client.sln"
```

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
| `OceanyaClient` | WPF frontend — the main executable. Multi-client AO2 controller with AI integration. |
| `AOBot-Testing` (`AO2.csproj`) | Core AO2 protocol layer. `AOClient` (WebSocket), message structures (`ICMessage`, etc.), character/INI parsing. |
| `AO2AIBot` | AI agent logic — prompt building, response parsing, client control. Provider-agnostic (OpenAI + Ollama). |
| `Common` | Shared utilities: `SaveFile`/`SaveData`, `Globals`, `CustomConsole`, `CountdownTimer`. |
| `OceanyaHivemindAgent` | Standalone background tray app for File Hivemind sync. Launched as a subprocess by `OceanyaClient`. |
| `UnitTests` | NUnit 4 test project. References all other projects. |

## AO2-Client Submodule

`AO2-Client/` is a git submodule pinned to `v2.11.0-release` of the original Attorney Online client. Use it to understand network protocols, packet structures, and game mechanics. **Do not copy this code directly** — implement equivalent behavior in the C# WPF projects.

```bash
# First-time clone
git clone --recurse-submodules

# After cloning without the flag
git submodule update --init --recursive
```

## Architecture

### Startup Flow

`StartupFunctionalityCatalog` defines the modes the user picks at launch:
- **GM Multi-Client** — multiple `AOClient` instances controlled from `MainWindow`.
- **AO2 AI Bot (Dev)** — same UI with `AOClientAgentController` wired to each client. Only visible in `DEBUG` builds where `Environment.UserName` is `"Usuario"`.
- **Character Database Viewer / File Creator / File Hivemind** — offline tools.

### AO2 Protocol Layer (`AOBot-Testing`)

`AOClient` holds a `ClientWebSocket` and implements the AO2 packet protocol. Key callbacks: `OnICMessageReceived`, `OnOOCMessageReceived`, `OnChangedCharacter`, `OnBGChange`, `OnReconnectionAttempt`.

Server endpoints are loaded from `OceanyaClient/server.json` at runtime via `Globals.LoadServerIPs()`.

AO2 message text uses symbol escaping: `<percent>` → `%`, `<dollar>` → `$`, `<num>` → `#`, `<and>` → `&`. Use `Globals.ReplaceTextForSymbols` / `ReplaceSymbolsForText` when crossing the protocol boundary.

### AI Agent Pipeline (`AO2AIBot`)

The AI pipeline is decoupled from the UI via injected delegates:

1. **`AOClientAgentController`** — receives `ChatLogEntry` records, queues evaluations with a `SemaphoreSlim` gate (one active evaluation at a time), calls `IAiChatCompletionService`.
2. **`AiChatCompletionService`** — selects `GPTClient` (OpenAI) or `OllamaClient` (local) based on `AiChatProviderSettings.Provider`. OpenAI key is read from env var `OPENAI_API_KEY` (or a custom variable name in settings).
3. **`AO2AiBotPromptBuilder`** — assembles the user prompt from `AOClientControlSnapshot` (current client state) and transcript history.
4. **`AO2AiBotPromptCatalog`** — provides system instructions. `AdditionalInstructions` from settings is appended as a second instruction block.
5. **`AOClientAgentResponseParser`** — parses the model response. Expects `SYSTEM_WAIT()` (no-op) or a JSON object. Accepts both the current shape (`state` sub-object) and the legacy shape (`modifiers` sub-object).

The `AOClientAgentDecision` from the parser is handed back to `MainWindow` via the `actionExecutor` delegate, which maps it to actual `AOClient` calls.

### Save Data (`Common`)

`SaveFile.Data` is the in-memory `SaveData` singleton. Call `SaveFile.SaveToDisk()` to persist. `SaveFile.LoadSnapshotFromDisk()` returns a copy without mutating the singleton (used by `OceanyaHivemindAgent`).

### File Hivemind

`OceanyaClient` can launch `OceanyaHivemindAgent.exe` as a subprocess. The agent uses a named `EventWaitHandle` (`FileHivemindBackgroundAgentCommandLine.AgentStopSignalEventName`) to receive stop signals from the parent.

### Test Conventions

Framework: NUnit 4 with Moq. Live network tests (`LiveServerConnectionTests`, `GoogleDriveSyncLiveTests`) and AI provider tests (`OllamaClientTests`, `GPTClientTests`) require external services and are not expected to pass without configuration.

## Code Style

- **Naming**: PascalCase for classes, methods, and properties. camelCase for local variables and parameters.
- **Formatting**: 4-space indentation. Lines under 120 characters.
- **Types**: Always use explicit types. Nullable reference types enabled (`<Nullable>enable</Nullable>`).
- **Error handling**: `try-catch` for expected exceptions. Log with `CustomConsole.Error(message, exception)`.
- **Async**: `async`/`await` throughout. No blocking calls.
- **Comments**: XML documentation comments on all public methods and classes.
- **Dependencies**: Inject dependencies rather than constructing them inline.
- **Namespaces**: Follow existing structure (`AOBot_Testing.Agents`, `AOBot_Testing.Structures`, etc.).

## UI Consistency

- **Dark ComboBoxes**: For dark-themed windows, always use the fully themed ComboBox pattern from `InitialConfigurationWindow.xaml` / `CharacterFolderVisualizerWindow.xaml`: `DarkComboBoxItemStyle`, `DarkComboBoxStyle` with custom `ControlTemplate`, dark popup/dropdown background and highlighted/selected states. Do not ship new popups with default/light-themed ComboBox dropdowns.
- **Auto-complete dropdowns**: Use `AutoCompleteComboBoxBehavior` for editable searchable dropdowns (opens on type, filters list, arrow-key navigation, `Enter` to commit).
