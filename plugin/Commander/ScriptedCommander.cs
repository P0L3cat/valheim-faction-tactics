using System;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Squad;

namespace FactionTactics.Commander
{
    /// <summary>
    /// v0 commander: runs doctrine FSM and maps order kinds → formation/stance.
    /// </summary>
    public sealed class ScriptedCommander : ICommander
    {
        private readonly DoctrinePackRegistry _registry;

        public ScriptedCommander(DoctrinePackRegistry registry)
        {
            _registry = registry;
        }

        public SquadOrder? Propose(SquadSnapshot snapshot)
        {
            var pack = _registry.GetById(snapshot.DoctrineId);
            if (pack == null || !pack.IsEnabled)
                return null;

            DoctrineOrderKind? previous = null;
            if (!string.IsNullOrEmpty(snapshot.PreviousOrderKind)
                && Enum.TryParse(snapshot.PreviousOrderKind, out DoctrineOrderKind parsed))
            {
                previous = parsed;
            }

            var kind = pack.SelectOrder(snapshot, previous);
            var (formation, stance) = MapPresentation(kind, pack.Id);

            return new SquadOrder
            {
                SquadId = snapshot.SquadId,
                OrderKind = kind,
                Formation = formation,
                Stance = stance,
                FocusTargetId = null,
                Source = "scripted",
                Notes = $"{pack.DisplayName} FSM → {kind}",
            };
        }

        private static (FormationType formation, StanceType stance) MapPresentation(
            DoctrineOrderKind kind,
            string doctrineId)
        {
            var ambush = string.Equals(doctrineId, "ambush", StringComparison.OrdinalIgnoreCase);

            switch (kind)
            {
                case DoctrineOrderKind.Hold:
                    // Ambush lurk: loose/skirmish hide — not a Roman shield wall.
                    return ambush
                        ? (FormationType.Loose, StanceType.Defensive)
                        : (FormationType.ShieldWall, StanceType.Defensive);
                case DoctrineOrderKind.Advance:
                    return (FormationType.Line, StanceType.Defensive);
                case DoctrineOrderKind.Charge:
                    return (FormationType.Wedge, StanceType.Aggressive);
                case DoctrineOrderKind.Flank:
                    return (FormationType.Skirmish, StanceType.Aggressive);
                case DoctrineOrderKind.FocusFire:
                    return ambush
                        ? (FormationType.Skirmish, StanceType.Aggressive)
                        : (FormationType.Line, StanceType.Aggressive);
                case DoctrineOrderKind.ProtectMissiles:
                    return ambush
                        ? (FormationType.Skirmish, StanceType.Defensive)
                        : (FormationType.ShieldWall, StanceType.Defensive);
                case DoctrineOrderKind.RetreatAndReform:
                    return (FormationType.Orb, StanceType.Fleeing);
                case DoctrineOrderKind.Kite:
                    // Peel / orbit (Ambush anxiety + TrollFortress).
                    return (FormationType.Skirmish, StanceType.Fleeing);
                default:
                    return (FormationType.Loose, StanceType.Passive);
            }
        }
    }
}
