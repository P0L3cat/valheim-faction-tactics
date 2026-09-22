using System;
using System.Collections.Generic;
using System.Linq;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Skeleton* → Roman: shield wall + archers first, not charge-heavy.
    /// Default ProtectMissiles / FocusFire under ShieldWall when missiles + threat in range
    /// (not eternal Hold). Front HoldGround; missiles PreferKeepRange.
    /// Advance only to close into the ~14–18m wall band; Charge almost never.
    /// When PreferRanged: Charge only last-resort casualties (never default Charge).
    /// Phase B: scored transitions + hysteresis. Wall band is <c>ft set</c>able.
    /// </summary>
    public sealed class RomanDoctrine : DoctrinePackBase
    {
        /// <summary>Advance until threat is inside this outer wall band (meters).</summary>
        public const float WallOuter = 18f;

        /// <summary>Prefer Hold wall once inside this inner band (meters).</summary>
        public const float WallInner = 14f;

        /// <summary>Legacy no-missile hold line (meters).</summary>
        public const float NoMissileHoldLine = 12f;

        /// <summary>Tight band once a rare Charge is committed (meters).</summary>
        public const float ChargeCommitBand = 5f;

        /// <summary>Casualty ratio treated as last-resort melee commit.</summary>
        public const float LastResortCasualtyRatio = 0.35f;

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
            // Hard gates — not score flips. Threat lost and shattered packs leave immediately.
            if (snapshot.ThreatCount <= 0)
                return DoctrineOrderKind.Hold;
            if (snapshot.IsBroken || snapshot.CasualtyRatio >= 0.45f)
                return DoctrineOrderKind.RetreatAndReform;

            var scores = ScoreOrders(snapshot, previous);
            OrderTransition.FoldActionScorer(scores, snapshot);
            return OrderTransition.Pick(scores, previous);
        }

        /// <summary>
        /// Missile line inside the wall band. Charge is vetoed unless <see cref="ShouldCharge"/>.
        /// Kite and Flank stay vetoed. Near the wall edges, scores compress so hysteresis
        /// stops one-step Advance/Focus flicker.
        /// </summary>
        internal static Dictionary<DoctrineOrderKind, float> ScoreOrders(
            SquadSnapshot snapshot,
            DoctrineOrderKind? previous)
        {
            var scores = OrderTransition.Blank();
            var wall = LiveWall();
            var outer = wall.outer;
            var inner = wall.inner;
            var d = snapshot.NearestThreatDistance;
            var hasMissiles = snapshot.CountByRole(SquadRole.Missile) > 0;
            var preferRanged = PreferRanged;
            var chargeBand = EffectiveChargeRange(snapshot);
            var should = ShouldCharge(snapshot, hasMissiles, preferRanged, chargeBand);
            // Commit band cannot keep a Charge that the charge gate already rejected.
            if (should && previous == DoctrineOrderKind.Charge && d > ChargeCommitBand)
                should = false;

            if (d > outer || d > snapshot.AdvanceRange)
            {
                scores[DoctrineOrderKind.Advance] = 3.1f;
                scores[DoctrineOrderKind.FocusFire] = hasMissiles ? 0.55f : -8f;
                scores[DoctrineOrderKind.Hold] = 0.4f;
                scores[DoctrineOrderKind.Charge] = -8f;
                OrderTransition.SoftenEdge(
                    scores,
                    DoctrineOrderKind.Advance,
                    hasMissiles ? DoctrineOrderKind.FocusFire : DoctrineOrderKind.Hold,
                    d,
                    Math.Min(outer, snapshot.AdvanceRange));
                return scores;
            }

            if (hasMissiles)
            {
                var press = d <= outer
                            && (snapshot.MissileThreatened
                                || previous == DoctrineOrderKind.ProtectMissiles
                                || previous == DoctrineOrderKind.FocusFire
                                || d <= inner);
                if (should)
                {
                    scores[DoctrineOrderKind.Charge] = 3.6f;
                    scores[DoctrineOrderKind.ProtectMissiles] = 1.5f;
                    scores[DoctrineOrderKind.FocusFire] = 1.2f;
                    scores[DoctrineOrderKind.Hold] = 0.8f;
                }
                else
                {
                    scores[DoctrineOrderKind.Charge] = -8f;
                    scores[DoctrineOrderKind.Hold] = 0.35f;
                    if (press)
                    {
                        scores[DoctrineOrderKind.ProtectMissiles] = 2.8f;
                        scores[DoctrineOrderKind.FocusFire] = 1.35f;
                    }
                    else
                    {
                        scores[DoctrineOrderKind.FocusFire] = 2.8f;
                        scores[DoctrineOrderKind.ProtectMissiles] = 1.2f;
                    }
                }

                var span = Math.Max(0.01f, outer - inner);
                scores[DoctrineOrderKind.Advance] = d > inner
                    ? 1.05f + 0.35f * ((d - inner) / span)
                    : 0.4f;
                OrderTransition.SoftenEdge(scores, DoctrineOrderKind.Advance, DoctrineOrderKind.FocusFire, d, outer);
                OrderTransition.SoftenEdge(
                    scores,
                    DoctrineOrderKind.FocusFire,
                    DoctrineOrderKind.ProtectMissiles,
                    d,
                    inner);
                return scores;
            }

            if (preferRanged)
            {
                if (should)
                {
                    scores[DoctrineOrderKind.Charge] = 3.5f;
                    scores[DoctrineOrderKind.Hold] = 1.4f;
                    scores[DoctrineOrderKind.Advance] = 0.6f;
                }
                else
                {
                    scores[DoctrineOrderKind.Charge] = -8f;
                    if (d > inner)
                    {
                        scores[DoctrineOrderKind.Advance] = 2.8f;
                        scores[DoctrineOrderKind.Hold] = 1.0f;
                    }
                    else
                    {
                        scores[DoctrineOrderKind.Hold] = 2.8f;
                        scores[DoctrineOrderKind.Advance] = 0.55f;
                    }
                }

                OrderTransition.SoftenEdge(scores, DoctrineOrderKind.Hold, DoctrineOrderKind.Advance, d, inner);
                return scores;
            }

            // PreferRanged off, no missiles: Advance outside the hold line, Charge only in band.
            scores[DoctrineOrderKind.Charge] = should ? 3.4f : -8f;
            if (d > chargeBand)
            {
                if (d > NoMissileHoldLine)
                {
                    scores[DoctrineOrderKind.Advance] = 2.8f;
                    scores[DoctrineOrderKind.Hold] = 1.0f;
                }
                else
                {
                    scores[DoctrineOrderKind.Hold] = 2.8f;
                    scores[DoctrineOrderKind.Advance] = 0.7f;
                }
            }
            else
            {
                scores[DoctrineOrderKind.Hold] = should ? 1.2f : 2.6f;
                scores[DoctrineOrderKind.Advance] = 0.4f;
            }

            return scores;
        }

        /// <summary>Live wall from <c>ft set</c>, clamped so inner stays inside outer.</summary>
        public static (float outer, float inner) LiveWall()
        {
            var outer = PluginConfig.RomanWallOuter?.Value ?? WallOuter;
            var inner = PluginConfig.RomanWallInner?.Value ?? WallInner;
            return ResolveWallBand(outer, inner);
        }

        public static (float outer, float inner) ResolveWallBand(float outer, float inner)
        {
            if (outer < 2f || float.IsNaN(outer) || float.IsInfinity(outer))
                outer = WallOuter;
            if (inner < 1f || float.IsNaN(inner) || float.IsInfinity(inner))
                inner = WallInner;
            if (inner >= outer)
                inner = Math.Max(1f, outer - 1f);
            return (outer, inner);
        }

        /// <summary>
        /// Charge gate: nearest &lt; chargeBand AND (no missiles left OR last-resort casualties).
        /// When missiles remain and PreferRanged, refuse Charge entirely unless last-resort.
        /// </summary>
        public static bool ShouldCharge(
            SquadSnapshot snapshot,
            bool hasMissiles,
            bool preferRanged,
            float chargeBand)
        {
            if (snapshot.NearestThreatDistance > chargeBand)
                return false;

            // Last resort: morale crumbling but not yet fully broken.
            var lastResort = snapshot.CasualtyRatio >= LastResortCasualtyRatio;

            // PreferRanged: never default Charge — Hold/ProtectMissiles/FocusFire primacy
            // even when missiles are gone. Charge only on last-resort casualties.
            if (preferRanged)
                return lastResort;

            // PreferRanged off: melee commit when inside band and no missiles (or last resort).
            return !hasMissiles || lastResort;
        }

        public static bool PreferRanged => PluginConfig.RomanPreferRanged?.Value ?? true;

        /// <summary>
        /// Roman effective charge range: min(snapshot.ChargeRange, config RomanChargeRange default 3.5).
        /// </summary>
        public static float EffectiveChargeRange(SquadSnapshot snapshot)
        {
            var configured = PluginConfig.RomanChargeRange?.Value ?? 3.5f;
            if (configured < 1f)
                configured = 3.5f;
            return Math.Min(snapshot.ChargeRange, configured);
        }
    }
}
