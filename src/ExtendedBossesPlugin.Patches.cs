using System;
using HarmonyLib;
using UnityEngine;

namespace ExtendedBosses
{
    public partial class ExtendedBossesPlugin
    {
        // ------------------------------------------------------------------
        // damage to the boss and its adds, on the owner
        // ------------------------------------------------------------------
        // Character.RPC_Damage runs on the owner of the struck creature; vanilla then divides the
        // damage by its own player scaling in ApplyDamage. For a boss we multiply by vanilla/ours
        // (so the result is our scaling alone), by the shield or the burn window, and per damage
        // type by the active resistance phase. Lieutenants carry an HP multiplier in their ZDO
        // and take damage divided by it (max HP is recomputed from the prefab on every load, so
        // a real SetMaxHealth would not survive). The postfix turns the HP actually lost into
        // threat for the attacking player.
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class Character_RPC_Damage_Patch
        {
            private static void Prefix(Character __instance, HitData hit, out float __state)
            {
                __state = -1f;
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
                        if (d != null && p.IsModMode(d))
                        {
                            p.RecordHit(__instance, hit);
                            mul *= p.BossDamageFactor(__instance, d);
                            p.ApplyResist(__instance, hit);
                            p.ApplyCycle(__instance, hit);
                            __state = __instance.GetHealth();
                        }
                    }
                    float hp = z.GetFloat(KHpMul, 1f);
                    if (hp > 0.01f && Mathf.Abs(hp - 1f) > 0.001f) mul /= hp;
                    if (Mathf.Abs(mul - 1f) > 0.001f) hit.ApplyModifier(mul);
                }
                catch (Exception e) { p.Fail("RPC_Damage", e); }
            }

            private static void Postfix(Character __instance, HitData hit, float __state)
            {
                if (__state < 0f) return;
                ExtendedBossesPlugin p = Instance;
                if (p == null || __instance == null || hit == null) return;
                try { p.AddThreat(__instance, hit.GetAttacker(), __state - __instance.GetHealth()); }
                catch (Exception e) { p.Fail("RPC_Damage threat", e); }
            }
        }

        // ------------------------------------------------------------------
        // the boss hits a player: Character.Damage runs on the attacker's side (the boss owner)
        // before the hit is sent to the player - bloodthirst reads the raw damage here
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(Character), "Damage")]
        private static class Character_Damage_Patch
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || !p.Active || __instance == null || hit == null || p._fights.Count == 0) return;
                try
                {
                    if (!__instance.IsPlayer()) return;
                    Character a = hit.GetAttacker();
                    if (a == null || !a.IsBoss() || !IsOwner(a)) return;
                    p.Lifesteal(a, hit);
                }
                catch (Exception e) { p.Fail("Character.Damage", e); }
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
                    FightRt rt = p.RtIfRunning(__instance);
                    if (rt != null) p.StopCharge(rt);
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

        // ------------------------------------------------------------------
        // self-check once the prefabs are known
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetScene_Awake_Patch
        {
            private static void Postfix()
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || p._disabledByErrors) return;
                try { p.SelfCheck(); } catch (Exception e) { p.Fail("self-check", e); }
            }
        }
    }
}
