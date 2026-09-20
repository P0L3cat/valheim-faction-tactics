using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;

namespace FactionTactics.Config
{
    public static class PluginConfig
    {
        /// <summary>Bound ConfigFile for Save/Reload from console knobs.</summary>
        public static ConfigFile? File { get; private set; }

        private static readonly Dictionary<string, ConfigEntryBase> Knobs =
            new Dictionary<string, ConfigEntryBase>(StringComparer.OrdinalIgnoreCase);

        public static ConfigEntry<bool> EnablePlugin { get; private set; } = null!;
        public static ConfigEntry<float> TickIntervalSeconds { get; private set; } = null!;
        public static ConfigEntry<int> MinSquadSize { get; private set; } = null!;
        public static ConfigEntry<float> DiscoveryRadius { get; private set; } = null!;
        public static ConfigEntry<float> SquadClusterRadius { get; private set; } = null!;
        public static ConfigEntry<bool> EnableRoman { get; private set; } = null!;
        public static ConfigEntry<float> RomanChargeRange { get; private set; } = null!;
        public static ConfigEntry<bool> RomanPreferRanged { get; private set; } = null!;
        public static ConfigEntry<bool> EnableAmbush { get; private set; } = null!;
        public static ConfigEntry<bool> EnableDeathRush { get; private set; } = null!;
        public static ConfigEntry<bool> EnableDeathRushScream { get; private set; } = null!;
        public static ConfigEntry<float> DeathRushScreamCooldownSeconds { get; private set; } = null!;
        public static ConfigEntry<int> DeathRushMinSquadSize { get; private set; } = null!;
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
        public static ConfigEntry<bool> HeartbeatLogging { get; private set; } = null!;

        // --- Dedicated enemy ownership PoC (0.1.9) ---
        public static ConfigEntry<bool> EnableEnemyServerOwnership { get; private set; } = null!;
        public static ConfigEntry<float> EnemyOwnershipIntervalSeconds { get; private set; } = null!;
        public static ConfigEntry<int> EnemyOwnershipMaxCreatesPerTick { get; private set; } = null!;
        public static ConfigEntry<bool> EnableStickyEnemyOwnership { get; private set; } = null!;

        public static ConfigEntry<bool> EnableRpcIntentSync { get; private set; } = null!;
        public static ConfigEntry<bool> EnableZdoIntentSync { get; private set; } = null!;
        public static ConfigEntry<bool> EnableOwnerCombatExecutor { get; private set; } = null!;


        // --- TEMP Black Forest ambush ambience (troubleshooting wire) ---
        public static ConfigEntry<bool> EnableAmbushAmbienceTemp { get; private set; } = null!;
        public static ConfigEntry<string> AmbushAmbienceFogEnvironment { get; private set; } = null!;
        public static ConfigEntry<string> AmbushAmbienceMessage { get; private set; } = null!;
        public static ConfigEntry<float> AmbushAmbienceMessageCooldownSeconds { get; private set; } = null!;
        public static ConfigEntry<float> AmbushAmbiencePlayerRange { get; private set; } = null!;
        public static ConfigEntry<int> AmbushAmbienceMinSquadSize { get; private set; } = null!;

        public static ConfigEntry<float> AmbushOuterPocket { get; private set; } = null!;
        public static ConfigEntry<float> AmbushInnerBand { get; private set; } = null!;
        public static ConfigEntry<float> AmbushReEncircleGap { get; private set; } = null!;


        public static void Bind(ConfigFile config)
        {
            File = config;
            Knobs.Clear();

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
                64f,
                "Max distance from any local player when scanning for faction allies (default 64; was briefly "
                + "documented as 40). Distinct from SquadClusterRadius (how tight a squad groups).");

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

                        RomanChargeRange = config.Bind(
                "Doctrine",
                "RomanChargeRange",
                3.5f,
                "Roman: max charge commit distance (m). Effective = min(snapshot ChargeRange, this). "
                + "Default 3.5 — Charge almost never; shield wall + archers first.");

            RomanPreferRanged = config.Bind(
                "Doctrine",
                "RomanPreferRanged",
                true,
                "Roman: prefer Hold/ProtectMissiles/FocusFire under ShieldWall. "
                + "Charge only when nearest < RomanChargeRange and (no missiles left or last-resort morale).");


