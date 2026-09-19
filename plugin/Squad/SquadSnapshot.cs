using System.Collections.Generic;
using FactionTactics.Doctrine;
using UnityEngine;

namespace FactionTactics.Squad
{
    /// <summary>
    /// Compact read model for ICommander (Route 3 DTO input).
    /// Safe to JSON-serialize for a future LlmCommander.
    /// </summary>
    public sealed class SquadSnapshot
    {
        public string SquadId { get; set; } = "";
        public string DoctrineId { get; set; } = "";
        public int MemberCount { get; set; }
        public int ThreatCount { get; set; }
        public float NearestThreatDistance { get; set; } = float.MaxValue;
        public Vector3 Centroid { get; set; }
        public float CasualtyRatio { get; set; }
        public bool IsBroken { get; set; }
        public bool MissileThreatened { get; set; }
        public bool FlankOpportunity { get; set; }

        /// <summary>Ambush / Steppe berserk commit: single / cut-off target.</summary>
        public bool TargetIsolated { get; set; }

        /// <summary>Ambush / Steppe commit: staggered or low-HP threat proxy.</summary>
        public bool ThreatStaggeredOrLow { get; set; }

        /// <summary>TrollFortress: count of Troll prefabs near squad centroid.</summary>
        public int NearbyTrollCount { get; set; }

        /// <summary>TrollFortress: distance to nearest Troll (meters).</summary>
        public float NearestTrollDistance { get; set; } = float.MaxValue;

        /// <summary>
        /// VikingShieldWall B-bias: indoors / crypt / dungeon heuristic.
        /// Stub false when undetectable; director may set via VALHEIM_REFS or leave unset.
        /// </summary>
        public bool IndoorsOrCrypt { get; set; }

        /// <summary>
        /// Steppe B-bias: near village / totem / structure.
        /// Filled by director heuristics or StructureDefenseRange placeholder.
        /// </summary>
        public bool NearStructure { get; set; }

        /// <summary>InsectSiege C: Dvergr* allies/neutrals nearby.</summary>
        public int NearbyDvergrCount { get; set; }

        /// <summary>InsectSiege C: true when NearbyDvergrCount &gt; 0 within soften range.</summary>
        public bool NearDvergr { get; set; }

        /// <summary>
        /// Siege Assault: player workbench / crafting station within WorkbenchTriggerRange.
        /// Stub false without VALHEIM_REFS.
        /// </summary>
        public bool NearWorkbench { get; set; }

        /// <summary>Siege Assault: meters to nearest detected workbench.</summary>
        public float NearestWorkbenchDistance { get; set; } = float.MaxValue;

        /// <summary>
        /// Siege Assault: players inside the assault bubble.
        /// False → light-touch (allow vanilla structure targeting).
        /// True → role-split (wall-breakers breach; missiles cover).
        /// </summary>
        public bool PlayersNearAssault { get; set; }

        /// <summary>
        /// Siege Assault: squad is in Assault stance this tick
        /// (NearWorkbench + eligible doctrine + size gate + master flag).
        /// </summary>
        public bool AssaultActive { get; set; }

        /// <summary>Doctrine-tunable ranges (meters).</summary>
        public float AdvanceRange { get; set; } = 28f;
        public float ChargeRange { get; set; } = 10f;

        public Dictionary<string, int> Roles { get; set; } = new Dictionary<string, int>();

        public string? PreviousOrderKind { get; set; }

        /// <summary>
        /// Seconds spent on <see cref="PreviousOrderKind"/> (order age).
        /// Ambush uses this to require Flank age &gt; 3s before rare envelope flash.
        /// </summary>
        public float AgeSeconds { get; set; }

        public int CountByRole(SquadRole role)
        {
            var key = role.ToString();
            return Roles.TryGetValue(key, out var n) ? n : 0;
        }

