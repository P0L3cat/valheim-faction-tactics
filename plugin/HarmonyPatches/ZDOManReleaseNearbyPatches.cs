#if VALHEIM_REFS
using System;
using System.Collections.Generic;
using FactionTactics.Config;
using FactionTactics.Util;
using HarmonyLib;
using UnityEngine;

namespace FactionTactics.HarmonyPatches
{
    /// <summary>
    /// 0.2.3 sticky enemy ownership: Prefix-replace <see cref="ZDOMan.ReleaseNearbyZDOS"/> so
    /// enemy prefabs near any peer stay on the dedicated server UID. Non-enemies keep vanilla
    /// reclaim (clients may own trees/buildings/ships). Stolen pattern from SSS Core, enemy-only.
    /// </summary>
    [HarmonyPatch(typeof(ZDOMan), "ReleaseNearbyZDOS")]
    public static class ZDOMan_ReleaseNearbyZDOS_StickyEnemy_Patch
    {
        private static readonly List<ZDO> TempNear = new List<ZDO>(512);
        private static float _reclaimLogAge;
        private static int _pendingReclaimLogs;

        /// <summary>Enemy ZDOs forced/kept on server this ReleaseNearby call.</summary>
        public static int LastStickyKeeps { get; private set; }

        /// <summary>Enemy ZDOs released to owner 0 (no peer nearby) this call.</summary>
        public static int LastStickyReleases { get; private set; }

        /// <summary>Times we stole an enemy ZDO from a non-server owner this call.</summary>
        public static int LastStickyReclaims { get; private set; }

        [HarmonyPrefix]
        public static bool Prefix(ZDOMan __instance, Vector3 refPosition, long uid)
        {
            if (PluginConfig.EnableEnemyServerOwnership?.Value != true)
                return true;
            if (PluginConfig.EnableStickyEnemyOwnership?.Value != true)
                return true;
            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return true;
            if (ZNetScene.instance == null)
                return true;

            try
            {
                RunStickyAndVanilla(__instance, refPosition, uid);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning(
                    $"StickyEnemy ReleaseNearbyZDOS failed (falling through vanilla): {ex.GetType().Name}: {ex.Message}");
                return true;
            }

            return false; // we fully handled this call
        }

        private static void RunStickyAndVanilla(ZDOMan zdoMan, Vector3 refPosition, long uid)
        {
            LastStickyKeeps = 0;
            LastStickyReleases = 0;
            LastStickyReclaims = 0;

            Vector2s zone = ZoneSystem.GetZone(refPosition);
            SimulationDistance synced = ZNet.instance.GetSyncedSimulationDistance();
            var simDist = new SimulationDistance(synced.NearSimulationDistance, 0, synced.IsClassic);

            TempNear.Clear();
            zdoMan.FindSectorObjects(zone, simDist, TempNear);

            long serverUid = ZNet.GetUID();

            foreach (var zdo in TempNear)
            {
                if (zdo == null || !zdo.Persistent)
                    continue;

                Vector3 position = zdo.GetPosition();

                if (ValheimWorldScan.IsLikelyEnemyPrefabZdo(zdo))
                {
                    ApplyEnemySticky(zdo, position, uid, serverUid);
                    continue;
                }

                ApplyVanillaReclaim(zdoMan, zdo, position, zone, uid);
            }
        }

        /// <summary>
        /// Enemy-only SSS-style sticky: near any peer → server owns; never assign to clients.
        /// Stronger than SSS for enemies already client-owned (always reclaim to server).
        /// </summary>
        private static void ApplyEnemySticky(ZDO zdo, Vector3 position, long uid, long serverUid)
        {
            bool nearPeer = IsNearAnyPeer(position);
            long owner = zdo.GetOwner();

            if (nearPeer)
            {
                if (owner != serverUid)
                {
                    zdo.SetOwner(serverUid);
                    LastStickyReclaims++;
                    _pendingReclaimLogs++;
                    MaybeLogReclaim(owner, serverUid);
                }
                LastStickyKeeps++;
            }
            else if (owner == uid || owner == serverUid)
            {
                zdo.SetOwner(0L);
                LastStickyReleases++;
            }
        }

        /// <summary>Vanilla <c>ReleaseNearbyZDOS</c> body for non-enemy ZDOs.</summary>
        private static void ApplyVanillaReclaim(
            ZDOMan zdoMan, ZDO zdo, Vector3 position, Vector2s zone, long uid)
        {
            if (zdo.GetOwner() == uid)
            {
                if (!ZNetScene.InActiveArea(position, zone))
                    zdo.SetOwner(0L);
            }
            else if ((!zdo.HasOwner() || !IsInPeerActiveArea(position, zdo.GetOwner()))
                     && ZNetScene.InActiveArea(position, zone))
            {
                zdo.SetOwner(uid);
            }
        }

        private static bool IsNearAnyPeer(Vector3 position)
        {
            List<ZNetPeer>? peers = null;
            try { peers = ZNet.instance.GetPeers(); }
            catch { peers = null; }
            if (peers == null || peers.Count == 0)
                return false;

            foreach (var peer in peers)
            {
                if (peer == null)
                    continue;
                Vector3 refPos;
                try { refPos = peer.GetRefPos(); }
                catch { continue; }
                if (refPos == Vector3.zero)
                    continue;
                if (float.IsNaN(refPos.x) || float.IsNaN(refPos.y) || float.IsNaN(refPos.z))
                    continue;
                try
                {
                    if (ZNetScene.InActiveArea(position, refPos))
                        return true;
                }
                catch
                {
                    // ignore bad peer ref
                }
            }

            return false;
        }

        /// <summary>Mirrors private <c>ZDOMan.IsInPeerActiveArea</c> without Traverse.</summary>
        private static bool IsInPeerActiveArea(Vector3 point, long ownerUid)
        {
            if (ownerUid == ZNet.GetUID())
            {
                try
                {
                    return ZNetScene.InActiveArea(point, ZNet.instance.GetReferencePosition());
                }
                catch
                {
                    return false;
                }
            }

            ZNetPeer? peer = null;
            try { peer = ZNet.instance.GetPeer(ownerUid); }
            catch { peer = null; }
            if (peer == null)
                return false;
            try
            {
                return ZNetScene.InActiveArea(point, peer.GetRefPos());
            }
            catch
            {
                return false;
            }
        }

        private static void MaybeLogReclaim(long fromOwner, long serverUid)
        {
            // Throttle: at most one summary every ~5s via UpdateAI-style age using Time.unscaledTime.
            float now;
            try { now = Time.unscaledTime; }
            catch { now = 0f; }

            if (_reclaimLogAge > 0f && now - _reclaimLogAge < 5f)
                return;
            if (_pendingReclaimLogs <= 0)
                return;

            _reclaimLogAge = now;
            var n = _pendingReclaimLogs;
            _pendingReclaimLogs = 0;
            Plugin.Log?.LogInfo(
                $"StickyEnemyOwnership: reclaimed {n} enemy ZDO(s) from non-server owners " +
                $"(example from={fromOwner} → server={serverUid}).");
        }
    }
}
#endif
