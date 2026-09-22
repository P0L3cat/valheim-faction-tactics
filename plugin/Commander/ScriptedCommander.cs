using System;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Siege;
using FactionTactics.Squad;

namespace FactionTactics.Commander
{
    /// <summary>
    /// v0 commander: runs doctrine FSM and maps order kinds → formation/stance.
    /// When Siege Assault is active, AssaultStance overrides the doctrine SelectOrder.
    /// </summary>
    public sealed class ScriptedCommander : ICommander
    {
        private readonly DoctrinePackRegistry _registry;
        private readonly SiegeDirector _siege;

        public ScriptedCommander(DoctrinePackRegistry registry, SiegeDirector? siege = null)
        {
            _registry = registry;
            _siege = siege ?? new SiegeDirector();
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

            // Siege Assault v1: workbench-triggered AssaultStance for Ambush + Viking.
            var assaultKind = _siege.TrySelectAssaultOrder(snapshot, previous);
            DoctrineOrderKind kind;
            string notes;
            if (assaultKind.HasValue)
            {
                kind = assaultKind.Value;
                notes = snapshot.PlayersNearAssault
                    ? $"{pack.DisplayName} Assault/hot → {kind} (role-split)"
                    : $"{pack.DisplayName} Assault/quiet → {kind} (vanilla structure OK)";
            }
            else
            {
                kind = pack.SelectOrder(snapshot, previous);
                notes = $"{pack.DisplayName} FSM → {kind}";
            }

            var (formation, stance) = MapPresentation(kind, pack.Id, assaultKind.HasValue);

            return new SquadOrder
            {
                SquadId = snapshot.SquadId,
                OrderKind = kind,
                Formation = formation,
                Stance = stance,
                FocusTargetId = null,
                Source = assaultKind.HasValue ? "scripted+siege" : "scripted",
                Notes = notes,
                AssaultActive = assaultKind.HasValue,
                AssaultPlayersPresent = snapshot.PlayersNearAssault,
                AllowVanillaStructure = assaultKind.HasValue && !snapshot.PlayersNearAssault,
            };
        }

        internal static (FormationType formation, StanceType stance) PresentationFor(
            DoctrineOrderKind kind,
            string doctrineId,
            bool assault)
            => MapPresentation(kind, doctrineId, assault);

        private static (FormationType formation, StanceType stance) MapPresentation(
            DoctrineOrderKind kind,
            string doctrineId,
            bool assault)
        {
            var ambush = Eq(doctrineId, "ambush");
            var steppe = Eq(doctrineId, "steppe");
            var pack = Eq(doctrineId, "pack-hunters");
            var jelly = Eq(doctrineId, "artillery-jelly");
            var insect = Eq(doctrineId, "insect-siege");
            var viking = Eq(doctrineId, "viking-shieldwall");
            var roman = Eq(doctrineId, "roman");
            var skirmishFaction = ambush || steppe || pack || jelly || insect;

            switch (kind)
            {
                case DoctrineOrderKind.Hold:
                    if (jelly)
                        return (FormationType.Loose, StanceType.Defensive);
                    if (steppe)
                        return (FormationType.Orb, StanceType.Defensive);
                    if (pack)
                        return (FormationType.Loose, StanceType.Defensive);
                    // Ambush: Orb primacy even on lurk Hold (ready to Flank/Kite); insect stays Loose.
                    if (ambush)
                        return (FormationType.Orb, StanceType.Defensive);
                    if (insect)
                        return (FormationType.Loose, StanceType.Defensive);
                    // Roman / VikingShieldWall / CharredLegion: shield / dense ranks.
                    return (FormationType.ShieldWall, StanceType.Defensive);
                case DoctrineOrderKind.Advance:
                    if (assault && viking)
                        return (FormationType.ShieldWall, StanceType.Aggressive); // TestBreach approach
                    if (assault && ambush)
                        return (FormationType.Skirmish, StanceType.Aggressive);
                    if (roman || viking)
                        return (FormationType.ShieldWall, StanceType.Defensive); // close while holding wall
                    if (jelly)
                        return (FormationType.Loose, StanceType.Defensive);
                    if (skirmishFaction)
                        return (FormationType.Skirmish, StanceType.Defensive);
                    return (FormationType.Line, StanceType.Defensive);
                case DoctrineOrderKind.Charge:
                    // Ambush: brief Skirmish flash (not Wedge blob). Others: Wedge commit.
                    if (ambush)
                        return (FormationType.Skirmish, StanceType.Aggressive);
                    return (FormationType.Wedge, StanceType.Aggressive); // TestBreach commit
                case DoctrineOrderKind.Flank:
                    // Ambush guerrilla: Orb encircle; others Skirmish.
                    if (ambush)
                        return (FormationType.Orb, StanceType.Aggressive);
                    return (FormationType.Skirmish, StanceType.Aggressive); // Encircle
                case DoctrineOrderKind.FocusFire:
                    if (assault)
                        return (FormationType.Skirmish, StanceType.Aggressive); // FocusWallman
                    if (ambush)
                        return (FormationType.Orb, StanceType.Aggressive); // shaman rear / encircle
                    if (roman || viking)
                        return (FormationType.ShieldWall, StanceType.Aggressive); // wall + archers
                    if (jelly)
                        return (FormationType.Loose, StanceType.Aggressive);
                    if (skirmishFaction)
                        return (FormationType.Skirmish, StanceType.Aggressive);
                    return (FormationType.Line, StanceType.Aggressive);
                case DoctrineOrderKind.ProtectMissiles:
                    if (assault)
                        return (FormationType.Skirmish, StanceType.Defensive); // FocusWallman cover
                    if (ambush)
                        return (FormationType.Orb, StanceType.Defensive);
                    if (skirmishFaction)
                        return (FormationType.Skirmish, StanceType.Defensive);
                    return (FormationType.ShieldWall, StanceType.Defensive);
                case DoctrineOrderKind.RetreatAndReform:
                    return (FormationType.Orb, StanceType.Fleeing); // Withdraw
                case DoctrineOrderKind.Kite:
                    if (ambush)
                        return (FormationType.Orb, StanceType.Fleeing); // peel then re-encircle
                    return (FormationType.Skirmish, StanceType.Fleeing); // Withdraw / peel
                default:
                    return (FormationType.Loose, StanceType.Passive);
            }
        }

        private static bool Eq(string a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
