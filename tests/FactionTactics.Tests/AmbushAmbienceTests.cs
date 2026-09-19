using FactionTactics.Ambience;
using FactionTactics.Commander;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Siege;
using FactionTactics.Squad;
using Xunit;

namespace FactionTactics.Tests
{
    public class AmbushAmbienceTests
    {
        public AmbushAmbienceTests() => TestConfig.EnsureBound();

        [Fact]
        public void Tick_DoesNotThrow_OnStubBuild_WithAmbushSquad()
        {
            PluginConfig.EnableAmbushAmbienceTemp.Value = true;
            var registry = DoctrinePackRegistry.CreateDefault();
            var ambush = registry.GetById("ambush")!;
            var squad = FakeSnapshots.MakeSquad(ambush, 4, "Greydwarf");
            var director = new SquadDirector(
                new FakeDiscovery(squad),
                new ScriptedCommander(registry, new SiegeDirector()),
                registry,
                new NullRoleScorer(),
                new NullActionScorer(),
                new OrderApplicator(),
                new SiegeDirector(),
                new AmbushAmbienceDirector());

            director.Tick(0.75f);
            Assert.NotEmpty(director.ActiveSquads);
        }

        [Fact]
        public void Tick_ClearsForceFlag_WhenDisabled()
        {
            var ambience = new AmbushAmbienceDirector();
            PluginConfig.EnableAmbushAmbienceTemp.Value = false;
            ambience.Tick(System.Array.Empty<SquadUnit>());
            Assert.False(ambience.IsForcingEnvironment);
        }
    }
}
