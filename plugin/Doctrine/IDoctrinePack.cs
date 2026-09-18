using System.Collections.Generic;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Data + role rules for a faction aesthetic (Route 1).
    /// </summary>
    public interface IDoctrinePack
    {
        string Id { get; }
        string DisplayName { get; }

        /// <summary>Prefab name prefixes this pack owns (e.g. "Skeleton").</summary>
        IReadOnlyList<string> PrefabPrefixes { get; }

        bool IsEnabled { get; }

        /// <summary>True if this prefab name belongs to the pack's family.</summary>
        bool MatchesPrefab(string prefabName);

        /// <summary>Assign a role for a member given current squad composition.</summary>
        SquadRole AssignRole(SquadMemberView member, IReadOnlyList<SquadMemberView> squad);

        /// <summary>
        /// Doctrine FSM: given snapshot + optional scorer hint, pick next order kind.
        /// ScriptedCommander uses this; LlmCommander would bypass and emit SquadOrder directly.
        /// </summary>
        DoctrineOrderKind SelectOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous);
    }
}
