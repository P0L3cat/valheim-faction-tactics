using System;
using System.Collections.Generic;
using FactionTactics.Commander;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Siege;
using FactionTactics.Squad;
using Xunit;

namespace FactionTactics.Tests
{
    public class PhaseBTests
    {
        public PhaseBTests() => TestConfig.EnsureBound();

        [Fact]
        public void PhaseB_knobs_are_ft_settable_with_defaults()
        {
            Assert.True(PluginConfig.TryGetKnob("OrderMinDwellSeconds", out var dwell));
            Assert.True(PluginConfig.TryGetKnob("OrderScoreHysteresis", out var hyst));
            Assert.True(PluginConfig.TryGetKnob("RomanWallOuter", out var outer));
            Assert.True(PluginConfig.TryGetKnob("RomanWallInner", out var inner));
            Assert.True(PluginConfig.TryGetKnob("ChargeMaxSeconds", out var charge));
            Assert.Equal(1.25f, (float)dwell!.BoxedValue);
            Assert.Equal(0.15f, (float)hyst!.BoxedValue);
            Assert.Equal(18f, (float)outer!.BoxedValue);
            Assert.Equal(14f, (float)inner!.BoxedValue);
            Assert.Equal(4f, (float)charge!.BoxedValue);
        }

        [Fact]
        public void ResolveWallBand_clamps_inner_inside_outer()
        {
            var defaults = RomanDoctrine.ResolveWallBand(18f, 14f);
            Assert.Equal(18f, defaults.outer);
            Assert.Equal(14f, defaults.inner);

            var swapped = RomanDoctrine.ResolveWallBand(18f, 20f);
            Assert.Equal(18f, swapped.outer);
            Assert.Equal(17f, swapped.inner);
        }

        [Fact]
        public void Hysteresis_sticks_until_score_clears_margin()
        {
            var scores = new Dictionary<DoctrineOrderKind, float>
            {
                [DoctrineOrderKind.Hold] = 1.0f,
                [DoctrineOrderKind.Advance] = 1.1f,
            };
            Assert.Equal(
                DoctrineOrderKind.Hold,
                OrderTransition.Pick(scores, DoctrineOrderKind.Hold, hysteresis: 0.15f));

            scores[DoctrineOrderKind.Advance] = 1.2f;
            Assert.Equal(
                DoctrineOrderKind.Advance,
                OrderTransition.Pick(scores, DoctrineOrderKind.Hold, hysteresis: 0.15f));
        }

        [Fact]
        public void Min_dwell_blocks_flip_unless_threat_lost_broken_or_ambush_force()
        {
            var fighting = new SquadSnapshot
            {
                DoctrineId = "roman",
                ThreatCount = 1,
                NearestThreatDistance = 16f,
            };
            Assert.Equal(
                DoctrineOrderKind.Advance,
                OrderTransition.ApplyMinDwell(
                    "roman", fighting, DoctrineOrderKind.Advance, DoctrineOrderKind.FocusFire,
                    orderAgeSeconds: 0.5f, minDwellSeconds: 1.25f));
            Assert.Equal(
                DoctrineOrderKind.FocusFire,
                OrderTransition.ApplyMinDwell(
                    "roman", fighting, DoctrineOrderKind.Advance, DoctrineOrderKind.FocusFire,
                    orderAgeSeconds: 1.25f, minDwellSeconds: 1.25f));

            fighting.ThreatCount = 0;
            Assert.Equal(
                DoctrineOrderKind.Hold,
                OrderTransition.ApplyMinDwell(
                    "roman", fighting, DoctrineOrderKind.Advance, DoctrineOrderKind.Hold,
                    orderAgeSeconds: 0.1f, minDwellSeconds: 1.25f));

            var broken = new SquadSnapshot { ThreatCount = 1, IsBroken = true, CasualtyRatio = 0.5f };
            Assert.Equal(
                DoctrineOrderKind.RetreatAndReform,
                OrderTransition.ApplyMinDwell(
                    "roman", broken, DoctrineOrderKind.Hold, DoctrineOrderKind.RetreatAndReform,
                    orderAgeSeconds: 0.1f, minDwellSeconds: 1.25f));

            var ambush = new SquadSnapshot { DoctrineId = "ambush", ThreatCount = 1, NearestThreatDistance = 6f };
            Assert.True(OrderTransition.IsExplicitForce("ambush", DoctrineOrderKind.Charge, DoctrineOrderKind.Kite));
            Assert.Equal(
                DoctrineOrderKind.Kite,
                OrderTransition.ApplyMinDwell(
                    "ambush", ambush, DoctrineOrderKind.Charge, DoctrineOrderKind.Kite,
                    orderAgeSeconds: 0.1f, minDwellSeconds: 1.25f));
            Assert.Equal(
                DoctrineOrderKind.Charge,
                OrderTransition.ApplyMinDwell(
                    "viking-shieldwall", ambush, DoctrineOrderKind.Charge, DoctrineOrderKind.Kite,
                    orderAgeSeconds: 0.1f, minDwellSeconds: 1.25f));
        }

