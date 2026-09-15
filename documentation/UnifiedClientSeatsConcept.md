# Unified Client Seats (concept — not implemented)

Replacing the "single internal client" / "multiple internal clients" launch toggle with one mode that
creates connections automatically, only when they are actually needed.

**Status: investigated, designed, deliberately NOT pursued.** Recorded so the analysis is not repeated.

## Why the toggle exists today

`MainWindow.useSingleInternalClient` (from `SaveData.UseSingleInternalClient`) branches in ~60 places, but
they collapse to five real differences:

| | single | multi |
|---|---|---|
| Connections | one shared `singleInternalClient`; entries in `clients` are lightweight state objects | one per client |
| Network ops | `GetTargetClientForNetwork` returns the shared connection; `ApplyProfileToSingleInternalClient` copies profile state onto it before every send | the profile *is* the connection |
| Server identity | one player, one character slot, one area | N players, N slots, N areas |
| Logs / viewport | `ResolveLogClientKey` maps every profile to the shared client; one unfiltered viewport | per-client log and pane |
| Sends | serialized through `singleClientSendGate` | independent |

Everything else a user might think needs a second client — shownames, emotes, colours, per-message
positions, iniswaps — is a per-packet field and needs no extra connection.

## What genuinely requires its own connection

A connection is **one seat at the table**. A second seat is only required for simultaneous presence:

1. Being in two areas at once.
2. Holding two character slots at once (a slot is per-connection).
3. Pairing two of your own characters (needs two char IDs present in the same area).
4. Appearing as two users in `/getarea`, user counts, and to other players.
5. Two independent IC streams.
6. Per-connection server state — CM status, area-scoped permissions, mod actions.

## The finding that reshapes the naive design

**Merging is not free, and the most common workflow is the expensive one.**

`AOClient.SendICMessage` calls `AlignIniPuppetWithCurrentCharacterIfAvailableAsync`, which performs a
**CC#/PV# round trip whenever `iniPuppetID` does not match the current character**. With several clients
merged onto one seat in the same room, *every alternation between speakers costs a server round trip before
the message is sent*. The method already carries a warning about this, and
[performance-notes](nav/performance-notes.md#message-freeze) traces "press Enter, message appears seconds
later" to it.

Secondary cost: the server broadcasts character changes, so the slot visibly flickers between characters for
everyone else in the room, and rapid CC# can trip server anti-spam.

So a rule of "same area ⇒ merge" would make a GM voicing three NPCs in one room **slower and uglier** than
the current multi mode. The trigger has to be presence, not location.

## The refined model

> A client gets its own seat when it needs to be present simultaneously. Otherwise clients time-share a seat.

**Split eagerly** (immediate, no prompt) when a client:
- moves to a different area,
- is paired with another of your clients,
- becomes CM or gains area-scoped state,
- speaks while another merged client spoke within the last few seconds — the anti-thrash rule that avoids
  the round-trip problem, by giving two actively alternating voices their own seats.

**Merge lazily**, never mid-conversation: only when a client has been idle past a threshold, is not paired,
is not CM, and shares an area with another. Merging is an optimisation and should happen invisibly during
quiet moments.

### Hard constraints

- **Splitting costs a connection**: roughly a second of handshake, a visible "user joined" to the room, and
  it is subject to the server reconnection rate limiting documented in
  [snapshot restore warning](nav/startup-updates-and-tests.md#snapshot-restore-warning--unfocused-second-window-on-launch).
  Splits must be queued and staggered, never bursty.
- **Splitting can fail**: the target slot may be taken, or the server may cap connections per IP. The
  fallback must be "stay merged and say why", not an error.

## INI puppet policy for automatically created seats

In AO2 the **slot you occupy** is independent of the **character you appear as**. An auto-created seat must
occupy some free slot, and choosing randomly means taking a slot someone else wanted. AO players keep mains
and secondaries, so the choice is not arbitrary to them.

Proposed setting — *New client INI puppet*:
- **Ask me** (default) — prompt.
- **Most-used available** — walk `SaveData.FrequentlyUsedIniPuppets` in order, take the first free slot.
- **My puppet list** — a user-ordered list of mains/secondaries, tried in order.
- **Match the character** — take the character's own slot if free, otherwise fall back to one of the above.

Regardless of policy: when a split has to take a different slot than requested, say so inline rather than
silently.

## Grouping visualisation

Two-level list — area headers, with the **seat** as the grouping inside them:

```
▸ Area 1 — Courtroom                    [1 seat]
    ⟦ Kam · Dredd · Scorpio ⟧            one bracket = one shared seat
▸ Area 2 — Lounge                       [1 seat]
    ⟦ Veles ⟧
▸ Area 3 — Basement                     [2 seats]
    ⟦ Athena ⟧  ⟦ Phoenix ⟧  🔗 paired
```

- The bracket is the seat, not the client; a shared seat is one bracket around several names.
- Every split carries a reason badge (`🔗 paired`, `👑 CM`, `💬 active`) so it is never mysterious.
- Splits animate out of their bracket, which teaches the model without a tutorial.
- Manual override is required: pin a client to its own seat, or force a merge, for cases the heuristics miss.

## Open questions before any implementation

1. Does the target server broadcast character changes to other players? If tsuserverCC broadcasts CC#, the
   flicker cost is real and the anti-thrash rule becomes mandatory.
2. What is the per-IP connection cap on the servers in use? It bounds how freely seats can be split.
3. **Is merging worth doing at all?** If its only real benefit is a tidier `/getarea`, the round-trip cost
   may not justify it, and the simpler end state is "every client is its own seat, the user just never picks
   a mode again". This is the biggest fork in the design and was left undecided.
