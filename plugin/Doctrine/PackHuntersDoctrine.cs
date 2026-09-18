using System;
using System.Collections.Generic;
using System.Linq;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Mountain pack: Wolves encircle/hamstring; Drake (Hatchling) overwatch/strafe/punish clump.
    /// Prefabs: Wolf*, Drake* (incl. Hatchling).
    /// </summary>
    public sealed class PackHuntersDoctrine : DoctrinePackBase
    {
        public override string Id => "pack-hunters";
        public override string DisplayName => "PackHunters";

        public override IReadOnlyList<string> PrefabPrefixes { get; } = new[]
        {
            "Wolf",
            "Drake",
            "Hatchling",
        };

        public override bool IsEnabled => PluginConfig.EnablePackHunters?.Value ?? true;

        public override SquadRole AssignRole(SquadMemberView member, IReadOnlyList<SquadMemberView> squad)
        {
            if (IsDrake(member))
            {
                member.LooksLikeMissile = true;
                return SquadRole.Missile;
            }

            // Wolves: pack flankers; one Leader (alpha).
            member.LooksLikeFlanker = true;
            var hasLeader = squad.Any(m =>
                m.AssignedRole == SquadRole.Leader && m.InstanceId != member.InstanceId);
            if (!hasLeader && (member.LooksLikeLeader || IsLowestId(member, squad)))
                return SquadRole.Leader;

            return SquadRole.Flanker;
        }

        public override DoctrineOrderKind SelectOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            // Pack hunters FSM:
            // Hold/overwatch → Drake FocusFire (strafe / punish clump) → Wolf Flank encircle
            // → Charge hamstring on isolated → Kite if broken / overcommitted.
            if (snapshot.ThreatCount <= 0)
                return DoctrineOrderKind.Hold;

            if (snapshot.IsBroken || snapshot.CasualtyRatio >= 0.30f)
                return DoctrineOrderKind.RetreatAndReform;

            if (previous == DoctrineOrderKind.Charge)
                return DoctrineOrderKind.Kite;

            var hasDrake = snapshot.CountByRole(SquadRole.Missile) > 0;
            var hasWolves = snapshot.CountByRole(SquadRole.Flanker) > 0
                            || snapshot.CountByRole(SquadRole.Leader) > 0;

            if (snapshot.NearestThreatDistance > snapshot.AdvanceRange)
            {
                if (hasDrake)
                    return DoctrineOrderKind.FocusFire;
                return DoctrineOrderKind.Advance;
            }

            // Punish clump: players bunched (not isolated) → Drake FocusFire / strafe.
            var clump = !snapshot.TargetIsolated && snapshot.ThreatCount >= 1
                        && snapshot.NearestThreatDistance <= snapshot.AdvanceRange;
            if (hasDrake && clump && previous != DoctrineOrderKind.Flank
                && snapshot.NearestThreatDistance > snapshot.ChargeRange * 0.6f)
            {
                return DoctrineOrderKind.FocusFire;
            }

            // Encircle / hamstring.
            if (hasWolves && previous != DoctrineOrderKind.Charge)
            {
                if (snapshot.TargetIsolated || snapshot.ThreatStaggeredOrLow
                    || (previous == DoctrineOrderKind.Flank
                        && snapshot.NearestThreatDistance <= snapshot.ChargeRange))
                {
                    return DoctrineOrderKind.Charge;
                }
                return DoctrineOrderKind.Flank;
            }

            if (hasDrake)
                return DoctrineOrderKind.FocusFire;

            return DoctrineOrderKind.Flank;
        }

        public static bool IsDrake(SquadMemberView member)
            => Contains(member.PrefabName, "Drake")
               || Contains(member.PrefabName, "Hatchling")
               || member.LooksLikeMissile;


    }
}