        [Fact]
        public void DeathRush_never_leaves_Charge_while_a_threat_exists()
        {
            var threat = new SquadSnapshot { ThreatCount = 1, IsBroken = true, CasualtyRatio = 0.9f };
            Assert.Equal(
                DoctrineOrderKind.Charge,
                OrderTransition.EnforceDeathRush("death-rush", threat, DoctrineOrderKind.Kite));
            Assert.Equal(
                DoctrineOrderKind.RetreatAndReform,
                OrderTransition.EnforceDeathRush("roman", threat, DoctrineOrderKind.RetreatAndReform));

            var idle = new SquadSnapshot { ThreatCount = 0 };
            Assert.Equal(
                DoctrineOrderKind.Hold,
                OrderTransition.EnforceDeathRush("death-rush", idle, DoctrineOrderKind.Hold));
        }

        [Fact]
        public void Roman_wall_edge_hysteresis_does_not_flip_one_step_outside_Focus()
        {
            var cmd = FakeSnapshots.CreateCommander();
            var edge = FakeSnapshots.WithThreat(FakeSnapshots.Roman(), 17.95f);
            edge.PreviousOrderKind = nameof(DoctrineOrderKind.Advance);
            Assert.Equal(DoctrineOrderKind.Advance, cmd.Propose(edge)!.OrderKind);

            var inside = FakeSnapshots.WithThreat(FakeSnapshots.Roman(), 16f);
            inside.PreviousOrderKind = nameof(DoctrineOrderKind.Advance);
            var order = cmd.Propose(inside);
            Assert.True(order!.OrderKind == DoctrineOrderKind.FocusFire
                        || order.OrderKind == DoctrineOrderKind.ProtectMissiles);
        }

        [Fact]
        public void Ambush_flank_opportunity_flashes_from_orbit_otherwise_kites()
        {
            var cmd = FakeSnapshots.CreateCommander();
            var flash = FakeSnapshots.WithThreat(FakeSnapshots.Ambush(), 6f);
            flash.PreviousOrderKind = nameof(DoctrineOrderKind.Flank);
            flash.AgeSeconds = 0.5f;
            flash.TargetIsolated = false;
            flash.ThreatStaggeredOrLow = false;
            flash.FlankOpportunity = true;
            Assert.Equal(DoctrineOrderKind.Charge, cmd.Propose(flash)!.OrderKind);

            var kite = FakeSnapshots.WithThreat(FakeSnapshots.Ambush(), 6f);
            kite.PreviousOrderKind = nameof(DoctrineOrderKind.Flank);
            kite.AgeSeconds = 0.5f;
            kite.TargetIsolated = false;
            kite.ThreatStaggeredOrLow = false;
            kite.FlankOpportunity = false;
            Assert.Equal(DoctrineOrderKind.Kite, cmd.Propose(kite)!.OrderKind);

            var firstContact = FakeSnapshots.WithThreat(FakeSnapshots.Ambush(), 6f);
            firstContact.FlankOpportunity = true;
            firstContact.TargetIsolated = true;
            Assert.NotEqual(DoctrineOrderKind.Charge, cmd.Propose(firstContact)!.OrderKind);
        }

        [Fact]
        public void Action_scorer_cannot_resurrect_vetoed_Roman_Charge()
        {
            var roman = new RomanDoctrine();
            var snap = FakeSnapshots.WithThreat(FakeSnapshots.Roman(), 5f);
            snap.PreviousOrderKind = nameof(DoctrineOrderKind.Hold);
            snap.CasualtyRatio = 0.1f;

            OrderScoreContext.ActionScorer = new ChargeBiasScorer();
            try
            {
                Assert.NotEqual(DoctrineOrderKind.Charge, roman.SelectOrder(snap, DoctrineOrderKind.Hold));
            }
            finally
            {
                OrderScoreContext.ActionScorer = null;
            }
        }

        [Fact]
        public void Director_dwell_holds_roman_order_and_ambush_force_kite_bypasses()
        {
            var roman = new RomanDoctrine();
            var romanSquad = Squad(roman, "Skeleton", 4);
            romanSquad.DebugThreatCount = 1;
            romanSquad.DebugNearestThreatDistance = 16f;
            var script = new ScriptCommander { Next = DoctrineOrderKind.Advance };
            var director = Director(new FakeDiscovery(romanSquad), script);

            director.Tick(0.75f);
            Assert.Equal(DoctrineOrderKind.Advance, romanSquad.CurrentOrder!.OrderKind);

            script.Next = DoctrineOrderKind.FocusFire;
            director.Tick(0.4f);
            Assert.Equal(DoctrineOrderKind.Advance, romanSquad.CurrentOrder!.OrderKind);

            romanSquad.DebugThreatCount = 0;
            script.Next = DoctrineOrderKind.Hold;
            director.Tick(0.1f);
            Assert.Equal(DoctrineOrderKind.Hold, romanSquad.CurrentOrder!.OrderKind);

            var ambush = new AmbushDoctrine();
            var ambushSquad = Squad(ambush, "Greydwarf", 4);
            ambushSquad.DebugThreatCount = 1;
            ambushSquad.DebugNearestThreatDistance = 6f;
            var ambushScript = new ScriptCommander { Next = DoctrineOrderKind.Charge };
            var ambushDirector = Director(new FakeDiscovery(ambushSquad), ambushScript);
            ambushDirector.Tick(0.2f);
            Assert.Equal(DoctrineOrderKind.Charge, ambushSquad.CurrentOrder!.OrderKind);
            ambushScript.Next = DoctrineOrderKind.Kite;
            ambushDirector.Tick(0.2f);
            Assert.Equal(DoctrineOrderKind.Kite, ambushSquad.CurrentOrder!.OrderKind);
        }

