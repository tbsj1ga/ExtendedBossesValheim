using System;
using HarmonyLib;
using UnityEngine;

namespace ExtendedBosses
{
    public partial class ExtendedBossesPlugin
    {
        // ------------------------------------------------------------------
        // effective HP: boss group scaling and lieutenant multipliers, on the owner
        // ------------------------------------------------------------------
        // Character.RPC_Damage runs on the owner of the struck creature; vanilla then divides the
        // damage by its own player scaling in ApplyDamage. We multiply by vanilla/ours, so the
        // result is our scaling alone. Lieutenants carry an HP multiplier in their ZDO and take
        // damage divided by it (their max HP is recomputed from the prefab on every load, so a
        // real SetMaxHealth would not survive).
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class Character_RPC_Damage_Patch
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || !p.Active || __instance == null || hit == null) return;
                try
                {
                    if (__instance.IsPlayer() || !IsOwner(__instance)) return;
                    ZDO z = Zdo(__instance);
                    if (z == null) return;
                    float mul = 1f;
                    if (__instance.IsBoss())
                    {
                        BossDef d = p.BossOf(z);
                        if (d != null && p.IsModMode(d)) mul *= p.BossDamageFactor(__instance, d);
                    }
                    float hp = z.GetFloat(KHpMul, 1f);
                    if (hp > 1.001f) mul /= hp;
                    if (Mathf.Abs(mul - 1f) > 0.001f) hit.ApplyModifier(mul);
                }
                catch (Exception e) { p.Fail("RPC_Damage", e); }
            }
        }

        // ------------------------------------------------------------------
        // the raid group on every client, for adds loaded from the world
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(Character), "Awake")]
        private static class Character_Awake_Patch
        {
            private static void Postfix(Character __instance)
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || p._disabledByErrors || __instance == null) return;
                try
                {
                    ZDO z = Zdo(__instance);
                    if (z != null && z.GetZDOID(KBoss) != ZDOID.None) __instance.m_group = RaidGroup;
                }
                catch (Exception e) { p.Fail("Character.Awake", e); }
            }
        }

        // ------------------------------------------------------------------
        // boss death: group reward on top of the vanilla drop, adds and nests removed
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(Character), "OnDeath")]
        private static class Character_OnDeath_Patch
        {
            private static void Prefix(Character __instance)
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || !p.Active || __instance == null || !__instance.IsBoss()) return;
                try
                {
                    if (!IsOwner(__instance)) return;
                    BossDef d = p.BossOf(Zdo(__instance));
                    if (d == null || !p.IsModMode(d)) return;
                    FightRt rt;
                    if (p._fights.TryGetValue(__instance.GetZDOID(), out rt)) p.StopCharge(rt);
                    p.DropRewards(__instance, d);
                    if (p.Sb(p._cfgCleanupOnDeath))
                    {
                        int n = p.Cleanup(__instance);
                        p.Debug(d.Prefab + " died: removed " + n + " add(s)/nest(s)");
                    }
                }
                catch (Exception e) { p.Fail("OnDeath", e); }
            }
        }
    }
}
