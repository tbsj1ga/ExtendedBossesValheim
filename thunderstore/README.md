# ExtendedBosses

Raid-style boss fights for a group, from Eikthyr to Fader: HP phases, waves of adds,
destructible nests, biome mini-bosses as lieutenants, shields that drop when the nests
fall, burn windows, marks, ground hazards, resistance shifts and a threat table — built
only from vanilla prefabs, animations and RPCs, so players **without the mod** see and
feel the same fight.

| | |
|---|---|
| ![Eikthyr marks a player for lightning — spread out!](https://raw.githubusercontent.com/tbsj1ga/ExtendedBossesValheim/main/docs/media/mark-lightning.webp) | ![The Elder: roots under the marked player, a seed grows a new nest](https://raw.githubusercontent.com/tbsj1ga/ExtendedBossesValheim/main/docs/media/roots-and-seeds.webp) |
| Eikthyr marks a player for lightning — spread out! | The Elder: roots under the marked player, a seed grows a new nest |

![Phase announcements — every player sees them, with or without the mod](https://raw.githubusercontent.com/tbsj1ga/ExtendedBossesValheim/main/docs/media/phase-messages.png)

*Phase announcements — every player sees them, with or without the mod*

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

## Compatibility

Tested with **Valheim 1.0.16** (network version 40), **BepInEx 5.4.23.5** (BepInExPack_Valheim 5.4.2351).

## Who needs it

| Who | What |
|---|---|
| Host | required — the fight runs on the boss's owner; with HostOwner (`Bosses = true`) that is always the host |
| Dedicated server | recommended for settings and messages; every player who may own the boss also needs the mod |
| Players without the mod | see and feel the same fight, messages in the `GuestLanguage` language |

## Known conflicts

- Other boss overhauls (HardBosses and similar) — do not run two at once.
- Mods that scale boss health or stars by player count or level (e.g. Creature Level and Loot Control) stack with this mod's group scaling; lower `02 Scaling` or theirs.

## Bugs and feedback

GitHub Issues: https://github.com/tbsj1ga/ExtendedBossesValheim/issues — please attach `BepInEx/LogOutput.log`.

## More mods by j1gA

| | Mod |
|---|---|
| [![LivingMap](https://raw.githubusercontent.com/tbsj1ga/LivingMapValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/) | **[LivingMap](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/)** — Your buildings, roads and cleared forest on the map and the minimap. |
| [![StationSpeed](https://raw.githubusercontent.com/tbsj1ga/StationSpeedValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/StationSpeed/) | **[StationSpeed](https://thunderstore.io/c/valheim/p/j1gA/StationSpeed/)** — Faster smelters, kilns, fermenters and crops — consistent even for players without the mod. |
| [![WeaponArts](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/) | **[WeaponArts](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/)** — One key, one active ability per weapon: stagger, taunt, heals, berserk, crits. |
| [![HostOwner](https://raw.githubusercontent.com/tbsj1ga/HostOwnerValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/) | **[HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/)** — The host takes ownership of stations and bosses near it, so its mods work for everyone. |

