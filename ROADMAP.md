# ExtendedBosses — status and plan

**English** · [Русский](ROADMAP-RU.md)

Current version: **0.8.4**. Every boss from Eikthyr to Fader has its raid fight
(`FIGHTS.md`). The Elder has been through a first in-game test (fixes in 0.8.2); the
others are built and checked against the game's code (`check-refs.ps1`, `eb check`
self-check) but not yet played.

## Next

- [ ] In-game test of every boss with a group and with a guest without the mod:
      phases, shield and burn window, phase cycles, marks, threat, reward, cleanup.
- [ ] Balance pass after the tests (numbers in FIGHTS.md are starting values).
- [ ] The Ice King — after watching his vanilla fight in game.
- [ ] Mini-bosses (Hildir's bosses, Lord Reto as a fight of his own).
- [ ] Fader's shield generators.
- [ ] Dedicated server: a way to keep the boss owned by one modded client without
      [HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/).

## Principles that stay

- Only vanilla prefabs, animations, status effects and RPCs — a guest without the mod
  must see and feel the same fight.
- The vanilla fight (AI, attacks) is never changed, only added to.
- Every mechanic can be switched off; every boss can be set back to `Vanilla` without a
  rejoin.
