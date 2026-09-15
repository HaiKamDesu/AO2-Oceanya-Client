# Character file creator and cutout tooling

Navigation detail extracted from the `AGENTS.md` Repository Navigation Map.
Each section is the full note for one map row; the map keeps the short pointer.

## configure cutouts popup

**Also called:** "configure cutouts popup", "emote cutting", "cut emote", "crop square", "selection square", "resize the cutout square", "photoshop selection", "thirds guides on the crop"

**Status:** Confirmed

Doc: `Documentation/CutoutSelectionSurface.md`. Reusable editor: `OceanyaClient/Utilities/CutoutSelectionSurface.cs`, hosted by `AOCharacterFileCreatorWindow.ShowEmoteCuttingDialog` (single) and `ShowBulkEmoteCuttingDialog` ("Configure Cutouts" batch) inside their `previewBorder`, with `selectionSurface.Toolbar` added to `previewPanel`. Square selection lives in SOURCE-IMAGE PIXELS; image + overlay canvas are sized `pixelSize * zoom`, so display = pixel × zoom (the old `ComputeImageViewport` / `ProjectPixelSquareSelectionToViewport` / `TryCreatePixelSquareSelectionFromDisplayBounds` letterbox helpers were deleted). Features: 8 resize handles (corners anchor opposite corner, edges anchor opposite edge and keep the other axis centered), drag-inside to move, drag-outside to redraw (a bare click restores the old square instead of clearing it), Alt-drag = resize/draw from center (`SquareFromCenter`, evaluated live per mouse move), Space+drag = pan (checked with `Keyboard.IsKeyDown(Key.Space)` on mouse-down so focus does not matter; `HandleKey` swallows Space while hovered so a focused button is not clicked), middle-drag = pan, rule-of-thirds guides, dim-outside overlay, marching-ants stroke, dashed orange ghost = saved selection (`SetGhostSelection`), undo/redo (`Ctrl+Z`/`Ctrl+Y`, one entry per gesture, `ResetHistory()` on emote/source switch), arrow-key nudge (only while hovered/focused so dialog focus nav still works), wheel zoom around cursor + middle-drag pan + bounds overlay like `AssetImageViewerDialog`, and a right-click menu (center to image, fit largest square, snap to visible content, center h/v, clear, copy/paste square, undo/redo, view toggles). Dialogs keep owning persistence (`CutSelectionState`, `SaveFile.Data.CharacterCreatorCutSelections`) and mirror `surface.Selection` via `SelectionChanged`/`SelectionCommitted`. Savefile toggles: `CharacterCreatorViewImageBounds`, `CharacterCreatorCutoutShowGuides`, `CharacterCreatorCutoutDimOutside`. **GOTCHA:** `ApplyZoom` must NEVER early-out without re-running `UpdateZoomDependentLayout()`+`RefreshOverlay()` — dialogs call `SetFrame(null)` before each emote (collapses host to 0×0) and the refit often lands on the same zoom (sibling emotes share a size), which left the image invisible/unclickable with the bounds rect in the corner until the user zoomed manually (`SetFrame_AfterClearing_RestoresContentLayoutEvenWhenZoomIsUnchanged`). Tests: `UnitTests/CutoutSelectionSurfaceTests.cs` (STA).

## button effects

**Also called:** "button effects", "button background", "selected button/unselected button section", "add effect", "stack effects/layers", "border then darken then overlay", "effect preset", "background preset", "solid color/gradient layer", "multi-effect button icons"

**Status:** Confirmed

