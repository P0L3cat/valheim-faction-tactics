using FactionTactics.Doctrine;
using UnityEngine;

namespace FactionTactics.Orders
{
    /// <summary>
    /// Per-member command produced by OrderApplicator (server) and consumed by owning-client executor.
    /// Replicated via <see cref="IntentZdoSync"/> (schema v1).
    /// </summary>
    public sealed class MemberIntent
    {
        public string SquadId { get; set; } = "";
        public DoctrineOrderKind OrderKind { get; set; }
        public FormationType Formation { get; set; }
        public StanceType Stance { get; set; }
        public SquadRole Role { get; set; }
        public Vector3 DesiredPosition { get; set; }
        public long? FocusTargetId { get; set; }
        public bool HoldGround { get; set; }
        public bool PreferRun { get; set; }
        /// <summary>
        /// 1.0.12 reinterpret: Attack (Charge / true press) → release this frame to vanilla
        /// <c>MonsterAI.UpdateAI</c> (native chase/swings). When false, FT sole-brain
        /// (formation / maneuver / Hold). Pre-1.0.12 this only meant "FT may close."
        /// </summary>
        public bool AllowVanillaChase { get; set; }
        /// <summary>Artillery jelly / kite: suppress melee chase into danger.</summary>
        public bool PreferKeepRange { get; set; }
        /// <summary>Siege Assault: melee/front/brute pressing structure breach.</summary>
        public bool AssaultWallBreaker { get; set; }
        /// <summary>Siege Assault: missile covering wall-breakers (FocusWallman).</summary>
        public bool AssaultMissileCover { get; set; }
        /// <summary>Quiet assault: leave vanilla structure targeting alone.</summary>
        public bool AllowVanillaStructure { get; set; }

        /// <summary>Meadows Death-Rush: bee-line charge; client plays vanilla alert scream.</summary>
        public bool DeathRush { get; set; }

        /// <summary>Phase A: locked formation lattice index (holes allowed). Used for swing stagger.</summary>
        public int SlotIndex { get; set; }
    }
}
