using System;
using System.Collections.Generic;
using System.Linq;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Fuling* / Goblin* → Steppe (ex-Mongol stub): kite, volley, encircle;
    /// berserkers only on cut-off targets. Near structure/totem/village → tighter defense orbit.
    /// </summary>
    public sealed class SteppeDoctrine : DoctrinePackBase
    {
        public override string Id => "steppe";
        public override string DisplayName => "Steppe";

        public override IReadOnlyList<string> PrefabPrefixes { get; } = new[]
        {
            "Fuling",
            "Goblin",
        };

        public override bool IsEnabled => PluginConfig.EnableSteppe?.Value ?? true;


        public override SquadRole AssignRole(SquadMemberView member, IReadOnlyList<SquadMemberView> squad)
        {
            if (IsShamanOrArcher(member) || member.LooksLikeMissile)
            {
                member.LooksLikeMissile = true;
                return SquadRole.Missile;
            }

            if (IsBerserker(member))
            {
                member.LooksLikeHeavy = true;
                var hasLeader = squad.Any(m =>
                    m.AssignedRole == SquadRole.Leader && m.InstanceId != member.InstanceId);
                if (!hasLeader && (member.LooksLikeLeader || IsLowestId(member, squad)))
                    return SquadRole.Leader;
                return SquadRole.Front;
            }

            // Horse-archer / skirmish body — default Flanker.
            member.LooksLikeFlanker = true;
            return SquadRole.Flanker;
        }

        public override DoctrineOrderKind SelectOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            // Steppe FSM: kite → volley (FocusFire) → encircle (Flank) → berserk Charge only on cut-off.
            // NearStructure: tighter defense orbit (Hold / ProtectMissiles / short Flank) instead of deep kite.
            if (snapshot.ThreatCount <= 0)
                return DoctrineOrderKind.Hold;

            if (snapshot.IsBroken || snapshot.CasualtyRatio >= 0.35f)
                return DoctrineOrderKind.RetreatAndReform;

            var nearStruct = snapshot.NearStructure;
            var hasMissiles = snapshot.CountByRole(SquadRole.Missile) > 0;
            var hasBerserkers = snapshot.CountByRole(SquadRole.Front) > 0
                                || snapshot.CountByRole(SquadRole.Leader) > 0;
            var hasSkirmishers = snapshot.CountByRole(SquadRole.Flanker) > 0;

            // Village / totem defense: compress orbit, refuse deep kite away from home.
            if (nearStruct)
            {
                if (snapshot.NearestThreatDistance <= snapshot.ChargeRange)
                {
                    if (hasBerserkers && (snapshot.TargetIsolated || snapshot.ThreatStaggeredOrLow))
                        return DoctrineOrderKind.Charge;
                    return hasSkirmishers ? DoctrineOrderKind.Flank : DoctrineOrderKind.ProtectMissiles;
                }

                if (hasMissiles)
                {
                    if (snapshot.MissileThreatened)
                        return DoctrineOrderKind.ProtectMissiles;
                    return DoctrineOrderKind.FocusFire;
                }

                return DoctrineOrderKind.Flank;
            }

            // Open steppe: peel if pressed.
            // Hysteresis: stay in Kite (or RetreatAndReform) until gap reopens (d >= 8)
            // to avoid Kite↔Flank oscillation every tick.
            if (snapshot.NearestThreatDistance < 8f)
            {
                if (previous == DoctrineOrderKind.Kite
                    || previous == DoctrineOrderKind.RetreatAndReform)
                    return DoctrineOrderKind.Kite;
                return DoctrineOrderKind.Kite;
            }

            if (previous == DoctrineOrderKind.Charge)
                return DoctrineOrderKind.Kite;

            // Mid range: volley then encircle.
            if (hasMissiles && snapshot.NearestThreatDistance > snapshot.ChargeRange)
            {
                if (previous == DoctrineOrderKind.FocusFire || previous == DoctrineOrderKind.Flank)
                    return DoctrineOrderKind.Flank;
                return DoctrineOrderKind.FocusFire;
            }

            // Berserkers only on cut-off / staggered / low targets.
            if (hasBerserkers
                && snapshot.NearestThreatDistance <= snapshot.ChargeRange
                && (snapshot.TargetIsolated || snapshot.ThreatStaggeredOrLow || snapshot.FlankOpportunity))
            {
                return DoctrineOrderKind.Charge;
            }

            if (hasSkirmishers)
                return DoctrineOrderKind.Flank;

            return DoctrineOrderKind.Kite;
        }

        public static bool IsShamanOrArcher(SquadMemberView member)
            => Contains(member.PrefabName, "Shaman")
               || Contains(member.PrefabName, "Archer")
               || Contains(member.PrefabName, "Bow");

        public static bool IsBerserker(SquadMemberView member)
            => Contains(member.PrefabName, "Berserker")
               || Contains(member.PrefabName, "Brute")
               || Contains(member.PrefabName, "Elite")
               || member.LooksLikeHeavy;


    }
}
