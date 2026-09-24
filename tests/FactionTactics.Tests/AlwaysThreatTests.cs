using System;
using System.Collections.Generic;
using System.Linq;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Squad;
using UnityEngine;
using Xunit;

namespace FactionTactics.Tests
{
    /// <summary>
    /// Post-1.0.12 hungrier: ceil(10% of N) always-threat while engaged / Theater.
    /// Press Attack OR ranged harass; formations keep the rest.
    /// </summary>
    public class AlwaysThreatTests
    {
        public AlwaysThreatTests() => TestConfig.EnsureBound();

        public static IEnumerable<object[]> RequiredCases()
        {
            yield return new object[] { 0, 0 };
            yield return new object[] { 1, 1 };
            yield return new object[] { 3, 1 };
            yield return new object[] { 5, 1 };
            yield return new object[] { 10, 1 };
            yield return new object[] { 11, 2 };
            yield return new object[] { 20, 2 };
        }

        [Theory]
        [MemberData(nameof(RequiredCases))]
        public void RequiredThreat_is_ceil_10_percent(int n, int expected)
        {
            Assert.Equal(expected, AlwaysThreat.Required(n));
            if (n >= 1)
                Assert.True(AlwaysThreat.Required(n) >= 1);
        }

        [Fact]
        public void PressAttack_counts_Charge_and_AllowVanillaChase()
        {
            Assert.True(AlwaysThreat.IsPressAttack(new MemberIntent
            {
                OrderKind = DoctrineOrderKind.Charge,
                AllowVanillaChase = true,
            }));
            Assert.True(AlwaysThreat.IsPressAttack(new MemberIntent
            {
                OrderKind = DoctrineOrderKind.Advance,
                AllowVanillaChase = true,
                PreferKeepRange = false,
            }));
            Assert.False(AlwaysThreat.IsPressAttack(new MemberIntent
            {
                OrderKind = DoctrineOrderKind.Hold,
                AllowVanillaChase = false,
            }));
            Assert.False(AlwaysThreat.IsPressAttack(new MemberIntent
            {
                OrderKind = DoctrineOrderKind.FocusFire,
                AllowVanillaChase = true,
                PreferKeepRange = true,
            }), "PreferKeepRange FocusFire is ranged harass, not melee press");
        }

        [Fact]
        public void RangedHarass_counts_PreferKeepRange_FocusFire_Flank_Kite()
        {
            Assert.True(AlwaysThreat.IsRangedHarass(new MemberIntent
            {
                OrderKind = DoctrineOrderKind.FocusFire,
                PreferKeepRange = true,
            }));
            Assert.True(AlwaysThreat.IsRangedHarass(new MemberIntent
            {
                OrderKind = DoctrineOrderKind.Flank,
                PreferKeepRange = true,
            }));
            Assert.True(AlwaysThreat.IsRangedHarass(new MemberIntent
            {
                OrderKind = DoctrineOrderKind.Kite,
                PreferKeepRange = true,
            }));
            Assert.False(AlwaysThreat.IsRangedHarass(new MemberIntent
            {
                OrderKind = DoctrineOrderKind.Hold,
                PreferKeepRange = false,
            }));
        }

        [Fact]
        public void InTheater_when_threat_in_range_or_TheaterRole()
        {
            Assert.True(AlwaysThreat.IsInThreatTheater(8f, 1, TheaterRole.None));
            Assert.True(AlwaysThreat.IsInThreatTheater(float.MaxValue, 0, TheaterRole.Pin));
            Assert.True(AlwaysThreat.IsInThreatTheater(float.MaxValue, 0, TheaterRole.Harass));
            Assert.False(AlwaysThreat.IsInThreatTheater(60f, 0, TheaterRole.None));
            Assert.False(AlwaysThreat.IsInThreatTheater(float.MaxValue, 0, TheaterRole.None));
        }

