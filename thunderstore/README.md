# ExtendedBosses

Raid-style boss fights for a group, from Eikthyr to Fader: HP phases, waves of adds,
destructible nests, biome mini-bosses as lieutenants, shields that drop when the nests
fall, burn windows, marks, ground hazards, resistance shifts and a threat table — built
only from vanilla prefabs, animations and RPCs, so players **without the mod** see and
feel the same fight.

- **Profiles** `Light` (bosses with reinforcements), `Raid` (default) and `Hard`, globally
  or per boss; any boss can be set back to `Vanilla`. Everything switches without a
  rejoin.
- **Group scaling** for 1–8 players: boss HP, adds, nests, stars and marks grow with the
  group; solo stays doable.
- **Reward** on top of the vanilla drop: coins, valuables, next-biome resources and
  upgraded gear for bigger groups.
- **English and Russian** messages; players without the mod get them in the language set
  by `GuestLanguage`.
- The server's settings apply to every client with the mod; config edits are picked up
  without a restart.

Install on the **host**; guests do not need it. With the HostOwner mod (`Bosses = true`)
the host keeps owning the boss whoever hits it. On a dedicated server every player who may
own the boss needs the mod.

Early version: every boss is implemented, in-game testing is in progress — the numbers are
starting values and all adjustable.

Fights by boss, settings and the changelog: https://github.com/TBSjiga/ExtendedBosses

Inspired by HardBosses (Nexus #877).

*Designed and written with the help of an AI assistant (Claude by Anthropic); the design
decisions, verification against the game code and in-game testing are the author's.*
