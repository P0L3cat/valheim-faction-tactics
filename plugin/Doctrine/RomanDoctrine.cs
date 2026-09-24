using System;
using System.Collections.Generic;
using System.Linq;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Skeleton* → Roman shield wall + archers.
    /// 1.0.7 cadence: Advance to ~20m standoff (HoldGround false), Hold for one roll of 1..15s,
    /// Advance until the front line is in swing range, then Hold. If the player opens out of
    /// swing, Hold 1s and press again. Missiles PreferKeepRange and never HoldGround.
    /// PreferRanged Charge gate stays (last-resort only). Hysteresis must not trap a press —
    /// the phase machine is authoritative; a timer expiry bypasses min-dwell.
    /// </summary>
    public sealed class RomanDoctrine : DoctrinePackBase
    {
        /// <summary>Legacy soft band. Cadence uses <see cref="StandoffDistance"/>.</summary>
        public const float WallOuter = 18f;

        /// <summary>Legacy soft band. Not the cadence hold line.</summary>
        public const float WallInner = 14f;

        /// <summary>Default standoff line (meters). Live value is RomanStandoffDistance.</summary>
        public const float StandoffDistance = 20f;

        /// <summary>Default front-line swing / contact band (meters).</summary>
        public const float DefaultSwingRange = 3.5f;

        public const float StandoffHoldMin = 1f;
        public const float StandoffHoldMax = 15f;
        public const float RetreatPauseSeconds = 1f;

        /// <summary>Tight band once a rare Charge is committed (meters).</summary>
        public const float ChargeCommitBand = 6.5f;

        /// <summary>Casualty ratio treated as last-resort melee commit.</summary>
        public const float LastResortCasualtyRatio = 0.28f;

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
            // 1.0.12: skeleton flanker aggression weight raises flanker quota (score/weight, not spawns).
            var targetFlankers = AggressionWeights.RomanTargetFlankers(squad.Count);
            if (!member.LooksLikeHeavy && flankerCount < targetFlankers && member.LooksLikeFlanker)
                return SquadRole.Flanker;

            return SquadRole.Front;
        }

        public override DoctrineOrderKind SelectOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            // Hard gates — not score flips. Threat lost and shattered packs leave immediately.
            if (snapshot.ThreatCount <= 0)
            {
                BandedCadence.Reset(snapshot);
                return DoctrineOrderKind.Hold;
            }
            if (snapshot.IsBroken || snapshot.CasualtyRatio >= 0.45f)
                return DoctrineOrderKind.RetreatAndReform;

            var profile = LiveProfile();
            BandedCadence.Step(snapshot, profile);
            var order = BandedCadence.OrderFor(snapshot.RomanPhase);

            // PreferRanged charge gate stays. Cadence is still authoritative: a scorer
            // cannot resurrect Charge, and hysteresis cannot keep Hold once the timer says press.
            var hasMissiles = snapshot.CountByRole(SquadRole.Missile) > 0;
            var chargeBand = EffectiveChargeRange(snapshot);
            var should = ShouldCharge(snapshot, hasMissiles, PreferRanged, chargeBand);
            if (should && previous == DoctrineOrderKind.Charge
                && BandedCadence.BandDistance(snapshot) > ChargeCommitBand)
                should = false;

            var inContact = snapshot.RomanPhase == RomanPhase.ContactHold
                            || snapshot.RomanPhase == RomanPhase.PressContact;
            if (should && inContact)
                return DoctrineOrderKind.Charge;

            return order;
        }

        /// <summary>Live standoff / swing / hold roll from <c>ft set</c>.</summary>
        public static CadenceProfile LiveProfile()
        {
            var standoff = PluginConfig.RomanStandoffDistance?.Value ?? StandoffDistance;
            var swing = PluginConfig.RomanContactSwingRange?.Value
                        ?? PluginConfig.RomanChargeRange?.Value
                        ?? DefaultSwingRange;
            var min = PluginConfig.RomanStandoffHoldMin?.Value ?? StandoffHoldMin;
            var max = PluginConfig.RomanStandoffHoldMax?.Value ?? StandoffHoldMax;
            var pause = PluginConfig.RomanRetreatPauseSeconds?.Value ?? RetreatPauseSeconds;
            return CadenceProfile.Resolve(standoff, swing, min, max, pause);
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

            // PreferRanged: while missiles live, Charge only last-resort.
            // When missiles are gone, PreferRanged must not block flanker/melee Attack commits.
            if (preferRanged && hasMissiles)
                return lastResort;

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
