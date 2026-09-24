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
    /// 1.0.12 Narvi offline: Attack → vanilla UpdateAI release path; non-Attack sole-brain;
    /// Ambush/Roman/Viking aggression score bumps (weight doctrine only — never spawn rates).
    /// AllowVanillaChase is reinterpreted as the Attack release flag (Prefix returns true).
    /// </summary>
    public class Version1012AggressionTests
    {
        public Version1012AggressionTests() => TestConfig.EnsureBound();

        [Fact]
        public void Attack_Prefix_releases_to_vanilla_UpdateAI()
        {
            var intent = new MemberIntent
            {
                OrderKind = DoctrineOrderKind.Charge,
                AllowVanillaChase = true,
            };

            Assert.True(CombatAuthority.IsAttackReleaseOrder(DoctrineOrderKind.Charge));
            Assert.True(CombatAuthority.ShouldReleaseToVanillaUpdateAI(intent),
                "Attack/Charge AllowVanillaChase must release Prefix to vanilla UpdateAI");
            Assert.True(CombatDriver.ShouldReleaseToVanilla(intent));
            Assert.False(CombatAuthority.ShouldSoleBrain(intent));
            Assert.True(CombatAuthority.ShouldAllowVanillaMoveTo(intent),
                "MoveTo guard must not re-block Attack release");
        }

        [Fact]
        public void Attack_Charge_OrderApplicator_sets_AllowVanillaChase_release()
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
                    "Charge must set AllowVanillaChase (Attack release)");
                Assert.True(CombatAuthority.ShouldReleaseToVanillaUpdateAI(intent));
            }
        }

        [Fact]
        public void NonAttack_FormUp_still_skips()
        {
            AssertNonAttackSkips(DoctrineOrderKind.Advance, FormationType.ShieldWall, RomanPhase.ApproachStandoff);
        }

        [Fact]
        public void NonAttack_Ambush_orbit_still_skips()
        {
            var intent = new MemberIntent
            {
                OrderKind = DoctrineOrderKind.Flank,
                Formation = FormationType.Orb,
                PreferKeepRange = true,
                AllowVanillaChase = false,
            };
            Assert.False(CombatAuthority.IsAttackReleaseOrder(intent.OrderKind));
            Assert.True(CombatAuthority.ShouldSoleBrain(intent));
            Assert.False(CombatAuthority.ShouldReleaseToVanillaUpdateAI(intent));
        }

        [Fact]
        public void NonAttack_Flank_maneuver_still_skips()
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
            Assert.False(intent.AllowVanillaChase,
                "Flank maneuver stays FT sole-brain — no Attack release");
            Assert.True(CombatAuthority.ShouldSoleBrain(intent));
        }

        [Fact]
        public void NonAttack_Hold_still_skips()
        {
            AssertNonAttackSkips(DoctrineOrderKind.Hold, FormationType.ShieldWall, RomanPhase.StandoffHold);
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
            Assert.Equal(AggressionWeights.RomanSkeletonFlanker,
                AggressionWeights.For("roman", SquadRole.Flanker));
            Assert.Equal(AggressionWeights.Baseline,
                AggressionWeights.For("roman", SquadRole.Front));

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
        public void Viking_draugr_aggression_bump()
        {
            Assert.True(AggressionWeights.VikingDraugr > AggressionWeights.Baseline);
            Assert.Equal(AggressionWeights.VikingDraugr,
                AggressionWeights.For("viking-shieldwall", SquadRole.Front));

            PluginConfig.VikingStandoffHoldMin.Value = 1f;
            PluginConfig.VikingStandoffHoldMax.Value = 8f;
            var profile = VikingShieldWallDoctrine.LiveProfile(indoors: false);
            Assert.True(profile.HoldMax < 8f,
                $"draugr aggression should shorten HoldMax below config 8; got {profile.HoldMax}");
            Assert.True(profile.HoldMax >= profile.HoldMin);
        }

        [Fact]
        public void Viking_PressContact_true_press_releases_vanilla()
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
            Assert.True(intent.AllowVanillaChase,
                "PressContact true press must AllowVanillaChase (Attack release)");
            Assert.True(CombatAuthority.ShouldReleaseToVanillaUpdateAI(intent));
            Assert.False(intent.HoldGround);
        }

        [Fact]
        public void Ambush_Kite_still_skips()
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
            }, new SquadRuntimeState { RomanPhase = phase });

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
