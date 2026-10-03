using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ExtendedBosses
{
    // Client side, players with the mod only: a soft light on everything a boss placed (nests,
    // totems, Eikthyr's skull pile) and on the slime crawling to Bonemass, so they can be found in
    // a crowd and under poison effects. Once a second the loaded network objects are scanned for
    // the boss tag. The same scan strips the drops of objects marked "no drop" (after a relog the
    // object is rebuilt from its prefab, drops included).
    public partial class ExtendedBossesPlugin
    {
        internal static readonly int KNoDrop = "j1ga.extendedbosses.nodrop".GetStableHashCode();   // totem: drops nothing

        private ConfigEntry<bool> _cfgHighlight;
        private float _nextGlowScan;
        private readonly Dictionary<int, GameObject> _glows = new Dictionary<int, GameObject>();
        private readonly List<int> _tmpGlowIds = new List<int>();
        private const string GlowName = "extendedbosses_glow";

        private void BindGlowConfig()
        {
            _cfgHighlight = B("07 Client", "HighlightObjects", true,
                "A soft light on boss nests, totems and the slime crawling to Bonemass, so they are easy to find (only players with the mod see it; not synced).",
                "Мягкая подсветка гнёзд и тотемов боссов и слизи, ползущей к Массивному, чтобы их было легко найти (видят только игроки с модом; не синкается).");
        }

        private void TickGlow(float now)
        {
            if (now < _nextGlowScan) return;
            _nextGlowScan = now + 1f;
            if (ZNetScene.instance == null) return;
            if (_instancesField == null) _instancesField = HarmonyLib.AccessTools.Field(typeof(ZNetScene), "m_instances");
            Dictionary<ZDO, ZNetView> inst = _instancesField != null ? _instancesField.GetValue(ZNetScene.instance) as Dictionary<ZDO, ZNetView> : null;
            if (inst == null) return;
            bool on = _cfgHighlight.Value;

            HashSet<int> seen = new HashSet<int>();
            HashSet<ZDOID> seenNoDrop = new HashSet<ZDOID>();
            foreach (KeyValuePair<ZDO, ZNetView> kv in inst)
            {
                ZDO z = kv.Key;
                ZNetView nv = kv.Value;
                if (z == null || nv == null || z.GetZDOID(KBossPair) == ZDOID.None) continue;
                GameObject go = nv.gameObject;
                if (z.GetBool(KNoDrop, false))
                {
                    StripDrops(go);
                    RememberNoDrop(z, go);
                    seenNoDrop.Add(z.m_uid);
                }

                Character c = go.GetComponent<Character>();
                bool slime = c != null && z.GetInt(KRole) == RoleFuse;
                if (!on || (c != null && !slime)) continue;      // creatures: only the fusing slime glows
                int id = go.GetInstanceID();
                seen.Add(id);
                if (!_glows.ContainsKey(id)) _glows[id] = AddGlow(go, slime);
            }

            TickNoDrop(now, seenNoDrop);

            // lights of objects that are gone (destroyed, unloaded) or switched off
            _tmpGlowIds.Clear();
            foreach (KeyValuePair<int, GameObject> kv in _glows) if (!seen.Contains(kv.Key) || kv.Value == null) _tmpGlowIds.Add(kv.Key);
            for (int i = 0; i < _tmpGlowIds.Count; i++)
            {
                GameObject g = _glows[_tmpGlowIds[i]];
                if (g != null) UnityEngine.Object.Destroy(g);
                _glows.Remove(_tmpGlowIds[i]);
            }
        }

        private static GameObject AddGlow(GameObject target, bool slime)
        {
            GameObject g = new GameObject(GlowName);
            g.transform.SetParent(target.transform, false);
            g.transform.localPosition = new Vector3(0f, slime ? 1f : 1.8f, 0f);
            Light l = g.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = slime ? new Color(0.55f, 1f, 0.35f) : new Color(1f, 0.75f, 0.4f);
            l.range = slime ? 4f : 7f;
            l.intensity = slime ? 1.6f : 2f;
            l.shadows = LightShadows.None;
            return g;
        }

        // The component stays (its Awake subscribed it to the object's destruction - removing it
        // would leave that subscription calling a destroyed component); only its drop table is
        // emptied. The table is this instance's own copy, the prefab is untouched.
        private static void StripDrops(GameObject go)
        {
            DropOnDestroyed[] drops = go.GetComponentsInChildren<DropOnDestroyed>(true);
            for (int i = 0; i < drops.Length; i++)
            {
                DropTable t = drops[i].m_dropWhenDestroyed;
                if (t == null) continue;
                t.m_dropChance = 0f;
                t.m_dropMin = 0;
                t.m_dropMax = 0;
            }
        }

        private void ClearGlows()
        {
            foreach (KeyValuePair<int, GameObject> kv in _glows) if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            _glows.Clear();
        }
    }
}
