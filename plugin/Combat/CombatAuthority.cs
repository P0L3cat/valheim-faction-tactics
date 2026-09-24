using FactionTactics.Orders;

namespace FactionTactics.Combat
{
    /// <summary>
    /// Pure UpdateAI / MoveTo authority policy (1.0.12). Offline-testable without Harmony.
    /// Nate lock: <see cref="MemberIntent.AllowVanillaChase"/> means release this frame to
    /// vanilla <c>MonsterAI.UpdateAI</c> (native chase/swings). Formation/maneuver stay FT
    /// sole-brain when false. Same gate opens BaseAI.MoveTo.
    /// </summary>
    public static class CombatAuthority
    {
        public static bool ShouldReleaseToVanillaUpdateAI(MemberIntent? intent)
            => ShouldReleaseToVanilla(intent);

        /// <summary>AllowVanillaChase → release vanilla UpdateAI this frame.</summary>
        public static bool ShouldReleaseToVanilla(MemberIntent? intent)
        {
            if (intent == null)
                return true;
            return intent.AllowVanillaChase;
        }

        public static bool ShouldSoleBrain(MemberIntent? intent)
            => intent != null && !ShouldReleaseToVanilla(intent);

        public static bool ShouldAllowVanillaMoveTo(MemberIntent? intent)
            => ShouldReleaseToVanilla(intent);
    }
}
