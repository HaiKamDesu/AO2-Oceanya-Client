# AO2 Chat Color Configuration Guide (`chat_config.ini`)

## Purpose
This document explains how the AO2 client reads `chat_config.ini` for IC text colors, what keys are supported, and how to safely customize colors.

It is based on the reference client source in `AO2-Client/`.

## Where `chat_config.ini` is loaded from
Color config values are read through:

- `AOApplication::get_chat_markup(...)` -> `get_config_value(..., "chat_config.ini", ..., p_chat)` in `AO2-Client/src/text_file_functions.cpp`.
- `get_config_value(...)` checks multiple paths in order (first hit wins), via `get_asset_paths(...)` in `AO2-Client/src/path_functions.cpp`.

For `chat_config.ini`, lookup order is:

1. `themes/<theme>/<subtheme>/misc/<chat>/chat_config.ini` (if subtheme + chatbox are active)
2. `themes/<theme>/misc/<chat>/chat_config.ini`
3. `themes/<theme>/<subtheme>/chat_config.ini`
4. `misc/<chat>/chat_config.ini`
5. `themes/<theme>/chat_config.ini`
6. `themes/<default_theme>/chat_config.ini`
7. `chat_config.ini`

`p_chat` is the chatbox/misc name for the current character when custom chatboxes are enabled.

## Supported color slots and limits
The hardcoded color slot count is:

- `max_colors = 12` in `AO2-Client/src/courtroom.h`
- Valid numeric slots are `c0` through `c11`.

When sending IC packet text color, values `>= max_colors` are forced back to `0` in `AO2-Client/src/courtroom.cpp`.

## Keys the client reads for each slot
For each `N` in `0..11`, the client reads:

- `cN` -> RGB color value as `R,G,B`
- `cN_name` -> dropdown label
- `cN_start` -> markdown start symbol
- `cN_end` -> markdown end symbol (optional; if omitted, behaves as toggle with `cN_start`)
- `cN_remove` -> `1` to remove marker chars from rendered text, otherwise keep them
- `cN_talking` -> `0` means "not talking color", any other value means talking color

Color parsing uses comma-separated integers. If invalid/missing, fallback color is white (`255,255,255`) for chat colors.

## How markup is parsed in IC text
Parsing is done in `Courtroom::filter_ic_text(...)`:

- It processes one grapheme at a time.
- Markdown markers are compared against one parsed grapheme at a time.
- Backslash escapes are supported (`\n`, `\s`, `\f`, `\p`, and escaping marker chars).
- Leading alignment shortcuts are supported:
  - `~~` center
  - `~>` right
  - `<>` justify
- Literal `{` and `}` are stripped by this parser path.

## Why `c9` works but `c11` fails
Two separate facts exist:

1. The client does support up to `c11` by design (`max_colors = 12`).
2. There is a replacement-order bug for two-digit slots.

Color placeholders are generated as `$cN` and later replaced in ascending order (`c0`, `c1`, ...).  
Because `$c1` is replaced before `$c10`/`$c11`, placeholders like `$c11` get partially consumed at the `$c1` step.

Practical result:

- `c0..c9` are reliable.
- `c10` and `c11` are unreliable/broken in rendered output without source fix.

## Recommended configuration rules
For stable behavior without modifying client source:

1. Use only slots `c0..c9`.
2. Define both color and metadata keys for each used slot:
   - `cN`, `cN_name`, `cN_start`
   - optionally `cN_end`, `cN_remove`, `cN_talking`
3. Keep `cN_start`/`cN_end` as single-symbol markers (single grapheme).
4. Avoid gaps in named slots if you want a continuous dropdown UX.
5. Keep `c0` configured as a safe default color.

## Minimal example snippet
```ini
c0=255,255,255
c0_name=Default
c0_start=

c1=255,80,80
c1_name=Red
c1_start=~
c1_end=~
c1_remove=1
c1_talking=1

c9=100,220,255
c9_name=Cyan 2
c9_start=^
c9_end=^
c9_remove=1
c9_talking=1
```

## If you want `c10` and `c11` to work
You need a source patch in the replacement logic to avoid prefix collisions (for example, replacing higher indexes first: `c11` -> `c0`).
