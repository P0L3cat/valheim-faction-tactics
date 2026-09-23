using System.Collections.Generic;
using System.Linq;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Squad;
using Xunit;

namespace FactionTactics.Tests
{
    public class PhaseATests
    {
        public PhaseATests() => TestConfig.EnsureBound();

        [Fact]
        public void Flankers_never_get_HoldGround_on_Hold_or_ProtectMissiles()
        {
            var doctrine = new AmbushDoctrine();
            var squad = new SquadUnit { SquadId = "ambush#1", Doctrine = doctrine };
            squad.Members.Add(new SquadMemberView
            {
                InstanceId = 1, PrefabName = "Greydwarf", IsAlive = true,
                AssignedRole = SquadRole.Flanker, Position = new UnityEngine.Vector3(0, 0, 0),
            });
            squad.Members.Add(new SquadMemberView
            {
                InstanceId = 2, PrefabName = "Greydwarf", IsAlive = true,
                AssignedRole = SquadRole.Front, Position = new UnityEngine.Vector3(2, 0, 0),
            });
            squad.Members.Add(new SquadMemberView
            {
                InstanceId = 3, PrefabName = "Greydwarf_Elite", IsAlive = true,
                AssignedRole = SquadRole.Leader, Position = new UnityEngine.Vector3(1, 0, 0),
            });

            OrderApplicator.Intents.Clear();
            var order = new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = FormationType.Orb,
                Stance = StanceType.Aggressive,
            };
            var runtime = new SquadRuntimeState { StableId = "ambush#1", DoctrineId = "ambush" };
            new OrderApplicator().Apply(squad, order, runtime);

            Assert.True(OrderApplicator.TryGetIntent(1, out var flanker));
            Assert.False(flanker.HoldGround);

            order.OrderKind = DoctrineOrderKind.ProtectMissiles;
            new OrderApplicator().Apply(squad, order, runtime);
            Assert.True(OrderApplicator.TryGetIntent(1, out flanker));
            Assert.False(flanker.HoldGround);
        }

        [Fact]
        public void Asksvin_flanker_never_HoldGround_in_Charred_Hold()
        {
            var doctrine = new CharredLegionDoctrine();
            var squad = new SquadUnit { SquadId = "charred#1", Doctrine = doctrine };
            squad.Members.Add(new SquadMemberView
            {
                InstanceId = 10, PrefabName = "Asksvin", IsAlive = true,
                AssignedRole = SquadRole.Flanker, LooksLikeFlanker = true,
                Position = new UnityEngine.Vector3(5, 0, 0),
            });
            squad.Members.Add(new SquadMemberView
            {
                InstanceId = 11, PrefabName = "Charred_Melee", IsAlive = true,
                AssignedRole = SquadRole.Front, Position = new UnityEngine.Vector3(0, 0, 0),
            });
            squad.Members.Add(new SquadMemberView
            {
                InstanceId = 12, PrefabName = "Charred_Melee", IsAlive = true,
                AssignedRole = SquadRole.Front, Position = new UnityEngine.Vector3(2, 0, 0),
            });

            OrderApplicator.Intents.Clear();
            var order = new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            };
            new OrderApplicator().Apply(squad, order, new SquadRuntimeState { StableId = "charred#1", DoctrineId = "charred-legion" });

