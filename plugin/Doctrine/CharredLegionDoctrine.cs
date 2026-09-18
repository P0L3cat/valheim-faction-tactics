using System;
using System.Collections.Generic;
using System.Linq;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Ashlands Charred* dense ranks + rear casters/warlocks; Asksvin* as cavalry flankers
    /// that do not join the rank.
    /// </summary>
    public sealed class CharredLegionDoctrine : DoctrinePackBase
    {
        public override string Id => "charred-legion";
        public override string DisplayName => "CharredLegion";

        public override IReadOnlyList<string> PrefabPrefixes { get; } = new[]
        {
            "Charred",
            "Asksvin",
        };

        public override bool IsEnabled => PluginConfig.EnableCharredLegion?.Value ?? true;


        public override SquadRole AssignRole(SquadMemberView member, IReadOnlyList<SquadMemberView> squad)
        {
            // C: Asksvin = cavalry flankers — never Front/Leader rank.
            if (IsAsksvin(member))
            {
                member.LooksLikeFlanker = true;
                return SquadRole.Flanker;
            }

            if (IsCasterOrArcher(member) || member.LooksLikeMissile)
            {
                member.LooksLikeMissile = true;
                return SquadRole.Missile;
            }

            var hasLeader = squad.Any(m =>
                m.AssignedRole == SquadRole.Leader && m.InstanceId != member.InstanceId);
            if (!hasLeader && (member.LooksLikeLeader || IsElite(member) || IsLowestId(member, squad)))
            {
                member.LooksLikeHeavy = true;
                return SquadRole.Leader;
            }

            member.LooksLikeHeavy = true;
            return SquadRole.Front;
        }

        public override DoctrineOrderKind SelectOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            // Charred legion FSM: dense Hold/Advance → rear FocusFire → Flank (Asksvin) → Charge ranks.
            // Asksvin never force a Skirmish-only squad order when ranks want Charge; FlankOpportunity
            // opens a Flank beat for cavalry before/alongside the rank Charge.
            if (snapshot.ThreatCount <= 0)
                return DoctrineOrderKind.Hold;

            if (snapshot.IsBroken || snapshot.CasualtyRatio >= 0.42f)
                return DoctrineOrderKind.RetreatAndReform;

            // After Charge, reform beat (mirror Viking) — avoid Charge↔Advance ping-pong.
            if (previous == DoctrineOrderKind.Charge)
                return DoctrineOrderKind.RetreatAndReform;
            if (previous == DoctrineOrderKind.RetreatAndReform)
            {
                return snapshot.NearestThreatDistance > snapshot.ChargeRange
                    ? DoctrineOrderKind.Advance
                    : DoctrineOrderKind.Hold;
            }

            var hasCasters = snapshot.CountByRole(SquadRole.Missile) > 0;
            var hasCavalry = snapshot.CountByRole(SquadRole.Flanker) > 0;
            var hasRanks = snapshot.CountByRole(SquadRole.Front) > 0
                           || snapshot.CountByRole(SquadRole.Leader) > 0;

            if (snapshot.NearestThreatDistance > snapshot.AdvanceRange)
                return DoctrineOrderKind.Advance;

            if (hasCasters && snapshot.NearestThreatDistance > snapshot.ChargeRange)
            {
                if (snapshot.MissileThreatened)
                    return DoctrineOrderKind.ProtectMissiles;
                return DoctrineOrderKind.FocusFire;
            }

            // Cavalry flank beat before rank slam when opportunity exists.
            if (hasCavalry
                && snapshot.FlankOpportunity
                && previous != DoctrineOrderKind.Flank
                && snapshot.NearestThreatDistance <= snapshot.AdvanceRange)
            {
                return DoctrineOrderKind.Flank;
            }

            if (hasRanks && snapshot.NearestThreatDistance <= snapshot.ChargeRange)
                return DoctrineOrderKind.Charge;

            if (hasCavalry && snapshot.NearestThreatDistance <= snapshot.ChargeRange)
                return DoctrineOrderKind.Flank;

            return DoctrineOrderKind.Advance;
        }

        public static bool IsAsksvin(SquadMemberView member)
            => Contains(member.PrefabName, "Asksvin");

        public static bool IsCasterOrArcher(SquadMemberView member)
            => Contains(member.PrefabName, "Mage")
               || Contains(member.PrefabName, "Warlock")
               || Contains(member.PrefabName, "Archer")
               || Contains(member.PrefabName, "Bow")
               || Contains(member.PrefabName, "Caster");

        public static bool IsElite(SquadMemberView member)
            => Contains(member.PrefabName, "Elite")
               || Contains(member.PrefabName, "Captain")
               || Contains(member.PrefabName, "Lord");
        // Twitcher/Melee use LooksLikeLeader from discovery; do not treat all Melee as elite.


    }
}
