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

Install on the **host**; guests do not need it. With the [HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/) mod (`Bosses = true`)
the host keeps owning the boss whoever hits it. On a dedicated server every player who may
own the boss needs the mod.

Early version: every boss is implemented, in-game testing is in progress — the numbers are
starting values and all adjustable.

## How a fight goes

The boss's own attacks and AI stay vanilla — everything below is added on top. Described
for the default `Raid` profile.

- **Nests.** As the fight goes on, the boss raises destructible spawners of its biome —
  greydwarf nests, bone piles, eggs…
- **Shield.** While the nests stand, the boss takes only a fraction of the damage.
  **Break them all** → the boss is staggered and takes **×1.5 damage for 10 s**. Ignore
  them and the shield crumbles on its own after a while — without the bonus.
- **Phases.** About once a minute the boss enters a special state for 30 s — armor
  against one damage type, regeneration, damage reflection… The message in the centre of
  the screen tells you how to answer it.
- **Marks.** The boss names a player and 3 s later strikes where they stand. Step away
  from your friends.
- **Lieutenants.** Mini-bosses of the biome join the fight, along with waves of adds.
- **Reward.** On top of the vanilla drop: coins, valuables, next-biome resources and
  upgraded gear for bigger groups.

`Light` keeps the adds and nests but drops shields, phases and marks; `Hard` is `Raid`,
denser and tougher. All the numbers:
[FIGHTS.md](https://github.com/tbsj1ga/ExtendedBossesValheim/blob/main/FIGHTS.md).

## The bosses

### Eikthyr — the tutorial
No shield and no phases: every mechanic once, gently.
- **Charge** — every 20 s he dashes at double speed at a player who keeps their distance.
- **Skull pile** — raises Meadows skeletons until you break it.
- **Lightning mark** — the marked player is struck 3 s later.
- **Reinforcements:** boars and necks, and a herd leader — a 2★ boar with triple health.

### The Elder
- **Greydwarf nests** hold his shield.
- **Roots** — roots burst out around the marked player.
- **Seeds** — his projectiles sometimes take root and grow a new nest.
- **Living bark** (phase): *Sap* — only slashing hits (axes, swords) get through;
  *Back* — armored in front, full damage from behind: the tank keeps him facing away from
  the group.
- **Shamans heal him** — kill them first.
- **Reinforcements:** a troll, a bear, greydwarf elites, a skeleton and a ghost.

### Bonemass
- **Bone piles** hold his shield.
- **Leeches** come out of the water nearby; **poison pools** under marked players; his
  hits leave you **Wet**, later **Tared**.
- **Merging** — blobs crawl towards him from afar, and each one that reaches him heals
  him. Intercept them.
- **Phases:** *Hardening* — blunt barely hurts, fire does double: burn him. *Rot
  vapour* — close hits are halved and poison the attacker: fight from range.
- **Reinforcements:** an abomination, writhans, surtlings, elite draugr, a wraith and
  swamp bats.

### Moder
- **Ice stalagmites** hatch drakes and hold her shield.
- **Ice nova** under marked players — frost and slow.
- **Breath mark** — she lands and fixes on the marked player for a few seconds.
- **Phases:** *Ice armor* — while she is on the ground, ranged hits do a quarter: get
  into melee. *Ice spikes* — every hit reflects part of its damage back as frost.
- **Reinforcements:** a wolf pack (ulvs at night), cultists, a stone golem, a fenring.

### Yagluth
- **Fuling totems** call fulings and archers and hold his shield.
- **Fuling shamans heal him.**
- **Meteor mark** — his own meteors fall around the marked player.
- **Beam** — he fixes on the marked player for a few seconds; his hits leave you
  **Tared**.
- **Phases:** *Regeneration* — heals over time; any frost hit stops it. *Adaptation* —
  resists the damage type you use most and takes more from the others; the message names
  the type.
- **Reinforcements:** fuling brutes, an unbjorn, three skeletons, deathsquitos; an echo of
  Moder (optional, off by default).

### The Queen
- **Egg clutches** hatch into seekers; the shield holds while any egg is whole — break
  them before they hatch.
- **Acid mark** — acid splashes under the marked player.
- **Cocoon** — after she teleports, a seeker and a brood guard her; while they live she
  is shielded.
- **Phases:** *Bloodlust* — she heals from her hits on players: block, don't get hit.
  *Acid spikes* — every hit reflects part of its damage back as poison.
- **Reinforcements:** seeker brutes, a gjall, ticks and young seekers.

### Fader
- **Charred spawner stones** hold his shield.
- **Meteor mark** — meteors fall around the marked player.
- **Wall of fire** — raised towards a random player again and again.
- **Phases:** *Molten armor* — close hits are halved and burn the attacker: fight from
  range. *Ash veil* — ranged hits do a quarter: everyone into melee.
- **Reinforcements:** a morgen, a fallen valkyrie, Lord Reto, an asksvin and a lava blob.

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

## More mods by j1gA

| | Mod |
|---|---|
| [![LivingMap](https://raw.githubusercontent.com/tbsj1ga/LivingMapValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/) | **[LivingMap](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/)** — Your buildings, roads and cleared forest on the map and the minimap. |
| [![StationSpeed](https://raw.githubusercontent.com/tbsj1ga/StationSpeedValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/StationSpeed/) | **[StationSpeed](https://thunderstore.io/c/valheim/p/j1gA/StationSpeed/)** — Faster smelters, kilns, fermenters and crops — consistent even for players without the mod. |
| [![WeaponArts](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/) | **[WeaponArts](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/)** — One key, one active ability per weapon: stagger, taunt, heals, berserk, crits. |
| [![HostOwner](https://raw.githubusercontent.com/tbsj1ga/HostOwnerValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/) | **[HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/)** — The host takes ownership of stations and bosses near it, so its mods work for everyone. |

