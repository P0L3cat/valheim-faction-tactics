using System;
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
    /// </summary>
    public class MotionDoctrineTests
    {
        public MotionDoctrineTests() => TestConfig.EnsureBound();

        /// <summary>(1) Kite under close threat: radial outward flee geometry.</summary>
        [Fact]
        public void Geom_PreferRun_flee_radial_outward_when_threatened()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            var ambush = DoctrinePackRegistry.CreateDefault().GetById("ambush")!;
            var squad = FakeSnapshots.MakeSquad(ambush, 4, "Greydwarf");
            // Start INSIDE threat radius so orbit slots (~11m) force outward escape.
            for (int i = 0; i < squad.Members.Count; i++)
                squad.Members[i].Position = new Vector3(0.8f + i * 0.25f, 0f, 0.15f * i);

            var player = SimPlayer.Parametric(7, _ => new Vector3(0f, 0f, 0f));
            var hist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(7f, 3.5f)
                .WithMemberStepping(true)
                .WithThreatSyncedFromPlayers(true)
                .WithPlayers(player)
                .WithSquad(squad)
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Kite,
                    Formation = FormationType.Orb,
                    Stance = StanceType.Aggressive,
                })
                .Run(20);

            Assert.True(hist.Count >= 8);
            var id = squad.Members[0].InstanceId;
            MotionPredicates.Require(
                MotionPredicates.FleeRadialOutward(
                    hist,
                    h => h.StickyPosition,
                    id,
                    rThreat: 12f,
                    cosThetaMin: 0.50f,
                    vMin: 2.0f,
                    dt: 0.25f,
                    deltaEscape: 2.0f,
                    minTicks: 8),
                "flee-radial");

            // Secondary: PreferRun while moving.
            Assert.Equal(0, PlayerPathSim.TotalPreferRunViolations(hist));
        }

        /// <summary>(2) Straggler Position closes on pack centroid under FormUp magnet.</summary>
        [Fact]
        public void Geom_straggler_merge_closes_on_centroid()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var roman = DoctrinePackRegistry.CreateDefault().GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            for (int i = 0; i < 3; i++)
                squad.Members[i].Position = new Vector3(i * 1.5f, 0f, 0f);
            squad.Members[3].Position = new Vector3(42f, 0f, 0f);
            var farId = squad.Members[3].InstanceId;

            var player = SimPlayer.Parametric(9, t => new Vector3(8f + t * 3f, 0f, 2f));
            var hist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(8f, 3.5f)
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
                MotionPredicates.StragglerMerge(
                    hist, farId, rOut: 20f, rIn: 14f, rPack: 18f, lastK: 5, minTicks: 10),
                "straggler-merge");

            Assert.True(hist[0].Members.First(m => m.InstanceId == farId).PreferRun); // secondary
        }

        /// <summary>(3) Ambush sticky orbit: band + angular + player path + explicit-dt dwell flaps.</summary>
        [Fact]
        public void Geom_Ambush_sticky_orbit_band_angular_player_path()
        {
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            PluginConfig.AmbushStickySwitchDwellSeconds.Value = 1.0f;
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;

            var ambush = DoctrinePackRegistry.CreateDefault().GetById("ambush")!;
            var squad = FakeSnapshots.MakeSquad(ambush, 5, "Greydwarf");
            for (int i = 0; i < squad.Members.Count; i++)
            {
                var ang = i * (2f * Mathf.PI / 5f);
                squad.Members[i].Position = new Vector3(
                    8f + Mathf.Cos(ang) * 11f, 0f, Mathf.Sin(ang) * 11f);
            }

            var player = SimPlayer.Waypoints(101, new[]
            {
                new Vector3(8f, 0f, 0f),
                new Vector3(30f, 0f, 0f),
                new Vector3(30f, 0f, 22f),
            }, 2.5f);

            var hist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(7f, 3.5f)
                .WithMemberStepping(true)
                .WithPlayers(player)
                .WithSquad(squad)
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Flank,
                    Formation = FormationType.Orb,
                    Stance = StanceType.Aggressive,
                })
                .Run(36);

            MotionPredicates.Require(
                MotionPredicates.AmbushStickyOrbit(
                    hist,
                    rLo: 4f,
                    rHi: 22f,
                    bandFraction: 0.75f,
                    flapsMax: 0,
                    phiMin: 0.8f,
                    angVarMin: 0.05f,
                    playerPathMin: 6f,
                    minTicks: 16,
                    warmup: 6),
                "ambush-orbit");
        }

        /// <summary>(4) Pack-as-Unit cohesion while player translates.</summary>
        [Fact]
        public void Geom_PackAsUnit_cohesion_while_player_translates()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var roman = DoctrinePackRegistry.CreateDefault().GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 5, "Skeleton");
            for (int i = 0; i < squad.Members.Count; i++)
                squad.Members[i].Position = new Vector3(i * 2f, 0f, (i % 2) * 1.5f);

            var player = SimPlayer.Parametric(3, t => new Vector3(12f + t * 4f, 0f, 1f));
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
                .Run(24);

            MotionPredicates.Require(
                MotionPredicates.PackAsUnit(
                    hist,
                    sigmaMax: 14f,
                    sigmaFraction: 0.85f,
                    alphaDeg: 45f,
                    epsRel: 6.0f,
                    playerTranslateMin: 8f,
                    minMobs: 3,
                    minTicks: 12,
                    warmup: 4),
                "pack-as-unit");
        }

        /// <summary>(5a) FormUp harness — no threat chase; Desired/Position to slot.</summary>
        [Fact]
        public void Geom_FormUp_no_threat_Desired_and_Position_to_slot()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var roman = DoctrinePackRegistry.CreateDefault().GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            for (int i = 0; i < 3; i++)
                squad.Members[i].Position = new Vector3(i * 1.2f, 0f, 0f);
            squad.Members[3].Position = new Vector3(35f, 0f, 0f);
            var farId = squad.Members[3].InstanceId;

            // No threat sync — FormUp magnet alone.
            var player = SimPlayer.Parametric(1, _ => new Vector3(100f, 0f, 0f)); // far, unused without sync
            var hist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(8f, 3.5f)
                .WithMemberStepping(true)
                .WithPlayers(player)
                .WithSquad(squad, new SquadRuntimeState { RomanPhase = RomanPhase.ApproachStandoff })
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Hold,
                    Formation = FormationType.ShieldWall,
                    Stance = StanceType.Defensive,
                })
                .Run(20);

            MotionPredicates.Require(
                MotionPredicates.FormUpNoThreat(
                    hist,
                    farId,
                    h => h.PackCentroid, // slot cluster ≈ centroid for magnet assert
                    closeEnough: 12f,
                    minTicks: 8),
                "formup-no-threat");
        }

        /// <summary>(5b) Charge harness — WITH DebugThreat inject; Desired toward threat, not FormUp.</summary>
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
                    formUpSlotAt: h => h.PackCentroid,
                    minClose: 4f,
                    minTicks: 8),
                "charge-threat");
        }

        /// <summary>(6) Zero magnet: Position AND Desired stay off player.</summary>
        [Fact]
        public void Geom_Zero_magnet_Position_and_Desired_stay_off_player()
        {
            PluginConfig.FormUpMagnetDistance.Value = 0f;
            var rush = DoctrinePackRegistry.CreateDefault().GetById("death-rush")!;
            var squad = FakeSnapshots.MakeSquad(rush, 4, "Greyling");
            for (int i = 0; i < 3; i++)
                squad.Members[i].Position = new Vector3(i * 1.2f, 0f, 0f);
            squad.Members[3].Position = new Vector3(40f, 0f, 0f);
            var farId = squad.Members[3].InstanceId;
            var threat = new Vector3(90f, 0f, 0f);
            squad.DebugThreatPosition = threat;

            var player = SimPlayer.Parametric(5, _ => new Vector3(0f, 0f, 0f));
            var hist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(8f, 3.5f)
                .WithMemberStepping(true)
                .WithPlayers(player)
                .WithSquad(squad)
                .BeforeTick((sim, _, _) => { sim.Squad!.DebugThreatPosition = threat; })
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.FocusFire,
                    Formation = FormationType.Wedge,
                    Stance = StanceType.Aggressive,
                })
                .Run(16);

            MotionPredicates.Require(
                MotionPredicates.ZeroMagnetUnsnapped(
                    hist,
                    farId,
                    h => h.PlayerPositions.Count > 0 ? h.PlayerPositions[0].pos : Vector3.zero,
                    rFloor: 20f,
                    rFloorD: 40f, // Desired on threat at x=90, player at 0
                    minTicks: 10,
                    lastK: 6),
                "zero-magnet");
        }

        /// <summary>(7) Theater Pin vs Flank geometry over multi-tick player path (not enum-only).</summary>
        [Fact]
        public void Geom_Theater_Pin_vs_Flank_lateral_halfplane_over_path()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;

            var pinSquad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            var flankSquad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            for (int i = 0; i < 4; i++)
            {
                pinSquad.Members[i].Position = new Vector3(i * 1.5f, 0f, 0f);
                flankSquad.Members[i].Position = new Vector3(i * 1.5f, 0f, 0f);
            }

            var player = SimPlayer.Parametric(42, t => new Vector3(18f + t * 3f, 0f, 0f));

            var pinHist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(7f, 3.5f)
                .WithMemberStepping(true)
                .WithThreatSyncedFromPlayers(true)
                .WithPlayers(player)
                .WithSquad(pinSquad, new SquadRuntimeState
                {
                    TheaterRole = TheaterRole.Pin,
                    RomanPhase = RomanPhase.PressContact,
                })
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Advance,
                    Formation = FormationType.ShieldWall,
                    Stance = StanceType.Aggressive,
                })
                .Run(24);

            var flankHist = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(7f, 3.5f)
                .WithMemberStepping(true)
                .WithThreatSyncedFromPlayers(true)
                .WithPlayers(player)
                .WithSquad(flankSquad, new SquadRuntimeState
                {
                    TheaterRole = TheaterRole.Flank,
                    RomanPhase = RomanPhase.PressContact,
                })
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Advance,
                    Formation = FormationType.ShieldWall,
                    Stance = StanceType.Aggressive,
                })
                .Run(24);

            MotionPredicates.Require(
                MotionPredicates.TheaterPinFlankHarass(
                    pinHist,
                    flankHist,
                    harassHist: null,
                    focusAt: h => h.PlayerPositions.Count > 0 ? h.PlayerPositions[0].pos : h.StickyPosition,
                    pinRLo: 2f,
                    pinRHi: 28f,
                    flankRLo: 2f,
                    flankRHi: 36f,
                    harassRLo: 6f,
                    bandFraction: 0.70f,
                    pinPhiMax: 1.2f,
                    playerPathMin: 10f,
                    minTicks: 16,
                    warmup: 4),
                "theater-pin-flank");

            // Secondary: roles differ in Desired mean X (existing Applicator geometry).
            var pinDesX = pinHist[pinHist.Count - 1].Members.Average(m => m.DesiredPosition.x);
            var flankDesX = flankHist[flankHist.Count - 1].Members.Average(m => m.DesiredPosition.x);
            Assert.True(Math.Abs(flankDesX - pinDesX) > 4f,
                $"supporting: Flank Desired mean x {flankDesX:F1} should separate from Pin {pinDesX:F1}");
        }
    }
}
