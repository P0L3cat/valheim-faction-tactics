using System;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Thin Black Forest addon: when a Troll is near Greydwarf squad(s), greys act as
    /// mobile-fortress skirmishers (orbit / peel backside / clear troll path).
    /// Lone troll → no Faction Tactics override (trolls are not a doctrine pack).
    /// Consulted by AmbushDoctrine FSM; Route 2 scorers may also read snapshot troll fields.
    /// </summary>
    public static class TrollFortressHelper
    {
        public const string TrollPrefabPrefix = "Troll";

        public static bool IsEnabled => PluginConfig.EnableTrollSynergy?.Value ?? true;

        public static float SynergyRange => PluginConfig.TrollSynergyRange?.Value ?? 28f;

        public static bool IsTrollPrefab(string? prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return false;
            return prefabName!.StartsWith(TrollPrefabPrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when snapshot reports a troll within configured synergy range.</summary>
        public static bool HasNearbyTroll(SquadSnapshot snapshot)
        {
            if (snapshot == null || !IsEnabled)
                return false;
            if (snapshot.NearbyTrollCount <= 0)
                return false;
            return snapshot.NearestTrollDistance <= SynergyRange;
        }

        /// <summary>
        /// Suggest an order override for greydwarf squads escorting a troll.
        /// Null = no synergy bias (caller continues normal Ambush FSM).
        /// </summary>
        public static DoctrineOrderKind? SuggestSynergyOrder(
            SquadSnapshot snapshot,
            DoctrineOrderKind? previous)
        {
            if (!HasNearbyTroll(snapshot))
                return null;

            // No player threat: orbit loosely as skirmishers — do not Hold in the troll's path.
            if (snapshot.ThreatCount <= 0)
                return DoctrineOrderKind.Flank;

            // Players pressing the pack: peel / draw off the troll backside (kite),
            // then re-flank so the fortress body (troll) keeps a clear lane.
            if (snapshot.NearestThreatDistance <= snapshot.ChargeRange)
            {
                if (previous == DoctrineOrderKind.Kite)
                    return DoctrineOrderKind.Flank;
                return DoctrineOrderKind.Kite;
            }

            // Mid pressure: keep orbiting; avoid frontal Charge that stacks on the troll.
            if (previous == DoctrineOrderKind.Charge)
                return DoctrineOrderKind.Kite;

            return DoctrineOrderKind.Flank;
        }
    }
}
