# Character Folder Tagging Migration Guide

Use this guide when asked to apply tags to specific character folders.

## Scope and intent
- Goal: migrate/enrich character-folder tags into the app tag cache directly (no UI actions).
- Primary cache file to modify:
  - `C:\Users\Usuario\AppData\Roaming\OceanyaClient\cache\character_folder_tags.json`
- Ordered folder source (when asked for first N or specific ordering):
  - `C:\Users\Usuario\AppData\Roaming\OceanyaClient\cache\folder_visualizer_<hash>.json`
  - Read `Items` in order.
- Tagging for Windows reference transcript:
  - `documentation/TaggingCharacters/TaggingForWindowsTags.md`

## Required procedure (must follow)
1. Read app cache and identify targets.
- Load ordered folders from `folder_visualizer_<hash>.json`.
- Select only requested targets (for example first 10, or explicit folder list).

2. Load existing app tags first (canonical taxonomy).
- Open `character_folder_tags.json`.
- Inspect all existing tag values in `FolderTags`.
- Reuse existing tag names/casing/spelling whenever possible.
- Avoid duplicate/synonym drift (do not create many tags for the same concept).

3. For each target folder, gather evidence.
- Use `TaggingForWindowsTags.md` as legacy reference for that folder.
- Inspect folder content on disk.
- Always read any `.txt` / `.md` readme/notes inside the folder and use key info.
- Pick at least one representative emote image and visually inspect it.
- Determine media/animation evidence from actual files (`png/jpg/webp` and `gif/apng`, etc.).
- Use emote count from character cache/char.ini to assign bucket tags.

4. Identity disambiguation and lookup.
- If folder name is ambiguous, use image/readme/ini evidence to infer identity.
- When identity can be inferred, attempt online lookup to confirm franchise/game/character.
- Record uncertainty if not fully certain.

5. Assign rich, normalized tags.
- Include at minimum these categories:
  - Gender: `male` / `female` / `nonbinary`
  - Age bucket: `kid` / `teen` / `young adult` / `adult` / `old`
  - Species/type: `human` / `monster` / `other`
  - Franchise
  - Specific game entry (when applicable)
  - Visual/wardrobe/traits (hair, outfit, accessories, skin tone, vibe/role like military/medic, etc.)
  - Personality descriptors (only if evidence-based)
  - Media type: `2d` / `3d` / `both`
  - Animation presence: `static` / `animated` / `both`
  - Emote-count buckets (from actual count):
    - `emotes 5 or less`
    - `emotes over 5`
    - `emotes over 10`
    - `emotes over 20`
    - `emotes over 40`
- Add high-signal filter tags if useful.
- Keep naming consistent and lowercase unless existing canonical tag dictates otherwise.

6. Write only the cache file.
- Modify only `character_folder_tags.json`.
- Update/add entries in `FolderTags` by folder key (folder-name key format used by app cache).
- Do not rename/move folders.
- Do not modify other files unless explicitly requested.

7. Output concise report.
- For each processed folder, output:
  - Full folder path
  - Final tags applied
- Include a short section for uncertain identifications/assumptions.

## Data format notes
- `character_folder_tags.json` structure:
  - `Version`
  - `FolderTags` (object: key = folder key, value = string[] tags)
  - filter/ui fields (`ActiveIncludeTagFilters`, etc.) should be preserved.
- Keep tag arrays deduplicated and normalized.

## Operational constraints
- No UI actions for tagging.
- Direct file edit only.
- Process only requested folders.
- Preserve existing unrelated tag assignments.

## Reusable task prompt template
Use this template for future runs:

"Apply tags to character folders `[...targets...]` using `documentation/TaggingCharacters/CharacterTaggingMigrationGuide.md`. Read and reuse canonical tags from `character_folder_tags.json`, reference legacy tags from `documentation/TaggingCharacters/TaggingForWindowsTags.md`, inspect representative emote images and all `.txt/.md` notes per folder, attempt online identity lookup when inferable, and write tags directly to `C:\\Users\\Usuario\\AppData\\Roaming\\OceanyaClient\\cache\\character_folder_tags.json` only. Return folder->tags report plus uncertainty notes."
