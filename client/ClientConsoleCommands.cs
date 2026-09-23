using System;
using System.Collections.Generic;
using System.Linq;
using FactionTactics.Combat;
using FactionTactics.ConsoleCmds;
using FactionTactics.Orders;

namespace FactionTactics.Client
{
    /// <summary>
    /// 1.0.7: full <c>ft help|get|set|reload|status</c> on pure clients.
    /// Mutating commands RPC to dedicated (FtConfigRpc); admin-only.
    /// Skips registration when server FactionTactics.dll is also loaded (listen-host).
    /// </summary>
    internal static class ClientConsoleCommands
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered)
                return;

            if (Type.GetType("FactionTactics.Plugin, FactionTactics", throwOnError: false) != null)
            {
                ClientPlugin.Log?.LogInfo("FactionTactics.Client: skipping ft console (server plugin present).");
                return;
            }

            _registered = true;
#if VALHEIM_REFS
            FtConfigRpc.LocalPrint = line =>
            {
                try
                {
                    // Valheim Terminal console (not System.Console)
                    var c = global::Console.instance;
                    if (c != null)
                        c.Print(line);
                }
                catch { /* ok */ }
                try { ClientPlugin.Log?.LogInfo(line); } catch { /* ok */ }
            };

            _ = new Terminal.ConsoleCommand(
                "ft",
                "Faction Tactics admin: ft help | get <key> | set <key> <value> | reload | status (RPC to dedicated)",
                (Terminal.ConsoleEventArgs args) => Run(args),
                isCheat: false,
                isNetwork: false,
                onlyServer: false,
                isSecret: false,
                allowInDevBuild: true,
                hideBehindDevCommands: false,
                optionsFetcher: TabOptions,
                alwaysRefreshTabOptions: true,
                remoteCommand: false,
                onlyAdmin: true);
            ClientPlugin.Log?.LogInfo("FactionTactics.Client: registered admin 'ft' (RPC to dedicated).");
#endif
        }

#if VALHEIM_REFS
        private static List<string> TabOptions()
        {
            return new List<string>
            {
                "help", "get", "set", "reload", "status",
                "HoldAttackCooldown", "SwingStaggerMs", "HoldAttackRangeFactor",
                "FormationReshuffleSeconds", "FormationCasualtyReshuffle", "ChargeMaxSeconds",
                "OrderMinDwellSeconds", "OrderScoreHysteresis", "RomanWallOuter", "RomanWallInner",
                "RomanStandoffDistance", "RomanStandoffHoldMin", "RomanStandoffHoldMax",
                "RomanContactSwingRange", "RomanRetreatPauseSeconds",
                "VikingStandoffDistance", "VikingIndoorsStandoff", "VikingStandoffHoldMin", "VikingStandoffHoldMax",
                "VikingContactSwingRange", "VikingRetreatPauseSeconds",
                "FlankSplitMeters", "IsolateBuddyMeters",
                "DiscoveryBurstOnSpawn", "DiscoveryBurstSeconds",
                "TickIntervalSeconds", "MinSquadSize", "DiscoveryRadius",
            };
        }

        private static void Run(Terminal.ConsoleEventArgs args)
        {
            if (!FtConfigRpc.LocalPlayerIsAdmin())
            {
                args.Context.AddString("[ft] admin only (ZNet admin list / host)");
                return;
            }

            var sub = args.Length >= 2 ? args[1].ToLowerInvariant() : "help";
            switch (sub)
            {
                case "help":
                case "?":
                    PrintLocalHelp(args);
                    FtConfigRpc.SendCmd("help");
                    break;
                case "get":
                    if (args.Length < 3)
                    {
                        args.Context.AddString("[ft] usage: ft get <key>");
                        return;
                    }
                    FtConfigRpc.SendCmd("get", args[2]);
                    break;
                case "set":
                    if (args.Length < 4)
                    {
                        args.Context.AddString("[ft] usage: ft set <key> <value>");
                        return;
                    }
                    {
                        var key = args[2];
                        var raw = string.Join(" ", args.Args.Skip(3));
                        args.Context.AddString($"[ft] requesting server set {key}={raw} …");
                        FtConfigRpc.SendCmd("set", key, raw);
                    }
                    break;
                case "reload":
                    args.Context.AddString("[ft] requesting server reload …");
                    FtConfigRpc.SendCmd("reload");
                    break;
                case "status":
                    PrintLocalStatus(args);
                    FtConfigRpc.SendCmd("status");
                    break;
                default:
                    args.Context.AddString($"[ft] unknown subcommand '{args[1]}' — try: ft help");
                    break;
            }
        }

        private static void PrintLocalHelp(Terminal.ConsoleEventArgs args)
        {
            args.Context.AddString($"[ft] FactionTactics.Client {ClientPlugin.PluginVersion} (admin RPC → dedicated)");
            args.Context.AddString("  ft help | get <key> | set <key> <value> | reload | status");
            args.Context.AddString("  Mutating commands apply on the dedicated server PluginConfig (persisted).");
            args.Context.AddString("  Phase A/B knobs: HoldAttackCooldown, SwingStaggerMs, FormationReshuffleSeconds,");
            args.Context.AddString("    ChargeMaxSeconds, OrderMinDwellSeconds, OrderScoreHysteresis,");
            args.Context.AddString("    RomanStandoffDistance, RomanStandoffHoldMin/Max, RomanContactSwingRange, RomanRetreatPauseSeconds,");
            args.Context.AddString("    VikingStandoffDistance, VikingIndoorsStandoff, VikingContactSwingRange, FlankSplitMeters, DiscoveryBurst*");
            args.Context.AddString($"  Local executor tuning: HoldAttackCooldown={CombatTuning.HoldAttackCooldown} SwingStaggerMs={CombatTuning.SwingStaggerMs}");
        }

        private static void PrintLocalStatus(Terminal.ConsoleEventArgs args)
        {
            args.Context.AddString($"[ft] FactionTactics.Client {ClientPlugin.PluginVersion} product={FtVersion.ProductVersion} schema=v{FtVersion.IntentSchemaVersion}");
            args.Context.AddString(
                $"[ft] local: swings={CombatDriver.Swings} holdBlocks={CombatDriver.HoldBlocks} drives={ClientMonsterAI_UpdateAI_Patch.DriveHits} rpcDrives={ClientMonsterAI_UpdateAI_Patch.RpcDriveHits}");
            args.Context.AddString(
                $"[ft] zdo: readsOk={IntentZdoSync.ReadsOk} stale={IntentZdoSync.ReadsStale} mismatch={IntentZdoSync.SchemaMismatches} rpcRecv={IntentRpcSync.RpcIntentsReceived} rpcPkts={IntentRpcSync.RpcPacketsReceived} rpcHits={IntentRpcSync.RpcCacheHits}");
            args.Context.AddString(
                $"[ft] tuning: HoldAttackCooldown={CombatTuning.HoldAttackCooldown} SwingStaggerMs={CombatTuning.SwingStaggerMs} RangeFactor={CombatTuning.HoldAttackRangeFactor}");
            args.Context.AddString("[ft] requesting server status …");
        }
#endif
    }
}
