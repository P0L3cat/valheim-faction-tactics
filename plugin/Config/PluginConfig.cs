using BepInEx.Configuration;

namespace FactionTactics.Config
{
    public static class PluginConfig
    {
        public static ConfigEntry<bool> EnablePlugin { get; private set; } = null!;
        public static ConfigEntry<float> TickIntervalSeconds { get; private set; } = null!;
        public static ConfigEntry<int> MinSquadSize { get; private set; } = null!;
        public static ConfigEntry<float> DiscoveryRadius { get; private set; } = null!;
        public static ConfigEntry<float> SquadClusterRadius { get; private set; } = null!;
        public static ConfigEntry<bool> EnableRoman { get; private set; } = null!;
        public static ConfigEntry<bool> EnableViking { get; private set; } = null!;
        public static ConfigEntry<bool> EnableMongol { get; private set; } = null!;
        public static ConfigEntry<bool> EnableAmbush { get; private set; } = null!;
        public static ConfigEntry<bool> EnableTrollSynergy { get; private set; } = null!;
        public static ConfigEntry<float> TrollSynergyRange { get; private set; } = null!;
        public static ConfigEntry<bool> DebugLogging { get; private set; } = null!;

        public static void Bind(ConfigFile config)
        {
            EnablePlugin = config.Bind(
                "General",
                "EnablePlugin",
                true,
                "Master switch for Faction Tactics.");

            TickIntervalSeconds = config.Bind(
                "General",
                "TickIntervalSeconds",
                0.75f,
                "SquadDirector tick interval in seconds (Route 1 FSM).");

            MinSquadSize = config.Bind(
                "Squad",
                "MinSquadSize",
                3,
                "Below this size, members stay on vanilla MonsterAI.");

            DiscoveryRadius = config.Bind(
                "Squad",
                "DiscoveryRadius",
                40f,
                "World radius used when scanning for faction allies.");

            SquadClusterRadius = config.Bind(
                "Squad",
                "SquadClusterRadius",
                18f,
                "Max distance between members to belong to the same squad cluster.");

            EnableRoman = config.Bind(
                "Doctrine",
                "EnableRoman",
                true,
                "Skeleton* → Roman doctrine (spike — implemented).");

            EnableViking = config.Bind(
                "Doctrine",
                "EnableViking",
                false,
                "Draugr* → Viking doctrine (stub).");

            EnableMongol = config.Bind(
                "Doctrine",
                "EnableMongol",
                false,
                "Fuling* → Mongol/steppe doctrine (stub).");

            EnableAmbush = config.Bind(
                "Doctrine",
                "EnableAmbush",
                true,
                "Greydwarf* → Black Forest Ambush predators (implemented).");

            EnableTrollSynergy = config.Bind(
                "Doctrine",
                "EnableTrollSynergy",
                true,
                "When a Troll is near Greydwarf squads, greys orbit/peel as mobile-fortress skirmishers. Lone troll stays vanilla.");

            TrollSynergyRange = config.Bind(
                "Doctrine",
                "TrollSynergyRange",
                28f,
                "Max distance (m) from greydwarf squad centroid to a Troll for fortress synergy.");

            DebugLogging = config.Bind(
                "Debug",
                "DebugLogging",
                false,
                "Verbose squad/order logging.");
        }
    }
}