            EnableAmbush = config.Bind(
                "Doctrine",
                "EnableAmbush",
                true,
                "Greydwarf* → Black Forest Ambush predators.");
            EnableDeathRush = config.Bind(
                "Doctrine",
                "EnableDeathRush",
                true,
                "Meadows Greyling Death-Rush: bee-line charge, fight to the death (no flee). Behavior-only.");
            EnableDeathRushScream = config.Bind(
                "Doctrine",
                "EnableDeathRushScream",
                true,
                "Play vanilla Greyling alert/hurt SFX on Death-Rush aggro/charge (no new assets).");
            DeathRushScreamCooldownSeconds = config.Bind(
                "Doctrine",
                "DeathRushScreamCooldownSeconds",
                4.5f,
                "Minimum seconds between Death-Rush scream pulses per mob.");
            DeathRushMinSquadSize = config.Bind(
                "Doctrine",
                "DeathRushMinSquadSize",
                1,
                "Min Greylings to form a Death-Rush squad (Meadows packs are often tiny).");


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

            HeartbeatLogging = config.Bind(
                "Debug",
                "HeartbeatLogging",
                true,
                "Periodic (~15s) LogInfo of squad count, top order kinds, MonsterAI candidates, player count. "
                + "Default true for smoke tests; set false once discovery is confirmed.");

            EnableEnemyServerOwnership = config.Bind(
                "Dedicated",
                "EnableEnemyServerOwnership",
                false,
                "Debug PoC only (0.3.0 default FALSE): claim ZDO ownership + CreateObject for enemy prefabs near peers "
                + "so MonsterAI can run on dedicated. Prefer hybrid ZDO intents + client ownership for combat latency. "
                + "See docs/SERVER-COMBAT-AI-ROADMAP.md.");

            EnemyOwnershipIntervalSeconds = config.Bind(
                "Dedicated",
                "EnemyOwnershipIntervalSeconds",
                1.0f,
                "Seconds between EnemyOwnershipDirector scans (peer FindSectorObjects + SetOwner/Create).");

            EnemyOwnershipMaxCreatesPerTick = config.Bind(
                "Dedicated",
                "EnemyOwnershipMaxCreatesPerTick",
                16,
                "Max ZNetScene.CreateObject calls per ownership pass (avoids hitching).");

            
                        EnableRpcIntentSync = config.Bind(
                "Hybrid",
                "EnableRpcIntentSync",
                true,
                "1.0.3: primary path — server broadcasts MemberIntent batches via ZRoutedRpc/ZPackage. Clients cache by ZDOID.");

            EnableZdoIntentSync = config.Bind(
                "Hybrid",
                "EnableZdoIntentSync",
                false,
                "1.0.3 optional/debug fallback: write MemberIntent into enemy ZDO customs. Unreliable when dedicated is not ZDO owner (ZDO.Set from non-owner often never reaches owning client). Prefer EnableRpcIntentSync.");

            EnableOwnerCombatExecutor = config.Bind(
                "Hybrid",
                "EnableOwnerCombatExecutor",
                true,
                "0.3.0: on non-dedicated peers that own an enemy ZDO, Prefix-skip vanilla UpdateAI and DriveControlledAI from intent/RPC/ZDO.");

            EnableStickyEnemyOwnership = config.Bind(
                "Dedicated",
                "EnableStickyEnemyOwnership",
                false,
                "0.2.3 sticky reclaim (debug): Harmony Prefix on ZDOMan.ReleaseNearbyZDOS keeps enemy prefabs "
                + "server-owned near peers. Non-enemies keep vanilla reclaim. Requires EnableEnemyServerOwnership. "
                + "Default FALSE in 0.3.0 hybrid (client-owned combat). See docs/SERVER-COMBAT-AI-ROADMAP.md.");


            AmbushOuterPocket = config.Bind(
                "Doctrine",
                "AmbushOuterPocket",
                18f,
                "Ambush: outside this distance (m) → Hold/Kite lurk.");

            AmbushInnerBand = config.Bind(
                "Doctrine",
                "AmbushInnerBand",
                8f,
                "Ambush: inner harassment band lower edge (m); 8–OuterPocket → Flank.");

