using System;
using System.Collections.Generic;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Meadows Greyling → Death-Rush (Nate: death-rush fanatics).
    /// Bee-line Charge, fight to the death — never Hold / Kite / Retreat while a threat exists.
    /// Prefab: Greyling (Meadows). Explicitly not Greydwarf* (those are Ambush).
    /// Player-facing names stay "Death-Rush" — no religious wording.
    /// </summary>
    public sealed class DeathRushDoctrine : DoctrinePackBase
    {
        public const string DoctrineId = "death-rush";

        public override string Id => DoctrineId;
        public override string DisplayName => "DeathRush";

        public override IReadOnlyList<string> PrefabPrefixes { get; } = new[]
        {
            "Greyling",
        };

        public override bool IsEnabled => PluginConfig.EnableDeathRush?.Value ?? true;

        public override bool MatchesPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return false;
            // Never steal Greydwarf* from Ambush.
            if (Contains(prefabName, "Greydwarf"))
                return false;
            return prefabName.StartsWith("Greyling", StringComparison.OrdinalIgnoreCase);
        }

        public override SquadRole AssignRole(SquadMemberView member, IReadOnlyList<SquadMemberView> squad)
        {
            // Fanatic swarm: everyone is a Front charger; one Leader for formation centroid.
            member.LooksLikeFlanker = false;
            member.LooksLikeMissile = false;
            var hasLeader = false;
            foreach (var m in squad)
            {
                if (m.AssignedRole == SquadRole.Leader && m.InstanceId != member.InstanceId)
                {
                    hasLeader = true;
                    break;
                }
            }
            if (!hasLeader && (member.LooksLikeLeader || IsLowestId(member, squad)))
                return SquadRole.Leader;
            return SquadRole.Front;
        }

        public override DoctrineOrderKind SelectOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            // Idle lurk only with no threat. Any threat → Charge forever (including "broken").
            if (snapshot.ThreatCount <= 0)
                return DoctrineOrderKind.Hold;

            // Nate: death-rush fanatics — ignore broken / casualty retreats.
            _ = previous;
            _ = snapshot.IsBroken;
            _ = snapshot.CasualtyRatio;
            return DoctrineOrderKind.Charge;
        }
    }
}
