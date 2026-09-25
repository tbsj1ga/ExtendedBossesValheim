# ExtendedBosses — boss fights (version 0.8.4)

**English** · [Русский](FIGHTS-RU.md)

How the fight with each boss works **now**, from the code. Not everything has been
tested in game: the numbers are starting values, all adjustable without a restart.

The boss's vanilla fight (AI, attacks, animations) **is not changed**: everything below
is added on top.

---

## General rules

### Difficulty profiles

The profile is `01 General / Profile` (for all) or `1x <Boss> / Profile` (for one).
`Mode = Vanilla` on a boss — the mod does not touch it at all.

| What | Light | Raid (default) | Hard |
|---|---|---|---|
| Add waves, lieutenants | yes | yes | yes, adds ×1.5 and **+1★** for ordinary adds |
| Nests / totems / eggs | yes — plain spawners | yes | yes, totems spawn 1.33× as often |
| The Elder's seeds | yes | yes | yes |
| Shield from nests and burn window | **no** | shield ×0.2 damage | shield **×0.1** damage |
| Phase cycles (bark, hardening…) | **no** | yes | yes |
| Healers heal, slime heals Bonemass | **no** (shamans exist but do not heal; slime does not crawl) | yes | yes |
| Marks, fixations, wall of fire | **no** | yes | 1.33× as often |
| Eikthyr's charge, on-hit effects (Wet, Tared), the Queen's cocoon | **no** | yes | yes |
| Threat table | **no** (vanilla target choice) | yes | yes |
| Boss effective HP | as Raid | see below | ×1.25 of Raid |
| Reward | the same | the same | the same |

`Light` is "bosses with reinforcements": adds, nests and mini-bosses without the raid
mechanics. `Hard` is `Raid`, denser and tougher.

Every mechanic can also be switched off on its own in `03 Mechanics`.

### Group size (N — living players within 100 m of the boss, at most 8)

**Boss effective HP** (how many times longer to kill than solo in vanilla):

| N | 1 | 2 | 3 | 4 | 5 | 6 | 8 |
|---|---|---|---|---|---|---|---|
| Vanilla | ×1 | ×1.3 | ×1.6 | ×1.9 | ×2.2 | ×2.2 | ×2.2 |
| Raid / Light | ×0.8 | ×1.5 | ×2 | ×2.5 | ×3 | ×3.5 | ×4.5 |
| Hard | ×1.0 | ×1.88 | ×2.5 | ×3.13 | ×3.75 | ×4.38 | ×5.63 |

**Number of adds** — the tables below give the "base" (how many of each kind come);
what actually comes:

| Base | Profile | N=1 | 2 | 3 | 4 | 5 | 6 | 8 |
|---|---|---|---|---|---|---|---|---|
| 1 | Raid / Light | 1 | 1 | 2 | 2 | 2 | 3 | 4 |
| 1 | Hard | 1 | 2 | 2 | 3 | 4 | 4 | 6 |
| 2 | Raid / Light | 1 | 2 | 3 | 4 | 5 | 6 | 8 |
| 2 | Hard | 2 | 3 | 4 | 6 | 8 | 9 | 12 |
| 3 | Raid / Light | 2 | 3 | 4 | 6 | 8 | 9 | 12 |
| 3 | Hard | 2 | 4 | 7 | 9 | 11 | 14 | 18 |

**Other things that depend on N:**
- nests, totems and eggs: solo — always 1 per phase; 2–5 players — as in the boss's
  table; 6+ — one more;
- a totem keeps 2–3 of its monsters alive, one more from 4 players;
- with one player the boss's HP is ×0.8 (`SoloHealthMultiplier`), lieutenants' ×0.6
  (`SoloLieutenantHealth`) — every mechanic falls on one person;
- lieutenants: **+1★** from 4 players; ordinary adds: **+1★** from 7 players (at most two
  stars);
- marks: 1 player at a time, 2 from 5 players;
- the Elder's troll: two from 3 players;
- positional phases (hit from behind) never come up for a single player;
- **crowd cap**: at most 8 monsters within 50 m of the boss for one player and +3 for
  each further player (`09 Raid / AddsCap*`); at the cap nests, totems, waves and slime
  release nobody (lieutenants, cocoon guards and roots always spawn).

### The general shape of a fight (Raid)

1. **85 / 70 / 55%** — nests (1 / 2 / 3), marks from 70%.
2. **55%** — the shield: while at least one nest **from the 85 / 70 / 55% thresholds**
   stands, damage to the boss is ×0.2 (Hard ×0.1). Nests grown from the Elder's seeds do
   not hold the shield. After 90 s the shield drops by itself ("the shield crumbles") —
   but with no window.
3. The last such nest is broken — the message "the defence has fallen — strike!",
   **a stagger and a burn window**: 10 s of ×1.5 damage.
4. After the window — **phase cycles**: a 30 s phase (random variant), then a 50–70 s
   break, and again — until death. **Phases do not start under the shield or the
   cocoon** (they wait for it to end); with no window the first phase comes no earlier
   than 60 s after 55%. The shield dropping interrupts the current phase. All mechanics
   together never cut damage to the boss below ×0.1 (`MinDamageFactor`).