            AmbushReEncircleGap = config.Bind(
                "Doctrine",
                "AmbushReEncircleGap",
                14f,
                "Ambush: after Kite, re-encircle (Flank) once gap exceeds this (m).");

            EnableAmbushAmbienceTemp = config.Bind(
                "AmbushAmbienceTemp",
                "EnableAmbushAmbienceTemp",
                true,
                "TEMP wire for BF ambush troubleshooting; disable for proper silent ambush.");

            AmbushAmbienceFogEnvironment = config.Bind(
                "AmbushAmbienceTemp",
                "AmbushAmbienceFogEnvironment",
                "Misty",
                "EnvMan force env name (empty = skip fog).");

            AmbushAmbienceMessage = config.Bind(
                "AmbushAmbienceTemp",
                "AmbushAmbienceMessage",
                "the hair on your neck stands up",
                "Center message shown once per player per cooldown when a qualifying Ambush squad is nearby.");

            AmbushAmbienceMessageCooldownSeconds = config.Bind(
                "AmbushAmbienceTemp",
                "AmbushAmbienceMessageCooldownSeconds",
                90f,
                "Seconds before the neck-hair message may fire again for the same player.");

            AmbushAmbiencePlayerRange = config.Bind(
                "AmbushAmbienceTemp",
                "AmbushAmbiencePlayerRange",
                40f,
                "Max distance (m) from an Ambush member/centroid to a player to trigger ambience.");

            AmbushAmbienceMinSquadSize = config.Bind(
                "AmbushAmbienceTemp",
                "AmbushAmbienceMinSquadSize",
                3,
                "Minimum alive Ambush (Greydwarf) squad size before ambience can fire.");

            RegisterAllKnobs();
        }

        private static void RegisterAllKnobs()
        {
            Register(
                EnablePlugin, TickIntervalSeconds, MinSquadSize, DiscoveryRadius, SquadClusterRadius,
                EnableRoman, RomanChargeRange, RomanPreferRanged,
                EnableAmbush, AmbushOuterPocket, AmbushInnerBand, AmbushReEncircleGap,
                EnableDeathRush, EnableDeathRushScream, DeathRushScreamCooldownSeconds, DeathRushMinSquadSize,
                EnableVikingShieldWall, EnableSteppe, EnableInsectSiege, EnableCharredLegion,
                EnablePackHunters, EnableArtilleryJelly, EnableTrollSynergy, TrollSynergyRange,
                StructureDefenseRange, DvergrSoftenRange,
                EnableSiegeAssault, WorkbenchTriggerRange, SiegeMinSquadSize, EnableSiegeAmbush, EnableSiegeViking,
                DebugLogging, HeartbeatLogging,
                EnableEnemyServerOwnership, EnemyOwnershipIntervalSeconds, EnemyOwnershipMaxCreatesPerTick,
                EnableStickyEnemyOwnership, EnableRpcIntentSync, EnableZdoIntentSync, EnableOwnerCombatExecutor,
                EnableAmbushAmbienceTemp, AmbushAmbienceFogEnvironment, AmbushAmbienceMessage,
                AmbushAmbienceMessageCooldownSeconds, AmbushAmbiencePlayerRange, AmbushAmbienceMinSquadSize);
        }

        private static void Register(params ConfigEntryBase[] entries)
        {
            foreach (var e in entries)
            {
                if (e == null)
                    continue;
                Knobs[e.Definition.Key] = e;
            }
        }

        public static bool TryGetKnob(string key, out ConfigEntryBase? entry)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                entry = null;
                return false;
            }
            return Knobs.TryGetValue(key.Trim(), out entry);
        }

        public static IEnumerable<string> KnobKeys =>
            Knobs.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

        public static IEnumerable<string> FormatKnobHelpLines()
        {
            foreach (var kv in Knobs.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var e = kv.Value;
                var type = e.SettingType.Name;
                var val = e.BoxedValue;
                var valStr = val is float f
                    ? f.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
                    : Convert.ToString(val, System.Globalization.CultureInfo.InvariantCulture);
                yield return $"{e.Definition.Key} ({type}, {e.Definition.Section}) = {valStr}";
            }
        }
    }
}
