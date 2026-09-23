using System;
using System.Collections.Generic;
using System.Linq;
using FactionTactics.Commander;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Util;
using FactionTactics.Siege;
using FactionTactics.Ambience;
using UnityEngine;

namespace FactionTactics.Squad
{
    /// <summary>
    /// Route 1 orchestrator: discover → merge persisted FSM state → min-size gate →
    /// roles → commander propose → apply. Consults Route 2 scorers before committing
    /// (NullScorer = no-op). Enriches ThreatAssessment with TrollFortress + environment
    /// heuristics and Siege Assault workbench proximity.
    /// </summary>
    public sealed class SquadDirector
    {
        /// <summary>Hard-break casualty ratio when doctrine-specific thresholds are not consulted.</summary>
        public const float BrokenCasualtyThreshold = 0.5f;

        /// <summary>Drop runtime keys after this many ticks without a matching cluster.</summary>
        public const int RuntimePruneTicks = 8;

        private readonly ISquadDiscovery _discovery;
        private readonly ICommander _commander;
        private readonly DoctrinePackRegistry _registry;
        private readonly IRoleScorer _roleScorer;
        private readonly IActionScorer _actionScorer;
        private readonly OrderApplicator _applicator;
        private readonly SiegeDirector _siege;
        private readonly AmbushAmbienceDirector _ambience;

        private readonly List<SquadUnit> _active = new List<SquadUnit>();
        private readonly List<SquadRuntimeState> _runtime = new List<SquadRuntimeState>();
        private int _nextStableSerial;
        private float _heartbeatAge;
        private const float HeartbeatIntervalSeconds = 15f;
        /// <summary>Squads returned from Discover() last Tick (before minSize gate).</summary>
        private int _lastDiscoveredCount;

        public SquadDirector(
            ISquadDiscovery discovery,
            ICommander commander,
            DoctrinePackRegistry registry,
            IRoleScorer roleScorer,
            IActionScorer actionScorer,
            OrderApplicator applicator,
            SiegeDirector? siege = null,
            AmbushAmbienceDirector? ambience = null)
        {
            _discovery = discovery;
            _commander = commander;
            _registry = registry;
            _roleScorer = roleScorer;
            _actionScorer = actionScorer;
            _applicator = applicator;
            _siege = siege ?? new SiegeDirector();
            _ambience = ambience ?? new AmbushAmbienceDirector();
        }

        public IReadOnlyList<SquadUnit> ActiveSquads => _active;

        /// <summary>Test/diagnostics: currently tracked runtime FSM states.</summary>
        public IReadOnlyList<SquadRuntimeState> RuntimeStates => _runtime;

        /// <summary>Last discovered cluster count (before minSize gate).</summary>
        public int LastDiscoveredCount => _lastDiscoveredCount;

        /// <summary>Phase A: last discovery candidate count (ZDO+live) for burst detection.</summary>
        public int LastCandidateGauge
        {
            get
            {
                if (_discovery is SquadDiscovery sd)
                    return Math.Max(sd.LastCandidateCount, sd.LastZdoCandidateCount);
                return _lastDiscoveredCount;
            }
        }

        /// <summary>Console <c>ft status</c> lines (no prefix).</summary>
        public IEnumerable<string> FormatStatusLines()
        {
            var orderCounts = new Dictionary<string, int>();
            foreach (var s in _active)
            {
                var kind = s.CurrentOrder?.OrderKind.ToString() ?? "none";
                var doctrine = s.Doctrine?.Id ?? "?";
                var key = $"{doctrine}:{kind}";
                orderCounts[key] = orderCounts.TryGetValue(key, out var c) ? c + 1 : 1;
            }

            var summary = "none";
            if (orderCounts.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in orderCounts.OrderBy(k => k.Key, StringComparer.Ordinal))
                    parts.Add($"{kv.Key}={kv.Value}");
                summary = string.Join(", ", parts);
            }

            yield return $"squads: discovered={_lastDiscoveredCount} active={_active.Count} runtime={_runtime.Count}";
            yield return $"orders: [{summary}]";

            long zdoWrites = 0, schemaWrites = 0, zdoReads = 0, zdoStale = 0, schemaMismatch = 0, ownerDrives = 0, rpcIntentsSent = 0, rpcPeerInvokes = 0, rpcBatchesSent = 0;
#if VALHEIM_REFS
            zdoWrites = FactionTactics.Orders.IntentZdoSync.Writes;
            schemaWrites = FactionTactics.Orders.IntentZdoSync.SchemaWrites;
            zdoReads = FactionTactics.Orders.IntentZdoSync.ReadsOk;
            zdoStale = FactionTactics.Orders.IntentZdoSync.ReadsStale;
            schemaMismatch = FactionTactics.Orders.IntentZdoSync.SchemaMismatches;
            ownerDrives = FactionTactics.HarmonyPatches.MonsterAI_UpdateAI_Patch.OwnerDriveCount;
            rpcIntentsSent = FactionTactics.Orders.IntentRpcSync.RpcIntentsSent;
            rpcPeerInvokes = FactionTactics.Orders.IntentRpcSync.RpcPeerInvokes;
            rpcBatchesSent = FactionTactics.Orders.IntentRpcSync.RpcBatchesSent;
#endif
            yield return $"zdo: writes={zdoWrites} schemaWrites={schemaWrites} readsOk={zdoReads} stale={zdoStale} mismatch={schemaMismatch} ownerDrives={ownerDrives} rpcIntentsSent={rpcIntentsSent} rpcPeerInvokes={rpcPeerInvokes} rpcBatchesSent={rpcBatchesSent}";

