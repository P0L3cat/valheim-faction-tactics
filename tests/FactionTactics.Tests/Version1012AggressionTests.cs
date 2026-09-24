using System.Linq;
using FactionTactics.Combat;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Squad;
using UnityEngine;
using Xunit;

namespace FactionTactics.Tests
{
    /// <summary>
    /// 1.0.12: AllowVanillaChase → release vanilla UpdateAI; hungrier Ambush/Roman/Viking presses.
    /// Weight doctrine only — never spawn rates.
    /// </summary>
    public class Version1012AggressionTests
    {
        public Version1012AggressionTests() => TestConfig.EnsureBound();

        [Fact]
        public void AllowVanillaChase_releases_to_vanilla_UpdateAI()
        {
            var intent = new MemberIntent
            {
                OrderKind = DoctrineOrderKind.Charge,
                AllowVanillaChase = true,
            };

            Assert.True(CombatAuthority.ShouldReleaseToVanilla(intent));
            Assert.True(CombatDriver.ShouldReleaseToVanilla(intent));
            Assert.False(CombatAuthority.ShouldSoleBrain(intent));
            Assert.True(CombatAuthority.ShouldAllowVanillaMoveTo(intent));
        }

        [Fact]
        public void Charge_OrderApplicator_sets_AllowVanillaChase_Attack()
        {
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            foreach (var m in squad.Members)
            {
                m.AssignedRole = SquadRole.Front;
                m.LooksLikeMissile = false;
            }
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Charge,
                Formation = FormationType.Wedge,
                Stance = StanceType.Aggressive,
            }, new SquadRuntimeState());

