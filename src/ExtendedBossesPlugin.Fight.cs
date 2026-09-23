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
            TickTotems(boss, rt, z, players, now);
            TickShield(boss, rt, z, net);
            TickHeal(boss, rt);
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
                    for (int p = 0; p < act.Prefabs.Length; p++)
                        for (int k = 0; k < count; k++)
                            if (SpawnCreature(rt.BossId, act.Prefabs[p], level, act.HpMul, center, 0, Sv(_cfgSpawnRadiusMin), Sv(_cfgSpawnRadiusMax), act.Role, act.Lifetime) != null) spawned++;
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
                    int count = NestCount(act.Count, players);
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
                case ActKind.Shield:
                    z.Set(KShieldAct, code + 1);
                    return true;
                case ActKind.Resist:
                    z.Set(KResistAct, code + 1);
                    z.Set(KResistUntil, act.Duration > 0f ? NetTicks() + Seconds(act.Duration) : 0L);
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
                if (z.GetInt(KRole) == RoleHealer && Vector3.Distance(c.transform.position, bp) <= healRange) healers++;
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
                    z.Set(KWindowUntil, net + Seconds(Sv(_cfgWindowSeconds)));
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
                Warn(rt.Def.Prefab + ": totem prefab '" + prefab + "' is missing or not a destructible network object; using BonePileSpawner (vanilla bone pile) instead.");
                prefab = "BonePileSpawner";
                pf = ZNetScene.instance.GetPrefab(prefab);
                vanillaNest = true;
            }
            if (!IsDestructibleProp(pf)) { Warn(rt.Def.Prefab + ": nest prefab '" + prefab + "' is missing or not destructible."); return false; }

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
            return aoe != null && aoe.m_radius > 0.1f ? aoe.m_radius : 4f;
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
            Aoe[] aoes = go.GetComponentsInChildren<Aoe>(true);
            for (int i = 0; i < aoes.Length; i++)
            {
                aoes[i].Setup(boss, Vector3.zero, 0f, null, null, null);   // boss as owner: its adds (same group) are friends
                aoes[i].m_damage = DamageOf(s.Act.DamageType, dmg);
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
            }
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
