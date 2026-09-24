using System.Collections.Generic;
using FactionTactics.Commander;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Siege;
using FactionTactics.Squad;
using UnityEngine;
using Xunit;

namespace FactionTactics.Tests
{
    public class PhaseCHoldGroundTests
    {
        public PhaseCHoldGroundTests() => TestConfig.EnsureBound();

        [Fact]
        public void Cadence_knobs_are_ft_settable_with_defaults()
        {
            Assert.True(PluginConfig.TryGetKnob("RomanStandoffDistance", out var standoff));
            Assert.True(PluginConfig.TryGetKnob("RomanStandoffHoldMin", out var min));
            Assert.True(PluginConfig.TryGetKnob("RomanStandoffHoldMax", out var max));
            Assert.True(PluginConfig.TryGetKnob("RomanContactSwingRange", out var swing));
            Assert.True(PluginConfig.TryGetKnob("RomanRetreatPauseSeconds", out var pause));
            Assert.True(PluginConfig.TryGetKnob("VikingStandoffDistance", out var viking));
            Assert.True(PluginConfig.TryGetKnob("VikingStandoffHoldMax", out var vikingMax));
            Assert.True(PluginConfig.TryGetKnob("VikingIndoorsStandoff", out var indoors));
            Assert.Equal(20f, (float)standoff!.BoxedValue);
            Assert.Equal(1f, (float)min!.BoxedValue);
            Assert.Equal(15f, (float)max!.BoxedValue);
            Assert.Equal(3.5f, (float)swing!.BoxedValue);
            Assert.Equal(1f, (float)pause!.BoxedValue);
            Assert.Equal(14f, (float)viking!.BoxedValue);
            Assert.Equal(8f, (float)vikingMax!.BoxedValue);
            Assert.Equal(12f, (float)indoors!.BoxedValue);
        }

        [Fact]
        public void RollHoldSeconds_stays_inside_inclusive_integer_range()
        {
            for (var i = 0; i < 40; i++)
            {
                var roman = BandedCadence.RollHoldSeconds(1f, 15f);
                var viking = BandedCadence.RollHoldSeconds(1f, 8f);
                Assert.InRange(roman, 1f, 15f);
                Assert.InRange(viking, 1f, 8f);
                Assert.Equal(roman, (float)System.Math.Round(roman));
                Assert.Equal(viking, (float)System.Math.Round(viking));
            }
        }

        [Fact]
        public void Roman_far_advances_and_fronts_do_not_HoldGround()
        {
            var roman = new RomanDoctrine();
            var snap = FakeSnapshots.WithThreat(FakeSnapshots.Roman(), 40f);
            Assert.Equal(DoctrineOrderKind.Advance, roman.SelectOrder(snap, null));
            Assert.Equal(RomanPhase.ApproachStandoff, snap.RomanPhase);

            var intent = ApplyFront("roman", new RomanDoctrine(), DoctrineOrderKind.Advance, RomanPhase.ApproachStandoff, 40f);
            Assert.False(intent.HoldGround);
            Assert.Equal(DoctrineOrderKind.Advance, intent.OrderKind);
        }

        [Fact]
        public void Roman_standoff_hold_pins_only_near_the_slot()
        {
            var roman = new RomanDoctrine();
            var snap = FakeSnapshots.WithThreat(FakeSnapshots.Roman(), 18f);
            snap.ForcedHoldSeconds = 7f;
            snap.SquadAgeSeconds = 2f;
            Assert.Equal(DoctrineOrderKind.Hold, roman.SelectOrder(snap, DoctrineOrderKind.Advance));
            Assert.Equal(RomanPhase.StandoffHold, snap.RomanPhase);
            Assert.Equal(7f, snap.StandoffHoldDuration);
            Assert.Equal(9f, snap.HoldPhaseDeadline);

            var near = ApplyFront("roman", new RomanDoctrine(), DoctrineOrderKind.Hold, RomanPhase.StandoffHold, 18f);
            Assert.True(near.HoldGround);

            var far = ApplySpreadFront("roman", new RomanDoctrine(), DoctrineOrderKind.Hold, RomanPhase.StandoffHold, 18f);
            Assert.False(far.HoldGround);

            // Approach / press phases do not pin even on the slot.
            var closing = ApplyFront("roman", new RomanDoctrine(), DoctrineOrderKind.Hold, RomanPhase.ApproachStandoff, 18f);
            Assert.False(closing.HoldGround);
            var pressing = ApplyFront("roman", new RomanDoctrine(), DoctrineOrderKind.Advance, RomanPhase.PressContact, 12f);
            Assert.False(pressing.HoldGround);
        }

