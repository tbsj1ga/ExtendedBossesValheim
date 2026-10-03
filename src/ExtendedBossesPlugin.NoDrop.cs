using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ExtendedBosses
{
    // Objects a boss placed with "no drop" (Eikthyr's skull pile) must drop nothing, whoever
    // breaks them. The drop happens on the client that OWNS the object - in a group that may be
    // any player near it, with or without the mod. Two layers:
    //   1. on every client with the mod, DropOnDestroyed.OnDestroyed and Piece.DropResources (a
    //      build piece, like skull_pile, gives its resources back) are skipped for such objects;
    //   2. every client with the mod remembers where such objects stand and what they would drop;
    //      when one is destroyed (its ZDO is gone), the items of its drop table that appear on
    //      that spot in the next seconds are removed - this covers an owner without the mod.
    public partial class ExtendedBossesPlugin
    {
        private class NoDropSpot
        {
            public Vector3 Pos;
            public HashSet<int> Items;      // prefab hashes of what it would drop
            public float CleanUntil;        // > 0: destroyed, cleaning until this time
        }

        private readonly Dictionary<ZDOID, NoDropSpot> _noDropSpots = new Dictionary<ZDOID, NoDropSpot>();
        private const float NoDropCleanSeconds = 4f;
        private const float NoDropCleanRadius = 5f;

        // called from the glow scan (once a second) for every loaded "no drop" object
        private void RememberNoDrop(ZDO z, GameObject go)
        {
            NoDropSpot s;
            if (_noDropSpots.TryGetValue(z.m_uid, out s)) { s.Pos = go.transform.position; return; }
            s = new NoDropSpot();
            s.Pos = go.transform.position;
            s.Items = new HashSet<int>();
            GameObject pf = ZNetScene.instance.GetPrefab(z.GetPrefab());
            // a build piece (skull_pile is one) gives its resources back when destroyed
            Piece piece = pf != null ? pf.GetComponent<Piece>() : null;
            if (piece != null && piece.m_resources != null)
                for (int i = 0; i < piece.m_resources.Length; i++)
                    if (piece.m_resources[i] != null && piece.m_resources[i].m_resItem != null)
                        s.Items.Add(piece.m_resources[i].m_resItem.gameObject.name.GetStableHashCode());
            DropOnDestroyed[] drops = pf != null ? pf.GetComponentsInChildren<DropOnDestroyed>(true) : new DropOnDestroyed[0];
            for (int i = 0; i < drops.Length; i++)
            {
                DropTable t = drops[i].m_dropWhenDestroyed;
                if (t == null || t.m_drops == null) continue;
                for (int k = 0; k < t.m_drops.Count; k++)
                    if (t.m_drops[k].m_item != null) s.Items.Add(t.m_drops[k].m_item.name.GetStableHashCode());
            }
            _noDropSpots[z.m_uid] = s;
        }

        // after the scan: a remembered object that is gone - destroyed, or just unloaded?
        private void TickNoDrop(float now, HashSet<ZDOID> seenNow)
        {
            if (_noDropSpots.Count == 0) return;
            List<ZDOID> forget = null;
            bool cleaning = false;
            foreach (KeyValuePair<ZDOID, NoDropSpot> kv in _noDropSpots)
            {
                NoDropSpot s = kv.Value;
                if (s.CleanUntil > 0f)
                {
                    if (now > s.CleanUntil) { if (forget == null) forget = new List<ZDOID>(); forget.Add(kv.Key); }
                    else cleaning = true;
                    continue;
                }
                if (seenNow.Contains(kv.Key)) continue;
                bool destroyed = ZDOMan.instance != null && ZDOMan.instance.GetZDO(kv.Key) == null;
                if (destroyed && s.Items.Count > 0) { s.CleanUntil = now + NoDropCleanSeconds; cleaning = true; }
                else { if (forget == null) forget = new List<ZDOID>(); forget.Add(kv.Key); }
            }
            if (forget != null) for (int i = 0; i < forget.Count; i++) _noDropSpots.Remove(forget[i]);
            if (cleaning) CleanNoDropItems();
        }

        private void CleanNoDropItems()
        {
            Dictionary<ZDO, ZNetView> inst = _instancesField != null ? _instancesField.GetValue(ZNetScene.instance) as Dictionary<ZDO, ZNetView> : null;
            if (inst == null) return;
            List<GameObject> gone = null;
            foreach (KeyValuePair<ZDO, ZNetView> kv in inst)
            {
                if (kv.Key == null || kv.Value == null) continue;
                int prefab = kv.Key.GetPrefab();
                foreach (NoDropSpot s in _noDropSpots.Values)
                {
                    if (s.CleanUntil <= 0f || !s.Items.Contains(prefab)) continue;
                    if (Flat(kv.Value.transform.position - s.Pos) > NoDropCleanRadius) continue;
                    if (kv.Value.GetComponent<ItemDrop>() == null) continue;
                    if (gone == null) gone = new List<GameObject>();
                    gone.Add(kv.Value.gameObject);
                    break;
                }
            }
            if (gone == null) return;
            for (int i = 0; i < gone.Count; i++) DestroyNetObject(gone[i]);
            Debug("removed " + gone.Count + " item(s) dropped by a no-drop object");
        }

        [HarmonyPatch(typeof(Piece), "DropResources")]
        private static class Piece_DropResources_Patch
        {
            private static bool Prefix(Piece __instance)
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || __instance == null) return true;
                try
                {
                    ZDO z = Zdo(__instance);
                    return z == null || !z.GetBool(KNoDrop, false);
                }
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
                try
                {
                    ZDO z = Zdo(__instance);
                    return z == null || !z.GetBool(KNoDrop, false);
                }
                catch (Exception e) { p.Fail("DropOnDestroyed.OnDestroyed", e); return true; }
            }
        }
    }
}
