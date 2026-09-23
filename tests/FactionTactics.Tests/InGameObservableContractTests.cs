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
    /// Offline proves intent/role/sticky math only — not Valheim pixels. See Proof §1–2.
    /// Azog R1 closed: sticky dwell ticks, PreferRun multi-tick, straggler centroid, orbit spread, theater siblings cited in PROOF.
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

            Assert.Equal(0, PlayerPathSim.TotalPreferRunViolations(hist));
            Assert.True(hist.Count >= 8);
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

            // Desired leaves the spawn pile (not freeze-in-blob).
            var late = hist[hist.Count - 1];
            var meanDesiredFromSpawn = late.Members.Average(m =>
                Vector3.Distance(m.DesiredPosition, spawnCentroid));
            Assert.True(meanDesiredFromSpawn > 2f,
                $"Advance Desired still piled on spawn (meanDist={meanDesiredFromSpawn:F2}) — PROOF claim 1");

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
                Assert.Equal(!intent.HoldGround, intent.PreferRun);
                if (intent.HoldGround)
                {
                    anyPlant = true;
                    Assert.False(intent.PreferRun);
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
                squad.Members[i].Position = new Vector3(8f + i, 0f, 0f);

            var player = SimPlayer.Waypoints(101, new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(30f, 0f, 0f),
                new Vector3(30f, 0f, 30f),
            }, 2.0f);

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
                .Run(32);

            Assert.True(hist.Count >= 10);
            var late = hist.Skip(hist.Count / 2).ToList();
            foreach (var tick in late)
            {
                Assert.True(tick.HasSticky);
                Assert.Equal(101L, tick.StickyId);
                Assert.True(tick.MeanDesiredToSticky < 20f,
                    $"orbit mean desired-to-sticky {tick.MeanDesiredToSticky:F1}m — PROOF claim 2 band");

                // Angular / offset spread around sticky — not all members sharing one identical offset.
                var angles = new List<float>();
                foreach (var m in tick.Members)
                {
                    var off = m.DesiredPosition - tick.StickyPosition;
                    off.y = 0f;
                    if (off.sqrMagnitude < 1e-6f)
                        continue;
                    angles.Add(Mathf.Atan2(off.z, off.x));
                }
                Assert.True(angles.Count >= 3, "need ≥3 orbit Desired offsets for spread assert");
                var meanAng = angles.Average();
                var varAng = angles.Average(a =>
                {
                    var d = a - meanAng;
                    while (d > Math.PI) d -= (float)(2 * Math.PI);
                    while (d < -Math.PI) d += (float)(2 * Math.PI);
                    return d * d;
                });
                Assert.True(varAng > 0.05f,
                    $"orbit Desired angles collapsed (var={varAng:F3}) — slots must spread around sticky");
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
            Assert.Equal(0, PlayerPathSim.CountStickyFlaps(brush));
            Assert.True(brush.All(t => t.StickyId == 101L),
                "3 ticks @ dt=0.25 with hysteresis+ closer must not flip sticky (0.75s < 1.0 dwell)");

            // Phase 2: keep B closer for 4+ more ticks (≥1.0s continuous from phase1 start, or fresh dwell).
            // Candidate already armed from brush — continue same sim runtime.
            var sustained = sim.RunStickyOnly(5, fixedCentroid: Vector3.zero);
            var combined = brush.Concat(sustained).ToList();
            var flaps = PlayerPathSim.CountStickyFlaps(combined);
            Assert.Equal(1, flaps);
            Assert.Equal(202L, combined[combined.Count - 1].StickyId);
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
            Assert.False(first.HoldGround);

            // Hard: Desired near pack centroid / lattice, not soft Desired.x < 25.
            var lateCentroid = hist[hist.Count - 1].PackCentroid;
            var desiredToCentroid = Vector3.Distance(last.DesiredPosition, lateCentroid);
            Assert.True(desiredToCentroid < 15f,
                $"straggler Desired far from pack centroid dist={desiredToCentroid:F1} "
                + $"(desired=({last.DesiredPosition.x:F1},{last.DesiredPosition.z:F1}), "
                + $"centroid=({lateCentroid.x:F1},{lateCentroid.z:F1})) — PROOF claim 3; "
                + "see also PackAsUnitEdgeTests.Hold_order_magnets_far_member_with_PreferRun / "
                + "Ambush_Flank_magnets_far_member_toward_slot");

            var earlyGap = Vector3.Distance(first.Position, hist[0].PackCentroid);
            var lateGap = Vector3.Distance(last.Position, lateCentroid);
            Assert.True(lateGap + 1f < earlyGap,
                $"straggler did not close under step sim (early={earlyGap:F1} late={lateGap:F1}) — "
                + "LIVE STEP: kite and watch FormUp");
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
