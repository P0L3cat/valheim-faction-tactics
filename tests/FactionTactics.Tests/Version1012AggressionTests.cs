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
                Assert.True(intent.AllowVanillaChase,
                    "Charge must set AllowVanillaChase (Attack → vanilla UpdateAI)");
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
            Assert.Equal(AggressionWeights.AmbushGreydwarf,
                AggressionWeights.For("ambush", SquadRole.Flanker));

            var snap = FakeSnapshots.WithThreat(FakeSnapshots.Ambush(), 10f);
            snap.PreviousOrderKind = nameof(DoctrineOrderKind.Flank);
            var scores = AmbushDoctrine.ScoreOrders(snap, DoctrineOrderKind.Flank);
            Assert.True(scores[DoctrineOrderKind.Flank] > 2.7f,
                $"greydwarf Flank utility should exceed pre-bump 2.7; got {scores[DoctrineOrderKind.Flank]}");
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
            Assert.True(flankers >= bumped || flankers > baselineQuota,
                $"expected more skeleton flankers; flankers={flankers} quota={bumped}");
        }

        [Fact]
        public void Viking_draugr_aggression_shortens_hold()
        {
            Assert.True(AggressionWeights.VikingDraugr > AggressionWeights.Baseline);
            PluginConfig.VikingStandoffHoldMin.Value = 1f;
            PluginConfig.VikingStandoffHoldMax.Value = 8f;
            var profile = VikingShieldWallDoctrine.LiveProfile(indoors: false);
            Assert.True(profile.HoldMax < 8f,
                $"draugr aggression should shorten HoldMax below config 8; got {profile.HoldMax}");
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
    }
}
