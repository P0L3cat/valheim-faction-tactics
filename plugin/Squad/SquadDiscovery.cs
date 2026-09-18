using System;
using System.Collections.Generic;
using System.Linq;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Util;
using UnityEngine;

namespace FactionTactics.Squad
{
    /// <summary>
    /// Discovers faction allies (within DiscoveryRadius of players) and clusters by SquadClusterRadius.
    /// SquadId is a member-set fingerprint; SquadDirector replaces it with a persistent StableId.
    /// </summary>
    public sealed class SquadDiscovery : ISquadDiscovery
    {
        private readonly DoctrinePackRegistry _registry;
        private int _nextSquadSerial;

        public SquadDiscovery(DoctrinePackRegistry registry)
        {
            _registry = registry;
        }

        public IReadOnlyList<SquadUnit> Discover()
        {
            var candidates = CollectCandidates();
            if (candidates.Count == 0)
                return Array.Empty<SquadUnit>();

            var byDoctrine = candidates.GroupBy(c => c.Doctrine.Id);
            var squads = new List<SquadUnit>();

            foreach (var group in byDoctrine)
            {
                var members = group.ToList();
                var clusters = ClusterByProximity(members, PluginConfig.SquadClusterRadius?.Value ?? 18f);
                foreach (var cluster in clusters)
                {
                    if (cluster.Count == 0)
                        continue;

                    var doctrine = cluster[0].Doctrine;
                    var views = cluster.Select(c => c.View).ToList();
                    // Fingerprint from member set; SquadDirector replaces with persistent StableId.
                    var squad = new SquadUnit
                    {
                        SquadId = SquadIdentity.MemberFingerprint(doctrine.Id, views),
                        Doctrine = doctrine,
                    };
                    _ = ++_nextSquadSerial; // retained for diagnostics / future telemetry
                    squad.Members.AddRange(views);
                    AssignRoles(squad);
                    squads.Add(squad);
                }
            }

            return squads;
        }

        private void AssignRoles(SquadUnit squad)
        {
            foreach (var m in squad.Members)
                m.AssignedRole = SquadRole.Unassigned;

            foreach (var m in squad.Members.OrderBy(x => x.InstanceId))
                m.AssignedRole = squad.Doctrine.AssignRole(m, squad.Members);
        }

        private List<Candidate> CollectCandidates()
        {
            var list = new List<Candidate>();

#if VALHEIM_REFS
            // TODO(hypothesis): FindObjectsOfType<MonsterAI> is acceptable on dedicated tick (0.5–1s).
            // Prefer a maintained registry if profiling shows spikes.
            // DiscoveryRadius: only consider allies near any local player (≠ SquadClusterRadius).
            var discoveryRadius = PluginConfig.DiscoveryRadius?.Value ?? 40f;
            var playerPositions = CollectPlayerPositions();

            var ais = UnityEngine.Object.FindObjectsOfType<MonsterAI>();
            foreach (var ai in ais)
            {
                var ch = ai.m_character;
                if (ch == null || ch.IsDead())
                    continue;

                if (playerPositions.Count > 0 && !WithinAny(ch.transform.position, playerPositions, discoveryRadius))
                    continue;

                var prefab = SanitizePrefabName(ch.name);
                var pack = _registry.ResolveByPrefab(prefab);
                if (pack == null)
                    continue;

                list.Add(new Candidate
                {
                    Doctrine = pack,
                    View = BuildView(ch, prefab, ai),
                });
            }
#else
            // Without game DLLs discovery is empty; director/doctrine still unit-testable with injected views.
            // DiscoveryRadius is still a config knob for VALHEIM_REFS builds (cluster radius ≠ discovery radius).
            _ = _registry;
            _ = PluginConfig.DiscoveryRadius;
#endif
            return list;
        }

#if VALHEIM_REFS
        private static List<Vector3> CollectPlayerPositions()
        {
            var positions = new List<Vector3>();
            try
            {
                // TODO(hypothesis): Player.GetAllPlayers() when available; FindObjectsOfType as fallback.
                var players = UnityEngine.Object.FindObjectsOfType<Player>();
                foreach (var p in players)
                {
                    if (p == null)
                        continue;
                    positions.Add(p.transform.position);
                }
            }
            catch
            {
                // no players → do not radius-filter (dedicated empty / early load)
            }
            return positions;
        }

        private static bool WithinAny(Vector3 pos, List<Vector3> anchors, float radius)
        {
            for (int i = 0; i < anchors.Count; i++)
            {
                if (Vector3.Distance(pos, anchors[i]) <= radius)
                    return true;
            }
            return false;
        }
#endif

#if VALHEIM_REFS
        private static SquadMemberView BuildView(Character ch, string prefab, MonsterAI ai)
        {
            var view = new SquadMemberView
            {
                // Stable ZDO packing — see ValheimIds.ToLong (UserID<<32 | ID).
                InstanceId = ValheimIds.FromCharacter(ch),
                PrefabName = prefab,
                Position = ch.transform.position,
                IsAlive = !ch.IsDead(),
                NativeHandle = ai,
            };

            ApplyPrefabHeuristics(view);
            return view;
        }

