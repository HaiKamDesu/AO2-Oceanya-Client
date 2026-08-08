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

## Stacked Button Effects
- Each button state (`button_on` / `button_off`) owns an ordered stack of effects instead of a single effect mode.
- Model: `ButtonEffectConfig` holds `List<ButtonEffectLayer> Layers` plus `CustomPresetId`; `ButtonEffectLayer.Kind` is one of `ReduceOpacity`, `Darken`, `Overlay`, `Border`. An empty stack means "use the base image as-is" (the old `None` mode). `ButtonEffectConfig.CreateDarken(50)` builds the historical `button_off` default.
- Rendering: `ApplyButtonEffectToBitmap` walks the layers in list order and calls `ApplyButtonEffectLayerToBitmap` for each one, feeding the previous result into the next. Border is now just another layer, so `border → darken → overlay → border` is expressible.
- UI: `BuildButtonEffectEditorSection` builds a collapsible section per state (`AnimateSectionExpansion` animates `MaxHeight`), a collapsible card per layer with move-up/move-down/remove chips (`CreateEffectChipButton`), a green `＋ Add effect` chip whose context menu offers the four kinds, and the preset row. Layer expansion state lives on the UI-only `ButtonEffectLayer.IsExpanded`.
- Presets: `CharacterCreatorButtonEffectPreset` / `CharacterCreatorButtonEffectSnapshot` persist the full `Layers` list, so a whole stack saves and reloads as one preset. The legacy single-effect fields (`Mode`, `OpacityPercent`, `DarknessPercent`, `OverlayPath`, `AddBorder`, `BorderColor`, `BorderWidth`) are kept only for migration: `SaveFile.NormalizeButtonEffectLayers` rebuilds them into an equivalent stack (effect first, border on top) when no layers were persisted.
- Overlay images referenced by any layer are cached/kept alive through `EnumerateButtonEffectOverlayPaths` in `CleanupCachedCharacterCreatorButtonIconAssets`.
- Tests: `UnitTests/AOCharacterFileCreatorBuilderTests.cs` (`ButtonEffectStack_AppliesEveryLayerInOrder`, `ButtonEffectStack_EmptyStack_LeavesBaseImageUnchanged`), `UnitTests/TestabilityHardeningTests.cs` (`ButtonEffectPresets_LegacySingleEffectSavefile_MigratesIntoLayerStack`).

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
