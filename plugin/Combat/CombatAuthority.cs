using FactionTactics.Doctrine;
using FactionTactics.Orders;

namespace FactionTactics.Combat
{
    /// <summary>
    /// Pure UpdateAI / MoveTo authority policy (1.0.12). Offline-testable without Harmony.
    /// <para>
    /// 1.0.12 reinterprets <see cref="MemberIntent.AllowVanillaChase"/>: when true on an Attack
    /// designation (Charge / true press), UpdateAI Prefix returns true and vanilla owns the
    /// brain (native chase/swings). Formation / Ambush orbit / Flank maneuver / Hold keep
    /// AllowVanillaChase false → FT sole-brain. Same gate opens BaseAI.MoveTo so release is
    /// not re-blocked.
    /// </para>
    /// </summary>
    public static class CombatAuthority
    {
        public static bool ShouldReleaseToVanillaUpdateAI(MemberIntent? intent)
            => ShouldReleaseToVanilla(intent);

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

        /// <summary>Attack designation vocabulary: Charge is the true-press / flash order.</summary>
        public static bool IsAttackReleaseOrder(DoctrineOrderKind order)
            => order == DoctrineOrderKind.Charge;
    }
}
