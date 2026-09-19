using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Siege;
using FactionTactics.Squad;
using Xunit;

namespace FactionTactics.Tests
{
    public class SiegeTests
    {
        public SiegeTests() => TestConfig.EnsureBound();

        [Fact]
        public void NearWorkbench_false_means_no_AssaultActive_order()
        {
            var cmd = FakeSnapshots.CreateCommander();
            var snap = FakeSnapshots.WithThreat(FakeSnapshots.Ambush(), 20f);
            snap.NearWorkbench = false;
            snap.NearestWorkbenchDistance = float.MaxValue;
            var order = cmd.Propose(snap);
            Assert.NotNull(order);
            Assert.False(order!.AssaultActive);
            Assert.NotEqual("scripted+siege", order.Source);
        }

        [Theory]
        [InlineData("ambush")]
        [InlineData("viking-shieldwall")]
        public void NearWorkbench_plus_eligible_doctrine_and_size_enters_Assault(string doctrineId)
        {
            var cmd = FakeSnapshots.CreateCommander();
            var snap = doctrineId == "ambush" ? FakeSnapshots.Ambush(4) : FakeSnapshots.Viking(4);
            snap = FakeSnapshots.WithThreat(snap, 20f);
            snap.NearWorkbench = true;
            snap.NearestWorkbenchDistance = 20f;
            snap.PlayersNearAssault = false;
            snap.MemberCount = 4;

            Assert.True(SiegeDirector.ShouldEnterAssault(snap));

            var order = cmd.Propose(snap);
            Assert.NotNull(order);
            Assert.True(order!.AssaultActive);
            Assert.Equal("scripted+siege", order.Source);
            Assert.True(order.AllowVanillaStructure); // quiet
            Assert.Equal(DoctrineOrderKind.Advance, order.OrderKind); // quiet light-touch
        }

        [Fact]
        public void Roman_near_workbench_does_not_enter_Assault()
        {
            var snap = FakeSnapshots.WithThreat(FakeSnapshots.Roman(4), 20f);
            snap.NearWorkbench = true;
            snap.NearestWorkbenchDistance = 10f;
            snap.MemberCount = 4;
            Assert.False(SiegeDirector.ShouldEnterAssault(snap));

            var order = FakeSnapshots.CreateCommander().Propose(snap);
            Assert.False(order!.AssaultActive);
        }

        [Fact]
        public void Assault_size_gate_blocks_small_squads()
        {
            var snap = FakeSnapshots.WithThreat(FakeSnapshots.Ambush(2), 15f);
            snap.NearWorkbench = true;
            snap.NearestWorkbenchDistance = 10f;
            snap.MemberCount = 2;
            Assert.False(SiegeDirector.ShouldEnterAssault(snap));
        }

        [Fact]
        public void Hot_assault_role_split_via_OrderApplicator()
        {
            TestConfig.EnsureBound();
            var registry = DoctrinePackRegistry.CreateDefault();
            var ambush = registry.GetById("ambush")!;
            var squad = FakeSnapshots.MakeSquad(ambush, 4, "Greydwarf");
            // Force role mix: shaman missile + brutes/front
            squad.Members[0].PrefabName = "Greydwarf_Shaman";
            squad.Members[0].AssignedRole = SquadRole.Missile;
            squad.Members[1].PrefabName = "Greydwarf_Elite";
            squad.Members[1].AssignedRole = SquadRole.Front;
            squad.Members[2].AssignedRole = SquadRole.Flanker;
            squad.Members[3].AssignedRole = SquadRole.Leader;

            var order = new SquadOrder
            {
                SquadId = squad.SquadId,
                OrderKind = DoctrineOrderKind.FocusFire,
                Formation = FormationType.Skirmish,
                Stance = StanceType.Aggressive,
                AssaultActive = true,
                AssaultPlayersPresent = true,
                AllowVanillaStructure = false,
                Source = "scripted+siege",
            };

            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, order);

            Assert.True(OrderApplicator.TryGetIntent(squad.Members[0].InstanceId, out var missile));
            Assert.True(missile.AssaultMissileCover);
            Assert.True(missile.PreferKeepRange);
            Assert.False(missile.AssaultWallBreaker);

            Assert.True(OrderApplicator.TryGetIntent(squad.Members[1].InstanceId, out var front));
            Assert.True(front.AssaultWallBreaker);
            Assert.False(front.AssaultMissileCover);
            Assert.True(front.AllowVanillaChase); // hot breacher
        }

        [Fact]
        public void Quiet_assault_wall_breakers_do_not_force_chase()
        {
            var registry = DoctrinePackRegistry.CreateDefault();
            var viking = registry.GetById("viking-shieldwall")!;
            var squad = FakeSnapshots.MakeSquad(viking, 3, "Draugr");
            foreach (var m in squad.Members)
                m.AssignedRole = SquadRole.Front;

            var order = new SquadOrder
            {
                SquadId = squad.SquadId,
                OrderKind = DoctrineOrderKind.Advance,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Aggressive,
                AssaultActive = true,
                AssaultPlayersPresent = false,
                AllowVanillaStructure = true,
                Source = "scripted+siege",
            };

            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, order);

            Assert.True(OrderApplicator.TryGetIntent(squad.Members[0].InstanceId, out var intent));
            Assert.True(intent.AssaultWallBreaker);
            Assert.True(intent.AllowVanillaStructure);
            Assert.False(intent.AllowVanillaChase); // quiet: leave vanilla structure AI
        }
    }
}