        [Fact]
        public void Roman_standoff_timer_presses_and_dwell_cannot_trap_it()
        {
            var roman = new RomanDoctrine();
            var snap = FakeSnapshots.WithThreat(FakeSnapshots.Roman(), 18f);
            snap.ForcedHoldSeconds = 1f;
            snap.SquadAgeSeconds = 0f;
            snap.RomanPhase = RomanPhase.ApproachStandoff;
            Assert.Equal(DoctrineOrderKind.Hold, roman.SelectOrder(snap, DoctrineOrderKind.Advance));
            Assert.Equal(RomanPhase.StandoffHold, snap.RomanPhase);

            snap.SquadAgeSeconds = snap.HoldPhaseDeadline;
            Assert.Equal(DoctrineOrderKind.Advance, roman.SelectOrder(snap, DoctrineOrderKind.Hold));
            Assert.Equal(RomanPhase.PressContact, snap.RomanPhase);
            Assert.True(snap.CadenceTimerElapsed);

            var fighting = new SquadSnapshot
            {
                DoctrineId = "roman",
                ThreatCount = 1,
                NearestThreatDistance = 18f,
                CadenceTimerElapsed = true,
            };
            Assert.Equal(
                DoctrineOrderKind.Advance,
                OrderTransition.ApplyMinDwell(
                    "roman", fighting, DoctrineOrderKind.Hold, DoctrineOrderKind.Advance,
                    orderAgeSeconds: 0.2f, minDwellSeconds: 30f));
        }

        [Fact]
        public void Roman_swing_range_holds_and_retreat_pauses_then_presses()
        {
            var roman = new RomanDoctrine();
            var snap = FakeSnapshots.WithThreat(FakeSnapshots.Roman(), 3f);
            snap.RomanPhase = RomanPhase.PressContact;
            snap.SquadAgeSeconds = 10f;
            snap.PreviousThreatDistance = 8f;
            Assert.Equal(DoctrineOrderKind.Hold, roman.SelectOrder(snap, DoctrineOrderKind.Advance));
            Assert.Equal(RomanPhase.ContactHold, snap.RomanPhase);

            var pinned = ApplyFront("roman", new RomanDoctrine(), DoctrineOrderKind.Hold, RomanPhase.ContactHold, 3f);
            Assert.True(pinned.HoldGround);

            snap.NearestThreatDistance = 8f;
            snap.FrontlineThreatDistance = 8f;
            var pause = roman.SelectOrder(snap, DoctrineOrderKind.Hold);
            Assert.Equal(DoctrineOrderKind.Hold, pause);
            Assert.Equal(RomanPhase.RetreatPause, snap.RomanPhase);
            Assert.True(snap.HoldPhaseDeadline > 10f);

            var retreatPin = ApplyFront("roman", new RomanDoctrine(), DoctrineOrderKind.Hold, RomanPhase.RetreatPause, 8f);
            Assert.True(retreatPin.HoldGround);

            snap.SquadAgeSeconds = snap.HoldPhaseDeadline;
            Assert.Equal(DoctrineOrderKind.Advance, roman.SelectOrder(snap, DoctrineOrderKind.Hold));
            Assert.Equal(RomanPhase.PressContact, snap.RomanPhase);
            Assert.True(snap.CadenceTimerElapsed);
        }

        [Fact]
        public void Roman_press_does_not_rehold_inside_the_old_wall()
        {
            var roman = new RomanDoctrine();
            var snap = FakeSnapshots.WithThreat(FakeSnapshots.Roman(), 12f);
            snap.RomanPhase = RomanPhase.PressContact;
            snap.SquadAgeSeconds = 4f;
            snap.PreviousThreatDistance = 14f;
            Assert.Equal(DoctrineOrderKind.Advance, roman.SelectOrder(snap, DoctrineOrderKind.Advance));
            Assert.Equal(RomanPhase.PressContact, snap.RomanPhase);
        }

