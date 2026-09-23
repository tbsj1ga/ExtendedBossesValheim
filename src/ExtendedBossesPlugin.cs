using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ExtendedBosses
{
    // Boss fights as raid encounters built only from vanilla parts. Everything runs on the OWNER
    // of the boss (HostOwner keeps that on the host): phases at HP thresholds spawn vanilla
    // creatures and props, marks drop vanilla AoE on players, damage to the boss is scaled by the
    // group size, and the fight state lives in the boss ZDO so it survives an owner change, a
    // relog and a world save. Players without the mod see all of it through the vanilla sync:
    // creatures, props, AoE, HP from the ZDO and centre-screen messages (MessageHud.MessageAll).
    //
    // ROADMAP.md holds the design; 0.6: the framework and the bosses up to the Queen.
    [BepInPlugin(Guid, Name, Version)]
    public partial class ExtendedBossesPlugin : BaseUnityPlugin
    {
        public const string Guid = "j1ga.extendedbosses";
        public const string Name = "Extended Bosses";
        public const string Version = "0.6.0";

        public static ExtendedBossesPlugin Instance;

        private Harmony _harmony;

        // Shared m_group of the boss and everything it brings: BaseAI.IsEnemy returns false for
        // two characters with the same non-empty group before it even looks at factions, so a
        // troll, a bear and greydwarfs summoned together never fight each other or the boss.
        internal const string RaidGroup = "extendedbosses";

        private int _errorCount;
        private bool _disabledByErrors;
        private const int MaxErrors = 25;
        private readonly HashSet<string> _loggedErrors = new HashSet<string>();

        private float _nextTick;
        private const float TickInterval = 0.25f;
        private float _nextOrphanScan;

        private void Awake()
        {
            try
            {
                Instance = this;
                BindConfig();
                BindSyncConfig();
                BuildBosses();
                BindBossConfig();
                RegisterCommands();
                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(ExtendedBossesPlugin).Assembly);
                Logger.LogInfo(Name + " " + Version + " loaded, " + _bosses.Count + " boss definition(s).");
            }
            catch (Exception e)
            {
                Logger.LogError("Awake failed, mod is inert: " + e);
                _disabledByErrors = true;
            }
        }

        private void OnDestroy()
        {
            try { RestoreAllCharges(); ClearCircles(); if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception e) { Logger.LogWarning("OnDestroy: " + e.Message); }
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_disabledByErrors) return;
            try
            {
                if (_syncDirty) FlushSync();
                UpdateCircles(Time.time);
                TickHello();
                if (!Active) return;
                float now = Time.time;
                if (now >= _nextTick)
                {
                    _nextTick = now + TickInterval;
                    TickFights(now);
                }
                if (now >= _nextOrphanScan)
                {
                    _nextOrphanScan = now + 10f;
                    ScanOrphans();
                }
            }
            catch (Exception e) { Fail("Update", e); }
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------
        internal bool Active { get { return !_disabledByErrors && Sb(_cfgEnabled) && ZNet.instance != null && ZNetScene.instance != null; } }

        internal static ZNetView View(Component c)
        {
            if (c == null) return null;
            ZNetView nv = c.GetComponent<ZNetView>();
            return nv != null && nv.IsValid() ? nv : null;
        }

        internal static ZDO Zdo(Component c)
        {
            ZNetView nv = View(c);
            return nv != null ? nv.GetZDO() : null;
        }

        internal static bool IsOwner(Component c)
        {
            ZNetView nv = View(c);
            return nv != null && nv.IsOwner();
        }

        internal static string F1(float v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }
        internal static string F2(float v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }

        internal void Debug(string text) { if (_cfgDebug.Value) Logger.LogInfo(text); }

        internal void Warn(string text)
        {
            if (_loggedErrors.Add("warn: " + text)) Logger.LogWarning(text);
        }

        internal void Fail(string where, Exception e)
        {
            string key = where + ": " + e.GetType().Name + ": " + e.Message;
            if (_loggedErrors.Add(key)) Logger.LogError(key + "\n" + e.StackTrace);
            if (++_errorCount >= MaxErrors && !_disabledByErrors)
            {
                _disabledByErrors = true;
                Logger.LogError("Too many errors, " + Name + " is now inert until the game restarts.");
            }
        }
    }
}