        [Fact]
        public void Director_DeathRush_ignores_kite_and_ChargeMax_while_threatened()
        {
            var rush = new DeathRushDoctrine();
            var squad = Squad(rush, "Greyling", 3);
            squad.DebugThreatCount = 1;
            squad.DebugNearestThreatDistance = 5f;
            var script = new ScriptCommander { Next = DoctrineOrderKind.Kite };
            var director = Director(new FakeDiscovery(squad), script);

            director.Tick(0.2f);
            Assert.Equal(DoctrineOrderKind.Charge, squad.CurrentOrder!.OrderKind);
            director.Tick(5f);
            Assert.Equal(DoctrineOrderKind.Charge, squad.CurrentOrder!.OrderKind);

            squad.DebugThreatCount = 0;
            script.Next = DoctrineOrderKind.Hold;
            director.Tick(0.2f);
            Assert.Equal(DoctrineOrderKind.Hold, squad.CurrentOrder!.OrderKind);
        }

        [Fact]
        public void Director_ChargeMax_returns_Roman_missiles_to_ProtectMissiles()
        {
            var roman = new RomanDoctrine();
            var squad = new SquadUnit { SquadId = "roman-b", Doctrine = roman };
            var roster = new List<SquadMemberView>();
            roster.Add(Member(1, "Skeleton", SquadRole.Front));
            roster.Add(Member(2, "Skeleton", SquadRole.Front));
            roster.Add(Member(3, "Skeleton", SquadRole.Leader));
            var missile = Member(4, "Skeleton", SquadRole.Missile);
            missile.LooksLikeMissile = true;
            roster.Add(missile);
            foreach (var m in roster)
            {
                m.AssignedRole = roman.AssignRole(m, roster);
                squad.Members.Add(m);
            }
            Assert.Contains(squad.Members, m => m.AssignedRole == SquadRole.Missile);

            squad.DebugThreatCount = 1;
            squad.DebugNearestThreatDistance = 8f;
            var script = new ScriptCommander { Next = DoctrineOrderKind.Charge };
            var director = Director(new FakeDiscovery(squad), script);

            director.Tick(0.1f);
            Assert.Equal(DoctrineOrderKind.Charge, squad.CurrentOrder!.OrderKind);
            director.Tick(4.5f);
            Assert.Equal(DoctrineOrderKind.ProtectMissiles, squad.CurrentOrder!.OrderKind);
        }

        private sealed class ChargeBiasScorer : IActionScorer
        {
            public float ScoreAction(DoctrineOrderKind order, SquadSnapshot snapshot)
                => order == DoctrineOrderKind.Charge ? 50f : 0f;
        }

        private sealed class ScriptCommander : ICommander
        {
            public DoctrineOrderKind Next { get; set; }

            public SquadOrder? Propose(SquadSnapshot snapshot)
            {
                return new SquadOrder
                {
                    SquadId = snapshot.SquadId,
                    OrderKind = Next,
                    Formation = FormationType.Loose,
                    Stance = StanceType.Aggressive,
                    Source = "test",
                };
            }
        }

        private static SquadDirector Director(ISquadDiscovery discovery, ICommander commander)
        {
            return new SquadDirector(
                discovery,
                commander,
                DoctrinePackRegistry.CreateDefault(),
                new NullRoleScorer(),
                new NullActionScorer(),
                new OrderApplicator(),
                new SiegeDirector());
        }

        private static SquadUnit Squad(IDoctrinePack doctrine, string prefab, int count)
        {
            var squad = new SquadUnit { SquadId = doctrine.Id + "-b", Doctrine = doctrine };
            var roster = new List<SquadMemberView>();
            for (var i = 0; i < count; i++)
                roster.Add(Member(100 + i, prefab, SquadRole.Unassigned));
            foreach (var m in roster)
            {
                m.AssignedRole = doctrine.AssignRole(m, roster);
                squad.Members.Add(m);
            }
            return squad;
        }

        private static SquadMemberView Member(long id, string prefab, SquadRole role)
        {
            return new SquadMemberView
            {
                InstanceId = id,
                PrefabName = prefab,
                IsAlive = true,
                AssignedRole = role,
                Position = new UnityEngine.Vector3(id, 0f, 0f),
            };
        }
    }
}
