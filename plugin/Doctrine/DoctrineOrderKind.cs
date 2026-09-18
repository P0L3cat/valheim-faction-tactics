namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Shared squad-order vocabulary (Roman spike + Black Forest Ambush / Kite).
    /// Prefer these over doctrine-specific Ambush/Disperse/ReAmbush kinds.
    ///
    /// Siege Assault v1 maps siege intents onto this same enum (no new kinds):
    ///   Encircle      → Flank
    ///   TestBreach    → Charge (or Advance while closing)
    ///   FocusWallman  → FocusFire / ProtectMissiles
    ///   Withdraw      → RetreatAndReform / Kite
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
        /// <summary>Skirmish peel / orbit — Ambush anxiety + TrollFortress synergy + Assault Withdraw.</summary>
        Kite,
    }
}
