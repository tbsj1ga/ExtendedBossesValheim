using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ExtendedBosses
{
    // A cap on the crowd around a boss: every monster within AddsCapRadius of it counts (the
    // boss's own adds and whatever its vanilla nests, eggs and summons spawned, which are not
    // tagged), not the boss and not tamed animals. At the cap nests, totems, waves, slime,
    // cocoon guards, eggs and the boss's own vanilla creature summons spawn nothing more;
    // lieutenants and roots are part of a mechanic and always come. Far past the cap the
    // oldest of the boss's adds leave (TrimCrowd).
    public partial class ExtendedBossesPlugin
    {
        private ConfigEntry<int> _cfgCapBase;
        private ConfigEntry<int> _cfgCapPerPlayer;
        private ConfigEntry<float> _cfgCapRadius;
        private ConfigEntry<float> _cfgTrimAt;
        private ConfigEntry<float> _cfgWaveLifetime;

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
            _cfgTrimAt = F(Rd, "AddsTrimAt", 1.5f, 1.1f, 10f,
                "When the monsters around a boss reach the cap times this, the oldest of its own adds leave until the cap (lieutenants stay, vanilla monsters are never removed).",
                "Когда монстров вокруг босса больше лимита во столько раз, самые старые из его аддов исчезают до лимита (лейтенанты остаются, ванильных монстров не трогает).");
            _cfgWaveLifetime = F(Rd, "WaveLifetime", 90f, 0f, 3600f,
                "Seconds an add of a wave lives if nobody kills it (0 = until killed).",
                "Сколько секунд живёт адд волны, если его не убили (0 — пока не убьют).");
        }

        internal int AddsCap(Character boss)
        {
            BossDef def = BossOf(Zdo(boss));
            int solo = def != null && def.CfgCapSolo != null ? Si(def.CfgCapSolo) : Si(_cfgCapBase);
            int per = def != null && def.CfgCapPerPlayer != null ? Si(def.CfgCapPerPlayer) : Si(_cfgCapPerPlayer);
            return solo + per * (GroupSize(boss) - 1);
        }

        // room left under the cap (0 at or past it)
        internal int CapRoom(Character boss)
        {
            return Mathf.Max(0, AddsCap(boss) - MonstersNear(boss));
        }

        // the boss of a nest, egg or summon, if it is one of ours fighting in mod mode
        private Character ModBoss(ZDOID bossId)
        {
            if (bossId == ZDOID.None || ZNetScene.instance == null) return null;
            GameObject go = ZNetScene.instance.FindInstance(bossId);
            Character boss = go != null ? go.GetComponent<Character>() : null;
            if (boss == null || boss.IsDead()) return null;
            BossDef def = BossOf(Zdo(boss));
            return def != null && IsModMode(def) ? boss : null;
        }

        internal float EggHatchSeconds(BossDef def)
        {
            return def.CfgEggHatch != null ? Sv(def.CfgEggHatch) : 40f;
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
            ZDOID bossId = z.GetZDOID(KBossPair);
            if (bossId == ZDOID.None) return true;      // not ours: vanilla behaviour
            Character boss = ModBoss(bossId);
            return boss == null || !AtCap(boss);
        }

        // An egg laid by a boss hatches on its timer, not when a player steps close (an egg
        // nobody saw would otherwise burst under the feet of the melee), and not at the cap.
        // CheckSpawn runs every second on the egg's owner.
        private static System.Reflection.MethodInfo _miHatch;

        private bool EggCheck(EggHatch egg)
        {
            ZNetView nv = egg.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid()) return true;
            ZDO z = nv.GetZDO();
            long at = z.GetLong(KHatchAt, 0L);
            if (at == 0L) return true;                  // not ours: vanilla behaviour
            if (!nv.IsOwner() || NetTicks() < at) return false;
            Character boss = ModBoss(z.GetZDOID(KBossPair));
            if (boss != null && AtCap(boss)) return false;
            if (_miHatch == null) _miHatch = AccessTools.Method(typeof(EggHatch), "Hatch");
            if (_miHatch == null) return true;
            _miHatch.Invoke(egg, null);
            return false;
        }

        [HarmonyPatch(typeof(EggHatch), "CheckSpawn")]
        private static class EggHatch_CheckSpawn_Patch
        {
            private static bool Prefix(EggHatch __instance)
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || !p.Active || __instance == null) return true;
                try { return p.EggCheck(__instance); }
                catch (Exception e) { p.Fail("EggHatch.CheckSpawn", e); return true; }
            }
        }

        // A boss's own vanilla summon (the Queen calling her brood) does nothing at the cap, for
        // bosses with CapSummons. Only abilities that spawn creatures or eggs are stopped -
        // meteors, spit and walls of fire go on (and the Elder's roots are not capped at all).
        private bool SummonAllowed(SpawnAbility ab, Character owner)
        {
            if (owner == null || !owner.IsBoss() || ab.m_spawnPrefab == null) return true;
            FightRt rt = RtIfRunning(owner);
            if (rt == null || !rt.Def.CapSummons || !IsModMode(rt.Def)) return true;
            bool creatures = false;
            for (int i = 0; i < ab.m_spawnPrefab.Length && !creatures; i++)
            {
                GameObject sp = ab.m_spawnPrefab[i];
                creatures = sp != null && (sp.GetComponent<Character>() != null || sp.GetComponent<EggHatch>() != null);
            }
            if (!creatures || !AtCap(owner)) return true;
            Debug(rt.Def.Prefab + ": summon skipped, the crowd is at the cap");
            return false;
        }

        [HarmonyPatch(typeof(SpawnAbility), "Setup")]
        private static class SpawnAbility_Setup_Patch
        {
            private static bool Prefix(SpawnAbility __instance, Character owner)
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || !p.Active || __instance == null || p._fights.Count == 0) return true;
                try
                {
                    if (p.SummonAllowed(__instance, owner)) return true;
                    if (!DestroyNetObject(__instance.gameObject)) UnityEngine.Object.Destroy(__instance.gameObject);
                    return false;
                }
                catch (Exception e) { p.Fail("SpawnAbility.Setup", e); return true; }
            }
        }

        // Vanilla regeneration (the whole HP in an hour) is off while a boss's fight runs in mod
        // mode: a group fight is long, and an hour's regen took back most of the Queen's bar.
        [HarmonyPatch(typeof(BaseAI), "UpdateRegeneration")]
        private static class BaseAI_UpdateRegeneration_Patch
        {
            private static bool Prefix(Character ___m_character)
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || !p.Active || ___m_character == null || p._fights.Count == 0 || !___m_character.IsBoss()) return true;
                try
                {
                    FightRt rt = p.RtIfRunning(___m_character);
                    if (rt == null || !p.IsModMode(rt.Def)) return true;
                    ZDO z = Zdo(___m_character);
                    return z == null || z.GetInt(KPhase) == 0;
                }
                catch (Exception e) { p.Fail("BaseAI.UpdateRegeneration", e); return true; }
            }
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
