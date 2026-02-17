# Oceanya Laboratories Client

### *a.k.a. The Multi-Attorney-Online Client*
(Last version: 1.1.0)

Welcome to the chaos-slaying, GM-saving, INI-wrangling tool you never knew you needed but now can’t live without: **The Oceanya Client (tm)**.

## What is this?

This lovely abomination of a program is built for **Attorney Online** GMs who are tired of juggling seven trillion clients, only to lose track of which INI said what, where, and why. So, we made this. One window to rule them all. Multiple clients, same screen, no alt-tab hell.

In short? It’s an **AO2-compatible multi-client manager** that lets you run several AO clients inside a single app, each one easily accessible, controllable, and nameable. Yes, you can name your clients. Yes, you can make them talk with a command like `Edgeworth: Put a shirt on.`

## Core Features

- **Multiple Clients:** Add as many as your RAM can handle. Hit "Connect Client" and boom—another client. Toss 'em in, toss 'em out.
- **IC/OOC Quickswap:** Press TAB while typing in IC/OOC to instantly swap textboxes. No more clicking around.
- **Focus QoL:** Various items have been modified to not lose focus of IC textbox, meaning it's easier to type than ever.
- **Internet Disconnect Protection:** If your internet flakes, clients auto-reconnect like champs. ~~Mods can’t kick what never leaves.~~
- **INI Dropdown Overload:** Actually shows *all* of your INIs. With icons. Yes, those icons you forgot existed.
- **Keybinds:** CTRL + Number to select clients. Because mouse movement is for the weak.
- **Dialogue Command:** Type `NameOfClient: (text)` and that client will say it. Magic.

## How to Install

1. **Go to the [Releases page](https://github.com/HaiKamDesu/AO2-Oceanya-Bot/releases)** on this GitHub repository.
2. Download the latest `.rar` file from the most recent release.
3. Extract the `.rar` wherever you want.
4. Run the executable.

> Heads up: Windows will probably throw a fit the first time you run it, because the app isn’t registered as a known publisher (yet). Just click "More info" and then "Run anyway"—you know the drill.

> If you don’t have **.NET 8**, the app will let you know. If it opens fine, you’re good. If not, Windows will yell at you and send you to download it.

### Wait, what’s .NET 8?

Great question, imaginary reader. .NET 8 is the framework this client runs on. Think of it like the invisible scaffolding that holds everything up so the app doesn’t instantly fall apart when you double-click it. It’s made by Microsoft, it’s free, and no, downloading it won’t make your PC explode. You probably already have it—unless your idea of Windows updates is hitting “remind me tomorrow” for three years straight.

Once that’s all done, congrats—you’re ready to dive into the glorious madness that is Oceanya Client.

## How to Use This Thing

So you’ve opened the client—awesome. You’re now staring at three fields and thinking, “What fresh hell is this?” Don’t worry, here’s what they mean (in not-too-nerdy terms):

- **Config.ini Path**
  
  This tells Oceanya Client where your Attorney Online's config.ini is. It’s how the client knows where your stuff lives—like asset folders and mount paths. TL;DR: It grabs your regular AO client’s assets so you don’t have to copy/paste anything manually. Work smarter, not harder.

- **Connection Path**
  
  This is where your clients land when they connect. Type something like `Basement`, and that’s where they’ll go—assuming your server has an area named exactly that. It’s case-sensitive and supports subareas via `/`.
  
  Example: `Basement/testing` will try to connect to the Basement area, then dive into testing. Type carefully, or your client might just sit there awkwardly in the void.

- **Refresh Assets Checkbox**
  
  AKA the “pls update my stuff” toggle. If this is checked, Oceanya Client will update your config, INIs, backgrounds, etc. before launching. Takes a moment the first time, but after that? Smooth and speedy.

Once you’ve filled everything in, hit **Save Configuration**. Boom. You’re officially in Oceanya Client™.

### You’re In—Now What?

At this point, your screen’s probably empty and sad. That’s normal. Time to bring in some clients.

Click the little `+` button to the left of the **IC Log** to add a client. You’ll be asked to name it. Call it whatever you want—Edgeworth, GoblinBoy42, anything. Just remember it, because it’ll come up once.

Now that you’ve got a client, let’s walk through the essentials:

### Core Controls:

- **Connect/Disconnect a Client:**
  Click `+` (connect) or `-` (disconnect) next to the IC Log. Simple stuff.

- **Right-Click for Advanced Options:**
  Right-click your client’s icon to unleash the secret menu. Here’s what’s inside:

  - **Rename Client:**
    This renames the client (not to be confused with the showname). This is mainly used with the dialogue feature, where you type something like `Larry: Objection!` and it sends that message from Larry's client. Rename freely.

  - **Select INI Puppet:**
    This sets your “INI Puppet”—the thing that shows up when someone uses `/getarea` in OOC. You can select one manually or let it auto-pick the first one it finds. Just make sure you spell the name right.

  - **Reconnect:**
    For when the client decides to stop working for no reason. The ol’ “turn it off and on again” fix lives here. You probably won’t need it. But if you do... it’s here waiting.

Now you should be able to do everything your AO2 client could do, minus the viewport visuals. If something doesn’t work... well, blame the dev. Probably deserved.

## Based On AO2

Oceanya Client mimics AO2’s layout and features as closely as possible, so it’s intuitive if you’re coming from regular Attorney Online. This includes:

- INI handling
- Emotes
- IC/OOC chat
- SFX & color dropdowns
- Effects & status toggles
- INI selection menus

BUT—and this is a big but—it’s still a solo dev project, so some features from AO2 didn’t make the cut (yet):

- Pairing
- Area viewing (you *can* still use `/area`)
- Y/X offset
- Judge controls
- Evidence
- Viewport (no emote/effect visuals for now)

Think of this more like a **GM console**, not a full visual client. If you need visuals, run a regular AO2 client on the side.

Currently, Oceanya Client only works with **Chill and Dices** server, because that’s where the dev's RPG group lives. Multi-server support *might* happen someday.

## Final Notes

This is a ground-up project. That means:

- Everything can be added.
- Everything can be broken.
- Maybe one day selecting Edgeworth launches DOOM. Who knows.

If you find differences between Oceanya and AO2 in terms of **base behavior**, that’s probably unintentional. Let me know.

Got feature requests? Bug reports? Life advice? Toss 'em in. I might even do something about it maybe. Scorpio2#3602 on Discord.

## Development

### Branch Structure
- `main`: Main development branch with the latest features
- `release/X-Y`: Release branches (e.g., `release/1-1` for version 1.1)
- `test/name`: Test branches for specific features or experiments