            long swings = 0, holdBlocks = 0, slotLocks = 0, reshuffles = 0;
#if VALHEIM_REFS
            swings = FactionTactics.Combat.CombatDriver.Swings;
            holdBlocks = FactionTactics.Combat.CombatDriver.HoldBlocks;
#endif
            slotLocks = FactionTactics.Orders.FormationSlotLock.SlotLocks;
            reshuffles = FactionTactics.Orders.FormationSlotLock.Reshuffles;
            yield return $"combat: swings={swings} holdBlocks={holdBlocks} slotLocks={slotLocks} reshuffles={reshuffles}";

            if (_discovery is SquadDiscovery sd)
            {
                yield return $"discovery: zdoCandidates={sd.LastZdoCandidateCount} prefabZdos={sd.LastPrefabZdos} prefabLive={sd.LastPrefabLive} mai={sd.LastPrefabMai}";
            }
        }

        public void Tick(float dt)
        {
            OrderScoreContext.ActionScorer = _actionScorer;
            try
            {
                TickCore(dt);
            }
            finally
            {
                OrderScoreContext.ActionScorer = null;
            }
        }

        private void TickCore(float dt)
        {
            _active.Clear();
            var discovered = MergeStragglers(_discovery.Discover());
            _lastDiscoveredCount = discovered.Count;
            var seen = new HashSet<SquadRuntimeState>();

            foreach (var squad in discovered)
            {
                var state = MatchOrCreateRuntime(squad);
                seen.Add(state);
                state.TicksUnseen = 0;

                ApplyRuntimeToSquad(squad, state, dt);

                var alive = CountAlive(squad);
                var roster = squad.Members.Count;
                var doctrineId = squad.Doctrine?.Id ?? "?";
                var minSize = EffectiveMinSize(doctrineId);
                state.PeakAlive = Math.Max(state.PeakAlive, Math.Max(alive, roster));
                if (state.PeakAlive >= minSize)
                    state.EverMetMinSize = true;

                // Refresh member set for next-tick overlap matching (include dead roster entries).
                state.MemberIds.Clear();
                foreach (var m in squad.Members)
                    state.MemberIds.Add(m.InstanceId);

                squad.PeakAlive = state.PeakAlive;

                if (alive < minSize)
                {
                    // Still record casualties for diagnostics when the cluster is collapsing.
                    var (ratio, broken) = ComputeCasualties(alive, state, minSize);
                    squad.LastCasualtyRatio = ratio;
                    squad.LastIsBroken = broken;
                    // 0.2.1: always LogInfo so GPortal smoke shows why _active stayed empty.
                    Plugin.Log?.LogInfo(
                        $"Squad {squad.SquadId} below minSize alive={alive} roster={roster} min={minSize} doctrine={doctrineId}");
                    continue;
                }

                RefineRolesWithScorer(squad);
                var threats = AssessThreats(squad, state, minSize);
                squad.LastCasualtyRatio = threats.CasualtyRatio;
                squad.LastIsBroken = threats.IsBroken;

                var snapshot = SquadSnapshot.FromSquad(squad, threats);
                BandedCadence.ReadInto(snapshot, state);

                var order = _commander.Propose(snapshot);
                BandedCadence.WriteBack(state, snapshot);
                if (order == null)
                {
                    Plugin.Log?.LogInfo(
                        $"Squad {squad.SquadId} Propose returned null doctrine={doctrineId}");
                    continue;
                }

                order = MaybeRescoreOrder(order, snapshot);
                order = ApplyOrderStability(squad, state, order, snapshot);
                squad.CurrentOrder = order;
                if (state.PreviousOrderKind != order.OrderKind)
                {
                    state.OrderAgeSeconds = 0f;
                    squad.OrderAgeSeconds = 0f;
                    // Order change also resets slot-lock age via FormationSlotLock reshuffle.
                }
                squad.PreviousOrderKind = order.OrderKind;
                state.PreviousOrderKind = order.OrderKind;
                try
                {
                    _applicator.Apply(squad, order, state);
                    _active.Add(squad);
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogInfo(
                        $"Squad {squad.SquadId} Apply failed doctrine={doctrineId}: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (PluginConfig.DebugLogging?.Value == true)
                {
                    var assaultTag = snapshot.AssaultActive
                        ? (snapshot.PlayersNearAssault ? " Assault/hot" : " Assault/quiet")
                        : "";
                    Plugin.Log?.LogInfo(
                        $"[{squad.Doctrine.DisplayName}]{assaultTag} {squad.SquadId} n={alive} peak={state.PeakAlive} cas={threats.CasualtyRatio:0.00} → {order.OrderKind} ({order.Formation}/{order.Stance})");
                }
            }

            PruneUnseenRuntime(seen);
            _ = _registry;
            try { _ambience.Tick(_active); }
            catch (Exception ex)
            {
                // SoftReferenceableAssets / EnvMan missing in unit-test hosts
                Plugin.Log?.LogDebug($"AmbushAmbience.Tick: {ex.GetType().Name}: {ex.Message}");
            }
#if VALHEIM_REFS
            // 1.0.3: flush queued MemberIntent RPC batch once per tick (primary hybrid transport).
            try { FactionTactics.Orders.IntentRpcSync.EnsureRegistered(); FactionTactics.Orders.IntentRpcSync.FlushBroadcast(); }
            catch (System.Exception ex) { Plugin.Log?.LogDebug($"IntentRpcSync.FlushBroadcast: {ex.GetType().Name}: {ex.Message}"); }
#endif
            MaybeLogHeartbeat(dt);
        }

        private void MaybeLogHeartbeat(float dt)
        {
            var debug = PluginConfig.DebugLogging?.Value == true;
            var beat = PluginConfig.HeartbeatLogging?.Value == true;
            if (!debug && !beat)
                return;

            _heartbeatAge += dt;
            if (_heartbeatAge < HeartbeatIntervalSeconds)
                return;
            _heartbeatAge = 0f;

            var registry = 0;
            var findAll = 0;
            var findObjects = 0;
            var sceneInstances = 0;
            var prefabZdos = 0;
            var prefabLive = 0;
            var prefabMai = 0;
            var updateAIHits = 0L;
            var baseAIUpdateHits = 0L;
            var monsterAi = 0;
            var candidates = 0;
            var zdoCandidates = 0;
            if (_discovery is SquadDiscovery sd)
            {
                registry = sd.LastRegistry;
                findAll = sd.LastFindAll;
                findObjects = sd.LastFindObjects;
                sceneInstances = sd.LastSceneInstances;
                prefabZdos = sd.LastPrefabZdos;
                prefabLive = sd.LastPrefabLive;
                prefabMai = sd.LastPrefabMai;
                updateAIHits = sd.LastUpdateAIHits;
                baseAIUpdateHits = sd.LastBaseAIUpdateHits;
                monsterAi = sd.LastMonsterAiCount;
                candidates = sd.LastCandidateCount;
                zdoCandidates = sd.LastZdoCandidateCount;
            }

#if VALHEIM_REFS
            try
            {
                if (sceneInstances > 0)
                    ValheimWorldScan.MaybeDumpSceneInstances(force: true);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"sceneDump heartbeat failed: {ex.GetType().Name}: {ex.Message}");
            }
#endif

            var orderCounts = new Dictionary<string, int>();
            var formationCounts = new Dictionary<string, int>();
            foreach (var s in _active)
            {
                var kind = s.CurrentOrder?.OrderKind.ToString() ?? "none";
                orderCounts[kind] = orderCounts.TryGetValue(kind, out var c) ? c + 1 : 1;
                var form = s.CurrentOrder?.Formation.ToString() ?? "none";
                formationCounts[form] = formationCounts.TryGetValue(form, out var fc) ? fc + 1 : 1;
            }

            var topOrders = "none";
            if (orderCounts.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in orderCounts)
                    parts.Add($"{kv.Key}={kv.Value}");
                parts.Sort(StringComparer.Ordinal);
                topOrders = string.Join(",", parts);
            }

            var topForms = "none";
            if (formationCounts.Count > 0)
            {
                var fparts = new List<string>();
                foreach (var kv in formationCounts)
                    fparts.Add($"{kv.Key}={kv.Value}");
                fparts.Sort(StringComparer.Ordinal);
                topForms = string.Join(",", fparts);
            }

            var enemyOwned = 0;
            var enemyLive = 0;
            var enemyMai = 0;
            var enemyServerOwned = 0;
            var enemyClientOwned = 0;
            var enemyReclaims = 0;
            var stickyKeeps = 0;
            var stickyReclaims = 0;
#if VALHEIM_REFS
            enemyOwned = FactionTactics.Dedicated.EnemyOwnershipDirector.LastEnemyOwned;
            enemyLive = FactionTactics.Dedicated.EnemyOwnershipDirector.LastEnemyLive;
            enemyMai = FactionTactics.Dedicated.EnemyOwnershipDirector.LastEnemyMai;
            enemyServerOwned = FactionTactics.Dedicated.EnemyOwnershipDirector.LastEnemyServerOwned;
            enemyClientOwned = FactionTactics.Dedicated.EnemyOwnershipDirector.LastEnemyClientOwned;
            enemyReclaims = FactionTactics.Dedicated.EnemyOwnershipDirector.LastEnemyReclaims;
            stickyKeeps = FactionTactics.HarmonyPatches.ZDOMan_ReleaseNearbyZDOS_StickyEnemy_Patch.LastStickyKeeps;
            stickyReclaims = FactionTactics.HarmonyPatches.ZDOMan_ReleaseNearbyZDOS_StickyEnemy_Patch.LastStickyReclaims;
#endif
            var zdoIntentsWritten = 0L;
            var schemaWrites = 0L;
            var zdoReads = 0L;
            var zdoStale = 0L;
            var schemaMismatch = 0L;
            var ownerDrives = 0L;
            var rpcIntentsSent = 0L;
            var rpcBatchesSent = 0L;
            var rpcPeerInvokes = 0L;
#if VALHEIM_REFS
            zdoIntentsWritten = FactionTactics.Orders.IntentZdoSync.Writes;
            schemaWrites = FactionTactics.Orders.IntentZdoSync.SchemaWrites;
            zdoReads = FactionTactics.Orders.IntentZdoSync.ReadsOk;
            zdoStale = FactionTactics.Orders.IntentZdoSync.ReadsStale;
            schemaMismatch = FactionTactics.Orders.IntentZdoSync.SchemaMismatches;
            ownerDrives = FactionTactics.HarmonyPatches.MonsterAI_UpdateAI_Patch.OwnerDriveCount;
            rpcIntentsSent = FactionTactics.Orders.IntentRpcSync.RpcIntentsSent;
            rpcBatchesSent = FactionTactics.Orders.IntentRpcSync.RpcBatchesSent;
            rpcPeerInvokes = FactionTactics.Orders.IntentRpcSync.RpcPeerInvokes;
#endif
            long swingsHb = 0, holdBlocksHb = 0, slotLocksHb = 0;
#if VALHEIM_REFS
            swingsHb = FactionTactics.Combat.CombatDriver.Swings;
            holdBlocksHb = FactionTactics.Combat.CombatDriver.HoldBlocks;
#endif
            slotLocksHb = FactionTactics.Orders.FormationSlotLock.SlotLocks;
            Plugin.Log?.LogInfo(
                $"FactionTactics heartbeat: discovered={_lastDiscoveredCount} active={_active.Count} " +
                $"squads={_active.Count} orders=[{topOrders}] " +
                $"formations=[{topForms}] baseAIUpdateHits={baseAIUpdateHits} updateAIHits={updateAIHits} " +
                $"registry={registry} findAll={findAll} findObjects={findObjects} " +
                $"sceneInstances={sceneInstances} prefabZdos={prefabZdos} prefabLive={prefabLive} prefabMai={prefabMai} " +
                $"monsterAI={monsterAi} candidates={candidates} zdoCandidates={zdoCandidates} " +
                $"enemyOwned={enemyOwned} enemyLive={enemyLive} enemyMai={enemyMai} " +
                $"enemyServerOwned={enemyServerOwned} enemyClientOwned={enemyClientOwned} " +
                $"enemyReclaims={enemyReclaims} stickyKeeps={stickyKeeps} stickyReclaims={stickyReclaims}" +
                $" zdoIntentsWritten={zdoIntentsWritten} schemaWrites={schemaWrites} zdoReads={zdoReads} zdoStale={zdoStale} schemaMismatch={schemaMismatch} ownerDrives={ownerDrives} rpcIntentsSent={rpcIntentsSent} rpcPeerInvokes={rpcPeerInvokes} rpcBatchesSent={rpcBatchesSent}" +
                $" swings={swingsHb} holdBlocks={holdBlocksHb} slotLocks={slotLocksHb}");
        }

