# Changelog

**English** · [Русский](CHANGELOG-RU.md)

The version is set in one place — `ExtendedBossesPlugin.Version` in
`src/ExtendedBossesPlugin.cs`.

## 0.8.12 — Moder: fewer drakes, abilities more often; the reward on the ground (not tested in game)

- **A threshold never fires twice when the boss changes owner.** Every client with the
  mod remembers which thresholds of a boss have fired; a client that takes the boss over
  (the owner died, HostOwner took it back) merges that memory into the boss data before
  checking thresholds. Before, a new owner with a moment-old copy could run a threshold
  again - in a group of 4 Moder put up far more than her 6 stalagmites. A fight reset is
  counted in the boss data, so a reset still starts the thresholds over.
- Every fired threshold is written to the log (`BepInEx/LogOutput.log`) without `Debug`:
  the boss, the threshold, the group size and the number of totems; a restored threshold
  memory is logged too.
- **Moder's stalagmites, adds and nests are placed on the ground, not in the sky.** The
  spawn point was measured from the boss, and a boss flying more than 12 m up failed every
  ground test - the stalagmites hung in the air at her height: invisible, out of reach,
  hatching drakes all fight long and holding the shield until its 90 s timeout. A boss
  more than 3 m above the ground now places everything around the ground under it.
- **Moder's ice flash is visible**: an ice ring (`fx_DvergerMage_Nova_ring`) plays where it
  lands; before, only the red circle showed and the hit itself was invisible.
- **Drakes under control.** Stalagmites hatch as before (a drake every 15 s, up to 2 alive
  per stalagmite), but all stalagmites together keep at most **2 drakes per player** alive.
  Besides, all fight long every 30 s 1-2 drakes fly in from afar (25-35 m), at most 2 of
  them alive at once, counted apart from the stalagmites (`13 Moder / DrakeFlightInterval`,
  `DrakeFlightMax`). The 20% wave brings 1 drake as a base (was 2).
- **Ice flash marks only near Moder**: players within 40 m of her; the message says what
  the red circle means ("an ice blast in a moment, step out of it").
- **Her abilities come more often.** Ice novas on marks every 15 s (was 20 s; new setting
  `13 Moder / MarkInterval`), the breath mark every 25 s (was 35 s), a 40–55 s break
  between phase cycles (was 50–70 s). The config is migrated: the cycle break of an
  existing file is reset to the new default once.
- **The reward lies on the ground under the boss.** Moder dies in the air, and the group
  reward was dropped where she hung - the gear could fall off the cliffs and be lost. A
  boss more than 3 m above the ground now drops it at ground level.

## 0.8.11 — the skull pile: owned by the fight, not cleaned up after (not tested in game)

- The 0.8.10 clean-up of items left on the broken pile's spot is removed: items are
  picked up instantly, so they would already be in an inventory. Instead the client
  running the fight keeps owning the pile, so its destruction is always handled by a client
  with the mod, where the drop is switched off - also when a player without the mod breaks
  it.

## 0.8.10 — after a group test (not tested in game)

- **Eikthyr's skull pile really drops nothing.** `skull_pile` is a build piece, and a
  destroyed piece gives its resources (the skulls) back - the drop table was never the
  source. Now, on every client with the mod, both a piece's resource drop and a drop table
  are skipped for the pile; and since the pile may belong to a player without the mod, the
  items it would give back are removed from its spot for 4 s after it breaks.
- **Nests, totems and adds appear on open ground**: no longer on top of a tree trunk or a
  pillar of the arena (the point is retried until nothing stands above the ground). The
  Elder's seed nests too.
- **A boss mid-fight without a target takes the nearest player** instead of idling and
  wandering off its arena (Bonemass walked away while the group fought lieutenants).

## 0.8.9 — Fix for 0.8.8 (not tested in game)

- The skull pile's drops are now switched off by emptying its drop table. 0.8.8 removed
  the drop component, whose subscription to the pile's destruction stayed and would fail
  when the pile was broken - possibly leaving it in the world.
- The boss tag of adds and nests is read with a pre-hashed key in the per-tick and
  per-second scans (no string work per object).

