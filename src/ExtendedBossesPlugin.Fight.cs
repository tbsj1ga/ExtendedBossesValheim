using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExtendedBosses
{
    // The fight controller. Runs every TickInterval on the client that OWNS a boss (with
    // HostOwner that is the host). Persistent state is in the boss ZDO (phases fired, the
    // totems it placed, shield/resist/window on network time); timers that only matter while we
    // simulate the boss live in FightRt and are rebuilt if the boss changes hands.
    public partial class ExtendedBossesPlugin
    {
        // ZDO keys
        internal static readonly int KPhase = "j1ga.extendedbosses.phase".GetStableHashCode();        // boss: int bitmask of fired phases
        internal static readonly int KTotems = "j1ga.extendedbosses.totems".GetStableHashCode();      // boss: number of totem slots
        internal static readonly int KShieldAct = "j1ga.extendedbosses.shield".GetStableHashCode();   // boss: act code + 1 of the armed shield
        internal static readonly int KResistAct = "j1ga.extendedbosses.resist".GetStableHashCode();   // boss: act code + 1 of the resist phase
        internal static readonly int KResistUntil = "j1ga.extendedbosses.resistuntil".GetStableHashCode(); // boss: net ticks, 0 = while shielded
        internal static readonly int KWindowUntil = "j1ga.extendedbosses.window".GetStableHashCode(); // boss: net ticks the burn window ends
        internal static readonly int KCycleAct = "j1ga.extendedbosses.cycle".GetStableHashCode();       // boss: act code + 1 of the bark cycle
        internal static readonly int KCycleStartAt = "j1ga.extendedbosses.cyclestart".GetStableHashCode(); // boss: net ticks of the first bark (until it starts)
        internal static readonly int KCycleStarted = "j1ga.extendedbosses.cycleon".GetStableHashCode(); // boss: 1 once the cycle runs
        internal static readonly int KCycleUntil = "j1ga.extendedbosses.cycleuntil".GetStableHashCode(); // boss: net ticks the current bark ends
        internal static readonly int KCycleNext = "j1ga.extendedbosses.cyclenext".GetStableHashCode();  // boss: net ticks the next bark starts
        internal static readonly int KCycleAdapt = "j1ga.extendedbosses.cycleadapt".GetStableHashCode(); // boss: DamageType an Adapt variant resists
        internal static readonly int KCycleKind ="j1ga.extendedbosses.cyclekind".GetStableHashCode();  // boss: 1 = Sap, 2 = Back (fixed per bark)
        internal const string KBoss = "j1ga.extendedbosses.boss";                                      // add/totem: ZDOID of its boss
        internal static readonly int KHpMul = "j1ga.extendedbosses.hpmul".GetStableHashCode();        // add: effective HP multiplier
        internal static readonly int KSrc = "j1ga.extendedbosses.src".GetStableHashCode();            // add: totem slot + 1 (0 = none)
        internal static readonly int KRole = "j1ga.extendedbosses.role".GetStableHashCode();          // add: RoleHealer, ...
        internal static readonly int KExpire = "j1ga.extendedbosses.expire".GetStableHashCode();      // add: net ticks to remove it at

        private static string TotemKey(int i) { return "j1ga.extendedbosses.totem" + i; }
        private static int TotemActKey(int i) { return ("j1ga.extendedbosses.totemact" + i).GetStableHashCode(); }
        private const int NestCode = -1;       // totem slot holding a vanilla nest (spawns on its own)

        private class Strike
        {
            public ZDOID Target;
            public float At;
            public Act Act;
        }

        private class FightRt
        {
            public ZDOID BossId;
            public BossDef Def;
            public Character Boss;
            public bool Seen;
            public float NextMark;
            public readonly List<Strike> Strikes = new List<Strike>();
            public float NextCharge;
            public float ChargeEnd;
            public bool Charging;
            public float BaseSpeed;
            public float BaseRun;
            public readonly Dictionary<int, float> TotemNext = new Dictionary<int, float>();
            public float NoPlayersSince = -1f;
            public bool CleanedForVanilla;

            // read by the damage prefix; refreshed every tick from the ZDO
            public bool ShieldActive;
            public bool WindowActive;
            public Act ResistAct;
            public Act CycleAct;            // non-null while a resistance cycle is up
            public CycleVariant CycleVar;   // and its variant
            public float NextFusion;        // Fusion: next slime wave (local time)
            public readonly Dictionary<ZDOID, float> RetaliateAt = new Dictionary<ZDOID, float>(); // per player, local time
            public readonly Dictionary<HitData.DamageType, float> TypeDmg = new Dictionary<HitData.DamageType, float>();   // recent damage by type (decays)
            public readonly Dictionary<HitData.DamageType, float> LastHitBy = new Dictionary<HitData.DamageType, float>();  // local time of the last hit with the type
            public HitData.DamageType AdaptType;    // the type an active Adapt variant resists
            public int TotemsAlive;
            public int Healers;

            // threat
            public readonly Dictionary<ZDOID, float> Threat = new Dictionary<ZDOID, float>();
            public ZDOID ThreatTarget = ZDOID.None;
            public float ThreatSince;
        }

        private readonly Dictionary<ZDOID, FightRt> _fights = new Dictionary<ZDOID, FightRt>();
        private readonly List<Character> _tmpBosses = new List<Character>();
        private readonly List<ZDOID> _tmpIds = new List<ZDOID>();
        private int _forcePlayers;             // 'eb players <n>' for testing; 0 = count

        internal static long NetTicks()
        {
            ZNet z = ZNet.instance;
            return z != null ? z.GetTime().Ticks : DateTime.UtcNow.Ticks;
        }

        private static long Seconds(float s) { return (long)(s * TimeSpan.TicksPerSecond); }

        // ------------------------------------------------------------------
        // tick
        // ------------------------------------------------------------------
        private void TickFights(float now)
        {
            _tmpBosses.Clear();
            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];
                if (c == null || !c.IsBoss() || c.IsDead()) continue;
                ZDO z = Zdo(c);
                if (z == null || BossOf(z) == null) continue;
                if (!IsOwner(c)) continue;
                _tmpBosses.Add(c);
            }

            foreach (FightRt rt in _fights.Values) rt.Seen = false;
            for (int i = 0; i < _tmpBosses.Count; i++)
            {
                Character boss = _tmpBosses[i];
                try { TickBoss(boss, now); }
                catch (Exception e) { Fail("tick " + boss.m_name, e); }
            }

            // bosses we no longer simulate (unloaded, died, handed over): undo local changes
            _tmpIds.Clear();
            foreach (KeyValuePair<ZDOID, FightRt> kv in _fights) if (!kv.Value.Seen) _tmpIds.Add(kv.Key);
            for (int i = 0; i < _tmpIds.Count; i++)
            {
                FightRt rt = _fights[_tmpIds[i]];
                StopCharge(rt);
                _fights.Remove(_tmpIds[i]);
            }
        }

        private FightRt Rt(Character boss, BossDef def)
        {
            ZDOID id = boss.GetZDOID();
            FightRt rt;
            if (!_fights.TryGetValue(id, out rt))
            {
                rt = new FightRt();
                rt.BossId = id;
                rt.Def = def;
                _fights[id] = rt;
            }
            rt.Boss = boss;
            rt.Seen = true;
            return rt;
        }

        private FightRt RtIfRunning(Character boss)
        {
            FightRt rt;
            return boss != null && _fights.TryGetValue(boss.GetZDOID(), out rt) ? rt : null;
        }

        private void TickBoss(Character boss, float now)
        {
            ZDO z = Zdo(boss);
            BossDef def = BossOf(z);
            FightRt rt = Rt(boss, def);

            if (!IsModMode(def))
            {
                StopCharge(rt);
                rt.Strikes.Clear();
                rt.ShieldActive = rt.WindowActive = false;
                rt.ResistAct = null;
                rt.CycleAct = null;
                rt.Threat.Clear();
                SetBossGroup(boss, false);
                if (!rt.CleanedForVanilla)
                {
                    rt.CleanedForVanilla = true;
                    if (Sb(_cfgCleanupOnDisable) && z.GetInt(KTotems) + CountAdds(rt.BossId, 0) > 0)
                    {
                        int n = Cleanup(boss);
                        Debug(def.Prefab + " switched to Vanilla: removed " + n + " object(s)");
                    }
                }
                return;
            }
            rt.CleanedForVanilla = false;
            SetBossGroup(boss, true);

            int players = GroupSize(boss);
            int mask = z.GetInt(KPhase);
            float pct = boss.GetHealthPercentage() * 100f;
            long net = NetTicks();

            if (TickReset(boss, rt, mask, pct, now)) return;

            // phases: mark first, then run - an exception never makes a phase fire twice
            for (int i = 0; i < def.Phases.Count && i < 31; i++)
            {
                int bit = 1 << i;
                if ((mask & bit) != 0 || pct > def.Phases[i].Pct) continue;
                mask |= bit;
                z.Set(KPhase, mask);
                RunPhase(boss, rt, i, players, now);
            }

            TickAdds(boss, rt, net);
            DecayTyped(rt);
            TickTotems(boss, rt, z, players, now);
            TickShield(boss, rt, z, net);
            TickCycle(boss, rt, z, net, players);
            TickHeal(boss, rt);
            TickFusion(boss, rt, mask, players, now);
            TickMarks(boss, rt, mask, players, now);
            TickCharge(boss, rt, mask, now);
            TickThreat(boss, rt);
        }

        private void RunPhase(Character boss, FightRt rt, int index, int players, float now)
        {
            BossDef def = rt.Def;
            PhaseDef ph = def.Phases[index];
            int ran = 0;
            for (int a = 0; a < ph.Acts.Count; a++)
            {
                Act act = ph.Acts[a];
                if (!MechanicOn(def, act.Kind)) continue;
                if (act.Gate != null)
                {
                    BepInEx.Configuration.ConfigEntry<bool> gate;
                    if (!def.Gates.TryGetValue(act.Gate, out gate) || !Sb(gate)) continue;
                }
                try
                {
                    if (RunAct(boss, rt, act, index, a, players, now)) ran++;
                }
                catch (Exception e) { Fail(def.Prefab + " phase " + index + " act " + a, e); }
            }
            Debug(def.Prefab + ": phase " + index + " (" + ph.Pct + "%) fired, " + ran + " action(s), players " + players);
            if (ran > 0 && !string.IsNullOrEmpty(ph.Say)) Announce(ph.Say, NameToken(boss));
        }

        private bool RunAct(Character boss, FightRt rt, Act act, int phase, int index, int players, float now)
        {
            Vector3 center = boss.transform.position;
            ZDO z = Zdo(boss);
            int code = phase * 8 + index;
            switch (act.Kind)
            {
                case ActKind.Wave:
                {
                    int count = ScaledCount(rt.Def, act.Count, players);
                    int level = AddLevel(rt.Def, act.Level, players);
                    int spawned = 0;
                    string[] prefabs = act.NightPrefabs != null && EnvMan.IsNight() ? act.NightPrefabs : act.Prefabs;
                    for (int p = 0; p < prefabs.Length; p++)
                        for (int k = 0; k < count; k++)
                        {
                            Character c;
                            if (act.InWater)
                            {
                                Vector3 wp;
                                if (!WaterPoint(center, Sv(_cfgSpawnRadiusMin), Sv(_cfgSpawnRadiusMax), out wp)) continue;
                                c = SpawnCreatureAt(rt.BossId, prefabs[p], level, act.HpMul, wp, 0, act.Role, act.Lifetime);
                            }
                            else c = SpawnCreature(rt.BossId, prefabs[p], level, act.HpMul, center, 0, Sv(_cfgSpawnRadiusMin), Sv(_cfgSpawnRadiusMax), act.Role, act.Lifetime);
                            if (c != null) spawned++;
                        }
                    if (act.InWater && spawned == 0) Debug(rt.Def.Prefab + ": no water around for " + act.Prefabs[0]);
                    return spawned > 0;
                }
                case ActKind.Lieutenant:
                {
                    int count = Mathf.Max(1, Mathf.RoundToInt(act.Count)) + (act.ExtraFrom > 0 && players >= act.ExtraFrom ? 1 : 0);
                    int level = LieutenantLevel(act.Level, players);
                    int spawned = 0;
                    for (int p = 0; p < act.Prefabs.Length; p++)
                        for (int k = 0; k < count; k++)
                            if (SpawnCreature(rt.BossId, act.Prefabs[p], level, act.HpMul, center, 0, Sv(_cfgSpawnRadiusMin), Sv(_cfgSpawnRadiusMax), act.Role, act.Lifetime) != null) spawned++;
                    return spawned > 0;
                }
                case ActKind.Nest:
                case ActKind.Totem:
                {
                    int count = act.ScaleAsAdds ? ScaledCount(rt.Def, act.Count, players) : NestCount(act.Count, players);
                    int placed = 0;
                    for (int k = 0; k < count; k++)
                        if (PlaceTotem(boss, rt, act, phase, index)) placed++;
                    return placed > 0;
                }
                case ActKind.Marks:
                    rt.NextMark = now + 3f;     // first mark shortly after the announcement
                    return true;
                case ActKind.Charge:
                    rt.NextCharge = now + 2f;
                    return true;
                case ActKind.Fusion:
                    rt.NextFusion = now + 5f;
                    return true;
                case ActKind.Shield:
                    z.Set(KShieldAct, code + 1);
                    return true;
                case ActKind.Resist:
                    z.Set(KResistAct, code + 1);
                    z.Set(KResistUntil, act.Duration > 0f ? NetTicks() + Seconds(act.Duration) : 0L);
                    return true;
                case ActKind.Cycle:
                    z.Set(KCycleAct, code + 1);
                    z.Set(KCycleStarted, 0);
                    z.Set(KCycleStartAt, NetTicks() + Seconds(rt.Def.CfgCycleDelay != null ? Sv(rt.Def.CfgCycleDelay) : act.Delay));
                    return true;
            }
            return false;
        }

        private Act ActByCode(BossDef def, int code)
        {
            int phase = code / 8, index = code % 8;
            if (code < 0 || phase >= def.Phases.Count || index >= def.Phases[phase].Acts.Count) return null;
            return def.Phases[phase].Acts[index];
        }

        // ------------------------------------------------------------------
        // scaling
        // ------------------------------------------------------------------
        internal int GroupSize(Character boss)
        {
            int max = Mathf.Max(1, Si(_cfgMaxScalingPlayers));
            if (_forcePlayers > 0) return Mathf.Min(_forcePlayers, max);
            return Mathf.Clamp(PlayersNear(boss.transform.position, Sv(_cfgScalingRange)).Count, 1, max);
        }

        // Multiplier of damage taken by a boss: our group scaling instead of vanilla's (which
        // ApplyDamage still divides by afterwards), shield, burn window. Resistances are per type
        // and applied separately (ApplyResist).
        internal float BossDamageFactor(Character boss, BossDef def)
        {
            int n = GroupSize(boss);
            float vanillaHp = 1f;
            Game g = Game.instance;
            if (g != null)
            {
                float s = g.GetDifficultyDamageScaleEnemy(boss.transform.position);
                if (s > 0.0001f) vanillaHp = 1f / s;
            }
            bool hard = ProfileOf(def) == ProfileHard;
            float ours = Sv(_cfgBaseHealth) * (1f + Sv(_cfgHealthPerPlayer) * (n - 1));
            if (hard) ours *= Sv(_cfgHardHealth);
            if (ours < 0.01f) ours = 0.01f;
            float f = vanillaHp / ours;

            FightRt rt = RtIfRunning(boss);
            if (rt != null)
            {
                if (rt.ShieldActive) f *= Sv(hard ? _cfgHardShieldFactor : _cfgShieldFactor);
                else if (rt.WindowActive) f *= Sv(_cfgWindowMultiplier);
            }
            return f;
        }

        internal void ApplyResist(Character boss, HitData hit)
        {
            FightRt rt = RtIfRunning(boss);
            if (rt == null || rt.ResistAct == null || rt.ResistAct.Mods == null) return;
            foreach (KeyValuePair<HitData.DamageType, float> kv in rt.ResistAct.Mods)
            {
                float m = kv.Value;
                switch (kv.Key)
                {
                    case HitData.DamageType.Blunt: hit.m_damage.m_blunt *= m; break;
                    case HitData.DamageType.Slash: hit.m_damage.m_slash *= m; break;
                    case HitData.DamageType.Pierce: hit.m_damage.m_pierce *= m; break;
                    case HitData.DamageType.Fire: hit.m_damage.m_fire *= m; break;
                    case HitData.DamageType.Frost: hit.m_damage.m_frost *= m; break;
                    case HitData.DamageType.Lightning: hit.m_damage.m_lightning *= m; break;
                    case HitData.DamageType.Poison: hit.m_damage.m_poison *= m; break;
                    case HitData.DamageType.Spirit: hit.m_damage.m_spirit *= m; break;
                }
            }
        }

        private float AddsFactor(BossDef def, int players)
        {
            float f = players <= 1 ? Sv(_cfgSoloAdds) : players * Sv(_cfgAddsPerPlayer);
            if (ProfileOf(def) == ProfileHard) f *= Sv(_cfgHardAdds);
            return f;
        }

        private int ScaledCount(BossDef def, float baseCount, int players)
        {
            return Mathf.Max(1, Mathf.RoundToInt(baseCount * AddsFactor(def, players)));
        }

        private int AddLevel(BossDef def, int level, int players)
        {
            if (players >= Si(_cfgAddStarPlayers)) level++;
            if (ProfileOf(def) == ProfileHard) level++;
            return Mathf.Clamp(level, 1, 3);
        }

        private int LieutenantLevel(int level, int players)
        {
            if (players >= Si(_cfgLieutenantStarPlayers)) level++;
            return Mathf.Clamp(level, 1, 3);
        }

        private static int NestCount(float baseCount, int players)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(baseCount));
            if (players <= 1) return 1;
            if (players >= 6) n++;
            return n;
        }

        // ------------------------------------------------------------------
        // adds: lifetime, healers
        // ------------------------------------------------------------------
        private readonly List<GameObject> _tmpExpired = new List<GameObject>();

        private void TickAdds(Character boss, FightRt rt, long net)
        {
            _tmpExpired.Clear();
            int healers = 0;
            float healRange = Sv(_cfgHealRange);
            Vector3 bp = boss.transform.position;
            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];
                if (c == null || c == boss || c.IsDead()) continue;
                ZDO z = Zdo(c);
                if (z == null || z.GetZDOID(KBoss) != rt.BossId) continue;
                long exp = z.GetLong(KExpire, 0L);
                if (exp > 0L && net >= exp) { _tmpExpired.Add(c.gameObject); continue; }
                int role = z.GetInt(KRole);
                if (role == RoleHealer && Vector3.Distance(c.transform.position, bp) <= healRange) healers++;
                else if (role == RoleFuse && TryFuse(boss, rt, c)) _tmpExpired.Add(c.gameObject);
            }
            for (int i = 0; i < _tmpExpired.Count; i++) DestroyNetObject(_tmpExpired[i]);
            rt.Healers = healers;
        }

        private void TickHeal(Character boss, FightRt rt)
        {
            if (rt.Healers <= 0 || !HealersOn(rt.Def)) return;
            float max = boss.GetMaxHealth();
            if (boss.GetHealth() >= max) return;
            float amount = max * Sv(_cfgHealPercent) / 100f * Mathf.Min(rt.Healers, 2) * TickInterval;
            if (amount > 0f) boss.Heal(amount, false);
        }

        // ------------------------------------------------------------------
        // fusion (Bonemass): slime crawls to the boss from afar; what reaches it heals it
        // ------------------------------------------------------------------
        private const float FuseReach = 3.5f;

        private void TickFusion(Character boss, FightRt rt, int mask, int players, float now)
        {
            Act act = ActiveAct(rt.Def, mask, ActKind.Fusion);
            if (act == null || !MechanicOn(rt.Def, ActKind.Fusion)) return;
            if (rt.NextFusion <= 0f) rt.NextFusion = now + 5f;
            if (now < rt.NextFusion) return;
            rt.NextFusion = now + (rt.Def.CfgFusionInterval != null ? Sv(rt.Def.CfgFusionInterval) : act.Interval);

            int count = ScaledCount(rt.Def, act.Count, players);
            int level = AddLevel(rt.Def, act.Level, players);
            int spawned = 0;
            for (int k = 0; k < count; k++)
            {
                string prefab = act.Prefabs[UnityEngine.Random.Range(0, act.Prefabs.Length)];
                Character c = SpawnCreature(rt.BossId, prefab, level, 1f, boss.transform.position, 0, 20f, 26f, RoleFuse, 0f);
                if (c == null) continue;
                FollowBoss(c, boss);
                spawned++;
            }
            if (spawned > 0 && !string.IsNullOrEmpty(act.MarkKey)) Announce(act.MarkKey, NameToken(boss));
        }

        // A fusing creature walks to the boss instead of hunting players (it still fights back).
        private static void FollowBoss(Character c, Character boss)
        {
            MonsterAI ai = c.GetComponent<MonsterAI>();
            if (ai == null) return;
            ai.SetHuntPlayer(false);
            if (ai.GetFollowTarget() != boss.gameObject) ai.SetFollowTarget(boss.gameObject);
        }

        // Called from TickAdds for every living fusing creature of the boss (we own the boss).
        private bool TryFuse(Character boss, FightRt rt, Character c)
        {
            if (!IsOwner(c)) return false;
            if (Vector3.Distance(c.transform.position, boss.transform.position) > FuseReach + boss.GetRadius())
            {
                FollowBoss(c, boss);        // follow target is not saved: re-apply after a handover
                return false;
            }
            if (MechanicOn(rt.Def, ActKind.Fusion))
            {
                Act act = ActiveAct(rt.Def, Zdo(boss).GetInt(KPhase), ActKind.Fusion);
                float pct = rt.Def.CfgFusionHeal != null ? Sv(rt.Def.CfgFusionHeal) : (act != null ? act.Heal : 0f);
                float amount = boss.GetMaxHealth() * pct / 100f;
                if (amount > 0f) boss.Heal(amount, true);
                Debug(rt.Def.Prefab + ": " + c.m_name + " fused, healed " + F1(amount));
            }
            return true;
        }

        // ------------------------------------------------------------------
        // shield, burn window, resistances
        // ------------------------------------------------------------------
        private void TickShield(Character boss, FightRt rt, ZDO z, long net)
        {
            rt.TotemsAlive = TotemsAlive(z);
            bool armed = z.GetInt(KShieldAct) > 0;
            if (armed && rt.TotemsAlive == 0)
            {
                z.Set(KShieldAct, 0);
                if (MechanicOn(rt.Def, ActKind.Shield) && Sv(_cfgWindowSeconds) > 0f)
                {
                    long windowEnd = net + Seconds(Sv(_cfgWindowSeconds));
                    z.Set(KWindowUntil, windowEnd);
                    // the bark waits for the stagger to end: first bark right after the window;
                    // a bark already up is cut so the window gets full damage
                    if (z.GetInt(KCycleAct) > 0)
                    {
                        if (z.GetInt(KCycleStarted) == 0) z.Set(KCycleStartAt, windowEnd);
                        else if (net < z.GetLong(KCycleUntil, 0L))
                        {
                            z.Set(KCycleUntil, net);
                            z.Set(KCycleNext, windowEnd + Seconds(CycleCooldown(rt.Def, ActByCode(rt.Def, z.GetInt(KCycleAct) - 1))));
                        }
                    }
                    boss.Stagger(-boss.transform.forward);
                    Announce("window", NameToken(boss));
                    Debug(rt.Def.Prefab + ": shield down, burn window " + F1(Sv(_cfgWindowSeconds)) + " s");
                }
                armed = false;
            }
            rt.ShieldActive = armed && MechanicOn(rt.Def, ActKind.Shield);
            rt.WindowActive = net < z.GetLong(KWindowUntil, 0L);

            rt.ResistAct = null;
            int rc = z.GetInt(KResistAct);
            if (rc > 0)
            {
                long until = z.GetLong(KResistUntil, 0L);
                bool on = until == 0L ? armed : net < until;
                if (!on) z.Set(KResistAct, 0);
                else if (MechanicOn(rt.Def, ActKind.Resist)) rt.ResistAct = ActByCode(rt.Def, rc - 1);
            }
        }

        // ------------------------------------------------------------------
        // resistance cycle (Elder's bark, Bonemass's hardening): repeats until death, one of the
        // act's variants each time (random, pinned by config, or Auto); positional variants are
        // never picked for a single player
        // ------------------------------------------------------------------
        private void TickCycle(Character boss, FightRt rt, ZDO z, long net, int players)
        {
            rt.CycleAct = null;
            int code = z.GetInt(KCycleAct);
            if (code <= 0) return;
            Act act = ActByCode(rt.Def, code - 1);
            if (act == null) return;
            bool on = MechanicOn(rt.Def, ActKind.Resist);

            if (z.GetInt(KCycleStarted) == 0)
            {
                // first bark: after the burn window, or after the delay if no window came
                if (rt.WindowActive || net < z.GetLong(KCycleStartAt, 0L)) return;
                z.Set(KCycleStarted, 1);
                StartCycle(boss, rt, z, act, net, players, on);
            }
            else
            {
                long until = z.GetLong(KCycleUntil, 0L);
                long next = z.GetLong(KCycleNext, 0L);
                if (net >= until && next == 0L)
                {
                    // just ended: announce and roll the cooldown
                    z.Set(KCycleNext, until + Seconds(CycleCooldown(rt.Def, act)));
                    int ended = z.GetInt(KCycleKind) - 1;
                    string endKey = act.Variants != null && ended >= 0 && ended < act.Variants.Count && !string.IsNullOrEmpty(act.Variants[ended].EndKey)
                        ? act.Variants[ended].EndKey : act.EndKey;
                    if (on && !string.IsNullOrEmpty(endKey)) Announce(endKey, NameToken(boss));
                    Debug(rt.Def.Prefab + ": cycle ended");
                }
                else if (next > 0L && net >= next && !rt.WindowActive) StartCycle(boss, rt, z, act, net, players, on);
            }

            if (on && net < z.GetLong(KCycleUntil, 0L) && act.Variants != null)
            {
                int vi = z.GetInt(KCycleKind) - 1;
                if (vi >= 0 && vi < act.Variants.Count)
                {
                    rt.CycleAct = act;
                    rt.CycleVar = act.Variants[vi];
                    rt.AdaptType = (HitData.DamageType)z.GetInt(KCycleAdapt);
                    TickRegen(boss, rt, rt.CycleVar);
                }
            }
        }

        // Regeneration variant: heals every tick unless a hit of the stopping type came lately.
        private void TickRegen(Character boss, FightRt rt, CycleVariant v)
        {
            if (v.RegenPercent <= 0f) return;
            float last;
            if (rt.LastHitBy.TryGetValue(v.RegenStopType, out last) && Time.time - last < v.RegenStopSeconds) return;
            float max = boss.GetMaxHealth();
            if (boss.GetHealth() >= max) return;
            float pct = rt.Def.CfgRegen != null ? Sv(rt.Def.CfgRegen) : v.RegenPercent;
            float amount = max * pct / 100f * TickInterval;
            if (amount > 0f) boss.Heal(amount, false);
        }

        // Bloodthirst: the boss's hit on a player is built on the boss owner (Character.Damage
        // runs there before the RPC to the player), so the raw damage is known here.
        internal void Lifesteal(Character boss, HitData hit)
        {
            FightRt rt = RtIfRunning(boss);
            if (rt == null || rt.CycleAct == null || rt.CycleVar == null || rt.CycleVar.Lifesteal <= 0f) return;
            float pct = rt.Def.CfgLifesteal != null ? Sv(rt.Def.CfgLifesteal) : rt.CycleVar.Lifesteal;
            float amount = hit.GetTotalDamage() * pct / 100f;
            if (amount > 0f && boss.GetHealth() < boss.GetMaxHealth()) boss.Heal(amount, true);
        }

        // Damage by type as it arrives (before any modifier), for adaptation and regen stops.
        internal void RecordHit(Character boss, HitData hit)
        {
            FightRt rt = RtIfRunning(boss);
            if (rt == null) return;
            float now = Time.time;
            AddTyped(rt, HitData.DamageType.Blunt, hit.m_damage.m_blunt, now);
            AddTyped(rt, HitData.DamageType.Slash, hit.m_damage.m_slash, now);
            AddTyped(rt, HitData.DamageType.Pierce, hit.m_damage.m_pierce, now);
            AddTyped(rt, HitData.DamageType.Fire, hit.m_damage.m_fire, now);
            AddTyped(rt, HitData.DamageType.Frost, hit.m_damage.m_frost, now);
            AddTyped(rt, HitData.DamageType.Lightning, hit.m_damage.m_lightning, now);
            AddTyped(rt, HitData.DamageType.Poison, hit.m_damage.m_poison, now);
            AddTyped(rt, HitData.DamageType.Spirit, hit.m_damage.m_spirit, now);
        }

        private static void AddTyped(FightRt rt, HitData.DamageType t, float amount, float now)
        {
            if (amount <= 0f) return;
            float v;
            rt.TypeDmg.TryGetValue(t, out v);
            rt.TypeDmg[t] = v + amount;
            rt.LastHitBy[t] = now;
        }

        // recent damage by type fades with a 10 s half-life
        private static void DecayTyped(FightRt rt)
        {
            if (rt.TypeDmg.Count == 0) return;
            float k = Mathf.Pow(0.5f, TickInterval / 10f);
            List<HitData.DamageType> keys = new List<HitData.DamageType>(rt.TypeDmg.Keys);
            for (int i = 0; i < keys.Count; i++) rt.TypeDmg[keys[i]] *= k;
        }

        private static HitData.DamageType MostUsedType(FightRt rt)
        {
            HitData.DamageType best = HitData.DamageType.Slash;
            float bestV = 0f;
            foreach (KeyValuePair<HitData.DamageType, float> kv in rt.TypeDmg)
                if (kv.Value > bestV) { bestV = kv.Value; best = kv.Key; }
            return best;
        }

        // vanilla tooltip token of a damage type ($inventory_slash), localized by every client
        private static string TypeToken(HitData.DamageType t)
        {
            return "$inventory_" + t.ToString().ToLowerInvariant();
        }

        private void StartCycle(Character boss, FightRt rt, ZDO z, Act act, long net, int players, bool on)
        {
            if (act.Variants == null || act.Variants.Count == 0) return;
            int vi = PickVariant(rt.Def, act, players);
            CycleVariant v = act.Variants[vi];
            float dur = rt.Def.CfgCycleDuration != null ? Sv(rt.Def.CfgCycleDuration) : act.Duration;
            z.Set(KCycleKind, vi + 1);
            z.Set(KCycleUntil, net + Seconds(dur));
            z.Set(KCycleNext, 0L);
            string extra = "";
            if (v.Adapt)
            {
                HitData.DamageType t = MostUsedType(rt);
                z.Set(KCycleAdapt, (int)t);
                extra = TypeToken(t);
            }
            if (on && !string.IsNullOrEmpty(v.Say)) Announce(v.Say, NameToken(boss), extra);
            Debug(rt.Def.Prefab + ": cycle " + v.Id + (v.Adapt ? " (" + extra + ")" : "") + " for " + F1(dur) + " s");
        }

        private int PickVariant(BossDef def, Act act, int players)
        {
            List<int> allowed = new List<int>();
            for (int i = 0; i < act.Variants.Count; i++)
                if (!(act.Variants[i].Back && players <= 1)) allowed.Add(i);     // solo: never positional
            if (allowed.Count == 0) allowed.Add(0);

            string want = def.CfgCycleVariant != null ? Ss(def.CfgCycleVariant) : CycleRandom;
            if (want == CycleAuto)
            {
                for (int k = 0; k < allowed.Count; k++)
                    if (act.Variants[allowed[k]].Back == (players >= 3)) return allowed[k];
                return allowed[0];
            }
            for (int k = 0; k < allowed.Count; k++)
                if (act.Variants[allowed[k]].Id == want) return allowed[k];
            return allowed[UnityEngine.Random.Range(0, allowed.Count)];          // Random, or a pinned one not allowed now
        }

        private float CycleCooldown(BossDef def, Act act)
        {
            float a = def.CfgCycleCdMin != null ? Sv(def.CfgCycleCdMin) : (act != null ? act.CooldownMin : 60f);
            float b = def.CfgCycleCdMax != null ? Sv(def.CfgCycleCdMax) : (act != null ? act.CooldownMax : 60f);
            if (b < a) b = a;
            return UnityEngine.Random.Range(a, b);
        }

        // Applied to a hit on the boss while a cycle is up.
        internal void ApplyCycle(Character boss, HitData hit)
        {
            FightRt rt = RtIfRunning(boss);
            if (rt == null || rt.CycleAct == null || rt.CycleVar == null) return;
            CycleVariant v = rt.CycleVar;
            if (v.Back)
            {
                if (FromBehind(boss, hit, rt.Def.CfgBackArc != null ? Sv(rt.Def.CfgBackArc) : 120f)) return;
                hit.ApplyModifier(v.Other);
                return;
            }
            if (v.GroundedOnly && boss.IsFlying()) return;       // e.g. Moder's ice armor: only on the ground
            if (v.MeleeFactor < 1f || v.Retaliate > 0f || v.RangedFactor < 1f || v.ThornsPercent > 0f)
            {
                Character a = hit.GetAttacker();
                if (a != null && a.IsPlayer())
                {
                    bool close = Flat(a.transform.position - boss.transform.position) <= v.MeleeRange + boss.GetRadius();
                    if (close)
                    {
                        float mf = rt.Def.CfgCycleMelee != null && v.MeleeFactor < 1f ? Sv(rt.Def.CfgCycleMelee) : v.MeleeFactor;
                        if (mf < 1f) hit.ApplyModifier(mf);
                        float back = rt.Def.CfgCycleRetaliate != null && v.Retaliate > 0f ? Sv(rt.Def.CfgCycleRetaliate) : v.Retaliate;
                        if (back > 0f) Retaliate(boss, rt, a, back, v.RetaliateType);
                    }
                    else
                    {
                        float rf = rt.Def.CfgCycleRanged != null && v.RangedFactor < 1f ? Sv(rt.Def.CfgCycleRanged) : v.RangedFactor;
                        if (rf < 1f) hit.ApplyModifier(rf);
                    }
                    float thorns = rt.Def.CfgCycleThorns != null && v.ThornsPercent > 0f ? Sv(rt.Def.CfgCycleThorns) : v.ThornsPercent;
                    if (thorns > 0f) Retaliate(boss, rt, a, Mathf.Max(v.ThornsMin, hit.GetTotalDamage() * thorns / 100f), v.RetaliateType);
                }
            }
            float o = v.Other;
            float blunt = o, slash = o, pierce = o, fire = o, frost = o, lightning = o, poison = o, spirit = o;
            if (v.Mods != null)
                foreach (KeyValuePair<HitData.DamageType, float> kv in v.Mods)
                    switch (kv.Key)
                    {
                        case HitData.DamageType.Blunt: blunt = kv.Value; break;
                        case HitData.DamageType.Slash: slash = kv.Value; break;
                        case HitData.DamageType.Pierce: pierce = kv.Value; break;
                        case HitData.DamageType.Fire: fire = kv.Value; break;
                        case HitData.DamageType.Frost: frost = kv.Value; break;
                        case HitData.DamageType.Lightning: lightning = kv.Value; break;
                        case HitData.DamageType.Poison: poison = kv.Value; break;
                        case HitData.DamageType.Spirit: spirit = kv.Value; break;
                    }
            if (v.Adapt)
            {
                float f = rt.Def.CfgAdaptFactor != null ? Sv(rt.Def.CfgAdaptFactor) : v.AdaptFactor;
                float others = rt.Def.CfgAdaptOthers != null ? Sv(rt.Def.CfgAdaptOthers) : v.AdaptOthers;
                HitData.DamageType t = rt.AdaptType;
                blunt = t == HitData.DamageType.Blunt ? f : others;
                slash = t == HitData.DamageType.Slash ? f : others;
                pierce = t == HitData.DamageType.Pierce ? f : others;
                fire = t == HitData.DamageType.Fire ? f : others;
                frost = t == HitData.DamageType.Frost ? f : others;
                lightning = t == HitData.DamageType.Lightning ? f : others;
                poison = t == HitData.DamageType.Poison ? f : others;
                spirit = t == HitData.DamageType.Spirit ? f : others;
            }
            hit.m_damage.m_blunt *= blunt;
            hit.m_damage.m_slash *= slash;
            hit.m_damage.m_pierce *= pierce;
            hit.m_damage.m_fire *= fire;
            hit.m_damage.m_frost *= frost;
            hit.m_damage.m_lightning *= lightning;
            hit.m_damage.m_poison *= poison;
            hit.m_damage.m_spirit *= spirit;
            hit.m_damage.m_chop *= slash;       // axes chop the tree too
        }

        // A vanilla hit on the attacking player from the boss (poison etc.): routed to the
        // player's own client like any monster hit, so players without the mod feel it too.
        private void Retaliate(Character boss, FightRt rt, Character attacker, float amount, HitData.DamageType type)
        {
            float now = Time.time, last;
            ZDOID id = attacker.GetZDOID();
            if (rt.RetaliateAt.TryGetValue(id, out last) && now - last < 1f) return;
            rt.RetaliateAt[id] = now;
            HitData h = new HitData();
            h.m_damage = DamageOf(type, amount);
            h.m_point = attacker.GetCenterPoint();
            Vector3 dir = attacker.transform.position - boss.transform.position;
            dir.y = 0f;
            h.m_dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
            h.SetAttacker(boss);
            attacker.Damage(h);
        }

        // Is the attacker within the arc behind the boss? No attacker (DoT, environment): front.
        private static bool FromBehind(Character boss, HitData hit, float arc)
        {
            Character a = hit.GetAttacker();
            Vector3 from = a != null ? a.transform.position : hit.m_point;
            if (a == null) return false;
            Vector3 to = from - boss.transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) return false;
            Vector3 fwd = boss.transform.forward;
            fwd.y = 0f;
            float angle = Vector3.Angle(fwd, to);          // 0 = in front, 180 = straight behind
            return angle >= 180f - arc * 0.5f;
        }

        private int TotemsAlive(ZDO bz)
        {
            int slots = bz.GetInt(KTotems), n = 0;
            for (int i = 0; i < slots; i++)
            {
                ZDOID id = bz.GetZDOID(TotemKey(i));
                if (id != ZDOID.None && ZDOMan.instance.GetZDO(id) != null) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------
        // totems and nests
        // ------------------------------------------------------------------
        private bool PlaceTotem(Character boss, FightRt rt, Act act, int phase, int index)
        {
            bool vanillaNest = act.Kind == ActKind.Nest;
            string prefab = vanillaNest ? act.Prefabs[0] : act.Prop;
            if (vanillaNest && rt.Def.CfgNestPrefab != null) prefab = Ss(rt.Def.CfgNestPrefab);
            if (!vanillaNest && rt.Def.CfgTotemPrefab != null) prefab = Ss(rt.Def.CfgTotemPrefab);

            GameObject pf = ZNetScene.instance.GetPrefab(prefab);
            if (!vanillaNest && !IsDestructibleProp(pf))
            {
                if (string.IsNullOrEmpty(act.Fallback)) { Warn(rt.Def.Prefab + ": totem prefab '" + prefab + "' is missing or not a destructible network object; no fallback, skipped."); return false; }
                Warn(rt.Def.Prefab + ": totem prefab '" + prefab + "' is missing or not a destructible network object; using " + act.Fallback + " (vanilla nest) instead.");
                prefab = act.Fallback;
                pf = ZNetScene.instance.GetPrefab(prefab);
                vanillaNest = true;
            }
            if (act.AnyProp ? (pf == null || pf.GetComponent<ZNetView>() == null) : !IsDestructibleProp(pf)) { Warn(rt.Def.Prefab + ": nest prefab '" + prefab + "' is missing or not " + (act.AnyProp ? "networked." : "destructible.")); return false; }

            GameObject go = SpawnObject(pf, RingPoint(boss.transform.position, Sv(_cfgSpawnRadiusMin), Sv(_cfgSpawnRadiusMax)));
            ZDO tz = Zdo(go.transform);
            if (tz == null) return false;
            tz.Set(KBoss, rt.BossId);

            ZDO bz = Zdo(boss);
            int slot = bz.GetInt(KTotems);
            bz.Set(TotemKey(slot), tz.m_uid);
            bz.Set(TotemActKey(slot), vanillaNest ? NestCode : phase * 8 + index);
            bz.Set(KTotems, slot + 1);
            Debug(rt.Def.Prefab + ": placed " + prefab + " in slot " + slot + (vanillaNest ? " (vanilla nest)" : ""));
            return true;
        }

        internal static bool IsDestructibleProp(GameObject pf)
        {
            if (pf == null || pf.GetComponent<ZNetView>() == null) return false;
            return pf.GetComponentInChildren<Destructible>(true) != null || pf.GetComponentInChildren<WearNTear>(true) != null;
        }

        private void TickTotems(Character boss, FightRt rt, ZDO bz, int players, float now)
        {
            if (!MechanicOn(rt.Def, ActKind.Totem)) return;
            int slots = bz.GetInt(KTotems);
            for (int i = 0; i < slots; i++)
            {
                int code = bz.GetInt(TotemActKey(i));
                if (code == NestCode) continue;
                Act act = ActByCode(rt.Def, code);
                if (act == null || act.Kind != ActKind.Totem) continue;

                ZDOID id = bz.GetZDOID(TotemKey(i));
                if (id == ZDOID.None) continue;
                GameObject totem = ZNetScene.instance.FindInstance(id);
                if (totem == null) continue;           // destroyed, or out of our area

                float next;
                if (rt.TotemNext.TryGetValue(i, out next) && now < next) continue;
                float interval = act.Interval * (ProfileOf(rt.Def) == ProfileHard ? 0.75f : 1f);
                rt.TotemNext[i] = now + interval;

                int maxAlive = act.MaxAlive + (players >= 4 ? 1 : 0);
                if (CountAdds(rt.BossId, i + 1) >= maxAlive) continue;
                string prefab = act.Prefabs[UnityEngine.Random.Range(0, act.Prefabs.Length)];
                SpawnCreature(rt.BossId, prefab, AddLevel(rt.Def, act.Level, players), act.HpMul, totem.transform.position, i + 1, 1.5f, 4f, act.Role, act.Lifetime);
            }
        }

        // ------------------------------------------------------------------
        // marks: "spread out" - a strike (or roots) on a marked player after a delay
        // ------------------------------------------------------------------
        private Act ActiveAct(BossDef def, int mask, ActKind kind)
        {
            for (int i = 0; i < def.Phases.Count && i < 31; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                List<Act> acts = def.Phases[i].Acts;
                for (int a = 0; a < acts.Count; a++) if (acts[a].Kind == kind) return acts[a];
            }
            return null;
        }

        private void TickMarks(Character boss, FightRt rt, int mask, int players, float now)
        {
            // pending strikes land even if marks were just switched off - they were announced
            for (int i = rt.Strikes.Count - 1; i >= 0; i--)
            {
                Strike s = rt.Strikes[i];
                if (now < s.At) continue;
                rt.Strikes.RemoveAt(i);
                try { LandStrike(boss, rt, s, players); }
                catch (Exception e) { Fail("strike", e); }
            }

            Act act = ActiveAct(rt.Def, mask, ActKind.Marks);
            if (act == null || !MechanicOn(rt.Def, ActKind.Marks)) return;
            if (rt.NextMark <= 0f) rt.NextMark = now + 3f;
            if (now < rt.NextMark) return;
            float interval = Sv(_cfgMarkInterval) * (ProfileOf(rt.Def) == ProfileHard ? 0.75f : 1f);
            rt.NextMark = now + interval;

            List<Player> targets = PlayersNear(boss.transform.position, Sv(_cfgScalingRange));
            if (targets.Count == 0) return;
            int count = Mathf.Max(1, Mathf.RoundToInt(act.Count)) + (players >= 5 ? 1 : 0);
            float delay = Sv(_cfgMarkDelay);
            float radius = MarkRadius(rt.Def, act);
            string effect = rt.Def.CfgMarkEffect != null ? Ss(rt.Def.CfgMarkEffect) : act.Prop;
            for (int k = 0; k < count && targets.Count > 0; k++)
            {
                int pick = UnityEngine.Random.Range(0, targets.Count);
                Player p = targets[pick];
                targets.RemoveAt(pick);
                Strike s = new Strike();
                s.Target = p.GetZDOID();
                s.At = now + delay;
                s.Act = act;
                rt.Strikes.Add(s);
                if (!string.IsNullOrEmpty(act.MarkKey)) Announce(act.MarkKey, NameToken(boss), p.GetPlayerName());
                SendFx(FxMark, s.Target, p.transform.position, radius, delay, effect);
                Debug(rt.Def.Prefab + ": marked " + p.GetPlayerName() + " (radius " + F1(radius) + ")");
            }
        }

        private string MarkPrefab(BossDef def, Act act)
        {
            if (act.Creature != null) return def.CfgMarkCreature != null ? Ss(def.CfgMarkCreature) : act.Creature;
            return def.CfgMarkPrefab != null ? Ss(def.CfgMarkPrefab) : act.Prefabs[0];
        }

        private float MarkRadius(BossDef def, Act act)
        {
            if (act.Creature != null) return act.RingRadius + 1f;
            GameObject pf = ZNetScene.instance.GetPrefab(MarkPrefab(def, act));
            Aoe aoe = pf != null ? pf.GetComponentInChildren<Aoe>(true) : null;
            if (aoe != null && aoe.m_radius > 0.1f) return aoe.m_radius;
            SpawnAbility sa = pf != null ? pf.GetComponentInChildren<SpawnAbility>(true) : null;
            if (sa != null && sa.m_spawnRadius > 0.1f) return sa.m_spawnRadius + 2f;   // meteors: where they fall + splash
            return 4f;
        }

        private void LandStrike(Character boss, FightRt rt, Strike s, int players)
        {
            GameObject target = ZNetScene.instance.FindInstance(s.Target);
            if (target == null) return;
            Player p = target.GetComponent<Player>();
            if (p == null || p.IsDead()) return;
            Vector3 pos = p.transform.position;
            string prefab = MarkPrefab(rt.Def, s.Act);

            if (s.Act.Creature != null)
            {
                // roots: a ring of creatures around where the player stands now
                int n = s.Act.CreatureCount;
                for (int k = 0; k < n; k++)
                    SpawnCreature(rt.BossId, prefab, AddLevel(rt.Def, s.Act.Level, players), 1f, pos, 0, s.Act.RingRadius, s.Act.RingRadius, 0, s.Act.Lifetime);
                return;
            }

            GameObject pf = ZNetScene.instance.GetPrefab(prefab);
            if (pf == null) { Warn(rt.Def.Prefab + ": mark prefab '" + prefab + "' not found."); return; }
            float dmg = (rt.Def.CfgMarkDamage != null ? Sv(rt.Def.CfgMarkDamage) : s.Act.Damage) * Sv(_cfgMarkDamage);
            GameObject go = UnityEngine.Object.Instantiate(pf, pos, Quaternion.identity);
            // a spawner of projectiles (Yagluth's meteors) drops them around itself - here, the
            // marked player - not at whoever is closest to the boss
            SpawnAbility[] sas = go.GetComponentsInChildren<SpawnAbility>(true);
            for (int i = 0; i < sas.Length; i++) sas[i].m_spawnAtTarget = false;
            // Aoe, SpawnAbility, Projectile: the boss as owner - its adds (same group) are friends
            IProjectile[] ps = go.GetComponentsInChildren<IProjectile>(true);
            for (int i = 0; i < ps.Length; i++) ps[i].Setup(boss, Vector3.zero, 0f, null, null, null);
            if (dmg > 0f)
            {
                Aoe[] aoes = go.GetComponentsInChildren<Aoe>(true);
                for (int i = 0; i < aoes.Length; i++) aoes[i].m_damage = DamageOf(s.Act.DamageType, dmg);
            }
            // a local-only prefab: show players with the mod a harmless copy
            if (pf.GetComponent<ZNetView>() == null) SendFx(FxStrike, ZDOID.None, pos, 0f, 0f, prefab);
        }

        private static HitData.DamageTypes DamageOf(HitData.DamageType type, float amount)
        {
            HitData.DamageTypes d = new HitData.DamageTypes();
            switch (type)
            {
                case HitData.DamageType.Blunt: d.m_blunt = amount; break;
                case HitData.DamageType.Slash: d.m_slash = amount; break;
                case HitData.DamageType.Pierce: d.m_pierce = amount; break;
                case HitData.DamageType.Fire: d.m_fire = amount; break;
                case HitData.DamageType.Frost: d.m_frost = amount; break;
                case HitData.DamageType.Poison: d.m_poison = amount; break;
                case HitData.DamageType.Spirit: d.m_spirit = amount; break;
                default: d.m_lightning = amount; break;
            }
            return d;
        }

        // ------------------------------------------------------------------
        // charge (Eikthyr): a burst of speed at the current target, like HardBosses
        // ------------------------------------------------------------------
        private void TickCharge(Character boss, FightRt rt, int mask, float now)
        {
            bool on = rt.Def.CfgChargeInterval != null && ActiveAct(rt.Def, mask, ActKind.Charge) != null && MechanicOn(rt.Def, ActKind.Charge);
            if (!on) { StopCharge(rt); return; }
            if (rt.Charging)
            {
                if (now >= rt.ChargeEnd) StopCharge(rt);
                return;
            }
            if (now < rt.NextCharge) return;
            MonsterAI ai = boss.GetComponent<MonsterAI>();
            Character target = ai != null ? ai.GetTargetCreature() : null;
            float dist = target != null ? Vector3.Distance(target.transform.position, boss.transform.position) : 0f;
            if (target == null || dist < 6f || dist > 40f) { rt.NextCharge = now + 2f; return; }

            float mul = Sv(rt.Def.CfgChargeSpeed);
            rt.BaseSpeed = boss.m_speed;
            rt.BaseRun = boss.m_runSpeed;
            boss.m_speed = rt.BaseSpeed * mul;
            boss.m_runSpeed = rt.BaseRun * mul;
            rt.Charging = true;
            rt.ChargeEnd = now + Sv(rt.Def.CfgChargeDuration);
            rt.NextCharge = now + Sv(rt.Def.CfgChargeInterval);
            Debug(rt.Def.Prefab + ": charge at " + target.m_name + " (" + F1(dist) + " m)");
        }

        private void StopCharge(FightRt rt)
        {
            if (rt == null || !rt.Charging) return;
            rt.Charging = false;
            if (rt.Boss != null)
            {
                rt.Boss.m_speed = rt.BaseSpeed;
                rt.Boss.m_runSpeed = rt.BaseRun;
            }
        }

        private void RestoreAllCharges()
        {
            foreach (FightRt rt in _fights.Values) StopCharge(rt);
        }

        // ------------------------------------------------------------------
        // reset (optional) and cleanup
        // ------------------------------------------------------------------
        private bool TickReset(Character boss, FightRt rt, int mask, float pct, float now)
        {
            if (!Sb(_cfgReset)) { rt.NoPlayersSince = -1f; return false; }
            bool started = mask != 0 || pct < 99.9f;
            if (!started || PlayersNear(boss.transform.position, Sv(_cfgResetRadius)).Count > 0) { rt.NoPlayersSince = -1f; return false; }
            if (rt.NoPlayersSince < 0f) { rt.NoPlayersSince = now; return false; }
            if (now - rt.NoPlayersSince < Sv(_cfgResetSeconds)) return false;
            ResetFight(boss, rt);
            return true;
        }

        private void ResetFight(Character boss, FightRt rt)
        {
            int removed = Cleanup(boss);
            ZDO z = Zdo(boss);
            if (z != null)
            {
                z.Set(KPhase, 0);
                z.Set(KShieldAct, 0);
                z.Set(KResistAct, 0);
                z.Set(KWindowUntil, 0L);
                z.Set(KCycleAct, 0);
                z.Set(KCycleStarted, 0);
                z.Set(KCycleUntil, 0L);
                z.Set(KCycleNext, 0L);
            }
            rt.CycleAct = null;
            boss.SetHealth(boss.GetMaxHealth());
            StopCharge(rt);
            rt.Strikes.Clear();
            rt.TotemNext.Clear();
            rt.Threat.Clear();
            rt.ThreatTarget = ZDOID.None;
            rt.NextMark = 0f;
            rt.NoPlayersSince = -1f;
            rt.ShieldActive = rt.WindowActive = false;
            rt.ResistAct = null;
            Debug(rt.Def.Prefab + ": fight reset, removed " + removed + " object(s)");
            Announce("reset", NameToken(boss));
        }

        // Removes the boss's adds and totems (and forgets the totem slots). Returns how many.
        internal int Cleanup(Character boss)
        {
            ZDOID bossId = boss.GetZDOID();
            int n = 0;
            List<Character> all = new List<Character>(Character.GetAllCharacters());
            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];
                if (c == null || c == boss) continue;
                ZDO z = Zdo(c);
                if (z == null || z.GetZDOID(KBoss) != bossId) continue;
                if (DestroyNetObject(c.gameObject)) n++;
            }
            ZDO bz = Zdo(boss);
            if (bz != null)
            {
                int slots = bz.GetInt(KTotems);
                for (int i = 0; i < slots; i++)
                {
                    GameObject t = ZNetScene.instance.FindInstance(bz.GetZDOID(TotemKey(i)));
                    if (t != null && DestroyNetObject(t)) n++;
                    bz.Set(TotemKey(i), ZDOID.None);
                }
                bz.Set(KTotems, 0);
            }
            return n;
        }

        internal static bool DestroyNetObject(GameObject go)
        {
            if (go == null) return false;
            ZNetView nv = go.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid()) return false;
            if (!nv.IsOwner()) nv.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
            return true;
        }

        internal int CountAdds(ZDOID bossId, int src)
        {
            int n = 0;
            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];
                if (c == null || c.IsDead()) continue;
                ZDO z = Zdo(c);
                if (z == null || z.GetZDOID(KBoss) != bossId) continue;
                if (src != 0 && z.GetInt(KSrc) != src) continue;
                n++;
            }
            return n;
        }

        internal static List<Player> PlayersNear(Vector3 center, float range)
        {
            List<Player> res = new List<Player>();
            List<Player> players = Player.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Player p = players[i];
                if (p == null || p.IsDead()) continue;
                Vector3 d = p.transform.position - center;
                d.y = 0f;
                if (d.magnitude <= range) res.Add(p);
            }
            return res;
        }

        // Boss in its own raid group while the mod runs it, back to the prefab's group otherwise.
        private void SetBossGroup(Character boss, bool mod)
        {
            if (mod) { boss.m_group = RaidGroup; return; }
            if (boss.m_group != RaidGroup) return;
            GameObject pf = ZNetScene.instance.GetPrefab(Zdo(boss).GetPrefab());
            Character pc = pf != null ? pf.GetComponent<Character>() : null;
            boss.m_group = pc != null ? pc.m_group : "";
        }

        // Server: adds and totems whose boss no longer exists (it died while they were unloaded,
        // the world was restarted mid-fight) are removed when they load.
        private System.Reflection.FieldInfo _instancesField;

        private void ScanOrphans()
        {
            ZNet znet = ZNet.instance;
            if (znet == null || !znet.IsServer() || ZDOMan.instance == null) return;
            if (_instancesField == null) _instancesField = HarmonyLib.AccessTools.Field(typeof(ZNetScene), "m_instances");
            Dictionary<ZDO, ZNetView> inst = _instancesField != null
                ? _instancesField.GetValue(ZNetScene.instance) as Dictionary<ZDO, ZNetView> : null;
            if (inst == null) return;
            List<GameObject> orphans = null;
            foreach (KeyValuePair<ZDO, ZNetView> kv in inst)
            {
                ZDO z = kv.Key;
                if (z == null || kv.Value == null) continue;
                ZDOID boss = z.GetZDOID(KBoss);
                if (boss == ZDOID.None || ZDOMan.instance.GetZDO(boss) != null) continue;
                if (orphans == null) orphans = new List<GameObject>();
                orphans.Add(kv.Value.gameObject);
            }
            if (orphans == null) return;
            for (int i = 0; i < orphans.Count; i++) DestroyNetObject(orphans[i]);
            Debug("removed " + orphans.Count + " orphaned add(s)/totem(s)");
        }
    }
}
