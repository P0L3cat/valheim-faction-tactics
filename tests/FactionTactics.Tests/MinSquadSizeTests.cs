using FactionTactics.Commander;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Siege;
using FactionTactics.Squad;
using Xunit;

namespace FactionTactics.Tests
{
    public class MinSquadSizeTests
    {
        public MinSquadSizeTests() => TestConfig.EnsureBound();

        [Fact]
        public void Small_packs_receive_no_orders_from_SquadDirector()
        {
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var small = FakeSnapshots.MakeSquad(roman, 2, "Skeleton");
            var large = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");

            PluginConfig.MinSquadSize.Value = 3;

            var discovery = new FakeDiscovery(small, large);
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
            Assert.Equal(large.SquadId, director.ActiveSquads[0].SquadId);
            Assert.Null(small.CurrentOrder);
            Assert.NotNull(large.CurrentOrder);
        }
    }
}