## 0.8.8 — Bonemass's slime, the Elder's shamans, rewards (not tested in game)

- **Bonemass**: slime comes every 45 s (was 25) and in smaller waves (base 1, was 2); the
  setting `12 Bonemass / SlimeInterval` is updated once in an existing config.
- **The Elder**: shamans heal 0.5% of max HP per second each (was 0.3) - its own
  setting `11 Elder / HealerPercentPerSecond`; Yagluth's shamans keep the common 0.3.
- **Eikthyr**: the skull pile drops nothing.
- **Highlight** (players with the mod): nests, totems and the slime crawling to Bonemass
  glow softly (`07 Client / HighlightObjects`).
- **Rewards, every boss**: coins x2; about a stack of each material of the biome; about
  half a stack of each material of the next biome, always (was a 15% chance); gear only of
  the next biome (the last boss: its own) and only for a group of 2+ (`05 Rewards /
  GearMinPlayers`), 2-4 players - 1 item, 5-7 - 2, 8 - 3. `NextBiomeChance` removed.

## 0.8.7

- Package page and README: how a fight goes, and every boss's phases and abilities in
  short. FIGHTS: Bonemass's 40% and 15% adds named correctly (writhans; a wraith and swamp
  bats). No code changes.

## 0.8.6

- Package page: HostOwner is a link to its Thunderstore page. No code changes.

## 0.8.5

- Package page: a gallery of animations and screenshots, and a section with the author's
  other mods (icons, one line each, links). No code changes.

## 0.8.4 — Publishing prep

- Documentation in English (README, CHANGELOG, FIGHTS, ROADMAP) with Russian copies
  (-RU.md); a real Thunderstore README; code and build-script comments in English.
- No gameplay changes.

## 0.8.3 — Balance after reviewing every boss (not tested in game)

- Phase cycles **do not start under the shield or the cocoon**; all mechanics together
  never cut damage to the boss below ×0.1 (`09 Raid / MinDamageFactor`).
- The shield **drops by itself after 90 s** (`ShieldMaxSeconds`), with no burn window —
  in case a nest cannot be broken with weapons.
- Softer healing: shamans 0.3%/s (was 0.5), Yagluth's regeneration 0.3%/s (was 0.6);
  Bonemass's slime — **at most 6% for the whole wave**, split evenly between the blobs
  (was 3% for each; `SlimeWaveHealPercent` instead of `SlimeHealPercent`).
- Eikthyr's lightning — 15 (was 30).
- The Queen's eggs follow the nest rule: 2 / 2 / 3, solo 1, +1 from 6 players (was "like
  adds", up to 28 eggs for 8 players).
- Solo: boss HP ×0.8 (`02 Scaling / SoloHealthMultiplier`), lieutenants ×0.6
  (`SoloLieutenantHealth`).
- Config migration (`01 General / ConfigVersion`): in an existing file the changed
  defaults (shaman healing, Eikthyr's lightning, Yagluth's regeneration) are updated once,
  every other value is kept.

## 0.8.2 — After the first test of The Elder

- **Only the nests from HP thresholds hold the shield** (85 / 70 / 55%). Before, the
  nests grown from seeds counted too, and they kept appearing — the shield never dropped.
  Now, once the threshold nests are broken — "the defence has fallen", burn window.
- **The Elder's seeds**: 5% chance (was 30%), at most once per 20 s, at most 2 seed nests
  at a time.
- **Crowd cap** around the boss: at most 8 monsters within 50 m solo, +3 for every
  further player (`09 Raid / AddsCapSolo`, `AddsCapPerPlayer`, `AddsCapRadius`). At the cap
  vanilla nests, totems, waves and slime release nobody; lieutenants, cocoon guards and
  roots always spawn.
- **Coins ×3** for every boss, **amber ×3** (Eikthyr, The Elder).
- **Messages** in the centre of the screen stay ×1.5 longer (`07 Client /
  MessageDurationMultiplier`, only for players with the mod).

## 0.8.1 — Config without a restart on a dedicated server (not tested in game)

