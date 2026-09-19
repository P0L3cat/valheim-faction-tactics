using System;
using System.Collections.Generic;
using System.Linq;
using FactionTactics.Commander;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Siege;
using FactionTactics.Squad;
using Xunit;

namespace FactionTactics.Tests
{
    /// <summary>
    /// Proves architecture blockers: PreviousOrderKind persists across rebuilt SquadUnits,
    /// and CasualtyRatio / IsBroken can become true from PeakAlive in offline sims.
    /// </summary>
    public class SquadDirectorPersistenceTests
    {
        private sealed class RecordingCommander : ICommander
        {
            private readonly ICommander _inner;
            public List<string?> CapturedPrevious { get; } = new List<string?>();
            public List<SquadSnapshot> CapturedSnapshots { get; } = new List<SquadSnapshot>();

            public RecordingCommander(ICommander inner) => _inner = inner;

            public SquadOrder? Propose(SquadSnapshot snapshot)
            {
                CapturedPrevious.Add(snapshot.PreviousOrderKind);
                CapturedSnapshots.Add(snapshot);
                return _inner.Propose(snapshot);
            }
        }

        private static SquadDirector CreateDirector(ISquadDiscovery discovery, ICommander commander)
        {
            TestConfig.EnsureBound();
            var registry = DoctrinePackRegistry.CreateDefault();
            return new SquadDirector(
                discovery,
                commander,
                registry,
                new NullRoleScorer(),
                new NullActionScorer(),
                new OrderApplicator(),
                new SiegeDirector());
        }

        [Fact]
        public void PreviousOrderKind_Persists_Across_Rebuilt_SquadUnits()
        {
            TestConfig.EnsureBound();
            var registry = DoctrinePackRegistry.CreateDefault();
            var ambush = registry.GetById("ambush") ?? throw new InvalidOperationException("ambush pack missing");

            // Six stable greydwarf ids; Discover rebuilds new SquadUnit each tick.
            var ids = new long[] { 101, 102, 103, 104, 105, 106 };
            var discovery = new RebuildingDiscovery(ambush, () => ids.Select(id =>
                new MemberSpec(id, "Greydwarf", looksLikeFlanker: true)).ToList())
            {
                // Force combat so FSM leaves Hold and records a meaningful previous order.
                DebugThreatCount = 1,
                DebugNearestThreatDistance = 8f,
            };

            var inner = new ScriptedCommander(registry, new SiegeDirector());
            var recorder = new RecordingCommander(inner);
            var director = CreateDirector(discovery, recorder);

            director.Tick(0.75f);
            Assert.True(recorder.CapturedPrevious.Count >= 1);
            Assert.Null(recorder.CapturedPrevious[0]); // first tick: no prior order

            var orderAfterTick1 = director.ActiveSquads.First().PreviousOrderKind;
            Assert.NotNull(orderAfterTick1);
            Assert.Single(director.RuntimeStates);
            Assert.Equal(orderAfterTick1, director.RuntimeStates[0].PreviousOrderKind);

            director.Tick(0.75f);
            Assert.True(recorder.CapturedPrevious.Count >= 2);
            // Second tick sees merged PreviousOrderKind from SquadRuntimeState despite new SquadUnit.
            Assert.Equal(orderAfterTick1.ToString(), recorder.CapturedPrevious[1]);
            // Guerrilla Ambush no longer auto-Charges from Flank; persistence is the assert.
            var orderAfterTick2 = director.ActiveSquads.First().PreviousOrderKind;
            Assert.NotNull(orderAfterTick2);
            Assert.Equal(orderAfterTick2, director.RuntimeStates[0].PreviousOrderKind);
            // Without isolate, Flank must not bum-rush Charge on the next tick.
            if (orderAfterTick1 == DoctrineOrderKind.Flank)
                Assert.NotEqual(DoctrineOrderKind.Charge, orderAfterTick2);
            // Same stable id across rebuilds (overlap match), not ephemeral discovery serial.
            Assert.Equal(director.RuntimeStates[0].StableId, director.ActiveSquads.First().SquadId);
            Assert.Equal(orderAfterTick2, director.RuntimeStates[0].PreviousOrderKind);
        }

        [Fact]
        public void IsBroken_Becomes_True_When_PeakAlive_Halves()
        {
            TestConfig.EnsureBound();
            var registry = DoctrinePackRegistry.CreateDefault();
            var roman = registry.GetById("roman") ?? throw new InvalidOperationException("roman pack missing");

            var fullIds = new long[] { 201, 202, 203, 204, 205, 206 };
            var survivors = new long[] { 201, 202, 203 }; // 50% casualties → ratio 0.5 → IsBroken
            var useFull = true;

            var discovery = new RebuildingDiscovery(roman, () =>
            {
                var set = useFull ? fullIds : survivors;
                return set.Select(id => new MemberSpec(id, "Skeleton", looksLikeHeavy: true)).ToList();
            })
            {
                DebugThreatCount = 1,
                DebugNearestThreatDistance = 12f,
            };

            var commander = new ScriptedCommander(registry, new SiegeDirector());
            var director = CreateDirector(discovery, commander);

            director.Tick(0.75f);
            var squad1 = Assert.Single(director.ActiveSquads);
            Assert.Equal(6, squad1.PeakAlive);
            Assert.Equal(0f, squad1.LastCasualtyRatio);
            Assert.False(squad1.LastIsBroken);

            useFull = false;
            director.Tick(0.75f);
            var squad2 = Assert.Single(director.ActiveSquads);
            Assert.Equal(6, squad2.PeakAlive);
            Assert.Equal(0.5f, squad2.LastCasualtyRatio, 3);
            Assert.True(squad2.LastIsBroken);
            // Anxiety path should be able to fire (Roman retreats at 0.45 / IsBroken).
            Assert.Equal(DoctrineOrderKind.RetreatAndReform, squad2.CurrentOrder?.OrderKind);
        }

        [Fact]
        public void ComputeCasualties_Uses_PeakAlive_And_BelowMin_Proxy()
        {
            var state = new SquadRuntimeState
            {
                PeakAlive = 5,
                EverMetMinSize = true,
            };
            var (ratio, broken) = SquadDirector.ComputeCasualties(alive: 2, state, minSize: 3);
            Assert.Equal(0.6f, ratio, 3);
            Assert.True(broken); // below min after having met it, and ratio >= 0.5

            var (ratio2, broken2) = SquadDirector.ComputeCasualties(alive: 4, state, minSize: 3);
            Assert.Equal(0.2f, ratio2, 3);
            Assert.False(broken2);
        }

        [Fact]
        public void AgeSeconds_Accumulates_Across_Ticks()
        {
            TestConfig.EnsureBound();
            var registry = DoctrinePackRegistry.CreateDefault();
            var pack = registry.GetById("pack-hunters") ?? throw new InvalidOperationException("missing pack");
            var ids = new long[] { 301, 302, 303, 304 };
            var discovery = new RebuildingDiscovery(pack, () =>
                ids.Select(id => new MemberSpec(id, "Wolf", looksLikeFlanker: true)).ToList());

            var director = CreateDirector(discovery, new ScriptedCommander(registry, new SiegeDirector()));
            director.Tick(0.5f);
            director.Tick(0.5f);
            var squad = Assert.Single(director.ActiveSquads);
            Assert.Equal(1.0f, squad.AgeSeconds, 3);
            Assert.Equal(1.0f, director.RuntimeStates[0].AgeSeconds, 3);
        }
    }
}
