using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ExtendedBosses
{
    // Every prefab every boss uses, checked once the world's prefabs are registered (and on
    // 'eb check'): does it exist, is it networked, destructible, a creature, an AoE, an item.
    // One report in the log instead of discovering problems phase by phase in a fight.
    public partial class ExtendedBossesPlugin
    {
        private enum Need { Creature, Nest, Totem, Aoe, Effect, Item }

        private int _checkOk;
        private List<string> _checkProblems = new List<string>();

        internal void SelfCheck()
        {
            if (ZNetScene.instance == null) return;
            _checkOk = 0;
            _checkProblems = new List<string>();
            StringBuilder details = new StringBuilder();
            HashSet<string> seen = new HashSet<string>();

            for (int b = 0; b < _bosses.Count; b++)
            {
                BossDef def = _bosses[b];
                string tag = "[" + def.Prefab + "] ";
                Check(tag, def.Prefab, Need.Creature, seen, details);
                for (int i = 0; i < def.Phases.Count; i++)
                {
                    List<Act> acts = def.Phases[i].Acts;
                    for (int a = 0; a < acts.Count; a++)
                    {
                        Act act = acts[a];
                        switch (act.Kind)
                        {
                            case ActKind.Wave:
                            case ActKind.Lieutenant:
                                for (int k = 0; k < act.Prefabs.Length; k++) Check(tag, act.Prefabs[k], Need.Creature, seen, details);
                                break;
                            case ActKind.Nest:
                                Check(tag, def.CfgNestPrefab != null ? Ss(def.CfgNestPrefab) : act.Prefabs[0], Need.Nest, seen, details);
                                break;
                            case ActKind.Totem:
                                Check(tag, def.CfgTotemPrefab != null ? Ss(def.CfgTotemPrefab) : act.Prop, Need.Totem, seen, details);
                                for (int k = 0; k < act.Prefabs.Length; k++) Check(tag, act.Prefabs[k], Need.Creature, seen, details);
                                break;
                            case ActKind.Marks:
                                Check(tag, MarkPrefab(def, act), act.Creature != null ? Need.Creature : Need.Aoe, seen, details);
                                Check(tag, def.CfgMarkEffect != null ? Ss(def.CfgMarkEffect) : act.Prop, Need.Effect, seen, details);
                                break;
                        }
                    }
                }
                for (int i = 0; i < def.Reward.Valuables.Count; i++) Check(tag, def.Reward.Valuables[i].Prefab, Need.Item, seen, details);
                for (int i = 0; i < def.Reward.NextBiome.Count; i++) Check(tag, def.Reward.NextBiome[i].Prefab, Need.Item, seen, details);
                for (int i = 0; i < def.Reward.Gear.Length; i++) Check(tag, def.Reward.Gear[i], Need.Item, seen, details);
                for (int i = 0; i < def.Reward.NextGear.Length; i++) Check(tag, def.Reward.NextGear[i], Need.Item, seen, details);
            }

            if (_cfgDebug.Value) Logger.LogInfo("Self-check details:\n" + details.ToString().TrimEnd());
            if (_checkProblems.Count == 0)
                Logger.LogInfo("Self-check: all " + _checkOk + " prefab(s) OK.");
            else
                Logger.LogWarning("Self-check: " + _checkOk + " prefab(s) OK, " + _checkProblems.Count + " problem(s):\n  " + string.Join("\n  ", _checkProblems.ToArray()));
        }

        private void Check(string tag, string prefab, Need need, HashSet<string> seen, StringBuilder details)
        {
            if (string.IsNullOrEmpty(prefab) || !seen.Add(need + ":" + prefab)) return;
            GameObject pf = ZNetScene.instance.GetPrefab(prefab);
            string problem = null, info = "";
            if (pf == null) problem = "not found";
            else
            {
                ZNetView nv = pf.GetComponent<ZNetView>();
                switch (need)
                {
                    case Need.Creature:
                        if (pf.GetComponent<Character>() == null) problem = "not a creature";
                        else if (nv == null) problem = "creature without ZNetView";
                        break;
                    case Need.Nest:
                        if (!IsDestructibleProp(pf)) problem = "not a destructible network object";
                        else if (pf.GetComponentInChildren<SpawnArea>(true) == null) problem = "no SpawnArea - it will not spawn anything";
                        break;
                    case Need.Totem:
                        if (!IsDestructibleProp(pf)) problem = "not a destructible network object - BonePileSpawner will be used";
                        break;
                    case Need.Aoe:
                    {
                        Aoe aoe = pf.GetComponentInChildren<Aoe>(true);
                        if (aoe == null) problem = "no Aoe component - it will deal no damage";
                        else
                        {
                            info = "radius " + F1(aoe.m_radius);
                            if (nv == null) problem = "local-only AoE: damage works, players WITHOUT the mod will not see it (" + info + ")";
                        }
                        break;
                    }
                    case Need.Effect:
                        if (nv != null) problem = "networked, will not be shown as a local effect";
                        break;
                    case Need.Item:
                        if (pf.GetComponent<ItemDrop>() == null) problem = "not an item";
                        break;
                }
            }
            if (problem != null) _checkProblems.Add(tag + prefab + " (" + need + "): " + problem);
            else _checkOk++;
            details.Append("  ").Append(tag).Append(prefab).Append(" (").Append(need).Append("): ")
                   .Append(problem ?? ("OK" + (info.Length > 0 ? ", " + info : ""))).Append("\n");
        }
    }
}