- Saved `j1ga.extendedbosses.cfg` — the mod rereads it a second later (file watch) and
  on the server sends the new values to clients with the mod right away. Switch —
  `08 Sync / WatchConfigFile`. The mod's own saves (settings window) are not reread; the
  file is never rewritten on a reread.

## 0.8.0 — Postponed mechanics (not tested in game)

- **The Elder's seeds** (from 70%): where his projectile lands, a nest grows with a 30%
  chance (at most 4 standing nests).
- **"Wet" and "Tared" on Bonemass**: from 70% his hits make you wet, from 35% — tared
  (slow and flammable — and there are surtlings around). The effect is written into the
  hit itself on the boss's side; shared attack data is not changed.
- **Moder's breath mark** (from 45%): every 35 s she lands and for 8 s keeps the marked
  player as her target — her vanilla breath goes at them.
- **Yagluth's beam** (from 45%): every 30 s for 8 s keeps the marked player as the
  target — the beam follows them; plus "Tared" on his hits.
- **The Queen's cocoon** (from 45%): after each of her teleports (at most once per 30 s)
  seeker guards appear; while they live (up to 20 s) damage to her goes through as under
  the shield.
- **Fader's wall of fire** (from 45%): every 45 s — his vanilla wall of fire towards a
  random player.
- Fixation beats the threat table; the WeaponArts taunt beats everything.
- Self-check: status effects (`Wet`, `Tared`), hazard spawners.
- `eb status`: `COCOON`, `FIXATION`.

## 0.7.0 — Fader (not tested in game)

- **Fader** (`16 Fader`):
  - 85 / 70 / 55% — 1 / 2 / 3 charred spawner stones (`Spawner_CharredStone`,
    alternative `Spawner_CharredCross`), at 55% they hold the shield;
  - from 70% — his meteors (`spawn_fader_meteors`) around the marked player;
  - alternating at random: **molten armor** — hits closer than 6 m ×0.5 and burn the
    attacker (15 fire, at most once per second); **ash veil** — hits from farther than
    6 m ×0.25;
  - 45% — morgen, 30% — fallen valkyrie, 20% — Lord Reto, 10% — asksvins and lava blobs;
  - reward: coins, gemstones, flametal ore, molten cores; Ashlands gear, with a chance —
    frozen cores.

## 0.6.0 — The Queen (not tested in game)

- **The Queen** (`15 Queen`):
  - 85 / 70 / 55% — egg clutches (`SeekerEgg_alwayshatch`, 2 / 2 / 3, growing with the
    group like adds): they hatch into seekers by themselves; at 55% they hold the shield
    while any egg is neither broken nor hatched — a race to break them before they hatch;
  - from 70% — acid splashes (`SeekerQueen_spithit`, poison) under the marked players;
  - alternating at random: **bloodlust** — heals for 40% of the raw damage of her hits on
    players (before armor and block); **acid spikes** — every hit on her returns 10% of
    the damage to the attacker as poison (at least 5, at most once per second per player);
  - 45% — seeker brutes, 30% — gjall, 20% — ticks and seeker soldiers' young;
  - reward: coins, rubies, soft tissue, black cores, eitr; Mistlands gear, with a
    chance — Ashlands; with a chance — flametal ore and gemstones.
- Nests can be any networked object, not only destructibles (eggs), and grow with the
  group like adds.

## 0.5.0 — Yagluth (not tested in game)

