using System;
using System.IO;
using System.Collections.Generic;
using BepInEx.Configuration;
using FactionTactics.Commander;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Siege;
using FactionTactics.Squad;
using UnityEngine;

namespace FactionTactics.Tests
{
    internal static class TestConfig
    {
        private static bool _bound;

        public static void EnsureBound()
        {
            if (_bound)
                return;
            PluginConfig.Bind(new ConfigFile(Path.Combine(Path.GetTempPath(), "faction-tactics-tests.cfg"), false));
            _bound = true;
        }
    }

    /// <summary>Injected discovery for offline SquadDirector tests.</summary>
    internal sealed class FakeDiscovery : ISquadDiscovery
    {
        private readonly IReadOnlyList<SquadUnit> _squads;

        public FakeDiscovery(params SquadUnit[] squads)
            => _squads = squads;

        public FakeDiscovery(IReadOnlyList<SquadUnit> squads)
            => _squads = squads;

        public IReadOnlyList<SquadUnit> Discover() => _squads;
    }

    /// <summary>
    /// Mimics real SquadDiscovery: returns <b>new</b> SquadUnit instances every Discover()
    /// so director must persist PreviousOrderKind / PeakAlive via SquadRuntimeState.
    /// </summary>
    internal sealed class RebuildingDiscovery : ISquadDiscovery
    {
        private readonly IDoctrinePack _doctrine;
        private readonly Func<IReadOnlyList<MemberSpec>> _membersFactory;

        public RebuildingDiscovery(IDoctrinePack doctrine, Func<IReadOnlyList<MemberSpec>> membersFactory)
        {
            _doctrine = doctrine;
            _membersFactory = membersFactory;
        }

        public int? DebugThreatCount { get; set; }
        public float? DebugNearestThreatDistance { get; set; }

        public IReadOnlyList<SquadUnit> Discover()
        {
            var specs = _membersFactory();
            var squad = new SquadUnit
            {
                SquadId = "ephemeral",
                Doctrine = _doctrine,
                DebugThreatCount = DebugThreatCount,
                DebugNearestThreatDistance = DebugNearestThreatDistance,
            };
            var roster = new List<SquadMemberView>();
            foreach (var spec in specs)
            {
                var view = new SquadMemberView
                {
                    InstanceId = spec.InstanceId,
                    PrefabName = spec.PrefabName,
                    Position = spec.Position,
                    IsAlive = spec.IsAlive,
                    HealthRatio = spec.HealthRatio,
                    LooksLikeMissile = spec.LooksLikeMissile,
                    LooksLikeHeavy = spec.LooksLikeHeavy,
                    LooksLikeLeader = spec.LooksLikeLeader,
                    LooksLikeFlanker = spec.LooksLikeFlanker,
                };
                roster.Add(view);
            }
            foreach (var view in roster)
            {
                view.AssignedRole = _doctrine.AssignRole(view, roster);
                squad.Members.Add(view);
            }
            squad.SquadId = SquadIdentity.MemberFingerprint(_doctrine.Id, squad.Members);
            return new[] { squad };
        }
    }

    internal readonly struct MemberSpec
    {
        public long InstanceId { get; }
        public string PrefabName { get; }
        public Vector3 Position { get; }
        public bool IsAlive { get; }
        public float HealthRatio { get; }
        public bool LooksLikeMissile { get; }
        public bool LooksLikeHeavy { get; }
        public bool LooksLikeLeader { get; }
        public bool LooksLikeFlanker { get; }

        public MemberSpec(
            long instanceId,
            string prefabName,
            Vector3? position = null,
            bool isAlive = true,
            float healthRatio = -1f,
            bool looksLikeMissile = false,
            bool looksLikeHeavy = false,
            bool looksLikeLeader = false,
            bool looksLikeFlanker = true)
        {
            InstanceId = instanceId;
            PrefabName = prefabName;
            Position = position ?? new Vector3(instanceId * 2f, 0, 0);
            IsAlive = isAlive;
            HealthRatio = healthRatio;
            LooksLikeMissile = looksLikeMissile;
            LooksLikeHeavy = looksLikeHeavy;
            LooksLikeLeader = looksLikeLeader;
            LooksLikeFlanker = looksLikeFlanker;
        }
    }

    internal static class FakeSnapshots
    {
        public static readonly string[] ExpectedDoctrineIds =
        {
            "roman",
            "ambush",
            "viking-shieldwall",
            "steppe",
            "insect-siege",
            "charred-legion",
            "pack-hunters",
            "artillery-jelly",
        };

        public static SquadSnapshot Base(string doctrineId, int members = 4)
        {
            var (advance, charge) = Ranges(doctrineId);
            return new SquadSnapshot
            {
                SquadId = $"{doctrineId}-test",
                DoctrineId = doctrineId,
                MemberCount = members,
                ThreatCount = 0,
                NearestThreatDistance = float.MaxValue,
                Centroid = Vector3.zero,
                AdvanceRange = advance,
                ChargeRange = charge,
                Roles = new Dictionary<string, int>
                {
                    [nameof(SquadRole.Front)] = System.Math.Max(1, members / 2),
                    [nameof(SquadRole.Missile)] = 1,
                    [nameof(SquadRole.Flanker)] = System.Math.Max(0, members - 2),
                    [nameof(SquadRole.Leader)] = 1,
                },
            };
        }

