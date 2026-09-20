using System;
using System.Collections.Generic;
using FactionTactics.Orders;

namespace FactionTactics.Client
{
    /// <summary>
    /// Read-only <c>ft status</c> / <c>ft help</c> on pure clients.
    /// Skips registration when the server FactionTactics.dll is also loaded (listen-host)
    /// so the full admin knobs win.
    /// </summary>
    internal static class ClientConsoleCommands
    {
        private static bool _registered;

        public static void RegisterReadOnly()
        {
            if (_registered)
                return;

            // Listen-host installs both DLLs — server plugin owns the full `ft` command.
            if (Type.GetType("FactionTactics.Plugin, FactionTactics", throwOnError: false) != null)
            {
                ClientPlugin.Log?.LogInfo("FactionTactics.Client: skipping ft console (server plugin present).");
                return;
            }

            _registered = true;
#if VALHEIM_REFS
            _ = new Terminal.ConsoleCommand(
                "ft",
                "Faction Tactics (client read-only): ft status | ft help",
                (Terminal.ConsoleEventArgs args) => Run(args),
                isCheat: false,
                isNetwork: false,
                onlyServer: false,
                isSecret: false,
                allowInDevBuild: true,
                hideBehindDevCommands: false,
                optionsFetcher: () => new List<string> { "help", "status" },
                alwaysRefreshTabOptions: true,
                remoteCommand: false,
                onlyAdmin: false);
            ClientPlugin.Log?.LogInfo("FactionTactics.Client: registered read-only 'ft status'.");
#endif
        }

#if VALHEIM_REFS
        private static void Run(Terminal.ConsoleEventArgs args)
        {
            var sub = args.Length >= 2 ? args[1].ToLowerInvariant() : "status";
            if (sub == "help" || sub == "?")
            {
                args.Context.AddString($"[ft] FactionTactics.Client {ClientPlugin.PluginVersion} (read-only)");
                args.Context.AddString("  ft status — version / schema / local ZDO read counters");
                args.Context.AddString("  Full get/set/reload knobs require the server DLL (dedicated or listen-host console).");
                return;
            }

            if (sub != "status")
            {
                args.Context.AddString("[ft] client supports: ft status | ft help");
                return;
            }

            args.Context.AddString($"[ft] FactionTactics.Client {ClientPlugin.PluginVersion} product={FtVersion.ProductVersion} schema=v{FtVersion.IntentSchemaVersion}");
            args.Context.AddString(
                $"[ft] zdo: readsOk={IntentZdoSync.ReadsOk} stale={IntentZdoSync.ReadsStale} mismatch={IntentZdoSync.SchemaMismatches} rpcRecv={IntentRpcSync.RpcIntentsReceived} rpcPkts={IntentRpcSync.RpcPacketsReceived} rpcHits={IntentRpcSync.RpcCacheHits} ownerDrives={ClientMonsterAI_UpdateAI_Patch.DriveHits} rpcDrives={ClientMonsterAI_UpdateAI_Patch.RpcDriveHits}");
        }
#endif
    }
}
