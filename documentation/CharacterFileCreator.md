# Character File Creator

## Purpose
The character file creator builds AO2-compatible character folders, lets users organize generated assets before export, and previews the most important character assets inside the editor.

## Main Entry Points
- `OceanyaClient/Components/Forms/AOCharacterFileCreatorWindow.xaml`
- `OceanyaClient/Components/Forms/AOCharacterFileCreatorWindow.xaml.cs`
- `OceanyaClient/Features/CharacterCreator/AOCharacterFileCreatorBuilder.cs`
- `OceanyaClient/Features/CharacterCreator/GeneratedAssetPathCollisionResolver.cs`

## Emote Tile Behavior
- Emote tiles are rendered from the `EmoteTileContentTemplate` in the XAML file.
- Tile selection and reorder behavior live in the `EmoteTilesListBox_*` handlers.
- Double-click rename now only uses the visible emote-name text bounds instead of the full row width.
- The SFX shortcut text now only reacts when the pointer is over the rendered text instead of the whole bottom row.
- The emote header includes a `(?)` help tooltip that documents the main interactions in the grid.

## Button Icon Behavior
- Button icon generation is driven by `ButtonIconGenerationConfig`, `TryBuildButtonIconPair`, and `BuildButtonIconGenerationConfig`.
- Tile previews now reflect AO2 semantics:
  - selected emote tile -> preview `button_on`
  - unselected emote tile -> preview `button_off`
- Clicking the button-icon field on an unselected tile imports the asset as `button_off`.
- Clicking the button-icon field on the selected tile imports the asset as `button_on`.
- Direct button-field clicks no longer change tile selection or start tile drag; they only import the targeted button asset.
- Two-image previews can show whichever side is already assigned, while the missing side still renders as empty.
- File organization and final export only treat button icons as generated when a real `button_on`/`button_off` pair can be produced from the current config.

## File Organization Viewers
- Double-click behavior is centralized in `OpenFileOrganizationEntry`.
- Text assets still open the existing text viewer/editor.
- Image assets now open a view-only image viewer with:
  - next/previous navigation across image assets in the same folder
  - animated playback controls when the source is animated
  - zoom controls and mouse-wheel zoom
  - a fixed preview frame where the image pans and scrolls inside the frame instead of resizing the window layout
- Audio assets now open a view-only sound player with:
  - next/previous navigation across sound assets in the same folder
  - basic play/stop playback and progress display

## Export Notes
- Emote images copy into `Images/`
- Emote SFX copy into `Sounds/`
- Generated button icons save into `emotions/`
- File organization overrides can move generated outputs away from their default locations, so the final generated path should be read from the file organization tab rather than assumed.

## Known Pitfalls
- A stale button preview can hide broken button config, so previews should always be derived from `TryBuildButtonIconPair`.
- The file organization section should rebuild generated entries whenever the user opens Step 5 so freshly configured `emotions/` outputs are visible immediately.
- Generated button entries in file organization are preview-only before export; they may not have a physical source file on disk yet.
- Animated asset viewers depend on `AnimationTimelinePreviewController` and `Ao2AnimationPreview`; static fallback is used when an animation controller cannot be created.

## Test Coverage
- `UnitTests/AOCharacterFileCreatorBuilderTests.cs`
- `UnitTests/CharacterFolderVisualizerWindowTests.cs`

## Missing Coverage
- No focused automated tests currently cover emote tile hitboxes, file-organization double-click viewers, or release-button preview synchronization.
