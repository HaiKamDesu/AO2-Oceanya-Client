# Oceanya Laboratories Client

### a.k.a. your AO chaos control panel

Welcome to **Oceanya Client**: the app for people who are tired of running Attorney Online like a circus act with 47 tabs, 12 panic clicks, and one emotional support alt-tab key.

This project has grown a lot, so this README is finally up to date and written for normal humans.

## What this app is

Think of it like an AO toolbox with **3 launch modes**:

1. **Attorney Online GM Multi-Client**  
   Run and control multiple client profiles in one place.
2. **AO Character Database Viewer**  
   Browse and manage your local character folders.
3. **AO Character File Creator**  
   Build or edit character folders with previews before you save.

So yes, this is no longer just "multi-client and pray."

## What you can do (without needing a computer science degree)

### GM Multi-Client mode
- Add/remove clients quickly.
- Rename each client so you know who is who.
- Send lines from a specific client using `ClientName: message`.
- Auto-reconnect if connection drops.
- Pick servers from a server picker (including favorites), not just one hardcoded place.
- Jump between clients with `Ctrl + number`.
- Use `Tab` to hop between IC/OOC text boxes faster.
- Open an area list and jump areas from inside the app.
- Right-click a client for extra actions (rename, pick puppet automatically/manually, reconnect).

### Character Database Viewer mode
- See your character folders in a browsable library.
- Search and filter them.
- Tag characters and filter by tags.
- Open `char.ini`, open readme files, and open the folder in Explorer.
- Run integrity checks and view results.
- Create, edit, duplicate, or delete character folders from the viewer.
- Double-click a character to open an emote/animation visualizer.

### Character File Creator mode
- Create a new character folder or edit an existing one.
- Preview animations/sounds while working.
- Organize files before applying changes.

## Important expectations

- This is a **control-room style AO client**, not a full courtroom viewport renderer.
- It is built for **Windows**.
- It uses **.NET 8**.

If you want full visual courtroom gameplay, keep a normal AO client around too.

## Quick start

1. Grab the latest release from the repo’s **Releases** tab.
2. Extract it.
3. Run the app.
4. Point it to your `config.ini` when asked.
5. Pick a launch mode and go cause reasonable amounts of chaos.

## Extra notes

- There is a fake loading screen with chaotic messages. It is dramatic on purpose.
- There is at least one secret/easter-egg style thing hidden in the app. I am not elaborating.
- Yes, things are still actively evolving.

## Repository layout

- `AO2-Client/` -> upstream AO2 reference code submodule (reference only)
- `AOBot-Testing/` -> bot/network logic used by this project
- `Common/` -> shared utility code
- `OceanyaClient/` -> main WPF desktop app
- `UnitTests/` -> tests

## Dev note

If you find differences between Oceanya and AO2 in terms of **base behavior**, that’s probably unintentional. Let me know. 

Got feature requests? Bug reports? Life advice? Toss 'em in. I might even do something about it maybe. 

Scorpio2#3602 on Discord if you speak english or french. 
Dredd#3414 if you speak russian. 

Neither of these people know that i'm putting them here, so it'll be funny and they'll put you in contact with me.

Have fun in your self-made chaos.

o7
