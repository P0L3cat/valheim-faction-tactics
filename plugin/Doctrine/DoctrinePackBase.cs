using System;
using System.Collections.Generic;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Shared MatchesPrefab / id helpers for doctrine packs (reduces per-pack boilerplate).
    /// </summary>
    public abstract class DoctrinePackBase : IDoctrinePack
    {
        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public abstract IReadOnlyList<string> PrefabPrefixes { get; }
        public abstract bool IsEnabled { get; }

        public virtual bool MatchesPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return false;
            foreach (var prefix in PrefabPrefixes)
            {
                if (prefabName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public abstract SquadRole AssignRole(SquadMemberView member, IReadOnlyList<SquadMemberView> squad);
        public abstract DoctrineOrderKind SelectOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous);

        protected static bool IsLowestId(SquadMemberView member, IReadOnlyList<SquadMemberView> squad)
        {
            long min = long.MaxValue;
            foreach (var m in squad)
            {
                if (m.InstanceId < min)
                    min = m.InstanceId;
            }
            return member.InstanceId == min;
        }

        protected static bool Contains(string? name, string token)
            => name != null
               && name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
