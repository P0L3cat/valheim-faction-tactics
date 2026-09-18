using System.Collections.Generic;
using FactionTactics.Commander;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using UnityEngine;

namespace FactionTactics.Squad
{
    /// <summary>
    /// Route 1 orchestrator: discover → min-size gate → roles → commander propose → apply.
    /// Consults Route 2 scorers before committing (NullScorer = no-op).
    /// Enriches ThreatAssessment with TrollFortress + environment heuristics (IndoorsOrCrypt,
    /// NearStructure, NearDvergr) for doctrine B/C biases.
    /// </summary>
    public sealed class SquadDirector
    {
        private readonly SquadDiscovery _discovery;
        private readonly ICommander _commander;
        private readonly DoctrinePackRegistry _registry;
        private readonly IRoleScorer _roleScorer;
        private readonly IActionScorer _actionScorer;
        private readonly OrderApplicator _applicator;

        private readonly List<SquadUnit> _active = new List<SquadUnit>();

        public SquadDirector(
            SquadDiscovery discovery,
            ICommander commander,
            DoctrinePackRegistry registry,
            IRoleScorer roleScorer,
            IActionScorer actionScorer,
            OrderApplicator applicator)
        {
            _discovery = discovery;
            _commander = commander;
            _registry = registry;
            _roleScorer = roleScorer;
            _actionScorer = actionScorer;
            _applicator = applicator;
        }

        public IReadOnlyList<SquadUnit> ActiveSquads => _active;

        public void Tick(float dt)
        {
            _active.Clear();
            var discovered = _discovery.Discover();
            var minSize = PluginConfig.MinSquadSize.Value;

            foreach (var squad in discovered)
            {
                squad.AgeSeconds += dt;

                if (squad.Members.Count < minSize)
                {
                    if (PluginConfig.DebugLogging.Value)
                        Plugin.Log.LogDebug($"Squad {squad.SquadId} below min size ({squad.Members.Count}/{minSize}) — vanilla AI.");
                    continue;
                }

                RefineRolesWithScorer(squad);
                var threats = AssessThreats(squad);
                var snapshot = SquadSnapshot.FromSquad(squad, threats);

                // Route 2: action scorer can bias which order ScriptedCommander already chose;
                // commander remains source of SquadOrder DTO (Route 3 contract).
                var order = _commander.Propose(snapshot);
                if (order == null)
                    continue;

                order = MaybeRescoreOrder(order, snapshot);
                squad.CurrentOrder = order;
                squad.PreviousOrderKind = order.OrderKind;
                _applicator.Apply(squad, order);
                _active.Add(squad);

                if (PluginConfig.DebugLogging.Value)
                {
                    Plugin.Log.LogInfo(
                        $"[{squad.Doctrine.DisplayName}] {squad.SquadId} n={squad.Members.Count} → {order.OrderKind} ({order.Formation}/{order.Stance})");
                }
            }

            _ = _registry;
        }

