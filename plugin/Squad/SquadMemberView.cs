using FactionTactics.Doctrine;
using UnityEngine;

namespace FactionTactics.Squad
{
    /// <summary>
    /// Game-agnostic view of a squad member. Populated by discovery adapters.
    /// </summary>
    public sealed class SquadMemberView
    {
        public long InstanceId { get; set; }
        public string PrefabName { get; set; } = "";
        public Vector3 Position { get; set; }
        public bool IsAlive { get; set; } = true;
        public SquadRole AssignedRole { get; set; } = SquadRole.Unassigned;

        /// <summary>
        /// Optional HP ratio 0..1 when known (stubs/tests or VALHEIM_REFS).
        /// Negative means unknown — casualty proxy falls back to PeakAlive / missing members.
        /// </summary>
        public float HealthRatio { get; set; } = -1f;

        /// <summary>Opaque handle for OrderApplicator (Character / MonsterAI). Not serialized.</summary>
        public object? NativeHandle { get; set; }

        // Heuristic flags filled by discovery (weapon / prefab name).
        public bool LooksLikeMissile { get; set; }
        public bool LooksLikeLeader { get; set; }
        public bool LooksLikeFlanker { get; set; }
        public bool LooksLikeHeavy { get; set; }
    }
}
