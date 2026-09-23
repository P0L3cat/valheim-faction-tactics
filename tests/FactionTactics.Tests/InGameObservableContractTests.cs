using System;
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
    /// PRIMARY wins are Azog geometric predicates over PlayerPathSim trajectories
    /// (world + sim character). Flag/enum/PreferRun asserts are secondary support only.
    /// Offline cannot prove Valheim pixels — see Proof §1–2.
    /// </summary>
    public class InGameObservableContractTests
    {
        public InGameObservableContractTests() => TestConfig.EnsureBound();

        /// <summary>PROOF claim 1: PreferRun == !HoldGround on Advance (multi-tick); planted Hold Fronts/Leaders plant.</summary>
        [Fact]
        public void Observable_PreferRun_equals_not_HoldGround_Advance_runs_Hold_plants()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 5, "Skeleton");
            // Tight spawn pile — Advance must PreferRun Desired out of the blob.
            for (int i = 0; i < squad.Members.Count; i++)
                squad.Members[i].Position = new Vector3(0.2f * i, 0f, 0.1f * i);

            var spawnCentroid = PlayerPathSim.ComputeCentroid(squad);
            var player = SimPlayer.Parametric(7, t => new Vector3(12f + t * 2f, 0f, 0f));
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
                .Run(16);

            Assert.True(hist.Count >= 8);

            // PRIMARY (geom): stepped Positions leave spawn blob; Desired leaves spawn.
            var late = hist[hist.Count - 1];
            var meanPosFromSpawn = late.Members.Average(m =>
                MotionPredicates.Dist(m.Position, spawnCentroid));
            var meanDesiredFromSpawn = late.Members.Average(m =>
                MotionPredicates.Dist(m.DesiredPosition, spawnCentroid));
            Assert.True(meanPosFromSpawn > 1.5f,
                $"PRIMARY: Advance Positions still freeze-in-blob meanDist={meanPosFromSpawn:F2}");
            Assert.True(meanDesiredFromSpawn > 2f,
                $"PRIMARY: Advance Desired still piled on spawn meanDist={meanDesiredFromSpawn:F2}");

            // Secondary: PreferRun mirrors / Advance never plants.
            Assert.Equal(0, PlayerPathSim.TotalPreferRunViolations(hist));
            foreach (var tick in hist)
            {
                foreach (var m in tick.Members)
                {
                    Assert.True(m.HasIntent);
                    Assert.Equal(!m.HoldGround, m.PreferRun);
                    Assert.True(m.PreferRun, "Advance must PreferRun while pressing");
                    Assert.False(m.HoldGround);
                }
            }

            // Hold plant: Front/Leader near slot plant; PreferRun false only then.
            var holdSquad = FakeSnapshots.MakeSquad(roman, 5, "Skeleton");
            var runtime = new SquadRuntimeState { RomanPhase = RomanPhase.StandoffHold };
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(holdSquad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            }, runtime);

            var anyPlant = false;
            foreach (var m in holdSquad.Members)
            {
                Assert.True(OrderApplicator.Intents.TryGetValue(m.InstanceId, out var intent));
                Assert.Equal(!intent.HoldGround, intent.PreferRun); // secondary
                if (intent.HoldGround)
                {
                    anyPlant = true;
                    Assert.False(intent.PreferRun);
                    // PRIMARY: planted ⇒ Desired dwells at body (not flee Destination).
                    var plantDist = MotionPredicates.Dist(m.Position, intent.DesiredPosition);
                    Assert.True(plantDist < 3.5f,
                        $"PRIMARY: Hold plant Desired far from Position dist={plantDist:F2} id={m.InstanceId}");
                    Assert.True(
                        m.AssignedRole == SquadRole.Front || m.AssignedRole == SquadRole.Leader,
                        $"HoldGround plant must be Front/Leader, got {m.AssignedRole} for {m.InstanceId}");
                }
            }
            Assert.True(anyPlant,
                "LIVE STEP if this fails: StandoffHold should plant at least one Front/Leader — see PROOF claim 1");
        }

        /// <summary>PROOF claim 2: Ambush slots track walking sticky; angular spread around sticky (not identical offset).</summary>
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
                squad.Members[i].Position = new Vector3(18f + i * 0.3f, 0f, 0.2f * i);

            var player = SimPlayer.Waypoints(101, new[]
            {
                new Vector3(8f, 0f, 0f),
                new Vector3(20f, 0f, 0f),
                new Vector3(20f, 0f, 12f),
                new Vector3(8f, 0f, 12f),
                new Vector3(8f, 0f, 0f),
            }, 3.5f);

            var hist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithMemberStepping(true)
                .WithSpeeds(7f, 3.5f)
                .WithPlayers(player)
                .WithSquad(squad)
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Flank,
                    Formation = FormationType.Orb,
                    Stance = StanceType.Aggressive,
                })
                .Run(80);

            Assert.True(hist.Count >= 16);

            // PRIMARY: Azog Ambush sticky orbit (Position band + angular + path length + flaps).
            MotionPredicates.Require(
                MotionPredicates.AmbushStickyOrbit(
                    hist,
                    rLo: 8f,
                    rHi: 14f,
                    bandFraction: 0.80f,
                    flapsMax: 0,
                    phiMin: MotionPredicates.PiOverTwo,
                    angVarAndMin: null,
                    playerPathMin: 6f,
                    minTicks: 16,
                    warmup: 6),
                "PROOF-claim-2-orbit");

            // Secondary: Desired band + angular spread (legacy R3).
            var late = hist.Skip(hist.Count / 2).ToList();
            foreach (var tick in late)
            {
                Assert.True(tick.HasSticky);
                Assert.Equal(101L, tick.StickyId);
                Assert.True(tick.MeanDesiredToSticky < 20f,
                    $"orbit mean desired-to-sticky {tick.MeanDesiredToSticky:F1}m — supporting");
            }
        }

        /// <summary>
        /// PROOF claim 5/2: hysteresis+ closer for 3 ticks @ dt=0.25 (<1s dwell) → zero flaps;
        /// sustained closer ≥ AmbushStickySwitchDwellSeconds → exactly one switch.
        /// </summary>
        [Fact]
        public void Observable_sticky_ignores_subsecond_hysteresis_spike()
        {
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            PluginConfig.AmbushStickySwitchDwellSeconds.Value = 1.0f;

            // Seed sticky on A alone.
            var seed = new SquadRuntimeState();
            OrderApplicator.UpdateAmbushStickyAnchor(seed, Vector3.zero, new List<(long, Vector3)>
            {
                (101, new Vector3(20f, 0f, 0f)),
            }, 0.25f);
            Assert.Equal(101L, seed.StickyPlayerId);

            // Phase 1: B hysteresis+ closer for exactly 3 ticks @ 0.25 = 0.75s < 1.0 dwell → stay.
            var a = SimPlayer.Parametric(101, _ => new Vector3(20f, 0f, 0f));
            var bCloser = SimPlayer.Parametric(202, _ => new Vector3(5f, 0f, 0f)); // 15m closer
            var sim = new PlayerPathSim()
                .WithDt(0.25f)
                .WithPlayers(a, bCloser);
            sim.Runtime.HasStickyPlayer = true;
            sim.Runtime.StickyPlayerId = 101;
            sim.Runtime.StickyPlayerPosition = new Vector3(20f, 0f, 0f);

            var brush = sim.RunStickyOnly(3, fixedCentroid: Vector3.zero);
            // PRIMARY: orbit anchor world pos stays on A during sub-second spike (not B).
            Assert.True(brush.All(t => MotionPredicates.Dist(t.StickyPosition, new Vector3(20f, 0f, 0f)) < 1f),
                "PRIMARY: StickyPosition must stay at A during brush (explicit dt=0.25)");
            Assert.Equal(0, PlayerPathSim.CountStickyFlaps(brush)); // secondary id
            Assert.True(brush.All(t => t.StickyId == 101L),
                "3 ticks @ dt=0.25 with hysteresis+ closer must not flip sticky (0.75s < 1.0 dwell)");

            // Phase 2: keep B closer for 4+ more ticks (≥1.0s continuous from phase1 start, or fresh dwell).
            // Candidate already armed from brush — continue same sim runtime.
            var sustained = sim.RunStickyOnly(5, fixedCentroid: Vector3.zero);
            var combined = brush.Concat(sustained).ToList();
            var flaps = PlayerPathSim.CountStickyFlaps(combined);
            Assert.Equal(1, flaps);
            Assert.Equal(202L, combined[combined.Count - 1].StickyId);
            Assert.True(
                MotionPredicates.Dist(combined[combined.Count - 1].StickyPosition, new Vector3(5f, 0f, 0f)) < 1f,
                "PRIMARY: after dwell, StickyPosition must relocate to B");
            var firstB = combined.FindIndex(t => t.StickyId == 202L);
            Assert.True(firstB >= 3,
                $"switch must not occur during first 3 brush ticks (firstB={firstB})");
            Assert.True(combined.Skip(firstB).All(t => t.StickyId == 202L),
                "sticky must stay on B after the single sustained switch");
        }

        /// <summary>PROOF claim 3: far straggler PreferRun magnet into lattice (Desired near centroid/slot).</summary>
        [Fact]
        public void Observable_straggler_runs_PreferRun_into_lattice_when_player_kites()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            for (int i = 0; i < 3; i++)
                squad.Members[i].Position = new Vector3(i * 1.4f, 0f, 0f);
            squad.Members[3].Position = new Vector3(36f, 0f, 0f);
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
                .Run(40);

            // PRIMARY: Azog straggler merge (Position closes; reject frozen body + soft Desired).
            MotionPredicates.Require(
                MotionPredicates.StragglerMerge(
                    hist, farId, rOut: 18f, rIn: 7f, rPack: 10f, lastK: 4, minTicks: 10),
                "PROOF-claim-3-straggler");

            // Secondary: PreferRun / Desired near centroid.
            Assert.Equal(0, PlayerPathSim.TotalPreferRunViolations(hist));
            var first = hist[0].Members.First(m => m.InstanceId == farId);
            var last = hist[hist.Count - 1].Members.First(m => m.InstanceId == farId);
            Assert.True(first.PreferRun);
            Assert.False(first.HoldGround);
            var lateCentroid = hist[hist.Count - 1].PackCentroid;
            var desiredToCentroid = MotionPredicates.Dist(last.DesiredPosition, lateCentroid);
            Assert.True(desiredToCentroid < 15f,
                $"supporting: straggler Desired far from centroid dist={desiredToCentroid:F1}");
        }

        /// <summary>
        /// PROOF claim 1 (Ambush): Flank/Kite living members PreferRun and never HoldGround-plant while moving.
        /// </summary>
        [Fact]
        public void Observable_Ambush_Flank_and_Kite_PreferRun_no_HoldGround_plant_while_moving()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            PluginConfig.AmbushStickySwitchDwellSeconds.Value = 1.0f;

            var registry = DoctrinePackRegistry.CreateDefault();
            var ambush = registry.GetById("ambush")!;

            foreach (var orderKind in new[] { DoctrineOrderKind.Flank, DoctrineOrderKind.Kite })
            {
                var squad = FakeSnapshots.MakeSquad(ambush, 5, "Greydwarf");
                for (int i = 0; i < squad.Members.Count; i++)
                    squad.Members[i].Position = new Vector3(16f + i * 0.3f, 0f, 0.2f * i);

                var player = SimPlayer.Waypoints(101, new[]
                {
                    new Vector3(6f, 0f, 0f),
                    new Vector3(18f, 0f, 0f),
                    new Vector3(18f, 0f, 12f),
                    new Vector3(6f, 0f, 12f),
                    new Vector3(6f, 0f, 0f),
                }, 3.5f);

                var hist = new PlayerPathSim()
                    .WithDt(0.25f)
                    .WithMemberStepping(true)
                    .WithSpeeds(7f, 3.5f)
                    .WithPlayers(player)
                    .WithSquad(squad)
                    .WithOrder(new SquadOrder
                    {
                        OrderKind = orderKind,
                        Formation = FormationType.Orb,
                        Stance = StanceType.Aggressive,
                    })
                    .Run(80);

                // PRIMARY: Position orbit band vs sticky while player walks (not PreferRun-only).
                MotionPredicates.Require(
                    MotionPredicates.AmbushStickyOrbit(
                        hist,
                        rLo: 8f,
                        rHi: 14f,
                        bandFraction: 0.80f,
                        flapsMax: 0,
                        phiMin: MotionPredicates.PiOverTwo,
                        angVarAndMin: null,
                        playerPathMin: 5f,
                        minTicks: 16,
                        warmup: 4),
                    $"PROOF-ambush-{orderKind}-orbit");

                // Secondary: PreferRun / no HoldGround plant.
                Assert.Equal(0, PlayerPathSim.TotalPreferRunViolations(hist));
                var late = hist.Skip(hist.Count / 3).ToList();
                foreach (var tick in late)
                {
                    foreach (var m in tick.Members.Where(x => x.HasIntent))
                    {
                        Assert.Equal(!m.HoldGround, m.PreferRun);
                        Assert.True(m.PreferRun,
                            $"{orderKind}: living member must PreferRun while Ambush moving");
                        Assert.False(m.HoldGround,
                            $"{orderKind}: living member must not HoldGround-plant while Ambush moving");
                    }
                }
            }
        }

        /// <summary>
        /// PROOF claim 4 (Assign support). PRIMARY geometry lives in
        /// MotionDoctrineTests.Geom_Theater_Pin_vs_Flank_lateral_halfplane_over_path.
        /// </summary>
        [Fact]
        public void Observable_Roman_and_Ambush_theater_assigns_Pin_and_Harass_when_coengaged()
        {
            // Multi-tick Assign (not one-shot): roles must stabilize across N steps.
            var roman = TheaterView("roman", "roman#proof", new Vector3(0f, 0f, 0f), focus: 42);
            var ambush = TheaterView("ambush", "ambush#proof", new Vector3(16f, 0f, 0f), focus: 42);
            for (int i = 0; i < 8; i++)
                TheaterCommander.Assign(new[] { roman, ambush }, 0.25f);

            Assert.Equal(TheaterRole.Pin, roman.State.TheaterRole);       // secondary
            Assert.Equal(TheaterRole.Harass, ambush.State.TheaterRole);  // secondary
            Assert.Equal(42, roman.State.TheaterFocusId);
            Assert.True(roman.State.TheaterRoleAgeSeconds > 0f,
                "PRIMARY-ish: Pin role must accumulate age across Assign(dt)×N (not one-shot)");
        }

        /// <summary>
        /// PROOF claim 4 DeathRush: ShapeOrder Charge is supporting;
        /// PRIMARY Charge geometry is Geom_Charge_with_threat_inject_closes_not_FormUp.
        /// </summary>
        [Fact]
        public void Observable_DeathRush_stays_Charge_while_theater_assigns_others()
        {
            var rush = TheaterView("death-rush", "dr#proof", new Vector3(1f, 0f, 0f), focus: 9);
            var roman = TheaterView("roman", "rom#proof", new Vector3(10f, 0f, 0f), focus: 9);
            var ambush = TheaterView("ambush", "amb#proof", new Vector3(18f, 0f, 0f), focus: 9);

            for (int i = 0; i < 8; i++)
                TheaterCommander.Assign(new[] { rush, roman, ambush }, 0.25f);
            Assert.Equal(TheaterRole.None, rush.State.TheaterRole);
            Assert.Equal(TheaterRole.Pin, roman.State.TheaterRole);
            Assert.Equal(TheaterRole.Harass, ambush.State.TheaterRole);
            Assert.True(roman.State.TheaterRoleAgeSeconds > 0f);

            var shaped = TheaterCommander.ShapeOrder(
                new SquadSnapshot
                {
                    DoctrineId = "death-rush",
                    TheaterRole = TheaterRole.Pin,
                    ThreatCount = 1,
                    NearestThreatDistance = 8f,
                },
                DoctrineOrderKind.Kite);

            Assert.Equal(DoctrineOrderKind.Charge, shaped); // secondary — geom in MotionDoctrineTests
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