        private void RefineRolesWithScorer(SquadUnit squad)
        {
            // NullRoleScorer returns 0 for all → keep doctrine assignment.
            // Non-null scorers: pick max score among doctrine-legal roles.
            var threats = AssessThreats(squad);
            var snapshot = SquadSnapshot.FromSquad(squad, threats);

            foreach (var member in squad.Members)
            {
                var best = member.AssignedRole;
                var bestScore = _roleScorer.ScoreRole(member, best, snapshot);
                foreach (SquadRole role in System.Enum.GetValues(typeof(SquadRole)))
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

        private SquadOrder MaybeRescoreOrder(SquadOrder order, SquadSnapshot snapshot)
        {
            var currentScore = _actionScorer.ScoreAction(order.OrderKind, snapshot);
            if (currentScore == 0f)
                return order; // NullScorer or no preference

            DoctrineOrderKind best = order.OrderKind;
            float bestScore = currentScore;
            foreach (DoctrineOrderKind kind in System.Enum.GetValues(typeof(DoctrineOrderKind)))
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

        private static ThreatAssessment AssessThreats(SquadUnit squad)
        {
            // v0: lightweight heuristics. VALHEIM_REFS path can inspect MonsterAI targets later.
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
            };

#if VALHEIM_REFS
            int engaged = 0;
            float nearest = float.MaxValue;
            Vector3 centroid = Vector3.zero;
            int alive = 0;
            foreach (var m in squad.Members)
            {
                if (!m.IsAlive)
                    continue;
                alive++;
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

                        // Brute commit proxies: low health / stagger when APIs exist.
                        TryThreatConditionFlags(target, assessment);
                    }
                }
            }

            if (alive > 0)
                centroid = new Vector3(centroid.x / alive, centroid.y / alive, centroid.z / alive);

            assessment.ThreatCount = engaged > 0 ? System.Math.Max(1, engaged / System.Math.Max(1, alive)) : 0;
            // Prefer counting distinct engagement: at least one engaged player-side threat.
            if (engaged > 0)
                assessment.ThreatCount = 1;
            assessment.NearestDistance = nearest;
            assessment.MissileThreatened = engaged > 0 && nearest < 12f
                && squad.Members.Exists(m => m.AssignedRole == SquadRole.Missile);

            // Isolated target: single engagement and pack outnumbers (swarm surround).
            assessment.TargetIsolated = engaged > 0 && alive >= 3
                && (assessment.ThreatCount <= 1)
                && nearest <= 12f;

            EnrichTrollProximity(centroid, assessment);
            EnrichEnvironmentHeuristics(squad, centroid, assessment);
#else
            // Stub / CI: no world threats; environment flags remain false (doctrine FSMs still run on injected snapshots).
            _ = squad;
            _ = Vector3.zero;
            // Config placeholders are available for tests that set flags manually on ThreatAssessment.
            _ = PluginConfig.StructureDefenseRange;
            _ = PluginConfig.DvergrSoftenRange;
#endif
            return assessment;
        }

#if VALHEIM_REFS
        private static void EnrichEnvironmentHeuristics(
            SquadUnit squad,
            Vector3 centroid,
            ThreatAssessment assessment)
        {
            // IndoorsOrCrypt: dungeon / crypt / buried-height heuristic (stub-tolerant).
            assessment.IndoorsOrCrypt = DetectIndoorsOrCrypt(centroid);

            // NearStructure: piece/totem/village proxies within StructureDefenseRange.
            var structRange = PluginConfig.StructureDefenseRange?.Value ?? 24f;
            assessment.NearStructure = DetectNearStructure(centroid, structRange);

            // NearDvergr: Dvergr* allies/neutrals within soften range (InsectSiege C).
            var dvergrRange = PluginConfig.DvergrSoftenRange?.Value ?? 30f;
            EnrichDvergrProximity(centroid, dvergrRange, assessment);

            _ = squad;
        }

        private static bool DetectIndoorsOrCrypt(Vector3 centroid)
        {
            try
            {
                // Heuristic: substantially below surface / dungeon env name tokens.
                // TODO(hypothesis): EnvMan / ZoneSystem location dungeon flags when available.
                var ground = ZoneSystem.instance != null
                    ? ZoneSystem.instance.GetGroundHeight(centroid)
                    : centroid.y;
                if (centroid.y < ground - 2.5f)
                    return true;

                // Prefab/location name scan is expensive; skip heavy scans — leave false if unsure.
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
                // TODO(hypothesis): Piece.FindPiecesInRadius / WearNTear for totems/walls.
                // Lightweight: scan Character-less Piece via FindObjects if available; else false.
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
                var ais = UnityEngine.Object.FindObjectsOfType<MonsterAI>();
                int count = 0;
                foreach (var ai in ais)
                {
                    var ch = ai.m_character;
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

            var range = TrollFortressHelper.SynergyRange;
            // TODO(hypothesis): FindObjectsOfType is OK on 0.5–1s tick; prefer registry if hot.
            var ais = UnityEngine.Object.FindObjectsOfType<MonsterAI>();
            int count = 0;
            float nearest = float.MaxValue;
            foreach (var ai in ais)
            {
                var ch = ai.m_character;
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

        private static void TryThreatConditionFlags(Character target, ThreatAssessment assessment)
        {
            try
            {
                // Low HP proxy via common Character health accessors.
                var getHealth = typeof(Character).GetMethod("GetHealth");
                var getMax = typeof(Character).GetMethod("GetMaxHealth");
                if (getHealth != null && getMax != null)
                {
                    var hp = (float)getHealth.Invoke(target, null);
                    var max = (float)getMax.Invoke(target, null);
                    if (max > 0f && hp / max <= 0.35f)
                        assessment.ThreatStaggeredOrLow = true;
                }

                // Stagger / staggerable flags if present on this build.
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
                // Common patterns across Valheim versions — pick whatever exists at runtime via reflection if needed.
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
            => name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase);

        private static bool Contains(string name, string token)
            => name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;
#endif
    }
}
