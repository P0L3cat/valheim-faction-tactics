using System;
using System.Collections.Generic;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Squad;
using UnityEngine;

namespace FactionTactics.Tests.Sim
{
    /// <summary>One simulated player whose world position is a function of sim time.</summary>
    internal sealed class SimPlayer
    {
        public long Id { get; }
        public Func<float, Vector3> Path { get; }

        public SimPlayer(long id, Func<float, Vector3> path)
        {
            Id = id;
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public static SimPlayer Waypoints(long id, IReadOnlyList<Vector3> points, float segmentSeconds)
        {
            if (points == null || points.Count == 0)
                throw new ArgumentException("need at least one waypoint", nameof(points));
            if (segmentSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(segmentSeconds));

            return new SimPlayer(id, t =>
            {
                if (points.Count == 1)
                    return points[0];
                var total = segmentSeconds * (points.Count - 1);
                var clamped = t < 0f ? 0f : (t > total ? total : t);
                var idx = (int)(clamped / segmentSeconds);
                if (idx >= points.Count - 1)
                    return points[points.Count - 1];
                var u = (clamped - idx * segmentSeconds) / segmentSeconds;
                return Vector3.Lerp(points[idx], points[idx + 1], u);
            });
        }

        public static SimPlayer Parametric(long id, Func<float, Vector3> path) => new SimPlayer(id, path);
    }

    /// <summary>Per-member snapshot after a sim tick's Apply (+ optional step).</summary>
    internal readonly struct MemberTickMetric
    {
        public long InstanceId { get; }
        public Vector3 Position { get; }
        public Vector3 DesiredPosition { get; }
        public bool PreferRun { get; }
        public bool HoldGround { get; }
        public bool HasIntent { get; }

        public MemberTickMetric(
            long instanceId,
            Vector3 position,
            Vector3 desiredPosition,
            bool preferRun,
            bool holdGround,
            bool hasIntent)
        {
            InstanceId = instanceId;
            Position = position;
            DesiredPosition = desiredPosition;
            PreferRun = preferRun;
            HoldGround = holdGround;
            HasIntent = hasIntent;
        }
    }

    /// <summary>Aggregate metrics recorded after each offline tick.</summary>
    internal sealed class TickMetrics
    {
        public int TickIndex { get; set; }
        public float Time { get; set; }
        public long StickyId { get; set; }
        public bool HasSticky { get; set; }
        public Vector3 StickyPosition { get; set; }
        public DoctrineOrderKind? OrderKind { get; set; }
        public Vector3 PackCentroid { get; set; }
        public float PackDiameter { get; set; }
        public float MeanDesiredToSticky { get; set; }
        public float MeanPositionToSticky { get; set; }
        public float MeanDesiredToCentroid { get; set; }
        /// <summary>Count of intents where PreferRun != !HoldGround.</summary>
        public int PreferRunViolations { get; set; }
        public List<MemberTickMetric> Members { get; } = new List<MemberTickMetric>();
        public List<(long id, Vector3 pos)> PlayerPositions { get; } = new List<(long, Vector3)>();
    }

    /// <summary>
    /// Offline multi-tick engine: simulated players → Ambush sticky → Apply (or director) →
    /// optional step-toward-DesiredPosition, recording quantitative reaction metrics.
    /// </summary>
    internal sealed class PlayerPathSim
    {
        private readonly List<SimPlayer> _players = new List<SimPlayer>();
        private SquadUnit? _squad;
        private SquadRuntimeState _runtime = new SquadRuntimeState();
        private SquadOrder _order = new SquadOrder
        {
            OrderKind = DoctrineOrderKind.Flank,
            Formation = FormationType.Orb,
            Stance = StanceType.Aggressive,
        };
        private readonly OrderApplicator _applicator = new OrderApplicator();
        private float _dt = 0.25f;
        private float _runSpeed = 7f;
        private float _walkSpeed = 3.5f;
        private bool _stepMembers = true;
        private Action<PlayerPathSim, float, int>? _beforeTick;
        private Func<PlayerPathSim, float, int, SquadOrder>? _orderProvider;
        /// <summary>When set, players returning false are omitted from the candidate list (disappear).</summary>
        private Func<long, float, int, bool>? _playerVisible;

        public SquadUnit? Squad => _squad;
        public SquadRuntimeState Runtime => _runtime;
        public float Dt => _dt;
        public IReadOnlyList<SimPlayer> Players => _players;

