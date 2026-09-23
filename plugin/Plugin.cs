using System;
using BepInEx;
using BepInEx.Logging;
using FactionTactics.Commander;
using FactionTactics.Config;
using FactionTactics.ConsoleCmds;
using FactionTactics.Doctrine;
using FactionTactics.HarmonyPatches;
using FactionTactics.Orders;
using FactionTactics.Siege;
using FactionTactics.Ambience;
using FactionTactics.Squad;
using FactionTactics.Dedicated;
using FactionTactics.Util;
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
        public const string PluginVersion = "1.0.11";

        internal static Plugin Instance { get; private set; } = null!;
        internal static ManualLogSource Log { get; private set; } = null!;

        private Harmony? _harmony;
        private SquadDirector? _director;

        /// <summary>Live director for console status (null if disabled / not constructed).</summary>
        internal SquadDirector? Director => _director;

#if VALHEIM_REFS
        private EnemyOwnershipDirector? _enemyOwnership;
#endif
        private float _tickAccumulator;
        private float _burstRemaining;
        private int _lastCandidateCount = -1;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            FactionTacticsLog.DebugSink = msg => Log.LogDebug(msg);

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

            // Console knobs always register so admins can ft set EnablePlugin without redeploy.
            FtConsoleCommands.Register();
            try { ConsoleCmds.FtConfigRpc.EnsureRegistered(); } catch { /* ZRoutedRpc may be null */ }

            if (PluginConfig.EnablePlugin.Value)
            {
                _harmony = new Harmony(PluginGuid);
                MonsterAIPatches.Apply(_harmony);
                Log.LogInfo($"{PluginName} {PluginVersion} loaded (1.0.11 Theater Commander: Pin/Flank/Harass across packs; Death-Rush stays Charge). Tick={PluginConfig.TickIntervalSeconds.Value}s");
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
            var steady = Math.Max(0.1f, PluginConfig.TickIntervalSeconds.Value);
            var interval = steady;
            if (_burstRemaining > 0f && (PluginConfig.DiscoveryBurstOnSpawn?.Value ?? true))
            {
                interval = Math.Max(0.05f, PluginConfig.DiscoveryBurstSeconds?.Value ?? 0.2f);
                _burstRemaining -= UnityEngine.Time.deltaTime;
            }
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
                MaybeStartDiscoveryBurst();
            }
            catch (Exception ex)
            {
                Log.LogError($"SquadDirector tick failed: {ex}");
            }

            try { ConsoleCmds.FtConfigRpc.EnsureRegistered(); } catch { /* ok */ }
        }

        /// <summary>
        /// Phase A optional burst: when near-player candidate / squad count spikes, briefly tick faster.
        /// </summary>
        private void MaybeStartDiscoveryBurst()
        {
            if (!(PluginConfig.DiscoveryBurstOnSpawn?.Value ?? true))
                return;
            var sd = _director;
            if (sd == null)
                return;

            var n = Math.Max(sd.LastDiscoveredCount, sd.LastCandidateGauge);
            if (_lastCandidateCount >= 0 && n > _lastCandidateCount + 1)
                _burstRemaining = 2.0f; // ~2s of fast ticks
            if (_lastCandidateCount == 0 && n > 0)
                _burstRemaining = 2.0f;
            _lastCandidateCount = n;
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
