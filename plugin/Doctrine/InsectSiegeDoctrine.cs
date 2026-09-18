using System;
using System.Collections.Generic;
using System.Linq;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Mistlands insects: Soldiers front, Seekers flank/climb-ish, Ticks harass,
    /// Gjall artillery when present. Soften aggression near Dvergr (avoid stealing fights).
    /// Prefabs: Seeker*, Tick*, Gjall*.
    /// </summary>
    public sealed class InsectSiegeDoctrine : DoctrinePackBase
    {
        public override string Id => "insect-siege";
        public override string DisplayName => "InsectSiege";

        public override IReadOnlyList<string> PrefabPrefixes { get; } = new[]
        {
            "Seeker",
            "Tick",
            "Gjall",
        };

        public override bool IsEnabled => PluginConfig.EnableInsectSiege?.Value ?? true;


        public override SquadRole AssignRole(SquadMemberView member, IReadOnlyList<SquadMemberView> squad)
        {
            if (IsGjall(member))
            {
                member.LooksLikeMissile = true;
                return SquadRole.Missile;
            }

            if (IsSoldier(member))
            {
                member.LooksLikeHeavy = true;
                var hasLeader = squad.Any(m =>
                    m.AssignedRole == SquadRole.Leader && m.InstanceId != member.InstanceId);
                if (!hasLeader && (member.LooksLikeLeader || IsLowestId(member, squad)))
                    return SquadRole.Leader;
                return SquadRole.Front;
            }

            // Seekers + Ticks: flank / climb / harass.
            member.LooksLikeFlanker = true;
            return SquadRole.Flanker;
        }

        public override DoctrineOrderKind SelectOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            // Insect siege FSM:
            // Soften near Dvergr → Hold/Kite (reduce commit).
            // Else: Advance soldiers → Gjall FocusFire → Seekers/Ticks Flank → Charge commit.
            if (snapshot.ThreatCount <= 0)
                return DoctrineOrderKind.Hold;

            // Break / casualty withdraw before Dvergr soften — do not kite forever while shattered.
            if (snapshot.IsBroken || snapshot.CasualtyRatio >= 0.40f)
                return DoctrineOrderKind.RetreatAndReform;

            // C: Soften aggression near Dvergr — don't steal their fights.
            if (snapshot.NearDvergr)
            {
                if (snapshot.NearestThreatDistance <= snapshot.ChargeRange)
                    return DoctrineOrderKind.Kite;
                if (snapshot.CountByRole(SquadRole.Missile) > 0)
                    return DoctrineOrderKind.FocusFire;
                return DoctrineOrderKind.Hold;
            }

            var hasGjall = snapshot.CountByRole(SquadRole.Missile) > 0;
            var hasSoldiers = snapshot.CountByRole(SquadRole.Front) > 0
                              || snapshot.CountByRole(SquadRole.Leader) > 0;
            var hasFlankers = snapshot.CountByRole(SquadRole.Flanker) > 0;

            if (snapshot.NearestThreatDistance > snapshot.AdvanceRange)
                return DoctrineOrderKind.Advance;

            // Artillery soften while pack envelopes.
            if (hasGjall && snapshot.NearestThreatDistance > snapshot.ChargeRange)
            {
                if (previous == DoctrineOrderKind.FocusFire && hasFlankers)
                    return DoctrineOrderKind.Flank;
                return DoctrineOrderKind.FocusFire;
            }

            if (hasFlankers && snapshot.NearestThreatDistance > snapshot.ChargeRange * 0.7f
                && previous != DoctrineOrderKind.Charge)
            {
                return DoctrineOrderKind.Flank;
            }

            if (snapshot.NearestThreatDistance <= snapshot.ChargeRange)
            {
                // Soldiers commit; Seekers already flanking may Charge after envelope.
                if (hasSoldiers || previous == DoctrineOrderKind.Flank)
                    return DoctrineOrderKind.Charge;
                return DoctrineOrderKind.Flank;
            }

            return hasSoldiers ? DoctrineOrderKind.Advance : DoctrineOrderKind.Flank;
        }

        public static bool IsGjall(SquadMemberView member)
            => Contains(member.PrefabName, "Gjall");

        public static bool IsSoldier(SquadMemberView member)
            => Contains(member.PrefabName, "Soldier")
               || member.LooksLikeHeavy;

        public static bool IsTick(SquadMemberView member)
            => Contains(member.PrefabName, "Tick");
    }
}
