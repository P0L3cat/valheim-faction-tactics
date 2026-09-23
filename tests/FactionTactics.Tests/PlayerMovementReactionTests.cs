using System;
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
    /// <summary>
    /// Adversarial multi-tick player-movement reaction tests.
    /// Thresholds are intentionally HARD — if a scenario greens trivially, tighten.
    /// Failures that name a real sticky/magnet/reattach invariant gap are preferred to watered asserts.
    /// </summary>
    public class PlayerMovementReactionTests
    {
        public PlayerMovementReactionTests() => TestConfig.EnsureBound();

        [Fact]
        public void Sticky_hysteresis_under_zigzag_zero_flaps_then_one_switch()
        {
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            const float hysteresis = 10f;
            const float below = hysteresis - 2f; // 8m closer — must NOT switch
            const float above = hysteresis + 5f; // 15m closer — must switch once

            // Seed sticky on A alone first (A orbits at 12m).
            var state = new SquadRuntimeState();
            OrderApplicator.UpdateAmbushStickyAnchor(state, Vector3.zero, new List<(long, Vector3)>
            {
                (101, new Vector3(12f, 0f, 0f)),
            }, 0.25f);
            Assert.Equal(101L, state.StickyPlayerId);

            var a = SimPlayer.Parametric(101, t =>
            {
                var ang = t * 1.2f;
                return new Vector3(12f * (float)Math.Cos(ang), 0f, 12f * (float)Math.Sin(ang));
            });

            // Phase 0..10s: B only 8m closer than A — no flap.
            // Phase 10s+: B hysteresis+ closer and stays — exactly one switch.
            var b = SimPlayer.Parametric(202, t =>
            {
                if (t < 10f)
                    return new Vector3(12f - below, 0f, 0f);
                return new Vector3(Math.Max(0.5f, 12f - above), 0f, 0f);
            });

            var sim = new PlayerPathSim()
                .WithDt(0.25f)
                .WithPlayers(a, b);
            // Carry seeded sticky into the sim runtime.
            sim.Runtime.HasStickyPlayer = true;
            sim.Runtime.StickyPlayerId = 101;
            sim.Runtime.StickyPlayerPosition = new Vector3(12f, 0f, 0f);

            const int phase1 = 40;
            const int phase2 = 40;
            var hist = sim.RunStickyOnly(phase1 + phase2, fixedCentroid: Vector3.zero);

            Assert.True(hist[0].HasSticky, "must keep seeded sticky");
            Assert.Equal(101L, hist[0].StickyId);

            var phase1Hist = hist.Take(phase1).ToList();
            var flapsP1 = PlayerPathSim.CountStickyFlaps(phase1Hist);
            Assert.True(flapsP1 == 0,
                $"sticky flapped {flapsP1} times under sub-hysteresis zigzag (invariant: flap count == 0)");

            var after = hist.Skip(phase1).ToList();
            Assert.True(after.Count > 0);
            Assert.True(after.Any(m => m.StickyId == 202L),
                "sticky must switch to B once B is hysteresis+ closer and stays");
            // Count flaps across the phase boundary (phase2-only would miss the tick-0 switch).
            var totalFlaps = PlayerPathSim.CountStickyFlaps(hist);
            Assert.True(totalFlaps == 1,
                $"expected exactly one sticky switch across the run, got {totalFlaps}");
            Assert.Equal(202L, hist[hist.Count - 1].StickyId);
            var firstB = hist.FindIndex(m => m.StickyId == 202L);
            Assert.True(hist.Skip(firstB).All(m => m.StickyId == 202L),
                "sticky must not flap after the decisive switch to B");
        }

        [Fact]
        public void Ambush_orbit_tracks_moving_sticky_within_slot_band()
        {
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;

            var registry = DoctrinePackRegistry.CreateDefault();
            var ambush = registry.GetById("ambush")!;
            var squad = FakeSnapshots.MakeSquad(ambush, 5, "Greydwarf");
            for (int i = 0; i < squad.Members.Count; i++)
                squad.Members[i].Position = new Vector3(20f + i * 0.4f, 0f, 0.2f * i);

            var player = SimPlayer.Waypoints(
                42,
                new[]
                {
                    new Vector3(8f, 0f, 0f),
                    new Vector3(36f, 0f, 0f),
                    new Vector3(36f, 0f, 24f),
                    new Vector3(8f, 0f, 24f),
                },
                segmentSeconds: 4f);

            var runtime = new SquadRuntimeState();
            var sim = new PlayerPathSim()
                .WithDt(0.5f)
                .WithSpeeds(runSpeed: 8f, walkSpeed: 4f)
                .WithMemberStepping(true)
                .WithSquad(squad, runtime)
                .WithPlayers(player)
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Flank,
                    Formation = FormationType.Orb,
                    Stance = StanceType.Aggressive,
                });

            const int ticks = 64;
            var hist = sim.Run(ticks);

            MotionPredicates.Require(
                MotionPredicates.AmbushStickyOrbit(
                    hist, rLo: 8f, rHi: 14f, bandFraction: 0.80f, flapsMax: 0,
                    phiMin: MotionPredicates.PiOverTwo, angVarAndMin: null, playerPathMin: 6f, minTicks: 16, warmup: 4),
                "Ambush_orbit_tracks_moving_sticky");

            Assert.True(hist.All(h => h.HasSticky), "sticky must stay acquired along the walk");
            Assert.True(hist.All(h => h.StickyId == 42L), "single-player walk must not flap sticky id");

            var steady = hist.Skip(4).ToList();
            Assert.NotEmpty(steady);
            foreach (var h in steady)
            {
                Assert.True(h.MeanDesiredToSticky < 20f,
                    $"tick {h.TickIndex}: mean slot→sticky {h.MeanDesiredToSticky:F2}m exceeds 20m band "
                    + $"(stickyX={h.StickyPosition.x:F1} stickyZ={h.StickyPosition.z:F1} "
                    + $"centroidX={h.PackCentroid.x:F1} centroidZ={h.PackCentroid.z:F1}) — slots glued behind?");
                Assert.True(h.MeanPositionToSticky < 26f,
                    $"tick {h.TickIndex}: mean Position→sticky {h.MeanPositionToSticky:F2}m exceeds band");
            }

            var earlyStickyX = hist[4].StickyPosition.x;
            var mid = hist[hist.Count / 2];
            Assert.True(mid.StickyPosition.x > earlyStickyX + 10f || mid.StickyPosition.z > 5f,
                $"player path did not translate enough mid=({mid.StickyPosition.x:F1},{mid.StickyPosition.z:F1}) earlyX={earlyStickyX}");
            var late = hist[hist.Count - 1];
            var meanSlotX = late.Members.Where(m => m.HasIntent).Average(m => m.DesiredPosition.x);
            Assert.True(Math.Abs(meanSlotX - late.StickyPosition.x) < 16f,
                $"slots lagged sticky: meanSlotX={meanSlotX:F1} stickyX={late.StickyPosition.x:F1} "
                + "(orbit stuck on old centroid)");
        }

        [Fact]
        public void PackAsUnit_chase_cohesion_diameter_shrinks_zero_PreferRun_violations()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;

            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 5, "Skeleton");
            squad.Members[0].Position = new Vector3(0f, 0f, 0f);
            squad.Members[1].Position = new Vector3(3f, 0f, 0f);
            squad.Members[2].Position = new Vector3(6f, 0f, 1f);
            squad.Members[3].Position = new Vector3(2f, 0f, -2f);
            squad.Members[4].Position = new Vector3(45f, 0f, 0f);

            var player = SimPlayer.Parametric(7, t => new Vector3(10f, 0f, 5f + t * 6f));

            var runtime = new SquadRuntimeState { RomanPhase = RomanPhase.PressContact };
            var sim = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(runSpeed: 7f, walkSpeed: 3.5f)
                .WithMemberStepping(true)
                .WithSquad(squad, runtime)
                .WithPlayers(player)
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Advance,
                    Formation = FormationType.ShieldWall,
                    Stance = StanceType.Aggressive,
                });

            var hist = sim.Run(48);
            Assert.True(hist.Count >= 16);

            var totalViolations = hist.Sum(h => h.PreferRunViolations);
            Assert.True(totalViolations == 0,
                $"PreferRun != !HoldGround on {totalViolations} member-ticks (must be zero)");

            foreach (var h in hist)
            {
                foreach (var m in h.Members.Where(x => x.HasIntent))
                {
                    Assert.False(m.HoldGround,
                        $"tick {h.TickIndex} member {m.InstanceId}: HoldGround planted during Advance chase");
                    Assert.True(m.PreferRun,
                        $"tick {h.TickIndex} member {m.InstanceId}: PreferRun false while !HoldGround");
                }
            }

            var farId = squad.Members[4].InstanceId;
            MotionPredicates.Require(
                MotionPredicates.StragglerMerge(
                    hist, farId, rOut: 18f, rIn: 7f, rPack: 12f, lastK: 4, minTicks: 10),
                "PackAsUnit_chase_straggler");

            var d0 = hist[0].PackDiameter;
            var dLate = hist.Skip(hist.Count / 2).Average(h => h.PackDiameter);
            var lateFar = hist[hist.Count - 1].Members.First(m => m.InstanceId == farId);
            var lateC = hist[hist.Count - 1].PackCentroid;
            var magnetized = MotionPredicates.Dist(lateFar.DesiredPosition, lateC) < 16f;

            Assert.True(
                dLate + 0.5f < d0 || magnetized,
                $"supporting: diameter d0={d0:F1} dLate={dLate:F1} magnetized={magnetized}");
        }

        [Fact]
        public void Straggler_reattach_after_chase_loop_within_K_ticks()
        {
            PluginConfig.MinSquadSize.Value = 3;
            PluginConfig.SquadMergeRadius.Value = 40f;
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;

            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var pack = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            pack.SquadId = "roman-pack-chase";
            for (int i = 0; i < pack.Members.Count; i++)
                pack.Members[i].Position = new Vector3(i * 2f, 0f, 0f);

            var dropout = FakeSnapshots.MakeSquad(roman, 1, "Skeleton");
            dropout.SquadId = "roman-dropout-chase";
            dropout.Members[0].Position = new Vector3(80f, 0f, 0f);
            var dropoutId = dropout.Members[0].InstanceId;

            const int k = 6;
            const float merge = 40f;

            OrderApplicator.Intents.Clear();
            var commander = new ScriptedCommander(registry, new SiegeDirector());
            var applicator = new OrderApplicator();

            var director = new SquadDirector(
                new FakeDiscovery(pack, dropout),
                commander,
                registry,
                new NullRoleScorer(),
                new NullActionScorer(),
                applicator,
                new SiegeDirector());

            director.Tick(0.75f);
            Assert.False(OrderApplicator.Intents.ContainsKey(dropoutId),
                "dropout beyond merge must not receive intents before reattach");

            int firstInRangeTick = -1;
            int reattachedAt = -1;
            for (int tick = 0; tick < 24; tick++)
            {
                var packC = PlayerPathSim.ComputeCentroid(pack);
                var dropC = PlayerPathSim.ComputeCentroid(dropout);
                var gap = Vector3.Distance(packC, dropC);
                if (gap > merge - 1f)
                {
                    var dir = (dropC - packC);
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 1e-6f)
                    {
                        dir.Normalize();
                        foreach (var m in pack.Members)
                            m.Position += dir * 12f;
                    }
                }

                // Avoid double-adding dropout members after a prior merge mutated pack.
                if (pack.Members.Any(m => m.InstanceId == dropoutId))
                {
                    // Already folded into pack object — still need intents via director.
                }

                OrderApplicator.Intents.Clear();
                // Reset pack roster if previous MergeStragglers duplicated members into pack
                // while discovery still lists dropout separately — use fresh member lists via clone positions.
                var packNow = CloneSquad(pack, "roman-pack-chase");
                var dropNow = CloneSquad(dropout, "roman-dropout-chase");
                // If pack already absorbed dropout ids from a prior helper call, strip them so discovery is clean.
                packNow.Members.RemoveAll(m => m.InstanceId == dropoutId);

                director = new SquadDirector(
                    new FakeDiscovery(packNow, dropNow),
                    commander,
                    registry,
                    new NullRoleScorer(),
                    new NullActionScorer(),
                    applicator,
                    new SiegeDirector());
                director.Tick(0.75f);

                var merged = SquadDirector.MergeStragglers(new[] { CloneSquad(packNow, "p"), CloneSquad(dropNow, "d") });
                var inParent = merged.Count == 1
                    && merged[0].Members.Any(m => m.InstanceId == dropoutId);
                var hasIntent = OrderApplicator.Intents.ContainsKey(dropoutId);

                var gapNow = Vector3.Distance(
                    PlayerPathSim.ComputeCentroid(packNow),
                    PlayerPathSim.ComputeCentroid(dropNow));
                if (gapNow <= merge && firstInRangeTick < 0)
                    firstInRangeTick = tick;

                // Sync positions back so next iteration continues the chase.
                for (int i = 0; i < pack.Members.Count && i < packNow.Members.Count; i++)
                    pack.Members[i].Position = packNow.Members[i].Position;
                dropout.Members[0].Position = dropNow.Members[0].Position;

                if (inParent && hasIntent)
                {
                    reattachedAt = tick;
                    break;
                }

                if (firstInRangeTick >= 0 && tick - firstInRangeTick >= k)
                    break;
            }

            var finalPack = PlayerPathSim.ComputeCentroid(pack);
            var finalDrop = PlayerPathSim.ComputeCentroid(dropout);
            Assert.True(reattachedAt >= 0,
                $"straggler failed to reattach with intents within chase loop "
                + $"(merge={merge}m, packX={finalPack.x:F1}, dropX={finalDrop.x:F1}, "
                + $"firstInRange={firstInRangeTick})");
            Assert.True(firstInRangeTick >= 0, "chase never closed to merge radius");
            Assert.True(reattachedAt - firstInRangeTick < k,
                $"reattach took {reattachedAt - firstInRangeTick} ticks after in-range (K={k})");
        }

        [Fact]
        public void Cross_player_flap_torture_sticky_id_never_changes()
        {
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            const float hysteresis = 10f;
            var a = SimPlayer.Parametric(501, t =>
            {
                var tick = (int)Math.Round(t / 0.25f);
                var dist = (tick % 2 == 0) ? 10.0f : 10.5f;
                return new Vector3(dist, 0f, 0f);
            });
            var b = SimPlayer.Parametric(502, t =>
            {
                var tick = (int)Math.Round(t / 0.25f);
                var dist = (tick % 2 == 0) ? 10.5f : 10.0f;
                return new Vector3(0f, 0f, dist);
            });

            var sim = new PlayerPathSim()
                .WithDt(0.25f)
                .WithPlayers(a, b);
            sim.Runtime.HasStickyPlayer = false;

            const int n = 80;
            var hist = sim.RunStickyOnly(n, fixedCentroid: Vector3.zero);
            Assert.True(hist[0].HasSticky);
            var initial = hist[0].StickyId;
            Assert.True(initial == 501L || initial == 502L);

            var flaps = PlayerPathSim.CountStickyFlaps(hist);
            Assert.True(flaps == 0,
                $"cross-player leapfrog (<{hysteresis}m delta) caused {flaps} sticky id changes — must be 0");
            Assert.True(hist.All(h => h.StickyId == initial),
                $"sticky id drifted from {initial} under sub-hysteresis leapfrog torture");
        }

        [Fact]
        public void Sticky_teleport_disappear_keep_last_then_reclaim_when_far()
        {
            // Documents keep-last (empty scanner) vs reclaim (sticky OOR / missing) invariants.
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            PluginConfig.AmbushOuterPocket.Value = 18f;
            PluginConfig.DiscoveryRadius.Value = 64f;
            // maxRange = Max(outer*2, discovery) = 64m

            var a = SimPlayer.Parametric(101, t =>
            {
                // Present near origin, then after vanish window reappears far (within range),
                // then teleports beyond maxRange.
                if (t < 2f)
                    return new Vector3(12f, 0f, 0f);
                if (t < 6f)
                    return new Vector3(12f, 0f, 0f); // vanished via filter — path unused
                if (t < 10f)
                    return new Vector3(50f, 0f, 0f); // within 64m — refresh position, keep id
                return new Vector3(120f, 0f, 0f); // beyond maxRange
            });
            var b = SimPlayer.Parametric(202, t => new Vector3(8f, 0f, 5f));

            // Visibility: t in [2,6) → nobody; t in [6,10) → A only; t>=10 → A+B
            // Before vanish seed A alone.
            var sim = new PlayerPathSim()
                .WithDt(0.25f)
                .WithPlayers(a, b)
                .WithPlayerVisible((id, t, tick) =>
                {
                    if (t < 2f)
                        return id == 101L; // seed phase: A only
                    if (t < 6f)
                        return false; // full disappear
                    if (t < 10f)
                        return id == 101L; // A reappears alone far-but-in-range
                    return true; // A beyond range + B nearby → reclaim B
                });

            var hist = sim.RunStickyOnly(56, fixedCentroid: Vector3.zero);
            Assert.True(hist.Count >= 40);

            // Phase seed: sticky A
            var seed = hist.TakeWhile(h => h.Time < 2f).ToList();
            Assert.NotEmpty(seed);
            Assert.True(seed.All(h => h.HasSticky && h.StickyId == 101L),
                "seed phase must lock sticky on A");

            // Phase vanish: empty candidates → keep-last id AND last known position
            var vanish = hist.Where(h => h.Time >= 2f && h.Time < 6f).ToList();
            Assert.NotEmpty(vanish);
            Assert.True(vanish.All(h => h.HasSticky && h.StickyId == 101L),
                "INVARIANT: empty scanner must keep-last sticky id (not clear)");
            var lastSeedPos = seed[seed.Count - 1].StickyPosition;
            Assert.True(vanish.All(h =>
                    Math.Abs(h.StickyPosition.x - lastSeedPos.x) < 0.05f
                    && Math.Abs(h.StickyPosition.z - lastSeedPos.z) < 0.05f),
                $"INVARIANT: empty scanner must keep-last sticky position "
                + $"(expected ~({lastSeedPos.x:F1},{lastSeedPos.z:F1}), got vanish drift)");

            // Phase reappear in-range: refresh position, keep id A
            var reappear = hist.Where(h => h.Time >= 6f && h.Time < 10f).ToList();
            Assert.NotEmpty(reappear);
            Assert.True(reappear.All(h => h.StickyId == 101L),
                "in-range reappear of sticky id must not reclaim a different player");
            Assert.True(reappear.All(h => h.StickyPosition.x > 40f),
                "sticky position must refresh to far in-range reappear (~50m)");

            // Phase OOR + B present: stickyDist > maxRange → reclaim nearest (B)
            var oor = hist.Where(h => h.Time >= 10f).ToList();
            Assert.NotEmpty(oor);
            Assert.True(oor.Any(h => h.StickyId == 202L),
                "INVARIANT: sticky beyond maxRange must reclaim nearest live candidate (B)");
            Assert.Equal(202L, oor[oor.Count - 1].StickyId);
            Assert.True(oor[oor.Count - 1].StickyPosition.x < 15f,
                "reclaimed sticky position must track B near origin, not A at 120m");
        }

        [Fact]
        public void Roman_pack_rapid_player_direction_changes_PreferRun_zero_diameter_bounded()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;

            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 5, "Skeleton");
            for (int i = 0; i < squad.Members.Count; i++)
                squad.Members[i].Position = new Vector3(i * 2.5f, 0f, (i % 2) * 1.5f);

            // Rapid lateral zig-zag while advancing +Z — adversarial facing churn.
            var player = SimPlayer.Parametric(77, t =>
            {
                var zig = ((int)(t / 0.5f) % 2 == 0) ? 12f : -12f;
                return new Vector3(zig, 0f, 8f + t * 5f);
            });

            var runtime = new SquadRuntimeState { RomanPhase = RomanPhase.PressContact };
            var sim = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(runSpeed: 7f, walkSpeed: 3.5f)
                .WithMemberStepping(true)
                .WithSquad(squad, runtime)
                .WithPlayers(player)
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Advance,
                    Formation = FormationType.ShieldWall,
                    Stance = StanceType.Aggressive,
                });

            var hist = sim.Run(64);
            var violations = PlayerPathSim.TotalPreferRunViolations(hist);
            Assert.True(violations == 0,
                $"PreferRun != !HoldGround on {violations} member-ticks under rapid direction changes");

            foreach (var h in hist)
            {
                foreach (var m in h.Members.Where(x => x.HasIntent))
                {
                    Assert.True(m.PreferRun,
                        $"tick {h.TickIndex} member {m.InstanceId}: PreferRun false during Advance zig-zag");
                    Assert.False(m.HoldGround,
                        $"tick {h.TickIndex} member {m.InstanceId}: HoldGround planted during Advance zig-zag");
                }
            }

            var d0 = hist[0].PackDiameter;
            var dMax = PlayerPathSim.MaxPackDiameter(hist);
            // HARD: diameter must not explode unboundedly under facing churn (≤ d0 + 12m slack,
            // or ≤ 28m absolute for already-tight packs).
            var bound = Math.Max(28f, d0 + 12f);
            Assert.True(dMax <= bound + 0.01f,
                $"INVARIANT GAP: pack diameter exploded under rapid player zig-zag "
                + $"(d0={d0:F1} dMax={dMax:F1} bound={bound:F1}) — facing/FormUp cohesion breach");
        }

        [Fact]
        public void Ambush_Flank_Kite_oscillation_circle_orbit_band_and_PreferRun()
        {
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            PluginConfig.AmbushOuterPocket.Value = 18f;
            PluginConfig.AmbushInnerBand.Value = 8f;

            var registry = DoctrinePackRegistry.CreateDefault();
            var ambush = registry.GetById("ambush")!;
            var squad = FakeSnapshots.MakeSquad(ambush, 5, "Greydwarf");
            for (int i = 0; i < squad.Members.Count; i++)
            {
                var ang = i * (Math.PI * 2.0 / 5.0);
                squad.Members[i].Position = new Vector3(
                    14f * (float)Math.Cos(ang), 0f, 14f * (float)Math.Sin(ang));
            }

            // Player walks a circle; packs orbit sticky.
            var player = SimPlayer.Parametric(88, t =>
            {
                var ang = t * 0.55f;
                return new Vector3(20f * (float)Math.Cos(ang), 0f, 20f * (float)Math.Sin(ang));
            });

            var runtime = new SquadRuntimeState();
            var sim = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(runSpeed: 8f, walkSpeed: 4f)
                .WithMemberStepping(true)
                .WithSquad(squad, runtime)
                .WithPlayers(player)
                .WithOrderProvider((s, t, tick) =>
                {
                    // Oscillate Flank ↔ Kite every 4 ticks (adversarial order churn).
                    var kite = (tick / 4) % 2 == 1;
                    return new SquadOrder
                    {
                        OrderKind = kite ? DoctrineOrderKind.Kite : DoctrineOrderKind.Flank,
                        Formation = kite ? FormationType.Skirmish : FormationType.Orb,
                        Stance = StanceType.Aggressive,
                    };
                });

            var hist = sim.Run(80);
            Assert.True(hist.All(h => h.HasSticky && h.StickyId == 88L),
                "single circling player must keep sticky id under Flank/Kite oscillation");

            var flankTicks = hist.Count(h => h.OrderKind == DoctrineOrderKind.Flank);
            var kiteTicks = hist.Count(h => h.OrderKind == DoctrineOrderKind.Kite);
            Assert.True(flankTicks >= 10 && kiteTicks >= 10,
                $"oscillation too weak: Flank={flankTicks} Kite={kiteTicks}");

            var violations = PlayerPathSim.TotalPreferRunViolations(hist);
            Assert.True(violations == 0,
                $"PreferRun violations={violations} under Flank/Kite oscillation (must be 0)");

            // Skip settle ticks; orbit band around sticky must hold through order flips.
            var steady = hist.Skip(8).ToList();
            foreach (var h in steady)
            {
                Assert.True(h.MeanDesiredToSticky < 24f,
                    $"tick {h.TickIndex} order={h.OrderKind}: mean slot→sticky "
                    + $"{h.MeanDesiredToSticky:F2}m exceeds 24m orbit band under Flank/Kite flip");
            }

            // PreferRun true whenever not HoldGround (already covered by violations), plus
            // Kite ticks must not plant the whole pack.
            var kiteHoldFraction = steady
                .Where(h => h.OrderKind == DoctrineOrderKind.Kite)
                .SelectMany(h => h.Members.Where(m => m.HasIntent))
                .DefaultIfEmpty()
                .Average(m => m.HasIntent && m.HoldGround ? 1.0 : 0.0);
            Assert.True(kiteHoldFraction < 0.75,
                $"INVARIANT GAP: Kite oscillation planted HoldGround on {kiteHoldFraction:P0} of intents "
                + "(orbit froze instead of skirmish)");
        }

        [Fact]
        public void Two_Ambush_packs_independent_sticky_leapfrog_flaps_zero()
        {
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            const float hysteresis = 10f;

            // Pack A near origin, pack B far east — each has its own runtime sticky.
            var centroidA = new Vector3(0f, 0f, 0f);
            var centroidB = new Vector3(80f, 0f, 0f);

            // One shared moving player path both packs see, plus leapfrog pair for flap torture.
            var mover = SimPlayer.Parametric(900, t =>
                new Vector3(40f + t * 2f, 0f, 0f)); // crosses between packs

            // Sub-hysteresis leapfrog near A (delta 0.5m << 10m hysteresis).
            var leapA1 = SimPlayer.Parametric(501, t =>
            {
                var tick = (int)Math.Round(t / 0.25f);
                var dist = (tick % 2 == 0) ? 10.0f : 10.5f;
                return new Vector3(dist, 0f, 0f);
            });
            var leapA2 = SimPlayer.Parametric(502, t =>
            {
                var tick = (int)Math.Round(t / 0.25f);
                var dist = (tick % 2 == 0) ? 10.5f : 10.0f;
                return new Vector3(0f, 0f, dist);
            });
            // Sub-hysteresis leapfrog near B.
            var leapB1 = SimPlayer.Parametric(601, t =>
            {
                var tick = (int)Math.Round(t / 0.25f);
                var dist = (tick % 2 == 0) ? 10.0f : 10.5f;
                return centroidB + new Vector3(dist, 0f, 0f);
            });
            var leapB2 = SimPlayer.Parametric(602, t =>
            {
                var tick = (int)Math.Round(t / 0.25f);
                var dist = (tick % 2 == 0) ? 10.5f : 10.0f;
                return centroidB + new Vector3(0f, 0f, dist);
            });

            // --- Phase 1: one moving player, two packs — independent sticky acquisition ---
            var runtimeA = new SquadRuntimeState();
            var runtimeB = new SquadRuntimeState();
            var simMover = new PlayerPathSim()
                .WithDt(0.5f)
                .WithPlayers(mover);

            // Drive both runtimes from the same samples (independent UpdateAmbushStickyAnchor).
            var histA = new List<TickMetrics>();
            var histB = new List<TickMetrics>();
            float t = 0f;
            for (int i = 0; i < 40; i++)
            {
                var players = simMover.SamplePlayers(t, i);
                OrderApplicator.UpdateAmbushStickyAnchor(runtimeA, centroidA, players, 0.5f);
                OrderApplicator.UpdateAmbushStickyAnchor(runtimeB, centroidB, players, 0.5f);
                histA.Add(new TickMetrics
                {
                    TickIndex = i,
                    Time = t,
                    HasSticky = runtimeA.HasStickyPlayer,
                    StickyId = runtimeA.StickyPlayerId,
                    StickyPosition = runtimeA.StickyPlayerPosition,
                    PackCentroid = centroidA,
                });
                histB.Add(new TickMetrics
                {
                    TickIndex = i,
                    Time = t,
                    HasSticky = runtimeB.HasStickyPlayer,
                    StickyId = runtimeB.StickyPlayerId,
                    StickyPosition = runtimeB.StickyPlayerPosition,
                    PackCentroid = centroidB,
                });
                t += 0.5f;
            }

            Assert.True(histA.All(h => h.HasSticky && h.StickyId == 900L),
                "pack A must independently sticky-lock the sole mover");
            Assert.True(histB.All(h => h.HasSticky && h.StickyId == 900L),
                "pack B must independently sticky-lock the sole mover (separate runtime)");
            // Positions must track the same player but are independent state objects.
            Assert.True(histA[histA.Count - 1].StickyPosition.x > 40f);
            Assert.True(Math.Abs(
                    histA[histA.Count - 1].StickyPosition.x
                    - histB[histB.Count - 1].StickyPosition.x) < 0.05f,
                "both packs refresh sticky position from the same mover");

            // --- Phase 2: leapfrog flap torture per pack (independent) ---
            var simA = new PlayerPathSim()
                .WithDt(0.25f)
                .WithPlayers(leapA1, leapA2);
            var histLeapA = simA.RunStickyOnly(80, fixedCentroid: centroidA);
            var flapsA = PlayerPathSim.CountStickyFlaps(histLeapA);
            Assert.True(flapsA == 0,
                $"pack A leapfrog (<{hysteresis}m) caused {flapsA} sticky flaps — must be 0");

            var simB = new PlayerPathSim()
                .WithDt(0.25f)
                .WithPlayers(leapB1, leapB2);
            var histLeapB = simB.RunStickyOnly(80, fixedCentroid: centroidB);
            var flapsB = PlayerPathSim.CountStickyFlaps(histLeapB);
            Assert.True(flapsB == 0,
                $"pack B leapfrog (<{hysteresis}m) caused {flapsB} sticky flaps — must be 0");

            // Independence: pack A sticky id is from {501,502}, pack B from {601,602}.
            var idA = histLeapA[0].StickyId;
            var idB = histLeapB[0].StickyId;
            Assert.True(idA == 501L || idA == 502L, $"pack A unexpected sticky {idA}");
            Assert.True(idB == 601L || idB == 602L, $"pack B unexpected sticky {idB}");
            Assert.True(histLeapA.All(h => h.StickyId == idA));
            Assert.True(histLeapB.All(h => h.StickyId == idB));
        }

        [Fact]
        public void Magnet_race_far_straggler_vs_sprinting_player_Desired_lattice_PreferRun()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;

            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 5, "Skeleton");
            // Tight lattice near origin; one member far behind.
            for (int i = 0; i < 4; i++)
                squad.Members[i].Position = new Vector3(i * 2f, 0f, 0f);
            squad.Members[4].Position = new Vector3(-40f, 0f, 0f);
            var farId = squad.Members[4].InstanceId;

            // Player sprints away +Z — pack must chase while magnet snaps straggler to lattice.
            var player = SimPlayer.Parametric(3, t => new Vector3(4f, 0f, 5f + t * 9f));

            var runtime = new SquadRuntimeState { RomanPhase = RomanPhase.PressContact };
            var sim = new PlayerPathSim()
                .WithDt(0.25f)
                .WithSpeeds(runSpeed: 8f, walkSpeed: 3.5f)
                .WithMemberStepping(true)
                .WithSquad(squad, runtime)
                .WithPlayers(player)
                .WithOrder(new SquadOrder
                {
                    OrderKind = DoctrineOrderKind.Advance,
                    Formation = FormationType.ShieldWall,
                    Stance = StanceType.Aggressive,
                });

            var hist = sim.Run(48);
            var violations = PlayerPathSim.TotalPreferRunViolations(hist);
            Assert.True(violations == 0,
                $"PreferRun violations={violations} during magnet race (must be 0)");

            // Every tick: far member PreferRun true and DesiredPosition lattice-side
            // (near pack centroid / formation, not chasing player solo past the wall).
            foreach (var h in hist)
            {
                var far = h.Members.First(m => m.InstanceId == farId);
                Assert.True(far.HasIntent, $"tick {h.TickIndex}: far member missing intent");
                Assert.True(far.PreferRun,
                    $"tick {h.TickIndex}: far member PreferRun false during magnet race");
                Assert.False(far.HoldGround,
                    $"tick {h.TickIndex}: far member HoldGround planted during magnet race");

                var desiredToCentroid = Vector3.Distance(far.DesiredPosition, h.PackCentroid);
                // Magnet rule: when distToSlot > FormUpMagnetDistance, DesiredPosition = slot.
                // Slot stays near formation origin (centroid±anchor). HARD: Desired within 18m of centroid.
                Assert.True(desiredToCentroid < 18f,
                    $"tick {h.TickIndex}: INVARIANT GAP magnet race — far Desired "
                    + $"{desiredToCentroid:F1}m from centroid (expected lattice-side <18m); "
                    + $"desired=({far.DesiredPosition.x:F1},{far.DesiredPosition.z:F1}) "
                    + $"centroid=({h.PackCentroid.x:F1},{h.PackCentroid.z:F1}) "
                    + $"playerZ={h.StickyPosition.z:F1}");
            }

            // Late: far member should have closed distance toward the pack (stepping toward magnet slot).
            var earlyFar = hist[0].Members.First(m => m.InstanceId == farId);
            var lateFar = hist[hist.Count - 1].Members.First(m => m.InstanceId == farId);
            var earlyGap = Vector3.Distance(earlyFar.Position, hist[0].PackCentroid);
            var lateGap = Vector3.Distance(lateFar.Position, hist[hist.Count - 1].PackCentroid);
            Assert.True(lateGap + 1f < earlyGap,
                $"far straggler did not close on pack (earlyGap={earlyGap:F1} lateGap={lateGap:F1}) "
                + "— magnet Desired not translating into chase");
        }

        /// <summary>
        /// Hard regression: hysteresis+ closer must use explicit dt (not null→TickInterval);
        /// interrupted dwell and mid-dwell candidate change reset; sustained still switches.
        /// </summary>
        [Fact]
        public void Sticky_requires_sustained_hysteresis_breach_not_single_spike()
        {
            PluginConfig.AmbushAnchorHysteresis.Value = 10f;
            PluginConfig.AmbushStickySwitchDwellSeconds.Value = 1.0f;
            const float dt = 0.25f;
            var state = new SquadRuntimeState();
            var centroid = Vector3.zero;

            OrderApplicator.UpdateAmbushStickyAnchor(state, centroid, new List<(long, Vector3)>
            {
                (101, new Vector3(20f, 0f, 0f)),
                (202, new Vector3(25f, 0f, 0f)),
            }, dt);
            Assert.Equal(101L, state.StickyPlayerId);

            // Interrupted dwell: B closer for one tick then retreats — must clear candidate.
            OrderApplicator.UpdateAmbushStickyAnchor(state, centroid, new List<(long, Vector3)>
            {
                (101, new Vector3(20f, 0f, 0f)),
                (202, new Vector3(5f, 0f, 0f)),
            }, dt);
            Assert.Equal(101L, state.StickyPlayerId);
            Assert.Equal(202L, state.StickySwitchCandidateId);
            Assert.True(state.StickySwitchCandidateSeconds > 0f);

            OrderApplicator.UpdateAmbushStickyAnchor(state, centroid, new List<(long, Vector3)>
            {
                (101, new Vector3(20f, 0f, 0f)),
                (202, new Vector3(22f, 0f, 0f)),
            }, dt);
            Assert.Equal(101L, state.StickyPlayerId);
            Assert.Equal(0L, state.StickySwitchCandidateId);
            Assert.Equal(0f, state.StickySwitchCandidateSeconds);

            // Mid-dwell candidate change: arm on B, then C becomes nearer — dwell resets to C.
            for (int i = 0; i < 3; i++)
            {
                OrderApplicator.UpdateAmbushStickyAnchor(state, centroid, new List<(long, Vector3)>
                {
                    (101, new Vector3(20f, 0f, 0f)),
                    (202, new Vector3(5f, 0f, 0f)),
                }, dt);
            }
            Assert.Equal(101L, state.StickyPlayerId);
            Assert.Equal(202L, state.StickySwitchCandidateId);
            Assert.True(state.StickySwitchCandidateSeconds >= 0.75f - 1e-3f);

            OrderApplicator.UpdateAmbushStickyAnchor(state, centroid, new List<(long, Vector3)>
            {
                (101, new Vector3(20f, 0f, 0f)),
                (202, new Vector3(8f, 0f, 0f)),
                (303, new Vector3(4f, 0f, 0f)),
            }, dt);
            Assert.Equal(101L, state.StickyPlayerId);
            Assert.Equal(303L, state.StickySwitchCandidateId);
            Assert.True(state.StickySwitchCandidateSeconds <= dt + 1e-3f,
                "candidate change must reset dwell accumulation");

            // Sustained C for full dwell from reset → one switch.
            for (int i = 0; i < 4; i++)
            {
                OrderApplicator.UpdateAmbushStickyAnchor(state, centroid, new List<(long, Vector3)>
                {
                    (101, new Vector3(20f, 0f, 0f)),
                    (303, new Vector3(4f, 0f, 0f)),
                }, dt);
            }
            Assert.Equal(303L, state.StickyPlayerId);
        }

        private static SquadUnit CloneSquad(SquadUnit src, string squadId)
        {
            var clone = new SquadUnit
            {
                SquadId = squadId,
                Doctrine = src.Doctrine,
            };
            foreach (var m in src.Members)
            {
                clone.Members.Add(new SquadMemberView
                {
                    InstanceId = m.InstanceId,
                    PrefabName = m.PrefabName,
                    Position = m.Position,
                    IsAlive = m.IsAlive,
                    HealthRatio = m.HealthRatio,
                    LooksLikeMissile = m.LooksLikeMissile,
                    LooksLikeHeavy = m.LooksLikeHeavy,
                    LooksLikeLeader = m.LooksLikeLeader,
                    LooksLikeFlanker = m.LooksLikeFlanker,
                    AssignedRole = m.AssignedRole,
                });
            }
            return clone;
        }
    }
}
