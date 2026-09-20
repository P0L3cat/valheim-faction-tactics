using System.Collections.Generic;
using FactionTactics.Doctrine;

namespace FactionTactics.Squad
{
    /// <summary>
    /// Cross-tick FSM memory for a squad cluster.
    /// Discovery rebuilds <see cref="SquadUnit"/> every tick; this state is what persists.
    /// </summary>
    public sealed class SquadRuntimeState
    {
        /// <summary>Stable id assigned once (doctrine + serial), not the ephemeral discovery serial.</summary>
        public string StableId { get; set; } = "";

        public string DoctrineId { get; set; } = "";

        /// <summary>Member instance ids observed on the most recent tick (for overlap matching).</summary>
        public HashSet<long> MemberIds { get; } = new HashSet<long>();

        public DoctrineOrderKind? PreviousOrderKind { get; set; }

        /// <summary>Highest alive (or roster) count seen while this cluster was tracked.</summary>
        public int PeakAlive { get; set; }

        public float AgeSeconds { get; set; }

        /// <summary>Seconds spent on <see cref="PreviousOrderKind"/> (resets on order change).</summary>
        public float OrderAgeSeconds { get; set; }

        public int TicksUnseen { get; set; }

        /// <summary>True once PeakAlive reached MinSquadSize (for IsBroken below-min proxy).</summary>
        public bool EverMetMinSize { get; set; }

        // --- Phase A formation slot lock ---
        /// <summary>Stable instanceId → lattice index; dead entries left as holes until reshuffle.</summary>
        public System.Collections.Generic.Dictionary<long, int> LockedSlots { get; }
            = new System.Collections.Generic.Dictionary<long, int>();

        public int SlotLockCapacity { get; set; } = 1;
        public float SlotLockAgeSeconds { get; set; }
        public float SlotLockCasualtyRatio { get; set; }
        public DoctrineOrderKind? SlotLockOrderKind { get; set; }
        public FactionTactics.Orders.FormationType? SlotLockFormation { get; set; }
    }

    /// <summary>Builds stable squad keys and matches clusters across ticks by member-id overlap.</summary>
    public static class SquadIdentity
    {
        /// <summary>
        /// Deterministic key from doctrine + sorted living/roster member ids.
        /// Used as a fingerprint; runtime StableId may outlive a changing member set via overlap match.
        /// </summary>
        public static string MemberFingerprint(string doctrineId, IReadOnlyList<SquadMemberView> members)
        {
            if (members == null || members.Count == 0)
                return doctrineId + ":empty";

            var ids = new long[members.Count];
            for (int i = 0; i < members.Count; i++)
                ids[i] = members[i].InstanceId;
            System.Array.Sort(ids);

            var sb = new System.Text.StringBuilder(doctrineId.Length + 2 + ids.Length * 8);
            sb.Append(doctrineId);
            sb.Append(':');
            for (int i = 0; i < ids.Length; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(ids[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Count shared instance ids between a squad and a runtime state's last member set.
        /// </summary>
        public static int Overlap(SquadRuntimeState state, IReadOnlyList<SquadMemberView> members)
        {
            if (state == null || members == null || members.Count == 0 || state.MemberIds.Count == 0)
                return 0;
            int n = 0;
            for (int i = 0; i < members.Count; i++)
            {
                if (state.MemberIds.Contains(members[i].InstanceId))
                    n++;
            }
            return n;
        }
    }
}