        public static IEnumerable<object[]> DoctrineAndSizes()
        {
            foreach (var doctrine in new[] { "ambush", "roman", "viking-shieldwall" })
            {
                foreach (var n in new[] { 1, 3, 10, 11 })
                    yield return new object[] { doctrine, n };
            }
        }

        [Theory]
        [MemberData(nameof(DoctrineAndSizes))]
        public void Engaged_Hold_parade_keeps_ceil_10pct_threat(string doctrineId, int n)
        {
            var (squad, runtime, threatPos) = BuildEngagedParade(doctrineId, n, TheaterRole.None);
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = doctrineId == "ambush" ? FormationType.Orb : FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            }, runtime);

            var required = AlwaysThreat.Required(n);
            var intents = LivingIntents(squad);
            Assert.Equal(n, intents.Count);
            var threatCount = AlwaysThreat.CountThreatElements(intents);
            Assert.True(threatCount >= required,
                $"{doctrineId} N={n}: threatElements={threatCount} < requiredThreat={required} " +
                $"(engaged Hold must not soft-parade the whole squad)");

            AssertThreatGeometry(squad, intents, threatPos, required);
        }

        [Theory]
        [MemberData(nameof(DoctrineAndSizes))]
        public void Theater_active_Advance_keeps_ceil_10pct_threat(string doctrineId, int n)
        {
            var role = doctrineId == "ambush" ? TheaterRole.Harass : TheaterRole.Pin;
            var (squad, runtime, threatPos) = BuildEngagedParade(doctrineId, n, role);
            // Far threat distance alone would skip engage — Theater role still forces quota.
            runtime.LastThreatDistance = 80f;
            squad.DebugNearestThreatDistance = 80f;

            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Advance,
                Formation = doctrineId == "ambush" ? FormationType.Skirmish : FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            }, runtime);

            var required = AlwaysThreat.Required(n);
            var intents = LivingIntents(squad);
            var threatCount = AlwaysThreat.CountThreatElements(intents);
            Assert.True(threatCount >= required,
                $"{doctrineId} Theater={role} N={n}: threatElements={threatCount} < required={required}");

            AssertThreatGeometry(squad, intents, threatPos, required);
        }

        [Fact]
        public void Far_idle_Hold_does_not_force_threat()
        {
            var (squad, runtime, _) = BuildEngagedParade("roman", 5, TheaterRole.None);
            runtime.LastThreatDistance = 90f;
            squad.DebugNearestThreatDistance = 90f;
            squad.DebugThreatCount = 0;
            squad.DebugThreatPosition = null;

            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            }, runtime);

            // Outside engage / Theater: soft Hold parade is allowed (no forced press).
            Assert.False(AlwaysThreat.IsInThreatTheater(90f, 0, TheaterRole.None));
            foreach (var intent in LivingIntents(squad))
                Assert.False(intent.AllowVanillaChase, "far idle must not Attack-release");
        }

        [Fact]
        public void Formations_remain_for_non_threat_majority_when_N_large()
        {
            var (squad, runtime, _) = BuildEngagedParade("roman", 11, TheaterRole.Pin);
            OrderApplicator.Intents.Clear();
            new OrderApplicator().Apply(squad, new SquadOrder
            {
                OrderKind = DoctrineOrderKind.Hold,
                Formation = FormationType.ShieldWall,
                Stance = StanceType.Defensive,
            }, runtime);

            var intents = LivingIntents(squad);
            var threat = AlwaysThreat.CountThreatElements(intents);
            var formation = intents.Count(i => !AlwaysThreat.CountsAsThreatElement(i));
            Assert.Equal(2, AlwaysThreat.Required(11));
            Assert.True(threat >= 2);
            Assert.True(formation >= 9, "majority stays formation / not whole-squad press");
        }

        static (SquadUnit squad, SquadRuntimeState runtime, Vector3 threatPos) BuildEngagedParade(
            string doctrineId,
            int n,
            TheaterRole theaterRole)
        {
            IDoctrinePack doctrine = doctrineId switch
            {
                "ambush" => new AmbushDoctrine(),
                "viking-shieldwall" => new VikingShieldWallDoctrine(),
                _ => new RomanDoctrine(),
            };
            var prefab = doctrineId switch
            {
                "ambush" => "Greydwarf",
                "viking-shieldwall" => "Draugr",
                _ => "Skeleton",
            };
            var threatPos = new Vector3(12f, 0f, 0f);
            var squad = FakeSnapshots.MakeSquad(doctrine, n, prefab);
            squad.DebugThreatCount = 1;
            squad.DebugNearestThreatDistance = 8f;
            squad.DebugThreatPosition = threatPos;
            squad.DebugFocusPlayerId = 42;
            squad.DebugFocusPlayerPosition = threatPos;

            // Spread members so Dist(Position, threat) is measurable for geometry.
            for (int i = 0; i < squad.Members.Count; i++)
            {
                var m = squad.Members[i];
                m.Position = new Vector3(-4f - i * 2f, 0f, (i % 3) * 1.5f);
                m.LooksLikeMissile = i == 0 && n >= 3;
                m.LooksLikeFlanker = doctrineId == "ambush" || i % 3 == 1;
                m.LooksLikeHeavy = i % 3 == 2;
                m.AssignedRole = doctrine.AssignRole(m, squad.Members);
            }

            var runtime = new SquadRuntimeState
            {
                DoctrineId = doctrineId,
                LastThreatDistance = 8f,
                TheaterRole = theaterRole,
                HasTheaterFocus = theaterRole != TheaterRole.None,
                TheaterFocusId = theaterRole != TheaterRole.None ? 42 : 0,
                TheaterFocusPosition = threatPos,
                RomanPhase = RomanPhase.StandoffHold,
            };
            return (squad, runtime, threatPos);
        }

        static List<MemberIntent> LivingIntents(SquadUnit squad)
        {
            var list = new List<MemberIntent>();
            foreach (var m in squad.Members.Where(x => x.IsAlive))
            {
                Assert.True(OrderApplicator.TryGetIntent(m.InstanceId, out var intent));
                list.Add(intent);
            }
            return list;
        }

        /// <summary>
        /// Geometric wins: press closes Dist to threat; ranged harass keeps PreferKeepRange pocket.
        /// </summary>
        static void AssertThreatGeometry(
            SquadUnit squad,
            List<MemberIntent> intents,
            Vector3 threatPos,
            int required)
        {
            var byId = squad.Members.ToDictionary(m => m.InstanceId);
            int geometricWins = 0;
            foreach (var kv in OrderApplicator.Intents)
            {
                if (!byId.TryGetValue(kv.Key, out var member))
                    continue;
                var intent = kv.Value;
                if (!AlwaysThreat.CountsAsThreatElement(intent))
                    continue;

                var distNow = Horizontal(member.Position, threatPos);
                var distDesired = Horizontal(intent.DesiredPosition, threatPos);

                if (AlwaysThreat.IsPressAttack(intent))
                {
                    Assert.True(intent.AllowVanillaChase || intent.OrderKind == DoctrineOrderKind.Charge);
                    Assert.False(intent.PreferKeepRange);
                    // Closing or already on the threat cone (Desired nearer / equal).
                    Assert.True(distDesired <= distNow + 0.05f,
                        $"press Desired must close Dist: now={distNow:F2} desired={distDesired:F2}");
                    geometricWins++;
                }
                else if (AlwaysThreat.IsRangedHarass(intent))
                {
                    Assert.True(intent.PreferKeepRange);
                    // Harass pocket: not planted on threat; stay at stand-off geometry.
                    Assert.True(distDesired >= 3f || distNow < 4f,
                        $"ranged harass Desired should keep pocket (d={distDesired:F2})");
                    geometricWins++;
                }
            }

            Assert.True(geometricWins >= required,
                $"geometric threat wins={geometricWins} < required={required}");
        }

        static float Horizontal(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
