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
    /// Advance only to close into the ~14–18m wall band; Charge almost never
    /// (nearest &lt; RomanChargeRange default 3.5m AND no missiles, or morale last resort).
    /// </summary>
    public sealed class RomanDoctrine : DoctrinePackBase
    {
        /// <summary>Advance until threat is inside this outer wall band (meters).</summary>
        public const float WallOuter = 18f;

        /// <summary>Prefer Hold wall once inside this inner band (meters).</summary>
        public const float WallInner = 14f;

        /// <summary>Legacy no-missile hold line (meters).</summary>
        public const float NoMissileHoldLine = 12f;

        /// <summary>Tight hysteresis once a rare Charge is committed.</summary>
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
            // Roman FSM — shield wall + archers:
            // 1) No threat → Hold
            // 2) Broken / very high casualties → RetreatAndReform
            // 3) Far → Advance toward 14–18m wall band, then Hold
            // 4) Has missiles in range → FocusFire / ProtectMissiles (never Charge)
            // 5) Charge only if nearest < chargeBand AND (no missiles OR last-resort morale)
            if (snapshot.ThreatCount <= 0)
                return DoctrineOrderKind.Hold;

            if (snapshot.IsBroken || snapshot.CasualtyRatio >= 0.45f)
                return DoctrineOrderKind.RetreatAndReform;

            var chargeBand = EffectiveChargeRange(snapshot);
            var preferRanged = PreferRanged;
            var hasMissiles = snapshot.CountByRole(SquadRole.Missile) > 0;
            var d = snapshot.NearestThreatDistance;

            // Rare Charge hysteresis: stay committed only while still inside a tight band
            // and still eligible for melee (no missile option / last resort).
            if (previous == DoctrineOrderKind.Charge)
            {
                if (d <= ChargeCommitBand && ShouldCharge(snapshot, hasMissiles, preferRanged, chargeBand))
                    return DoctrineOrderKind.Charge;
                // Drop back to wall / missile posture.
            }

            // Close from far into the wall band only.
            if (d > WallOuter || d > snapshot.AdvanceRange)
                return DoctrineOrderKind.Advance;

            // Inside ~14–18m (and closer): wall + missiles — never eternal Hold while threat in range.
            if (hasMissiles || preferRanged)
            {
                // With missiles: prefer ProtectMissiles (Front HoldGround) over FocusFire when
                // threat is inside the wall band; missiles PreferKeepRange / FocusFire posture.
                if (hasMissiles)
                {
                    // Last-resort Charge only (preferRanged + missiles ⇒ almost never).
                    if (ShouldCharge(snapshot, hasMissiles: true, preferRanged, chargeBand))
                        return DoctrineOrderKind.Charge;

                    // Threat in wall band → ProtectMissiles (Front holds line, missiles keep range).
                    // FocusFire when still closing or missiles not yet threatened.
                    if (d <= WallOuter
                        && (snapshot.MissileThreatened
                            || previous == DoctrineOrderKind.ProtectMissiles
                            || previous == DoctrineOrderKind.FocusFire
                            || d <= WallInner))
                        return DoctrineOrderKind.ProtectMissiles;
                    return DoctrineOrderKind.FocusFire;
                }

                // PreferRanged but no missiles left: Hold / Advance wall, Charge only last resort.
                if (d > WallInner)
                    return DoctrineOrderKind.Advance;
                if (ShouldCharge(snapshot, hasMissiles: false, preferRanged, chargeBand))
                    return DoctrineOrderKind.Charge;
                return DoctrineOrderKind.Hold;
            }

            // PreferRanged disabled: older Advance→Hold→Charge path with tight chargeBand.
            if (!hasMissiles && d > chargeBand)
            {
                if (d > NoMissileHoldLine)
                    return DoctrineOrderKind.Advance;
                return DoctrineOrderKind.Hold;
            }

            if (ShouldCharge(snapshot, hasMissiles, preferRanged, chargeBand))
                return DoctrineOrderKind.Charge;

            return DoctrineOrderKind.Hold;
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

            if (hasMissiles && preferRanged)
                return lastResort;

            // No missile option → melee only when inside the tight band.
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