            Assert.NotEmpty(OrderApplicator.Intents);
            foreach (var intent in OrderApplicator.Intents.Values)
            {
                Assert.True(intent.AllowVanillaChase);
                Assert.True(CombatAuthority.ShouldReleaseToVanilla(intent));
            }
        }

        [Fact]
        public void Roman_flanker_Flank_releases_vanilla_Attack()
        {
            var roman = new RomanDoctrine();
            var squad = new SquadUnit { SquadId = "roman-flank", Doctrine = roman, DebugNearestThreatDistance = 8f };
            var id = 91001L;
            squad.Members.Add(new SquadMemberView
            {
                InstanceId = id,
                PrefabName = "Skeleton",
                IsAlive = true,
                AssignedRole = SquadRole.Flanker,
                LooksLikeFlanker = true,
                Position = Vector3.zero,
            });
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Flank,
                Formation = FormationType.Skirmish,
                Stance = StanceType.Aggressive,
            }, new SquadRuntimeState { DoctrineId = "roman", LastThreatDistance = 8f });

            Assert.True(OrderApplicator.TryGetIntent(id, out var intent));
            Assert.True(intent.AllowVanillaChase);
            Assert.False(intent.PreferKeepRange);
            Assert.True(CombatAuthority.ShouldReleaseToVanilla(intent));
        }

        [Fact]
        public void Ambush_Flank_in_contact_releases_vanilla_Attack()
        {
            var ambush = new AmbushDoctrine();
            var squad = new SquadUnit { SquadId = "ambush-press", Doctrine = ambush, DebugNearestThreatDistance = 4f };
            var id = 91002L;
            squad.Members.Add(new SquadMemberView
            {
                InstanceId = id,
                PrefabName = "Greydwarf",
                IsAlive = true,
                AssignedRole = SquadRole.Flanker,
                LooksLikeFlanker = true,
                Position = Vector3.zero,
            });
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Flank,
                Formation = FormationType.Orb,
                Stance = StanceType.Aggressive,
            }, new SquadRuntimeState { DoctrineId = "ambush", LastThreatDistance = 4f });

            Assert.True(OrderApplicator.TryGetIntent(id, out var intent));
            Assert.True(intent.AllowVanillaChase);
            Assert.False(intent.PreferKeepRange);
        }

        [Fact]
        public void Ambush_Kite_keeps_PreferKeepRange_no_Attack_release()
        {
            var ambush = new AmbushDoctrine();
            var squad = new SquadUnit { SquadId = "ambush-kite", Doctrine = ambush, DebugNearestThreatDistance = 4f };
            var id = 91003L;
            squad.Members.Add(new SquadMemberView
            {
                InstanceId = id,
                PrefabName = "Greydwarf",
                IsAlive = true,
                AssignedRole = SquadRole.Flanker,
                LooksLikeFlanker = true,
                Position = Vector3.zero,
            });
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Kite,
                Formation = FormationType.Orb,
                Stance = StanceType.Defensive,
            }, new SquadRuntimeState { DoctrineId = "ambush", LastThreatDistance = 4f });

            Assert.True(OrderApplicator.TryGetIntent(id, out var intent));
            Assert.False(intent.AllowVanillaChase);
            Assert.True(intent.PreferKeepRange);
            Assert.True(CombatAuthority.ShouldSoleBrain(intent));
        }

        [Fact]
        public void Roman_flanker_FocusFire_releases_vanilla_Attack()
        {
            var roman = new RomanDoctrine();
            var squad = new SquadUnit { SquadId = "roman-ff", Doctrine = roman, DebugNearestThreatDistance = 8f };
            var id = 91011L;
            squad.Members.Add(new SquadMemberView
            {
                InstanceId = id,
                PrefabName = "Skeleton",
                IsAlive = true,
                AssignedRole = SquadRole.Flanker,
                LooksLikeFlanker = true,
                Position = Vector3.zero,
            });
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.FocusFire,
                Formation = FormationType.Skirmish,
                Stance = StanceType.Aggressive,
            }, new SquadRuntimeState { DoctrineId = "roman", LastThreatDistance = 8f });

            Assert.True(OrderApplicator.TryGetIntent(id, out var intent));
            Assert.True(intent.AllowVanillaChase);
            Assert.False(intent.PreferKeepRange);
            Assert.True(CombatAuthority.ShouldReleaseToVanilla(intent));
            Assert.True(CombatAuthority.ShouldReleaseToVanillaUpdateAI(intent));
        }

        [Fact]
        public void Ambush_FocusFire_in_contact_releases_vanilla_Attack()
        {
            var ambush = new AmbushDoctrine();
            var squad = new SquadUnit { SquadId = "ambush-ff", Doctrine = ambush, DebugNearestThreatDistance = 4f };
            var id = 91012L;
            squad.Members.Add(new SquadMemberView
            {
                InstanceId = id,
                PrefabName = "Greydwarf",
                IsAlive = true,
                AssignedRole = SquadRole.Flanker,
                LooksLikeFlanker = true,
                Position = Vector3.zero,
            });
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.FocusFire,
                Formation = FormationType.Orb,
                Stance = StanceType.Aggressive,
            }, new SquadRuntimeState { DoctrineId = "ambush", LastThreatDistance = 4f });

            Assert.True(OrderApplicator.TryGetIntent(id, out var intent));
            Assert.True(intent.AllowVanillaChase);
            Assert.False(intent.PreferKeepRange);
            Assert.True(CombatAuthority.ShouldReleaseToVanilla(intent));
        }

        [Fact]
        public void Ambush_orbit_outside_contact_still_skips()
        {
            var ambush = new AmbushDoctrine();
            var squad = new SquadUnit { SquadId = "ambush-orbit", Doctrine = ambush, DebugNearestThreatDistance = 30f };
            var id = 91013L;
            squad.Members.Add(new SquadMemberView
            {
                InstanceId = id,
                PrefabName = "Greydwarf",
                IsAlive = true,
                AssignedRole = SquadRole.Flanker,
                LooksLikeFlanker = true,
                Position = Vector3.zero,
            });
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Flank,
                Formation = FormationType.Orb,
                Stance = StanceType.Aggressive,
            }, new SquadRuntimeState { DoctrineId = "ambush", LastThreatDistance = 30f });

            Assert.True(OrderApplicator.TryGetIntent(id, out var intent));
            Assert.False(intent.AllowVanillaChase);
            Assert.True(intent.PreferKeepRange);
            Assert.True(CombatAuthority.ShouldSoleBrain(intent));
            Assert.False(CombatAuthority.IsAttackReleaseOrder(intent.OrderKind));
        }

        [Fact]
        public void NonAttack_FormUp_Advance_still_skips()
        {
            AssertNonAttackSkips(DoctrineOrderKind.Advance, FormationType.ShieldWall, RomanPhase.ApproachStandoff);
        }

        [Fact]
        public void NonAttack_Hold_still_skips()
        {
            AssertNonAttackSkips(DoctrineOrderKind.Hold, FormationType.ShieldWall, RomanPhase.StandoffHold);
        }

        [Fact]
        public void Soft_parade_Hold_closes_chase_no_Attack_release()
        {
            var roman = new RomanDoctrine();
            var squad = new SquadUnit { SquadId = "roman-parade", Doctrine = roman, DebugNearestThreatDistance = 8f };
            var id = 91014L;
            squad.Members.Add(new SquadMemberView
            {
                InstanceId = id,
                PrefabName = "Skeleton",
                IsAlive = true,
                AssignedRole = SquadRole.Front,
                LooksLikeHeavy = true,
                Position = Vector3.zero,
            });
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            }, new SquadRuntimeState
            {
                DoctrineId = "roman",
                RomanPhase = RomanPhase.StandoffHold,
                LastThreatDistance = 8f,
            });

            Assert.True(OrderApplicator.TryGetIntent(id, out var intent));
            Assert.False(intent.AllowVanillaChase, "parade Hold must not soft-release Attack");
            Assert.True(CombatAuthority.ShouldSoleBrain(intent));
        }

        [Fact]
        public void DANGEROUS_Attack_gets_real_vanilla_UpdateAI()
        {
            Assert.True(CombatAuthority.IsAttackReleaseOrder(DoctrineOrderKind.Charge));
            Assert.False(CombatAuthority.IsAttackReleaseOrder(DoctrineOrderKind.Hold));
            Assert.False(CombatAuthority.IsAttackReleaseOrder(DoctrineOrderKind.Advance));
            Assert.False(CombatAuthority.IsAttackReleaseOrder(DoctrineOrderKind.Flank));

            var intent = new MemberIntent
            {
                OrderKind = DoctrineOrderKind.Charge,
                AllowVanillaChase = true,
            };
            Assert.True(CombatAuthority.ShouldReleaseToVanillaUpdateAI(intent),
                "DANGEROUS north star: Attack AllowVanillaChase must Prefix-release real vanilla UpdateAI");
            Assert.True(CombatAuthority.ShouldAllowVanillaMoveTo(intent),
                "MoveTo must not re-block Attack release");
            Assert.False(CombatAuthority.ShouldSoleBrain(intent));
        }

        [Fact]
        public void Formation_Advance_without_AllowVanillaChase_stays_sole_brain()
        {
            var intent = new MemberIntent
            {
                OrderKind = DoctrineOrderKind.Advance,
                Formation = FormationType.ShieldWall,
                AllowVanillaChase = false,
            };
            Assert.True(CombatAuthority.ShouldSoleBrain(intent));
            Assert.False(CombatAuthority.ShouldReleaseToVanilla(intent));
        }

        [Fact]
        public void Ambush_greydwarf_aggression_bump()
        {
            Assert.True(AggressionWeights.AmbushGreydwarf > AggressionWeights.Baseline);
            var snap = FakeSnapshots.WithThreat(FakeSnapshots.Ambush(), 10f);
            snap.PreviousOrderKind = nameof(DoctrineOrderKind.Flank);
            var scores = AmbushDoctrine.ScoreOrders(snap, DoctrineOrderKind.Flank);
            Assert.True(scores[DoctrineOrderKind.Flank] > 2.7f);
        }

        [Fact]
        public void Roman_skeleton_flanker_aggression_bump()
        {
            Assert.True(AggressionWeights.RomanSkeletonFlanker > AggressionWeights.Baseline);
            var baselineQuota = System.Math.Max(1, 8 / 4);
            var bumped = AggressionWeights.RomanTargetFlankers(8);
            Assert.True(bumped > baselineQuota);

            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 8, "Skeleton");
            foreach (var m in squad.Members)
            {
                m.LooksLikeMissile = false;
                m.LooksLikeHeavy = false;
                m.LooksLikeFlanker = true;
                m.LooksLikeLeader = false;
                m.AssignedRole = SquadRole.Front;
            }
            foreach (var m in squad.Members)
                m.AssignedRole = roman.AssignRole(m, squad.Members);

            var flankers = squad.Members.Count(m => m.AssignedRole == SquadRole.Flanker);
            Assert.True(flankers >= bumped || flankers > baselineQuota);
        }

        [Fact]
        public void Viking_draugr_aggression_shortens_hold()
        {
            PluginConfig.VikingStandoffHoldMin.Value = 1f;
            PluginConfig.VikingStandoffHoldMax.Value = 8f;
            var profile = VikingShieldWallDoctrine.LiveProfile(indoors: false);
            Assert.True(profile.HoldMax < 8f);
            Assert.True(profile.HoldMax >= profile.HoldMin);
        }

        [Fact]
        public void Viking_PressContact_melee_AllowVanillaChase()
        {
            var viking = new VikingShieldWallDoctrine();
            var squad = new SquadUnit { SquadId = "viking-press", Doctrine = viking, DebugNearestThreatDistance = 3f };
            var id = 91004L;
            squad.Members.Add(new SquadMemberView
            {
                InstanceId = id,
                PrefabName = "Draugr",
                IsAlive = true,
                AssignedRole = SquadRole.Front,
                LooksLikeHeavy = true,
                Position = Vector3.zero,
            });
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Advance,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Aggressive,
            }, new SquadRuntimeState
            {
                DoctrineId = "viking-shieldwall",
                RomanPhase = RomanPhase.PressContact,
                LastThreatDistance = 3f,
            });

            Assert.True(OrderApplicator.TryGetIntent(id, out var intent));
            Assert.True(intent.AllowVanillaChase);
            Assert.False(intent.HoldGround);
        }

        static void AssertNonAttackSkips(DoctrineOrderKind order, FormationType formation, RomanPhase phase)
        {
            Assert.False(CombatAuthority.IsAttackReleaseOrder(order));

            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman")!;
            var squad = FakeSnapshots.MakeSquad(roman, 4, "Skeleton");
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = order,
                Formation = formation,
                Stance = StanceType.Defensive,
            }, new SquadRuntimeState { RomanPhase = phase, DoctrineId = "roman" });

            Assert.NotEmpty(OrderApplicator.Intents);
            foreach (var intent in OrderApplicator.Intents.Values)
            {
                Assert.False(intent.AllowVanillaChase,
                    $"{order}/{phase} must stay FT sole-brain (no Attack release)");
                Assert.True(CombatAuthority.ShouldSoleBrain(intent));
            }
        }
    }
}
