using System;
using System.Collections.Generic;
using System.Linq;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Greydwarf* → Black Forest Ambush predators.
    /// Refuse fair fights: lurk → multi-angle flash → disperse → re-ambush.
    /// High anxiety, low slugfest. TrollFortress synergy consulted when enabled.
    /// </summary>
    public sealed class AmbushDoctrine : DoctrinePackBase
    {
        public override string Id => "ambush";
        public override string DisplayName => "Ambush";

        /// <summary>
        /// Prefab family: Greydwarf, Greydwarf_Elite (Brute), Greydwarf_Shaman.
        /// MatchesPrefab applies extra excludes (Root / Greyling).
        /// </summary>
        public override IReadOnlyList<string> PrefabPrefixes { get; } = new[]
        {
            "Greydwarf",
        };

        public override bool IsEnabled => PluginConfig.EnableAmbush?.Value ?? true;

        public override bool MatchesPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return false;

            // Exclude related-but-not-squad entities.
            if (Contains(prefabName, "Root") || Contains(prefabName, "Greyling"))
                return false;

            foreach (var prefix in PrefabPrefixes)
            {
                if (prefabName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public override SquadRole AssignRole(SquadMemberView member, IReadOnlyList<SquadMemberView> squad)
        {
            // Roles: shaman (opener/poison) → Missile; brute → Front (heavy);
            // swarm normals → Flanker. One Leader among brutes/lowest id if needed.
            if (IsShaman(member))
            {
                member.LooksLikeMissile = true;
                return SquadRole.Missile;
            }

            if (IsBrute(member))
            {
                member.LooksLikeHeavy = true;
                var hasLeader = squad.Any(m =>
                    m.AssignedRole == SquadRole.Leader && m.InstanceId != member.InstanceId);
                if (!hasLeader && (member.LooksLikeLeader || IsLowestId(member, squad)))
                    return SquadRole.Leader;
                return SquadRole.Front;
            }

            // Swarm: multi-angle predators.
            member.LooksLikeFlanker = true;
            return SquadRole.Flanker;
        }

        public override DoctrineOrderKind SelectOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            // Ambush / Black Forest ranges: tight pocket, early break-off.
            // Local pocket constants; snapshot AdvanceRange/ChargeRange also set for Ambush.
            const float pocketEntry = 16f;   // players enter ambush pocket
            const float flashRange = 9f;    // multi-angle flash charge
            const float reAmbushGap = 22f;  // after disperse, hold until gap reopens
            const float anxietyCasualties = 0.22f;

            // --- Troll mobile fortress (addon) ---
            var synergy = TrollFortressHelper.SuggestSynergyOrder(snapshot, previous);
            if (synergy.HasValue)
                return synergy.Value;

            // 1) No threat → Hold (lurk / hide in pocket)
            if (snapshot.ThreatCount <= 0)
                return DoctrineOrderKind.Hold;

            // 2) Broken / anxiety break → Disperse (RetreatAndReform), then re-ambush via Hold
            if (snapshot.IsBroken || snapshot.CasualtyRatio >= anxietyCasualties)
                return DoctrineOrderKind.RetreatAndReform;

            // After a flash charge, refuse slugfest: disperse even if casualties low.
            if (previous == DoctrineOrderKind.Charge)
                return DoctrineOrderKind.RetreatAndReform;

            // Re-ambush: after disperse, Hold (hide) until pocket is quiet / threat far.
            if (previous == DoctrineOrderKind.RetreatAndReform)
            {
                if (snapshot.NearestThreatDistance > reAmbushGap)
                    return DoctrineOrderKind.Hold;
                // Still hot: kite away rather than re-commit.
                return DoctrineOrderKind.Kite;
            }

            // 3) Threat outside pocket → Hold (refuse fair approach / stay hidden)
            if (snapshot.NearestThreatDistance > pocketEntry)
                return DoctrineOrderKind.Hold;

            var hasShaman = snapshot.CountByRole(SquadRole.Missile) > 0;
            var hasSwarm = snapshot.CountByRole(SquadRole.Flanker) > 0;
            var hasBrute = snapshot.CountByRole(SquadRole.Front) > 0
                           || snapshot.CountByRole(SquadRole.Leader) > 0;

            // 4) Players just entered pocket → shaman opener, then multi-angle Flank
            if (snapshot.NearestThreatDistance > flashRange)
            {
                if (hasShaman && previous != DoctrineOrderKind.FocusFire
                    && previous != DoctrineOrderKind.Flank)
                {
                    // Poison / opener pressure; hang back (ProtectMissiles if already focusing).
                    if (previous == DoctrineOrderKind.FocusFire || snapshot.MissileThreatened)
                        return DoctrineOrderKind.ProtectMissiles;
                    return DoctrineOrderKind.FocusFire;
                }

                return DoctrineOrderKind.Flank;
            }

            // 5) Flash charge window — swarm Flank→Charge; brute only if target isolated/low
            // Hysteresis: once peeling (Kite) while not isolated, hold Kite until gap or isolate
            // to avoid Flank↔Kite oscillation every tick.
            if (previous == DoctrineOrderKind.Kite
                && snapshot.NearestThreatDistance <= flashRange
                && !snapshot.TargetIsolated)
                return DoctrineOrderKind.Kite;

            if (hasSwarm && previous != DoctrineOrderKind.Flank && previous != DoctrineOrderKind.Charge
                && previous != DoctrineOrderKind.Kite)
                return DoctrineOrderKind.Flank;

            if (ShouldCommitFlash(snapshot, hasBrute, hasSwarm, previous))
                return DoctrineOrderKind.Charge;

            // Anxiety: if we would otherwise slugfest, peel.
            if (previous == DoctrineOrderKind.Flank && !snapshot.TargetIsolated)
                return DoctrineOrderKind.Kite;

            return DoctrineOrderKind.Flank;
        }

        /// <summary>
        /// Flash commit: swarm may charge after Flank; brute (Front/Leader) only when
        /// target is isolated / staggered proxy / already low (TargetIsolated / FlankOpportunity).
        /// </summary>
        private static bool ShouldCommitFlash(
            SquadSnapshot snapshot,
            bool hasBrute,
            bool hasSwarm,
            DoctrineOrderKind? previous)
        {
            if (snapshot.NearestThreatDistance > snapshot.ChargeRange
                && snapshot.NearestThreatDistance > 9f)
                return false;

            // Pure swarm flash after envelope.
            if (hasSwarm && previous == DoctrineOrderKind.Flank)
                return true;

            // Brute: only commit if target isolated / staggered / low (snapshot flags).
            if (hasBrute)
            {
                if (snapshot.TargetIsolated || snapshot.ThreatStaggeredOrLow)
                    return previous == DoctrineOrderKind.Flank || previous == DoctrineOrderKind.FocusFire;
                return false;
            }

            return previous == DoctrineOrderKind.Flank;
        }

        public static bool IsShaman(SquadMemberView member)
            => Contains(member.PrefabName, "Shaman") || member.LooksLikeMissile;

        public static bool IsBrute(SquadMemberView member)
            => Contains(member.PrefabName, "Elite")
               || Contains(member.PrefabName, "Brute")
               || member.LooksLikeHeavy;


    }
}
