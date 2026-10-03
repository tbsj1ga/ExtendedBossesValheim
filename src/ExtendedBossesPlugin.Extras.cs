using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ExtendedBosses
{
    // The smaller mechanics: fixation (Moder's breath mark, Yagluth's beam), hazards (Fader's wall
    // of fire), the Queen's cocoon after a teleport, status effects on the boss's hits (Wet, Tared)
    // and the Elder's seeds (a nest where his projectile lands). All of it on the boss owner,
    // with vanilla prefabs and vanilla hits only.
    public partial class ExtendedBossesPlugin
    {
        // the act of this kind from the LAST fired phase (later phases override earlier ones)
        private Act ActiveActLast(BossDef def, int mask, ActKind kind)
        {
            for (int i = Mathf.Min(def.Phases.Count, 31) - 1; i >= 0; i--)
            {
                if ((mask & (1 << i)) == 0) continue;
                List<Act> acts = def.Phases[i].Acts;
                for (int a = 0; a < acts.Count; a++) if (acts[a].Kind == kind) return acts[a];
            }
            return null;
        }

        // ------------------------------------------------------------------
        // fixation: the boss takes a marked player as its target for a while (its vanilla
        // attacks - breath, beam - then go at that player); a flying boss lands first
        // ------------------------------------------------------------------
        private void TickFixate(Character boss, FightRt rt, int mask, float now)
        {
            Act act = ActiveAct(rt.Def, mask, ActKind.Fixate);
            if (act == null || !MechanicOn(rt.Def, ActKind.Fixate)) { rt.FixTarget = ZDOID.None; return; }
            if (rt.FixTarget != ZDOID.None && now >= rt.FixUntil) rt.FixTarget = ZDOID.None;
            if (rt.NextFix <= 0f) rt.NextFix = now + 5f;
            if (now < rt.NextFix) return;
            rt.NextFix = now + act.Interval * (ProfileOf(rt.Def) == ProfileHard ? 0.75f : 1f);

            List<Player> targets = PlayersNear(boss.transform.position, Sv(_cfgScalingRange));
            if (targets.Count == 0) return;
            Player p = targets[UnityEngine.Random.Range(0, targets.Count)];
            rt.FixTarget = p.GetZDOID();
            rt.FixUntil = now + act.Duration;
            if (act.Land && boss.IsFlying()) boss.Land();
            if (!string.IsNullOrEmpty(act.MarkKey)) Announce(act.MarkKey, NameToken(boss), p.GetPlayerName());
            SendFx(FxMark, rt.FixTarget, p.transform.position, 2f, act.Duration, null);
            Debug(rt.Def.Prefab + ": fixated on " + p.GetPlayerName() + " for " + F1(act.Duration) + " s");
        }

        // Called first from the UpdateTarget postfix: an active fixation beats the threat table.
        private bool HoldFixation(MonsterAI ai, FightRt rt)
        {
            if (rt.FixTarget == ZDOID.None || Time.time >= rt.FixUntil) return false;
            Player p = PlayerById(rt.FixTarget);
            if (p == null || p.IsDead()) { rt.FixTarget = ZDOID.None; return false; }
            if (ai.GetTargetCreature() != p) SetTarget(ai, p);
            return true;
        }

        // ------------------------------------------------------------------
        // hazard: a vanilla spawner/AoE raised from the boss every Interval (Fader's wall of fire)
        // ------------------------------------------------------------------
        private void TickHazard(Character boss, FightRt rt, int mask, float now)
        {
            Act act = ActiveAct(rt.Def, mask, ActKind.Hazard);
            if (act == null || !MechanicOn(rt.Def, ActKind.Hazard)) return;
            if (rt.NextHazard <= 0f) rt.NextHazard = now + 5f;
            if (now < rt.NextHazard) return;
            rt.NextHazard = now + act.Interval * (ProfileOf(rt.Def) == ProfileHard ? 0.75f : 1f);

            GameObject pf = ZNetScene.instance.GetPrefab(act.Prefabs[0]);
            if (pf == null) { Warn(rt.Def.Prefab + ": hazard prefab '" + act.Prefabs[0] + "' not found."); return; }
            // faces a random player, so a wall is raised between the boss and part of the raid
            Quaternion rot = boss.transform.rotation;
            List<Player> players = PlayersNear(boss.transform.position, Sv(_cfgScalingRange));
            if (players.Count > 0)
            {
                Vector3 d = players[UnityEngine.Random.Range(0, players.Count)].transform.position - boss.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f) rot = Quaternion.LookRotation(d.normalized);
            }
            GameObject go = UnityEngine.Object.Instantiate(pf, boss.transform.position, rot);
            IProjectile[] ps = go.GetComponentsInChildren<IProjectile>(true);
            for (int i = 0; i < ps.Length; i++) ps[i].Setup(boss, rot * Vector3.forward, 0f, null, null, null);
            if (!string.IsNullOrEmpty(act.MarkKey)) Announce(act.MarkKey, NameToken(boss));
            Debug(rt.Def.Prefab + ": hazard " + act.Prefabs[0]);
        }

        // ------------------------------------------------------------------
        // cocoon (Queen): after a teleport of hers guards appear; while they live (at most
        // Duration) she is shielded; at most once per Interval
        // ------------------------------------------------------------------
        private void TickCocoon(Character boss, FightRt rt, int mask, int players, float now)
        {
            Vector3 pos = boss.transform.position;
            bool jumped = rt.HasLastPos && Flat(pos - rt.LastPos) > 12f;
            rt.LastPos = pos;
            rt.HasLastPos = true;

            Act act = ActiveAct(rt.Def, mask, ActKind.Cocoon);
            bool on = act != null && MechanicOn(rt.Def, ActKind.Cocoon);
            rt.CocoonActive = on && rt.Guards > 0 && now < rt.CocoonUntil;
            if (!on || !jumped || now < rt.CocoonReady) return;

            rt.CocoonReady = now + act.Interval;
            rt.CocoonUntil = now + act.Duration;
            int count = ScaledCount(rt.Def, act.Count, players);
            int level = AddLevel(rt.Def, act.Level, players);
            for (int p = 0; p < act.Prefabs.Length; p++)
                for (int k = 0; k < count; k++)
                    SpawnCreature(rt.BossId, act.Prefabs[p], level, 1f, pos, 0, 3f, 7f, RoleGuard, 0f);
            rt.Guards = count * act.Prefabs.Length;
            rt.CocoonActive = true;
            if (!string.IsNullOrEmpty(act.MarkKey)) Announce(act.MarkKey, NameToken(boss));
            Debug(rt.Def.Prefab + ": teleported, cocoon for up to " + F1(act.Duration) + " s");
        }

        // ------------------------------------------------------------------
        // status effect on the boss's hits (Wet, Tared): put into the HitData on the boss owner
        // before it is sent to the player, who applies it by hash like any vanilla effect
        // ------------------------------------------------------------------
        internal void ApplyHitEffect(Character boss, HitData hit)
        {
            FightRt rt = RtIfRunning(boss);
            if (rt == null || hit.m_statusEffectHash != 0) return;      // never override the attack's own effect
            ZDO z = Zdo(boss);
            if (z == null) return;
            Act act = ActiveActLast(rt.Def, z.GetInt(KPhase), ActKind.HitEffect);
            if (act == null || string.IsNullOrEmpty(act.Effect) || !MechanicOn(rt.Def, ActKind.HitEffect)) return;
            hit.m_statusEffectHash = act.Effect.GetStableHashCode();
        }

        // ------------------------------------------------------------------
        // seeds (Elder): where a projectile of the boss lands, a nest may grow
        // ------------------------------------------------------------------
        private static System.Reflection.FieldInfo _fiProjectileOwner;

        private void OnProjectileHit(Projectile proj, Vector3 point)
        {
            if (_fiProjectileOwner == null) _fiProjectileOwner = AccessTools.Field(typeof(Projectile), "m_owner");
            if (_fiProjectileOwner == null) return;
            Character owner = _fiProjectileOwner.GetValue(proj) as Character;
            if (owner == null || !owner.IsBoss() || owner.IsDead()) return;
            FightRt rt = RtIfRunning(owner);
            if (rt == null || !IsModMode(rt.Def)) return;
            ZDO bz = Zdo(owner);
            if (bz == null) return;
            int mask = bz.GetInt(KPhase);
            for (int i = 0; i < rt.Def.Phases.Count && i < 31; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                List<Act> acts = rt.Def.Phases[i].Acts;
                for (int a = 0; a < acts.Count; a++)
                {
                    Act act = acts[a];
                    if (act.Kind != ActKind.Seeds || !MechanicOn(rt.Def, ActKind.Seeds)) continue;
                    if (Time.time < rt.NextSeed || SeedsAlive(bz) >= act.MaxAlive) return;
                    if (UnityEngine.Random.value >= act.Chance) return;
                    rt.NextSeed = Time.time + act.Interval;
                    Vector3 at = RingPoint(point, 0f, 4f);   // open ground near where it landed, not on a tree it hit
                    if (PlaceTotem(owner, rt, act, i, a, at))
                    {
                        if (!string.IsNullOrEmpty(act.MarkKey)) Announce(act.MarkKey, NameToken(owner));
                        Debug(rt.Def.Prefab + ": a seed grew into a nest");
                    }
                    return;
                }
            }
        }

        [HarmonyPatch(typeof(Projectile), "OnHit")]
        private static class Projectile_OnHit_Patch
        {
            private static void Postfix(Projectile __instance, Vector3 hitPoint)
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || !p.Active || __instance == null || p._fights.Count == 0) return;
                try { p.OnProjectileHit(__instance, hitPoint); }
                catch (Exception e) { p.Fail("Projectile.OnHit", e); }
            }
        }
    }
}