        private SquadRuntimeState MatchOrCreateRuntime(SquadUnit squad)
        {
            var doctrineId = squad.Doctrine?.Id ?? "";
            SquadRuntimeState? best = null;
            var bestOverlap = 0;
            foreach (var candidate in _runtime)
            {
                if (!string.Equals(candidate.DoctrineId, doctrineId, StringComparison.OrdinalIgnoreCase))
                    continue;
                var overlap = SquadIdentity.Overlap(candidate, squad.Members);
                if (overlap <= 0)
                    continue;
                // Prefer the strongest overlap; require at least one shared member.
                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    best = candidate;
                }
            }

            // Accept match if we share any members (casualties shrink the set; survivors keep the chain).
            if (best != null)
                return best;

            var state = new SquadRuntimeState
            {
                StableId = $"{doctrineId}#{++_nextStableSerial}",
                DoctrineId = doctrineId,
            };
            _runtime.Add(state);
            return state;
        }

        private static void ApplyRuntimeToSquad(SquadUnit squad, SquadRuntimeState state, float dt)
        {
            state.AgeSeconds += dt;
            state.OrderAgeSeconds += dt;
            FactionTactics.Orders.FormationSlotLock.TickAge(state, dt);
            squad.AgeSeconds = state.AgeSeconds;
            squad.OrderAgeSeconds = state.OrderAgeSeconds;
            squad.PreviousOrderKind = state.PreviousOrderKind;
            squad.SquadId = state.StableId;
            squad.PeakAlive = state.PeakAlive;
        }

        private void PruneUnseenRuntime(HashSet<SquadRuntimeState> seen)
        {
            for (int i = _runtime.Count - 1; i >= 0; i--)
            {
                var state = _runtime[i];
                if (seen.Contains(state))
                    continue;
                state.TicksUnseen++;
                if (state.TicksUnseen >= RuntimePruneTicks)
                    _runtime.RemoveAt(i);
            }
        }

        private void RefineRolesWithScorer(SquadUnit squad)
        {
            // NullRoleScorer returns 0 for all → keep doctrine assignment.
            // Non-null scorers: pick max score among doctrine-legal roles.
            // Use a lightweight assessment without mutating PeakAlive twice.
            var minSize = EffectiveMinSize(squad.Doctrine?.Id);
            var state = FindRuntime(squad.SquadId);
            var threats = AssessThreats(squad, state, minSize);
            var snapshot = SquadSnapshot.FromSquad(squad, threats);

            foreach (var member in squad.Members)
            {
                var best = member.AssignedRole;
                var bestScore = _roleScorer.ScoreRole(member, best, snapshot);
                foreach (SquadRole role in Enum.GetValues(typeof(SquadRole)))
                {
                    if (role == SquadRole.Unassigned)
                        continue;
                    var score = _roleScorer.ScoreRole(member, role, snapshot);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = role;
                    }
                }

                if (bestScore > 0f)
                    member.AssignedRole = best;
            }
        }

        private SquadRuntimeState? FindRuntime(string stableId)
        {
            foreach (var s in _runtime)
            {
                if (s.StableId == stableId)
                    return s;
            }
            return null;
        }

        private SquadOrder MaybeRescoreOrder(SquadOrder order, SquadSnapshot snapshot)
        {
            // Roman/Ambush fold IActionScorer into their score vector (vetoes stick)
            // and apply OrderScoreHysteresis inside SelectOrder.
            if (IsPhaseBScored(snapshot.DoctrineId))
                return order;

            var currentScore = _actionScorer.ScoreAction(order.OrderKind, snapshot);
            if (currentScore == 0f)
                return order; // NullScorer or no preference

            DoctrineOrderKind best = order.OrderKind;
            float bestScore = currentScore;
            foreach (DoctrineOrderKind kind in Enum.GetValues(typeof(DoctrineOrderKind)))
            {
                var score = _actionScorer.ScoreAction(kind, snapshot);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = kind;
                }
            }

            if (best != order.OrderKind)
            {
                order.OrderKind = best;
                order.Source = order.Source + "+ActionScorer";
            }

            return order;
        }

        private static bool IsPhaseBScored(string? doctrineId)
            => string.Equals(doctrineId, "roman", StringComparison.OrdinalIgnoreCase)
               || string.Equals(doctrineId, "ambush", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Charge cap, then min dwell, then DeathRush lock. Remap formation if the kind changed
        /// so a forced Kite/Protect is not left wearing a Charge wedge.
        /// </summary>
        private static SquadOrder ApplyOrderStability(
            SquadUnit squad,
            SquadRuntimeState state,
            SquadOrder order,
            SquadSnapshot snapshot)
        {
            var proposedKind = order.OrderKind;
            order = ApplyChargeMaxHygiene(squad, state, order, snapshot);

            var doctrineId = squad.Doctrine?.Id ?? snapshot.DoctrineId;
            var dwelled = OrderTransition.ApplyMinDwell(
                doctrineId,
                snapshot,
                state.PreviousOrderKind,
                order.OrderKind,
                state.OrderAgeSeconds);
            if (dwelled != order.OrderKind)
            {
                order.OrderKind = dwelled;
                order.Source = (order.Source ?? "") + "+Dwell";
            }

            var locked = OrderTransition.EnforceDeathRush(doctrineId, snapshot, order.OrderKind);
            if (locked != order.OrderKind)
            {
                order.OrderKind = locked;
                order.Source = (order.Source ?? "") + "+DeathRush";
            }

            if (order.OrderKind != proposedKind)
            {
                var (formation, stance) = ScriptedCommander.PresentationFor(
                    order.OrderKind,
                    doctrineId ?? "",
                    order.AssaultActive);
                order.Formation = formation;
                order.Stance = stance;
            }

            return order;
        }

        /// <summary>
        /// Hard-cap Charge / FlashCharge, then re-eval (DeathRush exempt — never peeled).
        /// Ambush → Kite. Roman with missiles still up → ProtectMissiles (missile line).
        /// Viking/Charred/others → RetreatAndReform. Jelly → FocusFire.
        /// </summary>
        private static SquadOrder ApplyChargeMaxHygiene(
            SquadUnit squad,
            SquadRuntimeState state,
            SquadOrder order,
            SquadSnapshot snapshot)
        {
            if (order == null)
                return order!;
            var deathRush = string.Equals(squad.Doctrine?.Id, "death-rush", StringComparison.OrdinalIgnoreCase);
            if (deathRush)
                return order;
            if (order.OrderKind != DoctrineOrderKind.Charge)
                return order;
            if (state.PreviousOrderKind != DoctrineOrderKind.Charge)
                return order; // first tick of Charge — allow flash

            var maxSec = PluginConfig.ChargeMaxSeconds?.Value ?? 4f;
            if (state.OrderAgeSeconds < maxSec)
                return order;

            // Forced re-eval: prefer doctrine post-Charge path via RetreatAndReform / Hold.
            var doctrineId = squad.Doctrine?.Id ?? "";
            if (string.Equals(doctrineId, "ambush", StringComparison.OrdinalIgnoreCase))
                order.OrderKind = DoctrineOrderKind.Kite;
            else if (string.Equals(doctrineId, "artillery-jelly", StringComparison.OrdinalIgnoreCase))
                order.OrderKind = DoctrineOrderKind.FocusFire;
            else if (string.Equals(doctrineId, "roman", StringComparison.OrdinalIgnoreCase))
            {
                var missiles = snapshot.CountByRole(SquadRole.Missile) > 0;
                order.OrderKind = missiles && !snapshot.IsBroken && snapshot.CasualtyRatio < 0.45f
                    ? DoctrineOrderKind.ProtectMissiles
                    : DoctrineOrderKind.RetreatAndReform;
            }
            else
                order.OrderKind = DoctrineOrderKind.RetreatAndReform;
            order.Source = (order.Source ?? "") + "+ChargeMax";
            return order;
        }

        /// <summary>
        /// OQ lock: FlankOpportunity = one player in contact band OR players split &gt; FlankSplitMeters.
        /// TargetIsolated = contact player with no buddy within IsolateBuddyMeters.
        /// </summary>
        private static void ApplyFlankAndIsolate(Vector3 centroid, float nearest, ThreatAssessment assessment)
        {
            var splitM = PluginConfig.FlankSplitMeters?.Value ?? 12f;
            var buddyM = PluginConfig.IsolateBuddyMeters?.Value ?? 8f;
            var contactBand = 12f;

#if VALHEIM_REFS
            try
            {
                var players = ValheimWorldScan.CollectPlayerPositions();
                if (players == null || players.Count == 0)
                {
                    // Keep role-seeded FlankOpportunity from AssessThreats init.
                    return;
                }

                // Nearest player to centroid = contact candidate.
                int nearestIdx = -1;
                float nearestPlayer = float.MaxValue;
                for (int i = 0; i < players.Count; i++)
                {
                    var d = Vector3.Distance(centroid, players[i]);
                    if (d < nearestPlayer)
                    {
                        nearestPlayer = d;
                        nearestIdx = i;
                    }
                }

                bool oneInContact = nearestPlayer <= contactBand
                                    || (nearest < float.MaxValue && nearest <= contactBand);

                bool split = false;
                if (players.Count >= 2)
                {
                    for (int i = 0; i < players.Count && !split; i++)
                    {
                        for (int j = i + 1; j < players.Count; j++)
                        {
                            if (Vector3.Distance(players[i], players[j]) > splitM)
                            {
                                split = true;
                                break;
                            }
                        }
                    }
                }

                assessment.FlankOpportunity = oneInContact || split;

                bool isolated = false;
                if (oneInContact && nearestIdx >= 0)
                {
                    isolated = true;
                    var focus = players[nearestIdx];
                    for (int i = 0; i < players.Count; i++)
                    {
                        if (i == nearestIdx)
                            continue;
                        if (Vector3.Distance(focus, players[i]) <= buddyM)
                        {
                            isolated = false;
                            break;
                        }
                    }
                }
                assessment.TargetIsolated = isolated;
            }
            catch
            {
                // keep prior flags
            }
#else
            _ = centroid;
            _ = nearest;
            _ = splitM;
            _ = buddyM;
            _ = contactBand;
#endif
        }

        /// <summary>Per-doctrine min roster. Death-Rush defaults to 1 (tiny Meadows packs).</summary>

        /// <summary>
        /// 1.0.8: fold below-MinSquadSize same-doctrine clusters into the nearest active
        /// (≥minSize) same-doctrine parent within SquadMergeRadius. Isolated stragglers stay vanilla.
        /// </summary>
        internal static System.Collections.Generic.List<SquadUnit> MergeStragglers(
            System.Collections.Generic.IReadOnlyList<SquadUnit> discovered)
        {
            var list = new System.Collections.Generic.List<SquadUnit>(discovered);
            if (list.Count <= 1)
                return list;

            var mergeRadius = PluginConfig.SquadMergeRadius?.Value ?? 40f;
            if (mergeRadius <= 0f)
                return list;

            var parents = new System.Collections.Generic.List<SquadUnit>();
            var stragglers = new System.Collections.Generic.List<SquadUnit>();
            foreach (var s in list)
            {
                var doctrineId = s.Doctrine?.Id ?? "";
                var minSize = EffectiveMinSize(doctrineId);
                if (CountAlive(s) >= minSize)
                    parents.Add(s);
                else
                    stragglers.Add(s);
            }

            if (parents.Count == 0 || stragglers.Count == 0)
                return list;

            var absorbed = new System.Collections.Generic.HashSet<SquadUnit>();
            foreach (var stray in stragglers)
            {
                var doctrineId = stray.Doctrine?.Id ?? "";
                var strayCentroid = ComputeSquadCentroid(stray);
                SquadUnit? best = null;
                float bestDist = float.MaxValue;
                foreach (var parent in parents)
                {
                    if (!string.Equals(parent.Doctrine?.Id, doctrineId, System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    var d = Vector3.Distance(strayCentroid, ComputeSquadCentroid(parent));
                    if (d <= mergeRadius && d < bestDist)
                    {
                        bestDist = d;
                        best = parent;
                    }
                }
                if (best == null)
                    continue;
                foreach (var m in stray.Members)
                    best.Members.Add(m);
                absorbed.Add(stray);
            }

            if (absorbed.Count == 0)
                return list;

            var merged = new System.Collections.Generic.List<SquadUnit>(list.Count - absorbed.Count);
            foreach (var s in list)
            {
                if (!absorbed.Contains(s))
                    merged.Add(s);
            }
            return merged;
        }

        internal static Vector3 ComputeSquadCentroid(SquadUnit squad)
        {
            var sum = Vector3.zero;
            int n = 0;
            foreach (var m in squad.Members)
            {
                if (!m.IsAlive)
                    continue;
                sum += m.Position;
                n++;
            }
            if (n == 0)
            {
                foreach (var m in squad.Members)
                {
                    sum += m.Position;
                    n++;
                }
            }
            return n > 0 ? sum / n : Vector3.zero;
        }

        public static int EffectiveMinSize(string? doctrineId)
        {
            var global = PluginConfig.MinSquadSize?.Value ?? 3;
            if (string.Equals(doctrineId, "death-rush", StringComparison.OrdinalIgnoreCase))
            {
                var dr = PluginConfig.DeathRushMinSquadSize?.Value ?? 1;
                return Math.Max(1, Math.Min(dr, global));
            }
            return Math.Max(1, global);
        }

        private ThreatAssessment AssessThreats(SquadUnit squad, SquadRuntimeState? state, int minSize)
        {
            var assessment = new ThreatAssessment
            {
                ThreatCount = 0,
                NearestDistance = float.MaxValue,
                CasualtyRatio = 0f,
                IsBroken = false,
                MissileThreatened = false,
                FlankOpportunity = squad.Members.Exists(m => m.AssignedRole == SquadRole.Flanker),
                TargetIsolated = false,
                ThreatStaggeredOrLow = false,
                NearbyTrollCount = 0,
                NearestTrollDistance = float.MaxValue,
                IndoorsOrCrypt = false,
                NearStructure = false,
                NearbyDvergrCount = 0,
                NearDvergr = false,
                NearWorkbench = false,
                NearestWorkbenchDistance = float.MaxValue,
                PlayersNearAssault = false,
                AssaultActive = false,
            };

            var alive = CountAlive(squad);
            var (casualtyRatio, isBroken) = ComputeCasualties(alive, state, minSize);
            assessment.CasualtyRatio = casualtyRatio;
            assessment.IsBroken = isBroken;

#if VALHEIM_REFS
            int engaged = 0;
            float nearest = float.MaxValue;
            float frontNearest = float.MaxValue;
            Vector3 centroid = Vector3.zero;
            int alivePos = 0;
            foreach (var m in squad.Members)
            {
                if (!m.IsAlive)
                    continue;
                alivePos++;
                centroid += m.Position;
                if (m.NativeHandle is MonsterAI ai)
                {
                    var target = GetTargetSafe(ai);
                    if (target != null)
                    {
                        engaged++;
                        var d = Vector3.Distance(m.Position, target.transform.position);
                        if (d < nearest)
                            nearest = d;
                        if ((m.AssignedRole == SquadRole.Front || m.AssignedRole == SquadRole.Leader)
                            && d < frontNearest)
                            frontNearest = d;

                        TryThreatConditionFlags(target, assessment);
                    }
                }
            }

            if (alivePos > 0)
                centroid = new Vector3(centroid.x / alivePos, centroid.y / alivePos, centroid.z / alivePos);

            assessment.ThreatCount = engaged > 0 ? Math.Max(1, engaged / Math.Max(1, alivePos)) : 0;
            if (engaged > 0)
                assessment.ThreatCount = 1;
            assessment.NearestDistance = nearest;

            // Player proximity as threat when AI targets not yet acquired (avoids eternal Hold).
            try
            {
                var players = ValheimWorldScan.CollectPlayerPositions();
                if (players.Count > 0 && alivePos > 0)
                {
                    foreach (var pp in players)
                    {
                        var d = Vector3.Distance(centroid, pp);
                        if (d < nearest)
                            nearest = d;
                    }
                    foreach (var m in squad.Members)
                    {
                        if (!m.IsAlive)
                            continue;
                        if (m.AssignedRole != SquadRole.Front && m.AssignedRole != SquadRole.Leader)
                            continue;
                        foreach (var pp in players)
                        {
                            var fd = Vector3.Distance(m.Position, pp);
                            if (fd <= 48f && fd < frontNearest)
                                frontNearest = fd;
                        }
                    }
                    if (nearest < float.MaxValue && nearest <= 48f)
                    {
                        if (assessment.ThreatCount <= 0)
                            assessment.ThreatCount = 1;
                        assessment.NearestDistance = nearest;
                    }
                }
            }
            catch { /* keep engaged-only assessment */ }

            assessment.MissileThreatened = assessment.ThreatCount > 0 && nearest < 12f
                && squad.Members.Exists(m => m.AssignedRole == SquadRole.Missile);

            ApplyFlankAndIsolate(centroid, nearest, assessment);

            if (frontNearest < float.MaxValue * 0.5f)
                assessment.FrontlineThreatDistance = frontNearest;

            EnrichTrollProximity(centroid, assessment);
            EnrichEnvironmentHeuristics(squad, centroid, assessment);
            _siege.EnrichWorkbenchProximity(centroid, assessment);
#else
            // Stub / CI: world threats stay empty; casualty / broken proxies above still fire
            // so anxiety / retreat doctrine branches work in offline sims.
            _ = squad;
            _ = Vector3.zero;
            _ = PluginConfig.StructureDefenseRange;
            _ = PluginConfig.DvergrSoftenRange;
            _siege.EnrichWorkbenchProximity(Vector3.zero, assessment);
#endif
            // Offline / injected threat hooks win over world scans (tests + stub sims).
            if (squad.DebugThreatCount.HasValue)
                assessment.ThreatCount = squad.DebugThreatCount.Value;
            if (squad.DebugNearestThreatDistance.HasValue)
            {
                assessment.NearestDistance = squad.DebugNearestThreatDistance.Value;
                assessment.FrontlineThreatDistance = squad.DebugNearestThreatDistance.Value;
            }

            var aliveForSiege = alive;
            assessment.AssaultActive =
                assessment.NearWorkbench
                && SiegeDirector.IsMasterEnabled
                && SiegeDirector.IsSiegeEligibleDoctrine(squad.Doctrine?.Id)
                && aliveForSiege >= SiegeDirector.MinAssaultSquadSize;
            return assessment;
        }

        /// <summary>
        /// Casualty proxies from PeakAlive (missing members vs historical peak) and optional HealthRatio.
        /// IsBroken when ratio ≥ <see cref="BrokenCasualtyThreshold"/> or alive drops below min after having met it.
        /// </summary>
        public static (float ratio, bool isBroken) ComputeCasualties(int alive, SquadRuntimeState? state, int minSize)
        {
            var peak = state != null ? Math.Max(state.PeakAlive, alive) : alive;
            if (peak <= 0)
                return (0f, false);

            var ratio = 1f - (float)alive / peak;
            if (ratio < 0f)
                ratio = 0f;
            if (ratio > 1f)
                ratio = 1f;

            var everMin = state != null && state.EverMetMinSize;
            var belowMin = everMin && alive < minSize;
            var isBroken = belowMin || ratio >= BrokenCasualtyThreshold;
            return (ratio, isBroken);
        }

        /// <summary>
        /// Alive roster for minSize / casualty math.
        /// 0.2.1: do not treat default/unknown HealthRatio (≤0, typically -1) as dead —
        /// HP dead-proxy only when HealthRatio is in (0, 0.02]. Under VALHEIM_REFS,
        /// refresh IsAlive from Character via NativeHandle MonsterAI each call.
        /// </summary>
        internal static int CountAlive(SquadUnit squad)
        {
            int n = 0;
            foreach (var m in squad.Members)
            {
#if VALHEIM_REFS
                if (m.NativeHandle is MonsterAI mai)
                {
                    try
                    {
                        var ch = ValheimIds.GetCharacter(mai);
                        if (ch != null)
                            m.IsAlive = !ch.IsDead();
                    }
                    catch
                    {
                        // keep existing IsAlive flag
                    }
                }
#endif
                if (!m.IsAlive)
                    continue;
                // HP dead-proxy only for known near-zero health — exclude 0 / negative (unknown).
                if (m.HealthRatio > 0f && m.HealthRatio <= 0.02f)
                    continue;
                n++;
            }
            return n;
        }

#if VALHEIM_REFS
        private static void EnrichEnvironmentHeuristics(
            SquadUnit squad,
            Vector3 centroid,
            ThreatAssessment assessment)
        {
            assessment.IndoorsOrCrypt = DetectIndoorsOrCrypt(centroid);

            var structRange = PluginConfig.StructureDefenseRange?.Value ?? 24f;
            assessment.NearStructure = DetectNearStructure(centroid, structRange);

            var dvergrRange = PluginConfig.DvergrSoftenRange?.Value ?? 30f;
            EnrichDvergrProximity(centroid, dvergrRange, assessment);

            _ = squad;
        }

        private static bool DetectIndoorsOrCrypt(Vector3 centroid)
        {
            try
            {
                var ground = ZoneSystem.instance != null
                    ? ZoneSystem.instance.GetGroundHeight(centroid)
                    : centroid.y;
                if (centroid.y < ground - 2.5f)
                    return true;
            }
            catch
            {
                // leave stub false
            }
            return false;
        }

        private static bool DetectNearStructure(Vector3 centroid, float range)
        {
            try
            {
                var pieces = UnityEngine.Object.FindObjectsOfType<Piece>();
                foreach (var p in pieces)
                {
                    if (p == null)
                        continue;
                    var d = Vector3.Distance(centroid, p.transform.position);
                    if (d > range)
                        continue;
                    var n = p.name ?? "";
                    if (Contains(n, "totem") || Contains(n, "fuling") || Contains(n, "goblin")
                        || Contains(n, "village") || Contains(n, "fence") || Contains(n, "wall")
                        || Contains(n, "banner") || Contains(n, "guard"))
                        return true;
                }
            }
            catch
            {
                // StructureDefenseRange remains a config placeholder for doctrines.
            }
            return false;
        }

        private static void EnrichDvergrProximity(Vector3 centroid, float range, ThreatAssessment assessment)
        {
            try
            {
                var ais = FactionTactics.Util.ValheimWorldScan.EnumerateMonsterAIs();
                int count = 0;
                foreach (var ai in ais)
                {
                    var ch = ValheimIds.GetCharacter(ai);
                    if (ch == null || ch.IsDead())
                        continue;
                    var prefab = ch.name.Replace("(Clone)", "").Trim();
                    if (!Starts(prefab, "Dvergr") && !Starts(prefab, "Dverger"))
                        continue;
                    var d = Vector3.Distance(centroid, ch.transform.position);
                    if (d > range)
                        continue;
                    count++;
                }
                assessment.NearbyDvergrCount = count;
                assessment.NearDvergr = count > 0;
            }
            catch
            {
                assessment.NearbyDvergrCount = 0;
                assessment.NearDvergr = false;
            }
        }

        private static void EnrichTrollProximity(Vector3 centroid, ThreatAssessment assessment)
        {
            if (!TrollFortressHelper.IsEnabled)
                return;

            try
            {
                var range = TrollFortressHelper.SynergyRange;
                var ais = FactionTactics.Util.ValheimWorldScan.EnumerateMonsterAIs();
                int count = 0;
                float nearest = float.MaxValue;
                foreach (var ai in ais)
                {
                    var ch = ValheimIds.GetCharacter(ai);
                    if (ch == null || ch.IsDead())
                        continue;
                    var prefab = ch.name.Replace("(Clone)", "").Trim();
                    if (!TrollFortressHelper.IsTrollPrefab(prefab))
                        continue;
                    var d = Vector3.Distance(centroid, ch.transform.position);
                    if (d > range)
                        continue;
                    count++;
                    if (d < nearest)
                        nearest = d;
                }

                assessment.NearbyTrollCount = count;
                assessment.NearestTrollDistance = nearest;
            }
            catch
            {
                // Unit-test hosts / missing Splatform — leave stub zeros.
                assessment.NearbyTrollCount = 0;
                assessment.NearestTrollDistance = float.MaxValue;
            }
        }

        private static void TryThreatConditionFlags(Character target, ThreatAssessment assessment)
        {
            try
            {
                var getHealth = typeof(Character).GetMethod("GetHealth");
                var getMax = typeof(Character).GetMethod("GetMaxHealth");
                if (getHealth != null && getMax != null)
                {
                    var hp = (float)getHealth.Invoke(target, null);
                    var max = (float)getMax.Invoke(target, null);
                    if (max > 0f && hp / max <= 0.35f)
                        assessment.ThreatStaggeredOrLow = true;
                }

                var staggerField = typeof(Character).GetField("m_staggerDamageFactor")
                                   ?? typeof(Character).GetField("m_staggerTimer");
                _ = staggerField;
            }
            catch
            {
                // ignore — Ambush still uses TargetIsolated / FlankOpportunity
            }
        }

        // TODO(hypothesis): MonsterAI target accessor name varies by game version.
        private static Character? GetTargetSafe(MonsterAI ai)
        {
            try
            {
                var mi = typeof(MonsterAI).GetMethod("GetTargetCreature")
                         ?? typeof(MonsterAI).GetMethod("GetAttackTarget");
                if (mi != null)
                    return mi.Invoke(ai, null) as Character;
            }
            catch
            {
                // ignore
            }
            return null;
        }

        private static bool Starts(string name, string prefix)
            => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        private static bool Contains(string name, string token)
            => name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
#endif
    }
}