5. **45% and below** — lieutenants and late waves, special mechanics.
6. Death — a reward on top of the vanilla drop; adds and nests disappear.

Each threshold fires **once per fight**: if the boss heals above a threshold and drops to
it again, the phase does not repeat (only a fight reset can repeat it, `06 Reset`, off by
default).

Messages stay in the centre of the screen ×1.5 as long as vanilla (`07 Client /
MessageDurationMultiplier`, only for players with the mod).

Marks: an announcement with the name → a hit under the player 3 s later (players with
the mod see a red circle of the radius); every 20 s (Hard 15 s).

### Reward (the same in every profile)

- The biome's valuables — every time; those marked "×N" grow by 50% for every player
  beyond the first.
- Next-biome resources — each with a 15% chance.
- Gear upgraded to quality 2–3: 1–2 players — none, 3–4 — 1 item, 5–7 — 2, 8 — 3; each
  with a 15% chance of coming from the next biome.

---

## 1. Eikthyr — `10 Eikthyr`

A tutorial fight: no shield, window or phase cycles.

| HP | Raid | Light | Hard |
|---|---|---|---|
| 80% | boars + necks, base 2 of each | the same | more, +1★ |
| 60% | **charge**: every 20 s a ×2 speed-up for 2.5 s towards a target farther than 6 m | no | the same |
| 50% | skull pile (`skull_pile`, if not destructible — `BonePileSpawner`): Meadows skeletons every 12 s, up to 3 alive | the same | every 9 s, skeletons +1★ |
| 30% | **lightning on marks**: 15 lightning damage | no | every 15 s |
| 15% | a leader — a 2★ boar with triple HP | the same | the same |

Reward: coins 60–90 ×N, amber 3 ×N; a chance — copper and tin ore; items — flint,
leather, wooden shield; a chance — bronze.

---

## 2. The Elder — `11 Elder`

| HP | Raid | Light | Hard |
|---|---|---|---|
| 85% | 1 greydwarf nest (`Spawner_GreydwarfNest`) | the same | the same |
| 70% | 2 more nests; **roots**: 4 roots around the marked player for 15 s; **seeds**: the Elder's projectile leaves a nest with a 5% chance (at most once per 20 s, at most 2 such nests; they do not hold the shield) | nests and seeds, no roots | roots more often |
| 55% | 3 more nests, **shield**; after the window — **living bark** | nests without a shield, no bark | shield ×0.1 |
| 45% | a troll (two from 3 players) | the same | the same |
| 30% | a bear (`Bjorn`) | the same | the same |
| 20% | elites (base 2) and **shamans** (base 1): heal the Elder for 0.3%/s each (up to two) while alive and within 40 m | shamans do not heal | more, +1★ |
| 10% | a skeleton + a ghost (base 1) | the same | more, +1★ |

**Living bark** (random variant each time):
- **Sap** — fire ×0.25, slash ×1.25, the rest ×0.25: chop with axes;
- **Back** — everything ×0.25 from the front, full damage from behind (a 120° arc): the
  tank keeps the Elder facing them. Never comes up solo.

Reward: coins 120–180 ×N, amber 3–6 ×N, a ruby, copper and tin; a chance — scrap iron;
items — bronze, troll hide; a chance — iron.

---

## 3. Bonemass — `12 Bonemass`

| HP | Raid | Light | Hard |
|---|---|---|---|
| 85% | 1 bone pile (`Spawner_DraugrPile`) | the same | the same |
| 70% | 2 more piles; **leeches** (base 2) — only in water nearby; **poison pools** on marks (25 poison); his hits make you **wet** | piles and leeches | more leeches, +1★; pools more often |
| 55% | 3 more piles, **shield**; after the window — phase cycles | piles without a shield or phases | shield ×0.1 |
| 45% | an abomination; **merging**: every 25 s slime (base 2) crawls in from afar (20–26 m); the whole wave together can heal at most 6%, split evenly between the blobs | abomination, no slime | more slime, +1★ |
| 40% | wraiths (base 1) | the same | more, +1★ |
| 35% | surtlings (base 2); his hits make you **tared** | surtlings | more, +1★ |
| 25% | elite draugr (base 1) | the same | more, +1★ |
| 15% | a ghost + 2 bats (base 1) | the same | more, +1★ |

**Phase cycles** (random variant):
- **Hardening** — blunt ×0.25, fire ×2: burn him;
- **Rot vapour** — hits closer than 5 m ×0.5, and each poisons the attacker (12 poison,
  at most once per second): strike from afar.

Reward: coins 180–270 ×N, rubies, pearls ×N, scrap iron; a chance — silver; items —
iron, root; a chance — silver.

---

## 4. Moder — `13 Moder`

