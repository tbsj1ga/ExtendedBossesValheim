using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExtendedBosses
{
    // The fight runs on whoever owns the boss, and vanilla hands a boss to any peer near it: when
    // the host dies or walks away, it may go to a player without the mod, whose client runs the
    // vanilla boss with our raised HP and no mechanics, or to one with an older version. On the
    // server, every 2 s: a boss owned by such a peer is handed to the modded player near it with
    // the newest version (the host first, then the closest). Vanilla does not take it back while
    // the new owner stays near. A boss held by a WeaponArts taunt is left to it for its window.
    public partial class ExtendedBossesPlugin
    {
        private const float HandoffInterval = 2f;
        private static readonly int KTauntBy = "j1ga.weaponarts.holdby".GetStableHashCode();       // WeaponArts: session of the taunting client
        private static readonly int KTauntUntil = "j1ga.weaponarts.holduntil".GetStableHashCode(); // WeaponArts: world ms the taunt ends

        private readonly Dictionary<long, System.Version> _peerVersions = new Dictionary<long, System.Version>();
        private readonly List<ZDO> _handoffNear = new List<ZDO>();
        private readonly HashSet<ZDOID> _handoffSeen = new HashSet<ZDOID>();
        private float _nextHandoff;

        private struct Candidate { public long Uid; public Vector3 Pos; public System.Version Ver; public bool Host; }
        private readonly List<Candidate> _candidates = new List<Candidate>();

        private static System.Version ParseVersion(string s)
        {
            try { return new System.Version(s); }
            catch (Exception) { return new System.Version(0, 0); }
        }

        private void TickHandoff(float now)
        {
            if (now < _nextHandoff) return;
            _nextHandoff = now + HandoffInterval;
            ZNet znet = ZNet.instance;
            ZDOMan man = ZDOMan.instance;
            if (znet == null || man == null || !znet.IsServer()) return;

            // who may run a fight: the hosting player and every peer that said hello
            _candidates.Clear();
            System.Version newest = null;
            long me = ZDOMan.GetSessionID();
            if (Player.m_localPlayer != null)
            {
                Candidate c = new Candidate { Uid = me, Pos = znet.GetReferencePosition(), Ver = ParseVersion(Version), Host = true };
                _candidates.Add(c);
                newest = c.Ver;
            }
            List<ZNetPeer> peers = znet.GetPeers();
            for (int i = 0; i < peers.Count; i++)
            {
                ZNetPeer peer = peers[i];
                System.Version v;
                if (peer == null || peer.m_uid == 0L || !_peerVersions.TryGetValue(peer.m_uid, out v)) continue;
                _candidates.Add(new Candidate { Uid = peer.m_uid, Pos = peer.m_refPos, Ver = v });
                if (newest == null || v.CompareTo(newest) > 0) newest = v;
            }
            if (_candidates.Count == 0) return;

            // bosses near the peers that should not run a fight: no mod, or older than the newest
            SimulationDistance synced = znet.GetSyncedSimulationDistance();
            SimulationDistance near = new SimulationDistance(synced.NearSimulationDistance, 0, synced.IsClassic);
            long worldMs = (long)(znet.GetTimeSeconds() * 1000.0);
            _handoffSeen.Clear();
            for (int i = 0; i < peers.Count; i++)
            {
                ZNetPeer peer = peers[i];
                if (peer == null || peer.m_uid == 0L) continue;
                System.Version v;
                if (_peerVersions.TryGetValue(peer.m_uid, out v) && v.CompareTo(newest) >= 0) continue;
                _handoffNear.Clear();
                man.FindSectorObjects(ZoneSystem.GetZone(peer.m_refPos), near, _handoffNear, null);
                for (int k = 0; k < _handoffNear.Count; k++)
                {
                    ZDO z = _handoffNear[k];
                    if (z == null || z.GetOwner() != peer.m_uid || !_handoffSeen.Add(z.m_uid)) continue;
                    BossDef def = BossOf(z);
                    if (def == null || !IsModMode(def)) continue;
                    if (z.GetLong(KTauntBy, 0L) == peer.m_uid && z.GetLong(KTauntUntil, 0L) > worldMs) continue;   // their taunt holds it
                    HandOff(znet, z, def, peer, newest);
                }
            }
            _handoffNear.Clear();
        }

        private void HandOff(ZNet znet, ZDO z, BossDef def, ZNetPeer from, System.Version newest)
        {
            Vector3 pos = z.GetPosition();
            System.Version fromVer;
            bool fromModded = _peerVersions.TryGetValue(from.m_uid, out fromVer);
            int best = -1;
            for (int i = 0; i < _candidates.Count; i++)
            {
                Candidate c = _candidates[i];
                if (c.Uid == from.m_uid || !ZNetScene.InActiveArea(pos, ZoneSystem.GetZone(c.Pos))) continue;
                if (fromModded && c.Ver.CompareTo(fromVer) <= 0) continue;       // a modded owner gives way only to a newer version
                if (best < 0) { best = i; continue; }
                Candidate b = _candidates[best];
                int cmp = c.Ver.CompareTo(b.Ver);
                if (cmp != 0) { if (cmp > 0) best = i; continue; }
                if (c.Host != b.Host) { if (c.Host) best = i; continue; }
                if ((c.Pos - pos).sqrMagnitude < (b.Pos - pos).sqrMagnitude) best = i;
            }
            if (best < 0) return;
            z.SetOwner(_candidates[best].Uid);
            Debug(def.Prefab + ": handed from " + from.m_playerName + (fromModded ? " (" + fromVer + ")" : " (no mod)") +
                  " to " + (_candidates[best].Host ? "the host" : PeerName(znet, _candidates[best].Uid)) + " (" + _candidates[best].Ver + ")");
        }

        private static string PeerName(ZNet znet, long uid)
        {
            ZNetPeer p = znet.GetPeer(uid);
            return p != null ? p.m_playerName : uid.ToString();
        }
    }
}
