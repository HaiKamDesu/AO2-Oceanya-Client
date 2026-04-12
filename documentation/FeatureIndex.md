# Feature Index

Use this file as the first stop before broad repository searches. It should point future work at the right files and focused docs fast.

## AO2 Chat And Log Behavior
- Doc: `Documentation/AO2-Chat-Config-Coloring-Guide.md`
- Main code: `AOBot-Testing/Agents/AOClient.cs`, `AOBot-Testing/Structures/ICMessage.cs`, `OceanyaClient/MainWindow.xaml.cs`, `OceanyaClient/Components/ICLog.xaml.cs`
- Notes: Covers IC/OOC packet handling, log formatting, and AO2-facing display behavior.

## Character File Creator
- Doc: `Documentation/AgentDocumentationWorkflow.md`
- Main code: `OceanyaClient/Components/Forms/AOCharacterFileCreatorWindow.xaml.cs`, `OceanyaClient/Features/CharacterCreator/AOCharacterFileCreatorBuilder.cs`, `OceanyaClient/Features/CharacterCreator/GeneratedAssetPathCollisionResolver.cs`
- Notes: Includes file organization, generated assets, and duplicate-name collision handling.

## Character Tagging
- Docs:
  - `Documentation/TaggingCharacters/CharacterTaggingMigrationGuide.md`
  - `Documentation/TaggingCharacters/CharacterFolderIdentitySummary.md`
  - `Documentation/TaggingCharacters/TaggingForWindowsTags.md`
- Main code: tagging-related workflows under `OceanyaClient` plus supporting data in `Documentation/TaggingCharacters`

## Release Packaging
- Doc: `Documentation/AgentDocumentationWorkflow.md`
- Main code: `Directory.Build.props`, `OceanyaHivemindAgent/OceanyaHivemindAgent.csproj`, `OceanyaClient/OceanyaClient.csproj`
- Notes: Release builds package the client output as `Oceanya Client v<version>` and generate a zip with the same folder inside it.

## File Hivemind
- Doc: `Documentation/AgentDocumentationWorkflow.md`
- Main code: `OceanyaClient/Features/FileHivemind/*`, `OceanyaHivemindAgent/Program.cs`, `Common/SaveFile.cs`
- Notes: Background agent lifecycle, saved connection state, and parent/child shutdown signaling live here.
