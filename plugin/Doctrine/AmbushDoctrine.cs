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
    /// Refuse mass Charge (blob bum-rush); flash on isolate, stagger, or
    /// FlankOpportunity, else kite in contact. After Charge → force Kite, then re-encircle.
    /// Phase B: scored transitions + hysteresis. Post-Charge Kite bypasses min dwell.
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

        /// <summary>Outside this → Hold/Kite lurk (meters). Default; live value from config.</summary>
        public const float OuterPocket = 18f;

        /// <summary>Inner harassment band lower edge (meters). Default; live value from config.</summary>
        public const float InnerBand = 8f;

        /// <summary>After Kite, re-encircle (Flank) once gap exceeds this. Default; live value from config.</summary>
        public const float ReEncircleGap = 14f;

        static float OuterPocketLive => PluginConfig.AmbushOuterPocket?.Value ?? OuterPocket;
        static float InnerBandLive => PluginConfig.AmbushInnerBand?.Value ?? InnerBand;
        static float ReEncircleGapLive => PluginConfig.AmbushReEncircleGap?.Value ?? ReEncircleGap;

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
            // Troll fortress addon stays a hard suggestion (not a scored flip).
            var synergy = TrollFortressHelper.SuggestSynergyOrder(snapshot, previous);
            if (synergy.HasValue)
                return synergy.Value;

            // Hard gates. Post-Charge Kite is the explicit force that bypasses min dwell.
            if (snapshot.ThreatCount <= 0)
                return DoctrineOrderKind.Hold;

            if (snapshot.IsBroken || snapshot.CasualtyRatio >= AnxietyCasualties)
            {
                if (previous == DoctrineOrderKind.Kite
                    && snapshot.NearestThreatDistance > ReEncircleGapLive)
                    return DoctrineOrderKind.Hold;
                return DoctrineOrderKind.Kite;
            }

            if (previous == DoctrineOrderKind.Charge)
                return DoctrineOrderKind.Kite;

            var scores = ScoreOrders(snapshot, previous);
            OrderTransition.FoldActionScorer(scores, snapshot);
            return OrderTransition.Pick(scores, previous);
        }

        /// <summary>
        /// Orbit in the pocket, kite in contact unless a flash window is open.
        /// Charge is vetoed outside that window (no blob rush, no HoldGround stand).
        /// </summary>
        internal static Dictionary<DoctrineOrderKind, float> ScoreOrders(
            SquadSnapshot snapshot,
            DoctrineOrderKind? previous)
        {
            var scores = OrderTransition.Blank();
            var d = snapshot.NearestThreatDistance;
            var outer = OuterPocketLive;
            var inner = InnerBandLive;
            var reGap = ReEncircleGapLive;
            var hasShaman = snapshot.CountByRole(SquadRole.Missile) > 0;

            // Re-encircle only after the gap opens. Do not flash straight out of Kite.
            if (previous == DoctrineOrderKind.Kite)
            {
                var open = d > reGap;
                scores[DoctrineOrderKind.Flank] = open ? 2.7f : 0.85f;
                scores[DoctrineOrderKind.Kite] = open ? 0.85f : 2.7f;
                scores[DoctrineOrderKind.Hold] = 0.3f;
                scores[DoctrineOrderKind.Charge] = -8f;
                OrderTransition.SoftenEdge(scores, DoctrineOrderKind.Flank, DoctrineOrderKind.Kite, d, reGap);
                return scores;
            }

            if (d > outer)
            {
                var peeling = previous == DoctrineOrderKind.Flank
                              || previous == DoctrineOrderKind.FocusFire
                              || previous == DoctrineOrderKind.ProtectMissiles;
                if (peeling)
                {
                    scores[DoctrineOrderKind.Kite] = 2.7f;
                    scores[DoctrineOrderKind.Hold] = 0.8f;
                }
                else
                {
                    scores[DoctrineOrderKind.Hold] = 2.7f;
                    scores[DoctrineOrderKind.Kite] = 0.7f;
                }

                scores[DoctrineOrderKind.Flank] = 0.4f;
                scores[DoctrineOrderKind.Charge] = -8f;
                OrderTransition.SoftenEdge(scores, DoctrineOrderKind.Hold, DoctrineOrderKind.Kite, d, outer);
                return scores;
            }

            if (d > inner)
            {
                var orbiting = previous == DoctrineOrderKind.Flank
                               || previous == DoctrineOrderKind.FocusFire
                               || previous == DoctrineOrderKind.ProtectMissiles;
                if (hasShaman && !orbiting)
                {
                    if (snapshot.MissileThreatened)
                    {
                        scores[DoctrineOrderKind.ProtectMissiles] = 2.7f;
                        scores[DoctrineOrderKind.FocusFire] = 1.2f;
                    }
                    else
                    {
                        scores[DoctrineOrderKind.FocusFire] = 2.7f;
                        scores[DoctrineOrderKind.ProtectMissiles] = 1.15f;
                    }

                    scores[DoctrineOrderKind.Flank] = 1.35f;
                    scores[DoctrineOrderKind.Kite] = 0.4f;
                }
                else if (hasShaman && (previous == DoctrineOrderKind.FocusFire || snapshot.MissileThreatened))
                {
                    scores[DoctrineOrderKind.ProtectMissiles] = 2.7f;
                    scores[DoctrineOrderKind.FocusFire] = 1.45f;
                    scores[DoctrineOrderKind.Flank] = 1.2f;
                    scores[DoctrineOrderKind.Kite] = 0.4f;
                }
                else
                {
                    scores[DoctrineOrderKind.Flank] = 2.7f;
                    scores[DoctrineOrderKind.FocusFire] = 1.0f;
                    scores[DoctrineOrderKind.ProtectMissiles] = 0.8f;
                    scores[DoctrineOrderKind.Kite] = 0.55f;
                }

                scores[DoctrineOrderKind.Hold] = 0.25f;
                scores[DoctrineOrderKind.Charge] = -8f;
                OrderTransition.SoftenEdge(scores, DoctrineOrderKind.Flank, DoctrineOrderKind.Kite, d, inner);
                return scores;
            }

            // Contact: flash on isolate / stagger / flank opportunity, otherwise kite.
            if (ShouldCommitFlash(snapshot, previous))
            {
                scores[DoctrineOrderKind.Charge] = 3.2f;
                scores[DoctrineOrderKind.Kite] = 1.3f;
                scores[DoctrineOrderKind.Flank] = 1.05f;
                scores[DoctrineOrderKind.Hold] = -4f;
            }
            else
            {
                scores[DoctrineOrderKind.Kite] = d < EnvelopeFlashRange ? 2.9f : 2.75f;
                scores[DoctrineOrderKind.Flank] = 1.25f;
                scores[DoctrineOrderKind.Hold] = -4f;
                scores[DoctrineOrderKind.Charge] = -8f;
            }

            return scores;
        }

        /// <summary>
        /// Flash only from an orbit (Flank / FocusFire), never from first contact.
        /// Window: TargetIsolated, ThreatStaggeredOrLow, or FlankOpportunity
        /// (one player in the contact band, or players split &gt; FlankSplitMeters),
        /// or a rare aged envelope. Brutes use the same gate.
        /// </summary>
        private static bool ShouldCommitFlash(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            if (snapshot.NearestThreatDistance > snapshot.ChargeRange
                && snapshot.NearestThreatDistance > InnerBandLive)
                return false;

            var fromOrbit = previous == DoctrineOrderKind.Flank
                            || previous == DoctrineOrderKind.FocusFire;
            if (!fromOrbit)
                return false;

            if (snapshot.TargetIsolated || snapshot.ThreatStaggeredOrLow || snapshot.FlankOpportunity)
                return true;

            // Rare envelope: aged Flank + very close + healthy pack.
            // Must NOT fire merely because previous was Flank (that was the bum-rush).
            return previous == DoctrineOrderKind.Flank
                   && snapshot.AgeSeconds >= MinFlankAgeForFlash
                   && snapshot.NearestThreatDistance < EnvelopeFlashRange
                   && snapshot.CasualtyRatio <= EnvelopeMaxCasualties;
        }

        public static bool IsShaman(SquadMemberView member)
            => Contains(member.PrefabName, "Shaman") || member.LooksLikeMissile;

        public static bool IsBrute(SquadMemberView member)
            => Contains(member.PrefabName, "Elite")
               || Contains(member.PrefabName, "Brute")
               || member.LooksLikeHeavy;
    }
}