| HP | Raid | Light | Hard |
|---|---|---|---|
| 85% | 1 ice stalagmite: drakes hatch every 15 s, up to 2 alive; **pack** — wolves (base 2), at night — ulvs | the same | drakes every 11 s, +1★; bigger pack |
| 70% | 2 more stalagmites; **ice novas** on marks (25 frost, slow) | stalagmites | novas more often |
| 55% | 3 more stalagmites, **shield**; after the window — phase cycles | stalagmites without a shield | shield ×0.1 |
| 45% | cultists (base 1); **breath mark**: every 35 s she lands and for 8 s keeps the marked player as her target | cultists | mark more often |
| 35% | a stone golem | the same | the same |
| 20% | a fenring (base 1) + drakes (base 2) | the same | more, +1★ |

**Phase cycles** (random variant):
- **Ice armor** — hits from farther than 6 m ×0.25 while she is **on the ground** (no
  effect in the air): a window for melee;
- **Ice spikes** — every hit returns 12% of the damage to the attacker as frost (at least
  5, at most once per second).

Reward: coins 240–360 ×N, rubies, silver necklaces ×N, silver, obsidian, crystals; a
chance — black metal; items — silver, wolf, fenris; a chance — black metal.

---

## 5. Yagluth — `14 Yagluth`

| HP | Raid | Light | Hard |
|---|---|---|---|
| 85% | 1 fuling totem: fulings and archers every 12 s, up to 3 alive | the same | every 9 s, +1★ |
| 70% | 2 more totems; his **meteors** around the marked player (vanilla damage) | totems | meteors more often |
| 55% | 3 more totems, **shield**, fuling **shamans** (base 1) heal him; after the window — phase cycles | totems and shamans without healing | shield ×0.1 |
| 45% | brutes (base 1); **beam**: every 30 s for 8 s keeps the marked player as the target; his hits make you **tared** | brutes | beam more often |
| 30% | an unbjorn | the same | the same |
| 20% | three skeletons (plain, archer, poison — base 1) | the same | more, +1★ |
| 15% | deathsquitos (base 1) | the same | more, +1★ |
| 10% | an echo of Moder (half HP) — **only** with `EchoOfModer = true` (off by default) | the same | the same |

**Phase cycles** (random variant):
- **Regeneration** — heals 0.3% of max HP per second; any frost hit stops it for 3 s;
- **Adaptation** — the damage type he was hit with most in the last seconds ×0.6, the
  others ×1.1; the announcement names the type.

Reward: coins 300–450 ×N, silver necklaces ×N, rubies, black metal; a chance — soft
tissue, a black core; items — black metal, linen; a chance — Mistlands.

---

## 6. The Queen — `15 Queen`

| HP | Raid | Light | Hard |
|---|---|---|---|
| 85% | a clutch of 2 eggs (solo 1, 3 from 6 players) — they hatch into seekers | the same | the same |
| 70% | 2 more eggs (solo 1, 3 from 6 players); **acid** on marks (30 poison) | eggs | acid more often |
| 55% | 3 more eggs (solo 1, 4 from 6 players), **shield** — while any egg is whole (break them before they hatch); after the window — phase cycles | a clutch without a shield | shield ×0.1 |
| 45% | brutes (base 1); **cocoon**: after her teleport — a seeker + a young one (base 1); while they live (up to 20 s) damage as under the shield; at most once per 30 s | brutes | guards +1★, shield ×0.1 |
| 30% | a gjall | the same | the same |
| 20% | ticks + young seekers (base 2) | the same | more, +1★ |

**Phase cycles** (random variant):
- **Bloodlust** — heals for 40% of the raw damage of her hits on players: do not get
  hit, block;
- **Acid spikes** — every hit returns 10% of the damage to the attacker as poison (at
  least 5, at most once per second).

Reward: coins 360–540 ×N, rubies, soft tissue, black cores, eitr ×N; a chance —
flametal ore, gemstones; items — Mistlands; a chance — Ashlands.

---

## 7. Fader — `16 Fader`

| HP | Raid | Light | Hard |
|---|---|---|---|
| 85% | 1 charred spawner stone (`Spawner_CharredStone`) | the same | the same |
| 70% | 2 more stones; his **meteors** around the marked player | stones | meteors more often |
| 55% | 3 more stones, **shield**; after the window — phase cycles | stones without a shield | shield ×0.1 |
| 45% | a morgen; **wall of fire** every 45 s towards a random player | morgen | wall every 34 s |
| 30% | a fallen valkyrie | the same | the same |
| 20% | Lord Reto | the same | the same |
| 10% | an asksvin + a lava blob (base 1) | the same | more, +1★ |

**Phase cycles** (random variant):
- **Molten armor** — hits closer than 6 m ×0.5 and burn the attacker (15 fire, at most
  once per second): strike from afar;
- **Ash veil** — hits from farther than 6 m ×0.25: everyone into melee.

Reward: coins 450–750 ×N, gemstones, flametal ore, molten cores; a chance — frozen
cores; items — Ashlands.

---

## Not yet

- The Ice King — after watching his vanilla fight in game.
- Mini-bosses (Hildir's bosses, Lord Reto as a fight of his own).
- Fader's shield generators.