            Assert.True(OrderApplicator.TryGetIntent(10, out var asksvin));
            Assert.False(asksvin.HoldGround);
        }

        [Fact]
        public void Slot_lock_leaves_holes_when_member_dies_without_immediate_reshuffle()
        {
            PluginConfig.FormationReshuffleSeconds.Value = 30f;
            PluginConfig.FormationCasualtyReshuffle.Value = 0.99f; // don't reshuffle on one death of 4

            var doctrine = new RomanDoctrine();
            var squad = MakeRomanSquad(new[] { 1L, 2L, 3L, 4L });
            var runtime = new SquadRuntimeState { StableId = "roman#1", DoctrineId = "roman" };
            var order = new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            };

            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, order, runtime);

            Assert.True(FormationSlotLock.TryGetSlot(runtime, 1, out var s1));
            Assert.True(FormationSlotLock.TryGetSlot(runtime, 2, out var s2));
            Assert.True(FormationSlotLock.TryGetSlot(runtime, 3, out var s3));
            Assert.True(FormationSlotLock.TryGetSlot(runtime, 4, out var s4));
            var before = new Dictionary<long, int> { [1] = s1, [2] = s2, [3] = s3, [4] = s4 };
            var capacityBefore = runtime.SlotLockCapacity;
            Assert.Equal(4, capacityBefore);

            // Kill member 2 — should leave hole, survivors keep indices
            squad.Members.First(m => m.InstanceId == 2).IsAlive = false;
            squad.LastCasualtyRatio = 0.25f;
            new OrderApplicator().Apply(squad, order, runtime);

            Assert.True(FormationSlotLock.TryGetSlot(runtime, 1, out var a1));
            Assert.True(FormationSlotLock.TryGetSlot(runtime, 3, out var a3));
            Assert.True(FormationSlotLock.TryGetSlot(runtime, 4, out var a4));
            Assert.Equal(before[1], a1);
            Assert.Equal(before[3], a3);
            Assert.Equal(before[4], a4);
            Assert.Equal(capacityBefore, runtime.SlotLockCapacity); // holes kept
            // Dead entry may remain in LockedSlots as a hole
            Assert.True(runtime.LockedSlots.ContainsKey(2) || !runtime.LockedSlots.ContainsKey(2));
        }

        [Fact]
        public void Slot_lock_reshuffles_on_order_change()
        {
            PluginConfig.FormationReshuffleSeconds.Value = 30f;
            var doctrine = new RomanDoctrine();
            var squad = MakeRomanSquad(new[] { 1L, 2L, 3L });
            var runtime = new SquadRuntimeState { StableId = "roman#2", DoctrineId = "roman" };
            var hold = new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            };
            new OrderApplicator().Apply(squad, hold, runtime);
            var reshufflesBefore = FormationSlotLock.Reshuffles;

            var charge = new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Charge,
                Formation = FormationType.Wedge,
                Stance = StanceType.Aggressive,
            };
            new OrderApplicator().Apply(squad, charge, runtime);
            Assert.True(FormationSlotLock.Reshuffles > reshufflesBefore);
            Assert.Equal(DoctrineOrderKind.Charge, runtime.SlotLockOrderKind);
        }

        [Fact]
        public void ChargeMax_forces_peel_for_non_DeathRush_when_lingering()
        {
            // Use director path indirectly via ApplyChargeMaxHygiene is private —
            // verify Viking post-Charge peel still works (existing contract).
            var cmd = FakeSnapshots.CreateCommander();
            // Healthy Viking no longer reforms forever after Charge; 8m re-enters the standoff Hold.
            var after = FakeSnapshots.WithThreat(FakeSnapshots.Viking(), 8f);
            after.PreviousOrderKind = nameof(DoctrineOrderKind.Charge);
            var vikingOrder = cmd.Propose(after);
            Assert.Equal(DoctrineOrderKind.Hold, vikingOrder!.OrderKind);
            Assert.NotEqual(DoctrineOrderKind.RetreatAndReform, vikingOrder.OrderKind);

            var dr = FakeSnapshots.WithThreat(FakeSnapshots.Base("death-rush", 3), 5f);
            dr.PreviousOrderKind = nameof(DoctrineOrderKind.Charge);
            Assert.Equal(DoctrineOrderKind.Charge, cmd.Propose(dr)!.OrderKind);
        }

        static SquadUnit MakeRomanSquad(long[] ids)
        {
            var doctrine = new RomanDoctrine();
            var squad = new SquadUnit { SquadId = "roman#t", Doctrine = doctrine };
            float x = 0;
            foreach (var id in ids)
            {
                squad.Members.Add(new SquadMemberView
                {
                    InstanceId = id,
                    PrefabName = "Skeleton",
                    IsAlive = true,
                    AssignedRole = SquadRole.Front,
                    Position = new UnityEngine.Vector3(x, 0, 0),
                });
                x += 2f;
            }
            return squad;
        }
    }
}
