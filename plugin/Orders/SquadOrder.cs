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

        /// <summary>Who produced this order (scripted | llm | scorer-augmented).</summary>
        public string Source { get; set; } = "scripted";

        public string? Notes { get; set; }
    }
}
