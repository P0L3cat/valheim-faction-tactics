using System;
using System.Collections.Generic;
using FactionTactics.Combat;
using FactionTactics.Util;
using HarmonyLib;

namespace FactionTactics.ConsoleCmds
{
    /// <summary>
    /// 1.0.6: client admin <c>ft</c> → dedicated PluginConfig via ZRoutedRpc.
    /// Shared file: linked into FactionTactics.Client (FT_CLIENT) and server plugin.
    /// </summary>
    public static class FtConfigRpc
    {
        public const string RpcCmd = "FT_ConfigCmd";
        public const string RpcReply = "FT_ConfigReply";
        public const string RpcSync = "FT_ConfigSync";

        private static bool _registered;
        private static object? _registeredOn;

        /// <summary>Optional UI sink (client Terminal Print).</summary>
        public static Action<string>? LocalPrint { get; set; }

        public static bool IsRegistered => _registered;

        public static void EnsureRegistered()
        {
#if VALHEIM_REFS
            try
            {
                var inst = ZRoutedRpc.instance;
                if (inst == null)
                    return;
                if (_registered && ReferenceEquals(_registeredOn, inst))
                    return;

                inst.Register(RpcCmd, new Action<long, ZPackage>(OnCmd));
                inst.Register(RpcReply, new Action<long, ZPackage>(OnReply));
                inst.Register(RpcSync, new Action<long, ZPackage>(OnSync));
                _registered = true;
                _registeredOn = inst;
                FactionTacticsLog.Debug($"[FT] FtConfigRpc registered {RpcCmd}/{RpcReply}/{RpcSync}");
            }
            catch (Exception ex)
            {
                FactionTacticsLog.Debug($"FtConfigRpc.EnsureRegistered: {ex.GetType().Name}: {ex.Message}");
            }
#endif
        }

#if VALHEIM_REFS
        /// <summary>Client: send admin command to dedicated server.</summary>
        public static void SendCmd(string op, string? key = null, string? value = null)
        {
            EnsureRegistered();
            if (ZRoutedRpc.instance == null || ZNet.instance == null)
            {
                PushLocal("[ft] not connected");
                return;
            }
            try
            {
                var pkg = new ZPackage();
                pkg.Write(op ?? "");
                pkg.Write(key ?? "");
                pkg.Write(value ?? "");
                var bytes = pkg.GetArray();

                try
                {
                    var server = ZNet.instance.GetServerPeer();
                    if (server != null && server.m_uid != 0)
                    {
                        ZRoutedRpc.instance.InvokeRoutedRPC(server.m_uid, RpcCmd, new ZPackage(bytes));
                        return;
                    }
                }
                catch { /* fall through */ }

                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcCmd, new ZPackage(bytes));
            }
            catch (Exception ex)
            {
                PushLocal($"[ft] RPC send failed: {ex.Message}");
            }
        }

