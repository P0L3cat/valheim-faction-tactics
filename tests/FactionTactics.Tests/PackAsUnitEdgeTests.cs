using FactionTactics.Commander;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Siege;
using FactionTactics.Squad;
using System.Linq;
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

        [Fact]
        public void Cross_doctrine_stragglers_do_not_merge()
        {
            PluginConfig.MinSquadSize.Value = 3;
            PluginConfig.SquadMergeRadius.Value = 40f;

            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var ambush = registry.GetById("ambush")!;
            var parent = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            parent.SquadId = "roman-parent";
            var stray = FakeSnapshots.MakeSquad(ambush, 2, "Greydwarf");
            stray.SquadId = "ambush-stray";
            for (int i = 0; i < stray.Members.Count; i++)
                stray.Members[i].Position = new Vector3(10f + i, 0f, 0f);

            var merged = SquadDirector.MergeStragglers(new[] { parent, stray });
            Assert.Equal(2, merged.Count);
            var parentOut = Assert.Single(merged, s => s.SquadId == parent.SquadId);
            var strayOut = Assert.Single(merged, s => s.SquadId == stray.SquadId);
            Assert.Equal(4, parentOut.Members.Count);
            Assert.Equal(2, strayOut.Members.Count);
            foreach (var m in stray.Members)
                Assert.DoesNotContain(parentOut.Members, p => p.InstanceId == m.InstanceId);
        }

        [Fact]
        public void ProtectMissiles_magnets_far_member_with_PreferRun()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 5, "Skeleton");
            squad.Members[4].Position = new Vector3(45f, 0f, 0f);

            var runtime = new SquadRuntimeState { RomanPhase = RomanPhase.ApproachStandoff };
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.ProtectMissiles,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            }, runtime);

            Assert.True(OrderApplicator.Intents.TryGetValue(squad.Members[4].InstanceId, out var intent));
            Assert.True(intent.PreferRun);
            Assert.False(intent.HoldGround);
            Assert.True(intent.DesiredPosition.x < 20f,
                $"ProtectMissiles magnet failed x={intent.DesiredPosition.x}");
        }

        [Fact]
        public void Advance_assigns_distinct_slot_destinations_for_living_members()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
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

            var dests = new System.Collections.Generic.HashSet<string>();
            foreach (var m in squad.Members)
            {
                Assert.True(OrderApplicator.Intents.TryGetValue(m.InstanceId, out var intent));
                Assert.True(intent.PreferRun);
                dests.Add($"{intent.DesiredPosition.x:F2},{intent.DesiredPosition.z:F2}");
            }
            Assert.True(dests.Count >= 3,
                $"pack should spread across slots, got {dests.Count} unique destinations");
        }
    }
}
