using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Squad;
using Xunit;

namespace FactionTactics.Tests
{
    public class CountAliveTests
    {
        public CountAliveTests() => TestConfig.EnsureBound();

        [Fact]
        public void HealthRatio_zero_is_not_treated_as_dead()
        {
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            foreach (var m in squad.Members)
            {
                m.IsAlive = true;
                m.HealthRatio = 0f; // must NOT dead-proxy (proxy is (0, 0.02])
            }

            Assert.Equal(4, SquadDirector.CountAlive(squad));
        }

        [Fact]
        public void HealthRatio_in_open_interval_dead_proxy_excludes_member()
        {
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            for (int i = 0; i < squad.Members.Count; i++)
            {
                squad.Members[i].IsAlive = true;
                squad.Members[i].HealthRatio = i < 3 ? 0.01f : -1f;
            }

            Assert.Equal(1, SquadDirector.CountAlive(squad));
        }

        [Fact]
        public void Default_negative_HealthRatio_counts_alive()
        {
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 3, "Skeleton");
            // MakeSquad leaves HealthRatio at default -1
            Assert.Equal(3, SquadDirector.CountAlive(squad));
        }
    }
}