        [Fact]
        public void Advance_never_HoldGround_even_when_standing_on_the_slot()
        {
            var roman = ApplyFront("roman", new RomanDoctrine(), DoctrineOrderKind.Advance, RomanPhase.StandoffHold, 8f);
            Assert.False(roman.HoldGround);

            var viking = ApplyFront("viking-shieldwall", new VikingShieldWallDoctrine(), DoctrineOrderKind.Advance, RomanPhase.ContactHold, 3f);
            Assert.False(viking.HoldGround);

            var charred = ApplyFront("charred-legion", new CharredLegionDoctrine(), DoctrineOrderKind.Advance, RomanPhase.Idle, 0f);
            Assert.False(charred.HoldGround);

            foreach (var kind in new[]
            {
                DoctrineOrderKind.Charge,
                DoctrineOrderKind.Flank,
                DoctrineOrderKind.Kite,
                DoctrineOrderKind.RetreatAndReform,
            })
            {
                var intent = ApplyFront("roman", new RomanDoctrine(), kind, RomanPhase.ContactHold, 2f);
                Assert.False(intent.HoldGround);
            }
        }

        [Fact]
        public void Ambush_flank_and_kite_never_HoldGround_pause_only_in_harass_band()
        {
            var flank = ApplyFront("ambush", new AmbushDoctrine(), DoctrineOrderKind.Flank, RomanPhase.Idle, 12f, FormationType.Orb);
            var kite = ApplyFront("ambush", new AmbushDoctrine(), DoctrineOrderKind.Kite, RomanPhase.Idle, 6f, FormationType.Orb);
            Assert.False(flank.HoldGround);
            Assert.False(kite.HoldGround);

            var lurk = ApplyFront("ambush", new AmbushDoctrine(), DoctrineOrderKind.Hold, RomanPhase.Idle, 30f, FormationType.ShieldWall);
            Assert.False(lurk.HoldGround);

            var pause = ApplyFront("ambush", new AmbushDoctrine(), DoctrineOrderKind.Hold, RomanPhase.Idle, 12f, FormationType.ShieldWall);
            Assert.True(pause.HoldGround);
        }

        [Fact]
        public void DeathRush_charge_never_HoldGround()
        {
            var rush = ApplyFront("death-rush", new DeathRushDoctrine(), DoctrineOrderKind.Charge, RomanPhase.ContactHold, 2f, FormationType.Wedge);
            Assert.False(rush.HoldGround);
            Assert.True(rush.AllowVanillaChase);

            var idle = ApplyFront("death-rush", new DeathRushDoctrine(), DoctrineOrderKind.Hold, RomanPhase.ContactHold, 0f);
            Assert.False(idle.HoldGround);
        }

        [Fact]
        public void Other_doctrines_HoldGround_only_in_contact_and_near_slot()
        {
            var far = ApplyFront("charred-legion", new CharredLegionDoctrine(), DoctrineOrderKind.Hold, RomanPhase.Idle, 20f);
            Assert.False(far.HoldGround);

            var close = ApplyFront("steppe", new SteppeDoctrine(), DoctrineOrderKind.Hold, RomanPhase.Idle, 3f);
            Assert.True(close.HoldGround);

            var steppeFar = ApplyFront("steppe", new SteppeDoctrine(), DoctrineOrderKind.Hold, RomanPhase.Idle, 12f);
            Assert.False(steppeFar.HoldGround);
        }

        [Fact]
        public void Missiles_and_flankers_never_inherit_roman_HoldGround()
        {
            var doctrine = new RomanDoctrine();
            var squad = new SquadUnit { SquadId = "roman-c", Doctrine = doctrine };
            squad.Members.Add(Front(1, "Skeleton", SquadRole.Missile, Vector3.zero));
            squad.Members.Add(Front(2, "Skeleton", SquadRole.Flanker, new Vector3(2f, 0f, 0f)));
            squad.Members.Add(Front(3, "Skeleton", SquadRole.Front, new Vector3(4f, 0f, 0f)));
            var runtime = new SquadRuntimeState
            {
                StableId = "roman-c",
                DoctrineId = "roman",
                RomanPhase = RomanPhase.ContactHold,
                LastThreatDistance = 3f,
            };
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, HoldOrder(), runtime);