        public static SquadSnapshot Roman(int members = 5) => WithCombatRoles(Base("roman", members), front: 2, missile: 1, flanker: 1, leader: 1);
        public static SquadSnapshot Ambush(int members = 5) => WithCombatRoles(Base("ambush", members), front: 1, missile: 1, flanker: 3, leader: 0);
        public static SquadSnapshot Viking(int members = 5) => WithCombatRoles(Base("viking-shieldwall", members), front: 2, missile: 1, flanker: 0, leader: 1);
        public static SquadSnapshot Steppe(int members = 5) => WithCombatRoles(Base("steppe", members), front: 1, missile: 1, flanker: 2, leader: 1);
        public static SquadSnapshot InsectSiege(int members = 5) => WithCombatRoles(Base("insect-siege", members), front: 1, missile: 1, flanker: 2, leader: 1);
        public static SquadSnapshot Charred(int members = 5) => WithCombatRoles(Base("charred-legion", members), front: 2, missile: 1, flanker: 1, leader: 1);
        public static SquadSnapshot PackHunters(int members = 5) => WithCombatRoles(Base("pack-hunters", members), front: 0, missile: 1, flanker: 3, leader: 1);
        public static SquadSnapshot ArtilleryJelly(int members = 4) => WithCombatRoles(Base("artillery-jelly", members), front: 0, missile: members, flanker: 0, leader: 0);

        public static SquadSnapshot WithThreat(SquadSnapshot s, float distance, int threatCount = 1)
        {
            s.ThreatCount = threatCount;
            s.NearestThreatDistance = distance;
            return s;
        }

        public static SquadSnapshot WithCombatRoles(
            SquadSnapshot s,
            int front,
            int missile,
            int flanker,
            int leader)
        {
            s.Roles = new Dictionary<string, int>
            {
                [nameof(SquadRole.Front)] = front,
                [nameof(SquadRole.Missile)] = missile,
                [nameof(SquadRole.Flanker)] = flanker,
                [nameof(SquadRole.Leader)] = leader,
            };
            s.MemberCount = front + missile + flanker + leader;
            return s;
        }

        public static ScriptedCommander CreateCommander()
        {
            TestConfig.EnsureBound();
            var registry = DoctrinePackRegistry.CreateDefault();
            return new ScriptedCommander(registry, new SiegeDirector());
        }

        public static List<DoctrineOrderKind> DriveFsm(
            ScriptedCommander commander,
            SquadSnapshot seed,
            int ticks,
            System.Func<SquadSnapshot, DoctrineOrderKind?, SquadSnapshot>? mutate = null)
        {
            var sequence = new List<DoctrineOrderKind>();
            DoctrineOrderKind? previous = null;
            var snap = seed;
            for (int i = 0; i < ticks; i++)
            {
                if (mutate != null)
                    snap = mutate(Clone(snap), previous);
                else
                    snap = Clone(snap);

                snap.PreviousOrderKind = previous?.ToString();
                var order = commander.Propose(snap);
                if (order == null)
                    break;
                sequence.Add(order.OrderKind);
                previous = order.OrderKind;
            }
            return sequence;
        }

        private static long _nextFakeInstanceId = 10_000;

        public static SquadUnit MakeSquad(IDoctrinePack doctrine, int size, string prefabFamily)
        {
            var squad = new SquadUnit
            {
                SquadId = $"{doctrine.Id}-squad",
                Doctrine = doctrine,
            };
            for (int i = 0; i < size; i++)
            {
                var view = new SquadMemberView
                {
                    // Unique across parallel xUnit collections (shared OrderApplicator.Intents).
                    InstanceId = System.Threading.Interlocked.Increment(ref _nextFakeInstanceId),
                    PrefabName = prefabFamily,
                    Position = new Vector3(i * 2f, 0, 0),
                    IsAlive = true,
                };
                view.AssignedRole = doctrine.AssignRole(view, squad.Members);
                squad.Members.Add(view);
            }
            return squad;
        }

        private static SquadSnapshot Clone(SquadSnapshot s) => new SquadSnapshot
        {
            SquadId = s.SquadId,
            DoctrineId = s.DoctrineId,
            MemberCount = s.MemberCount,
            ThreatCount = s.ThreatCount,
            NearestThreatDistance = s.NearestThreatDistance,
            Centroid = s.Centroid,
            CasualtyRatio = s.CasualtyRatio,
            IsBroken = s.IsBroken,
            MissileThreatened = s.MissileThreatened,
            FlankOpportunity = s.FlankOpportunity,
            TargetIsolated = s.TargetIsolated,
            ThreatStaggeredOrLow = s.ThreatStaggeredOrLow,
            NearbyTrollCount = s.NearbyTrollCount,
            NearestTrollDistance = s.NearestTrollDistance,
            IndoorsOrCrypt = s.IndoorsOrCrypt,
            NearStructure = s.NearStructure,
            NearbyDvergrCount = s.NearbyDvergrCount,
            NearDvergr = s.NearDvergr,
            NearWorkbench = s.NearWorkbench,
            NearestWorkbenchDistance = s.NearestWorkbenchDistance,
            PlayersNearAssault = s.PlayersNearAssault,
            AssaultActive = s.AssaultActive,
            AdvanceRange = s.AdvanceRange,
            ChargeRange = s.ChargeRange,
            Roles = new Dictionary<string, int>(s.Roles),
            PreviousOrderKind = s.PreviousOrderKind,
            AgeSeconds = s.AgeSeconds,
        };

        private static (float advance, float charge) Ranges(string doctrineId) => doctrineId switch
        {
            "roman" => (28f, 3.5f),
            "ambush" => (16f, 9f),
            "viking-shieldwall" => (26f, 10f),
            "steppe" => (30f, 9f),
            "insect-siege" => (28f, 11f),
            "charred-legion" => (26f, 10f),
            "pack-hunters" => (32f, 9f),
            "artillery-jelly" => (24f, 8f),
            _ => (28f, 10f),
        };
    }
}
