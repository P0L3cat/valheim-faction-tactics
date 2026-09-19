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
#if VALHEIM_REFS
        private static bool _loggedZeroPlayersWithPeers;
#endif

        /// <summary>Last Discover() player count (diagnostics / heartbeat).</summary>
        public int LastPlayerCount { get; private set; }

        /// <summary>Last Discover() Character.GetAllCharacters size (players + mobs).</summary>
        public int LastCharacterCount { get; private set; }

        /// <summary>Last Discover() GetCharactersInRange non-player hits.</summary>
        public int LastInRangeCount { get; private set; }

        /// <summary>Last Discover() GetAllCharacterZDOS count (player ZDOs only).</summary>
        public int LastPlayerZdoCount { get; private set; }

        /// <summary>Obsolete alias for <see cref="LastPlayerZdoCount"/>.</summary>
        public int LastZdoCount => LastPlayerZdoCount;

        /// <summary>Last Harmony MonsterAI registry size.</summary>
        public int LastRegistry { get; private set; }

        /// <summary>Last ZNetScene.m_instances count.</summary>
        public int LastSceneInstances { get; private set; }

        /// <summary>Last enemy-prefab ZDO hits.</summary>
        public int LastPrefabZdos { get; private set; }

        /// <summary>Last prefab ZDOs with live FindInstance.</summary>
        public int LastPrefabLive { get; private set; }

        /// <summary>Last prefab live views that yielded MonsterAI.</summary>
        public int LastPrefabMai { get; private set; }

        /// <summary>Last AnimalAI count seen during scan.</summary>
        public int LastAnimalAi { get; private set; }

        /// <summary>Last FindObjectsOfType/ByType count.</summary>
        public int LastFindObjects { get; private set; }

        /// <summary>Last Resources.FindObjectsOfTypeAll count.</summary>
        public int LastFindAll { get; private set; }

        /// <summary>Last Discover() MonsterAI scan count before radius/doctrine filters.</summary>
        public int LastMonsterAiCount { get; private set; }

        /// <summary>Last Harmony MonsterAI.UpdateAI postfix hits (lifetime).</summary>
        public long LastUpdateAIHits { get; private set; }

        /// <summary>Last Harmony BaseAI.UpdateAI postfix hits (lifetime).</summary>
        public long LastBaseAIUpdateHits { get; private set; }

        /// <summary>Last Discover() candidates that matched a doctrine pack.</summary>
        public int LastCandidateCount { get; private set; }

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
            // Dedicated: use Character/BaseAI maintained instance lists (FindObjectsOfType is empty there).
            // DiscoveryRadius: only consider allies near any player anchor (≠ SquadClusterRadius).
            var discoveryRadius = PluginConfig.DiscoveryRadius?.Value ?? 64f;
            var playerPositions = CollectPlayerPositions();
            LastPlayerCount = playerPositions.Count;

            var ais = ValheimWorldScan.EnumerateMonsterAIs();
            LastMonsterAiCount = ais.Count;
            LastCharacterCount = ValheimWorldScan.LastScanCharacterCount;
            LastInRangeCount = ValheimWorldScan.LastScanInRangeCount;
            LastPlayerZdoCount = ValheimWorldScan.LastScanPlayerZdoCount;
            LastRegistry = ValheimWorldScan.LastScanRegistry;
            LastSceneInstances = ValheimWorldScan.LastScanSceneInstances;
            LastPrefabZdos = ValheimWorldScan.LastScanPrefabZdos;
            LastPrefabLive = ValheimWorldScan.LastScanPrefabLive;
            LastPrefabMai = ValheimWorldScan.LastScanPrefabMai;
            LastAnimalAi = ValheimWorldScan.LastScanAnimalAi;
            LastFindObjects = ValheimWorldScan.LastScanFindObjects;
            LastFindAll = ValheimWorldScan.LastScanFindAll;
            LastUpdateAIHits = FactionTactics.HarmonyPatches.MonsterAI_UpdateAI_Patch.UpdateAIHitCount;
            LastBaseAIUpdateHits = FactionTactics.HarmonyPatches.BaseAI_UpdateAI_Patch.BaseAIUpdateHitCount;
            // Prefer scan's player tally when Character list was the source.
            if (ValheimWorldScan.LastScanPlayerCount > 0)
                LastPlayerCount = Math.Max(LastPlayerCount, ValheimWorldScan.LastScanPlayerCount);
            ValheimWorldScan.LogDedicatedDiscoveryOnce(LastPlayerCount, LastMonsterAiCount);

            var skippedDead = 0;
            var skippedRadius = 0;
            var skippedPrefab = 0;
            var prefabSamples = new List<string>();

            foreach (var ai in ais)
            {
                var ch = ValheimIds.GetCharacter(ai);
                if (ch == null || ch.IsDead())
                {
                    skippedDead++;
                    continue;
                }

                if (playerPositions.Count > 0 && !WithinAny(ch.transform.position, playerPositions, discoveryRadius))
                {
                    skippedRadius++;
                    continue;
                }

                var prefab = SanitizePrefabName(ch.name);
                var pack = _registry.ResolveByPrefab(prefab);
                if (pack == null)
                {
                    skippedPrefab++;
                    if (prefabSamples.Count < 8 && !string.IsNullOrEmpty(prefab))
                        prefabSamples.Add(prefab);
                    continue;
                }

                list.Add(new Candidate
                {
                    Doctrine = pack,
                    View = BuildView(ch, prefab, ai),
                });
            }
            LastCandidateCount = list.Count;

            // Loud diagnostics: 0.1.9 smoke had updateAIHits climbing while monsterAI/candidates stayed 0.
            if (LastUpdateAIHits > 0 && LastMonsterAiCount == 0)
            {
                Plugin.Log?.LogWarning(
                    $"SquadDiscovery 0.1.10: updateAIHits={LastUpdateAIHits} but EnumerateMonsterAIs=0 " +
                    $"(registry={LastRegistry} scene={LastSceneInstances} prefabMai={LastPrefabMai}). " +
                    "Ownership live cache / BaseAI.Instances harvest should feed discovery.");
            }
            else if (LastMonsterAiCount > 0 && LastCandidateCount == 0)
            {
                Plugin.Log?.LogWarning(
                    $"SquadDiscovery 0.1.10: monsterAI={LastMonsterAiCount} but candidates=0 " +
                    $"(dead={skippedDead} radius={skippedRadius} prefabMiss={skippedPrefab} " +
                    $"players={LastPlayerCount} radiusM={discoveryRadius} samplePrefabs=[{string.Join(",", prefabSamples)}]).");
            }
            else if (LastCandidateCount > 0)
            {
                Plugin.Log?.LogInfo(
                    $"SquadDiscovery 0.1.10: candidates={LastCandidateCount} monsterAI={LastMonsterAiCount} " +
                    $"registry={LastRegistry} players={LastPlayerCount} (Roman/doctrine match OK).");
            }