        public PlayerPathSim WithPlayers(params SimPlayer[] players)
        {
            _players.Clear();
            if (players != null)
                _players.AddRange(players);
            return this;
        }

        public PlayerPathSim WithSquad(SquadUnit squad, SquadRuntimeState? runtime = null)
        {
            _squad = squad ?? throw new ArgumentNullException(nameof(squad));
            _runtime = runtime ?? new SquadRuntimeState();
            return this;
        }

        public PlayerPathSim WithOrder(SquadOrder order)
        {
            _order = order ?? throw new ArgumentNullException(nameof(order));
            return this;
        }

        public PlayerPathSim WithOrderProvider(Func<PlayerPathSim, float, int, SquadOrder> provider)
        {
            _orderProvider = provider;
            return this;
        }

        public PlayerPathSim WithDt(float dt)
        {
            _dt = dt > 0f ? dt : 0.25f;
            return this;
        }

        public PlayerPathSim WithSpeeds(float runSpeed, float walkSpeed)
        {
            _runSpeed = runSpeed;
            _walkSpeed = walkSpeed;
            return this;
        }

        public PlayerPathSim WithMemberStepping(bool enabled)
        {
            _stepMembers = enabled;
            return this;
        }

        public PlayerPathSim BeforeTick(Action<PlayerPathSim, float, int> hook)
        {
            _beforeTick = hook;
            return this;
        }

        /// <summary>
        /// Control per-tick presence. Returning false omits that player from sticky candidates
        /// (models logout / scanner gap / teleport vanish). Keep-last sticky applies when empty.
        /// </summary>
        public PlayerPathSim WithPlayerVisible(Func<long, float, int, bool> visible)
        {
            _playerVisible = visible;
            return this;
        }

        public List<(long id, Vector3 pos)> SamplePlayers(float time, int tickIndex = 0)
        {
            var list = new List<(long, Vector3)>(_players.Count);
            foreach (var p in _players)
            {
                if (_playerVisible != null && !_playerVisible(p.Id, time, tickIndex))
                    continue;
                list.Add((p.Id, p.Path(time)));
            }
            return list;
        }

        /// <summary>
        /// Run <paramref name="ticks"/> offline ticks. Clears OrderApplicator.Intents once at start.
        /// </summary>
        public List<TickMetrics> Run(int ticks)
        {
            if (_squad == null)
                throw new InvalidOperationException("WithSquad required before Run");
            if (ticks < 1)
                throw new ArgumentOutOfRangeException(nameof(ticks));

            OrderApplicator.Intents.Clear();
            var history = new List<TickMetrics>(ticks);
            float t = 0f;

            for (int i = 0; i < ticks; i++)
            {
                _beforeTick?.Invoke(this, t, i);

                var players = SamplePlayers(t, i);
                var centroid = ComputeCentroid(_squad);
                OrderApplicator.UpdateAmbushStickyAnchor(_runtime, centroid, players, _dt);

                var order = _orderProvider != null ? _orderProvider(this, t, i) : _order;
                _order = order;
                _applicator.Apply(_squad, order, _runtime);

                if (_stepMembers)
                    StepMembersTowardIntents();

                history.Add(CaptureMetrics(i, t, players, order));
                t += _dt;
            }

            return history;
        }

        /// <summary>
        /// Sticky-only loop (no Apply) — useful for pure hysteresis flap counts.
        /// Still records sticky id + player positions; member metrics empty.
        /// </summary>
        public List<TickMetrics> RunStickyOnly(int ticks, Vector3? fixedCentroid = null)
        {
            if (ticks < 1)
                throw new ArgumentOutOfRangeException(nameof(ticks));

            var history = new List<TickMetrics>(ticks);
            float t = 0f;
            for (int i = 0; i < ticks; i++)
            {
                _beforeTick?.Invoke(this, t, i);
                var players = SamplePlayers(t, i);
                var centroid = fixedCentroid
                    ?? (_squad != null ? ComputeCentroid(_squad) : Vector3.zero);
                OrderApplicator.UpdateAmbushStickyAnchor(_runtime, centroid, players, _dt);

                var m = new TickMetrics
                {
                    TickIndex = i,
                    Time = t,
                    HasSticky = _runtime.HasStickyPlayer,
                    StickyId = _runtime.StickyPlayerId,
                    StickyPosition = _runtime.StickyPlayerPosition,
                    PackCentroid = centroid,
                };
                m.PlayerPositions.AddRange(players);
                history.Add(m);
                t += _dt;
            }

            return history;
        }

