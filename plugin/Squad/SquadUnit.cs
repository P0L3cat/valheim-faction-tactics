using System.Collections.Generic;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using UnityEngine;

namespace FactionTactics.Squad
{
    /// <summary>Runtime squad cluster under one doctrine pack.</summary>
    public sealed class SquadUnit
    {
        public string SquadId { get; set; } = "";
        public IDoctrinePack Doctrine { get; set; } = null!;
        public List<SquadMemberView> Members { get; } = new List<SquadMemberView>();
        public SquadOrder? CurrentOrder { get; set; }
        public DoctrineOrderKind? PreviousOrderKind { get; set; }
        public float AgeSeconds { get; set; }

        /// <summary>Seconds on current PreviousOrderKind (for Flank-age flash gate).</summary>
        public float OrderAgeSeconds { get; set; }

        /// <summary>Highest alive/roster count tracked for this cluster (diagnostics / tests).</summary>
        public int PeakAlive { get; set; }

        /// <summary>Last-tick casualty proxy from PeakAlive (diagnostics / tests).</summary>
        public float LastCasualtyRatio { get; set; }

        /// <summary>Last-tick broken-line proxy (diagnostics / tests).</summary>
        public bool LastIsBroken { get; set; }

        /// <summary>
        /// Offline/sim hook: when set, AssessThreats uses these instead of world scans
        /// so anxiety/retreat branches can fire without VALHEIM_REFS.
        /// </summary>
        public int? DebugThreatCount { get; set; }

        /// <summary>Offline/sim hook for nearest threat distance (meters).</summary>
        public float? DebugNearestThreatDistance { get; set; }

        /// <summary>
        /// Offline/sim hook: when set, OrderApplicator Charge/FocusFire / facing use this
        /// world position instead of VALHEIM_REFS combat-target / player scans.
        /// </summary>
        public Vector3? DebugThreatPosition { get; set; }

        /// <summary>
        /// Offline/sim hook: player id Theater Commander groups on.
        /// Unset means "no scripted focus" (live play scans the world).
        /// </summary>
        public long? DebugFocusPlayerId { get; set; }

        /// <summary>World position of <see cref="DebugFocusPlayerId"/>. Defaults to the squad centroid when null.</summary>
        public Vector3? DebugFocusPlayerPosition { get; set; }
    }
}
