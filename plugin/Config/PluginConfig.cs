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
        public static ConfigEntry<float> SquadMergeRadius { get; private set; } = null!;
        public static ConfigEntry<float> FormUpMagnetDistance { get; private set; } = null!;
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


        // --- Phase A actuation (1.0.5) ---
        public static ConfigEntry<float> HoldAttackCooldown { get; private set; } = null!;
        public static ConfigEntry<float> HoldAttackRangeFactor { get; private set; } = null!;
        public static ConfigEntry<float> SwingStaggerMs { get; private set; } = null!;
        public static ConfigEntry<float> FormationReshuffleSeconds { get; private set; } = null!;
        public static ConfigEntry<float> FormationCasualtyReshuffle { get; private set; } = null!;
        public static ConfigEntry<float> ChargeMaxSeconds { get; private set; } = null!;
        public static ConfigEntry<float> OrderMinDwellSeconds { get; private set; } = null!;
        public static ConfigEntry<float> TheaterCoEngageRadius { get; private set; } = null!;
        public static ConfigEntry<float> TheaterRoleDwellSeconds { get; private set; } = null!;
        public static ConfigEntry<float> OrderScoreHysteresis { get; private set; } = null!;
        public static ConfigEntry<float> RomanWallOuter { get; private set; } = null!;
        public static ConfigEntry<float> RomanWallInner { get; private set; } = null!;
        public static ConfigEntry<float> RomanStandoffDistance { get; private set; } = null!;
        public static ConfigEntry<float> RomanStandoffHoldMin { get; private set; } = null!;
        public static ConfigEntry<float> RomanStandoffHoldMax { get; private set; } = null!;
        public static ConfigEntry<float> RomanContactSwingRange { get; private set; } = null!;
        public static ConfigEntry<float> RomanRetreatPauseSeconds { get; private set; } = null!;
        public static ConfigEntry<float> VikingStandoffDistance { get; private set; } = null!;
        public static ConfigEntry<float> VikingIndoorsStandoff { get; private set; } = null!;
        public static ConfigEntry<float> VikingStandoffHoldMin { get; private set; } = null!;
        public static ConfigEntry<float> VikingStandoffHoldMax { get; private set; } = null!;
        public static ConfigEntry<float> VikingContactSwingRange { get; private set; } = null!;
        public static ConfigEntry<float> VikingRetreatPauseSeconds { get; private set; } = null!;
        public static ConfigEntry<float> FlankSplitMeters { get; private set; } = null!;
        public static ConfigEntry<float> IsolateBuddyMeters { get; private set; } = null!;
        public static ConfigEntry<bool> DiscoveryBurstOnSpawn { get; private set; } = null!;
        public static ConfigEntry<float> DiscoveryBurstSeconds { get; private set; } = null!;


        public static ConfigEntry<float> AmbushOuterPocket { get; private set; } = null!;
        public static ConfigEntry<float> AmbushInnerBand { get; private set; } = null!;
        public static ConfigEntry<float> AmbushReEncircleGap { get; private set; } = null!;
        public static ConfigEntry<float> AmbushAnchorHysteresis { get; private set; } = null!;
        public static ConfigEntry<float> AmbushStickySwitchDwellSeconds { get; private set; } = null!;


        private static readonly object BindGate = new object();

        public static void Bind(ConfigFile config)
        {
            lock (BindGate)
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

            SquadMergeRadius = config.Bind(
                "Squad",
                "SquadMergeRadius",
                40f,
                "Below-MinSquadSize same-doctrine clusters within this radius of an active (≥MinSquadSize) "
                + "same-doctrine squad merge into that parent so stragglers FormUp instead of vanilla wandering.");

            FormUpMagnetDistance = config.Bind(
                "Squad",
                "FormUpMagnetDistance",
                3.5f,
                "1.0.9 Pack-as-Unit: members farther than this from their formation slot PreferRun hard into the slot.");

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

            AmbushAnchorHysteresis = config.Bind(
                "Doctrine",
                "AmbushAnchorHysteresis",
                10f,
                "Ambush sticky player: switch orbit anchor only when a new nearest player is this many meters closer "
                + "(or sticky dead/out of range). Prevents orbit flap between nearby players.");

            AmbushStickySwitchDwellSeconds = config.Bind(
                "Doctrine",
                "AmbushStickySwitchDwellSeconds",
                1.0f,
                "Ambush sticky player: require the hysteresis+ closer alternate to stay closer for this many seconds "
                + "before switching. Single-tick spikes do not steal sticky. Invalid/dead/OOR sticky still switches immediately.");

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

            HoldAttackCooldown = config.Bind(
                "Combat",
                "HoldAttackCooldown",
                0.85f,
                "Phase A: seconds between Hold/Protect Front swings (floor; also respects m_minAttackInterval).");

            HoldAttackRangeFactor = config.Bind(
                "Combat",
                "HoldAttackRangeFactor",
                1.0f,
                "Phase A: scale of m_aiAttackRange required to swing on Hold/Protect Front.");

            SwingStaggerMs = config.Bind(
                "Combat",
                "SwingStaggerMs",
                75f,
                "Phase A: per-slot swing stagger in milliseconds (slotIndex * SwingStaggerMs).");

            FormationReshuffleSeconds = config.Bind(
                "Combat",
                "FormationReshuffleSeconds",
                3.0f,
                "Phase A: slot lock lifetime before formation lattice rebuild (leave holes for dead until then).");

            FormationCasualtyReshuffle = config.Bind(
                "Combat",
                "FormationCasualtyReshuffle",
                0.25f,
                "Phase A: casualty-ratio delta that forces a formation slot reshuffle.");

            ChargeMaxSeconds = config.Bind(
                "Combat",
                "ChargeMaxSeconds",
                4.0f,
                "Phase A/B: hard cap on Charge/FlashCharge, then re-eval (DeathRush exempt; Roman with missiles returns to ProtectMissiles).");

            OrderMinDwellSeconds = config.Bind(
                "Combat",
                "OrderMinDwellSeconds",
                1.25f,
                "Phase B: minimum seconds on an order before a change. Bypassed on threat lost, broken, Ambush Charge→Kite, or a Roman/Viking cadence timer expiry.");

            TheaterCoEngageRadius = config.Bind(
                "Combat",
                "TheaterCoEngageRadius",
                48f,
                "1.0.11 Theater: packs whose centroid is within this many meters of the same player share Pin/Flank/Harass. Default 48.");

            TheaterRoleDwellSeconds = config.Bind(
                "Combat",
                "TheaterRoleDwellSeconds",
                2.5f,
                "1.0.11 Theater: seconds a Pin/Flank/Harass job sticks before it can change. Death-Rush is never assigned. Default 2.5.");

            OrderScoreHysteresis = config.Bind(
                "Combat",
                "OrderScoreHysteresis",
                0.15f,
                "Phase B: Roman/Ambush switch orders only when the new score exceeds the current by this margin.");

            RomanWallOuter = config.Bind(
                "Combat",
                "RomanWallOuter",
                18f,
                "Legacy soft band (meters). 1.0.7 cadence uses RomanStandoffDistance. Kept so older ft set keys still bind.");

            RomanWallInner = config.Bind(
                "Combat",
                "RomanWallInner",
                14f,
                "Legacy soft band (meters). Not the cadence hold line. Kept so older ft set keys still bind.");

            RomanStandoffDistance = config.Bind(
                "Combat",
                "RomanStandoffDistance",
                20f,
                "Roman cadence: advance to this standoff (meters) from the player, then Hold for one rolled duration. Default 20.");

            RomanStandoffHoldMin = config.Bind(
                "Combat",
                "RomanStandoffHoldMin",
                1f,
                "Roman cadence: minimum standoff Hold seconds (rolled once per entry, inclusive). Default 1.");

            RomanStandoffHoldMax = config.Bind(
                "Combat",
                "RomanStandoffHoldMax",
                15f,
                "Roman cadence: maximum standoff Hold seconds (rolled once per entry, inclusive). Default 15.");

            RomanContactSwingRange = config.Bind(
                "Combat",
                "RomanContactSwingRange",
                3.5f,
                "Roman cadence: press until the front line is within this swing band (meters), then Hold. Charge gate still uses RomanChargeRange.");

            RomanRetreatPauseSeconds = config.Bind(
                "Combat",
                "RomanRetreatPauseSeconds",
                1f,
                "Roman cadence: Hold seconds after the player opens out of swing, then Advance again. Default 1.");

            VikingStandoffDistance = config.Bind(
                "Combat",
                "VikingStandoffDistance",
                14f,
                "Viking cadence: open-field standoff (meters). Default 14 (choke band ~12–15).");

            VikingIndoorsStandoff = config.Bind(
                "Combat",
                "VikingIndoorsStandoff",
                12f,
                "Viking cadence: indoors/crypt standoff (meters). Live standoff is min(open, this) when IndoorsOrCrypt.");

            VikingStandoffHoldMin = config.Bind(
                "Combat",
                "VikingStandoffHoldMin",
                1f,
                "Viking cadence: minimum standoff Hold seconds. Default 1.");

            VikingStandoffHoldMax = config.Bind(
                "Combat",
                "VikingStandoffHoldMax",
                8f,
                "Viking cadence: maximum standoff Hold seconds. Default 8.");

            VikingContactSwingRange = config.Bind(
                "Combat",
                "VikingContactSwingRange",
                3.5f,
                "Viking cadence: press until the front line is within this swing band (meters). Default 3.5.");

            VikingRetreatPauseSeconds = config.Bind(
                "Combat",
                "VikingRetreatPauseSeconds",
                1f,
                "Viking cadence: Hold seconds after the player opens out of swing, then press. Default 1.");

            FlankSplitMeters = config.Bind(
                "Combat",
                "FlankSplitMeters",
                12f,
                "Phase A/B: players split farther than this → FlankOpportunity (default 12).");

            IsolateBuddyMeters = config.Bind(
                "Combat",
                "IsolateBuddyMeters",
                8f,
                "Phase A/B: no allied player within this of the contact player → TargetIsolated (default 8).");

            DiscoveryBurstOnSpawn = config.Bind(
                "Combat",
                "DiscoveryBurstOnSpawn",
                true,
                "Phase A: briefly speed SquadDirector ticks on spawn / near-player ZDO candidate spike.");

            DiscoveryBurstSeconds = config.Bind(
                "Combat",
                "DiscoveryBurstSeconds",
                0.2f,
                "Phase A: burst tick interval (seconds) during discovery spike window, then back to TickIntervalSeconds.");


            RegisterAllKnobs();
            try { FactionTactics.Combat.CombatTuning.CopyFromPluginConfig(); } catch { /* ok */ }
            } // end BindGate
        }

        private static void RegisterAllKnobs()
        {
            Register(
                EnablePlugin, TickIntervalSeconds, MinSquadSize, DiscoveryRadius, SquadClusterRadius, SquadMergeRadius, FormUpMagnetDistance,
                EnableRoman, RomanChargeRange, RomanPreferRanged,
                EnableAmbush, AmbushOuterPocket, AmbushInnerBand, AmbushReEncircleGap, AmbushAnchorHysteresis, AmbushStickySwitchDwellSeconds,
                EnableDeathRush, EnableDeathRushScream, DeathRushScreamCooldownSeconds, DeathRushMinSquadSize,
                EnableVikingShieldWall, EnableSteppe, EnableInsectSiege, EnableCharredLegion,
                EnablePackHunters, EnableArtilleryJelly, EnableTrollSynergy, TrollSynergyRange,
                StructureDefenseRange, DvergrSoftenRange,
                EnableSiegeAssault, WorkbenchTriggerRange, SiegeMinSquadSize, EnableSiegeAmbush, EnableSiegeViking,
                DebugLogging, HeartbeatLogging,
                EnableEnemyServerOwnership, EnemyOwnershipIntervalSeconds, EnemyOwnershipMaxCreatesPerTick,
                EnableStickyEnemyOwnership, EnableRpcIntentSync, EnableZdoIntentSync, EnableOwnerCombatExecutor,
                EnableAmbushAmbienceTemp, AmbushAmbienceFogEnvironment, AmbushAmbienceMessage,
                AmbushAmbienceMessageCooldownSeconds, AmbushAmbiencePlayerRange, AmbushAmbienceMinSquadSize,
                HoldAttackCooldown, HoldAttackRangeFactor, SwingStaggerMs,
                FormationReshuffleSeconds, FormationCasualtyReshuffle, ChargeMaxSeconds,
                OrderMinDwellSeconds, TheaterCoEngageRadius, TheaterRoleDwellSeconds, OrderScoreHysteresis, RomanWallOuter, RomanWallInner,
                RomanStandoffDistance, RomanStandoffHoldMin, RomanStandoffHoldMax,
                RomanContactSwingRange, RomanRetreatPauseSeconds,
                VikingStandoffDistance, VikingIndoorsStandoff, VikingStandoffHoldMin, VikingStandoffHoldMax,
                VikingContactSwingRange, VikingRetreatPauseSeconds,
                FlankSplitMeters, IsolateBuddyMeters, DiscoveryBurstOnSpawn, DiscoveryBurstSeconds);
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
