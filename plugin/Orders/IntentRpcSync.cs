using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using FactionTactics.Doctrine;
using FactionTactics.Util;
using UnityEngine;

namespace FactionTactics.Orders
{
    /// <summary>
    /// Primary hybrid transport (1.0.4): server broadcasts <see cref="MemberIntent"/> batches
    /// via ZRoutedRpc + ZPackage. Clients cache by packed ZDOID; combat executor reads cache first.
    /// <para>
    /// 1.0.4: dedicated unreliable often fails <c>Everybody</c> delivery — FlushBroadcast iterates
    /// <c>ZNet.GetPeers()</c> and invokes per <c>peer.m_uid</c> (cloned ZPackage), with Everybody backup.
    /// </para>
    /// <para>
    /// ZDO custom fields (<see cref="IntentZdoSync"/>) remain an optional/debug fallback —
    /// <c>ZDO.Set</c> from a non-owner peer (dedicated commander) is unreliable and often never
    /// reaches the owning client (1.0.1–1.0.2: schema mismatch zdo=0 local=2 despite server writes).
    /// </para>
    /// </summary>
    public static class IntentRpcSync
    {
        public const string RpcName = "FT_MemberIntents";

        private static readonly ConcurrentDictionary<long, PendingEntry> Pending
            = new ConcurrentDictionary<long, PendingEntry>();

        private static readonly ConcurrentDictionary<long, CacheEntry> Cache
            = new ConcurrentDictionary<long, CacheEntry>();

        public static long RpcIntentsSent { get; private set; }
        public static long RpcBatchesSent { get; private set; }
        public static long RpcPeerInvokes { get; private set; }
        public static long RpcIntentsReceived { get; private set; }
        public static long RpcPacketsReceived { get; private set; }
        public static long RpcCacheHits { get; private set; }
        public static long RpcSchemaMismatches { get; private set; }

        /// <summary>True after a successful Register against the current ZRoutedRpc.instance.</summary>
        public static bool IsRegistered => _registered;

        private static bool _registered;
        private static object? _registeredOn; // ZRoutedRpc.instance identity
        private static bool _loggedFirstApply;
        private static bool _loggedFirstPacket;
        private static bool _loggedRegister;

        private struct PendingEntry
        {
            public MemberIntent Intent;
            public float WrittenAt;
        }

        private struct CacheEntry
        {
            public MemberIntent Intent;
            public float WrittenAt;
        }

        public static void ResetCounters()
        {
            RpcIntentsSent = RpcBatchesSent = RpcPeerInvokes = 0;
            RpcIntentsReceived = RpcPacketsReceived = RpcCacheHits = RpcSchemaMismatches = 0;
            _loggedFirstApply = false;
            _loggedFirstPacket = false;
            Pending.Clear();
            Cache.Clear();
        }

