namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Shared squad-order vocabulary (Roman spike + Black Forest Ambush / Kite).
    /// Prefer these over doctrine-specific Ambush/Disperse/ReAmbush kinds.
    /// </summary>
    public enum DoctrineOrderKind
    {
        Hold,
        Advance,
        Charge,
        Flank,
        FocusFire,
        ProtectMissiles,
        RetreatAndReform,
        /// <summary>Skirmish peel / orbit — Ambush anxiety + TrollFortress synergy.</summary>
        Kite,
    }
}
