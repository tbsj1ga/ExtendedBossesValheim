# ExtendedBosses

**English** · [Русский](README-RU.md)

A Valheim mod: bosses as **raid fights** — HP phases, waves of adds, spawner nests,
biome mini-bosses as lieutenants, shields, burn windows, marks, ground hazards,
resistance shifts, a threat table and a group reward. Everything is built from vanilla
prefabs, animations and RPCs, so players **with and without the mod** see and feel the
same fight.

The idea grew out of `HardBosses` (Nexus #877, long unmaintained): the same "HP
threshold → vanilla spawn" principle, with a much wider set of mechanics, tuned for a
group of 1–8 players.

**Status: 0.8.4 — every boss from Eikthyr to Fader is implemented; only partly tested
in game.** The fight with each boss, by phase, profile and group size — `FIGHTS.md`;
plans — `ROADMAP.md`; history — `CHANGELOG.md`.

## Principle

The boss's logic runs on the **owner client** of its ZDO. The mod on the owner watches
the HP and the fight time, creates vanilla creatures and objects, weakens or strengthens
hits and writes the fight state into the boss's ZDO. Everyone else sees the result
through the normal sync: positions, animations, HP, spawned prefabs, hits, messages in
the centre of the screen and in chat.

For the mechanics to work whoever hits the boss, the boss, its adds and nests should be
owned by a host with the mod — the separate [HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/) mod does that (`Bosses = true` in
`j1ga.hostowner.cfg`). Guests do not need the mod.

## Mechanics in short

| Building block | Example |
|---|---|
| Add waves at HP thresholds | The Elder at 45% — a troll, at 30% — a bear |
| Spawner nests | The Elder 85/70/55% — 1/2/3 greydwarf nests; Bonemass — bone piles |
| Lieutenants | an abomination for Bonemass, a golem for Moder, Lord Reto for Fader |
| Shield / healing from adds | the boss is nearly invulnerable while the nests or shamans live |
| Burn window | all nests broken — the boss is staggered and takes ×1.5 damage |
| Marks, zones | "spread out", fixation on a player, meteors, a wall of fire |
| Resistance shift | Bonemass "hardens" — blunt is useless, burn him |
| Threat, group scaling, reward | the boss attacks whoever angers it most; solo is doable, 6–8 is not easy |
| Settings | every boss has a Vanilla / Mod mode and a Light / Raid / Hard profile — switchable without a rejoin |

## Screenshots

| | |
|---|---|
| ![Eikthyr marks a player for lightning — spread out!](https://raw.githubusercontent.com/tbsj1ga/ExtendedBossesValheim/main/docs/media/mark-lightning.webp) | ![The Elder: roots under the marked player, a seed grows a new nest](https://raw.githubusercontent.com/tbsj1ga/ExtendedBossesValheim/main/docs/media/roots-and-seeds.webp) |
| Eikthyr marks a player for lightning — spread out! | The Elder: roots under the marked player, a seed grows a new nest |

![Phase announcements — every player sees them, with or without the mod](https://raw.githubusercontent.com/tbsj1ga/ExtendedBossesValheim/main/docs/media/phase-messages.png)

*Phase announcements — every player sees them, with or without the mod*

## Compatibility

Tested with **Valheim 1.0.16** (network version 40), **BepInEx 5.4.23.5** (BepInExPack_Valheim 5.4.2351).

## Who needs it

| Who | What |
|---|---|
| Host | required — the fight runs on the boss's owner; with [HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/) (`Bosses = true`) that is always the host |
| Dedicated server | recommended for settings and messages; every player who may own the boss also needs the mod |
| Players without the mod | see and feel the same fight, messages in the `GuestLanguage` language |

## Known conflicts

- Other boss overhauls (HardBosses and similar) — do not run two at once.
- Mods that scale boss health or stars by player count or level (e.g. Creature Level and Loot Control) stack with this mod's group scaling; lower `02 Scaling` or theirs.

## Bugs and feedback

GitHub Issues: https://github.com/tbsj1ga/ExtendedBossesValheim/issues — please attach `BepInEx/LogOutput.log`.

## Installation

Through r2modman / Thunderstore, or put `build/ExtendedBosses.dll` into
`BepInEx\plugins\ExtendedBosses\` (or `build.ps1 -Install`). Install it on the **host**;
guests do not need it. To keep the host owning the boss whoever hits it, set
`Bosses = true` in `j1ga.hostowner.cfg` ([HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/) mod).

## Settings

`BepInEx\config\j1ga.extendedbosses.cfg`, or the BepInEx ConfigurationManager window
(F1) — everything applies at once, without a rejoin:

| Section | What |
|---|---|
| 01 General | on/off, profile `Light / Raid / Hard`, where announcements go, the language for players without the mod |
| 02 Scaling | HP per player (0.5), player cap (8), multipliers, stars from N |
| 03 Mechanics | switches for waves, nests, lieutenants, marks, abilities, shield, healers, resistances, threat; cleanup |
| 04 Marks | hit delay, interval, damage multiplier |
| 05 Rewards | reward: item quality, items per player, next-biome chance |
| 06 Reset | fight reset (off by default) |
| 07 Client | mark circles on the ground (local only, not synced) |
| 08 Sync | the server hands its settings to modded clients; config file watch |
| 09 Raid | shield, burn window, healing, threat — the numbers |
| 10 Eikthyr | `Mode = Mod / Vanilla`, profile, totem, lightning, charge |
| 11 Elder | `Mode`, profile, nest, root creature, living bark |
| 12 Bonemass | `Mode`, profile, bone pile, poison pool, hardening, slime |
| 13 Moder | `Mode`, profile, ice stalagmite, ice nova, ice armor / spikes |
| 14 Yagluth | `Mode`, profile, totem, meteors, regeneration / adaptation, echo of Moder (experimental) |
| 15 Queen | `Mode`, profile, egg, acid, bloodlust / acid spikes |
| 16 Fader | `Mode`, profile, spawner stone, meteors, molten armor / ash veil |

Console (F5, needs `devcommands` or a server): `eb status` — bosses nearby, phases,
group, damage multiplier, shield/window/resistance; `eb check` — prefab self-check;
`eb phase <n>` — force a phase; `eb reset`; `eb probe <prefab>` — what a prefab is made
of; `eb players <n>` — fake the group size for testing (0 — count for real).

## Languages

All text is in Russian and English. A player with the mod sees messages in their game's
language. To a player without the mod the server can only send a vanilla message with
ready text, so their language is set by `01 General / GuestLanguage` (`Russian`,
`English` or `Both`); boss and monster names still show in their game's language.
Setting descriptions and console replies are bilingual.

## Dedicated server

- The mod on the server: its config is the only source of settings for every client with
  the mod (`08 Sync`), and it sends the phase messages to everyone in their language.
  File edits are picked up without a restart (`WatchConfigFile`).
- The fight runs on the boss's owner client (on a dedicated server that is a player near
  the boss), so for now every player who may end up owning it needs the mod. [HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/)
  does not work on a dedicated server.
- The `eb …` console commands run on the owning player's game, not on the server.

## Building

```
powershell -ExecutionPolicy Bypass -File .\build.ps1            # build and check references
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install   # ... and copy into plugins
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Package   # ... and make the Thunderstore zip
```

The compiler is `csc.exe` from the .NET Framework (C# 5); references come from the game
folder and the r2modman profile's `BepInEx\core`; the paths are at the top of
`build.ps1`, `check-refs.ps1` and `src\ExtendedBosses.csproj`. After the build
`check-refs.ps1` checks every reference, reflection target and Harmony patch target
against the installed game.

| What | Where |
|---|---|
| Sources | `src\ExtendedBossesPlugin*.cs` — one `partial class`, one file per area |
| Build output | `build\ExtendedBosses.dll` |
| Mod version (one place) | the `Version` constant in `src\ExtendedBossesPlugin.cs` |
| Thunderstore package | `thunderstore\` (manifest, icon 256×256, README) → `build\ExtendedBosses-<version>.zip` |
| License | `LICENSE`, MIT |

## More mods by j1gA

| | Mod |
|---|---|
| [![LivingMap](https://raw.githubusercontent.com/tbsj1ga/LivingMapValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/) | **[LivingMap](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/)** — Your buildings, roads and cleared forest on the map and the minimap. |
| [![StationSpeed](https://raw.githubusercontent.com/tbsj1ga/StationSpeedValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/StationSpeed/) | **[StationSpeed](https://thunderstore.io/c/valheim/p/j1gA/StationSpeed/)** — Faster smelters, kilns, fermenters and crops — consistent even for players without the mod. |
| [![WeaponArts](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/) | **[WeaponArts](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/)** — One key, one active ability per weapon: stagger, taunt, heals, berserk, crits. |
| [![HostOwner](https://raw.githubusercontent.com/tbsj1ga/HostOwnerValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/) | **[HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/)** — The host takes ownership of stations and bosses near it, so its mods work for everyone. |

## AI assistance

This mod was designed and written with the help of an AI assistant (Claude by
Anthropic). Prefab names and game hooks were checked against the game's asset manifest
and IL; the design decisions, in-game testing and releases are the author's.
