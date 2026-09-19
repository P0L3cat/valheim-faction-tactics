using System;
using BepInEx;
using BepInEx.Logging;
using FactionTactics.Commander;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.HarmonyPatches;
using FactionTactics.Orders;
using FactionTactics.Siege;
using FactionTactics.Ambience;
using FactionTactics.Squad;
using FactionTactics.Dedicated;
using HarmonyLib;

#if !VALHEIM_REFS
using ZNet = FactionTactics.Stubs.ZNet;
#endif

namespace FactionTactics
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.nate.factiontactics";
        public const string PluginName = "FactionTactics";
        public const string PluginVersion = "0.1.10";

        internal static Plugin Instance { get; private set; } = null!;
        internal static ManualLogSource Log { get; private set; } = null!;

        private Harmony? _harmony;
        private SquadDirector? _director;
#if VALHEIM_REFS
        private EnemyOwnershipDirector? _enemyOwnership;
#endif
        private float _tickAccumulator;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            PluginConfig.Bind(Config);

            var registry = DoctrinePackRegistry.CreateDefault();
            var siege = new SiegeDirector();
            var ambience = new AmbushAmbienceDirector();
            ICommander commander = new ScriptedCommander(registry, siege);
            IRoleScorer roleScorer = new NullRoleScorer();
            IActionScorer actionScorer = new NullActionScorer();
            var applicator = new OrderApplicator();
            var discovery = new SquadDiscovery(registry);

            _director = new SquadDirector(
                discovery,
                commander,
                registry,
                roleScorer,
                actionScorer,
                applicator,
                siege,
                ambience);
#if VALHEIM_REFS
            _enemyOwnership = new EnemyOwnershipDirector();
#endif

            if (PluginConfig.EnablePlugin.Value)
            {
                _harmony = new Harmony(PluginGuid);
                MonsterAIPatches.Apply(_harmony);
                Log.LogInfo($"{PluginName} {PluginVersion} loaded (doctrine packs + Siege Assault v1 + enemy-ownership + discovery capture 0.1.10). Tick={PluginConfig.TickIntervalSeconds.Value}s");
            }
            else
            {
                Log.LogInfo($"{PluginName} disabled via config.");
            }
        }

        private void Update()
        {
            if (!PluginConfig.EnablePlugin.Value || _director == null)
                return;

            // Server-authoritative: only tick where mobs are simulated.
            if (ZNet.instance != null && !ZNet.instance.IsServer())
                return;

            _tickAccumulator += UnityEngine.Time.deltaTime;
            var interval = Math.Max(0.1f, PluginConfig.TickIntervalSeconds.Value);
            if (_tickAccumulator < interval)
                return;

            _tickAccumulator = 0f;
#if VALHEIM_REFS
            try
            {
                _enemyOwnership?.Tick(interval);
            }
            catch (Exception ex)
            {
                Log.LogError($"EnemyOwnershipDirector tick failed: {ex}");
            }
#endif
            try
            {
                _director.Tick(interval);
            }
            catch (Exception ex)
            {
                Log.LogError($"SquadDirector tick failed: {ex}");
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
