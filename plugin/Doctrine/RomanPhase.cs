namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Banded standoff cadence for Roman and VikingShieldWall.
    /// HoldGround is legal only in <see cref="StandoffHold"/>, <see cref="ContactHold"/>,
    /// and <see cref="RetreatPause"/> — never while closing.
    /// </summary>
    public enum RomanPhase
    {
        /// <summary>No threat, or cadence not started.</summary>
        Idle = 0,

        /// <summary>Closing to the standoff line. HoldGround false.</summary>
        ApproachStandoff = 1,

        /// <summary>Paused on the standoff line for one rolled duration. HoldGround near slot.</summary>
        StandoffHold = 2,

        /// <summary>Advancing until the front line is in swing range. HoldGround false.</summary>
        PressContact = 3,

        /// <summary>Front line is in swing range. HoldGround near slot.</summary>
        ContactHold = 4,

        /// <summary>Player opened out of swing. Brief hold, then press again.</summary>
        RetreatPause = 5,
    }
}