        public static SquadSnapshot FromSquad(SquadUnit squad, ThreatAssessment threats)
        {
            var roles = new Dictionary<string, int>();
            Vector3 sum = Vector3.zero;
            int alive = 0;
            foreach (var m in squad.Members)
            {
                if (!m.IsAlive)
                    continue;
                alive++;
                sum += m.Position;
                var key = m.AssignedRole.ToString();
                roles[key] = roles.TryGetValue(key, out var c) ? c + 1 : 1;
            }

            var centroid = alive > 0
                ? new Vector3(sum.x / alive, sum.y / alive, sum.z / alive)
                : Vector3.zero;

            var doctrine = squad.Doctrine
                ?? throw new System.InvalidOperationException("SquadUnit.Doctrine is required for snapshot.");

            var (advance, charge) = RangesForDoctrine(doctrine.Id);

            return new SquadSnapshot
            {
                SquadId = squad.SquadId,
                DoctrineId = doctrine.Id,
                MemberCount = alive,
                ThreatCount = threats.ThreatCount,
                NearestThreatDistance = threats.NearestDistance,
                Centroid = centroid,
                CasualtyRatio = threats.CasualtyRatio,
                IsBroken = threats.IsBroken,
                MissileThreatened = threats.MissileThreatened,
                FlankOpportunity = threats.FlankOpportunity,
                TargetIsolated = threats.TargetIsolated,
                ThreatStaggeredOrLow = threats.ThreatStaggeredOrLow,
                NearbyTrollCount = threats.NearbyTrollCount,
                NearestTrollDistance = threats.NearestTrollDistance,
                IndoorsOrCrypt = threats.IndoorsOrCrypt,
                NearStructure = threats.NearStructure,
                NearbyDvergrCount = threats.NearbyDvergrCount,
                NearDvergr = threats.NearDvergr,
                NearWorkbench = threats.NearWorkbench,
                NearestWorkbenchDistance = threats.NearestWorkbenchDistance,
                PlayersNearAssault = threats.PlayersNearAssault,
                AssaultActive = threats.AssaultActive,
                AdvanceRange = advance,
                ChargeRange = charge,
                Roles = roles,
                PreviousOrderKind = squad.PreviousOrderKind?.ToString(),
                AgeSeconds = squad.OrderAgeSeconds,
            };
        }

        private static (float advance, float charge) RangesForDoctrine(string doctrineId)
        {
            switch (doctrineId)
            {
                case "roman":
                    return (28f, 3.5f);
                case "ambush":
                    return (16f, 9f);
                case "viking-shieldwall":
                    return (26f, 10f);
                case "steppe":
                    return (30f, 9f);
                case "insect-siege":
                    return (28f, 11f);
                case "charred-legion":
                    return (26f, 10f);
                case "pack-hunters":
                    return (32f, 9f);
                case "artillery-jelly":
                    return (24f, 8f);
                default:
                    return (28f, 10f);
            }
        }
    }

    public sealed class ThreatAssessment
    {
        public int ThreatCount { get; set; }
        public float NearestDistance { get; set; } = float.MaxValue;
        public float CasualtyRatio { get; set; }
        public bool IsBroken { get; set; }
        public bool MissileThreatened { get; set; }
        public bool FlankOpportunity { get; set; }
        public bool TargetIsolated { get; set; }
        public bool ThreatStaggeredOrLow { get; set; }
        public int NearbyTrollCount { get; set; }
        public float NearestTrollDistance { get; set; } = float.MaxValue;
        public bool IndoorsOrCrypt { get; set; }
        public bool NearStructure { get; set; }
        public int NearbyDvergrCount { get; set; }
        public bool NearDvergr { get; set; }

        /// <summary>Siege Assault: workbench / crafting station nearby.</summary>
        public bool NearWorkbench { get; set; }
        public float NearestWorkbenchDistance { get; set; } = float.MaxValue;
        public bool PlayersNearAssault { get; set; }
        /// <summary>Set by SquadDirector after SiegeDirector eligibility check.</summary>
        public bool AssaultActive { get; set; }
    }
}
