using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ExtendedBosses
{
    // Objects a boss placed with "no drop" (Eikthyr's skull pile) must drop nothing, whoever
    // breaks them. What a destroyed object drops is decided by the client that OWNS it:
    //   1. on a client with the mod both drops are skipped for such objects - Piece.DropResources
    //      (skull_pile is a build piece: a destroyed piece gives its resources, the skulls, back)
    //      and DropOnDestroyed.OnDestroyed (a drop table);
    //   2. so that the owner is always a client with the mod, the client running the boss fight
    //      keeps claiming them (in a group the game may hand an object to any player near it,
    //      also one without the mod). Removing the items afterwards is no option: the pickup is
    //      instant, they would already be in someone's inventory.
    public partial class ExtendedBossesPlugin
    {
        // boss owner, every tick: own every "no drop" object of this boss
        private void ClaimNoDrop(ZDO bz)
        {
            int slots = bz.GetInt(KTotems);
            for (int i = 0; i < slots; i++)
            {
                ZDOID id = bz.GetZDOID(TotemKey(i));
                if (id == ZDOID.None) continue;
                GameObject go = ZNetScene.instance.FindInstance(id);
                if (go == null) continue;
                ZNetView nv = go.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid() || nv.IsOwner()) continue;
                if (!nv.GetZDO().GetBool(KNoDrop, false)) continue;
                nv.ClaimOwnership();
            }
        }

        private static bool NoDropObject(Component c)
        {
            ZDO z = Zdo(c);
            return z != null && z.GetBool(KNoDrop, false);
        }

        [HarmonyPatch(typeof(Piece), "DropResources")]
        private static class Piece_DropResources_Patch
        {
            private static bool Prefix(Piece __instance)
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || __instance == null) return true;
                try { return !NoDropObject(__instance); }
                catch (Exception e) { p.Fail("Piece.DropResources", e); return true; }
            }
        }

        [HarmonyPatch(typeof(DropOnDestroyed), "OnDestroyed")]
        private static class DropOnDestroyed_OnDestroyed_Patch
        {
            private static bool Prefix(DropOnDestroyed __instance)
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || __instance == null) return true;
                try { return !NoDropObject(__instance); }
                catch (Exception e) { p.Fail("DropOnDestroyed.OnDestroyed", e); return true; }
            }
        }
    }
}
