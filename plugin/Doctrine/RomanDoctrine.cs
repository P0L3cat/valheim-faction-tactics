using System;
using System.Collections.Generic;
using System.Linq;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Skeleton* → Roman: shield line, missiles behind, flanks, disciplined reform.
    /// Fully implemented for the first spike.
    /// </summary>
    public sealed class RomanDoctrine : DoctrinePackBase
    {
        public override string Id => "roman";
        public override string DisplayName => "Roman";

        public override IReadOnlyList<string> PrefabPrefixes { get; } = new[]
        {
            "Skeleton",
        };

        public override bool IsEnabled => PluginConfig.EnableRoman?.Value ?? true;

        public override SquadRole AssignRole(SquadMemberView member, IReadOnlyList<SquadMemberView> squad)
        {
            // Heuristics: bow/missile → Missile; named "leader" / first by id → Leader;
            // agile/light → Flanker; default Front (hastati line).
            if (member.LooksLikeMissile)
                return SquadRole.Missile;

            var hasLeader = squad.Any(m => m.AssignedRole == SquadRole.Leader && m.InstanceId != member.InstanceId);
            if (!hasLeader && (member.LooksLikeLeader || IsLowestId(member, squad)))
                return SquadRole.Leader;

            var flankerCount = squad.Count(m => m.AssignedRole == SquadRole.Flanker);
            var targetFlankers = Math.Max(1, squad.Count / 4);
            if (!member.LooksLikeHeavy && flankerCount < targetFlankers && member.LooksLikeFlanker)
                return SquadRole.Flanker;

            return SquadRole.Front;
        }

        public override DoctrineOrderKind SelectOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            // Roman FSM (scripted):
            // 1) No threat → Hold
            // 2) Threat far → Advance
            // 3) Missiles present & threat mid → ProtectMissiles / FocusFire
            // 4) Threat close + morale OK → Charge (or Flank if flanks ready)
            // 5) Casualties / broken line → RetreatAndReform
            if (snapshot.ThreatCount <= 0)
                return DoctrineOrderKind.Hold;

            if (snapshot.IsBroken || snapshot.CasualtyRatio >= 0.45f)
                return DoctrineOrderKind.RetreatAndReform;

            var hasMissiles = snapshot.CountByRole(SquadRole.Missile) > 0;
            var hasFlankers = snapshot.CountByRole(SquadRole.Flanker) > 0;

            if (snapshot.NearestThreatDistance > snapshot.AdvanceRange)
                return DoctrineOrderKind.Advance;

            if (hasMissiles && snapshot.NearestThreatDistance > snapshot.ChargeRange)
            {
                // Prefer protecting the rear rank unless already focusing fire.
                if (previous != DoctrineOrderKind.FocusFire && snapshot.MissileThreatened)
                    return DoctrineOrderKind.ProtectMissiles;
                return DoctrineOrderKind.FocusFire;
            }

            if (hasFlankers && snapshot.NearestThreatDistance <= snapshot.ChargeRange
                && previous != DoctrineOrderKind.Flank
                && snapshot.FlankOpportunity)
            {
                return DoctrineOrderKind.Flank;
            }

            if (snapshot.NearestThreatDistance <= snapshot.ChargeRange)
                return DoctrineOrderKind.Charge;

            return DoctrineOrderKind.Advance;
        }

    }
}