Doc: `Documentation/CharacterFileCreator.md` → "Stacked Button Effects". Each button state owns an ordered `ButtonEffectConfig.Layers` list (`ButtonEffectLayer.Kind` = `ReduceOpacity`/`Darken`/`Overlay`/`Border`); empty stack = base image untouched, `ButtonEffectConfig.CreateDarken(50)` is the `button_off` default. Rendering walks layers in order in `ApplyButtonEffectToBitmap` → `ApplyButtonEffectLayerToBitmap` (each layer draws on top of the previous result), all in `OceanyaClient/Components/Forms/AOCharacterFileCreatorWindow.xaml.cs`. UI is `BuildButtonEffectEditorSection` (collapsible section + collapsible per-layer cards via `AnimateSectionExpansion`, reorder/remove chips via `CreateEffectChipButton`, `＋ Add effect` context menu, preset row); called from the per-emote button dialog, the bulk button icons editor, and the character-icon dialog's `AddEffectsFields`. Persistence lives in `Common/SaveFile.cs`: `CharacterCreatorButtonEffectLayer` + `Layers` on `CharacterCreatorButtonEffectPreset`/`CharacterCreatorButtonEffectSnapshot`; legacy `Mode`/`AddBorder` fields are migration-only and rebuilt by `NormalizeButtonEffectLayers`. The automatic BACKGROUND uses the SAME stack type (`ButtonIconGenerationConfig.AutomaticBackground`, rendered by `BuildAutomaticBackgroundBitmap` before the cutout is drawn on top); `ButtonAutomaticBackgroundMode` is deleted. Layer kinds shared by both: `ReduceOpacity`/`Darken`/`UploadImage` (was Overlay)/`Border`/`SolidColor`/`Gradient`. `BuildAutomaticBackgroundEditor` = preview → `No BG`/built-in preset/saved-preset selector → shared stack editor → preset buttons; built-in presets become one `UploadImage` layer holding a `pack://` path (`IsBuiltInBackgroundPresetPath` keeps those out of the upload cache). Gradient strip editor is `BuildGradientLayerEditor`. Background presets persist as `CharacterCreatorButtonBackgroundPreset.Layers` (legacy migrated in `SaveFile.NormalizeBackgroundPresetLayers`); the bulk snapshot uses `BackgroundLayers`+`BackgroundLayersMigrated`, migrated UI-side in `ApplyLastBulkButtonIconsBackgroundConfig`. Tests: `UnitTests/AOCharacterFileCreatorBuilderTests.cs` `ButtonEffectStack_*` + `AutomaticBackgroundStack_*`, `UnitTests/TestabilityHardeningTests.cs` `ButtonEffectPresets_LegacySingleEffectSavefile_MigratesIntoLayerStack` + `ButtonBackgroundPresets_LegacySingleModeSavefile_MigratesIntoLayerStack`.

## shout visual becomes a gif

**Also called:** "shout visual becomes a gif", "apng/webp shout breaks", "objection_bubble wrong extension", "shout bubble extension"

**Status:** Confirmed

AO2 `AOApplication::get_image_suffix` (`AO2-Client/src/text_file_functions.cpp`) probes shout stems in order `.webp`, `.apng`, `.gif`, `.png` ONLY (non-static), so the container must match the name — renaming an `.apng` to `.gif` yields a file AO2 finds but cannot decode, and `.jpg`/`.bmp` are never probed at all. Creator code: `AOCharacterFileCreatorWindow.CopyShoutVisual` / `AddShoutOrganizationEntry` / `AddGeneratedAssetPathCollisionCandidateForShout` route the extension through `ResolveAo2ShoutVisualExtension` (keeps the four AO2 containers byte-for-byte; anything else is RE-ENCODED to real PNG via `TryLoadFirstFrame` + `SaveBitmapAsPng`, not renamed). **Root cause of the "always turns into gif" bug:** `InitializeFileOrganizationFromExistingFolder` records the discovered path per asset key into `generatedOrganizationOverrides` (e.g. `shout:visual:objection` -> `objection_bubble.gif`), and `ResolveOutputPathForAsset` prefers that override, so replacing the asset with a different container wrote the new bytes under the old extension. Fixed by `RetargetGeneratedOverrideExtension(assetKey, newExtension)`, called from `ShoutVisualFromFileButton_Click` / `ShoutSfxFromFileButton_Click` (which now also `RefreshFileOrganizationEntries()`). Same override-staleness trap applies to any other generated asset key swapped to a new extension. Shout SFX suffixes AO2 accepts (`get_sfx_suffix`): `.opus`, `.ogg`, `.mp3`, `.wav` — no transcode, extension kept as-is.

