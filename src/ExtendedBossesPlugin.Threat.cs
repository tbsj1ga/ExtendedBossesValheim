using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ExtendedBosses
{
    // Threat table, on the boss owner. Damage to the boss adds threat to the
    // attacking player; threat fades over time and fast outside the leash radius. The boss
    // targets the highest threat among players inside the leash, switches only past a margin and
    // holds a new target for a while - no ping-pong, and "hit, run away, come back first" does
    // not work. Nobody inside the leash: vanilla target choice.
    //
    // The WeaponArts taunt patches the same MonsterAI.UpdateTarget postfix; ours runs FIRST so
    // the taunt, running after, always wins for its window.
    public partial class ExtendedBossesPlugin
    {
        private static System.Reflection.MethodInfo _miSetTarget;
        private readonly List<ZDOID> _tmpThreatIds = new List<ZDOID>();

        private void TickThreat(Character boss, FightRt rt)
        {
            if (!ThreatOn(rt.Def)) { rt.Threat.Clear(); rt.ThreatTarget = ZDOID.None; return; }
            if (rt.Threat.Count == 0) return;
            float dt = TickInterval;
            float keep = Mathf.Clamp01(1f - Sv(_cfgThreatDecay) * dt);
            float outKeep = Mathf.Pow(0.5f, dt / Mathf.Max(0.1f, Sv(_cfgOutOfLeashHalfLife)));
            float leash = Sv(_cfgLeash);
            Vector3 bp = boss.transform.position;
            _tmpThreatIds.Clear();
            foreach (ZDOID id in rt.Threat.Keys) _tmpThreatIds.Add(id);
            for (int i = 0; i < _tmpThreatIds.Count; i++)
            {
                ZDOID id = _tmpThreatIds[i];
                float v = rt.Threat[id] * keep;
                Player p = PlayerById(id);
                if (p == null || p.IsDead() || Flat(p.transform.position - bp) > leash) v *= outKeep;
                if (v < 0.5f) rt.Threat.Remove(id); else rt.Threat[id] = v;
            }
        }

        internal void AddThreat(Character boss, Character attacker, float amount)
        {
            if (amount <= 0f || attacker == null || !attacker.IsPlayer()) return;
            FightRt rt = RtIfRunning(boss);
            if (rt == null || !ThreatOn(rt.Def)) return;
            ZDOID id = attacker.GetZDOID();
            float v;
            rt.Threat.TryGetValue(id, out v);
            rt.Threat[id] = v + amount;
        }

        private void PickThreatTarget(MonsterAI ai, Character boss)
        {
            FightRt rt = RtIfRunning(boss);
            if (rt == null || !IsModMode(rt.Def)) return;
            if (HoldFixation(ai, rt)) return;                       // a fixation beats the threat table
            if (!ThreatOn(rt.Def) || rt.Threat.Count == 0) return;
            float leash = Sv(_cfgLeash);
            Vector3 bp = boss.transform.position;

            Player best = null;
            float bestV = 0f;
            foreach (KeyValuePair<ZDOID, float> kv in rt.Threat)
            {
                Player p = PlayerById(kv.Key);
                if (p == null || p.IsDead() || Flat(p.transform.position - bp) > leash) continue;
                if (kv.Value > bestV) { bestV = kv.Value; best = p; }
            }
            if (best == null) { rt.ThreatTarget = ZDOID.None; return; }   // nobody holds it: vanilla

            float now = Time.time;
            Player cur = rt.ThreatTarget != ZDOID.None ? PlayerById(rt.ThreatTarget) : null;
            bool curOk = cur != null && !cur.IsDead() && Flat(cur.transform.position - bp) <= leash;
            if (!curOk)
            {
                cur = best;
                rt.ThreatTarget = best.GetZDOID();
                rt.ThreatSince = now;
            }
            else if (cur != best && now - rt.ThreatSince >= Sv(_cfgHoldSeconds))
            {
                float curV;
                rt.Threat.TryGetValue(rt.ThreatTarget, out curV);
                if (bestV > curV * (1f + Sv(_cfgSwitchMargin)))
                {
                    cur = best;
                    rt.ThreatTarget = best.GetZDOID();
                    rt.ThreatSince = now;
                }
            }
            if (ai.GetTargetCreature() != cur) SetTarget(ai, cur);
        }

        private void SetTarget(MonsterAI ai, Character target)
        {
            if (_miSetTarget == null) _miSetTarget = AccessTools.Method(typeof(MonsterAI), "SetTarget", new Type[] { typeof(Character) });
            if (_miSetTarget == null) { Warn("MonsterAI.SetTarget not found; the threat table is inert."); return; }
            _miSetTarget.Invoke(ai, new object[] { target });
        }

        private static Player PlayerById(ZDOID id)
        {
            if (id == ZDOID.None || ZNetScene.instance == null) return null;
            GameObject go = ZNetScene.instance.FindInstance(id);
            return go != null ? go.GetComponent<Player>() : null;
        }

        private static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }

        [HarmonyPatch(typeof(MonsterAI), "UpdateTarget")]
        private static class MonsterAI_UpdateTarget_Patch
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(MonsterAI __instance)
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || !p.Active || __instance == null || p._fights.Count == 0) return;
                try
                {
                    Character c = __instance.GetComponent<Character>();
                    if (c == null || !c.IsBoss()) return;
                    p.PickThreatTarget(__instance, c);
                }
                catch (Exception e) { p.Fail("UpdateTarget", e); }
            }
        }
    }
}