            Assert.True(OrderApplicator.TryGetIntent(1, out var missile));
            Assert.True(OrderApplicator.TryGetIntent(2, out var flanker));
            Assert.False(missile.HoldGround);
            Assert.True(missile.PreferKeepRange);
            Assert.False(flanker.HoldGround);
        }

        [Fact]
        public void Anchor_shift_walks_the_line_to_the_standoff_not_past_it()
        {
            var centroid = Vector3.zero;
            var threat = new Vector3(40f, 0f, 0f);
            var shift = OrderApplicator.ComputeAnchorShift(centroid, threat, 20f);
            Assert.InRange(shift.x, 19.5f, 20.5f);
            Assert.Equal(0f, shift.y);
            Assert.Equal(0f, shift.z);

            var already = OrderApplicator.ComputeAnchorShift(new Vector3(22f, 0f, 0f), threat, 20f);
            Assert.Equal(Vector3.zero, already);
        }

        [Fact]
        public void Viking_indoors_uses_tighter_standoff_then_same_press()
        {
            var viking = new VikingShieldWallDoctrine();
            var open = FakeSnapshots.WithThreat(FakeSnapshots.Viking(), 13f);
            Assert.Equal(DoctrineOrderKind.Hold, viking.SelectOrder(open, null));
            Assert.Equal(RomanPhase.StandoffHold, open.RomanPhase);

            var crypt = FakeSnapshots.WithThreat(FakeSnapshots.Viking(), 13f);
            crypt.IndoorsOrCrypt = true;
            Assert.Equal(DoctrineOrderKind.Advance, viking.SelectOrder(crypt, null));
            Assert.Equal(RomanPhase.ApproachStandoff, crypt.RomanPhase);

            crypt.NearestThreatDistance = 11f;
            crypt.FrontlineThreatDistance = 11f;
            crypt.ForcedHoldSeconds = 2f;
            Assert.Equal(DoctrineOrderKind.Hold, viking.SelectOrder(crypt, DoctrineOrderKind.Advance));
            Assert.Equal(RomanPhase.StandoffHold, crypt.RomanPhase);
            Assert.InRange(crypt.StandoffHoldDuration, 1f, 8f);

            var near = ApplyFront("viking-shieldwall", viking, DoctrineOrderKind.Hold, RomanPhase.StandoffHold, 11f);
            Assert.True(near.HoldGround);
        }

        [Fact]
        public void Director_persists_roman_phase_and_presses_when_the_timer_elapses()
        {
            var roman = new RomanDoctrine();
            var discovery = new RebuildingDiscovery(roman, () => new List<MemberSpec>
            {
                new MemberSpec(1, "Skeleton", new Vector3(0f, 0f, 0f), looksLikeFlanker: false),
                new MemberSpec(2, "Skeleton", new Vector3(2f, 0f, 0f), looksLikeFlanker: false),
                new MemberSpec(3, "Skeleton", new Vector3(4f, 0f, 0f), looksLikeFlanker: false),
                new MemberSpec(4, "Skeleton", new Vector3(6f, 0f, 0f), looksLikeMissile: true, looksLikeFlanker: false),
            })
            {
                DebugThreatCount = 1,
                DebugNearestThreatDistance = 40f,
            };

            var director = new SquadDirector(
                discovery,
                FakeSnapshots.CreateCommander(),
                DoctrinePackRegistry.CreateDefault(),
                new NullRoleScorer(),
                new NullActionScorer(),
                new OrderApplicator(),
                new SiegeDirector());

            director.Tick(0.2f);
            Assert.Equal(DoctrineOrderKind.Advance, director.ActiveSquads[0].CurrentOrder!.OrderKind);
            Assert.Equal(RomanPhase.ApproachStandoff, director.RuntimeStates[0].RomanPhase);

            discovery.DebugNearestThreatDistance = 18f;
            director.Tick(1.3f);
            var state = director.RuntimeStates[0];
            Assert.Equal(DoctrineOrderKind.Hold, director.ActiveSquads[0].CurrentOrder!.OrderKind);
            Assert.Equal(RomanPhase.StandoffHold, state.RomanPhase);
            Assert.InRange(state.StandoffHoldDuration, 1f, 15f);

            // Rebuild (new SquadUnit) must keep the same runtime phase.
            director.Tick(0.05f);
            Assert.Single(director.RuntimeStates);
            Assert.Equal(RomanPhase.StandoffHold, director.RuntimeStates[0].RomanPhase);

            state.HoldPhaseDeadline = state.AgeSeconds;
            director.Tick(0.2f);
            Assert.Equal(DoctrineOrderKind.Advance, director.ActiveSquads[0].CurrentOrder!.OrderKind);
            Assert.Equal(RomanPhase.PressContact, state.RomanPhase);
        }

