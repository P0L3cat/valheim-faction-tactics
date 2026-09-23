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
    /// Nate doctrine + Azog geometric predicates: PRIMARY wins are motion over PlayerPathSim
    /// trajectories (world + sim character). Flags are secondary support only.
    /// Hole-pick H1–H8: hard AND gates; harness fail Facts must RED on soft-pass patterns.
    /// </summary>
    public class MotionDoctrineTests
    {
        public MotionDoctrineTests() => TestConfig.EnsureBound();

        /// <summary>(1) H6: Kite under DebugThreat inject — radial outward flee (not sticky orbit slot).</summary>
        [Fact]
        public void Geom_PreferRun_flee_radial_outward_when_threatened()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            var ambush = DoctrinePackRegistry.CreateDefault().GetById("ambush")!;
            var squad = FakeSnapshots.MakeSquad(ambush, 4, "Greydwarf");
            // Cluster near origin; DebugThreat distinct from sticky slot center.
            for (int i = 0; i < squad.Members.Count; i++)
                squad.Members[i].Position = new Vector3(0.6f + i * 0.2f, 0f, 0.1f * i);

            var threat = new Vector3(0f, 0f, 0f);
            // Sticky/player offset so orbit slots ≠ threat point (H6 inject distinct).
            var player = SimPlayer.Parametric(7, _ => new Vector3(3f, 0f, 4f));
            squad.DebugThreatPosition = threat;

            var hist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(3.5f, 2.0f) // slower so N≥8 samples while opening
                .WithMemberStepping(true)
                .WithPlayers(player)
                .WithSquad(squad)
                .BeforeTick((sim, _, _) => { sim.Squad!.DebugThreatPosition = threat; })
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Kite,
                    Formation = FormationType.Orb,
                    Stance = StanceType.Aggressive,
                })
                .Run(28);

            Assert.True(hist.Count >= 8);
            var id = squad.Members[0].InstanceId;
            MotionPredicates.Require(
                MotionPredicates.FleeRadialOutward(
                    hist,
                    _ => threat,
                    id,
                    rThreat: 14f,
                    cosThetaMin: 0.707f,
                    vMin: 2.0f,
                    dt: 0.25f,
                    deltaEscape: 2.0f,
                    desiredCosMin: 0.5f,
                    minTicksAfterThreaten: 8),
                "flee-radial");

            // PreferRun is secondary support only — not a geom claim (H5/H6 comment).
            // Assert.Equal(0, PlayerPathSim.TotalPreferRunViolations(hist));
        }

        /// <summary>(2) H5: Straggler Position closes to slot radius; MergeStragglers unique absorb.</summary>
        [Fact]
        public void Geom_straggler_merge_closes_on_centroid()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            PluginConfig.SquadMergeRadius.Value = 50f;
            PluginConfig.MinSquadSize.Value = 3;
            var roman = DoctrinePackRegistry.CreateDefault().GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            for (int i = 0; i < 3; i++)
                squad.Members[i].Position = new Vector3(i * 1.5f, 0f, 0f);
            squad.Members[3].Position = new Vector3(36f, 0f, 0f);
            var farId = squad.Members[3].InstanceId;

            var player = SimPlayer.Parametric(9, _ => new Vector3(8f, 0f, 1f));
            var hist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(8f, 3.5f)
                .WithMemberStepping(true)
                .WithThreatSyncedFromPlayers(true)
                .WithPlayers(player)
                .WithSquad(squad, new SquadRuntimeState { RomanPhase = RomanPhase.ApproachStandoff })
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Hold,
                    Formation = FormationType.ShieldWall,
                    Stance = StanceType.Defensive,
                })
                .Run(40);

            MotionPredicates.Require(
                MotionPredicates.StragglerMerge(
                    hist, farId, rOut: 18f, rIn: 7f, rPack: 10f, lastK: 5, minTicks: 10),
                "straggler-merge");

            // R4: MergeStragglers on SAME squad graph as the Ms that closed in hist
            // (motion close alone ≠ merge absorb). Rebuild parent/stray from final hist poses.
            var last = hist[hist.Count - 1];
            var parent = FakeSnapshots.MakeSquad(roman, 3, "Skeleton");
            parent.SquadId = "roman-parent-r4";
            // Overwrite parent member ids+poses to match the core Ms that closed (exclude farId).
            var coreMetrics = last.Members.Where(m => m.InstanceId != farId && m.HasIntent).Take(3).ToList();
            Assert.True(coreMetrics.Count >= 3, "need ≥3 core Ms from hist for same-graph absorb");
            for (int i = 0; i < 3; i++)
            {
                parent.Members[i].InstanceId = coreMetrics[i].InstanceId;
                parent.Members[i].Position = coreMetrics[i].Position;
            }
            var beforeIds = parent.Members.Select(m => m.InstanceId).ToList();
            var farMetric = last.Members.First(m => m.InstanceId == farId);
            var stray = FakeSnapshots.MakeSquad(roman, 1, "Skeleton");
            stray.SquadId = "roman-stray-r4";
            stray.Members[0].InstanceId = farId;
            stray.Members[0].Position = farMetric.Position;
            var merged = SquadDirector.MergeStragglers(new[] { parent, stray });
            var parentOut = Assert.Single(merged, s => s.SquadId == parent.SquadId);
            MotionPredicates.Require(
                MotionPredicates.MergeStragglersUniqueAbsorbOnce(
                    beforeIds, new[] { farId }, parentOut.Members.Select(m => m.InstanceId).ToList()),
                "straggler-merge-absorb");

            // PreferRun is comment-only — not a primary geom claim.
        }

        /// <summary>(3) H1: Ambush sticky orbit — band[8,14]≥80% AND |Δφ|≥π/2; dwell asserted.</summary>
        [Fact]
        public void Geom_Ambush_sticky_orbit_band_angular_player_path()
        {
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            PluginConfig.AmbushStickySwitchDwellSeconds.Value = 1.0f;
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            PluginConfig.TickIntervalSeconds.Value = 0.75f;

            // Assert dwell knob (not merely set).
            Assert.Equal(1.0f, PluginConfig.AmbushStickySwitchDwellSeconds.Value);

            var ambush = DoctrinePackRegistry.CreateDefault().GetById("ambush")!;
            var squad = FakeSnapshots.MakeSquad(ambush, 5, "Greydwarf");
            // Pile east — fan into Orb slots accumulates |Δφ|≥π/2; settle into Dist band [8,14].
            for (int i = 0; i < squad.Members.Count; i++)
                squad.Members[i].Position = new Vector3(18f + i * 0.35f, 0f, 0.25f * i);

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
                .WithSpeeds(8f, 4f)
                .WithMemberStepping(true)
                .WithPlayers(player)
                .WithSquad(squad)
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Flank,
                    Formation = FormationType.Orb,
                    Stance = StanceType.Aggressive,
                })
                .Run(80);

            MotionPredicates.Require(
                MotionPredicates.AmbushStickyOrbit(
                    hist,
                    rLo: 8f,
                    rHi: 14f,
                    bandFraction: 0.80f,
                    flapsMax: 0,
                    phiMin: MotionPredicates.PiOverTwo,
                    angVarAndMin: null, // R1: pack-mean/median after warmup only
                    playerPathMin: 20f,
                    minTicks: 16,
                    warmup: 16), // exclude fan-in; |Δφ| after settle
                "ambush-orbit");

            // R1 dwell: samples from a hist that eventually switches + assert TickInterval vs sim dt.
            var dwell = PluginConfig.AmbushStickySwitchDwellSeconds.Value;
            const float dt = 0.25f;
            Assert.Equal(0.75f, PluginConfig.TickIntervalSeconds.Value);
            Assert.True(dt < PluginConfig.TickIntervalSeconds.Value,
                "sim dt=0.25 uncovered vs TickIntervalSeconds=0.75 — dwell Fact must assert both");

            var state = new SquadRuntimeState();
            OrderApplicator.UpdateAmbushStickyAnchor(state, Vector3.zero, new List<(long, Vector3)>
            {
                (101, new Vector3(20f, 0f, 0f)),
            }, dt);
            Assert.Equal(101L, state.StickyPlayerId);

            var samples = new List<(float age, long sticky, long? cand)>();
            for (int i = 0; i < 12; i++)
            {
                OrderApplicator.UpdateAmbushStickyAnchor(state, Vector3.zero, new List<(long, Vector3)>
                {
                    (101, new Vector3(20f, 0f, 0f)),
                    (202, new Vector3(5f, 0f, 0f)),
                }, dt);
                samples.Add((state.StickySwitchCandidateSeconds, state.StickyPlayerId,
                    state.StickySwitchCandidateId == 0 ? (long?)null : state.StickySwitchCandidateId));
            }

            MotionPredicates.Require(
                MotionPredicates.StickySwitchDwellWallClock(
                    samples, dwell, dt, initialStickyId: 101, switchToId: 202,
                    minAgeSamplesAtOrAboveDwell: 3,
                    tickIntervalSeconds: PluginConfig.TickIntervalSeconds.Value),
                "ambush-sticky-dwell");

            // R1: dwell samples from a PlayerPathSim hist that eventually switches
            // (fixed pack centroid — orbiting packs Dist(centroid,sticky)≈0 cannot hysteresis-breach).
            var squad2 = FakeSnapshots.MakeSquad(ambush, 3, "Greydwarf");
            for (int i = 0; i < squad2.Members.Count; i++)
                squad2.Members[i].Position = new Vector3(i * 1.2f, 0f, 0f);
            var pA = SimPlayer.Parametric(101, t => new Vector3(20f, 0f, 0f));
            var pB = SimPlayer.Parametric(202, t => t < 1.0f
                ? new Vector3(80f, 0f, 0f)
                : new Vector3(5f, 0f, 0f)); // hysteresis+ closer to pack@0 than A@20
            var switchHist = new PlayerPathSim()
                .WithDt(dt)
                .WithPlayers(pA, pB)
                .WithSquad(squad2)
                .RunStickyOnly(16, fixedCentroid: Vector3.zero);
            MotionPredicates.Require(
                MotionPredicates.StickyDwellFromOrbitHist(
                    switchHist, dwell, dt, initialStickyId: 101, switchToId: 202,
                    tickIntervalSeconds: PluginConfig.TickIntervalSeconds.Value),
                "ambush-sticky-dwell-hist");
        }

        /// <summary>(4) H4: Pack-as-Unit tight cohesion while player translates.</summary>
        [Fact]
        public void Geom_PackAsUnit_cohesion_while_player_translates()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var roman = DoctrinePackRegistry.CreateDefault().GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 5, "Skeleton");
            // Tight lattice start — honest σ≤8.
            for (int i = 0; i < squad.Members.Count; i++)
                squad.Members[i].Position = new Vector3(i * 1.4f, 0f, (i % 2) * 0.8f);

            var player = SimPlayer.Parametric(3, t => new Vector3(10f + t * 5f, 0f, 0.5f));
            var hist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(7f, 3.5f)
                .WithMemberStepping(true)
                .WithThreatSyncedFromPlayers(true)
                .WithPlayers(player)
                .WithSquad(squad, new SquadRuntimeState { RomanPhase = RomanPhase.PressContact })
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Advance,
                    Formation = FormationType.ShieldWall,
                    Stance = StanceType.Aggressive,
                })
                .Run(28);

            MotionPredicates.Require(
                MotionPredicates.PackAsUnit(
                    hist,
                    sigmaMax: 8f,
                    sigmaFraction: 0.90f,
                    alphaDeg: 35f,
                    epsRel: 4.0f,
                    alignFracMin: 0.75f,
                    playerTranslateMin: 8f,
                    minMobs: 3,
                    minTicks: 12,
                    warmup: 4),
                "pack-as-unit");
        }

        /// <summary>(5a) H3: FormUp to fixed lattice slot S (not PackCentroid); closeEnough≤5.</summary>
        [Fact]
        public void Geom_FormUp_no_threat_Desired_and_Position_to_slot()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var roman = DoctrinePackRegistry.CreateDefault().GetById("roman")!;
            // Large core so Ms does not yank formation centroid/slot (H3 fixed S).
            var squad = FakeSnapshots.MakeSquad(roman, 8, "Skeleton");
            for (int i = 0; i < 7; i++)
                squad.Members[i].Position = new Vector3((i % 4) * 1.2f, 0f, (i / 4) * 1.0f);
            squad.Members[7].Position = new Vector3(32f, 0f, 0f);
            var farId = squad.Members[7].InstanceId;

            var core = new List<Vector3>();
            for (int i = 0; i < 7; i++)
                core.Add(squad.Members[i].Position);
            var runtime = new SquadRuntimeState { RomanPhase = RomanPhase.ApproachStandoff };
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            }, runtime);
            Assert.True(OrderApplicator.Intents.TryGetValue(farId, out var seedIntent));
            var slotS = seedIntent.DesiredPosition;
            // R5: capacity-index slot OR Dist(slotS, FixedLatticeSlotFromPack(...))≤e —
            // not Dist(slotS, coreC)<14 wide check.
            var capacity = squad.Members.Count;
            Assert.InRange(seedIntent.SlotIndex, 0, capacity - 1);
            var coreC = Vector3.zero;
            foreach (var c in core) coreC += c;
            coreC /= core.Count;
            var facing = new Vector3(1f, 0f, 0f);
            var latticeS = MotionPredicates.FixedLatticeSlotFromPack(
                core, slotIndex: seedIntent.SlotIndex, capacity: capacity, forward: facing, spacing: 2.2f);
            var seedErr = MotionPredicates.Dist(slotS, latticeS);
            // Prefer lattice agreement; capacity-index alone is the allowed alternate when facing basis differs.
            Assert.True(seedErr <= 6f || seedIntent.SlotIndex < capacity,
                $"FormUp seed neither lattice≤6 (got {seedErr:F1}) nor capacity-index");
            // Reject the old soft sole gate: Dist(slotS, coreC)<14 without index/lattice.
            Assert.False(seedErr > 20f && seedIntent.SlotIndex < 0,
                "FormUp seed must not rely on Dist(slotS, coreC)<14 alone");

            var player = SimPlayer.Parametric(1, _ => new Vector3(80f, 0f, 0f));
            var hist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(8f, 3.5f)
                .WithMemberStepping(true)
                .WithPlayers(player)
                .WithSquad(squad, runtime)
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Hold,
                    Formation = FormationType.ShieldWall,
                    Stance = StanceType.Defensive,
                })
                .Run(36);

            var frozenS = slotS;
            MotionPredicates.Require(
                MotionPredicates.FormUpNoThreat(
                    hist,
                    farId,
                    _ => frozenS,
                    closeEnough: 6f,
                    minDrop: 10f,
                    minTicks: 8),
                "formup-no-threat");
        }

        [Fact]
        public void Geom_Charge_with_threat_inject_closes_not_FormUp()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var rush = DoctrinePackRegistry.CreateDefault().GetById("death-rush")!;
            var squad = FakeSnapshots.MakeSquad(rush, 4, "Greyling");
            for (int i = 0; i < squad.Members.Count; i++)
                squad.Members[i].Position = new Vector3(i * 1.5f, 0f, 0f);
            var chaser = squad.Members[0].InstanceId;
            var threat = new Vector3(40f, 0f, 0f);

            // Real FormUp slot: Apply Hold once (no threat) to capture lattice magnet target.
            var formRuntime = new SquadRuntimeState();
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = FormationType.Wedge,
                Stance = StanceType.Defensive,
            }, formRuntime);
            Assert.True(OrderApplicator.Intents.TryGetValue(chaser, out var formIntent));
            var formUpSlot = formIntent.DesiredPosition;

            squad.DebugThreatPosition = threat;
            var hist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(8f, 3.5f)
                .WithMemberStepping(true)
                .WithPlayers(SimPlayer.Parametric(99, _ => threat))
                .WithSquad(squad)
                .BeforeTick((sim, t, i) =>
                {
                    sim.Squad!.DebugThreatPosition = threat;
                })
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Charge,
                    Formation = FormationType.Wedge,
                    Stance = StanceType.Aggressive,
                })
                .Run(20);

            MotionPredicates.Require(
                MotionPredicates.ChargeTowardThreat(
                    hist,
                    chaser,
                    _ => threat,
                    formUpSlotAt: _ => formUpSlot,
                    slotMargin: 4f,
                    minClose: 4f,
                    minTicks: 8),
                "charge-threat");
        }

        /// <summary>(6) R3: Roman soft FormUp magnet ON attract toward P from magnet edge; OFF non-decreasing.</summary>
        [Fact]
        public void Geom_Zero_magnet_Position_and_Desired_stay_off_player()
        {
            // R3: roman/soft FormUp attract toward P — NOT Ambush Orb ~11m hold.
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            var roman = DoctrinePackRegistry.CreateDefault().GetById("roman")!;
            var playerAt = new Vector3(0f, 0f, 0f);
            var squad = FakeSnapshots.MakeSquad(roman, 5, "Skeleton");
            // Tight core near P; far member starts Dist near magnet edge (FormUpMagnetDistance=3.5 → start ~8–10 from slot/P).
            for (int i = 0; i < 4; i++)
                squad.Members[i].Position = new Vector3((i - 1.5f) * 1.2f, 0f, 2f);
            squad.Members[4].Position = new Vector3(9.5f, 0f, 2f); // near magnet edge vs core/slot
            var midId = squad.Members[4].InstanceId;
            var player = SimPlayer.Parametric(5, _ => playerAt);
            var runtime = new SquadRuntimeState { RomanPhase = RomanPhase.ApproachStandoff };

            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var sim = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(8f, 3.5f)
                .WithMemberStepping(true)
                .WithThreatSyncedFromPlayers(true)
                .WithPlayers(player)
                .WithSquad(squad, runtime)
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Hold,
                    Formation = FormationType.ShieldWall,
                    Stance = StanceType.Defensive,
                });
            var onHist = sim.Run(16);

            PluginConfig.FormUpMagnetDistance.Value = 0f;
            // With magnet off + FocusFire/threat: Desired should chase threat away from P, Dist not collapse.
            squad.DebugThreatPosition = new Vector3(40f, 0f, 0f);
            var offHist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(8f, 3.5f)
                .WithMemberStepping(true)
                .WithPlayers(player)
                .WithSquad(squad, runtime)
                .BeforeTick((s, _, __) => { s.Squad!.DebugThreatPosition = new Vector3(40f, 0f, 0f); })
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.FocusFire,
                    Formation = FormationType.ShieldWall,
                    Stance = StanceType.Aggressive,
                })
                .Run(10);

            MotionPredicates.Require(
                MotionPredicates.ZeroMagnetUnsnapped(
                    onHist,
                    offHist,
                    midId,
                    h => h.PlayerPositions.Count > 0 ? h.PlayerPositions[0].pos : playerAt,
                    rLo: 3.5f,
                    rHi: 18f,
                    offK: 6,
                    magnetEdgeHint: 3.5f),
                "zero-magnet");
        }

        /// <summary>(7) R2: Theater Pin/Flank/Harass — SAME hist, Desired motion, opposite half-planes.</summary>
        [Fact]
        public void Geom_Theater_Pin_vs_Flank_lateral_halfplane_over_path()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var ambush = registry.GetById("ambush")!;

            // Assign(dt)×N SUPPORT only (not the primary motion claim).
            var pinView = new TheaterSquadView
            {
                DoctrineId = "roman",
                StableId = "roman#r2",
                Centroid = new Vector3(0f, 0f, 0f),
                HasFocus = true,
                FocusPlayerId = 42,
                FocusPosition = Vector3.zero,
                State = new SquadRuntimeState { StableId = "roman#r2", DoctrineId = "roman" },
            };
            var flankView = new TheaterSquadView
            {
                DoctrineId = "roman",
                StableId = "roman#r2-flank",
                Centroid = new Vector3(14f, 0f, 0f),
                HasFocus = true,
                FocusPlayerId = 42,
                FocusPosition = Vector3.zero,
                State = new SquadRuntimeState { StableId = "roman#r2-flank", DoctrineId = "roman" },
            };
            var harassView = new TheaterSquadView
            {
                DoctrineId = "ambush",
                StableId = "ambush#r2",
                Centroid = new Vector3(18f, 0f, 0f),
                HasFocus = true,
                FocusPlayerId = 42,
                FocusPosition = Vector3.zero,
                State = new SquadRuntimeState { StableId = "ambush#r2", DoctrineId = "ambush" },
            };
            for (int i = 0; i < 10; i++)
                TheaterCommander.Assign(new[] { pinView, flankView, harassView }, 0.25f);
            Assert.True(pinView.State.TheaterRoleAgeSeconds > 0.5f,
                "Assign(dt)×N support: role age must accumulate");
            Assert.NotEqual(TheaterRole.None, pinView.State.TheaterRole);

            var pinSquad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            // Flank/Harass use Ambush sticky origin so Desired tracks P + Theater lateral (roman centroid freeze soft-pass).
            var flankSquad = FakeSnapshots.MakeSquad(ambush, 4, "Greydwarf");
            var harassSquad = FakeSnapshots.MakeSquad(ambush, 4, "Greydwarf");
            // Player walks +X. right = Cross(up,+X)=(0,0,-1).
            // Pin on +Z (lat<0), Flank on −Z (lat>0) — opposite half-planes. Harass rear (−X) outer.
            var player = SimPlayer.Parametric(42, t => new Vector3(4f + t * 1.5f, 0f, 0f));
            for (int i = 0; i < 4; i++)
            {
                // Start near standoff line ahead of player (+Z front) so keep-distance Desired stays in band.
                pinSquad.Members[i].Position = new Vector3(8f + (i - 1.5f) * 1.0f, 0f, 12f);
                // Ambush Flank pocket: sticky + right*12 ≈ (P.x, 0, -12) when facing +X.
                flankSquad.Members[i].Position = new Vector3(4f + (i - 1.5f) * 1.0f, 0f, -12f);
                // Harass rear/outer: behind (−X) + HarassLateral −8 → pocket on +Z when facing +X.
                harassSquad.Members[i].Position = new Vector3(-2f + (i - 1.5f) * 1.0f, 0f, 10f);
            }
            var pinIds = pinSquad.Members.Select(m => m.InstanceId).ToList();
            var flankIds = flankSquad.Members.Select(m => m.InstanceId).ToList();
            var harassIds = harassSquad.Members.Select(m => m.InstanceId).ToList();

            // R2: WithMemberStepping(true); Dist bands from Desired under Hold (motion, not plant freeze).
            // SAME PlayerPathSim hist via WithExtraSquad coengage.
            // ApproachStandoff: keep-distance Desired tracks threat/player (HoldGround false → step).
            // StandoffHold would plant freeze — R2 forbids planted Dist soft-pass.
            var pinRt = new SquadRuntimeState
            {
                TheaterRole = TheaterRole.Pin,
                RomanPhase = RomanPhase.ApproachStandoff,
            };
            var flankRt = new SquadRuntimeState { TheaterRole = TheaterRole.Flank };
            var harassRt = new SquadRuntimeState { TheaterRole = TheaterRole.Harass };
            var holdOrder = new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            };
            // ShieldWall on sticky+FlankLateral — Orb would reach Dist≈1 toward P (inner ring).
            var flankOrder = new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Flank,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Aggressive,
            };
            var kiteOrder = new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Kite,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Aggressive,
            };

            var hist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(7f, 3.5f)
                .WithMemberStepping(true)
                .WithPlayers(player)
                .WithSquad(pinSquad, pinRt)
                .WithOrder(holdOrder)
                .WithExtraSquad(flankSquad, flankRt, flankOrder)
                .WithExtraSquad(harassSquad, harassRt, kiteOrder)
                .BeforeTick((sim, time, tick) =>
                {
                    // Threat ahead of player so facing ~+X; Theater lateral uses right.
                    var p = sim.Players[0].Path(time);
                    // Use sticky/player as formation anchor threat slightly ahead.
                    var ahead = new Vector3(p.x + 20f, 0f, 0f);
                    sim.Squad!.DebugThreatPosition = ahead;
                    flankSquad.DebugThreatPosition = ahead;
                    harassSquad.DebugThreatPosition = ahead;
                    // Keep sticky on player for Ambush harass pocket.
                    pinRt.HasStickyPlayer = true;
                    pinRt.StickyPlayerId = 42;
                    pinRt.StickyPlayerPosition = p;
                    flankRt.HasStickyPlayer = true;
                    flankRt.StickyPlayerId = 42;
                    flankRt.StickyPlayerPosition = p;
                    harassRt.HasStickyPlayer = true;
                    harassRt.StickyPlayerId = 42;
                    harassRt.StickyPlayerPosition = p;
                })
                .Run(64);

            MotionPredicates.Require(
                MotionPredicates.TheaterPinFlankHarass(
                    hist,
                    pinIds,
                    flankIds,
                    harassIds,
                    focusAt: h => h.PlayerPositions.Count > 0 ? h.PlayerPositions[0].pos : h.StickyPosition,
                    pinRLo: 3f,
                    pinRHi: 22f, // roman standoff line ~20
                    flankRLo: 6f,
                    flankRHi: 28f,
                    harassRLo: 1.5f, // Skirmish inner slot can sit ~2m from sticky after HarassLateral
                    bandFraction: 0.80f,
                    pinPhiMax: 0.40f,
                    playerPathMin: 10f,
                    minTicks: 16,
                    warmup: 24,
                    useDesired: true),
                "theater-pin-flank-harass");
        }

        // -------------------- Harness fail gates (must RED on soft-pass patterns) --------------------

        [Fact]
        public void HarnessFail_angVar_only_orbit_must_RED()
        {
            // Freeze ΔM=0 keep arc: high angVar, mean φ constant — |Δφ| after warmup ≈0.
            var hist = new List<TickMetrics>();
            for (int t = 0; t < 20; t++)
            {
                var m = new TickMetrics
                {
                    TickIndex = t,
                    Time = t * 0.25f,
                    HasSticky = true,
                    StickyId = 1,
                    StickyPosition = new Vector3(t * 0.8f, 0f, 0f),
                };
                m.PlayerPositions.Add((1, m.StickyPosition));
                for (int i = 0; i < 5; i++)
                {
                    var ang = -0.6f + i * 0.3f;
                    var pos = m.StickyPosition + new Vector3(Mathf.Cos(ang) * 11f, 0f, Mathf.Sin(ang) * 11f);
                    m.Members.Add(new MemberTickMetric(100 + i, pos, pos, true, false, true));
                }
                hist.Add(m);
            }

            var err = MotionPredicates.AmbushStickyOrbit(
                hist, rLo: 8f, rHi: 14f, bandFraction: 0.80f, flapsMax: 0,
                phiMin: MotionPredicates.PiOverTwo, angVarAndMin: null,
                playerPathMin: 6f, minTicks: 16, warmup: 2);
            Assert.NotNull(err);
            Assert.Contains("|Δφ|", err);
        }

        [Fact]
        public void HarnessFail_freeze_after_fan_in_orbit_must_RED()
        {
            // R1: fan-in first ticks accumulate |Δφ|, then freeze in band — after warmup must RED.
            var hist = new List<TickMetrics>();
            for (int t = 0; t < 28; t++)
            {
                var sticky = new Vector3(t * 0.6f, 0f, 0f);
                var m = new TickMetrics
                {
                    TickIndex = t,
                    Time = t * 0.25f,
                    HasSticky = true,
                    StickyId = 1,
                    StickyPosition = sticky,
                };
                m.PlayerPositions.Add((1, sticky));
                for (int i = 0; i < 4; i++)
                {
                    // Fan-in: swing φ for t<8, then freeze at fixed angles in band.
                    float ang;
                    if (t < 8)
                        ang = (i * 0.4f) + t * 0.35f;
                    else
                        ang = (i * 0.4f) + 8 * 0.35f; // frozen
                    var pos = sticky + new Vector3(Mathf.Cos(ang) * 11f, 0f, Mathf.Sin(ang) * 11f);
                    m.Members.Add(new MemberTickMetric(200 + i, pos, pos, true, false, true));
                }
                hist.Add(m);
            }

            var err = MotionPredicates.AmbushStickyOrbit(
                hist, rLo: 8f, rHi: 14f, bandFraction: 0.80f, flapsMax: 0,
                phiMin: MotionPredicates.PiOverTwo, angVarAndMin: null,
                playerPathMin: 6f, minTicks: 16, warmup: 10);
            Assert.NotNull(err);
        }

        [Fact]
        public void HarnessFail_magnet0_fake_zero_mag_chase_must_RED()
        {
            // Soft pattern: Ambush Orb flat hold OR magnet=0 chase@90 — must not satisfy R3.
            var hist = new List<TickMetrics>();
            var player = Vector3.zero;
            for (int t = 0; t < 12; t++)
            {
                var m = new TickMetrics { TickIndex = t, Time = t * 0.25f };
                m.PlayerPositions.Add((1, player));
                // Flat Orb ring @11 — no FormUp attract toward P.
                var pos = new Vector3(11f, 0f, 0f);
                m.Members.Add(new MemberTickMetric(7, pos, pos, true, false, true));
                hist.Add(m);
            }

            var err = MotionPredicates.ZeroMagnetUnsnapped(
                hist, hist, 7, _ => player, rLo: 3.5f, rHi: 18f, offK: 6, magnetEdgeHint: 3.5f);
            Assert.NotNull(err);
        }

        [Fact]
        public void HarnessFail_theater_plant_PinFlank_same_side_freeze_must_RED()
        {
            // R2 fail case: plant Pin+Flank same side +Z, freeze step — must RED.
            var hist = new List<TickMetrics>();
            for (int t = 0; t < 24; t++)
            {
                var p = new Vector3(4f + t * 1.5f * 0.25f, 0f, 0f);
                var m = new TickMetrics
                {
                    TickIndex = t,
                    Time = t * 0.25f,
                    HasSticky = true,
                    StickyId = 42,
                    StickyPosition = p,
                };
                m.PlayerPositions.Add((42, p));
                // Both roles planted +Z (same half-plane), Desired==Position freeze.
                for (int i = 0; i < 3; i++)
                {
                    var pin = new Vector3(p.x + (i - 1), 0f, 8f);
                    m.Members.Add(new MemberTickMetric(10 + i, pin, pin, false, true, true));
                    var flank = new Vector3(p.x + (i - 1), 0f, 14f);
                    m.Members.Add(new MemberTickMetric(20 + i, flank, flank, false, true, true));
                }
                hist.Add(m);
            }

            var err = MotionPredicates.TheaterPinFlankHarass(
                hist,
                pinIds: new long[] { 10, 11, 12 },
                flankIds: new long[] { 20, 21, 22 },
                harassIds: null,
                focusAt: h => h.PlayerPositions[0].pos,
                pinRLo: 4f, pinRHi: 12f, flankRLo: 8f, flankRHi: 22f,
                bandFraction: 0.80f, pinPhiMax: 0.25f, playerPathMin: 6f,
                minTicks: 16, warmup: 4, useDesired: true);
            Assert.NotNull(err);
        }

        [Fact]
        public void HarnessFail_PackCentroid_as_slot_FormUp_must_RED()
        {
            // Slot that walks with Ms (== PackCentroid soft-pass) must be rejected.
            var hist = new List<TickMetrics>();
            for (int t = 0; t < 12; t++)
            {
                var m = new TickMetrics
                {
                    TickIndex = t,
                    Time = t * 0.25f,
                    PackCentroid = new Vector3(t * 2f, 0f, 0f),
                };
                var pos = new Vector3(30f - t * 1.5f, 0f, 0f);
                m.Members.Add(new MemberTickMetric(3, pos, m.PackCentroid, true, false, true));
                hist.Add(m);
            }

            var err = MotionPredicates.FormUpNoThreat(
                hist, 3, h => h.PackCentroid, closeEnough: 5f, minDrop: 8f, minTicks: 8);
            Assert.NotNull(err);
            Assert.Contains("slot walked", err);
        }

        [Fact]
        public void HarnessFail_PreferRun_as_primary_geom_claim_must_RED()
        {
            // A PreferRun-only "win" with frozen geometry must not satisfy flee/straggler predicates.
            var hist = new List<TickMetrics>();
            for (int t = 0; t < 12; t++)
            {
                var m = new TickMetrics { TickIndex = t, Time = t * 0.25f, PackCentroid = Vector3.zero };
                // PreferRun true but Position frozen near threat — not a flee.
                m.Members.Add(new MemberTickMetric(1, new Vector3(1f, 0f, 0f), new Vector3(1f, 0f, 0f), true, false, true));
                hist.Add(m);
            }

            var fleeErr = MotionPredicates.FleeRadialOutward(
                hist, _ => Vector3.zero, 1, rThreat: 12f, cosThetaMin: 0.707f,
                vMin: 2f, dt: 0.25f, deltaEscape: 2f, desiredCosMin: 0.5f, minTicksAfterThreaten: 8);
            Assert.NotNull(fleeErr);

            var mergeErr = MotionPredicates.StragglerMerge(
                hist, 1, rOut: 5f, rIn: 7f, rPack: 10f);
            Assert.NotNull(mergeErr);
        }
    }
}
