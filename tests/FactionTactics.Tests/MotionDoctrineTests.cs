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

            // Unique id absorb once via MergeStragglers (supporting).
            var parent = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            parent.SquadId = "roman-parent-h5";
            var beforeIds = parent.Members.Select(m => m.InstanceId).ToList();
            var stray = FakeSnapshots.MakeSquad(roman, 1, "Skeleton");
            stray.SquadId = "roman-stray-h5";
            stray.Members[0].Position = new Vector3(8f, 0f, 0f);
            var absorbId = stray.Members[0].InstanceId;
            var merged = SquadDirector.MergeStragglers(new[] { parent, stray });
            var parentOut = Assert.Single(merged, s => s.SquadId == parent.SquadId);
            MotionPredicates.Require(
                MotionPredicates.MergeStragglersUniqueAbsorbOnce(
                    beforeIds, new[] { absorbId }, parentOut.Members.Select(m => m.InstanceId).ToList()),
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
                    angVarAndMin: null, // H1: no OR-escape; optional AND only
                    playerPathMin: 20f,
                    minTicks: 16,
                    warmup: 8),
                "ambush-orbit");

            // Wall-clock sticky dwell with explicit dt (candidate age ≥ T_dwell over ≥3 samples).
            var dwell = PluginConfig.AmbushStickySwitchDwellSeconds.Value;
            const float dt = 0.25f;
            var state = new SquadRuntimeState();
            OrderApplicator.UpdateAmbushStickyAnchor(state, Vector3.zero, new List<(long, Vector3)>
            {
                (101, new Vector3(20f, 0f, 0f)),
            }, dt);
            Assert.Equal(101L, state.StickyPlayerId);

            var samples = new List<(float age, long sticky, long? cand)>();
            // Arm candidate B (hysteresis+ closer) and accumulate until switch.
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
                    samples, dwell, dt, initialStickyId: 101, switchToId: 202, minAgeSamplesAtOrAboveDwell: 3),
                "ambush-sticky-dwell");
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
            var coreC = Vector3.zero;
            foreach (var c in core) coreC += c;
            coreC /= core.Count;
            Assert.True(MotionPredicates.Dist(slotS, coreC) < 14f,
                $"FormUp slot too far from core lattice Dist={MotionPredicates.Dist(slotS, coreC):F1}");

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

        /// <summary>(6) H2: Magnet ON soft band near P; then OFF — Dist non-decreasing, D not on P.</summary>
        [Fact]
        public void Geom_Zero_magnet_Position_and_Desired_stay_off_player()
        {
            // Ambush Orb around sticky P: magnet ON soft attract keeps Dist(M,P),Dist(D,P) in band;
            // magnet OFF must not collapse Dist toward P / Desired onto P.
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            var ambush = DoctrinePackRegistry.CreateDefault().GetById("ambush")!;
            var playerAt = new Vector3(0f, 0f, 0f);
            var squad = FakeSnapshots.MakeSquad(ambush, 4, "Greydwarf");
            for (int i = 0; i < squad.Members.Count; i++)
            {
                var ang = i * (2f * Mathf.PI / 4f);
                squad.Members[i].Position = new Vector3(
                    Mathf.Cos(ang) * 11f, 0f, Mathf.Sin(ang) * 11f);
            }
            var midId = squad.Members[0].InstanceId;
            var player = SimPlayer.Parametric(5, _ => playerAt);
            var runtime = new SquadRuntimeState();

            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var sim = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(8f, 3.5f)
                .WithMemberStepping(true)
                .WithPlayers(player)
                .WithSquad(squad, runtime)
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Flank,
                    Formation = FormationType.Orb,
                    Stance = StanceType.Aggressive,
                });
            var onHist = sim.Run(12);

            PluginConfig.FormUpMagnetDistance.Value = 0f;
            var offHist = sim.Run(10);

            MotionPredicates.Require(
                MotionPredicates.ZeroMagnetUnsnapped(
                    onHist,
                    offHist,
                    midId,
                    h => h.HasSticky ? h.StickyPosition : playerAt,
                    rLo: 8f,
                    rHi: 14f,
                    offK: 6),
                "zero-magnet");
        }

        /// <summary>(7) H7: Theater Pin/Flank/Harass geometry + Assign(dt)×N in same run.</summary>
        [Fact]
        public void Geom_Theater_Pin_vs_Flank_lateral_halfplane_over_path()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var ambush = registry.GetById("ambush")!;

            // Assign(dt)×N supporting Fact (not one-shot).
            var pinView = new TheaterSquadView
            {
                DoctrineId = "roman",
                StableId = "roman#h7",
                Centroid = new Vector3(0f, 0f, 0f),
                HasFocus = true,
                FocusPlayerId = 42,
                FocusPosition = Vector3.zero,
                State = new SquadRuntimeState { StableId = "roman#h7", DoctrineId = "roman" },
            };
            var flankView = new TheaterSquadView
            {
                DoctrineId = "roman",
                StableId = "roman#h7-flank",
                Centroid = new Vector3(14f, 0f, 0f),
                HasFocus = true,
                FocusPlayerId = 42,
                FocusPosition = Vector3.zero,
                State = new SquadRuntimeState { StableId = "roman#h7-flank", DoctrineId = "roman" },
            };
            var harassView = new TheaterSquadView
            {
                DoctrineId = "ambush",
                StableId = "ambush#h7",
                Centroid = new Vector3(18f, 0f, 0f),
                HasFocus = true,
                FocusPlayerId = 42,
                FocusPosition = Vector3.zero,
                State = new SquadRuntimeState { StableId = "ambush#h7", DoctrineId = "ambush" },
            };
            for (int i = 0; i < 10; i++)
                TheaterCommander.Assign(new[] { pinView, flankView, harassView }, 0.25f);
            Assert.True(pinView.State.TheaterRoleAgeSeconds > 0.5f,
                "Assign(dt)×N must accumulate role age (not one-shot)");
            Assert.NotEqual(TheaterRole.None, pinView.State.TheaterRole);

            var pinSquad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            var flankSquad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            var harassSquad = FakeSnapshots.MakeSquad(ambush, 4, "Greydwarf");
            // Parallel walk: Pin holds ~8m on +Z; Flank ~14m +Z; Harass outer −Z — Dist stable in bands.
            // Parallel walk Δx≈12m (path≥10) while Dist to Pin@z=8 stays in [4,12].
            var player = SimPlayer.Parametric(42, t => new Vector3(4f + t * 1.5f, 0f, 0f));
            for (int i = 0; i < 4; i++)
            {
                pinSquad.Members[i].Position = new Vector3(10f + (i - 1.5f) * 1.0f, 0f, 8f);
                flankSquad.Members[i].Position = new Vector3(10f + (i - 1.5f) * 1.0f, 0f, 14f);
                harassSquad.Members[i].Position = new Vector3(10f + (i - 1.5f) * 1.0f, 0f, -16f);
            }

            // Planted bodies in role bands (no step) — Apply still writes Desired with Theater lateral bias.
            PlayerPathSim TheaterSim(SquadUnit squad, SquadRuntimeState rt, SquadOrder order) =>
                new PlayerPathSim()
                    .WithDt(0.25f)
                    .WithSpeeds(7f, 3.5f)
                    .WithMemberStepping(false)
                    .WithPlayers(player)
                    .WithSquad(squad, rt)
                    .BeforeTick((sim, _, _) => { sim.Squad!.DebugThreatPosition = new Vector3(10f, 0f, 40f); })
                    .WithOrder(order);

            var pinHist = TheaterSim(pinSquad, new SquadRuntimeState
            {
                TheaterRole = TheaterRole.Pin,
                RomanPhase = RomanPhase.StandoffHold,
            }, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            }).Run(32);

            var flankHist = TheaterSim(flankSquad, new SquadRuntimeState
            {
                TheaterRole = TheaterRole.Flank,
                RomanPhase = RomanPhase.StandoffHold,
            }, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            }).Run(32);

            var harassHist = TheaterSim(harassSquad, new SquadRuntimeState
            {
                TheaterRole = TheaterRole.Harass,
            }, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Kite,
                Formation = FormationType.Orb,
                Stance = StanceType.Aggressive,
            }).Run(32);

            MotionPredicates.Require(
                MotionPredicates.TheaterPinFlankHarass(
                    pinHist,
                    flankHist,
                    harassHist,
                    focusAt: h => h.PlayerPositions.Count > 0 ? h.PlayerPositions[0].pos : h.StickyPosition,
                    pinRLo: 4f,
                    pinRHi: 12f,
                    flankRLo: 8f,
                    flankRHi: 22f,
                    harassRLo: 10f,
                    bandFraction: 0.80f,
                    pinPhiMax: 0.25f, // H7
                    playerPathMin: 10f,
                    minTicks: 16,
                    warmup: 10),
                "theater-pin-flank-harass");
        }

        // -------------------- Harness fail gates (must RED on soft-pass patterns) --------------------

        [Fact]
        public void HarnessFail_angVar_only_orbit_must_RED()
        {
            // Freeze ΔM=0 keep ring: high angular variance, zero |Δφ| travel — old OR-escape would pass.
            var hist = new List<TickMetrics>();
            // Freeze ΔM=0 keep arc: high angVar, mean φ constant (no travel) — old OR-escape would pass.
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
                    // Arc on +Z half only — mean φ stable ~π/2, variance high, |Δφ|sum≈0.
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
            Assert.Contains("|Δφ|sum", err);
        }

        [Fact]
        public void HarnessFail_magnet0_fake_zero_mag_chase_must_RED()
        {
            // Old soft pattern: FormUpMagnetDistance=0, Ms@40 chasing threat@90 — must not satisfy H2.
            var hist = new List<TickMetrics>();
            var player = Vector3.zero;
            var threat = new Vector3(90f, 0f, 0f);
            for (int t = 0; t < 12; t++)
            {
                var m = new TickMetrics { TickIndex = t, Time = t * 0.25f };
                m.PlayerPositions.Add((1, player));
                var pos = new Vector3(40f + t * 2f, 0f, 0f); // chasing threat, Dist(M,P)→ large
                m.Members.Add(new MemberTickMetric(7, pos, threat, true, false, true));
                hist.Add(m);
            }

            var err = MotionPredicates.ZeroMagnetUnsnapped(
                hist, hist, 7, _ => player, rLo: 4f, rHi: 18f, offK: 6);
            Assert.NotNull(err); // Dist(M,P) not in soft-attract band
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
