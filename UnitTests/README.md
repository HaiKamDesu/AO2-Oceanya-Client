# Attorney Online - Oceanya Bot Unit Tests

This directory contains comprehensive unit tests for the AO2-Oceanya-Bot project. These tests ensure the functionality of various components of the application and help prevent regressions when making changes.

## Test Files Overview

1. **BotJSONHandlingTests.cs**
   - Tests for JSON serialization/deserialization of bot messages
   - Tests for player list parsing from server messages
   - Tests for special character handling in JSON

2. **LiveServerConnectionTests.cs**
   - Real-server integration checks against the default configured endpoint
   - Verifies real connection, multi-client connection, and OOC send flow
   - Marked `RequiresConnection` and `Explicit` for manual execution

3. **ChatLogManagerTests.cs**
   - Tests for chat log storage and retrieval
   - Tests for message history limitations
   - Tests for chat log formatting
   - Integration tests with AOClient

4. **CustomUnitTests.cs**
   - Contains CountdownTimer tests
   - ICMessage tests for packet creation/parsing
   - Tests for utility scripts and analysis

5. **GPTClientTests.cs**
   - Tests for GPT API client functionality
   - Tests for handling API responses and errors
   - Tests for system instructions and variables

6. **GlobalsTests.cs**
   - Tests for global utility functions
   - Tests for special character replacement
   - Tests for configuration loading

7. **INIParserTests.cs**
   - Tests for character INI file parsing
   - Tests for emote configuration loading
   - Tests for character folder management

8. **NetworkTests.cs**
   - Tests for network communication with AO2 server
   - Deterministic packet-handler tests (no live sockets)
   - Tests for packet handling and protocol implementation
   - Protocol parsing validation for SC/CharsCheck/CT/BN/SP

9. **StructureTests.cs**
   - Tests for Emote class functionality
   - Tests for Background class functionality
   - Tests for game asset management

10. **LiveServerConnectionTests.cs**
    - Live external connectivity tests for AO2 server behavior

## Running the Tests

To run all tests EXCEPT those that might make real network/API calls:

```bash
dotnet test UnitTests/UnitTests.csproj --filter "Category!=RequiresCredentials"
```

To run all tests that are specifically designed to NOT use real API calls:

```bash
dotnet test UnitTests/UnitTests.csproj --filter "Category=NoAPICall"

To run live server integration tests (manual only):

```bash
dotnet test UnitTests/UnitTests.csproj --filter "Category=RequiresConnection"
```

Live integration logs are written to:
- `%TEMP%\\ao2_live_connection_YYYYMMDD.log`
```

To run a specific test file:

```bash
dotnet test UnitTests/UnitTests.csproj --filter "FullyQualifiedName~UnitTests.ChatLogManagerTests"
```

To run a specific test:

```bash
dotnet test UnitTests/UnitTests.csproj --filter "FullyQualifiedName=UnitTests.ICMessageTests.Test_ICMessage_ToCommand"
```

## Important Safety Notes

1. **GPT API Tests**: All GPT client tests use mock HTTP clients to prevent real API calls to OpenAI. This avoids any charges to your OpenAI account during testing.

2. **Network Tests**: The NetworkTests class implements a mock AO2 server that runs locally and doesn't make any real network connections.

3. **W2G Tests**: The W2GTests are marked with `[Ignore]` and have fake credentials to prevent accidental execution.

4. **Test Categories**:
   - `NoAPICall`: Tests that explicitly won't make API calls
   - `NoNetworkCall`: Tests that won't make network connections
   - `RequiresCredentials`: Tests that would need real credentials and make real external connections (ignored by default)

## Test Dependencies

- NUnit 4.3.2 - Testing framework
- NUnit3TestAdapter 5.0.0 - Adapter for running NUnit tests
- Microsoft.NET.Test.Sdk 17.13.0 - .NET test SDK
- Moq 4.20.70 - Mocking framework for unit tests

## Test Coverage

These tests cover all major components of the application:

- Network communication protocol
- Character and emote handling
- Background and assets management
- Message parsing and formatting
- Chat log management
- GPT API integration
- JSON handling
- Configuration management
- Utility functions

## Best Practices

When adding new features or making changes to the codebase:

1. Run the existing tests to ensure you haven't broken anything
2. Add new tests for any new functionality
3. Update existing tests if necessary
4. Ensure all tests pass before committing changes

## Mocking External Dependencies

Several tests use mocking to simulate external dependencies:

- `NetworkTests.cs` implements a mock AO2 server
- `GPTClientTests.cs` uses Moq to mock HTTP responses
- `BackgroundTests` uses temporary directories to simulate file system

This allows tests to run without actual external dependencies, making them more reliable and faster to execute.