- **Yagluth** (`14 Yagluth`):
  - 85 / 70 / 55% — 1 / 2 / 3 fuling totems (`goblin_totempole`), fulings and archers
    come out of them (up to 3 alive, every 12 s), at 55% they hold the shield;
  - from 70% — his own meteor shower (`spawn_meteors`) under the marked player, vanilla
    damage;
  - 55% — fuling shamans heal Yagluth while alive;
  - alternating at random: **regeneration** — 0.6% of max HP per second unless he was hit
    with frost in the last 3 s; **adaptation** (soft) — the damage type he was hit with
    most in the last seconds ×0.6, the others ×1.1; the announcement names the type (in
    each player's language);
  - 45% — brutes, 30% — an unbjorn, 20% — three kinds of skeletons, 15% — deathsquitos;
    10% — an echo of Moder (`Aspect_Moder`, half HP) — experimental,
    `EchoOfModer = false` by default;
  - reward: coins, silver necklaces, rubies, black metal; black-metal and linen gear, with
    a chance — Mistlands.
- Marks can use projectile spawners (`SpawnAbility`: meteors fall around the marked
  player, not around whoever is nearest the boss); mark damage with no setting is vanilla.
- Actions with an experiment flag (`Gate`), HP multiplier below 1.

## 0.4.0 — Moder (not tested in game)

- **Moder** (`13 Moder`):
  - 85 / 70 / 55% — 1 / 2 / 3 ice stalagmites (`caverock_ice_stalagmite`): drakes hatch
    from each (up to 2 alive, every 15 s), at 55% they hold the shield; all broken —
    stagger and a burn window;
  - 85% — a wolf pack, at night — ulvs;
  - from 70% — ice novas (`FenringIceNova_aoe`, frost slows) under the marked players;
  - alternating at random (30 s, cooldown 50–70 s, after the stagger): **ice armor** —
    hits from farther than 6 m ×0.25, only while Moder is on the ground (a window for
    melee); **ice spikes** — every hit returns 12% of the damage to the attacker as frost
    (at least 5, at most once per second);
  - 45% — cultists, 35% — a stone golem, 20% — a fenring and drakes;
  - reward: coins, rubies, silver necklaces, silver, obsidian, crystals; silver and
    fenris gear, with a chance — black metal.
- Waves can have a night composition (`NightPrefabs`); a totem can have its own fallback
  nest or none.

## 0.3.1 — Bonemass alternates two phases (not tested in game)

- A new cycle variant for Bonemass — **rot vapour** (`Rot`): hits closer than 5 m ×0.5,
  and every such hit poisons the attacker (12 poison, at most once per second per
  player — a vanilla hit, players without the mod are poisoned too); from afar the damage
  is full. Alternates with hardening at random, like the Elder's bark
  (`12 Bonemass / CycleVariant = Random | Harden | Rot`).
- Bonemass settings renamed: `Harden*` → `Cycle*` (duration, cooldown, start delay), plus
  `RotMeleeFactor`, `RotPoisonDamage`.

## 0.3.0 — Bonemass, a shared resistance cycle (not tested in game)

- **Bonemass** per table B3:
  - 85 / 70 / 55% — 1 / 2 / 3 bone piles (`Spawner_DraugrPile`), at 55% — the shield
    while the piles stand and a burn window after they are destroyed;
  - from 70% — leeches in the water around (no water — no leeches) and poison pools
    (`bonemass_aoe`) under the marked players;
  - **hardening** — blunt ×0.25, fire ×2 — 30 s after the stagger ends, then a
    50–70 s cooldown, until death; with no window — 60 s after 55%;
  - 45% — an abomination and **merging**: every 25 s a slime (`Blob`, `BlobElite`)
    appears far away and crawls to the boss; one that arrives is absorbed and heals him
    for 3% of max HP;
  - 40% — writhans (`Writhan`), 35% — surtlings, 25% — elite draugr, 15% — ghosts and
    bats;
  - reward: coins, rubies, pearls, scrap iron; iron and root gear, with a chance silver.
- **Resistance cycle** — a shared mechanism with variants: the Elder's living bark
  (`Sap` / `Back`) and Bonemass's hardening run on it.
- **Solo** (one player) — the positional variant never comes up: the Elder only uses
  `Sap`, whatever `BarkVariant` is set to.
- `eb status` — `HARDENED` / `BARK (sap)` / `BARK (back)`.

## 0.2.2 — Bark alternates at random

- `11 Elder / BarkVariant = Random` (the new default): before each bark the variant `Sap`
  or `Back` is picked again, 50/50. `Sap`, `Back`, `Auto` remain for a fixed variant.

## 0.2.1 — The Elder's living bark reworked (not tested in game)

- The bark is no longer tied to the nests' shield: a cycle "30 s of bark → cooldown
  50–70 s (random) → bark again" until death. The first bark comes right after the
  stagger (burn window) ends; with no window for 60 s after 55% the cycle starts by
  itself. The shield dropping during the bark interrupts it — the window gives full
  damage.
- Two variants, `11 Elder / BarkVariant`:
  - `Sap` (default) — "raw bark": fire ×0.25 (the usual weakness is gone), slash ×1.25,
    the rest ×0.25 — chop with axes;
  - `Back` — ×0.25 from the front, full damage only from behind (a 120° arc) — the tank
    keeps the Elder facing them;
  - `Auto` — `Back` for a group of 3+, otherwise `Sap`;
  - `Random` — see 0.2.2.
- All bark parameters are in `11 Elder`; `eb status` shows `BARK (sap)` / `BARK (back)`;
  `eb probe` — the creature's vanilla resistances.

## 0.2.0 — The Elder, languages, self-check (not tested in game)

- **Two languages.** All of the mod's text is in Russian and English. Players with the
  mod see messages in their game's language; to players without the mod the server sends
  a vanilla message in the `GuestLanguage` language (Russian / English / Both), and boss
  and monster names go as vanilla tokens (`$enemy_gdking`), translated by their own game.
  The server recognises players with the mod by a "hello" on connect. Setting
  descriptions and console replies are bilingual too.
- **Self-check** on world load and with `eb check`: every prefab of every boss — does it
  exist, is it networked, destructible, does it have an AoE and its radius, are the
  rewards items. The result — one line in the log, problems — as a list.
- **Shield** (A4): while the boss's nests stand, it takes ×0.2 damage (×0.1 on Hard).
  **Burn window** (A6): when the last nest falls — a stagger (vanilla `Stagger`) and
  ×1.5 damage for 10 s.
- **Resistance shift** (A9): "Living bark" — pierce ×0.25, fire ×2 while the shield holds.
- **Healers** (A5): shamans heal the boss for 0.5% of max HP per second each (at most
  two) while alive and within 40 m.
- **Roots** (A8): instead of a hit — a ring of vanilla `TentaRoot` around the marked
  player, living 15 s.
- **Threat** (A10): the boss attacks whoever took the most HP off it, among players within
  a 25 m leash; outside the radius threat halves every 3 s; the target changes only with a
  10% lead and at most once per 3.5 s; the WeaponArts taunt overrides it.
- The Elder per table B2: nests 1/2/3 at 85/70/55%, roots from 70%, shield and bark at
  55%, a troll (a second one with 3+ players), a bear, an elite with shamans, skeletons
  with ghosts; reward — bronze and troll hide, with a chance iron.
- Temporary adds (lifetime in the ZDO), add roles.

## 0.1.0 — Skeleton and Eikthyr (not tested in game)

- The fight skeleton on the boss's owner: phases by HP threshold, the fight state in the
  boss's ZDO (a phase bitmask, totem slots) — a phase never repeats after a rejoin or an
  owner change.
- Building blocks: an add wave, a lieutenant (stars + HP multiplier in the ZDO), a totem
  (a destructible vanilla object the code spawns monsters from), a vanilla nest, marks
  ("spread out": announcement → an AoE under the player 3 s later), a charge.
- Adds and the boss share one `m_group` so they do not fight each other; adds hunt
  players.
- Group scaling: the boss's effective HP `1 + 0.5·(N−1)` up to 8 players instead of the
  vanilla `0.3` up to 5; the number of adds, stars, nests — from N.
- Profiles `Light / Raid / Hard`, a `Vanilla / Mod` mode and a profile per boss, mechanic
  switches — all without a rejoin; synced from the server.
- A reward on top of the vanilla drop: coins, amber, the next biome's ore with a chance,
  upgraded gear (quality 2–3) — 1 item per 2.5 players.
- Adds and totems are cleaned up on the boss's death and on a switch to `Vanilla`; the
  server removes "orphans" after a restart.
- Fight reset — optional, off by default.
- For players with the mod — a circle of the mark's radius on the ground.
- Console: `eb status | phase <n> | reset | probe <prefab> | players <n>`.
- Eikthyr: 80% — boars and necks; 60% — a charge; 50% — a skull pile with Meadows
  skeletons; 30% — lightning on marks; 15% — a 2★ boar leader with HP ×3.