        /// <summary>Queue one intent for the next <see cref="FlushBroadcast"/> (server).</summary>
        public static void Queue(long zdoidPacked, MemberIntent intent, float nowSeconds)
        {
            if (zdoidPacked == 0 || intent == null)
                return;
            Pending[zdoidPacked] = new PendingEntry { Intent = Clone(intent), WrittenAt = nowSeconds };
        }

#if VALHEIM_REFS
        /// <summary>
        /// Register the routed RPC handler. Safe to call repeatedly; no-ops until
        /// <c>ZRoutedRpc.instance</c> exists. Re-registers if the instance is replaced
        /// (disconnect / rejoin).
        /// </summary>
        public static void EnsureRegistered()
        {
            try
            {
                var inst = ZRoutedRpc.instance;
                if (inst == null)
                    return;
                if (_registered && ReferenceEquals(_registeredOn, inst))
                    return;

                inst.Register(RpcName, new Action<long, ZPackage>(OnRouted));
                _registered = true;
                _registeredOn = inst;
                if (!_loggedRegister)
                {
                    _loggedRegister = true;
                    FactionTacticsLog.Debug($"[FT] IntentRpcSync registered RPC '{RpcName}'");
                    try
                    {
                        Debug.Log($"[FactionTactics] IntentRpcSync registered '{RpcName}' (RPC hybrid transport).");
                    }
                    catch { /* headless ok */ }
                }
                else
                {
                    FactionTacticsLog.Debug($"[FT] IntentRpcSync re-registered RPC '{RpcName}' (ZRoutedRpc instance changed).");
                }
            }
            catch (Exception ex)
            {
                FactionTacticsLog.Debug($"IntentRpcSync.EnsureRegistered failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Pack pending intents into one ZPackage and deliver to all connected peers.
        /// Dedicated: prefer per-peer <see cref="ZRoutedRpc.InvokeRoutedRPC(long,string,object[])"/>;
        /// <c>Everybody</c> alone is unreliable from dedicated. Call once per commander tick.
        /// </summary>
        public static void FlushBroadcast()
        {
            EnsureRegistered();
            if (Pending.Count == 0)
                return;
            if (ZRoutedRpc.instance == null)
                return;

            try
            {
                var snap = new List<KeyValuePair<long, PendingEntry>>(Pending.Count);
                foreach (var kv in Pending)
                    snap.Add(kv);
                Pending.Clear();
                if (snap.Count == 0)
                    return;

                var pkg = new ZPackage();
                pkg.Write(IntentZdoCodec.SchemaVersion);
                pkg.Write(snap.Count);
                foreach (var kv in snap)
                {
                    WriteOne(pkg, kv.Key, kv.Value.Intent, kv.Value.WrittenAt);
                    RpcIntentsSent++;
                }

                // Snapshot bytes once; clone per invoke so buffer/pos is not consumed across peers.
                var bytes = pkg.GetArray();
                var peerInvokes = 0;
                long selfUid = 0;
                try { selfUid = ZNet.GetUID(); } catch { /* ok */ }

                try
                {
                    if (ZNet.instance != null)
                    {
                        var peers = ZNet.instance.GetPeers();
                        if (peers != null)
                        {
                            for (var i = 0; i < peers.Count; i++)
                            {
                                var peer = peers[i];
                                if (peer == null || peer.m_uid == 0)
                                    continue;
                                if (selfUid != 0 && peer.m_uid == selfUid)
                                    continue;
                                // params object[] — pass ZPackage directly, not new object[] { pkg }
                                ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RpcName, new ZPackage(bytes));
                                peerInvokes++;
                                RpcPeerInvokes++;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    FactionTacticsLog.Debug(
                        $"IntentRpcSync.FlushBroadcast peer iterate: {ex.GetType().Name}: {ex.Message}");
                }

                // Backup: Everybody (listen-host / when peer list empty). Clone again.
                try
                {
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, new ZPackage(bytes));
                }
                catch (Exception ex)
                {
                    FactionTacticsLog.Debug(
                        $"IntentRpcSync.FlushBroadcast Everybody: {ex.GetType().Name}: {ex.Message}");
                }

                RpcBatchesSent++;
                if (peerInvokes == 0)
                {
                    FactionTacticsLog.Debug(
                        $"[FT] IntentRpcSync FlushBroadcast: no peers targeted; Everybody backup only (intents={snap.Count}).");
                }
            }
            catch (Exception ex)
            {
                FactionTacticsLog.Debug($"IntentRpcSync.FlushBroadcast failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void OnRouted(long senderPeerId, ZPackage pkg)
        {
            RpcPacketsReceived++;
            if (pkg == null)
            {
                LogFirstPacket(senderPeerId, count: -1, note: "null pkg");
                return;
            }
            try
            {
                var ver = pkg.ReadInt();
                if (ver != IntentZdoCodec.SchemaVersion)
                {
                    RpcSchemaMismatches++;
                    LogFirstPacket(senderPeerId, count: -1, note: $"schema mismatch rpc={ver}");
                    FactionTacticsLog.Debug(
                        $"[FT] IntentRpcSync schema mismatch rpc={ver} local={IntentZdoCodec.SchemaVersion}");
                    return;
                }

                var count = pkg.ReadInt();
                LogFirstPacket(senderPeerId, count, note: null);
                if (count < 0 || count > 4096)
                    return;

                var now = Time.time;
                for (var i = 0; i < count; i++)
                {
                    if (!TryReadOne(pkg, out var id, out var intent, out var writtenAt))
                        break;
                    if (id == 0 || intent == null)
                        continue;
                    // Prefer sender clock; if wildly off, stamp local now so IsFresh still works.
                    if (!IntentZdoCodec.IsFresh(writtenAt, now))
                        writtenAt = now;
                    Cache[id] = new CacheEntry { Intent = intent, WrittenAt = writtenAt };
                    RpcIntentsReceived++;
                }
            }
            catch (Exception ex)
            {
                FactionTacticsLog.Debug($"IntentRpcSync.OnRouted failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void LogFirstPacket(long senderPeerId, int count, string? note)
        {
            if (_loggedFirstPacket)
                return;
            _loggedFirstPacket = true;
            var extra = string.IsNullOrEmpty(note) ? "" : $" {note}";
            var msg =
                $"[FactionTactics] RPC OnRouted first packet sender={senderPeerId} count={count} packets={RpcPacketsReceived}{extra}";
            try
            {
                Debug.Log(msg);
            }
            catch { /* headless */ }
            try
            {
                // BepInEx Info when available (client / server plugin log sinks via Unity + FactionTacticsLog).
                FactionTacticsLog.Debug(msg);
            }
            catch { /* ok */ }
        }

        private static void WriteOne(ZPackage pkg, long id, MemberIntent intent, float writtenAt)
        {
            pkg.Write(id);
            pkg.Write((int)IntentZdoCodec.PackFlags(intent));
            pkg.Write((int)intent.OrderKind);
            pkg.Write((int)intent.Formation);
            pkg.Write((int)intent.Stance);
            pkg.Write((int)intent.Role);
            pkg.Write(intent.DesiredPosition);
            pkg.Write(intent.FocusTargetId ?? 0L);
            pkg.Write(writtenAt);
            pkg.Write(IntentZdoSync.StableHash(intent.SquadId));
        }

        private static bool TryReadOne(ZPackage pkg, out long id, out MemberIntent intent, out float writtenAt)
        {
            id = 0;
            intent = null!;
            writtenAt = 0f;
            try
            {
                id = pkg.ReadLong();
                var flags = (IntentZdoCodec.IntentFlags)pkg.ReadInt();
                var order = (DoctrineOrderKind)pkg.ReadInt();
                var formation = (FormationType)pkg.ReadInt();
                var stance = (StanceType)pkg.ReadInt();
                var role = (SquadRole)pkg.ReadInt();
                var slot = pkg.ReadVector3();
                var focus = pkg.ReadLong();
                writtenAt = pkg.ReadSingle();
                _ = pkg.ReadInt(); // squad hash (reserved)

                if (!IntentZdoCodec.IsActive(flags))
                    return true; // consumed bytes; skip inactive

                intent = new MemberIntent
                {
                    OrderKind = order,
                    Formation = formation,
                    Stance = stance,
                    Role = role,
                    DesiredPosition = slot,
                    FocusTargetId = focus != 0L ? focus : (long?)null,
                    SquadId = "",
                };
                IntentZdoCodec.ApplyFlags(intent, flags);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGet(long zdoidPacked, float nowSeconds, out MemberIntent intent)
        {
            intent = null!;
            if (zdoidPacked == 0)
                return false;
            if (!Cache.TryGetValue(zdoidPacked, out var entry))
                return false;
            if (!IntentZdoCodec.IsFresh(entry.WrittenAt, nowSeconds))
                return false;
            intent = entry.Intent;
            RpcCacheHits++;
            if (!_loggedFirstApply)
            {
                _loggedFirstApply = true;
                try
                {
                    Debug.Log(
                        $"[FactionTactics] RPC intent applied (first) zdoid={zdoidPacked} order={intent.OrderKind} formation={intent.Formation}");
                }
                catch { /* ok */ }
                FactionTacticsLog.Debug(
                    $"[FT] first RPC intent applied zdoid={zdoidPacked} order={intent.OrderKind}");
            }
            return true;
        }

        public static bool TryGetFromMonsterAI(MonsterAI ai, float nowSeconds, out MemberIntent intent)
        {
            intent = null!;
            try
            {
                var ch = ValheimIds.GetCharacter(ai);
                var id = ValheimIds.FromCharacter(ch);
                return TryGet(id, nowSeconds, out intent);
            }
            catch
            {
                return false;
            }
        }
#else
        public static void EnsureRegistered() { }

        public static void FlushBroadcast()
        {
            Pending.Clear();
        }

        public static bool TryGet(long zdoidPacked, float nowSeconds, out MemberIntent intent)
        {
            intent = null!;
            _ = zdoidPacked;
            _ = nowSeconds;
            return false;
        }

        public static bool TryGetFromMonsterAI(object ai, float nowSeconds, out MemberIntent intent)
        {
            intent = null!;
            _ = ai;
            _ = nowSeconds;
            return false;
        }
#endif

        private static MemberIntent Clone(MemberIntent src)
        {
            return new MemberIntent
            {
                SquadId = src.SquadId,
                OrderKind = src.OrderKind,
                Formation = src.Formation,
                Stance = src.Stance,
                Role = src.Role,
                DesiredPosition = src.DesiredPosition,
                FocusTargetId = src.FocusTargetId,
                HoldGround = src.HoldGround,
                PreferRun = src.PreferRun,
                AllowVanillaChase = src.AllowVanillaChase,
                PreferKeepRange = src.PreferKeepRange,
                AssaultWallBreaker = src.AssaultWallBreaker,
                AssaultMissileCover = src.AssaultMissileCover,
                AllowVanillaStructure = src.AllowVanillaStructure,
                DeathRush = src.DeathRush,
            };
        }
    }
}
