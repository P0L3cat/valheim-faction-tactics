using System;
using BepInEx;
using BepInEx.Logging;
using FactionTactics.Util;
using HarmonyLib;

namespace FactionTactics.Client
{
    /// <summary>
    /// Player-side Faction Tactics executor (1.0.10).
    /// Reads ZDO intents written by the server commander and drives owned enemies
    /// (MoveTo / Hold / LookAt / DoAttack) while keeping local ZDO ownership for
    /// Character physics + hit detection latency.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class ClientPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.nate.factiontactics.client";
        public const string PluginName = "FactionTactics.Client";
        public const string PluginVersion = "1.0.10";

        internal static ClientPlugin Instance { get; private set; } = null!;
        internal static ManualLogSource Log { get; private set; } = null!;

        private Harmony? _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            FactionTacticsLog.DebugSink = msg => Log.LogDebug(msg);

            // Never run executor sole-brain on dedicated — that host is commander-only.
            if (IsDedicatedProcess())
            {
                Log.LogInfo($"{PluginName} {PluginVersion}: dedicated process detected — client executor idle (install FactionTactics server DLL for commander).");
                return;
            }

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(ClientPlugin).Assembly);
            try { FactionTactics.Orders.IntentRpcSync.EnsureRegistered(); } catch { /* ZRoutedRpc may not exist yet */ }
            try { FactionTactics.ConsoleCmds.FtConfigRpc.EnsureRegistered(); } catch { /* ok */ }
            ClientConsoleCommands.Register();
            Log.LogInfo($"{PluginName} {PluginVersion} loaded — owning-client combat executor (RPC intents → CombatDriver swing gate; admin ft RPC). Install alongside/without server DLL on players.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        private static bool IsDedicatedProcess()
        {
#if VALHEIM_REFS
            try
            {
                if (ZNet.instance != null)
                    return ZNet.instance.IsDedicated();
            }
            catch { /* ignore */ }
#endif
            // Before ZNet exists: BepInEx dedicated servers usually have -batchmode / no graphics.
            var args = Environment.GetCommandLineArgs();
            foreach (var a in args)
            {
                if (string.Equals(a, "-batchmode", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(a, "-nographics", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
