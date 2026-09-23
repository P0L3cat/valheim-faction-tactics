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
            var core = Vector3.zero;
            int cn = 0;
            for (int i = 0; i < 3; i++)
            {
                core += squad.Members[i].Position;
                cn++;
            }
            core /= cn;
            var distToCore = Vector3.Distance(intent.DesiredPosition, core);
            Assert.True(distToCore < 15f,
                $"Hold magnet Desired not near pack core dist={distToCore:F1} "
                + $"desired=({intent.DesiredPosition.x:F1},{intent.DesiredPosition.z:F1})");
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
            var distToSticky = Vector3.Distance(intent.DesiredPosition, runtime.StickyPlayerPosition);
            Assert.True(distToSticky < 18f,
                $"Ambush Flank magnet Desired not near sticky/slot dist={distToSticky:F1}");
        }

        [Fact]
        public void Zero_magnet_distance_leaves_far_member_unsnapped()
        {
            // FocusFire + far DebugThreat: without magnet Desired stays on threat; magnet>0 would snap to slot.
            PluginConfig.FormUpMagnetDistance.Value = 0f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var rush = registry.GetById("death-rush")!;
            var squad = FakeSnapshots.MakeSquad(rush, 4, "Greyling");
            squad.Members[3].Position = new Vector3(40f, 0f, 0f);
            var farThreat = new Vector3(90f, 0f, 0f);
            squad.DebugThreatPosition = farThreat;

            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.FocusFire,
                Formation = FormationType.Wedge,
                Stance = StanceType.Aggressive,
            }, new SquadRuntimeState());

            Assert.True(OrderApplicator.Intents.TryGetValue(squad.Members[3].InstanceId, out var intent));
            Assert.Equal(!intent.HoldGround, intent.PreferRun);
            // FormUpMagnetDistance=0: Apply must not teleport the body; member stays far from pack core.
            Assert.True(squad.Members[3].Position.x > 35f,
                "magnet=0 must leave far member Position unsnapped (Apply does not teleport)");
            var core = Vector3.zero;
            int cn = 0;
            for (int i = 0; i < 3; i++)
            {
                core += squad.Members[i].Position;
                cn++;
            }
            core /= cn;
            var bodyToCore = Vector3.Distance(squad.Members[3].Position, core);
            Assert.True(bodyToCore > 20f,
                $"magnet=0 far member body still near core? dist={bodyToCore:F1}");
            // Desired also unsnapped: stays on injected threat, not formation slot/core.
            var desiredToThreat = Vector3.Distance(intent.DesiredPosition, farThreat);
            var desiredToCore = Vector3.Distance(intent.DesiredPosition, core);
            Assert.True(desiredToThreat < 5f,
                $"magnet=0 Desired should stay on threat dist={desiredToThreat:F1} "
                + $"desired=({intent.DesiredPosition.x:F1},{intent.DesiredPosition.z:F1})");
            Assert.True(desiredToCore > 20f,
                $"magnet=0 Desired must not snap to slot/core dist={desiredToCore:F1}");
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

            // Tick already ran MergeStragglers once — do NOT call MergeStragglers again on the same
            // mutable squads (would double-absorb members). Assert via ActiveSquads + unique ids.
            Assert.Single(director.ActiveSquads);
            var active = director.ActiveSquads[0];
            Assert.Contains(active.Members, m => m.InstanceId == dropout.Members[0].InstanceId);
            var ids = active.Members.Select(m => m.InstanceId).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());

            Assert.True(OrderApplicator.Intents.TryGetValue(dropout.Members[0].InstanceId, out var absorbed),
                "dropout inside merge radius must reattach and receive intents same tick");
            Assert.True(absorbed.PreferRun, "absorbed member must PreferRun after merge reattach");
            Assert.False(absorbed.HoldGround);
            var parentCentroid = SquadDirector.ComputeSquadCentroid(active);
            var desiredToParent = Vector3.Distance(absorbed.DesiredPosition, parentCentroid);
            Assert.True(desiredToParent < 20f,
                $"absorbed Desired not near parent centroid dist={desiredToParent:F1} "
                + $"desired=({absorbed.DesiredPosition.x:F1},{absorbed.DesiredPosition.z:F1})");
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
            var core = Vector3.zero;
            int cn = 0;
            for (int i = 0; i < 4; i++)
            {
                core += squad.Members[i].Position;
                cn++;
            }
            core /= cn;
            var distToCore = Vector3.Distance(intent.DesiredPosition, core);
            Assert.True(distToCore < 15f,
                $"ProtectMissiles magnet Desired not near pack core dist={distToCore:F1}");
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

        /// <summary>
        /// Charge is excluded from formUpOrder — FormUp magnet must not override Charge Desired to slot.
        /// Inject far DebugThreat so Desired would differ from slot; assert Desired near threat / far from core.
        /// </summary>
        [Fact]
        public void Charge_is_excluded_from_FormUp_magnet()
        {
            PluginConfig.FormUpMagnetDistance.Value = 3.5f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var rush = registry.GetById("death-rush")!;
            var squad = FakeSnapshots.MakeSquad(rush, 4, "Greyling");
            squad.Members[3].Position = new Vector3(40f, 0f, 0f);
            var farId = squad.Members[3].InstanceId;
            var farThreat = new Vector3(95f, 0f, 0f);
            squad.DebugThreatPosition = farThreat;

            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Charge,
                Formation = FormationType.Wedge,
                Stance = StanceType.Aggressive,
            }, new SquadRuntimeState());

            Assert.True(OrderApplicator.Intents.TryGetValue(farId, out var intent));
            Assert.Equal(!intent.HoldGround, intent.PreferRun);
            Assert.True(squad.Members[3].Position.x > 35f);
            var core = Vector3.zero;
            int cn = 0;
            for (int i = 0; i < 3; i++)
            {
                core += squad.Members[i].Position;
                cn++;
            }
            core /= cn;
            // Charge must chase threat, not FormUp-magnet onto pack slot/core.
            var desiredToThreat = Vector3.Distance(intent.DesiredPosition, farThreat);
            var desiredToCore = Vector3.Distance(intent.DesiredPosition, core);
            Assert.True(desiredToThreat < 5f,
                $"Charge Desired not near injected threat dist={desiredToThreat:F1} "
                + $"desired=({intent.DesiredPosition.x:F1},{intent.DesiredPosition.z:F1})");
            Assert.True(desiredToCore > 20f,
                $"Charge Desired magnet-snapped to core? dist={desiredToCore:F1} "
                + "(contrast FormUp_magnet_snaps_far_member_to_slot_with_PreferRun)");
        }

        [Fact]
        public void Two_undersize_same_doctrine_with_no_parent_stay_vanilla()
        {
            PluginConfig.MinSquadSize.Value = 3;
            PluginConfig.SquadMergeRadius.Value = 40f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var a = FakeSnapshots.MakeSquad(roman, 2, "Skeleton");
            a.SquadId = "roman-a";
            var b = FakeSnapshots.MakeSquad(roman, 2, "Skeleton");
            b.SquadId = "roman-b";
            for (int i = 0; i < b.Members.Count; i++)
                b.Members[i].Position = new Vector3(5f + i, 0f, 0f);

            var merged = SquadDirector.MergeStragglers(new[] { a, b });
            Assert.Equal(2, merged.Count);
            Assert.Equal(2, Assert.Single(merged, s => s.SquadId == a.SquadId).Members.Count);
            Assert.Equal(2, Assert.Single(merged, s => s.SquadId == b.SquadId).Members.Count);
        }

        [Fact]
        public void Merge_at_exact_SquadMergeRadius_boundary_attaches()
        {
            PluginConfig.MinSquadSize.Value = 3;
            PluginConfig.SquadMergeRadius.Value = 40f;
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var parent = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            parent.SquadId = "roman-parent";
            // Parent centroid ~ average of 0..3 spaced by MakeSquad (~0,1,2,3) → ~1.5
            var stray = FakeSnapshots.MakeSquad(roman, 1, "Skeleton");
            stray.SquadId = "roman-stray";
            var parentCentroid = SquadDirector.ComputeSquadCentroid(parent);
            // Place stray centroid exactly at merge radius from parent.
            stray.Members[0].Position = parentCentroid + new Vector3(40f, 0f, 0f);

            var merged = SquadDirector.MergeStragglers(new[] { parent, stray });
            Assert.Single(merged);
            Assert.Contains(merged[0].Members, m => m.InstanceId == stray.Members[0].InstanceId);
            var ids = merged[0].Members.Select(m => m.InstanceId).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }
    }
}