        private static void ApplyPrefabHeuristics(SquadMemberView view)
        {
            var name = view.PrefabName;

            // Greydwarf family (Black Forest Ambush).
            if (Starts(name, "Greydwarf"))
            {
                view.LooksLikeMissile = Contains(name, "Shaman");
                view.LooksLikeHeavy = Contains(name, "Elite") || Contains(name, "Brute");
                view.LooksLikeLeader = view.LooksLikeHeavy;
                view.LooksLikeFlanker = !view.LooksLikeHeavy && !view.LooksLikeMissile;
                return;
            }

            // Draugr / VikingShieldWall.
            if (Starts(name, "Draugr"))
            {
                view.LooksLikeMissile = Contains(name, "Archer") || Contains(name, "Bow") || Contains(name, "Ranged");
                view.LooksLikeHeavy = !view.LooksLikeMissile;
                view.LooksLikeLeader = Contains(name, "Elite") || Contains(name, "Captain") || Contains(name, "Lord");
                view.LooksLikeFlanker = false;
                return;
            }

            // Fuling / Goblin → Steppe.
            if (Starts(name, "Fuling") || Starts(name, "Goblin"))
            {
                view.LooksLikeMissile = Contains(name, "Shaman") || Contains(name, "Archer") || Contains(name, "Bow");
                view.LooksLikeHeavy = Contains(name, "Berserker") || Contains(name, "Brute") || Contains(name, "Elite");
                view.LooksLikeLeader = view.LooksLikeHeavy;
                view.LooksLikeFlanker = !view.LooksLikeHeavy && !view.LooksLikeMissile;
                return;
            }

            // Mistlands insects.
            if (Starts(name, "Gjall"))
            {
                view.LooksLikeMissile = true;
                return;
            }
            if (Starts(name, "Seeker") || Starts(name, "Tick"))
            {
                view.LooksLikeHeavy = Contains(name, "Soldier");
                view.LooksLikeLeader = view.LooksLikeHeavy;
                view.LooksLikeFlanker = !view.LooksLikeHeavy;
                view.LooksLikeMissile = false;
                return;
            }

            // Ashlands Charred + Asksvin.
            if (Starts(name, "Asksvin"))
            {
                view.LooksLikeFlanker = true;
                return;
            }
            if (Starts(name, "Charred"))
            {
                view.LooksLikeMissile = Contains(name, "Archer") || Contains(name, "Mage")
                    || Contains(name, "Warlock") || Contains(name, "Caster");
                view.LooksLikeHeavy = !view.LooksLikeMissile;
                view.LooksLikeLeader = Contains(name, "Elite") || Contains(name, "Melee");
                view.LooksLikeFlanker = false;
                return;
            }

            // Mountain pack.
            if (Starts(name, "Drake") || Starts(name, "Hatchling"))
            {
                view.LooksLikeMissile = true;
                return;
            }
            if (Starts(name, "Wolf"))
            {
                view.LooksLikeFlanker = true;
                view.LooksLikeLeader = Contains(name, "Elite") || Contains(name, "Alpha");
                return;
            }

            // Blob artillery.
            if (Starts(name, "Blob"))
            {
                view.LooksLikeMissile = true;
                view.LooksLikeHeavy = Contains(name, "Elite") || Contains(name, "Oozer");
                view.LooksLikeLeader = view.LooksLikeHeavy;
                return;
            }

            // Skeleton / generic fallback.
            view.LooksLikeMissile = Contains(name, "bow") || Contains(name, "archer") || Contains(name, "ranged");
            view.LooksLikeHeavy = Contains(name, "heavy") || Contains(name, "shield") || Contains(name, "tank");
            view.LooksLikeFlanker = !view.LooksLikeHeavy && !view.LooksLikeMissile;
            view.LooksLikeLeader = Contains(name, "chief") || Contains(name, "elite") || Contains(name, "captain");
        }
#endif

        private static string SanitizePrefabName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "";
            return name.Replace("(Clone)", "").Trim();
        }

        private static bool Starts(string name, string prefix)
            => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        private static bool Contains(string name, string token)
            => name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        private static List<List<Candidate>> ClusterByProximity(List<Candidate> members, float radius)
        {
            var remaining = new List<Candidate>(members);
            var clusters = new List<List<Candidate>>();

            while (remaining.Count > 0)
            {
                var seed = remaining[0];
                remaining.RemoveAt(0);
                var cluster = new List<Candidate> { seed };
                bool grew;
                do
                {
                    grew = false;
                    for (int i = remaining.Count - 1; i >= 0; i--)
                    {
                        var c = remaining[i];
                        if (cluster.Any(x => Vector3.Distance(x.View.Position, c.View.Position) <= radius))
                        {
                            cluster.Add(c);
                            remaining.RemoveAt(i);
                            grew = true;
                        }
                    }
                } while (grew);

                clusters.Add(cluster);
            }

            return clusters;
        }

        private sealed class Candidate
        {
            public IDoctrinePack Doctrine { get; set; } = null!;
            public SquadMemberView View { get; set; } = null!;
        }
    }
}
