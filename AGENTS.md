## Repository Structure
- **AO2-Client/**: Git submodule pointing to the original Attorney Online client (reference code only)
- **AOBot-Testing/**: My implementation of an Attorney Online bot client
- **Common/**: Shared utilities and code for my implementation
- **OceanyaClient/**: My main WPF client implementation
- **UnitTests/**: Tests for my implementation

## Reference Code Usage
The code in the AO2-Client submodule is from the original Attorney Online project and is checked out at tag `v2.11.0-release`. It should be used for:
- Understanding network protocols and packet structures
- Referencing game mechanics and logic
- Studying UI workflows and features

Claude should NOT copy this code directly but use it to understand patterns and functionality while helping me implement my own C# WPF version.

## Working with Git Submodules
- The AO2-Client submodule is set to the v2.11.0-release tag
- When cloning this repository for the first time, use: `git clone --recurse-submodules`
- To update the submodule after cloning without the above flag: `git submodule update --init --recursive`

## Build And Test Verification

Any time code is changed, run build and test to verify the work using these exact commands from the project root:

```bash
/mnt/c/Program\ Files/dotnet/dotnet.exe build "AOBot-Testing.sln"
/mnt/c/Program\ Files/dotnet/dotnet.exe test "AOBot-Testing.sln"
```

## Code Style Guidelines
- **Naming**: Use PascalCase for classes, methods, and properties. Use camelCase for local variables and parameters.
- **Formatting**: Use 4-space indentation. Keep line length under 120 characters.
- **Types**: Always use explicit types. Enable nullable reference types with `<Nullable>enable</Nullable>`.
- **Error Handling**: Use try-catch blocks for expected exceptions. Log errors with CustomConsole.Error(message, exception). Use appropriate log levels (Info, Warning, Error, Debug).
- **Async**: Use async/await pattern for asynchronous operations. Avoid blocking calls.
- **Comments**: Use XML documentation comments for public methods and classes.
- **Dependencies**: Use dependency injection where possible.
- **Organization**: Keep code organized in the existing namespace structure (AOBot_Testing.Agents, AOBot_Testing.Structures).