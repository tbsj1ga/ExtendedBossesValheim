using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExtendedBosses
{
    // The fight controller. Runs every TickInterval on the client that OWNS a boss (with
    // HostOwner that is the host). Persistent state is in the boss ZDO (phases fired, the
    // totems it placed); timers that only matter while we simulate the boss live in FightRt and
    // are simply rebuilt if the boss changes hands.
    public partial class ExtendedBossesPlugin
    {
        // ZDO keys
        internal static readonly int KPhase = "j1ga.extendedbosses.phase".GetStableHashCode();   // boss: int bitmask of fired phases
        internal static readonly int KTotems = "j1ga.extendedbosses.totems".GetStableHashCode(); // boss: number of totem slots
        internal const string KBoss = "j1ga.extendedbosses.boss";                                 // add/totem: ZDOID of its boss
        internal static readonly int KHpMul = "j1ga.extendedbosses.hpmul".GetStableHashCode();   // add: effective HP multiplier
        internal static readonly int KSrc = "j1ga.extendedbosses.src".GetStableHashCode();       // add: totem slot + 1 (0 = none)

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
        }

        private readonly Dictionary<ZDOID, FightRt> _fights = new Dictionary<ZDOID, FightRt>();
        private readonly List<Character> _tmpBosses = new List<Character>();
        private readonly List<ZDOID> _tmpIds = new List<ZDOID>();
        private int _forcePlayers;             // 'eb players <n>' for testing; 0 = count

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

        private void TickBoss(Character boss, float now)
        {
            ZDO z = Zdo(boss);
            BossDef def = BossOf(z);
            FightRt rt = Rt(boss, def);

            if (!IsModMode(def))
            {
                StopCharge(rt);
                rt.Strikes.Clear();
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

            TickTotems(boss, rt, z, players, now);
            TickMarks(boss, rt, mask, players, now);
            TickCharge(boss, rt, mask, now);
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
            if (ran > 0 && !string.IsNullOrEmpty(ph.Say)) Announce(ph.Say);
        }

        private bool RunAct(Character boss, FightRt rt, Act act, int phase, int index, int players, float now)
        {
            Vector3 center = boss.transform.position;
            switch (act.Kind)
            {
                case ActKind.Wave:
                {
                    int count = ScaledCount(rt.Def, act.Count, players);
                    int level = AddLevel(rt.Def, act.Level, players);
                    int spawned = 0;
                    for (int p = 0; p < act.Prefabs.Length; p++)
                        for (int k = 0; k < count; k++)
                            if (SpawnCreature(rt.BossId, act.Prefabs[p], level, act.HpMul, center, 0) != null) spawned++;
                    return spawned > 0;
                }
                case ActKind.Lieutenant:
                {
                    int count = Mathf.Max(1, Mathf.RoundToInt(act.Count));
                    int level = LieutenantLevel(act.Level, players);
                    int spawned = 0;
                    for (int p = 0; p < act.Prefabs.Length; p++)
                        for (int k = 0; k < count; k++)
                            if (SpawnCreature(rt.BossId, act.Prefabs[p], level, act.HpMul, center, 0) != null) spawned++;
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
            }
            return false;
        }

        // ------------------------------------------------------------------
        // scaling
        // ------------------------------------------------------------------
        internal int GroupSize(Character boss)
        {
            int max = Mathf.Max(1, Si(_cfgMaxScalingPlayers));
            if (_forcePlayers > 0) return Mathf.Min(_forcePlayers, max);
            float range = Sv(_cfgScalingRange);
            Vector3 c = boss.transform.position;
            int n = 0;
            List<Player> players = Player.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Player p = players[i];
                if (p == null || p.IsDead()) continue;
                Vector3 d = p.transform.position - c;
                d.y = 0f;
                if (d.magnitude <= range) n++;
            }
            return Mathf.Clamp(n, 1, max);
        }

        // Multiplier of damage taken by a boss, replacing the vanilla player scaling (which
        // ApplyDamage still divides by afterwards) with ours.
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
            float ours = Sv(_cfgBaseHealth) * (1f + Sv(_cfgHealthPerPlayer) * (n - 1));
            if (ProfileOf(def) == ProfileHard) ours *= Sv(_cfgHardHealth);
            if (ours < 0.01f) ours = 0.01f;
            return vanillaHp / ours;
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
        // totems and nests
        // ------------------------------------------------------------------
        private bool PlaceTotem(Character boss, FightRt rt, Act act, int phase, int index)
        {
            string prefab = act.Kind == ActKind.Totem ? act.Prop : act.Prefabs[0];
            if (act.Kind == ActKind.Totem && rt.Def.CfgTotemPrefab != null) prefab = Ss(rt.Def.CfgTotemPrefab);
            bool vanillaNest = act.Kind == ActKind.Nest;

            GameObject pf = ZNetScene.instance.GetPrefab(prefab);
            if (act.Kind == ActKind.Totem && !IsDestructibleProp(pf))
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

        private static bool IsDestructibleProp(GameObject pf)
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
                int phase = code / 8, index = code % 8;
                if (phase < 0 || phase >= rt.Def.Phases.Count || index >= rt.Def.Phases[phase].Acts.Count) continue;
                Act act = rt.Def.Phases[phase].Acts[index];

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
                SpawnCreature(rt.BossId, prefab, AddLevel(rt.Def, act.Level, players), act.HpMul, totem.transform.position, i + 1, 1.5f, 4f);
            }
        }

        // ------------------------------------------------------------------
        // marks: "spread out" - a strike on a marked player after a delay
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
                try { LandStrike(boss, rt, s); }
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
                Announce(rt.Def.Title + " отметил " + p.GetPlayerName() + " — отойдите от него!");
                SendFx(FxMark, s.Target, p.transform.position, radius, delay, effect);
                Debug(rt.Def.Prefab + ": marked " + p.GetPlayerName() + " (radius " + F1(radius) + ")");
            }
        }

        private string MarkPrefab(BossDef def, Act act)
        {
            return def.CfgMarkPrefab != null ? Ss(def.CfgMarkPrefab) : act.Prefabs[0];
        }

        private float MarkRadius(BossDef def, Act act)
        {
            GameObject pf = ZNetScene.instance.GetPrefab(MarkPrefab(def, act));
            Aoe aoe = pf != null ? pf.GetComponentInChildren<Aoe>(true) : null;
            return aoe != null && aoe.m_radius > 0.1f ? aoe.m_radius : 4f;
        }

        private void LandStrike(Character boss, FightRt rt, Strike s)
        {
            GameObject target = ZNetScene.instance.FindInstance(s.Target);
            if (target == null) return;
            Player p = target.GetComponent<Player>();
            if (p == null || p.IsDead()) return;

            string prefab = MarkPrefab(rt.Def, s.Act);
            GameObject pf = ZNetScene.instance.GetPrefab(prefab);
            if (pf == null) { Warn(rt.Def.Prefab + ": mark prefab '" + prefab + "' not found."); return; }
            Vector3 pos = p.transform.position;

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
            if (z != null) z.Set(KPhase, 0);
            boss.SetHealth(boss.GetMaxHealth());
            StopCharge(rt);
            rt.Strikes.Clear();
            rt.TotemNext.Clear();
            rt.NextMark = 0f;
            rt.NoPlayersSince = -1f;
            Debug(rt.Def.Prefab + ": fight reset, removed " + removed + " object(s)");
            Announce(rt.Def.Title + " восстанавливает силы…");
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
            System.Collections.Generic.Dictionary<ZDO, ZNetView> inst = _instancesField != null
                ? _instancesField.GetValue(ZNetScene.instance) as System.Collections.Generic.Dictionary<ZDO, ZNetView> : null;
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

        // ------------------------------------------------------------------
        // announcements
        // ------------------------------------------------------------------
        internal void Announce(string text)
        {
            string mode = Ss(_cfgAnnounce);
            if (mode == "Off" || string.IsNullOrEmpty(text)) return;
            if (mode == "Chat")
            {
                if (Chat.instance != null) Chat.instance.SendText(Talker.Type.Normal, text);
                return;
            }
            // the same routed RPC MessageHud.MessageAll sends; vanilla clients show it too
            if (ZRoutedRpc.instance != null)
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ShowMessage", new object[] { (int)MessageHud.MessageType.Center, text });
        }
    }
}
