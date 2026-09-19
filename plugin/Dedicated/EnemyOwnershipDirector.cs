#if VALHEIM_REFS
using System;
using System.Collections.Generic;
using System.Reflection;
using FactionTactics.Config;
using FactionTactics.Util;
using UnityEngine;

namespace FactionTactics.Dedicated
{
    /// <summary>
    /// 0.1.9 ownership PoC + 0.1.10 live MonsterAI cache for discovery: force server ZDO ownership + CreateObject for enemy prefabs near
    /// connected peers so MonsterAI.UpdateAI can run on dedicated Linux without full SSS.
    /// Does not patch ZoneSystem / CreateDestroyObjects; leaves trees, buildings, ships on
    /// vanilla client authority.
    /// </summary>
    public sealed class EnemyOwnershipDirector
    {
        private static MethodInfo? _createObjectMethod;
        private static bool _createObjectResolved;
        private static bool _loggedEnabled;
        private static bool _loggedCreateMissing;
        private static readonly object LiveGate = new object();
        private static readonly Dictionary<int, MonsterAI> LiveMonsterAIs = new Dictionary<int, MonsterAI>();
        private static bool _loggedLiveCache;

        private float _age;

        /// <summary>Enemy ZDOs claimed by server this last pass (or already owned).</summary>
        public static int LastEnemyOwned { get; private set; }

        /// <summary>Enemy ZDOs with live ZNetScene.FindInstance after pass.</summary>
        public static int LastEnemyLive { get; private set; }

        /// <summary>Live enemy instances that yielded MonsterAI.</summary>
        public static int LastEnemyMai { get; private set; }

        /// <summary>CreateObject invocations this last pass.</summary>
        public static int LastCreates { get; private set; }

        /// <summary>Enemy ZDOs considered this last pass.</summary>
        public static int LastEnemySeen { get; private set; }

        /// <summary>
        /// Live MonsterAI components from the last ownership pass (and retained until destroyed).
        /// Primary feed for SquadDiscovery when Harmony registry Snapshot was empty in 0.1.9 smoke.
        /// </summary>
        public static List<MonsterAI> SnapshotLiveMonsterAIs()
        {
            lock (LiveGate)
            {
                var list = new List<MonsterAI>(LiveMonsterAIs.Count);
                var dead = new List<int>();
                foreach (var kv in LiveMonsterAIs)
                {
                    try
                    {
                        if (kv.Value == null)
                        {
                            dead.Add(kv.Key);
                            continue;
                        }
                        list.Add(kv.Value);
                    }
                    catch
                    {
                        dead.Add(kv.Key);
                    }
                }
                foreach (var id in dead)
                    LiveMonsterAIs.Remove(id);
                return list;
            }
        }

        private static void RememberLiveMonsterAI(MonsterAI? mai)
        {
            if (mai == null)
                return;
            int id;
            try { id = mai.GetInstanceID(); }
            catch { return; }
            if (id == 0)
                return;
            lock (LiveGate)
            {
                LiveMonsterAIs[id] = mai;
                if (!_loggedLiveCache)
                {
                    _loggedLiveCache = true;
                    Plugin.Log?.LogInfo(
                        $"EnemyOwnershipDirector: caching live MonsterAI for discovery id={id} name={mai.gameObject?.name}");
                }
            }
            FactionTactics.HarmonyPatches.MonsterAIRegistry.Register(mai);
        }

