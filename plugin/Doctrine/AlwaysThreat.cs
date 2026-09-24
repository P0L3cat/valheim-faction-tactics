using System;
using System.Collections.Generic;
using FactionTactics.Orders;
using FactionTactics.Squad;
using UnityEngine;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Post-1.0.12 hungrier doctrine: every engaged / Theater squad always keeps
    /// ceil(10% of N) members pressing Attack or harassing at range.
    /// Formations stay for the rest — never soft parade for the whole squad.
    /// </summary>
    public static class AlwaysThreat
    {
        public const float Fraction = 0.10f;

        /// <summary>
        /// Engaged gate: player/threat within this of the squad (matches Theater co-engage / player scan).
        /// </summary>
        public const float EngagedRangeMeters = 48f;

        /// <summary>Ranged harass stand-off from threat when promoting a missile/skirmisher.</summary>
        public const float HarassPocketMeters = 11f;

        /// <summary>requiredThreat = ceil(N * 0.10). For N≥1 this is always ≥1.</summary>
        public static int Required(int memberCount)
        {
            if (memberCount <= 0)
                return 0;
            return (int)Math.Ceiling(memberCount * Fraction);
        }

        /// <summary>
        /// True press Attack: Charge, AllowVanillaChase release, or PressContact melee
        /// (AllowVanillaChase with PreferKeepRange false).
        /// </summary>
        public static bool IsPressAttack(MemberIntent? intent)
        {
            if (intent == null)
                return false;
            if (intent.OrderKind == DoctrineOrderKind.Charge)
                return true;
            return intent.AllowVanillaChase && !intent.PreferKeepRange;
        }

        /// <summary>
        /// Ranged harass: PreferKeepRange FocusFire / ProtectMissiles / Kite / Flank orbit.
        /// </summary>
        public static bool IsRangedHarass(MemberIntent? intent)
        {
            if (intent == null || !intent.PreferKeepRange)
                return false;
            return intent.OrderKind == DoctrineOrderKind.FocusFire
                || intent.OrderKind == DoctrineOrderKind.ProtectMissiles
                || intent.OrderKind == DoctrineOrderKind.Kite
                || intent.OrderKind == DoctrineOrderKind.Flank;
        }

        public static bool CountsAsThreatElement(MemberIntent? intent)
            => IsPressAttack(intent) || IsRangedHarass(intent);

        /// <summary>
        /// Engaged with player in range, has a threat, or Theater role active.
        /// Outside this gate the quota does not force presses (idle / far packs stay quiet).
        /// </summary>
        public static bool IsInThreatTheater(
            float nearestThreatDistance,
            int threatCount,
            TheaterRole theaterRole)
        {
            if (theaterRole != TheaterRole.None)
                return true;
            if (float.IsNaN(nearestThreatDistance) || float.IsInfinity(nearestThreatDistance))
                return threatCount > 0;
            if (threatCount > 0 && nearestThreatDistance <= EngagedRangeMeters)
                return true;
            return nearestThreatDistance <= EngagedRangeMeters;
        }

        public static int CountThreatElements(IEnumerable<MemberIntent>? intents)
        {
            if (intents == null)
                return 0;
            int n = 0;
            foreach (var intent in intents)
            {
                if (CountsAsThreatElement(intent))
                    n++;
            }
            return n;
        }

        /// <summary>
        /// After formation intents are written: promote the shortfall to press or ranged harass.
        /// Behavior-only — never touches spawn rates. Death-Rush already bee-lines (no change needed).
        /// </summary>
        public static void Enforce(
            SquadUnit squad,
            SquadRuntimeState? runtime,
            Vector3? threatPos)
        {
            if (squad?.Members == null || squad.Members.Count == 0)
                return;

            // Death-Rush: whole pack is Charge press — quota already satisfied when engaged.
            if (string.Equals(squad.Doctrine?.Id, "death-rush", StringComparison.OrdinalIgnoreCase))
                return;

            int alive = 0;
            foreach (var m in squad.Members)
            {
                if (m.IsAlive)
                    alive++;
            }

            var required = Required(alive);
            if (required <= 0)
                return;

            var dist = ResolveThreatDistance(squad, runtime);
            var threatCount = squad.DebugThreatCount ?? (dist <= EngagedRangeMeters ? 1 : 0);
            var theater = runtime?.TheaterRole ?? TheaterRole.None;
            if (!IsInThreatTheater(dist, threatCount, theater))
                return;

            var living = new List<(SquadMemberView member, MemberIntent intent)>(alive);
            int have = 0;
            foreach (var m in squad.Members)
            {
                if (!m.IsAlive)
                    continue;
                if (!OrderApplicator.TryGetIntent(m.InstanceId, out var intent) || intent == null)
                    continue;
                living.Add((m, intent));
                if (CountsAsThreatElement(intent))
                    have++;
            }

            var need = required - have;
            if (need <= 0)
                return;

            // Prefer non-HoldGround first so formation Front pins survive when a flanker/missile can press.
            // Then missiles → ranged harass; flankers → melee press; Front/Leader last.
            living.Sort((a, b) =>
            {
                var hold = (a.intent.HoldGround ? 1 : 0).CompareTo(b.intent.HoldGround ? 1 : 0);
                if (hold != 0)
                    return hold;
                return PromoteRank(a.member).CompareTo(PromoteRank(b.member));
            });

            foreach (var (member, intent) in living)
            {
                if (need <= 0)
                    break;
                if (CountsAsThreatElement(intent))
                    continue;

                if (PreferRangedHarass(member, theater))
                    PromoteRangedHarass(intent, member, threatPos);
                else
                    PromotePressAttack(intent, member, threatPos);

                OrderApplicator.Intents[member.InstanceId] = intent;
                need--;
            }
        }

        private static int PromoteRank(SquadMemberView m)
        {
            if (m.AssignedRole == SquadRole.Missile || m.LooksLikeMissile)
                return 0;
            if (m.AssignedRole == SquadRole.Flanker || m.LooksLikeFlanker)
                return 1;
            if (m.AssignedRole == SquadRole.Front || m.LooksLikeHeavy)
                return 2;
            if (m.AssignedRole == SquadRole.Leader || m.LooksLikeLeader)
                return 3;
            return 4;
        }

        private static bool PreferRangedHarass(SquadMemberView member, TheaterRole theater)
        {
            if (member.AssignedRole == SquadRole.Missile || member.LooksLikeMissile)
                return true;
            if (theater == TheaterRole.Harass)
                return true;
            return false;
        }

        private static void PromotePressAttack(MemberIntent intent, SquadMemberView member, Vector3? threatPos)
        {
            intent.AllowVanillaChase = true;
            intent.PreferKeepRange = false;
            intent.HoldGround = false;
            intent.PreferRun = true;
            if (intent.OrderKind == DoctrineOrderKind.Hold
                || intent.OrderKind == DoctrineOrderKind.Advance
                || intent.OrderKind == DoctrineOrderKind.ProtectMissiles
                || intent.OrderKind == DoctrineOrderKind.RetreatAndReform)
                intent.OrderKind = DoctrineOrderKind.Charge;

            if (threatPos.HasValue)
                intent.DesiredPosition = threatPos.Value;
        }

        private static void PromoteRangedHarass(MemberIntent intent, SquadMemberView member, Vector3? threatPos)
        {
            intent.PreferKeepRange = true;
            intent.AllowVanillaChase = false;
            intent.HoldGround = false;
            intent.PreferRun = true;
            if (intent.OrderKind != DoctrineOrderKind.FocusFire
                && intent.OrderKind != DoctrineOrderKind.ProtectMissiles
                && intent.OrderKind != DoctrineOrderKind.Kite
                && intent.OrderKind != DoctrineOrderKind.Flank)
                intent.OrderKind = DoctrineOrderKind.FocusFire;

            if (threatPos.HasValue)
            {
                var from = member.Position;
                var delta = threatPos.Value - from;
                delta.y = 0f;
                var len = delta.magnitude;
                if (len < 0.05f)
                {
                    intent.DesiredPosition = threatPos.Value + new Vector3(HarassPocketMeters, 0f, 0f);
                }
                else
                {
                    var dir = delta / len;
                    // Stand off at pocket: Desired stays HarassPocket from threat along approach axis.
                    intent.DesiredPosition = threatPos.Value - dir * HarassPocketMeters;
                }
            }
        }

        private static float ResolveThreatDistance(SquadUnit squad, SquadRuntimeState? runtime)
        {
            if (runtime != null
                && !float.IsNaN(runtime.LastThreatDistance)
                && !float.IsInfinity(runtime.LastThreatDistance))
                return runtime.LastThreatDistance;
            if (squad.DebugNearestThreatDistance.HasValue)
                return squad.DebugNearestThreatDistance.Value;
            return float.MaxValue;
        }
    }
}
