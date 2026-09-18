using System;
using System.Collections.Generic;
using FactionTactics.Doctrine;

namespace FactionTactics.Orders
{
    /// <summary>
    /// Route 3 DTO — JSON-serializable squad intent.
    /// ScriptedCommander and future LlmCommander both emit this shape.
    /// </summary>
    [Serializable]
    public sealed class SquadOrder
    {
        public string SquadId { get; set; } = "";
        public DoctrineOrderKind OrderKind { get; set; } = DoctrineOrderKind.Hold;
        public FormationType Formation { get; set; } = FormationType.Line;
        public StanceType Stance { get; set; } = StanceType.Defensive;

        /// <summary>Optional focus target (ZDO id / instance id). Null = doctrine default.</summary>
        public long? FocusTargetId { get; set; }

        /// <summary>Optional per-member role overrides (instanceId → role name).</summary>
        public Dictionary<string, string>? RoleOverrides { get; set; }

        /// <summary>Who produced this order (scripted | llm | scorer-augmented | scripted+siege).</summary>
        public string Source { get; set; } = "scripted";

        public string? Notes { get; set; }

        /// <summary>Siege Assault v1: order produced under AssaultStance.</summary>
        public bool AssaultActive { get; set; }

        /// <summary>Siege Assault: players in the assault bubble (role-split vs quiet).</summary>
        public bool AssaultPlayersPresent { get; set; }

        /// <summary>
        /// Quiet assault: do not fight vanilla MonsterAI structure targeting
        /// (wall-breakers may chew pieces via vanilla when no players present).
        /// </summary>
        public bool AllowVanillaStructure { get; set; }
    }
}