        public void Tick(float dt)
        {
            if (PluginConfig.EnableEnemyServerOwnership?.Value != true)
                return;

            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return;

            if (ZDOMan.instance == null || ZNetScene.instance == null)
                return;

            if (!_loggedEnabled)
            {
                _loggedEnabled = true;
                Plugin.Log?.LogInfo(
                    "EnemyOwnershipDirector enabled (PoC): server SetOwner + CreateObject for enemy prefabs near peers; " +
                    "trees/buildings/ships left on vanilla client authority.");
            }

            var interval = Math.Max(0.25f, PluginConfig.EnemyOwnershipIntervalSeconds?.Value ?? 1f);
            _age += dt;
            if (_age < interval)
                return;
            _age = 0f;

            LastEnemyOwned = 0;
            LastEnemyLive = 0;
            LastEnemyMai = 0;
            LastCreates = 0;
            LastEnemySeen = 0;

            try
            {
                RunPass();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning(
                    $"EnemyOwnershipDirector tick failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void RunPass()
        {
            var peers = ZNet.instance.GetConnectedPeers();
            if (peers == null || peers.Count == 0)
                peers = ZNet.instance.GetPeers();
            if (peers == null || peers.Count == 0)
                return;

            var serverUid = ZNet.GetUID();
            var near = new List<ZDO>(256);
            var distant = new List<ZDO>(256);
            var visited = new HashSet<long>();
            var maxCreates = Math.Max(1, PluginConfig.EnemyOwnershipMaxCreatesPerTick?.Value ?? 16);
            var creates = 0;
            var owned = 0;
            var live = 0;
            var mai = 0;
            var seen = 0;
            var simDist = SimulationDistance.OriginalDistance;

            foreach (var peer in peers)
            {
                if (peer == null)
                    continue;

                Vector3 refPos;
                try { refPos = peer.GetRefPos(); }
                catch { continue; }

                // Skip unset / garbage ref positions (vanilla dedicated GetReferencePosition is outside world;
                // peer ref pos should be real once the client syncs).
                if (refPos == Vector3.zero)
                    continue;
                if (float.IsNaN(refPos.x) || float.IsNaN(refPos.y) || float.IsNaN(refPos.z))
                    continue;

                Vector2s zone;
                try { zone = ZoneSystem.GetZone(refPos); }
                catch { continue; }

                near.Clear();
                distant.Clear();
                try
                {
                    ZDOMan.instance.FindSectorObjects(zone, simDist, near, distant);
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning(
                        $"EnemyOwnership FindSectorObjects failed: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                foreach (var batch in new[] { near, distant })
                {
                    foreach (var zdo in batch)
                    {
                        if (zdo == null)
                            continue;

                        long key;
                        try { key = ValheimIds.ToLong(zdo.m_uid); }
                        catch { key = zdo.GetHashCode(); }
                        if (!visited.Add(key))
                            continue;

                        if (!ValheimWorldScan.IsLikelyEnemyPrefabZdo(zdo))
                            continue;

                        seen++;

                        try
                        {
                            if (!zdo.IsOwner())
                                zdo.SetOwner(serverUid);
                            owned++;
                        }
                        catch (Exception ex)
                        {
                            if (PluginConfig.DebugLogging?.Value == true)
                            {
                                Plugin.Log?.LogDebug(
                                    $"EnemyOwnership SetOwner failed: {ex.GetType().Name}: {ex.Message}");
                            }
                            continue;
                        }

                        ZNetView? nv = null;
                        try { nv = ZNetScene.instance.FindInstance(zdo); }
                        catch { nv = null; }

                        if (nv == null && creates < maxCreates)
                        {
                            if (TryCreateObject(zdo))
                            {
                                creates++;
                                try { nv = ZNetScene.instance.FindInstance(zdo); }
                                catch { nv = null; }
                            }
                        }

                        if (nv == null)
                            continue;

                        live++;
                        try
                        {
                            MonsterAI? foundMai = null;
                            try { foundMai = nv.GetComponent<MonsterAI>(); } catch { /* ignore */ }
                            if (foundMai == null)
                            {
                                try { foundMai = nv.GetComponentInChildren<MonsterAI>(true); } catch { /* ignore */ }
                            }
                            if (foundMai != null)
                            {
                                mai++;
                                RememberLiveMonsterAI(foundMai);
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log?.LogWarning(
                                $"EnemyOwnership live MA harvest failed: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
            }

            LastEnemySeen = seen;
            LastEnemyOwned = owned;
            LastEnemyLive = live;
            LastEnemyMai = mai;
            LastCreates = creates;
        }

        private static bool TryCreateObject(ZDO zdo)
        {
            if (!_createObjectResolved)
            {
                _createObjectResolved = true;
                try
                {
                    _createObjectMethod = typeof(ZNetScene).GetMethod(
                        "CreateObject",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                        binder: null,
                        types: new[] { typeof(ZDO) },
                        modifiers: null);
                }
                catch (Exception ex)
                {
                    _createObjectMethod = null;
                    Plugin.Log?.LogWarning(
                        $"EnemyOwnership CreateObject resolve failed: {ex.GetType().Name}: {ex.Message}");
                }

                if (_createObjectMethod == null && !_loggedCreateMissing)
                {
                    _loggedCreateMissing = true;
                    Plugin.Log?.LogWarning(
                        "EnemyOwnership: ZNetScene.CreateObject not found via reflection — ownership-only mode.");
                }
            }

            if (_createObjectMethod == null || ZNetScene.instance == null)
                return false;

            try
            {
                var go = _createObjectMethod.Invoke(ZNetScene.instance, new object[] { zdo }) as GameObject;
                return go != null;
            }
            catch (Exception ex)
            {
                if (PluginConfig.DebugLogging?.Value == true)
                {
                    Plugin.Log?.LogDebug(
                        $"EnemyOwnership CreateObject invoke failed: {ex.GetType().Name}: {ex.Message}");
                }
                return false;
            }
        }
    }
}
#endif