        private static void OnCmd(long senderPeerId, ZPackage pkg)
        {
            if (pkg == null)
                return;
            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return;

#if FT_CLIENT
            // Pure client DLL never applies config — server DLL owns PluginConfig.
            return;
#else
            string op, key, value;
            try
            {
                op = pkg.ReadString() ?? "";
                key = pkg.ReadString() ?? "";
                value = pkg.ReadString() ?? "";
            }
            catch
            {
                return;
            }

            if (!PeerIsAdmin(senderPeerId))
            {
                Reply(senderPeerId, "[ft] denied: admin only");
                Plugin.Log?.LogWarning($"[ft] denied peer={senderPeerId} op={op}");
                return;
            }

            var lines = new List<string>();
            try
            {
                switch ((op ?? "").ToLowerInvariant())
                {
                    case "help":
                        lines.AddRange(FtConsoleCommands.BuildHelpLines());
                        break;
                    case "get":
                        lines.Add(FtConsoleCommands.BuildGetLine(key));
                        break;
                    case "set":
                        lines.Add(FtConsoleCommands.ApplySet(key, value, out var changed));
                        if (changed)
                        {
                            CombatTuning.CopyFromPluginConfig();
                            BroadcastSync();
                        }
                        break;
                    case "reload":
                        lines.Add(FtConsoleCommands.ApplyReload());
                        CombatTuning.CopyFromPluginConfig();
                        BroadcastSync();
                        break;
                    case "status":
                        lines.AddRange(FtConsoleCommands.BuildStatusLines());
                        break;
                    default:
                        lines.Add($"[ft] unknown op '{op}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                lines.Add($"[ft] error: {ex.Message}");
            }

            Reply(senderPeerId, string.Join("\n", lines));
#endif
        }

        private static void Reply(long peerId, string text)
        {
            try
            {
                EnsureRegistered();
                if (ZRoutedRpc.instance == null)
                    return;
                var pkg = new ZPackage();
                pkg.Write(text ?? "");
                if (peerId != 0)
                    ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcReply, pkg);
            }
            catch (Exception ex)
            {
                FactionTacticsLog.Debug($"FtConfigRpc.Reply: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void BroadcastSync()
        {
            try
            {
                EnsureRegistered();
                if (ZRoutedRpc.instance == null)
                    return;
                var pkg = new ZPackage();
                pkg.Write(CombatTuning.HoldAttackCooldown);
                pkg.Write(CombatTuning.HoldAttackRangeFactor);
                pkg.Write(CombatTuning.SwingStaggerMs);
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcSync, pkg);
            }
            catch (Exception ex)
            {
                FactionTacticsLog.Debug($"FtConfigRpc.BroadcastSync: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void OnReply(long senderPeerId, ZPackage pkg)
        {
            if (pkg == null)
                return;
            try
            {
                var text = pkg.ReadString() ?? "";
                foreach (var line in text.Split(new[] { '\n' }, StringSplitOptions.None))
                    PushLocal(line);
            }
            catch (Exception ex)
            {
                FactionTacticsLog.Debug($"FtConfigRpc.OnReply: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void OnSync(long senderPeerId, ZPackage pkg)
        {
            if (pkg == null)
                return;
            try
            {
                CombatTuning.HoldAttackCooldown = pkg.ReadSingle();
                CombatTuning.HoldAttackRangeFactor = pkg.ReadSingle();
                CombatTuning.SwingStaggerMs = pkg.ReadSingle();
            }
            catch (Exception ex)
            {
                FactionTacticsLog.Debug($"FtConfigRpc.OnSync: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void PushLocal(string line)
        {
            try { LocalPrint?.Invoke(line); } catch { /* ok */ }
            try { FactionTacticsLog.Debug(line); } catch { /* ok */ }
        }

        public static bool PeerIsAdmin(long peerId)
        {
            if (ZNet.instance == null)
                return false;
            try
            {
                if (peerId == 0 || peerId == ZNet.GetUID())
                {
                    try
                    {
                        if (ZNet.instance.LocalPlayerIsAdminOrHost())
                            return true;
                    }
                    catch { /* continue */ }
                }
            }
            catch { /* continue */ }

            try
            {
                var mi = AccessTools.Method(typeof(ZNet), "PlayerIsAdmin", new[] { typeof(long) });
                if (mi != null)
                    return (bool)mi.Invoke(ZNet.instance, new object[] { peerId });
            }
            catch { /* continue */ }

            try
            {
                var mi = AccessTools.Method(typeof(ZNet), "IsAdmin");
                if (mi != null)
                {
                    var ps = mi.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(long))
                        return (bool)mi.Invoke(ZNet.instance, new object[] { peerId });
                }
            }
            catch { /* continue */ }

            try
            {
                var peer = ZNet.instance.GetPeer(peerId);
                if (peer == null)
                    return false;
                string? id = null;
                try { id = peer.m_socket?.GetHostName(); } catch { /* ok */ }
                var listField = AccessTools.Field(typeof(ZNet), "m_adminList");
                if (listField != null && !string.IsNullOrEmpty(id))
                {
                    var list = listField.GetValue(ZNet.instance);
                    if (list != null)
                    {
                        var contains = AccessTools.Method(list.GetType(), "Contains")
                                       ?? AccessTools.Method(list.GetType(), "IsInList");
                        if (contains != null)
                            return (bool)contains.Invoke(list, new object[] { id });
                    }
                }
            }
            catch (Exception ex)
            {
                FactionTacticsLog.Debug($"PeerIsAdmin fallback: {ex.GetType().Name}: {ex.Message}");
            }

            return false;
        }

        /// <summary>Client-side: local player admin?</summary>
        public static bool LocalPlayerIsAdmin()
        {
            try
            {
                if (ZNet.instance != null && ZNet.instance.LocalPlayerIsAdminOrHost())
                    return true;
            }
            catch { /* continue */ }
            try
            {
                return PeerIsAdmin(ZNet.GetUID());
            }
            catch
            {
                return false;
            }
        }
#endif
    }
}
