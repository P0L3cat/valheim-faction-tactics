using System;
using System.Collections.Generic;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>Fuling* → Mongol/steppe — STUB (Route 1 placeholder). Greydwarf* is AmbushDoctrine.</summary>
    public sealed class MongolDoctrine : IDoctrinePack
    {
        public string Id => "mongol";
        public string DisplayName => "Mongol";

        public IReadOnlyList<string> PrefabPrefixes { get; } = new[] { "Goblin", "Fuling" };

        public bool IsEnabled => PluginConfig.EnableMongol?.Value ?? false;

        public bool MatchesPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return false;
            // Valheim Fulings often use Goblin* prefab names.
            foreach (var p in PrefabPrefixes)
            {
                if (prefabName.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public SquadRole AssignRole(SquadMemberView member, IReadOnlyList<SquadMemberView> squad)
        {
            // TODO: horse-archer / skirmish roles for Fuling family.
            if (member.LooksLikeMissile)
                return SquadRole.Missile;
            return SquadRole.Flanker;
        }

        public DoctrineOrderKind SelectOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            // TODO: kite / encircle FSM.
            if (snapshot.ThreatCount <= 0)
                return DoctrineOrderKind.Hold;
            if (snapshot.NearestThreatDistance < 8f)
                return DoctrineOrderKind.RetreatAndReform;
            return DoctrineOrderKind.Flank;
        }
    }
}
