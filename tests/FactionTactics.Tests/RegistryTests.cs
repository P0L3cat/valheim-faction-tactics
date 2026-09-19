using System.Linq;
using FactionTactics.Doctrine;
using Xunit;

namespace FactionTactics.Tests
{
    public class RegistryTests
    {
        public RegistryTests() => TestConfig.EnsureBound();

        [Fact]
        public void CreateDefault_registers_all_eight_doctrine_packs()
        {
            var registry = DoctrinePackRegistry.CreateDefault();
            var ids = registry.All.Select(p => p.Id).OrderBy(x => x).ToArray();
            var expected = FakeSnapshots.ExpectedDoctrineIds.OrderBy(x => x).ToArray();
            Assert.Equal(expected, ids);
            Assert.All(registry.All, p => Assert.True(p.IsEnabled));
        }

        [Theory]
        [InlineData("roman", "Skeleton")]
        [InlineData("ambush", "Greydwarf")]
        [InlineData("viking-shieldwall", "Draugr")]
        [InlineData("steppe", "Fuling")]
        [InlineData("insect-siege", "Seeker")]
        [InlineData("charred-legion", "Charred")]
        [InlineData("pack-hunters", "Wolf")]
        [InlineData("artillery-jelly", "Blob")]
        public void ResolveByPrefab_maps_families(string doctrineId, string prefab)
        {
            var registry = DoctrinePackRegistry.CreateDefault();
            var pack = registry.ResolveByPrefab(prefab);
            Assert.NotNull(pack);
            Assert.Equal(doctrineId, pack!.Id);
        }

        [Fact]
        public void GetById_is_case_insensitive()
        {
            var registry = DoctrinePackRegistry.CreateDefault();
            Assert.NotNull(registry.GetById("Roman"));
            Assert.NotNull(registry.GetById("VIKING-SHIELDWALL"));
            Assert.Equal("ambush", registry.GetById("Ambush")!.Id);
        }

        [Fact]
        public void Ambush_excludes_Root_and_Greyling()
        {
            var ambush = new AmbushDoctrine();
            Assert.False(ambush.MatchesPrefab("Greydwarf_Root"));
            Assert.False(ambush.MatchesPrefab("Greyling"));
            Assert.True(ambush.MatchesPrefab("Greydwarf_Elite"));
        }
    }
}