## duplicate character folder

**Also called:** "duplicate character folder", "file organization missing new asset", "dropped gif not in file organization", "new emote asset disappears after duplicating"

**Status:** Confirmed

Duplicate entry point: `CharacterFolderVisualizerWindow.OpenCharacterFolderDuplicateInCreatorAsync` -> `AOCharacterFileCreatorWindow.TryLoadCharacterFolderForDuplication` (wraps `TryLoadCharacterFolderForEditing`, then flips to create mode and sets `loadedSourceCharacterDirectoryPath`). File-organization tree is rebuilt by `RefreshFileOrganizationEntries` -> `BuildGeneratedFileOrganizationEntries` + `externalOrganizationEntries`, then `PrunePhantomStandardAssetEntries`. **Gotcha:** newly added assets default to `StandardAssetRootFolders` (`Images/`, `Sounds/`) while a real AO2 character keeps sprites at its ROOT (`ResolveLoadedCharacterRelativePathOrDefault` redirects loaded assets to their real relative path). So after loading a folder for edit/duplication, `<source>/Images` does not exist and the prune deleted the user's brand-new drag-dropped asset along with the phantom folder. `PrunePhantomStandardAssetEntries` now keeps a standard folder when it holds any external entry OR any `IsNewlyAddedAssetEntry` entry (generated entry whose `SourcePath` exists on disk and is OUTSIDE the loaded source root). Diagnostics: `WriteFileOrganizationDebugLog` traces every load/prune/generated entry.

## cannot edit a character the client has used

**Also called:** "access to the path is denied", "can't edit a folder I already edited", "changes don't follow through", "character folder locked", "gedge report"

**Status:** Confirmed (proved empirically, regression-tested)

Not an editor bug - a **file handle leak in the animation player**. `GifAnimationPlayer` held its source via
`System.Drawing.Image.FromFile`, which keeps the FILE open for the lifetime of the image, and that player
lives for as long as its sprite or background is on screen. So any character whose `.gif` the client had
displayed stayed locked for the rest of the session, and
`AOCharacterFileCreatorWindow.ReplaceCharacterFolderFromStaging`'s `Directory.Move` failed with
"access to the path is denied".

Matches the report exactly: *"if the character was used recently during the client's lifespan it'll just
give me an access to filepath denied message"*.

Measured directly (`Image.FromFile` held vs an in-memory copy held, opening the file `FileShare.None`):

```
baseline (nothing open):  writable=True
Image.FromFile held:      writable=False   <-- the lock
after Dispose:            writable=True
Image.FromStream held:    writable=True    <-- the fix
```

Fixed by decoding from an in-memory copy: `gifSourceStream = new MemoryStream(File.ReadAllBytes(path))` then
`Image.FromStream`. System.Drawing requires that stream to OUTLIVE the image (frame selection reads from
it), so it is a field disposed alongside the image in `Stop()`. Cost is one compressed GIF in memory, which
is nothing next to the decoded frames the player already holds.

Other asset loaders were checked and are already safe: `BitmapFileLoader` uses `BitmapCacheOption.OnLoad`,
`ApngFrameDecoder` uses `File.ReadAllBytes`, and every `SKCodec.Create` and the other `Image.FromFile` call
sites are inside `using`.

Note the separate cause the reporter also hit: the **stock AO2 client being open at the same time** locks
the same files, and Oceanya cannot do anything about that.

### Editing a character that is actively in use

The lock was only half of it. An edit applied while the client is showing that character also has to not
leave stale art behind, on screen AND in the emote buttons, icons and pickers. The editor therefore runs
**release, apply, reload** around `ReplaceCharacterFolderFromStaging`, via
`OceanyaClient/Features/Assets/AssetHandleReleaser.cs`:

- `Release(directory)` before the swap: stops every live animation in every viewport pane (which is what
  holds the decoded frames), then purges every path-keyed cache under that folder -
  `Ao2AnimationPreview.ReleaseCachedAssetsUnder`, `ICMessageSettings.ReleaseEmoteButtonImagesUnder`,
  `CharacterSelectorWindow.ReleaseIconsUnder`, `AO2ViewportAssetResolver.ReleaseCachesUnder`.
