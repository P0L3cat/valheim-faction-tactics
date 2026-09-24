using System;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// 1.0.12 aggression score/weight doctrine (Nate Theater playtest lock).
    /// Score and weight only — never spawn rates / raid APIs.
    /// Bumps: skeleton flankers (Roman), greydwarfs overall (Ambush), draugr overall (Viking).
    /// </summary>
    public static class AggressionWeights
    {
        public const float Baseline = 1.0f;

        /// <summary>Roman Skeleton Flanker role pressure bump.</summary>
        public const float RomanSkeletonFlanker = 1.35f;

        /// <summary>Ambush Greydwarf overall aggression bump (Flank/Charge utilities).</summary>
        public const float AmbushGreydwarf = 1.25f;

        /// <summary>VikingShieldWall Draugr overall aggression bump (press cadence).</summary>
        public const float VikingDraugr = 1.25f;

        public static float ForRomanFlanker() => RomanSkeletonFlanker;

        public static float ForAmbushGreydwarf() => AmbushGreydwarf;

        public static float ForVikingDraugr() => VikingDraugr;

        /// <summary>
        /// Doctrine-scoped weight. Roman Flanker → skeleton flanker bump;
        /// Ambush any role → greydwarf overall; Viking any role → draugr overall.
        /// </summary>
        public static float For(string doctrineId, SquadRole? role = null)
        {
            if (string.IsNullOrEmpty(doctrineId))
                return Baseline;

            if (string.Equals(doctrineId, "roman", StringComparison.OrdinalIgnoreCase)
                && role == SquadRole.Flanker)
                return RomanSkeletonFlanker;

            if (string.Equals(doctrineId, "ambush", StringComparison.OrdinalIgnoreCase))
                return AmbushGreydwarf;

            if (string.Equals(doctrineId, "viking-shieldwall", StringComparison.OrdinalIgnoreCase)
                || string.Equals(doctrineId, "viking", StringComparison.OrdinalIgnoreCase))
                return VikingDraugr;

            return Baseline;
        }

        /// <summary>Roman target flanker quota after 1.0.12 skeleton-flanker bump.</summary>
        public static int RomanTargetFlankers(int squadCount)
        {
            if (squadCount <= 0)
                return 0;
            var raw = Math.Max(1, squadCount / 4f) * RomanSkeletonFlanker;
            return Math.Max(1, (int)Math.Ceiling(raw));
        }

        /// <summary>
        /// Post-1.0.12 hungrier: ceil(10% of N) members must press or ranged-harass
        /// while engaged / Theater. Delegates to <see cref="AlwaysThreat.Required"/>.
        /// </summary>
        public static int RequiredThreatElements(int squadCount)
            => AlwaysThreat.Required(squadCount);
    }
}
