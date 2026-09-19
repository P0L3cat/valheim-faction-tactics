# Faction Tactics console knobs (1.0.2)

Admin / dedicated / listen-host **Terminal** commands to tweak live `PluginConfig` without redeploying DLLs.

Prefix: **`ft`** (also documented as `factiontactics` conceptually — the registered command is `ft`).

Works on:

- **Dedicated server** console (F2 / stdin where available)
- **Listen-host** (host machine with server DLL) — full get/set
- **Pure clients** — read-only `ft status` / `ft help` (skipped if server DLL is also loaded)

Requires **admin/server** for mutating commands (`onlyServer` + `LocalPlayerIsAdminOrHost`).

## Commands

| Command | Effect |
|---------|--------|
| `ft help` | List subcommands + all knobs with current values |
| `ft get <key>` | Read one ConfigEntry |
| `ft set <key> <value>` | Set ConfigEntry, persist via `ConfigFile.Save` |
| `ft reload` | `ConfigFile.Reload()` from disk |
| `ft status` | Version, schema, discovery/squad/ZDO counters |

Examples:

```
ft set MinSquadSize 2
ft set EnableDeathRushScream false
ft set AmbushOuterPocket 22
ft get TickIntervalSeconds
ft reload
ft status
```

Bool values accept: `true`/`false`, `1`/`0`, `on`/`off`, `yes`/`no`.

## Hot knobs (take effect next tick)

SquadDirector and doctrines already read `ConfigEntry.Value` each tick. After `ft set`, the next tick uses the new value. No DLL bounce required.

### Keys (short = ConfigEntry key)

**General / squad:** `EnablePlugin`, `TickIntervalSeconds`, `MinSquadSize`, `DiscoveryRadius`, `SquadClusterRadius`

**Doctrines:** `EnableRoman`, `RomanChargeRange`, `RomanPreferRanged`, `EnableAmbush`, `AmbushOuterPocket`, `AmbushInnerBand`, `AmbushReEncircleGap`, `EnableDeathRush`, `EnableDeathRushScream`, `DeathRushScreamCooldownSeconds`, `DeathRushMinSquadSize`, `EnableVikingShieldWall`, `EnableSteppe`, `EnableInsectSiege`, `EnableCharredLegion`, `EnablePackHunters`, `EnableArtilleryJelly`, `EnableTrollSynergy`, `TrollSynergyRange`, `StructureDefenseRange`, `DvergrSoftenRange`

**Siege:** `EnableSiegeAssault`, `WorkbenchTriggerRange`, `SiegeMinSquadSize`, `EnableSiegeAmbush`, `EnableSiegeViking`

**Hybrid / dedicated:** `EnableZdoIntentSync`, `EnableOwnerCombatExecutor`, `EnableEnemyServerOwnership`, `EnemyOwnershipIntervalSeconds`, `EnemyOwnershipMaxCreatesPerTick`, `EnableStickyEnemyOwnership`

**Debug:** `DebugLogging`, `HeartbeatLogging`

**Ambush ambience (temp):** `EnableAmbushAmbienceTemp`, `AmbushAmbienceFogEnvironment`, `AmbushAmbienceMessage`, `AmbushAmbienceMessageCooldownSeconds`, `AmbushAmbiencePlayerRange`, `AmbushAmbienceMinSquadSize`

## Notes

- `ft set` writes the BepInEx config file (`BepInEx/config/com.nate.factiontactics.cfg`).
- `EnablePlugin false` stops ticking but console stays registered so you can turn it back on.
- Sticky / enemy server ownership remain debug-oriented; prefer hybrid ZDO intents + client executor.