- `Reload(directory, previousDirectory)` after: re-indexes the folder and calls
  `AO2ViewportControl.RequestVisualRefreshForAll()`, which reuses the web-asset refresh path - visual only,
  so the current message keeps typing, its SFX do not replay and the chat queue is untouched.

**Which caches actually went stale matters.** `Ao2AnimationPreview.StaticPreviewCache` and its
`ApngDetectionCache` are keyed by PATH ALONE with no write timestamp, so they served the pre-edit image
indefinitely - this is the "changes don't seem to follow through" half of the report. The emote button,
icon and image-size caches all validate by last-write-time and self-heal, but are purged anyway so the edit
appears immediately rather than on the next natural refresh. Every release step is individually guarded, so
one failing surface cannot block the rest.

Tests: `UnitTests/AssetFileLockTests.cs` - the lock tests were verified to FAIL when reverted to
`Image.FromFile`, plus cache-purge coverage including that releasing one character does not purge another's.


## edited emote on screen still shows the old image until a restart

Reported as "I use emote 1 with image 1, edit the folder so emote 1 uses image 2, hit done, and the
viewport still shows image 1 - restarting the client applies it". Separate from the file-lock half of the
same report (see "cannot edit a character the client has used"): the files were replaced correctly, the
UI just kept describing the old ones.

**A `CharacterFolder` resolves each emote's sprite paths ONCE, at parse time.** So dropping every decoded
bitmap achieves nothing on its own - whatever still holds the pre-edit *model* keeps pointing at the
pre-edit files.

Two holders had to be re-pointed:

- **The viewport.** `AO2ViewportControl.RefreshSceneVisualsForWebAsset` repaints from
  `lastSceneRenderArgs`, which carries the `CharacterFolder` instance captured when the scene was drawn -
  and a visual refresh does NOT rewrite `lastSceneRenderArgs` (only a real render does), so it replayed
  the stale instance forever. `RequestVisualRefreshForAll` now flags the refresh as an asset EDIT, and the
  repaint re-reads the character through `ResolveCharacterForVisualRefresh(...)` and keeps the fresh
  instance in `lastSceneRenderArgs`. A late web asset still takes the cheap path: the files change, the
  character does not.
- **Everything else holding the model** - emote buttons, character icon, dropdowns. These rebind on
  `ClientAssetRefreshService.AssetsRefreshed` (`MainWindow.RebindClientsToRefreshedCharacters`), but the
  editor replaces and re-indexes the folder itself, so no refresh ran and nothing raised that event.
  `AssetHandleReleaser.Reload` now calls `ClientAssetRefreshService.NotifyCharacterFolderChanged(name)`,
  reusing the existing debounced rebind rather than adding a second path.

### The actual cause: two decode caches keyed without the write time

`DEBUG.txt` proved the pipeline was fine - release ran, the character re-indexed, the pane repainted,
`replaced=True`, the rebind finished - and the sprite still did not change. The render line said:

```
Render character char="OceanyaTest10" emote="Images/Attack" assetPath="...\OceanyaTest10\Images\Attack.png"
```

**Editing a character rewrites its sprites IN PLACE.** The file-organization overrides keep each asset's
destination name, so after an edit the path the viewport draws is the identical string - only the bytes
differ. Two caches were keyed on that path with no write time, so both kept serving the pre-edit bitmap for
the rest of the session:

1. **`Ao2AnimationPreview.StaticPreviewCache`** - keyed `(path, width, maxDim)`. Now
   `(path, lastWriteUtc, width, maxDim)`, matching `AnimationFrameCache`, which always had the timestamp.
   `ApngDetectionCache` got the same treatment, so replacing a .png with a real APNG re-probes.
2. **WPF's own `BitmapImage` URI cache** - global, keyed by `Uri`, and completely outside our code. Any
   `BitmapImage` built from a `UriSource` without `BitmapCreateOptions.IgnoreImageCache` returns the
   previously decoded bytes for a file rewritten in place. `Utilities/BitmapFileLoader` already set that
   flag; `Ao2AnimationPreview`, `ImageComboBox`, `AO2ChatPreviewControl`, `CharacterFolderVisualizerWindow`
   and `CharacterSelectorWindow` did not, so the sprite, the dropdown icon, the chatbox art, the database
   icon and the selector icon all pinned the old image.

