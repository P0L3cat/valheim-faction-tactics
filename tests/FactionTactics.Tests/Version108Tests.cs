using System.Collections.Generic;
using System.Linq;
using FactionTactics.Commander;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Siege;
using FactionTactics.Squad;
using FactionTactics.Tests.Sim;
using UnityEngine;
using Xunit;

namespace FactionTactics.Tests
{
    public class Version108Tests
    {
        public Version108Tests() => TestConfig.EnsureBound();

        [Fact]
        public void PreferRun_true_when_not_holding()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            for (int i = 0; i < squad.Members.Count; i++)
                squad.Members[i].Position = new Vector3(0.15f * i, 0f, 0.05f * i);
            var spawnCentroid = PlayerPathSim.ComputeCentroid(squad);

            var player = SimPlayer.Parametric(3, t => new Vector3(10f + t * 3f, 0f, 0f));
            var hist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(8f, 3.5f)
                .WithMemberStepping(true)
                .WithPlayers(player)
                .WithSquad(squad, new SquadRuntimeState { RomanPhase = RomanPhase.ApproachStandoff })
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Advance,
                    Formation = FormationType.ShieldWall,
                    Stance = StanceType.Aggressive,
                })
                .Run(12);

            var late = hist[hist.Count - 1];
            var meanPos = late.Members.Average(m => MotionPredicates.Dist(m.Position, spawnCentroid));
            var meanDesired = late.Members.Average(m => MotionPredicates.Dist(m.DesiredPosition, spawnCentroid));
            Assert.True(meanPos > 1.5f,
                $"PRIMARY: Advance Positions freeze-in-blob meanDist={meanPos:F2}");
            Assert.True(meanDesired > 2f,
                $"PRIMARY: Advance Desired freeze-in-blob meanDist={meanDesired:F2}");

            // Secondary flags
            Assert.Equal(0, PlayerPathSim.TotalPreferRunViolations(hist));
            foreach (var tick in hist)
            {
                foreach (var m in tick.Members)
                {
                    Assert.True(m.PreferRun);
                    Assert.False(m.HoldGround);
                }
            }
        }

        [Fact]
        public void PreferRun_mirrors_not_HoldGround()
        {
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            var runtime = new SquadRuntimeState
            {
                RomanPhase = RomanPhase.ContactHold,
                LastThreatDistance = 2f,
            };
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            }, runtime);

            Assert.NotEmpty(OrderApplicator.Intents);
            foreach (var intent in OrderApplicator.Intents.Values)
                Assert.Equal(!intent.HoldGround, intent.PreferRun);
        }

        [Fact]
        public void Stragglers_within_merge_radius_join_parent_and_get_intents()
        {
            PluginConfig.MinSquadSize.Value = 3;
            PluginConfig.SquadMergeRadius.Value = 40f;

            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var large = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            large.SquadId = "roman-large";
            var small = FakeSnapshots.MakeSquad(roman, 2, "Skeleton");
            small.SquadId = "roman-straggler";
            for (int i = 0; i < small.Members.Count; i++)
                small.Members[i].Position = new Vector3(20f + i, 0f, 0f);

            var director = new SquadDirector(
                new FakeDiscovery(small, large),
                new ScriptedCommander(registry, new SiegeDirector()),
                registry,
                new NullRoleScorer(),
                new NullActionScorer(),
                new OrderApplicator(),
                new SiegeDirector());

            OrderApplicator.Intents.Clear();
            director.Tick(0.75f);

            // Tick already merged once — assert ActiveSquads only (no second MergeStragglers absorb).
            Assert.Single(director.ActiveSquads);
            var active = director.ActiveSquads[0];
            Assert.True(active.Members.Count >= 6);
            var ids = active.Members.Select(m => m.InstanceId).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.NotNull(active.CurrentOrder);
            foreach (var m in small.Members)
                Assert.True(OrderApplicator.Intents.ContainsKey(m.InstanceId),
                    $"missing intent for merged straggler {m.InstanceId}");

            // Fresh copies: MergeStragglers helper alone also folds once with unique ids.
            var large2 = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            large2.SquadId = "roman-large2";
            var small2 = FakeSnapshots.MakeSquad(roman, 2, "Skeleton");
            small2.SquadId = "roman-straggler2";
            for (int i = 0; i < small2.Members.Count; i++)
                small2.Members[i].Position = new Vector3(20f + i, 0f, 0f);
            var merged = SquadDirector.MergeStragglers(new[] { small2, large2 });
            Assert.Single(merged);
            var mid = merged[0].Members.Select(m => m.InstanceId).ToList();
            Assert.Equal(mid.Count, mid.Distinct().Count());
        }

        [Fact]
        public void Isolated_stragglers_beyond_merge_radius_get_no_intents()
        {
            PluginConfig.MinSquadSize.Value = 3;
            PluginConfig.SquadMergeRadius.Value = 40f;

            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var large = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            large.SquadId = "roman-large";
            var small = FakeSnapshots.MakeSquad(roman, 2, "Skeleton");
            small.SquadId = "roman-isolated";
            for (int i = 0; i < small.Members.Count; i++)
                small.Members[i].Position = new Vector3(80f + i, 0f, 0f);

            var director = new SquadDirector(
                new FakeDiscovery(small, large),
                new ScriptedCommander(registry, new SiegeDirector()),
                registry,
                new NullRoleScorer(),
                new NullActionScorer(),
                new OrderApplicator(),
                new SiegeDirector());

            OrderApplicator.Intents.Clear();
            director.Tick(0.75f);

            Assert.Single(director.ActiveSquads);
            Assert.Equal(large.SquadId, director.ActiveSquads[0].SquadId);
            Assert.Null(small.CurrentOrder);
            foreach (var m in small.Members)
                Assert.False(OrderApplicator.Intents.ContainsKey(m.InstanceId));
        }

        [Fact]
        public void Ambush_sticky_player_hysteresis_prevents_flap()
        {
            // Pin TickIntervalSeconds below dwell so null-dt never equals a full dwell tick.
            // Sims always pass explicit dt (see also Ambush_sticky_null_dt_one_breach_does_not_steal_when_tick_below_dwell).
            PluginConfig.TickIntervalSeconds.Value = 0.75f;
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            PluginConfig.AmbushStickySwitchDwellSeconds.Value = 1.0f;
            var state = new SquadRuntimeState();
            var centroid = Vector3.zero;
            const float dt = 0.5f;

            OrderApplicator.UpdateAmbushStickyAnchor(state, centroid, new List<(long, Vector3)>
            {
                (101, new Vector3(10f, 0f, 0f)),
                (202, new Vector3(12f, 0f, 0f)),
            }, dt);
            Assert.True(state.HasStickyPlayer);
            Assert.Equal(101, state.StickyPlayerId);

            // Only 3m closer — below 10m hysteresis — keep sticky.
            OrderApplicator.UpdateAmbushStickyAnchor(state, centroid, new List<(long, Vector3)>
            {
                (101, new Vector3(10f, 0f, 0f)),
                (303, new Vector3(7f, 0f, 0f)),
            }, dt);
            Assert.Equal(101, state.StickyPlayerId);

            // 15m closer — still sticky until AmbushStickySwitchDwellSeconds elapses.
            var closer = new List<(long, Vector3)>
            {
                (101, new Vector3(20f, 0f, 0f)),
                (404, new Vector3(5f, 0f, 0f)),
            };
            OrderApplicator.UpdateAmbushStickyAnchor(state, centroid, closer, dt);
            Assert.Equal(101, state.StickyPlayerId);
            // Second half-second completes 1.0s dwell → switch.
            OrderApplicator.UpdateAmbushStickyAnchor(state, centroid, closer, dt);
            Assert.Equal(404, state.StickyPlayerId);
        }

        /// <summary>
        /// Document+pin: null deltaTime uses TickIntervalSeconds. With TickInterval=0.75 and dwell=1.0,
        /// one hysteresis-breach tick must NOT steal sticky. If TickInterval were raised to equal dwell,
        /// a single null-dt breach would complete dwell in one call (known residual for 1.0.12 —
        /// tests always pass explicit dt; live Apply path uses TickInterval).
        /// </summary>
        [Fact]
        public void Ambush_sticky_null_dt_one_breach_does_not_steal_when_tick_below_dwell()
        {
            PluginConfig.TickIntervalSeconds.Value = 0.75f;
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            PluginConfig.AmbushStickySwitchDwellSeconds.Value = 1.0f;
            var state = new SquadRuntimeState();
            var centroid = Vector3.zero;

            OrderApplicator.UpdateAmbushStickyAnchor(state, centroid, new List<(long, Vector3)>
            {
                (101, new Vector3(20f, 0f, 0f)),
            }, 0.25f);
            Assert.Equal(101L, state.StickyPlayerId);

            // Null dt → TickIntervalSeconds=0.75 < dwell=1.0 → one breach must not steal.
            OrderApplicator.UpdateAmbushStickyAnchor(state, centroid, new List<(long, Vector3)>
            {
                (101, new Vector3(20f, 0f, 0f)),
                (202, new Vector3(5f, 0f, 0f)),
            }); // intentional null dt
            Assert.Equal(101L, state.StickyPlayerId);
            Assert.Equal(202L, state.StickySwitchCandidateId);
            Assert.True(state.StickySwitchCandidateSeconds < 1.0f - 1e-3f);
        }

        
        [Fact]
        public void Ambush_flank_orb_slots_center_on_sticky_player()
        {
            var registry = DoctrinePackRegistry.CreateDefault();
            var ambush = registry.GetById("ambush")!;
            var squad = FakeSnapshots.MakeSquad(ambush, 4, "Greydwarf");
            for (int i = 0; i < squad.Members.Count; i++)
                squad.Members[i].Position = new Vector3(100f + i, 0f, 0f);

            var runtime = new SquadRuntimeState
            {
                HasStickyPlayer = true,
                StickyPlayerId = 42,
                StickyPlayerPosition = Vector3.zero,
            };
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Flank,
                Formation = FormationType.Orb,
                Stance = StanceType.Aggressive,
            }, runtime);

            foreach (var m in squad.Members)
            {
                Assert.True(OrderApplicator.Intents.TryGetValue(m.InstanceId, out var intent),
                    $"missing intent for {m.InstanceId}");
                var d = Vector3.Distance(intent!.DesiredPosition, runtime.StickyPlayerPosition);
                Assert.True(d < 20f, $"slot far from sticky player: {d}");
                Assert.True(intent.PreferRun);
            }
        }

    }
}
