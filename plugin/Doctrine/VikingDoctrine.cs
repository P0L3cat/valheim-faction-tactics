using System;
using System.Collections.Generic;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>Draugr* → Viking — STUB (Route 1 placeholder).</summary>
    public sealed class VikingDoctrine : IDoctrinePack
    {
        public string Id => "viking";
        public string DisplayName => "Viking";

        public IReadOnlyList<string> PrefabPrefixes { get; } = new[] { "Draugr" };

        public bool IsEnabled => PluginConfig.EnableViking?.Value ?? false;

        public bool MatchesPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return false;
            foreach (var p in PrefabPrefixes)
            {
                if (prefabName.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public SquadRole AssignRole(SquadMemberView member, IReadOnlyList<SquadMemberView> squad)
        {
            // TODO: shield-wall / berserk role map for Draugr family.
            return SquadRole.Front;
        }

        public DoctrineOrderKind SelectOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            // TODO: aggressive rush FSM.
            if (snapshot.ThreatCount <= 0)
                return DoctrineOrderKind.Hold;
            return DoctrineOrderKind.Charge;
        }
    }
}
