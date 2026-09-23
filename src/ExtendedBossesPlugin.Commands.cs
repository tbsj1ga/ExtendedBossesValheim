using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ExtendedBosses
{
    public partial class ExtendedBossesPlugin
    {
        // ------------------------------------------------------------------
        // console: eb status | check | phase <n> | reset | probe <prefab> | players <n>
        // (answers in the client's language; probe output is technical and stays English)
        // ------------------------------------------------------------------
        private void RegisterCommands()
        {
            new Terminal.ConsoleCommand("eb", "Extended Bosses: eb status | check | phase <n> | reset | probe <prefab> | players <n|0>",
                delegate(Terminal.ConsoleEventArgs args) { RunCommand(args); });
        }

        private static void Say(Terminal.ConsoleEventArgs args, string text)
        {
            if (args != null && args.Context != null) args.Context.AddString(text);
        }

        private void RunCommand(Terminal.ConsoleEventArgs args)
        {
            try
            {
                string sub = args.Args.Length > 1 ? args.Args[1].ToLowerInvariant() : "";
                string arg = args.Args.Length > 2 ? args.Args[2] : "";
                if (sub == "status") { Say(args, Status()); return; }
                if (sub == "probe") { Say(args, Probe(arg)); return; }
                if (sub == "check")
                {
                    if (ZNetScene.instance == null) { Say(args, L("cmd.noworld")); return; }
                    SelfCheck();
                    Say(args, L("cmd.checkdone", _checkOk.ToString(), _checkProblems.Count.ToString()));
                    for (int i = 0; i < _checkProblems.Count; i++) Say(args, "  " + _checkProblems[i]);
                    return;
                }
                if (sub == "players")
                {
                    int n;
                    if (!int.TryParse(arg, out n) || n < 0) { Say(args, L("cmd.players")); return; }
                    _forcePlayers = n;
                    Say(args, n == 0 ? L("cmd.playerscount") : L("cmd.playersforced", n.ToString()));
                    return;
                }
                if (sub == "phase" || sub == "reset")
                {
                    Character boss = NearestOwnedBoss();
                    if (boss == null) { Say(args, L("cmd.noboss")); return; }
                    BossDef def = BossOf(Zdo(boss));
                    FightRt rt = Rt(boss, def);
                    if (sub == "reset") { ResetFight(boss, rt); Say(args, L("cmd.reset", def.Prefab)); return; }
                    int i;
                    if (!int.TryParse(arg, out i) || i < 0 || i >= def.Phases.Count) { Say(args, L("cmd.phaseusage", (def.Phases.Count - 1).ToString())); return; }
                    ZDO z = Zdo(boss);
                    z.Set(KPhase, z.GetInt(KPhase) | (1 << i));
                    RunPhase(boss, rt, i, GroupSize(boss), Time.time);
                    Say(args, L("cmd.phase", def.Prefab, i.ToString(), def.Phases[i].Pct.ToString()));
                    return;
                }
                Say(args, L("cmd.help"));
            }
            catch (Exception e)
            {
                Say(args, "eb: " + e.Message);
                Fail("command", e);
            }
        }

        private Character NearestOwnedBoss()
        {
            Player me = Player.m_localPlayer;
            Vector3 from = me != null ? me.transform.position : Vector3.zero;
            Character best = null;
            float bestD = float.MaxValue;
            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];
                if (c == null || !c.IsBoss() || c.IsDead() || !IsOwner(c) || BossOf(Zdo(c)) == null) continue;
                float d = Vector3.Distance(from, c.transform.position);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        private string Status()
        {
            if (_disabledByErrors) return L("cmd.inert", Name);
            StringBuilder sb = new StringBuilder();
            sb.Append(Name).Append(" ").Append(Version).Append(Sb(_cfgEnabled) ? "" : " (disabled)")
              .Append(", profile ").Append(Ss(_cfgProfile)).Append(_serverSynced ? ", server settings" : ", local settings");
            if (_forcePlayers > 0) sb.Append(", players = ").Append(_forcePlayers);
            sb.Append("\n");
            int shown = 0;
            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];
                if (c == null || !c.IsBoss()) continue;
                ZDO z = Zdo(c);
                BossDef def = BossOf(z);
                if (def == null) continue;
                shown++;
                int mask = z.GetInt(KPhase);
                int fired = 0;
                for (int k = 0; k < def.Phases.Count; k++) if ((mask & (1 << k)) != 0) fired++;
                FightRt rt = RtIfRunning(c);
                string flags = "";
                if (rt != null && rt.ShieldActive) flags += L("cmd.shield");
                if (rt != null && rt.WindowActive) flags += L("cmd.window");
                if (rt != null && rt.ResistAct != null) flags += L("cmd.resist");
                if (rt != null && rt.CycleAct != null && rt.CycleVar != null) flags += L("cmd.cycle." + rt.CycleVar.Id.ToLowerInvariant());
                sb.Append(L("cmd.status",
                    def.Prefab, Ss(def.CfgMode), ProfileOf(def),
                    F1(c.GetHealthPercentage() * 100f), fired.ToString(), def.Phases.Count.ToString(),
                    IsOwner(c) ? L("cmd.ownedhere") : L("cmd.ownedother"),
                    GroupSize(c).ToString(), F2(IsModMode(def) ? BossDamageFactor(c, def) : 1f),
                    CountAdds(c.GetZDOID(), 0).ToString(), (rt != null ? rt.TotemsAlive : z.GetInt(KTotems)).ToString(), flags)).Append("\n");
            }
            if (shown == 0) sb.Append(L("cmd.nobosses"));
            return sb.ToString().TrimEnd();
        }

        // What a prefab is made of - for the "(check)" items of the roadmap.
        private static string Probe(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return "eb probe <prefab>";
            if (ZNetScene.instance == null) return L("cmd.noworld");
            GameObject pf = ZNetScene.instance.GetPrefab(prefab);
            if (pf == null) return "Prefab '" + prefab + "' not found.";
            StringBuilder sb = new StringBuilder();
            ZNetView nv = pf.GetComponent<ZNetView>();
            sb.Append(prefab).Append(": ").Append(nv != null ? "networked" + (nv.m_persistent ? ", persistent" : ", not persistent") : "LOCAL ONLY (no ZNetView)").Append("\n");
            Character ch = pf.GetComponent<Character>();
            if (ch != null) sb.Append("  Character '").Append(ch.m_name).Append("' hp ").Append(ch.m_health).Append(" faction ").Append(ch.m_faction)
                              .Append(" group '").Append(ch.m_group).Append("' boss ").Append(ch.m_boss).Append("\n");
            if (ch != null)
            {
                HitData.DamageModifiers dm = ch.m_damageModifiers;
                sb.Append("  resist: blunt ").Append(dm.m_blunt).Append(", slash ").Append(dm.m_slash).Append(", pierce ").Append(dm.m_pierce)
                  .Append(", chop ").Append(dm.m_chop).Append(", fire ").Append(dm.m_fire).Append(", frost ").Append(dm.m_frost)
                  .Append(", lightning ").Append(dm.m_lightning).Append(", poison ").Append(dm.m_poison).Append(", spirit ").Append(dm.m_spirit).Append("\n");
            }
            Humanoid hu = pf.GetComponent<Humanoid>();
            if (hu != null && hu.m_defaultItems != null)
            {
                sb.Append("  default items:");
                for (int i = 0; i < hu.m_defaultItems.Length; i++) if (hu.m_defaultItems[i] != null) sb.Append(" ").Append(hu.m_defaultItems[i].name);
                sb.Append("\n");
            }
            Destructible de = pf.GetComponentInChildren<Destructible>(true);
            if (de != null) sb.Append("  Destructible hp ").Append(de.m_health).Append("\n");
            WearNTear wt = pf.GetComponentInChildren<WearNTear>(true);
            if (wt != null) sb.Append("  WearNTear hp ").Append(wt.m_health).Append("\n");
            SpawnArea sa = pf.GetComponentInChildren<SpawnArea>(true);
            if (sa != null)
            {
                sb.Append("  SpawnArea every ").Append(sa.m_spawnIntervalSec).Append("s, max near ").Append(sa.m_maxNear).Append(":");
                for (int i = 0; i < sa.m_prefabs.Count; i++) if (sa.m_prefabs[i].m_prefab != null) sb.Append(" ").Append(sa.m_prefabs[i].m_prefab.name);
                sb.Append("\n");
            }
            Aoe[] aoes = pf.GetComponentsInChildren<Aoe>(true);
            for (int i = 0; i < aoes.Length; i++)
                sb.Append("  Aoe radius ").Append(aoes[i].m_radius).Append(" damage ").Append(aoes[i].m_damage.GetTotalDamage()).Append(" ttl ").Append(aoes[i].m_ttl).Append("\n");
            ItemDrop id = pf.GetComponent<ItemDrop>();
            if (id != null) sb.Append("  Item '").Append(id.m_itemData.m_shared.m_name).Append("' max quality ").Append(id.m_itemData.m_shared.m_maxQuality)
                              .Append(" stack ").Append(id.m_itemData.m_shared.m_maxStackSize).Append("\n");
            return sb.ToString().TrimEnd();
        }
    }
}
