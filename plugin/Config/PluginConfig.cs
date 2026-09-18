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
        public static ConfigEntry<bool> EnableAmbush { get; private set; } = null!;
        public static ConfigEntry<bool> EnableVikingShieldWall { get; private set; } = null!;
        public static ConfigEntry<bool> EnableSteppe { get; private set; } = null!;
        public static ConfigEntry<bool> EnableInsectSiege { get; private set; } = null!;
        public static ConfigEntry<bool> EnableCharredLegion { get; private set; } = null!;
        public static ConfigEntry<bool> EnablePackHunters { get; private set; } = null!;
        public static ConfigEntry<bool> EnableArtilleryJelly { get; private set; } = null!;
        public static ConfigEntry<bool> EnableTrollSynergy { get; private set; } = null!;
        public static ConfigEntry<float> TrollSynergyRange { get; private set; } = null!;
        public static ConfigEntry<float> StructureDefenseRange { get; private set; } = null!;
        public static ConfigEntry<float> DvergrSoftenRange { get; private set; } = null!;

        // --- Siege Assault v1 ---
        public static ConfigEntry<bool> EnableSiegeAssault { get; private set; } = null!;
        public static ConfigEntry<float> WorkbenchTriggerRange { get; private set; } = null!;
        public static ConfigEntry<int> SiegeMinSquadSize { get; private set; } = null!;
        public static ConfigEntry<bool> EnableSiegeAmbush { get; private set; } = null!;
        public static ConfigEntry<bool> EnableSiegeViking { get; private set; } = null!;

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
                "Max distance from any local player when scanning for faction allies. "
                + "Distinct from SquadClusterRadius (how tight a squad groups).");

            SquadClusterRadius = config.Bind(
                "Squad",
                "SquadClusterRadius",
                18f,
                "Max distance between members to belong to the same squad cluster.");

            EnableRoman = config.Bind(
                "Doctrine",
                "EnableRoman",
                true,
                "Skeleton* → Roman doctrine.");

            EnableAmbush = config.Bind(
                "Doctrine",
                "EnableAmbush",
                true,
                "Greydwarf* → Black Forest Ambush predators.");

            EnableVikingShieldWall = config.Bind(
                "Doctrine",
                "EnableVikingShieldWall",
                true,
                "Draugr* → VikingShieldWall (shield wall, archers behind, charge, reform).");

            EnableSteppe = config.Bind(
                "Doctrine",
                "EnableSteppe",
                true,
                "Fuling*/Goblin* → Steppe (kite, volley, encircle; village defense orbit).");

            EnableInsectSiege = config.Bind(
                "Doctrine",
                "EnableInsectSiege",
                true,
                "Seeker*/Tick*/Gjall* → InsectSiege (Mistlands; soften near Dvergr).");

            EnableCharredLegion = config.Bind(
                "Doctrine",
                "EnableCharredLegion",
                true,
                "Charred*/Asksvin* → CharredLegion (dense ranks + cavalry flankers).");

            EnablePackHunters = config.Bind(
                "Doctrine",
                "EnablePackHunters",
                true,
                "Wolf*/Drake*/Hatchling* → PackHunters (encircle + overwatch).");

            EnableArtilleryJelly = config.Bind(
                "Doctrine",
                "EnableArtilleryJelly",
                true,
                "Blob* → ArtilleryJelly (keep range, zone denial, no melee chase).");

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

            StructureDefenseRange = config.Bind(
                "Doctrine",
                "StructureDefenseRange",
                24f,
                "Steppe NearStructure placeholder range (m) when village/totem/structure heuristic is unavailable.");

            DvergrSoftenRange = config.Bind(
                "Doctrine",
                "DvergrSoftenRange",
                30f,
                "InsectSiege: soften aggression when Dvergr* allies/neutrals are within this range (m).");

            EnableSiegeAssault = config.Bind(
                "Siege",
                "EnableSiegeAssault",
                true,
                "Siege Assault v1 master switch (Assault only — no Defense/Raid Event). "
                + "Does not spawn or start raids; may make already-nearby mobs fight / push bases harder.");

            WorkbenchTriggerRange = config.Bind(
                "Siege",
                "WorkbenchTriggerRange",
                48f,
                "Radius (m) from squad centroid to detect player workbench / crafting stations for Assault.");

            SiegeMinSquadSize = config.Bind(
                "Siege",
                "SiegeMinSquadSize",
                3,
                "Minimum squad size to enter Siege Assault stance (may differ from Squad.MinSquadSize).");

            EnableSiegeAmbush = config.Bind(
                "Siege",
                "EnableSiegeAmbush",
                true,
                "Allow Ambush (Greydwarf / Black Forest) squads to enter Siege Assault. Meadows: no siege.");

            EnableSiegeViking = config.Bind(
                "Siege",
                "EnableSiegeViking",
                true,
                "Allow VikingShieldWall (Draugr / Swamp) squads to enter Siege Assault. Higher biomes: not enabled yet.");

            DebugLogging = config.Bind(
                "Debug",
                "DebugLogging",
                false,
                "Verbose squad/order logging.");
        }
    }
}
