using System.Linq;
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
    public class TheaterCommanderTests
    {
        public TheaterCommanderTests() => TestConfig.EnsureBound();

        [Fact]
        public void TwoPacks_roman_pins_ambush_harasses()
        {
            var roman = View("roman", "roman#1", new Vector3(0f, 0f, 0f), focus: 7);
            var ambush = View("ambush", "ambush#1", new Vector3(16f, 0f, 0f), focus: 7);
            TheaterCommander.Assign(new[] { roman, ambush }, 0.75f);

            Assert.Equal(TheaterRole.Pin, roman.State.TheaterRole);
            Assert.Equal(TheaterRole.Harass, ambush.State.TheaterRole);
            Assert.Equal(7, roman.State.TheaterFocusId);
            Assert.Equal(0f, roman.State.TheaterRoleAgeSeconds);
        }

        [Fact]
        public void TwoRomans_closer_pins_farther_flanks()
        {
            var near = View("roman", "roman#1", new Vector3(2f, 0f, 0f), focus: 1);
            var far = View("roman", "roman#2", new Vector3(24f, 0f, 0f), focus: 1);
            TheaterCommander.Assign(new[] { far, near }, 0.5f);

            Assert.Equal(TheaterRole.Pin, near.State.TheaterRole);
            Assert.Equal(TheaterRole.Flank, far.State.TheaterRole);
        }

        [Fact]
        public void TwoAmbush_closer_harasses_farther_flanks()
        {
            var near = View("ambush", "ambush#1", new Vector3(4f, 0f, 0f), focus: 3);
            var far = View("ambush", "ambush#2", new Vector3(20f, 0f, 0f), focus: 3);
            TheaterCommander.Assign(new[] { far, near }, 0.5f);

            Assert.Equal(TheaterRole.Harass, near.State.TheaterRole);
            Assert.Equal(TheaterRole.Flank, far.State.TheaterRole);
        }

        [Fact]
        public void ThreeRomans_pin_flank_and_far_harass()
        {
            var a = View("roman", "r#1", new Vector3(2f, 0f, 0f), focus: 1);
            var b = View("roman", "r#2", new Vector3(12f, 0f, 0f), focus: 1);
            var c = View("roman", "r#3", new Vector3(28f, 0f, 0f), focus: 1);
            TheaterCommander.Assign(new[] { c, a, b }, 0.2f);

            Assert.Equal(TheaterRole.Pin, a.State.TheaterRole);
            Assert.Equal(TheaterRole.Flank, b.State.TheaterRole);
            Assert.Equal(TheaterRole.Harass, c.State.TheaterRole);
        }

        [Fact]
        public void VikingCloserThanRoman_takes_pin()
        {
            var viking = View("viking-shieldwall", "vik#1", new Vector3(3f, 0f, 0f), focus: 9);
            var roman = View("roman", "rom#1", new Vector3(18f, 0f, 0f), focus: 9);
            TheaterCommander.Assign(new[] { roman, viking }, 0.2f);

            Assert.Equal(TheaterRole.Pin, viking.State.TheaterRole);
            Assert.Equal(TheaterRole.Flank, roman.State.TheaterRole);
        }

        [Fact]
        public void DeathRush_is_exempt_and_does_not_take_pin()
        {
            var rush = View("death-rush", "dr#1", new Vector3(1f, 0f, 0f), focus: 4);
            var roman = View("roman", "rom#1", new Vector3(10f, 0f, 0f), focus: 4);
            var ambush = View("ambush", "amb#1", new Vector3(18f, 0f, 0f), focus: 4);
            rush.State.TheaterRole = TheaterRole.Pin;
            rush.State.TheaterRoleAgeSeconds = 4f;

            TheaterCommander.Assign(new[] { rush, roman, ambush }, 0.2f);

            Assert.Equal(TheaterRole.None, rush.State.TheaterRole);
            Assert.False(rush.State.HasTheaterFocus);
            Assert.Equal(TheaterRole.Pin, roman.State.TheaterRole);
            Assert.Equal(TheaterRole.Harass, ambush.State.TheaterRole);
        }

        [Fact]
        public void DeathRush_plus_one_line_still_pins_the_line()
        {
            var rush = View("death-rush", "dr#1", new Vector3(1f, 0f, 0f), focus: 4);
            var roman = View("roman", "rom#1", new Vector3(8f, 0f, 0f), focus: 4);
            TheaterCommander.Assign(new[] { rush, roman }, 0.2f);

            Assert.Equal(TheaterRole.None, rush.State.TheaterRole);
            Assert.Equal(TheaterRole.Pin, roman.State.TheaterRole);
        }

        [Fact]
        public void DeathRush_plus_one_ambush_harasses()
        {
            var rush = View("death-rush", "dr#1", new Vector3(1f, 0f, 0f), focus: 4);
            var ambush = View("ambush", "amb#1", new Vector3(12f, 0f, 0f), focus: 4);
            TheaterCommander.Assign(new[] { rush, ambush }, 0.2f);

            Assert.Equal(TheaterRole.None, rush.State.TheaterRole);
            Assert.Equal(TheaterRole.Harass, ambush.State.TheaterRole);
        }

        [Fact]
        public void Solo_pack_and_split_focus_get_no_role()
        {
            var solo = View("roman", "rom#1", new Vector3(0f, 0f, 0f), focus: 1);
            var otherPlayer = View("ambush", "amb#1", new Vector3(4f, 0f, 0f), focus: 2);
            TheaterCommander.Assign(new[] { solo, otherPlayer }, 0.2f);

            Assert.Equal(TheaterRole.None, solo.State.TheaterRole);
            Assert.Equal(TheaterRole.None, otherPlayer.State.TheaterRole);
        }

        [Fact]
        public void Outside_coengage_radius_does_not_join()
        {
            var radius = PluginConfig.TheaterCoEngageRadius.Value;
            try
            {
                PluginConfig.TheaterCoEngageRadius.Value = 48f;
                var near = View("roman", "rom#1", new Vector3(4f, 0f, 0f), focus: 1);
                var far = View("ambush", "amb#1", new Vector3(80f, 0f, 0f), focus: 1);
                TheaterCommander.Assign(new[] { near, far }, 0.2f);

                Assert.Equal(TheaterRole.None, near.State.TheaterRole);
                Assert.Equal(TheaterRole.None, far.State.TheaterRole);
            }
            finally
            {
                PluginConfig.TheaterCoEngageRadius.Value = radius;
            }
        }

        [Fact]
        public void Hysteresis_holds_pin_against_a_closer_arrival_until_dwell()
        {
            var dwell = PluginConfig.TheaterRoleDwellSeconds.Value;
            try
            {
                PluginConfig.TheaterRoleDwellSeconds.Value = 2.5f;
                var holder = View("roman", "rom#hold", new Vector3(6f, 0f, 0f), focus: 1);
                var challenger = View("viking-shieldwall", "vik#new", new Vector3(20f, 0f, 0f), focus: 1);
                TheaterCommander.Assign(new[] { holder, challenger }, 0f);
                Assert.Equal(TheaterRole.Pin, holder.State.TheaterRole);
                Assert.Equal(TheaterRole.Flank, challenger.State.TheaterRole);

                // Challenger is now closer, but the dwelling pin must not swap on a 0-age recompute.
                holder.Centroid = new Vector3(22f, 0f, 0f);
                challenger.Centroid = new Vector3(2f, 0f, 0f);
                TheaterCommander.Assign(new[] { holder, challenger }, 0f);

                Assert.Equal(TheaterRole.Pin, holder.State.TheaterRole);
                Assert.Equal(TheaterRole.Flank, challenger.State.TheaterRole);
                Assert.Equal(0f, holder.State.TheaterRoleAgeSeconds);

                TheaterCommander.Assign(new[] { holder, challenger }, 2.5f);
                Assert.Equal(TheaterRole.Flank, holder.State.TheaterRole);
                Assert.Equal(TheaterRole.Pin, challenger.State.TheaterRole);
            }
            finally
            {
                PluginConfig.TheaterRoleDwellSeconds.Value = dwell;
            }
        }

        [Fact]
        public void Duplicate_sticky_pins_keep_the_older_holder()
        {
            var dwell = PluginConfig.TheaterRoleDwellSeconds.Value;
            try
            {
                PluginConfig.TheaterRoleDwellSeconds.Value = 5f;
                var older = View("roman", "rom#old", new Vector3(4f, 0f, 0f), focus: 1);
                var younger = View("roman", "rom#new", new Vector3(8f, 0f, 0f), focus: 1);
                older.State.TheaterRole = TheaterRole.Pin;
                older.State.HasTheaterFocus = true;
                older.State.TheaterFocusId = 1;
                older.State.TheaterRoleAgeSeconds = 3f;
                younger.State.TheaterRole = TheaterRole.Pin;
                younger.State.HasTheaterFocus = true;
                younger.State.TheaterFocusId = 1;
                younger.State.TheaterRoleAgeSeconds = 0.2f;

                TheaterCommander.Assign(new[] { older, younger }, 0f);

                Assert.Equal(TheaterRole.Pin, older.State.TheaterRole);
                Assert.NotEqual(TheaterRole.Pin, younger.State.TheaterRole);
            }
            finally
            {
                PluginConfig.TheaterRoleDwellSeconds.Value = dwell;
            }
        }

        [Fact]
        public void ShapeOrder_flank_turns_ambush_hold_into_flank()
        {
            var snap = new SquadSnapshot
            {
                DoctrineId = "ambush",
                TheaterRole = TheaterRole.Flank,
                ThreatCount = 1,
                NearestThreatDistance = 40f,
            };
            Assert.Equal(DoctrineOrderKind.Flank, TheaterCommander.ShapeOrder(snap, DoctrineOrderKind.Hold));
        }

        [Fact]
        public void ShapeOrder_pin_keeps_roman_cadence_and_harass_kites_in_pocket()
        {
            var pin = new SquadSnapshot
            {
                DoctrineId = "roman",
                TheaterRole = TheaterRole.Pin,
                NearestThreatDistance = 30f,
            };
            Assert.Equal(DoctrineOrderKind.Advance, TheaterCommander.ShapeOrder(pin, DoctrineOrderKind.Advance));
            Assert.Equal(DoctrineOrderKind.Hold, TheaterCommander.ShapeOrder(pin, DoctrineOrderKind.Hold));

            var harass = new SquadSnapshot
            {
                DoctrineId = "ambush",
                TheaterRole = TheaterRole.Harass,
                NearestThreatDistance = 10f,
            };
            Assert.Equal(DoctrineOrderKind.Kite, TheaterCommander.ShapeOrder(harass, DoctrineOrderKind.Flank));

            harass.NearestThreatDistance = 40f;
            Assert.Equal(DoctrineOrderKind.Flank, TheaterCommander.ShapeOrder(harass, DoctrineOrderKind.Hold));
        }

        [Fact]
        public void ShapeOrder_broken_and_deathrush_are_not_rewritten()
        {
            var broken = new SquadSnapshot
            {
                DoctrineId = "roman",
                TheaterRole = TheaterRole.Flank,
                IsBroken = true,
            };
            Assert.Equal(
                DoctrineOrderKind.RetreatAndReform,
                TheaterCommander.ShapeOrder(broken, DoctrineOrderKind.RetreatAndReform));

            var rush = new SquadSnapshot
            {
                DoctrineId = "death-rush",
                TheaterRole = TheaterRole.Pin,
                ThreatCount = 2,
                NearestThreatDistance = 4f,
            };
            Assert.Equal(DoctrineOrderKind.Charge, TheaterCommander.ShapeOrder(rush, DoctrineOrderKind.Charge));
            Assert.Equal(DoctrineOrderKind.Charge, TheaterCommander.ShapeOrder(rush, DoctrineOrderKind.Kite));
        }

        [Fact]
        public void Director_orders_reflect_pin_flank_harass_and_deathrush_charge()
        {
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = Place(FakeSnapshots.MakeSquad(registry.GetById("roman")!, 4, "Skeleton"), new Vector3(0f, 0f, 0f));
            var ambush = Place(FakeSnapshots.MakeSquad(registry.GetById("ambush")!, 4, "Greydwarf"), new Vector3(18f, 0f, 0f));
            var rush = Place(FakeSnapshots.MakeSquad(registry.GetById("death-rush")!, 4, "Greyling"), new Vector3(6f, 0f, 4f));
            var focus = Vector3.zero;
            Arm(roman, focus, threat: 30f);
            Arm(ambush, focus, threat: 10f);
            Arm(rush, focus, threat: 6f);

            var director = Director(registry, roman, ambush, rush);
            director.Tick(0.5f);

            var romanSquad = Active(director, "roman");
            var ambushSquad = Active(director, "ambush");
            var rushSquad = Active(director, "death-rush");
            var romanState = StateFor(director, romanSquad);
            var ambushState = StateFor(director, ambushSquad);
            var rushState = StateFor(director, rushSquad);

            Assert.Equal(TheaterRole.Pin, romanState.TheaterRole);
            Assert.Equal(TheaterRole.Harass, ambushState.TheaterRole);
            Assert.Equal(TheaterRole.None, rushState.TheaterRole);
            Assert.Equal(DoctrineOrderKind.Advance, romanSquad.CurrentOrder!.OrderKind);
            Assert.Equal(DoctrineOrderKind.Kite, ambushSquad.CurrentOrder!.OrderKind);
            Assert.Equal(DoctrineOrderKind.Charge, rushSquad.CurrentOrder!.OrderKind);
        }

        [Fact]
        public void Director_farther_roman_flanks_and_hysteresis_holds_across_a_swap()
        {
            var dwell = PluginConfig.TheaterRoleDwellSeconds.Value;
            try
            {
                PluginConfig.TheaterRoleDwellSeconds.Value = 10f;
                var registry = DoctrinePackRegistry.CreateDefault();
                var near = Place(FakeSnapshots.MakeSquad(registry.GetById("roman")!, 4, "Skeleton"), new Vector3(0f, 0f, 0f));
                var far = Place(FakeSnapshots.MakeSquad(registry.GetById("viking-shieldwall")!, 4, "Draugr"), new Vector3(22f, 0f, 0f));
                var focus = Vector3.zero;
                Arm(near, focus, threat: 30f);
                Arm(far, focus, threat: 30f);

                var director = Director(registry, near, far);
                director.Tick(0.2f);

                var nearSquad = Active(director, "roman");
                var farSquad = Active(director, "viking-shieldwall");
                Assert.Equal(TheaterRole.Pin, StateFor(director, nearSquad).TheaterRole);
                Assert.Equal(TheaterRole.Flank, StateFor(director, farSquad).TheaterRole);
                Assert.Equal(DoctrineOrderKind.Flank, farSquad.CurrentOrder!.OrderKind);

                Place(near, new Vector3(26f, 0f, 0f));
                Place(far, new Vector3(0f, 0f, 0f));
                director.Tick(0.2f);

                Assert.Equal(TheaterRole.Pin, StateFor(director, nearSquad).TheaterRole);
                Assert.Equal(TheaterRole.Flank, StateFor(director, farSquad).TheaterRole);
                Assert.Equal(DoctrineOrderKind.Flank, farSquad.CurrentOrder!.OrderKind);

                director.Tick(10f);
                Assert.Equal(TheaterRole.Flank, StateFor(director, nearSquad).TheaterRole);
                Assert.Equal(TheaterRole.Pin, StateFor(director, farSquad).TheaterRole);
                Assert.NotEqual(DoctrineOrderKind.Flank, farSquad.CurrentOrder!.OrderKind);
                Assert.NotEqual(DoctrineOrderKind.Kite, farSquad.CurrentOrder!.OrderKind);
            }
            finally
            {
                PluginConfig.TheaterRoleDwellSeconds.Value = dwell;
            }
        }

        [Fact]
        public void Applicator_steps_flank_off_the_pin_axis()
        {
            var registry = DoctrinePackRegistry.CreateDefault();
            var pinSquad = FakeSnapshots.MakeSquad(registry.GetById("roman")!, 4, "Skeleton");
            var flankSquad = FakeSnapshots.MakeSquad(registry.GetById("roman")!, 4, "Skeleton");
            var order = new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Advance,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            };
            var pinState = new SquadRuntimeState { TheaterRole = TheaterRole.Pin, RomanPhase = RomanPhase.Idle };
            var flankState = new SquadRuntimeState { TheaterRole = TheaterRole.Flank, RomanPhase = RomanPhase.Idle };

            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(pinSquad, order, pinState);
            var pinXs = pinSquad.Members.Select(m => OrderApplicator.Intents[m.InstanceId].DesiredPosition.x).ToList();

            new OrderApplicator().Apply(flankSquad, order, flankState);
            var flankXs = flankSquad.Members.Select(m => OrderApplicator.Intents[m.InstanceId].DesiredPosition.x).ToList();

            Assert.True(flankXs.Average() > pinXs.Average() + 8f,
                $"flank mean x {flankXs.Average()} should sit well to the right of pin mean x {pinXs.Average()}");
        }

        private static TheaterSquadView View(string doctrineId, string stableId, Vector3 centroid, long focus)
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

        private static SquadUnit Place(SquadUnit squad, Vector3 origin)
        {
            for (int i = 0; i < squad.Members.Count; i++)
                squad.Members[i].Position = origin + new Vector3(i * 2f, 0f, 0f);
            return squad;
        }

        private static void Arm(SquadUnit squad, Vector3 focus, float threat)
        {
            squad.DebugFocusPlayerId = 42;
            squad.DebugFocusPlayerPosition = focus;
            squad.DebugThreatCount = 1;
            squad.DebugNearestThreatDistance = threat;
        }

        private static SquadDirector Director(DoctrinePackRegistry registry, params SquadUnit[] squads)
        {
            return new SquadDirector(
                new FakeDiscovery(squads),
                new ScriptedCommander(registry, new SiegeDirector()),
                registry,
                new NullRoleScorer(),
                new NullActionScorer(),
                new OrderApplicator(),
                new SiegeDirector());
        }

        private static SquadUnit Active(SquadDirector director, string doctrineId)
        {
            return Assert.Single(director.ActiveSquads, s => s.Doctrine.Id == doctrineId);
        }

        private static SquadRuntimeState StateFor(SquadDirector director, SquadUnit squad)
        {
            return Assert.Single(director.RuntimeStates, s => s.StableId == squad.SquadId);
        }
    }
}
