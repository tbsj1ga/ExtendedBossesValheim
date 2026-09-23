using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ExtendedBosses
{
    // A cap on the crowd around a boss: every monster within AddsCapRadius of it counts (the
    // boss's own adds and whatever its vanilla nests spawned, which are not tagged), not the boss
    // and not tamed animals. At the cap nests, totems, waves and slime spawn nothing more;
    // lieutenants, cocoon guards and roots are part of a mechanic and always come.
    public partial class ExtendedBossesPlugin
    {
        private ConfigEntry<int> _cfgCapBase;
        private ConfigEntry<int> _cfgCapPerPlayer;
        private ConfigEntry<float> _cfgCapRadius;

        private void BindCapConfig()
        {
            const string Rd = "09 Raid";
            _cfgCapBase = I(Rd, "AddsCapSolo", 8, 1, 100,
                "At most this many monsters around a boss for a single player (nests, totems, waves and slime stop at it).",
                "Не больше стольких монстров вокруг босса для одного игрока (гнёзда, тотемы, волны и слизь на этом останавливаются).");
            _cfgCapPerPlayer = I(Rd, "AddsCapPerPlayer", 3, 0, 50,
                "The cap grows by this for every player beyond the first.", "Лимит растёт на столько за каждого игрока сверх первого.");
            _cfgCapRadius = F(Rd, "AddsCapRadius", 50f, 10f, 150f,
                "Monsters within this distance of the boss count towards the cap.", "Монстры ближе этого расстояния к боссу считаются в лимит.");
        }

        internal int AddsCap(Character boss)
        {
            return Si(_cfgCapBase) + Si(_cfgCapPerPlayer) * (GroupSize(boss) - 1);
        }

        internal int MonstersNear(Character boss)
        {
            float r = Sv(_cfgCapRadius);
            Vector3 bp = boss.transform.position;
            int n = 0;
            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];
                if (c == null || c == boss || c.IsPlayer() || c.IsDead() || c.IsTamed() || c.IsBoss()) continue;
                if (c.GetBaseAI() == null) continue;
                if (Flat(c.transform.position - bp) <= r) n++;
            }
            return n;
        }

        internal bool AtCap(Character boss)
        {
            return MonstersNear(boss) >= AddsCap(boss);
        }

        // Vanilla nests placed by a boss stop spawning at the cap. SpawnOne runs on the nest's
        // owner; the boss must be loaded there (it stands next to its nests).
        private bool NestMaySpawn(SpawnArea area)
        {
            ZDO z = Zdo(area);
            if (z == null) return true;
            ZDOID bossId = z.GetZDOID(KBoss);
            if (bossId == ZDOID.None) return true;      // not ours: vanilla behaviour
            GameObject go = ZNetScene.instance.FindInstance(bossId);
            Character boss = go != null ? go.GetComponent<Character>() : null;
            if (boss == null || boss.IsDead()) return true;
            BossDef def = BossOf(Zdo(boss));
            if (def == null || !IsModMode(def)) return true;
            return !AtCap(boss);
        }

        [HarmonyPatch(typeof(SpawnArea), "SpawnOne")]
        private static class SpawnArea_SpawnOne_Patch
        {
            private static bool Prefix(SpawnArea __instance, ref bool __result)
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || !p.Active || __instance == null) return true;
                try
                {
                    if (p.NestMaySpawn(__instance)) return true;
                    __result = false;
                    return false;
                }
                catch (Exception e) { p.Fail("SpawnArea.SpawnOne", e); return true; }
            }
        }
    }
}
