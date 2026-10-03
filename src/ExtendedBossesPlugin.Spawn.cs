using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExtendedBosses
{
    // Spawning vanilla creatures and objects on the boss owner, the group reward on death, and
    // the "fx" RPC that shows players with the mod what players without it only read in the
    // centre message: the exact radius of a mark, and a strike whose prefab is local-only.
    public partial class ExtendedBossesPlugin
    {
        // ------------------------------------------------------------------
        // creatures and objects
        // ------------------------------------------------------------------
        internal Vector3 RingPoint(Vector3 center, float rMin, float rMax)
        {
            if (rMax < rMin) rMax = rMin;
            float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float dist = UnityEngine.Random.Range(rMin, rMax);
            Vector3 p = center + new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
            ZoneSystem zs = ZoneSystem.instance;
            p.y = zs != null ? zs.GetSolidHeight(p) : center.y;
            if (Mathf.Abs(p.y - center.y) > 12f) p.y = center.y;   // cliff or cave: stay at the boss's height
            return p;
        }

        internal static GameObject SpawnObject(GameObject prefab, Vector3 pos)
        {
            Quaternion rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            return UnityEngine.Object.Instantiate(prefab, pos, rot);
        }

        // A vanilla creature tied to the boss: its ZDO carries the boss id (cleanup, orphan
        // scan, the raid group on every client) and optionally an HP multiplier, totem slot, role
        // (healer) and lifetime (roots).
        internal Character SpawnCreature(ZDOID bossId, string prefab, int level, float hpMul, Vector3 center, int src, float rMin, float rMax, int role, float lifetime)
        {
            return SpawnCreatureAt(bossId, prefab, level, hpMul, RingPoint(center, rMin, rMax), src, role, lifetime);
        }

        // A spot under water in the ring (for leeches); false if there is no water around.
        internal bool WaterPoint(Vector3 center, float rMin, float rMax, out Vector3 pos)
        {
            pos = center;
            ZoneSystem zs = ZoneSystem.instance;
            if (zs == null) return false;
            float water = zs.m_waterLevel;
            for (int i = 0; i < 16; i++)
            {
                float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float dist = UnityEngine.Random.Range(rMin, rMax * 1.5f);
                Vector3 p = center + new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                if (zs.GetGroundHeight(p) < water - 0.6f)
                {
                    p.y = water - 0.3f;
                    pos = p;
                    return true;
                }
            }
            return false;
        }

        internal Character SpawnCreatureAt(ZDOID bossId, string prefab, int level, float hpMul, Vector3 pos, int src, int role, float lifetime)
        {
            GameObject pf = ZNetScene.instance.GetPrefab(prefab);
            if (pf == null || pf.GetComponent<Character>() == null) { Warn("creature prefab '" + prefab + "' not found."); return null; }
            GameObject go = SpawnObject(pf, pos);
            Character c = go.GetComponent<Character>();
            ZDO z = Zdo(c);
            if (z != null)
            {
                z.Set(KBoss, bossId);
                if (Mathf.Abs(hpMul - 1f) > 0.001f) z.Set(KHpMul, hpMul);
                if (src != 0) z.Set(KSrc, src);
                if (role != 0) z.Set(KRole, role);
                if (lifetime > 0f) z.Set(KExpire, NetTicks() + Seconds(lifetime));
            }
            if (level > 1) c.SetLevel(level);
            c.m_group = RaidGroup;
            BaseAI ai = go.GetComponent<BaseAI>();
            if (ai != null) ai.SetHuntPlayer(true);
            Debug("spawned " + prefab + " lvl " + level + (Mathf.Abs(hpMul - 1f) > 0.001f ? " hp x" + F1(hpMul) : ""));
            return c;
        }

        // ------------------------------------------------------------------
        // reward: on top of the vanilla drop, grows with the group
        // ------------------------------------------------------------------
        internal void DropRewards(Character boss, BossDef def)
        {
            if (!Sb(_cfgRewards)) return;
            int n = GroupSize(boss);
            Vector3 pos = boss.transform.position;
            float val = Sv(_cfgValuables);
            RewardDef r = def.Reward;
            int drops = 0;

            // valuables and this biome's materials (about a stack), then the next biome's
            // materials (about half a stack) - always
            for (int i = 0; i < r.Valuables.Count; i++)
                drops += DropLoot(r.Valuables[i], n, val, pos);
            for (int i = 0; i < r.NextBiome.Count; i++)
                drops += DropLoot(r.NextBiome[i], n, val, pos);

            // gear: only of the next biome (the last boss: its own), only for a group of 2+;
            // one piece per PlayersPerItem players, at least one
            string[] pool = r.NextGear.Length > 0 ? r.NextGear : r.Gear;
            int minPlayers = Si(_cfgGearMinPlayers);
            int pieces = n < minPlayers ? 0 : Mathf.Max(1, Mathf.FloorToInt(n / Mathf.Max(1f, Sv(_cfgPlayersPerItem))));
            for (int i = 0; i < pieces && pool.Length > 0; i++)
            {
                int qMin = Si(_cfgQualityMin), qMax = Mathf.Max(qMin, Si(_cfgQualityMax));
                if (DropItem(pool[UnityEngine.Random.Range(0, pool.Length)], 1, UnityEngine.Random.Range(qMin, qMax + 1), pos)) drops++;
            }
            Debug(def.Prefab + ": reward for " + n + " player(s), " + drops + " stack(s), " + pieces + " piece(s) of gear");
        }

        private int DropLoot(Loot l, int players, float mul, Vector3 pos)
        {
            float amount;
            if (l.StackFraction > 0f)
            {
                // a material: about this share of its stack (90-100 %), not scaled by the group
                GameObject pf = ZNetScene.instance.GetPrefab(l.Prefab);
                ItemDrop idp = pf != null ? pf.GetComponent<ItemDrop>() : null;
                int stack = idp != null ? Mathf.Max(1, idp.m_itemData.m_shared.m_maxStackSize) : 1;
                amount = stack * l.StackFraction * UnityEngine.Random.Range(0.9f, 1f);
            }
            else
            {
                amount = UnityEngine.Random.Range(l.Min, l.Max + 1);
                if (l.PerPlayer) amount *= 1f + 0.5f * (players - 1);
            }
            int total = Mathf.Max(l.StackFraction > 0f ? 1 : 0, Mathf.RoundToInt(amount * mul));
            return total > 0 && DropItem(l.Prefab, total, 0, pos) ? 1 : 0;
        }

        // Drops `amount` of a vanilla item near pos in stacks; quality > 0 = upgraded gear.
        private bool DropItem(string prefab, int amount, int quality, Vector3 pos)
        {
            GameObject pf = ZNetScene.instance.GetPrefab(prefab);
            ItemDrop idp = pf != null ? pf.GetComponent<ItemDrop>() : null;
            if (idp == null) { Warn("reward item '" + prefab + "' not found."); return false; }
            int maxStack = Mathf.Max(1, idp.m_itemData.m_shared.m_maxStackSize);
            bool any = false;
            while (amount > 0)
            {
                int stack = Mathf.Min(amount, maxStack);
                amount -= stack;
                ItemDrop.ItemData data = idp.m_itemData.Clone();
                data.m_dropPrefab = pf;
                data.m_stack = stack;
                if (quality > 0)
                {
                    int q = Mathf.Clamp(quality, 1, Mathf.Max(1, data.m_shared.m_maxQuality));
                    data.m_quality = q;
                    data.m_durability = data.GetMaxDurability(q);
                }
                Vector3 p = pos + Vector3.up * 1.5f + new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), 0f, UnityEngine.Random.Range(-1.5f, 1.5f));
                ItemDrop.DropItem(data, stack, p, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                any = true;
            }
            return any;
        }

        // ------------------------------------------------------------------
        // fx for players with the mod
        // ------------------------------------------------------------------
        private const string FxRpc = "j1ga.extendedbosses.fx";
        internal const int FxMark = 1;      // circle on a marked player (+ vanilla effect on him)
        internal const int FxStrike = 2;    // harmless visual copy of a local-only strike prefab

        internal void SendFx(int kind, ZDOID target, Vector3 pos, float radius, float duration, string prefab)
        {
            if (ZRoutedRpc.instance == null) return;
            ZPackage pkg = new ZPackage();
            pkg.Write(kind);
            pkg.Write(target);
            pkg.Write(pos);
            pkg.Write(radius);
            pkg.Write(duration);
            pkg.Write(prefab ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, FxRpc, new object[] { pkg });
        }

        private void OnFxPacket(long sender, ZPackage pkg)
        {
            try
            {
                if (pkg == null || ZNetScene.instance == null) return;
                int kind = pkg.ReadInt();
                ZDOID target = pkg.ReadZDOID();
                Vector3 pos = pkg.ReadVector3();
                float radius = pkg.ReadSingle();
                float duration = pkg.ReadSingle();
                string prefab = pkg.ReadString();
                bool fromSelf = sender == ZDOMan.GetSessionID();

                if (kind == FxMark)
                {
                    GameObject t = target != ZDOID.None ? ZNetScene.instance.FindInstance(target) : null;
                    if (_cfgShowCircles.Value) AddCircle(t != null ? t.transform : null, pos, radius, duration);
                    SpawnVisual(prefab, t != null ? t.transform.position : pos);
                }
                else if (kind == FxStrike && !fromSelf)
                {
                    SpawnVisual(prefab, pos);
                }
            }
            catch (Exception e) { Fail("fx packet", e); }
        }

        // A local, damage-free copy of a vanilla prefab: built under an inactive parent so no Aoe
        // (or anything networked) ever wakes up, stripped, then released.
        private static void SpawnVisual(string prefab, Vector3 pos)
        {
            if (string.IsNullOrEmpty(prefab)) return;
            GameObject pf = ZNetScene.instance.GetPrefab(prefab);
            if (pf == null || pf.GetComponent<ZNetView>() != null) return;
            GameObject holder = new GameObject("extendedbosses_fx");
            holder.SetActive(false);
            GameObject go = UnityEngine.Object.Instantiate(pf, pos, Quaternion.identity, holder.transform);
            Aoe[] aoes = go.GetComponentsInChildren<Aoe>(true);
            for (int i = 0; i < aoes.Length; i++) UnityEngine.Object.DestroyImmediate(aoes[i]);
            go.transform.SetParent(null, true);
            UnityEngine.Object.Destroy(holder);
            UnityEngine.Object.Destroy(go, 10f);
        }

        // ------------------------------------------------------------------
        // ground circles (LineRenderer), players with the mod only
        // ------------------------------------------------------------------
        private class Circle
        {
            public GameObject Go;
            public LineRenderer Line;
            public Transform Follow;
            public Vector3 Pos;
            public float Radius;
            public float Until;
        }

        private readonly List<Circle> _circles = new List<Circle>();
        private Material _circleMat;
        private const int CircleSegments = 48;

        private void AddCircle(Transform follow, Vector3 pos, float radius, float duration)
        {
            if (_circleMat == null)
            {
                Shader sh = Shader.Find("Sprites/Default");
                if (sh == null) return;
                _circleMat = new Material(sh);
            }
            GameObject go = new GameObject("extendedbosses_circle");
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = _circleMat;
            lr.loop = true;
            lr.useWorldSpace = true;
            lr.positionCount = CircleSegments;
            lr.widthMultiplier = 0.15f;
            Color c = new Color(1f, 0.15f, 0.1f, 0.9f);
            lr.startColor = c;
            lr.endColor = c;
            Circle ci = new Circle();
            ci.Go = go;
            ci.Line = lr;
            ci.Follow = follow;
            ci.Pos = pos;
            ci.Radius = radius;
            ci.Until = Time.time + duration;
            _circles.Add(ci);
        }

        private void UpdateCircles(float now)
        {
            for (int i = _circles.Count - 1; i >= 0; i--)
            {
                Circle ci = _circles[i];
                if (ci.Go == null || now >= ci.Until)
                {
                    if (ci.Go != null) UnityEngine.Object.Destroy(ci.Go);
                    _circles.RemoveAt(i);
                    continue;
                }
                Vector3 center = ci.Follow != null ? ci.Follow.position : ci.Pos;
                for (int s = 0; s < CircleSegments; s++)
                {
                    float a = s * Mathf.PI * 2f / CircleSegments;
                    Vector3 p = center + new Vector3(Mathf.Cos(a) * ci.Radius, 0f, Mathf.Sin(a) * ci.Radius);
                    float h;
                    if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(p, out h)) p.y = Mathf.Max(h, center.y - 2f) + 0.15f;
                    else p.y = center.y + 0.15f;
                    ci.Line.SetPosition(s, p);
                }
            }
        }

        private void ClearCircles()
        {
            for (int i = 0; i < _circles.Count; i++) if (_circles[i].Go != null) UnityEngine.Object.Destroy(_circles[i].Go);
            _circles.Clear();
        }
    }
}