        private void StepMembersTowardIntents()
        {
            foreach (var member in _squad!.Members)
            {
                if (!member.IsAlive)
                    continue;
                if (!OrderApplicator.Intents.TryGetValue(member.InstanceId, out var intent))
                    continue;

                var speed = intent.PreferRun ? _runSpeed : _walkSpeed;
                if (intent.HoldGround)
                    speed = 0f;
                var step = speed * _dt;
                if (step <= 0f)
                    continue;

                var delta = intent.DesiredPosition - member.Position;
                delta.y = 0f;
                var dist = delta.magnitude;
                if (dist <= 1e-4f)
                {
                    member.Position = intent.DesiredPosition;
                    continue;
                }

                if (dist <= step)
                    member.Position = intent.DesiredPosition;
                else
                    member.Position = member.Position + delta * (step / dist);
            }
        }

        private TickMetrics CaptureMetrics(
            int tickIndex,
            float time,
            List<(long id, Vector3 pos)> players,
            SquadOrder order)
        {
            var living = new List<SquadMemberView>();
            foreach (var m in _squad!.Members)
            {
                if (m.IsAlive)
                    living.Add(m);
            }

            var centroid = ComputeCentroid(_squad);
            var diameter = PackDiameter(living);
            var sticky = _runtime.StickyPlayerPosition;
            float sumDesiredSticky = 0f;
            float sumPosSticky = 0f;
            float sumDesiredCentroid = 0f;
            int nWithIntent = 0;
            int violations = 0;

            var metrics = new TickMetrics
            {
                TickIndex = tickIndex,
                Time = time,
                HasSticky = _runtime.HasStickyPlayer,
                StickyId = _runtime.StickyPlayerId,
                StickyPosition = sticky,
                OrderKind = order.OrderKind,
                PackCentroid = centroid,
                PackDiameter = diameter,
            };
            metrics.PlayerPositions.AddRange(players);

            foreach (var m in living)
            {
                var has = OrderApplicator.Intents.TryGetValue(m.InstanceId, out var intent);
                var desired = has ? intent!.DesiredPosition : m.Position;
                var preferRun = has && intent!.PreferRun;
                var hold = has && intent!.HoldGround;
                if (has)
                {
                    nWithIntent++;
                    sumDesiredSticky += Vector3.Distance(desired, sticky);
                    sumPosSticky += Vector3.Distance(m.Position, sticky);
                    sumDesiredCentroid += Vector3.Distance(desired, centroid);
                    if (preferRun != !hold)
                        violations++;
                }

                metrics.Members.Add(new MemberTickMetric(
                    m.InstanceId,
                    m.Position,
                    desired,
                    preferRun,
                    hold,
                    has));
            }

            metrics.PreferRunViolations = violations;
            metrics.MeanDesiredToSticky = nWithIntent > 0 ? sumDesiredSticky / nWithIntent : 0f;
            metrics.MeanPositionToSticky = nWithIntent > 0 ? sumPosSticky / nWithIntent : 0f;
            metrics.MeanDesiredToCentroid = nWithIntent > 0 ? sumDesiredCentroid / nWithIntent : 0f;
            return metrics;
        }

        public static Vector3 ComputeCentroid(SquadUnit squad)
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
            return n == 0 ? Vector3.zero : sum / n;
        }

        public static float PackDiameter(IReadOnlyList<SquadMemberView> living)
        {
            float max = 0f;
            for (int i = 0; i < living.Count; i++)
            {
                for (int j = i + 1; j < living.Count; j++)
                {
                    var d = Vector3.Distance(living[i].Position, living[j].Position);
                    if (d > max)
                        max = d;
                }
            }
            return max;
        }

        public static int CountStickyFlaps(IReadOnlyList<TickMetrics> history)
        {
            int flaps = 0;
            for (int i = 1; i < history.Count; i++)
            {
                if (!history[i].HasSticky || !history[i - 1].HasSticky)
                    continue;
                if (history[i].StickyId != history[i - 1].StickyId)
                    flaps++;
            }
            return flaps;
        }

        public static float MaxPackDiameter(IReadOnlyList<TickMetrics> history)
        {
            float max = 0f;
            foreach (var h in history)
            {
                if (h.PackDiameter > max)
                    max = h.PackDiameter;
            }
            return max;
        }

        public static int TotalPreferRunViolations(IReadOnlyList<TickMetrics> history)
        {
            int n = 0;
            foreach (var h in history)
                n += h.PreferRunViolations;
            return n;
        }
    }
}
