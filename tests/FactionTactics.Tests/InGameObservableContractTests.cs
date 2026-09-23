using System.Collections.Generic;
using System.Linq;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Squad;
using FactionTactics.Tests.Sim;
using UnityEngine;
using Xunit;

namespace FactionTactics.Tests
{
    /// <summary>
    /// Observable contracts named in docs/PROOF-1.0.11.md.
    /// Offline proves intent/role/sticky math only — not Valheim pixels. See Proof §1–2.
    /// </summary>
    public class InGameObservableContractTests
    {
        public InGameObservableContractTests() => TestConfig.EnsureBound();

        /// <summary>PROOF claim 1: PreferRun == !HoldGround on Advance; planted Hold Fronts plant.</summary>
        [Fact]
        public void Observable_PreferRun_equals_not_HoldGround_Advance_runs_Hold_plants()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 5, "Skeleton");

            var runtime = new SquadRuntimeState { RomanPhase = RomanPhase.PressContact };
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Advance,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Aggressive,
            }, runtime);

            foreach (var m in squad.Members)
            {
                Assert.True(OrderApplicator.Intents.TryGetValue(m.InstanceId, out var intent));
                Assert.Equal(!intent.HoldGround, intent.PreferRun);
                Assert.True(intent.PreferRun, "Advance must PreferRun while pressing");
                Assert.False(intent.HoldGround);
            }

            runtime.RomanPhase = RomanPhase.StandoffHold;
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            }, runtime);

            var anyPlant = false;
            foreach (var m in squad.Members)
            {
                Assert.True(OrderApplicator.Intents.TryGetValue(m.InstanceId, out var intent));
                Assert.Equal(!intent.HoldGround, intent.PreferRun);
                if (intent.HoldGround)
                {
                    anyPlant = true;
                    Assert.False(intent.PreferRun);
                }
            }
            Assert.True(anyPlant,
                "LIVE STEP if this fails: StandoffHold should plant at least one Front/Leader — see PROOF claim 1");
        }

        /// <summary>PROOF claim 2: Ambush slots track walking sticky player within orbit band.</summary>
        [Fact]
        public void Observable_Ambush_orbit_tracks_walking_player_slots_stay_near_player()
        {
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            PluginConfig.AmbushStickySwitchDwellSeconds.Value = 1.0f;
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;

            var registry = DoctrinePackRegistry.CreateDefault();
            var ambush = registry.GetById("ambush")!;
            var squad = FakeSnapshots.MakeSquad(ambush, 5, "Greydwarf");
            for (int i = 0; i < squad.Members.Count; i++)
                squad.Members[i].Position = new Vector3(8f + i, 0f, 0f);

            var player = SimPlayer.Waypoints(101, new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(30f, 0f, 0f),
                new Vector3(30f, 0f, 30f),
            }, 2.0f);

            var hist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithMemberStepping(false)
                .WithPlayers(player)
                .WithSquad(squad)
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Flank,
                    Formation = FormationType.Orb,
                    Stance = StanceType.Aggressive,
                })
                .Run(32);

            Assert.True(hist.Count >= 10);
            var late = hist.Skip(hist.Count / 2).ToList();
            foreach (var tick in late)
            {
                Assert.True(tick.HasSticky);
                Assert.Equal(101L, tick.StickyId);
                Assert.True(tick.MeanDesiredToSticky < 20f,
                    $"orbit mean desired-to-sticky {tick.MeanDesiredToSticky:F1}m — PROOF claim 2 band");
            }
        }

        /// <summary>PROOF claim 5/2: sub-second hysteresis spike must not steal sticky.</summary>
        [Fact]
        public void Observable_sticky_ignores_subsecond_hysteresis_spike()
        {
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            PluginConfig.AmbushStickySwitchDwellSeconds.Value = 1.0f;
            var state = new SquadRuntimeState();
            var centroid = Vector3.zero;

            OrderApplicator.UpdateAmbushStickyAnchor(state, centroid, new List<(long, Vector3)>
            {
                (101, new Vector3(20f, 0f, 0f)),
                (202, new Vector3(25f, 0f, 0f)),
            }, 0.25f);
            Assert.Equal(101L, state.StickyPlayerId);

            OrderApplicator.UpdateAmbushStickyAnchor(state, centroid, new List<(long, Vector3)>
            {
                (101, new Vector3(20f, 0f, 0f)),
                (202, new Vector3(5f, 0f, 0f)),
            }, 0.25f);

            OrderApplicator.UpdateAmbushStickyAnchor(state, centroid, new List<(long, Vector3)>
            {
                (101, new Vector3(20f, 0f, 0f)),
                (202, new Vector3(22f, 0f, 0f)),
            }, 0.25f);

            Assert.Equal(101L, state.StickyPlayerId);
        }

        /// <summary>PROOF claim 3: far straggler PreferRun magnet into lattice while player kites.</summary>
        [Fact]
        public void Observable_straggler_runs_PreferRun_into_lattice_when_player_kites()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            squad.Members[3].Position = new Vector3(45f, 0f, 0f);
            var farId = squad.Members[3].InstanceId;

            var player = SimPlayer.Parametric(7, t => new Vector3(5f + t * 4f, 0f, 0f));
            var hist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(8f, 3.5f)
                .WithMemberStepping(true)
                .WithPlayers(player)
                .WithSquad(squad, new SquadRuntimeState { RomanPhase = RomanPhase.PressContact })
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Advance,
                    Formation = FormationType.ShieldWall,
                    Stance = StanceType.Aggressive,
                })
                .Run(24);

            Assert.Equal(0, PlayerPathSim.TotalPreferRunViolations(hist));
            var first = hist[0].Members.First(m => m.InstanceId == farId);
            var last = hist[hist.Count - 1].Members.First(m => m.InstanceId == farId);
            Assert.True(first.PreferRun);
            Assert.True(last.DesiredPosition.x < 25f,
                $"straggler Desired still far x={last.DesiredPosition.x} — PROOF claim 3 magnet");
            Assert.True(last.Position.x < first.Position.x - 5f,
                $"straggler did not close under step sim — LIVE STEP: kite and watch FormUp");
        }

        /// <summary>PROOF claim 4: Roman Pin + Ambush Harass when co-engaged.</summary>
        [Fact]
        public void Observable_Roman_and_Ambush_theater_assigns_Pin_and_Harass_when_coengaged()
        {
            var roman = TheaterView("roman", "roman#proof", new Vector3(0f, 0f, 0f), focus: 42);
            var ambush = TheaterView("ambush", "ambush#proof", new Vector3(16f, 0f, 0f), focus: 42);
            TheaterCommander.Assign(new[] { roman, ambush }, 0.75f);

            Assert.Equal(TheaterRole.Pin, roman.State.TheaterRole);
            Assert.Equal(TheaterRole.Harass, ambush.State.TheaterRole);
            Assert.Equal(42, roman.State.TheaterFocusId);
        }

        /// <summary>PROOF claim 4: DeathRush stays Charge under theater while others take jobs.</summary>
        [Fact]
        public void Observable_DeathRush_stays_Charge_while_theater_assigns_others()
        {
            var rush = TheaterView("death-rush", "dr#proof", new Vector3(1f, 0f, 0f), focus: 9);
            var roman = TheaterView("roman", "rom#proof", new Vector3(10f, 0f, 0f), focus: 9);
            var ambush = TheaterView("ambush", "amb#proof", new Vector3(18f, 0f, 0f), focus: 9);

            TheaterCommander.Assign(new[] { rush, roman, ambush }, 0.5f);
            Assert.Equal(TheaterRole.None, rush.State.TheaterRole);
            Assert.Equal(TheaterRole.Pin, roman.State.TheaterRole);
            Assert.Equal(TheaterRole.Harass, ambush.State.TheaterRole);

            // Even if a theater job were wrongly stuck on Death-Rush, ShapeOrder must keep Charge.
            var shaped = TheaterCommander.ShapeOrder(
                new SquadSnapshot
                {
                    DoctrineId = "death-rush",
                    TheaterRole = TheaterRole.Pin,
                    ThreatCount = 1,
                    NearestThreatDistance = 8f,
                },
                DoctrineOrderKind.Kite);

            Assert.Equal(DoctrineOrderKind.Charge, shaped);
        }

        private static TheaterSquadView TheaterView(string doctrineId, string stableId, Vector3 centroid, long focus)
        {
            return new TheaterSquadView
            {
                DoctrineId = doctrineId,
                StableId = stableId,
                Centroid = centroid,
                HasFocus = true,
                FocusPlayerId = focus,
                FocusPosition = Vector3.zero,
                State = new SquadRuntimeState
                {
                    StableId = stableId,
                    DoctrineId = doctrineId,
                },
            };
        }
    }
}