#else
            // Without game DLLs discovery is empty; director/doctrine still unit-testable with injected views.
            // DiscoveryRadius is still a config knob for VALHEIM_REFS builds (cluster radius ≠ discovery radius).
            _ = _registry;
            _ = PluginConfig.DiscoveryRadius;
            LastPlayerCount = 0;
            LastCharacterCount = 0;
            LastInRangeCount = 0;
            LastPlayerZdoCount = 0;
            LastRegistry = 0;
            LastSceneInstances = 0;
            LastPrefabZdos = 0;
            LastPrefabLive = 0;
            LastPrefabMai = 0;
            LastAnimalAi = 0;
            LastFindObjects = 0;
            LastFindAll = 0;
            LastMonsterAiCount = 0;
            LastUpdateAIHits = 0;
            LastBaseAIUpdateHits = 0;
            LastCandidateCount = 0;
#endif
            return list;
        }

#if VALHEIM_REFS
        private static List<Vector3> CollectPlayerPositions()
        {
            var positions = ValheimWorldScan.CollectPlayerPositions();
            if (positions.Count == 0)
                MaybeLogZeroPlayersWithPeers();
            return positions;
        }

        private static void MaybeLogZeroPlayersWithPeers()
        {
            if (_loggedZeroPlayersWithPeers)
                return;
            try
            {
                if (ZNet.instance == null)
                    return;
                var peers = ZNet.instance.GetPeers();
                var peerCount = peers != null ? peers.Count : 0;
                if (peerCount <= 0)
                    return;
                Plugin.Log?.LogWarning(
                    $"FactionTactics discovery: player count is 0 but ZNet has {peerCount} peer(s). " +
                    "Character/BaseAI Instances + ZNet player list returned no anchors — radius filter disabled this tick.");
                _loggedZeroPlayersWithPeers = true;
            }
            catch
            {
                // ignore peer probe failures
            }
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

        /// <summary>
        /// Strip Unity clone suffix and trailing junk after the first space
        /// so ResolveByPrefab gets Skeleton / Greydwarf from names like
        /// <c>Greydwarf(Clone)</c> or <c>Skeleton Something</c>.
        /// </summary>
        internal static string SanitizePrefabName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "";
            var s = name.Replace("(Clone)", "").Trim();
            var space = s.IndexOf(' ');
            if (space >= 0)
                s = s.Substring(0, space).Trim();
            return s;
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
