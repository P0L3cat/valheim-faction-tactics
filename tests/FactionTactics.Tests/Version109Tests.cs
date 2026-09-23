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
    public class Version109Tests
    {
        public Version109Tests() => TestConfig.EnsureBound();

        [Fact]
        public void FormUp_magnet_snaps_far_member_to_slot_with_PreferRun()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            // Park one member far away from the squad lattice.
            squad.Members[3].Position = new Vector3(40f, 0f, 0f);

            var runtime = new SquadRuntimeState { RomanPhase = RomanPhase.ApproachStandoff };
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Advance,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Aggressive,
            }, runtime);

            Assert.True(OrderApplicator.Intents.TryGetValue(squad.Members[3].InstanceId, out var intent));
            Assert.False(intent.HoldGround);
            Assert.True(intent.PreferRun);
            // Desired position should be near the formation (centroid ~3), not stay at x=40.
            Assert.True(intent.DesiredPosition.x < 20f,
                $"far member not magnetized into formation x={intent.DesiredPosition.x}");
        }

        [Fact]
        public void Remnant_pack_that_EverMetMinSize_still_gets_orders()
        {
            PluginConfig.MinSquadSize.Value = 3;
            PluginConfig.SquadMergeRadius.Value = 40f;

            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            // Start as a valid pack of 4, then leave only 2 alive on the same squad object
            // after director has marked EverMetMinSize via PeakAlive.
            var squad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            squad.SquadId = "roman-remnant";

            var discovery = new FakeDiscovery(squad);
            var director = new SquadDirector(
                discovery,
                new ScriptedCommander(registry, new SiegeDirector()),
                registry,
                new NullRoleScorer(),
                new NullActionScorer(),
                new OrderApplicator(),
                new SiegeDirector());

            OrderApplicator.Intents.Clear();
            director.Tick(0.75f);
            Assert.Single(director.ActiveSquads);

            // Kill down to 2 — remnant of a formed pack.
            squad.Members[2].IsAlive = false;
            squad.Members[3].IsAlive = false;
            OrderApplicator.Intents.Clear();
            director.Tick(0.75f);

            Assert.Single(director.ActiveSquads);
            Assert.NotNull(director.ActiveSquads[0].CurrentOrder);
            foreach (var m in squad.Members)
            {
                if (!m.IsAlive) continue;
                Assert.True(OrderApplicator.Intents.ContainsKey(m.InstanceId),
                    "remnant member should still receive intents");
            }
        }

        [Fact]
        public void Advancing_line_shares_one_facing_basis()
        {
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

            // All living members PreferRun and not HoldGround while pressing.
            foreach (var m in squad.Members)
            {
                Assert.True(OrderApplicator.Intents.TryGetValue(m.InstanceId, out var intent));
                Assert.True(intent.PreferRun);
                Assert.False(intent.HoldGround);
            }
        }
    }
}
