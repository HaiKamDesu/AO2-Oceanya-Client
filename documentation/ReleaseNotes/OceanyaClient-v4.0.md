## Oceanya Client (v4.0)

***The Hivemind Update***

`v4.0` is what happens when I get an idea, realize it is unnecessary, stupid, and absolutely possible, and then decide that means it has to exist.

So yes, the main new feature is me looking at the Attorney Online community's traditional "just send the updated asset pack in Discord again bro" work we've been doing for years, then looking at dredd's idea of syncing through a drive folder which worked out great (but had people being too lazy to do it and also it wasnt too repeatable/generic/required too much setup on players), and then deciding "if you've invited me to your hivemind, why shouldn't i take control of it?" and upgrading it to my heart's content.

Thanks for the inspiration, Dredd.

Now, here we go:

### The Big New Thing: The Oceanyan File Hivemind

Non-technical explanation:

- Pick a shared Google Drive folder. That is now The Folder.
- Oceanya can connect that shared folder to a normal local AO asset folder on your PC.
- The host can set up the connection once, export it, and hand the connection file to other people so they do not all have to perform Google Cloud ritual sacrifice on every machine.
- Everyone still signs into their own Google account normally.
- After that, people can pull updates from the shared folder, publish local changes back to it, or let a hidden little background gremlin keep things synced automatically.
- There are even desktop toast popups for important sync events, because apparently I made a tray-icon hivemind daemon now.

What this means in normal person terms:

- Less "who has the newest version of the assets?"
- Less "use this pack_final_REAL_final2.zip"
- Less DM archaeology through six months of link corpses
- More everybody using the same folder brain at the same time like a proper synchronized cult

Basically, if your group shares characters, evidence, music, backgrounds, or any other cursed AO folder content on a regular basis, The Oceanyan File Hivemind is my attempt to make that process stop feeling like community-maintained suffering.

So by the end, you'll just have magic folder that is the same for everyone and AO can read it automatically.

### Actual Useful Changes

- Fixed some `WaitForm` nonsense, so loading/progress windows behave less like they were assembled in a sleep-deprived fugue state.
- Fixed simultaneous effects breaking IC messages. (thx gedge for noticing this)
- Asset refresh got giga optimized and should be noticeably faster, especially when your folder situation has escalated beyond reason.

### Honest Translation Of The Useful Changes

- `WaitForm` should now stop being weird when the app is busy doing things.
- Using multiple effects at once should no longer decide your message formatting deserves the death penalty.
- Asset refresh got a serious optimization pass as far as speed, but not as far as processing power. If your PC explodes, that is between you, your asset hoard, and God. Not me. Should (probably) be fine though.

### Internal / Nerd Stuff

- The solution name finally got cleaned up, because that had been annoying me.
- There was a lot of hardening around the new sync flow, background agent, connection handling, and asset-refresh path.
- There is now literally an `OceanyaHivemindAgent.exe`, which sounds like a fake thing I made up as a bit (which it was at one point), but no, that is a real file now.

### Known-ish Reality

- The Hivemind is brand new, not "quietly battle-tested for six months" new.
- If you find behavior differences versus normal AO base behavior, tell me, because that is probably not intentional.
- If you break the Hivemind in a creative way, that still counts as QA.
- If you use deletion mirroring carelessly and vaporize your own folder, I will feel bad for you, but not surprised.

---

This whole feature exists because I had the thought "if I can, I must," which is usually not a sentence spoken by stable people.

Anyway, the end result is that Oceanya can now do shared remote asset-folder nonsense with a straight face, and the old AO tradition of manually shuffling packs around like courtroom raccoons can maybe calm down a little. Or you're all lazy and nobody will use this, just like all the rest of things i make.

Anyway here's the nerd shit in case anyone needs it:
**Full Changelog**: https://github.com/HaiKamDesu/AO2-Oceanya-Client/compare/v3.1...v4.0
