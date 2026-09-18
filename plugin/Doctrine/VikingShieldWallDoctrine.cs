using System;
using System.Collections.Generic;
using System.Linq;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Draugr* → Viking shield wall: line of shields, archers behind, charge, reform.
    /// Indoors/crypt bias: prefer choke Holds over open Advance when IndoorsOrCrypt is set.
    /// </summary>
    public sealed class VikingShieldWallDoctrine : DoctrinePackBase
    {
        public override string Id => "viking-shieldwall";
        public override string DisplayName => "VikingShieldWall";

        public override IReadOnlyList<string> PrefabPrefixes { get; } = new[]
        {
            "Draugr",
        };

        public override bool IsEnabled => PluginConfig.EnableVikingShieldWall?.Value ?? true;


        public override SquadRole AssignRole(SquadMemberView member, IReadOnlyList<SquadMemberView> squad)
        {
            if (IsArcher(member) || member.LooksLikeMissile)
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

            // Shield-wall body; light flankers only if clearly marked.
            if (!member.LooksLikeHeavy && member.LooksLikeFlanker)
            {
                var flankerCount = squad.Count(m => m.AssignedRole == SquadRole.Flanker);
                if (flankerCount < Math.Max(1, squad.Count / 5))
                    return SquadRole.Flanker;
            }

            member.LooksLikeHeavy = true;
            return SquadRole.Front;
        }

        public override DoctrineOrderKind SelectOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            // Viking shield-wall FSM:
            // Hold (wall) → Advance → ProtectMissiles/FocusFire → Charge → RetreatAndReform
            // IndoorsOrCrypt: choke bias — Hold/ProtectMissiles longer; Charge only when very close.
            if (snapshot.ThreatCount <= 0)
                return DoctrineOrderKind.Hold;

            if (snapshot.IsBroken || snapshot.CasualtyRatio >= 0.40f)
                return DoctrineOrderKind.RetreatAndReform;

            // After a charge, briefly reform the wall.
            if (previous == DoctrineOrderKind.Charge)
                return DoctrineOrderKind.RetreatAndReform;

            if (previous == DoctrineOrderKind.RetreatAndReform)
            {
                if (snapshot.NearestThreatDistance > snapshot.AdvanceRange)
                    return DoctrineOrderKind.Hold;
                return DoctrineOrderKind.Advance;
            }

            var hasMissiles = snapshot.CountByRole(SquadRole.Missile) > 0;
            var choke = snapshot.IndoorsOrCrypt;
            var chargeBand = choke
                ? Math.Min(snapshot.ChargeRange, 7f)
                : snapshot.ChargeRange;
            var advanceBand = choke
                ? Math.Min(snapshot.AdvanceRange, 18f)
                : snapshot.AdvanceRange;

            // Crypt/choke: prefer holding the wall at the bottleneck.
            if (choke && snapshot.NearestThreatDistance > chargeBand)
            {
                if (hasMissiles)
                {
                    if (snapshot.MissileThreatened || previous == DoctrineOrderKind.FocusFire)
                        return DoctrineOrderKind.ProtectMissiles;
                    return DoctrineOrderKind.FocusFire;
                }
                return DoctrineOrderKind.Hold;
            }

            if (snapshot.NearestThreatDistance > advanceBand)
                return DoctrineOrderKind.Advance;

            if (hasMissiles && snapshot.NearestThreatDistance > chargeBand)
            {
                if (previous != DoctrineOrderKind.FocusFire && snapshot.MissileThreatened)
                    return DoctrineOrderKind.ProtectMissiles;
                return DoctrineOrderKind.FocusFire;
            }

            if (snapshot.NearestThreatDistance <= chargeBand)
                return DoctrineOrderKind.Charge;

            return DoctrineOrderKind.Advance;
        }

        public static bool IsArcher(SquadMemberView member)
            => Contains(member.PrefabName, "Archer")
               || Contains(member.PrefabName, "Bow")
               || Contains(member.PrefabName, "Ranged");

        public static bool IsElite(SquadMemberView member)
            => Contains(member.PrefabName, "Elite")
               || Contains(member.PrefabName, "Captain")
               || Contains(member.PrefabName, "Lord");


    }
}
