using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ExtendedBosses
{
    // The server's settings on every client with the mod (same scheme as WeaponArts), so the
    // fight is the same whoever ends up owning the boss. One routed RPC, sent to a peer after its
    // login and to everybody when a setting changes on the server; the client uses the values
    // (through Sv/Sb/Ss/Si) while connected. Section 07 Client is per-client and never synced.
    public partial class ExtendedBossesPlugin
    {
        private const string SyncRpc = "j1ga.extendedbosses.config";
        private const int SyncVersion = 1;

        private ConfigEntry<bool> _cfgSync;
        private bool _syncDirty;

        private bool _serverSynced;
        private readonly Dictionary<string, object> _serverByKey = new Dictionary<string, object>();

        private void BindSyncConfig()
        {
            _cfgSync = B("08 Sync", "SyncConfig", true,
                "The server sends its settings to every client with the mod on connect and on change; the client uses them while connected. Off on the server: everyone uses their own file. No effect on a client. Section 07 Client is never synced.",
                "Сервер раздаёт свои настройки всем клиентам с модом при входе и при изменении; клиент использует их, пока подключён. Выкл на сервере — каждый со своим файлом. На клиенте не влияет. Секция 07 Client не синкается никогда.");
            Config.SettingChanged += delegate { _syncDirty = true; if (!_reloading) _lastSettingChange = Time.realtimeSinceStartup; };
        }

        private static string CompKey(ConfigDefinition d) { return d.Section + "\u0001" + d.Key; }

        private bool ServerValue(ConfigEntryBase e, out object o)
        {
            o = null;
            return _serverSynced && _serverByKey.TryGetValue(CompKey(e.Definition), out o) && o != null;
        }

        internal float Sv(ConfigEntry<float> e)
        {
            object o;
            if (ServerValue(e, out o) && o is float) return (float)o;
            return e.Value;
        }

        internal int Si(ConfigEntry<int> e)
        {
            object o;
            if (ServerValue(e, out o) && o is int) return (int)o;
            return e.Value;
        }

        internal bool Sb(ConfigEntry<bool> e)
        {
            object o;
            if (ServerValue(e, out o) && o is bool) return (bool)o;
            return e.Value;
        }

        internal string Ss(ConfigEntry<string> e)
        {
            object o;
            if (ServerValue(e, out o) && o is string) return (string)o;
            return e.Value;
        }

        // ------------------------------------------------------------------
        // both sides
        // ------------------------------------------------------------------
        private void RegisterRpc()
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.Register<ZPackage>(SyncRpc, new Action<long, ZPackage>(OnConfigPacket));
            ZRoutedRpc.instance.Register<ZPackage>(FxRpc, new Action<long, ZPackage>(OnFxPacket));
            RegisterTextRpcs();
        }

        private void FlushSync()
        {
            _syncDirty = false;
            ZNet znet = ZNet.instance;
            if (znet == null || !znet.IsServer() || !_cfgSync.Value || ZRoutedRpc.instance == null) return;
            if (znet.GetPeers().Count == 0) return;
            SendConfig(ZRoutedRpc.Everybody);
            Debug("settings sent to " + znet.GetPeers().Count + " peers");
        }

        // ------------------------------------------------------------------
        // server
        // ------------------------------------------------------------------
        private ZPackage BuildPacket()
        {
            List<ConfigDefinition> send = new List<ConfigDefinition>();
            foreach (ConfigDefinition d in Config.Keys)
            {
                if (d.Section != null && d.Section.StartsWith("07 Client")) continue;
                Type t = Config[d].SettingType;
                if (t == typeof(float) || t == typeof(bool) || t == typeof(string) || t == typeof(int)) send.Add(d);
            }

            ZPackage pkg = new ZPackage();
            pkg.Write(SyncVersion);
            pkg.Write(send.Count);
            for (int i = 0; i < send.Count; i++)
            {
                ConfigDefinition d = send[i];
                ConfigEntryBase e = Config[d];
                Type t = e.SettingType;
                int tag = t == typeof(float) ? 1 : t == typeof(bool) ? 2 : t == typeof(int) ? 4 : 3;
                pkg.Write(tag);
                pkg.Write(CompKey(d));
                if (tag == 1) pkg.Write((float)e.BoxedValue);
                else if (tag == 2) pkg.Write((bool)e.BoxedValue);
                else if (tag == 4) pkg.Write((int)e.BoxedValue);
                else pkg.Write((string)(e.BoxedValue ?? ""));
            }
            return pkg;
        }

        private void SendConfig(long target)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(target, SyncRpc, new object[] { BuildPacket() });
        }

        private void OnPeerInfo(ZNet znet, ZRpc rpc)
        {
            if (!znet.IsServer() || !_cfgSync.Value || rpc == null) return;
            foreach (ZNetPeer peer in znet.GetPeers())
            {
                if (peer == null || peer.m_rpc != rpc) continue;
                if (peer.m_uid != 0L) StartCoroutine(SendConfigLater(peer.m_uid));
                return;
            }
        }

        private IEnumerator SendConfigLater(long uid)
        {
            yield return new WaitForSeconds(1f);
            ZNet znet = ZNet.instance;
            if (znet == null || !znet.IsServer() || ZRoutedRpc.instance == null || !_cfgSync.Value) yield break;
            ZNetPeer peer = znet.GetPeer(uid);
            if (peer == null) yield break;
            try { SendConfig(uid); Debug("settings sent to " + peer.m_playerName); }
            catch (Exception e) { Fail("send config", e); }
        }

        // ------------------------------------------------------------------
        // client
        // ------------------------------------------------------------------
        private void OnConfigPacket(long sender, ZPackage pkg)
        {
            try
            {
                ZNet znet = ZNet.instance;
                if (znet == null || znet.IsServer() || pkg == null) return;
                ZNetPeer server = znet.GetServerPeer();
                if (server == null || sender != server.m_uid) return;
                int version = pkg.ReadInt();
                if (version != SyncVersion) { Logger.LogWarning("Server config packet version " + version + " != " + SyncVersion + "; using local settings."); return; }
                int n = pkg.ReadInt();
                if (n < 0 || n > 5000) return;
                Dictionary<string, object> map = new Dictionary<string, object>();
                for (int i = 0; i < n; i++)
                {
                    int tag = pkg.ReadInt();
                    string key = pkg.ReadString();
                    if (tag == 1) map[key] = pkg.ReadSingle();
                    else if (tag == 2) map[key] = pkg.ReadBool();
                    else if (tag == 4) map[key] = pkg.ReadInt();
                    else map[key] = pkg.ReadString();
                }
                _serverByKey.Clear();
                foreach (KeyValuePair<string, object> kv in map) _serverByKey[kv.Key] = kv.Value;
                _serverSynced = true;
                Logger.LogInfo("Settings received from the server (" + map.Count + " values).");
            }
            catch (Exception e) { Fail("config packet", e); }
        }

        private void ClearServerValues()
        {
            _serverSynced = false;
            _serverByKey.Clear();
        }

        // ------------------------------------------------------------------
        // Harmony
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class ZNet_Awake_Patch
        {
            private static void Postfix()
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null) return;
                try { p.RegisterRpc(); } catch (Exception e) { p.Fail("ZNet.Awake", e); }
            }
        }

        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        private static class ZNet_RPC_PeerInfo_Patch
        {
            private static void Postfix(ZNet __instance, ZRpc rpc)
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || p._disabledByErrors) return;
                try { p.OnPeerInfo(__instance, rpc); } catch (Exception e) { p.Fail("ZNet.RPC_PeerInfo", e); }
            }
        }

        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        private static class ZNet_OnDestroy_Patch
        {
            private static void Postfix()
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null) return;
                try
                {
                    p.StopAllCoroutines();
                    p.ClearServerValues();
                    p.RestoreAllCharges();
                    p._fights.Clear();
                    p.ClearCircles();
                    p.ClearGlows();
                    p._helloSent = false;
                    p._moddedPeers.Clear();
                }
                catch (Exception e) { p.Fail("ZNet.OnDestroy", e); }
            }
        }
    }
}
