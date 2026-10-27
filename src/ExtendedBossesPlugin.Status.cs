using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ExtendedBosses
{
    // Client side, players with the mod only: what a centre message says once stays on screen.
    // A line under the boss's health bar tells how many of its nests/totems/eggs are left to break
    // (and whether they hold its shield), the burn window and the running phase with the seconds
    // left; every one of those objects gets a pulsing pin on the minimap. Both are read from the
    // boss's network data, so they work on every client, not only the boss owner.
    public partial class ExtendedBossesPlugin
    {
        private ConfigEntry<bool> _cfgStatusLine;
        private ConfigEntry<bool> _cfgMapPins;

        private const float StatusRefresh = 0.5f;
        private sealed class StatusCache { public string Text; public float At; }
        private readonly Dictionary<Character, StatusCache> _status = new Dictionary<Character, StatusCache>();
        private float _nextPinScan;
        private Minimap _pinMap;            // the map our pins live on (a new one after a relog)
        private readonly Dictionary<ZDOID, Minimap.PinData> _pins = new Dictionary<ZDOID, Minimap.PinData>();
        private readonly HashSet<ZDOID> _tmpPinSeen = new HashSet<ZDOID>();
        private readonly List<ZDOID> _tmpPinGone = new List<ZDOID>();

        private static FieldInfo _fiHuds, _fiHudChar, _fiHudName;
        private static MethodInfo _miAddPin;

        private void BindStatusConfig()
        {
            _cfgStatusLine = B("07 Client", "BossStatusLine", true,
                "A line under the boss's health bar: nests/totems/eggs left to break and the shield they hold, the burn window, the running phase and its seconds (only players with the mod see it; not synced).",
                "Строка под полоской HP босса: сколько гнёзд/тотемов/яиц осталось разбить и держат ли они щит, окно уязвимости, текущая фаза и её секунды (видят только игроки с модом; не синкается).");
            _cfgMapPins = B("07 Client", "MapPins", false,
                "A pulsing pin on the minimap at every nest, totem and egg of a boss in a fight, gone once it is broken (only players with the mod see it; not synced).",
                "Мигающая метка на миникарте на каждом гнезде, тотеме и яйце босса в бою; пропадает, когда его разбили (видят только игроки с модом; не синкается).");
        }

        // ------------------------------------------------------------------
        // the line under the boss bar
        // ------------------------------------------------------------------
        private string StatusLine(Character boss)
        {
            ZDO z = Zdo(boss);
            BossDef def = z != null ? BossOf(z) : null;
            if (def == null || !IsModMode(def) || z.GetInt(KPhase) == 0) return null;
            long net = NetTicks();
            List<string> lines = new List<string>();

            int left = TotemsAlive(z);
            if (left > 0)
            {
                bool shield = z.GetInt(KShieldAct) > 0 && MechanicOn(def, ActKind.Shield);
                lines.Add(L(shield ? "status.shield" : "status.targets", left.ToString()));
            }
            long window = z.GetLong(KWindowUntil, 0L);
            if (net < window) lines.Add(L("status.window", SecondsLeft(window, net)));

            int ac = z.GetInt(KCycleAct), vk = z.GetInt(KCycleKind);
            long until = z.GetLong(KCycleUntil, 0L);
            if (ac > 0 && vk > 0 && z.GetInt(KCycleStarted) != 0 && net < until && MechanicOn(def, ActKind.Cycle))
            {
                Act act = ActByCode(def, ac - 1);
                if (act != null && act.Variants != null && vk <= act.Variants.Count)
                {
                    CycleVariant v = act.Variants[vk - 1];
                    if (!string.IsNullOrEmpty(v.Say))
                    {
                        string extra = v.Adapt ? TypeToken((HitData.DamageType)z.GetInt(KCycleAdapt)) : "";
                        lines.Add(L("status.cycle", L(v.Say, NameToken(boss), extra), SecondsLeft(until, net)));
                    }
                }
            }
            return lines.Count > 0 ? string.Join("\n", lines.ToArray()) : null;
        }

        private static string SecondsLeft(long until, long net)
        {
            return Mathf.CeilToInt((until - net) / (float)TimeSpan.TicksPerSecond).ToString();
        }

        private string CachedStatus(Character boss, float now)
        {
            StatusCache c;
            if (!_status.TryGetValue(boss, out c)) { c = new StatusCache(); _status[boss] = c; c.At = -1f; }
            if (now - c.At >= StatusRefresh || c.At < 0f)
            {
                c.At = now;
                c.Text = StatusLine(boss);
            }
            return c.Text;
        }

        // Vanilla writes the boss name into the bar every frame; ours goes under it in the same text.
        private void DecorateBossHuds(EnemyHud hud)
        {
            if (!_cfgStatusLine.Value) return;
            if (_fiHuds == null)
            {
                _fiHuds = AccessTools.Field(typeof(EnemyHud), "m_huds");
                Type hd = AccessTools.Inner(typeof(EnemyHud), "HudData");
                if (hd != null)
                {
                    _fiHudChar = FieldNamed(hd, "m_character");
                    _fiHudName = FieldNamed(hd, "m_name");
                }
            }
            if (_fiHuds == null || _fiHudChar == null || _fiHudName == null) return;
            IDictionary huds = _fiHuds.GetValue(hud) as IDictionary;
            if (huds == null) return;
            float now = Time.time;
            foreach (object data in huds.Values)
            {
                Character c = _fiHudChar.GetValue(data) as Character;
                if (c == null || !c.IsBoss()) continue;
                string line = CachedStatus(c, now);
                if (line == null) continue;
                TMP_Text name = _fiHudName.GetValue(data) as TMP_Text;
                if (name == null) continue;
                name.text = name.text + "\n<size=65%><color=#FFC870>" + line + "</color></size>";
            }
            if (_status.Count > 8) _status.Clear();     // bosses come and go; the cache refills
        }

        // A public field of the private nested HudData, looked up by its name (null if gone - the
        // line is then simply not shown). Not GetField: check-refs.ps1 would pair the name with
        // the outer EnemyHud, the last typeof before it.
        private static FieldInfo FieldNamed(Type t, string name)
        {
            FieldInfo[] fs = t.GetFields();
            for (int i = 0; i < fs.Length; i++) if (fs[i].Name == name) return fs[i];
            return null;
        }

        [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
        private static class EnemyHud_UpdateHuds_Patch
        {
            private static void Postfix(EnemyHud __instance)
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || __instance == null) return;
                try { p.DecorateBossHuds(__instance); }
                catch (Exception e) { p.Fail("EnemyHud.UpdateHuds", e); }
            }
        }

        // ------------------------------------------------------------------
        // minimap pins
        // ------------------------------------------------------------------
        private void TickPins(float now)
        {
            if (now < _nextPinScan) return;
            _nextPinScan = now + 1f;
            Minimap map = Minimap.instance;
            if (map == null || ZDOMan.instance == null) return;
            if (map != _pinMap) { _pins.Clear(); _pinMap = map; }
            _tmpPinSeen.Clear();
            if (_cfgMapPins.Value && Active)
            {
                List<Character> all = Character.GetAllCharacters();
                for (int i = 0; i < all.Count; i++)
                {
                    Character boss = all[i];
                    if (boss == null || !boss.IsBoss() || boss.IsDead()) continue;
                    ZDO bz = Zdo(boss);
                    BossDef def = bz != null ? BossOf(bz) : null;
                    if (def == null || !IsModMode(def) || bz.GetInt(KPhase) == 0) continue;
                    int slots = bz.GetInt(KTotems);
                    for (int s = 0; s < slots; s++)
                    {
                        if (bz.GetInt(TotemActKey(s)) == SeedCode) continue;
                        ZDOID id = bz.GetZDOID(TotemKey(s));
                        ZDO tz = id != ZDOID.None ? ZDOMan.instance.GetZDO(id) : null;
                        if (tz == null) continue;
                        _tmpPinSeen.Add(id);
                        if (!_pins.ContainsKey(id)) _pins[id] = AddPin(map, tz.GetPosition(), L("pin.target"));
                    }
                }
            }
            _tmpPinGone.Clear();
            foreach (KeyValuePair<ZDOID, Minimap.PinData> kv in _pins) if (!_tmpPinSeen.Contains(kv.Key)) _tmpPinGone.Add(kv.Key);
            for (int i = 0; i < _tmpPinGone.Count; i++)
            {
                Minimap.PinData pin = _pins[_tmpPinGone[i]];
                if (pin != null) map.RemovePin(pin);
                _pins.Remove(_tmpPinGone[i]);
            }
        }

        // AddPin's last parameter is a platform user id from another assembly: called by
        // reflection with a default one, so the build needs no reference to it.
        private static Minimap.PinData AddPin(Minimap map, Vector3 pos, string name)
        {
            if (_miAddPin == null) _miAddPin = AccessTools.Method(typeof(Minimap), "AddPin");
            if (_miAddPin == null) return null;
            ParameterInfo[] ps = _miAddPin.GetParameters();
            object[] args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
                args[i] = ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
            args[0] = pos;
            args[1] = Minimap.PinType.Icon3;
            args[2] = name;
            args[3] = false;        // not saved with the map
            args[4] = false;
            Minimap.PinData pin = _miAddPin.Invoke(map, args) as Minimap.PinData;
            if (pin != null) pin.m_animate = true;
            return pin;
        }

        private void ClearPins()
        {
            Minimap map = Minimap.instance;
            if (map != null)
                foreach (KeyValuePair<ZDOID, Minimap.PinData> kv in _pins) if (kv.Value != null) map.RemovePin(kv.Value);
            _pins.Clear();
        }
    }
}
