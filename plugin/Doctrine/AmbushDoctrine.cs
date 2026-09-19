using System;
using System.Collections.Generic;
using System.Linq;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Greydwarf* → Black Forest Ambush predators.
    /// Guerrilla encircle: Orb/Skirmish orbit + Kite/Flank harassment.
    /// Refuse mass Charge (blob bum-rush); flash only on isolate/stagger or
    /// a rare aged-Flank envelope. After Charge → Kite, then re-encircle.
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

        /// <summary>Outside this → Hold/Kite lurk (meters).</summary>
        public const float OuterPocket = 18f;

        /// <summary>Inner harassment band lower edge (meters).</summary>
        public const float InnerBand = 8f;

        /// <summary>After Kite, re-encircle (Flank) once gap exceeds this.</summary>
        public const float ReEncircleGap = 14f;

        /// <summary>Rare envelope flash: nearest must be inside this.</summary>
        public const float EnvelopeFlashRange = 5f;

        /// <summary>Minimum seconds on Flank before any non-isolate flash.</summary>
        public const float MinFlankAgeForFlash = 3f;

        /// <summary>Anxiety break → Kite / Hold.</summary>
        public const float AnxietyCasualties = 0.22f;

        /// <summary>Envelope flash requires casualty ratio below this.</summary>
        public const float EnvelopeMaxCasualties = 0.12f;

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
            // --- Troll mobile fortress (addon) ---
            var synergy = TrollFortressHelper.SuggestSynergyOrder(snapshot, previous);
            if (synergy.HasValue)
                return synergy.Value;

            // 1) No threat → Hold (lurk / hide in pocket)
            if (snapshot.ThreatCount <= 0)
                return DoctrineOrderKind.Hold;

            // 2) Broken / high casualties → Kite, then Hold once gap reopens
            if (snapshot.IsBroken || snapshot.CasualtyRatio >= AnxietyCasualties)
            {
                if (previous == DoctrineOrderKind.Kite
                    && snapshot.NearestThreatDistance > ReEncircleGap)
                    return DoctrineOrderKind.Hold;
                return DoctrineOrderKind.Kite;
            }

            // 3) After any Charge → Kite (harassment loop; prefer over RetreatAndReform)
            if (previous == DoctrineOrderKind.Charge)
                return DoctrineOrderKind.Kite;

            // 4) After Kite until gap > 14f → Flank (re-encircle); else keep Kite
            if (previous == DoctrineOrderKind.Kite)
            {
                if (snapshot.NearestThreatDistance > ReEncircleGap)
                    return DoctrineOrderKind.Flank;
                return DoctrineOrderKind.Kite;
            }

            var hasShaman = snapshot.CountByRole(SquadRole.Missile) > 0;

            // 5) Outside 18f → Hold/Kite
            if (snapshot.NearestThreatDistance > OuterPocket)
            {
                if (previous == DoctrineOrderKind.Flank
                    || previous == DoctrineOrderKind.FocusFire
                    || previous == DoctrineOrderKind.ProtectMissiles)
                    return DoctrineOrderKind.Kite;
                return DoctrineOrderKind.Hold;
            }

            // 6) 8–18f → Flank (Orb) / shaman FocusFire from rear
            if (snapshot.NearestThreatDistance > InnerBand)
            {
                if (hasShaman
                    && previous != DoctrineOrderKind.Flank
                    && previous != DoctrineOrderKind.FocusFire
                    && previous != DoctrineOrderKind.ProtectMissiles)
                {
                    if (snapshot.MissileThreatened)
                        return DoctrineOrderKind.ProtectMissiles;
                    return DoctrineOrderKind.FocusFire;
                }

                if (hasShaman && (previous == DoctrineOrderKind.FocusFire || snapshot.MissileThreatened))
                    return DoctrineOrderKind.ProtectMissiles;

                return DoctrineOrderKind.Flank;
            }

            // 7) <8f → Flank/Kite primacy; Charge only flash (isolate/stagger/rare envelope)
            if (ShouldCommitFlash(snapshot, previous))
                return DoctrineOrderKind.Charge; // flash only — never default swarm Charge

            // Prefer orbit Flank; peel to Kite if already close without a flash window
            // and not isolated (avoid slugfest).
            if (!snapshot.TargetIsolated && !snapshot.ThreatStaggeredOrLow
                && previous == DoctrineOrderKind.Flank
                && snapshot.NearestThreatDistance < EnvelopeFlashRange)
                return DoctrineOrderKind.Kite;

            return DoctrineOrderKind.Flank;
        }

        /// <summary>
        /// Strict flash commit — refuses mass swarm Charge after Flank.
        /// Charge only if TargetIsolated OR ThreatStaggeredOrLow OR
        /// (Flank aged &gt; 3s AND nearest &lt; 5f AND casualtyRatio low).
        /// Brutes share the same gate (orbit Flank otherwise).
        /// </summary>
        private static bool ShouldCommitFlash(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            if (snapshot.NearestThreatDistance > snapshot.ChargeRange
                && snapshot.NearestThreatDistance > InnerBand)
                return false;

            // Isolate / stagger / low-HP: allow flash from Flank or FocusFire.
            if (snapshot.TargetIsolated || snapshot.ThreatStaggeredOrLow)
            {
                return previous == DoctrineOrderKind.Flank
                       || previous == DoctrineOrderKind.FocusFire;
            }

            // Rare envelope: aged Flank + very close + healthy pack.
            // Must NOT fire merely because previous was Flank (that was the bum-rush).
            if (previous == DoctrineOrderKind.Flank
                && snapshot.AgeSeconds >= MinFlankAgeForFlash
                && snapshot.NearestThreatDistance < EnvelopeFlashRange
                && snapshot.CasualtyRatio <= EnvelopeMaxCasualties)
            {
                return true;
            }

            return false;
        }

        public static bool IsShaman(SquadMemberView member)
            => Contains(member.PrefabName, "Shaman") || member.LooksLikeMissile;

        public static bool IsBrute(SquadMemberView member)
            => Contains(member.PrefabName, "Elite")
               || Contains(member.PrefabName, "Brute")
               || member.LooksLikeHeavy;
    }
}