**Both fixes are independently required** - verified by reverting each one alone and watching
`UnitTests/EditedAssetCacheInvalidationTests.cs` fail.

Why it looked intermittent: animated emotes (.gif/.webp/.apng) decode through `AnimationFrameCache`, which
was always timestamped, so they self-healed. Static .png emotes never did. Same button, different outcome,
depending only on the emote's file type.

**Rule for any new decode path: a character asset can be replaced in place, so key every cache by path AND
`LastWriteTimeUtc`, and never build a `BitmapImage` from a `UriSource` without `IgnoreImageCache`.**

### The token, not just the character

`DEBUG.txt` settled this one. The repaint ran, the character was re-read (`replaced=True`), and the render
line still said:

```
Render character char="OceanyaTest10" emote="Images/Attack" assetPath="...\OceanyaTest10\Images\Attack.png"
```

**The viewport draws the animation token the MESSAGE carried, not the character's emote list.** So
re-reading the character repaints the very same file. Repointing emote 1 at a different image stayed
invisible until a new message was sent (or a restart), while merely replacing the CONTENTS of the existing
file did show up - which is what made it look intermittent.

Only the identity of the emote survives an edit, so
`AO2ViewportControl.ResolveEmoteTokenForVisualRefresh(...)` maps the rendered token BACK to its emote in
the pre-edit parse (by animation token, then by name) and FORWARD to that emote's current animation in the
re-read parse (by id, then by name, since a reorder keeps the name but not the id). A token that matches no
emote is left alone rather than guessed.

### Why it also looked pane-dependent

`RefreshSceneVisualsForWebAsset` consumed both pending counters BEFORE its `!IsVisible` bail-out, so a
repaint requested while a pane was hidden was dropped on the floor and never retried. In GM multi-client
only the selected client's pane is visible, and every pane is hidden while the viewport window is closed -
so whether the edit "took" depended on which pane was showing when it landed. The request is now put back
and replayed from `OnIsVisibleChanged`.

### Tracing it

The whole path logs `[ASSET-EDIT]` into `DEBUG.txt` (category System, so it is present on release builds
too). Expected sequence for one edit:

```
[ASSET-EDIT] Release starting for "<dir>"
[ASSET-RELEASE] Released in-memory holds under "<dir>"
[ASSET-EDIT] Reload starting for "<dir>" (was "<old dir>")
[ASSET-EDIT] Re-indexed "<name>" with N emote(s)
[ASSET-EDIT] Visual refresh requested for N viewport pane(s)
[ASSET-EDIT] Repainting pane: coalesced=1 hasScene=True emote="..." captured="<name>" instance=XXXXXXXX emotes=N anim[emote]=<old sprite>
[ASSET-EDIT] Re-resolved character: replaced=True now="<name>" instance=YYYYYYYY emotes=N anim[emote]=<new sprite>
[ASSET-EDIT] Announcing character folder change for "<name>"; subscribers will rebind
[ASSET-EDIT] Rebinding clients and UI to the refreshed characters / Rebind finished
[ASSET-EDIT] Reload finished for "<dir>"
```

How to read a failure:
- **`Visual refresh requested for 0 pane(s)`** - no live viewport control is registered.
- **`Pane not visible; deferring repaint`** - expected for unselected panes; the repaint should follow when
  that pane is shown again.
- **`anim[emote]` identical before and after** - the edit did not change what that emote points at, or the
  re-index read the pre-edit `char.ini`.
- **`replaced=False`** with a differing `instance=` - the lookup returned the same object, meaning the
  parsed cache was not invalidated by the re-index.
- **No `Rebind finished`** - emote buttons and the character icon will still show the pre-edit model even
  if the viewport repainted correctly.

Tests: `UnitTests/InUseCharacterEditRefreshTests.cs`.