        private static MemberIntent ApplyFront(
            string doctrineId,
            IDoctrinePack doctrine,
            DoctrineOrderKind kind,
            RomanPhase phase,
            float threatDistance,
            FormationType formation = FormationType.ShieldWall)
        {
            var squad = new SquadUnit
            {
                SquadId = doctrineId + "-c",
                Doctrine = doctrine,
                DebugNearestThreatDistance = threatDistance,
                DebugThreatCount = 1,
            };
            var id = NextId();
            // Front under test + filler flanker so AlwaysThreat ceil(10%) can press without peeling the pin.
            squad.Members.Add(Front(id, "Skeleton", SquadRole.Front, Vector3.zero));
            squad.Members.Add(new SquadMemberView
            {
                InstanceId = NextId(),
                PrefabName = "Skeleton",
                IsAlive = true,
                AssignedRole = SquadRole.Flanker,
                LooksLikeFlanker = true,
                Position = new Vector3(2f, 0f, 2f),
            });
            var runtime = new SquadRuntimeState
            {
                StableId = squad.SquadId,
                DoctrineId = doctrineId,
                RomanPhase = phase,
                LastThreatDistance = threatDistance,
            };
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = kind,
                Formation = formation,
                Stance = StanceType.Defensive,
            }, runtime);
            Assert.True(OrderApplicator.TryGetIntent(id, out var intent));
            return intent;
        }

        private static MemberIntent ApplySpreadFront(
            string doctrineId,
            IDoctrinePack doctrine,
            DoctrineOrderKind kind,
            RomanPhase phase,
            float threatDistance)
        {
            var squad = new SquadUnit { SquadId = doctrineId + "-spread", Doctrine = doctrine };
            var nearId = NextId();
            var farId = NextId();
            squad.Members.Add(Front(nearId, "Skeleton", SquadRole.Front, Vector3.zero));
            squad.Members.Add(Front(farId, "Skeleton", SquadRole.Front, new Vector3(30f, 0f, 0f)));
            var runtime = new SquadRuntimeState
            {
                StableId = squad.SquadId,
                DoctrineId = doctrineId,
                RomanPhase = phase,
                LastThreatDistance = threatDistance,
            };
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, HoldOrder(), runtime);
            Assert.True(OrderApplicator.TryGetIntent(nearId, out var intent));
            return intent;
        }

        private static SquadOrder HoldOrder() => new SquadOrder
        {
            OrderKind = DoctrineOrderKind.Hold,
            Formation = FormationType.ShieldWall,
            Stance = StanceType.Defensive,
        };

        private static SquadMemberView Front(long id, string prefab, SquadRole role, Vector3 position) => new SquadMemberView
        {
            InstanceId = id,
            PrefabName = prefab,
            IsAlive = true,
            AssignedRole = role,
            Position = position,
            LooksLikeMissile = role == SquadRole.Missile,
            LooksLikeFlanker = role == SquadRole.Flanker,
        };

        private static long _nextId = 70_000;

        private static long NextId() => System.Threading.Interlocked.Increment(ref _nextId);
    }
}
