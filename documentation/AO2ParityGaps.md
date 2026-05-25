# AO2 Parity Gaps — Oceanya Client vs AO2 Reference Client

**Generated:** 2026-05-14
**Branch:** release/6-2

This document catalogs every user-facing feature present in the AO2 reference client (AO2-Client/ Qt app + tsuserver3/tsuserverCC) that is absent or incomplete in OceanyaClient. For each gap:

- **User description** — what the feature does in plain language
- **Current state** — what Oceanya does or doesn't do today
- **Technical spec** — packets, files, and implementation notes for a future agent

---

## Table of Contents

1. [Character Pairing UI](#1-character-pairing-ui)
2. [Evidence System — Full Management](#2-evidence-system--full-management)
3. [Judge Controls Panel — HP Bars and Verdicts](#3-judge-controls-panel--hp-bars-and-verdicts)
4. [Timer System (TI# packet)](#4-timer-system-ti-packet)
5. [Spectator Mode](#5-spectator-mode)
6. [Typing Indicator](#6-typing-indicator)
7. [Custom Blip Name per Message](#7-custom-blip-name-per-message)
8. [Mute / Ignore System](#8-mute--ignore-system)
9. [Mod Call (ZZ# packet)](#9-mod-call-zz-packet)
10. [Notepad / In-Client Notes](#10-notepad--in-client-notes)
11. [Case Announcement System (CASEA#/SETCASE#)](#11-case-announcement-system-caseasetcase)
12. [Player List / Presence (PR#/PU# packets)](#12-player-list--presence-prpu-packets)
13. [Demo Recording and Playback](#13-demo-recording-and-playback)
14. [Subtheme Packet (ST#)](#14-subtheme-packet-st)
15. [Judge State Control Packet (JD#)](#15-judge-state-control-packet-jd)
16. [Authentication State Packet (AUTH#)](#16-authentication-state-packet-auth)
17. [Jukebox / Music Voting Mode](#17-jukebox--music-voting-mode)
18. [Server-Driven Position Dropdown (SD#)](#18-server-driven-position-dropdown-sd)
19. [WTCE / RT# Sending (Testimony Triggers)](#19-wtce--rt-sending-testimony-triggers)
20. [Callwords — AO2-Standard Audio Alert](#20-callwords--ao2-standard-audio-alert)

---

## 1. Character Pairing UI

### User Description

Two players can appear on-screen at the same time, standing side by side. One player uses a "Pair" button to select which other character they want to appear next to, set how far left or right each character stands, and choose who appears in front of whom.

### Current State

The viewport can **render incoming pairs** from other players (the `PairCharacterImage` layer and `RenderPairCharacter` in `AO2ViewportControl.xaml.cs` are fully working). Oceanya now has a working **Pairing Studio** opened from the IC controls beside the offset button. It refreshes current-area players through an internal `/getarea` OOC command, hides that internal refresh from the visible OOC log, prefers current-area players over the full server roster, falls back to the server character roster when parsing fails, highlights other internal Oceanya clients first, previews the pair in a live viewport, adjusts this client's outgoing offset, keeps partner offset preview-only, chooses layer order, clears pairing, and persists pair state per GM profile.

Implementation paths:
- `OceanyaClient/Components/Forms/CharacterPairingStudioWindow.cs`
- `OceanyaClient/Components/ICMessageSettings.xaml(.cs)` `btnPairingStudio`
- `AOBot-Testing/AO2Parser.cs` `/getarea` parser for current-area player candidates
- `AOBot-Testing/Agents/AOClient.cs` `PairTargetCharId`, `PairTargetCharacterName`, `PairLayerOrder`
- `Common/SaveFile.cs` `GmMultiClientSnapshotClient` pair fields

Remaining improvement area: add a richer partner-readiness workflow based on live echoed IC state. Vanilla AO2/tsuserver still requires both clients to select each other and send IC from the same position before the pair appears in server echoes.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/courtroom.h` — `ui_pair_button`, `ui_pair_list`, `ui_pair_offset_spinbox`, `ui_pair_vert_offset_spinbox`, `ui_pair_order_dropdown`
- `AO2-Client/src/courtroom.cpp` — sends these values in the `MS#` packet fields `OTHER_CHARID`, `OTHER_OFFSET`, `OTHER_FLIP`, `SELF_OFFSET`

**Relevant MS# packet fields (already defined in `AOBot-Testing/Structures/ICMessage.cs`):**
- `OtherCharId` (int, -1 = no pair)
- `OtherCharIdRaw` (string — encodes pair ordering as `charId^order` when the server supports effects)
- `OtherOffset` (int, horizontal pixel offset for the pair character)
- `OtherOffsetVertical` (int, vertical offset — Y_OFFSET feature)
- `OtherFlip` (bool)
- `SelfOffset` (int, horizontal offset for your own character)
- `SelfOffsetVertical` (int)

**Implemented send-side behavior:**
1. Pairing Studio lists current-area players from supported `/getarea` output such as newline AO2 reports and single-line `$ASG` reports. If parsing fails, it falls back to AO2's sorted server character roster, with no OOC/showname subtitles and without treating CharsCheck "taken" state as unselectable.
2. The selected pair data is stored per-client in the GM snapshot (`GMMultiClientSnapshot`) through `PairTargetCharId`, `PairTargetCharacterName`, `PairLayerOrder`, and `SelfOffset`.
3. `AOClient.SendICMessage()` fills outgoing AO2-compatible legacy `MS#` pair fields, including placeholder `OtherName`, `OtherEmote`, `OtherOffset`, and `OtherFlip`, from saved pair state. The server supplies meaningful values for those fields in echoed legacy `MS#` packets after mutual pairing is confirmed.

**Key constraint:** pairing requires `CCCC_IC_SUPPORT` in the server feature list (`AOClient.ServerFeatures`). Check before enabling the UI.

---

## 2. Evidence System — Full Management

### User Description

Players can maintain a list of evidence items (case files, weapons, documents, etc.) — each with a name, description, and image. Any player can add new items, edit existing ones, or delete them. When sending an IC message, a player can select one piece of evidence to "present" — this shows the evidence image as an overlay in the courtroom viewport.

### Current State

Oceanya **receives** the evidence list from the server (`LE#` packet parsed in `AOClient.cs`) and stores names and images in-memory. The `EvidenceID` field in outgoing `MS#` packets is **hardcoded to `"0"`** (`ICMessageSettings.xaml.cs:777`), meaning evidence is never presented. There is no UI window for viewing, adding, editing, or deleting evidence, and the `PE#`/`EE#`/`DE#` packets are never sent.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/evidence.cpp` — evidence panel UI
- `AO2-Client/src/courtroom.h` — `ui_evidence_list`, `ui_evidence_add`, `ui_evidence_edit`, `ui_evidence_delete`, `ui_evidence_present`, `ui_evidence_transfer`, `ui_evidence_save`, `ui_evidence_load`

**Packets (none of these are currently sent by Oceanya):**
```
PE#name&description&image_filename#%   — Add evidence
EE#evidence_id&name&description&image_filename#%  — Edit evidence
DE#evidence_id#%   — Delete evidence
```

**Packets received (already parsed):**
```
LE#name&description&image_filename&...#%  — Full evidence list broadcast
```

Evidence images are resolved from the character asset directory: `<AO install>/base/evidence/<image_filename>`.

**What needs to be built:**
1. An evidence management window or panel with:
   - A grid of evidence buttons (with thumbnail images) — similar to AO2's 6×3 paged grid
   - "Add", "Edit", "Delete" buttons that send `PE#`, `EE#`, `DE#` packets via `AOClient`
   - An image picker that browses `<AO install>/base/evidence/` for the image filename
2. In `ICMessageSettings`, an evidence selector dropdown/popup that lets the user pick an evidence ID (1-based index into `AOClient.AvailableEvidenceNames`) before sending. The selected ID is placed into `EvidenceID` instead of `"0"`.
3. New methods on `AOClient`: `SendAddEvidence(string name, string desc, string image)`, `SendEditEvidence(int id, string name, string desc, string image)`, `SendDeleteEvidence(int id)`.
4. In the viewport, the evidence image overlay (`ShowEvidenceOverlay` in `AO2ViewportControl.xaml.cs`) is already implemented — it just needs a non-zero `EvidenceID` to trigger.

---

## 3. Judge Controls Panel — HP Bars and Verdicts

### User Description

When playing as the Judge (or when the server grants judge powers), a special panel appears with controls for the two "penalty bars" — one for the defense side and one for the prosecution side. Each bar has + and − buttons to increase or decrease the bar level (0–10). There are also "Guilty" and "Not Guilty" verdict buttons that trigger special animations.

### Current State

Neither HP penalty bars nor Guilty/Not Guilty buttons exist anywhere in OceanyaClient. The `HP#` packet is not handled in `AOClient.cs`, the `RT#`-based verdict types are already rendered in the viewport but cannot be triggered by the local user.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/courtroom.h` — `ui_defense_bar`, `ui_prosecution_bar`, `ui_def_plus/minus`, `ui_pro_plus/minus`, `ui_guilty`, `ui_not_guilty`
- `AO2-Client/src/courtroom.cpp` — `set_hp_bar()`, HP# packet assembly

**Packets:**
```
HP#1&new_value#%   — Set defense bar (type=1, value 0-10)
HP#2&new_value#%   — Set prosecution bar (type=2, value 0-10)
```
Receiving `HP#` from the server broadcasts the updated bar value to all clients.

Guilty/Not Guilty use `RT#`:
```
RT#judgeruling&0#%  — Not Guilty
RT#judgeruling&1#%  — Guilty
```
`RT#` receive handling is already in `AOClient.cs:778-786` and rendered in `AO2ViewportControl.xaml.cs` via `HandleRtPacket` → `ShowWtceOverlay`.

**What needs to be built:**
1. A collapsible "Judge Controls" panel in `MainWindow.xaml` (visible when the current character side is `"jud"` or when `JD#` packet sets judge state to forced-visible — see gap #15):
   - Defense bar image (10 states, 0–10) with `−` and `+` buttons
   - Prosecution bar image (10 states, 0–10) with `−` and `+` buttons
   - "Guilty" button → sends `RT#judgeruling&1#%` via `AOClient`
   - "Not Guilty" button → sends `RT#judgeruling&0#%` via `AOClient`
2. On `AOClient`: `SendHp(int type, int value)` that formats and sends `HP#type&value#%`; and `SendRt(string type, int variant = 0)` (partially exists for testimony triggers — see gap #19).
3. On receiving `HP#`, update `AOClient` state and fire an `OnHpChanged` event so the viewport can display bar levels. Bar image assets live at `<theme>/courtroom/<config_ini_key>_bar_<value>.png` (e.g., `defence_bar_5.png`).

---

## 4. Timer System (TI# packet)

### User Description

Up to 5 independent countdown or count-up timers can be shown in the courtroom. Moderators or case managers start, pause, and hide these timers. Players see them ticking in real time. They're used for time-limited arguments or breaks.

### Current State

The `TI#` packet is not handled anywhere in `AOClient.cs`. No timer UI exists.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/aoclocklabel.h/cpp` — clock label widget
- `AO2-Client/src/courtroom.h` — `ui_clock[5]` array

**Packet:**
```
TI#timer_id&type&milliseconds#%
  timer_id: 0-4 (which of the 5 timers)
  type: 0=start/resume, 1=pause, 2=show, 3=hide
  milliseconds: current value (remaining or elapsed)
```

**What needs to be built:**
1. `AOClient`: Parse `TI#` packets; fire `OnTimerReceived(int timerId, int type, long ms)` event.
2. `AO2ViewportControl` or `MainWindow`: A row of up to 5 clock labels, initially hidden. Show/hide and start/stop ticking based on `type`. Timers count down from `ms` when `type=0`; pause at current value when `type=1`. Display format: `MM:SS.mmm`.
3. Timer accuracy: Apply half the measured network latency as a correction offset on receipt (AO2 reference does `latency/2`).

---

## 5. Spectator Mode

### User Description

A player can join the room as a pure spectator — they watch everything happening but cannot send IC messages, play music, or modify evidence. There is a dedicated "Spectate" button on the character selection screen.

### Current State

There is no spectator button or spectator mode. Users must select a character to join. In multi-client mode, all clients are active participants.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/charselect.cpp` — spectator button sends `CC#0&-1&<hdid>#%`
- `AO2-Client/src/courtroom.h` — `ui_spectator` button

**Packet:**
```
CC#client_id&-1&<hdid>#%   — Char ID -1 means spectator
```

**What needs to be built:**
1. A "Spectate" button in the character selection UI (`CharacterSelectorWindow`) that sends `CC#` with `char_id = -1`.
2. `AOClient`: Track `IsSpectating` (bool) when `char_id == -1`. 
3. When `IsSpectating == true`, disable the IC send button, music play, evidence add/edit/delete, and judge controls.
4. On receiving `CharsCheck#` response, spectator clients remain valid — just suppress active controls.

---

## 6. Typing Indicator

### User Description

When a player starts typing an IC message, a small animation or indicator appears in the courtroom to show other players that someone is composing a message. This prevents confusion when the chat log is quiet for a moment.

### Current State

Not implemented. The `TA#` packet is never sent or handled.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/courtroom.cpp` — sends `TA#1#%` on key-press in the IC message field, `TA#0#%` after message is sent or field cleared
- Receives `TA#<char_id>&<state>#%` (or similar) to show an animation

**Packet variants (server-dependent):**
```
TA#1#%   — client started typing (sent by local user)
TA#0#%   — client stopped typing
```
Received: `TA#char_id&state#%` (in some server versions the broadcast includes the char_id)

**What needs to be built:**
1. `AOClient`: `SendTypingState(bool isTyping)` that sends `TA#1#%` or `TA#0#%`.
2. In `MainWindow`, subscribe to `PreviewKeyDown` on the IC text input and call `SendTypingState(true)`; subscribe to `TextChanged` going empty or on message send to call `SendTypingState(false)`.
3. `AOClient`: Parse incoming `TA#` packets and fire `OnTypingReceived(int charId, bool isTyping)`.
4. In `AO2ViewportControl` or the IC log, show a "…" or character-specific typing animation when `OnTypingReceived` fires with `isTyping=true`.

---

## 7. Custom Blip Name per Message

### User Description

Each IC message can have its own custom blip sound — the clicking noise you hear when character dialogue text scrolls. In AO2, you can pick a specific blip sound file for each individual message rather than always using the character's default blip.

### Current State

The `BlipName` field exists in `ICMessage.cs` and is included in the packet serializer (as part of the `CUSTOM_BLIPS` server feature block). However, there is no UI element in `ICMessageSettings` to let the user pick a blip per message. The field is always sent as empty/default.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/courtroom.h` — `ui_custom_blips` dropdown/field
- `AO2-Client/src/courtroom.cpp` — fills `BLIPNAME` field in `MS#`

**Packet field (already serialized when CUSTOM_BLIPS is in server feature list):**
```
MS#...&<blipname>&<slide>#%
  blipname: filename under <AO install>/base/sounds/blips/ (no extension), empty = use character default
```

**What needs to be built:**
1. A blip dropdown in `ICMessageSettings.xaml` (small, below or next to the SFX dropdown) populated from the blip catalog (`BlipCatalog.cs` — already exists).
2. Wire the selected blip name to `ICMessage.BlipName` in `ICMessageSettings.BuildICMessage()`.
3. Add the selected blip to the GM snapshot so it persists per-client (`GMMultiClientSnapshot` in `SaveFile.cs`).

---

## 8. Mute / Ignore System

### User Description

A player can mute a specific character so all of that character's IC messages are hidden locally. The muted player's text never appears in the chat log for the muting player. This is purely a local client-side filter. Separately, the server can mute a player (preventing them from sending IC messages).

### Current State

There is no mute list, no mute button, and no incoming mute-state packet handling. Server-sent mute (`MU#`) and unmute (`UM#`) packets are not parsed in `AOClient.cs`.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/courtroom.cpp` — `set_mute()` toggles a per-char_id mute list; incoming `MS#` is filtered against it
- Server packets: `MU#<char_id>#%` (server mutes client), `UM#<char_id>#%` (unmute)

**What needs to be built:**

**Local mute (client-side filter):**
1. `SaveFile.Data` or in-session list: `MutedCharIds` (HashSet<int>).
2. On receiving `MS#` in `AOClient`, check if the sender's `CharId` is in the mute list; if so, suppress `OnICMessageReceived`.
3. A right-click context menu on IC log messages or a dedicated mute panel where the user can toggle mute per character.

**Server-side mute (receive-only indicator):**
1. `AOClient`: Parse `MU#` — set a flag `IsServerMuted = true`, fire `OnMuteStateChanged`.
2. `AOClient`: Parse `UM#` — set `IsServerMuted = false`, fire `OnMuteStateChanged`.
3. In `MainWindow`, subscribe to `OnMuteStateChanged`; disable the IC send button and show a "you are muted" indicator when muted.

---

## 9. Mod Call (ZZ# packet)

### User Description

A "Call Moderator" button lets a player send a distress signal to online moderators, optionally including a short reason. The call shows up in moderators' chat so they can respond. There is a cooldown (30 seconds) to prevent spam.

### Current State

The `ZZ#` packet is never sent. There is no mod call button in the UI.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/courtroom.cpp` — `callmod_clicked()` sends `ZZ#<reason>#%`, enforces 30s cooldown
- `tsuserver3/server/network/aoprotocol.py` — broadcasts to moderators with timestamp and client info

**Packet:**
```
ZZ#<reason>#%   — reason can be empty string
```
Incoming `ZZ#` to non-mods: not typically broadcast; mods receive it in their OOC.

**What needs to be built:**
1. A "Call Mod" button in `MainWindow.xaml` (small, near OOC input or in a separate panel).
2. Clicking opens a small dialog asking for an optional reason (100 char limit, or blank).
3. `AOClient.SendModCall(string reason)` — formats and sends `ZZ#reason#%`.
4. Track last mod-call timestamp; disable the button for 30s after each call (visual countdown or greyed-out state).
5. Spectators cannot call mods (check `IsSpectating`).

---

## 10. Notepad / In-Client Notes

### User Description

An in-client notepad lets players jot down notes during a session — clues, character names, timelines. The notes are saved locally and persist between sessions. This is a purely local feature (no packet involved).

### Current State

No notepad feature exists.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/courtroom.h` — `ui_note_area` (text edit), `ui_note_shown` (toggle button)
- Notes are saved to a `.txt` file alongside the client settings

**What needs to be built:**
1. A collapsible notes panel in `MainWindow` or a floating `NotesWindow` (a simple resizable text editor).
2. Notes saved to `SaveFile.Data` (a new `string Notes` field) or to a dedicated `notes.txt` file in `%APPDATA%/OceanyaClient/`.
3. A "Notes" toggle button to show/hide the panel.

---

## 11. Case Announcement System (CASEA#/SETCASE#)

### User Description

A case manager can broadcast a "case announcement" to the whole server: a short title and a list of which roles are needed (defense, prosecution, judge, jury, stenographer). Players who previously registered interest in those roles receive a notification. There is a 60-second cooldown.

Players can also register their role preferences so the server knows to notify them.

### Current State

Neither `CASEA#` nor `SETCASE#` are handled or sent anywhere in the codebase.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/courtroom.cpp` — case announcement dialog, `CASEA#` assembly
- `tsuserver3/server/network/aoprotocol.py` — `net_cmd_setcase()`, `net_cmd_casea()`

**Packets:**
```
CASEA#case_title&need_def&need_pro&need_judge&need_jury&need_steno#%
  (sent by case manager to broadcast a case)

SETCASE#cases&will_cm&will_def&will_pro&will_judge&will_jury&will_steno#%
  (sent by client to declare role interests — "cases" is a pipe-separated list of case names)
```
Incoming `CASEA#` is a notification to interested players; typically displayed as an OOC message.

**What needs to be built:**
1. On connection, send `SETCASE#` with user's preferences (new `CasePreferences` struct in `SaveFile.Data`).
2. A "Case Announcement" dialog (case manager only, `CASEA#` send) with:
   - Case title input
   - Checkboxes for each role needed
   - 60-second cooldown enforced client-side
3. Parse incoming `CASEA#` and display as a special OOC notification with role info.

---

## 12. Player List / Presence (PR#/PU# packets)

### User Description

A live player list shows everyone connected to the server with their current character and area. The list updates in real time as players join, leave, or switch characters.

### Current State

`PR#` and `PU#` packets are not handled in `AOClient.cs`. OceanyaClient infers player presence indirectly through area rosters embedded in OOC messages, but has no dedicated player list panel.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/widgets/playerlistwidget.h/cpp` — player list widget
- `tsuserverCC` — broadcasts `PR#` on join/leave, `PU#` on character or area change

**Packets:**
```
PR#player_id&register_type#%   — register_type: ADD or REMOVE
PU#player_id&data_type&data#%  — data_type: char, area, showname, etc.
```

**What needs to be built:**
1. `AOClient`: Parse `PR#` and `PU#`; maintain `OnlinePlayers` dictionary keyed by player_id with character, area, and showname.
2. Fire `OnPlayerListChanged` event.
3. A "Players" panel or window in `MainWindow` that shows the list; refresh on `OnPlayerListChanged`.

---

## 13. Demo Recording and Playback

### User Description

The client can silently record everything that happens in a session to a `.demo` file. This file can later be played back to replay the session exactly — useful for sharing interesting cases or reviewing what happened.

### Current State

No demo recording or playback exists.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/aoapplication.cpp` — `append_to_demofile()` writes each received packet with a timestamp
- Demo format: one packet per line, prefixed with millisecond timestamp
- Playback: packets are re-injected through the packet handler at the recorded timestamps

**What needs to be built:**
1. A setting in `SaveFile.Data` to enable demo recording.
2. In `AOClient`, an optional `StreamWriter` that appends each incoming packet to a `.demo` file under `<AppData>/OceanyaClient/demos/`.
3. A demo player utility (could be a separate window) that reads the file, replays packets through a simulated `AOClient` at their recorded intervals, and renders the viewport.

---

## 14. Subtheme Packet (ST#)

### User Description

A server can tell the client to switch to a specific UI subtheme (a visual skin variant within the main theme). This lets servers customize how the client looks without the user having to change settings manually. The change can also trigger a theme reload.

### Current State

The `ST#` packet is not parsed in `AOClient.cs`. Incoming subtheme changes from the server are silently dropped.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/packet_distribution.cpp` — `ST#` handler sets options subtheme and calls `reload_theme()`

**Packet:**
```
ST#subtheme_name&reload_flag#%
  reload_flag: 0 or 1 (1 = force theme reload)
```

**What needs to be built:**
1. `AOClient`: Parse `ST#`; fire `OnSubthemeChanged(string subtheme, bool reload)`.
2. In `MainWindow` or the theme system: On `OnSubthemeChanged`, update the current subtheme setting. If OceanyaClient has a theme/skin layer, apply it; otherwise at minimum save the subtheme name for use when building asset paths.

---

## 15. Judge State Control Packet (JD#)

### User Description

A server or moderator can forcibly show or hide the judge control buttons (penalty bars, guilty/not guilty) for a specific client — overriding the position-based default. This is used when a server wants a non-judge position to use judge controls, or wants to hide them from the actual judge.

### Current State

`JD#` is not parsed. Judge controls are not implemented (see gap #3).

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/packet_distribution.cpp` — `JD#` handler sets `judge_state`

**Packet:**
```
JD#state#%
  state: -1 = position-dependent (default), 0 = force hide, 1 = force show
```

**What needs to be built:**
1. `AOClient`: Parse `JD#`; fire `OnJudgeStateChanged(int state)`.
2. In `MainWindow`, subscribe and apply the judge panel visibility accordingly (once gap #3 is implemented).

---

## 16. Authentication State Packet (AUTH#)

### User Description

When a server has account-based authentication, the client receives a packet indicating whether the player is logged in. Some server features (like moderator tools or account-linked evidence) are only available when authenticated.

### Current State

`AUTH#` is not parsed in `AOClient.cs`.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/packet_distribution.cpp` — `AUTH#` handler updates authentication status

**Packet:**
```
AUTH#status#%
  status: 0 = not authenticated, 1 = authenticated
```

**What needs to be built:**
1. `AOClient`: Parse `AUTH#`; set `IsAuthenticated` bool; fire `OnAuthChanged(bool authenticated)`.
2. Conditionally enable features gated on authentication (e.g., judge controls in some server configs, mod commands).

---

## 17. Jukebox / Music Voting Mode

### User Description

In jukebox mode (a server setting), players cannot play music directly. Instead, playing a song adds it to a queue. The server picks the next song from the queue based on votes, then broadcasts it to everyone. Players vote for songs rather than forcing them.

### Current State

Not implemented. Oceanya always sends `MC#` for direct playback; jukebox mode is not acknowledged.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/aomusicplayer.h/cpp` — jukebox queue handling
- Server signals jukebox mode through area status (ARUP) or a dedicated packet

**What needs to be built:**
1. Detect jukebox mode from area status or server config in the area info.
2. When in jukebox mode, change the music play button behavior: show a "Vote" confirmation instead of immediate playback.
3. Display the current jukebox queue in the music panel.

---

## 18. Server-Driven Position Dropdown (SD#)

### User Description

A server can send a custom list of available positions for a specific area (e.g., only "defense" and "witness" are available in a particular room). The client's position selector updates to only show those positions.

### Current State

The `SD#` packet is not handled in `AOClient.cs`. The `PositionDropdown` in `ICMessageSettings` is populated from a hard-coded or character-INI-based list, not from server-sent positions.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/packet_distribution.cpp` — `SD#` handler populates position dropdown

**Packet:**
```
SD#pos1&pos2&pos3...#%
  pipe or ampersand-separated list of available position strings
```

**What needs to be built:**
1. `AOClient`: Parse `SD#`; store `AvailablePositions` list; fire `OnAvailablePositionsChanged(IReadOnlyList<string>)`.
2. `ICMessageSettings.PositionDropdown`: Subscribe to `OnAvailablePositionsChanged` and filter the dropdown to only show server-allowed positions.

---

## 19. WTCE / RT# Sending (Testimony Triggers)

### User Description

Case managers can trigger the large "WITNESS TESTIMONY" and "CROSS-EXAMINATION" animated splash screens that appear over the courtroom. There are also controls to navigate testimony statements (next, previous, read). Currently, these overlays can only be seen when another client sends them — Oceanya users cannot trigger them themselves.

### Current State

`RT#` is **received** and rendered (handled in `AOClient.cs:778-786`, displayed via `AO2ViewportControl.HandleRtPacket`). There is no UI or method to **send** `RT#` packets.

### Technical Spec

**AO2 reference implementation:**
- `AO2-Client/src/courtroom.cpp` — `/testify` and `/examine` IC chat commands send `RT#testimony1#%` / `RT#testimony2#%`
- Also sends `RT#judgeruling&0#%` / `RT#judgeruling&1#%` for verdicts (part of gap #3)

**Packet:**
```
RT#type#%
  type: "testimony1" = Witness Testimony, "testimony2" = Cross Examination
RT#judgeruling&0#%  — Not Guilty
RT#judgeruling&1#%  — Guilty
```

**What needs to be built:**
1. `AOClient.SendRt(string type, int? variant = null)` — formats and sends `RT#type#%` or `RT#type&variant#%`.
2. A small panel (or right-click context menu on viewport) with buttons: "Witness Testimony", "Cross Examination", "Not Guilty", "Guilty". Only show when the user's current area role permits it (CMs and judges).
3. `SendRt` should be gated in the same way as other moderator actions (server may reject from non-CMs regardless, but the UI should reflect the expected permission level).

---

## 20. Callwords — AO2-Standard Audio Alert

### User Description

AO2 has a "callwords" feature where the user can set specific words that, when mentioned in the IC or OOC chat, cause the client to play a sound and/or flash the window to alert the user.

### Current State

Oceanya has a **highly extended callword/alert system** (`CallwordAudioNotifier.cs`, `CallwordRuleEditorWindow`) that is strictly **better** than the AO2 original — it supports trigger types beyond simple keyword matching (character-speaks, emote-used, showname-speaks, etc.), per-rule audio files, volume control, and whole-word matching. **This gap is informational only — no action needed.**

The only difference is that the AO2-standard settings file format (`callwords.ini`) is not importable. If import compatibility with AO2's settings ever matters, a migration import tool could be added.

---

## Summary Table

| # | Gap | Severity | Packets | State |
|---|-----|----------|---------|-------|
| 1 | Character Pairing UI | High | MS# (send) | Protocol ready, no UI |
| 2 | Evidence Full Management | High | PE#, EE#, DE# (send); LE# (recv) | Receive-only, EvidenceID hardcoded 0 |
| 3 | Judge Controls / HP Bars / Verdicts | High | HP# (send/recv), RT# (send) | Not implemented |
| 4 | Timer System | Medium | TI# (recv) | Not implemented |
| 5 | Spectator Mode | Medium | CC# char_id=-1 | Not implemented |
| 6 | Typing Indicator | Medium | TA# (send/recv) | Not implemented |
| 7 | Custom Blip Name per Message | Low | MS# BlipName field | Field exists, no UI |
| 8 | Mute / Ignore | Medium | MU#, UM# (recv) | Not implemented |
| 9 | Mod Call | Medium | ZZ# (send) | Not implemented |
| 10 | Notepad | Low | (none) | Not implemented |
| 11 | Case Announcement | Low | CASEA#, SETCASE# | Not implemented |
| 12 | Player List / Presence | Low | PR#, PU# (recv) | Not implemented |
| 13 | Demo Recording | Low | (none — all packets) | Not implemented |
| 14 | Subtheme Packet | Low | ST# (recv) | Not implemented |
| 15 | Judge State Packet | Low | JD# (recv) | Not implemented (depends on #3) |
| 16 | Authentication State | Low | AUTH# (recv) | Not implemented |
| 17 | Jukebox Mode | Low | MC# variant | Not implemented |
| 18 | Server Position Dropdown | Low | SD# (recv) | Not implemented |
| 19 | WTCE / RT# Sending | Medium | RT# (send) | Receive-only |
| 20 | Callwords AO2 Import | Info | (none) | Oceanya system is superset |
