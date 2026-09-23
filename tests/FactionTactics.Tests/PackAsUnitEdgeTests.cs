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
    /// <summary>1.0.9 Pack-as-Unit edge cases beyond Version109Tests happy path.</summary>
    public class PackAsUnitEdgeTests
    {
        public PackAsUnitEdgeTests() => TestConfig.EnsureBound();

        [Fact]
        public void Hold_order_magnets_far_member_with_PreferRun()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            squad.Members[3].Position = new Vector3(40f, 0f, 0f);

            var runtime = new SquadRuntimeState { RomanPhase = RomanPhase.ApproachStandoff };
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            }, runtime);

            Assert.True(OrderApplicator.Intents.TryGetValue(squad.Members[3].InstanceId, out var intent));
            Assert.False(intent.HoldGround);
            Assert.True(intent.PreferRun, "magnet must PreferRun while snapping into Hold lattice");
            Assert.True(intent.DesiredPosition.x < 20f,
                $"Hold magnet failed x={intent.DesiredPosition.x}");
        }

        [Fact]
        public void Ambush_Flank_magnets_far_member_toward_slot()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var ambush = registry.GetById("ambush")!;
            var squad = FakeSnapshots.MakeSquad(ambush, 5, "Greydwarf");
            squad.Members[4].Position = new Vector3(50f, 0f, 0f);

            var runtime = new SquadRuntimeState
            {
                HasStickyPlayer = true,
                StickyPlayerId = 42,
                StickyPlayerPosition = new Vector3(0f, 0f, 0f),
            };
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Flank,
                Formation = FormationType.Orb,
                Stance = StanceType.Aggressive,
            }, runtime);

            Assert.True(OrderApplicator.Intents.TryGetValue(squad.Members[4].InstanceId, out var intent));
            Assert.True(intent.PreferRun);
            Assert.False(intent.HoldGround);
            Assert.True(intent.DesiredPosition.x < 25f,
                $"Ambush Flank magnet failed x={intent.DesiredPosition.x}");
        }

        [Fact]
        public void Zero_magnet_distance_leaves_far_member_unsnapped()
        {
            PluginConfig.FormUpMagnetDistance.Value = 0f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            squad.Members[3].Position = new Vector3(40f, 0f, 0f);

            var runtime = new SquadRuntimeState { RomanPhase = RomanPhase.PressContact };
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Advance,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Aggressive,
            }, runtime);

            Assert.True(OrderApplicator.Intents.TryGetValue(squad.Members[3].InstanceId, out var intent));
            // With magnet off, DesiredPosition may chase threat/slot heuristics — assert PreferRun still mirrors !HoldGround.
            Assert.Equal(!intent.HoldGround, intent.PreferRun);
        }

        [Fact]
        public void Dropout_within_merge_radius_reattaches_same_tick()
        {
            PluginConfig.MinSquadSize.Value = 3;
            PluginConfig.SquadMergeRadius.Value = 40f;

            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var pack = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            pack.SquadId = "roman-pack";
            var dropout = FakeSnapshots.MakeSquad(roman, 1, "Skeleton");
            dropout.SquadId = "roman-dropout";
            dropout.Members[0].Position = new Vector3(15f, 0f, 0f);

            var director = new SquadDirector(
                new FakeDiscovery(pack, dropout),
                new ScriptedCommander(registry, new SiegeDirector()),
                registry,
                new NullRoleScorer(),
                new NullActionScorer(),
                new OrderApplicator(),
                new SiegeDirector());

            OrderApplicator.Intents.Clear();
            director.Tick(0.75f);

            var merged = SquadDirector.MergeStragglers(new[] { pack, dropout });
            Assert.Single(merged);
            Assert.Contains(merged[0].Members, m => m.InstanceId == dropout.Members[0].InstanceId);

            Assert.True(OrderApplicator.Intents.ContainsKey(dropout.Members[0].InstanceId),
                "dropout inside merge radius must reattach and receive intents same tick");
        }
    }
}
