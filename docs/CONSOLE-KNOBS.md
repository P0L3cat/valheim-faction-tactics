# Faction Tactics console knobs (1.0.7)

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


## Client admin (dedicated) — 1.0.5

Nate joins a **dedicated** server with Server Devcommands. From the **client F5 console** as admin:

```
ft help
ft get HoldAttackCooldown
ft set HoldAttackCooldown 1.2
ft set SwingStaggerMs 100
ft set FormationReshuffleSeconds 3
ft set ChargeMaxSeconds 4
ft reload
ft status
```

How it works:

1. Client registers `ft` with `onlyServer:false`, `onlyAdmin:true`.
2. `ft set` / `ft reload` / `ft get` / `ft status` / `ft help` send `FT_ConfigCmd` ZRoutedRpc to the dedicated server.
3. Server checks the sender against the ZNet admin list (`PlayerIsAdmin` / `m_adminList`). Non-admins get `[ft] denied`.
4. Server applies `PluginConfig` + `ConfigFile.Save`, then replies via `FT_ConfigReply`.
5. Executor knobs (`HoldAttackCooldown`, `HoldAttackRangeFactor`, `SwingStaggerMs`) are broadcast to all peers via `FT_ConfigSync` so owning-client CombatDrivers pick them up.

Listen-host (server DLL present) still uses the local `ft` command (no RPC). Pure clients skip registering when the server DLL is also loaded.

### Phase A combat knobs

| Key | Default | Meaning |
|-----|---------|---------|
| `HoldAttackCooldown` | 0.85 | Seconds between Hold/Protect Front swings |
| `HoldAttackRangeFactor` | 1.0 | Scale of `m_aiAttackRange` for the swing gate |
| `SwingStaggerMs` | 75 | Per-slot stagger (ms) |
| `FormationReshuffleSeconds` | 3.0 | Slot lock lifetime before lattice rebuild |
| `FormationCasualtyReshuffle` | 0.25 | Casualty delta that forces reshuffle |
| `ChargeMaxSeconds` | 4.0 | Hard cap on Charge/FlashCharge, then re-eval (DeathRush exempt) |
| `FlankSplitMeters` | 12 | Players farther apart → FlankOpportunity |
| `IsolateBuddyMeters` | 8 | No buddy within this → TargetIsolated |
| `DiscoveryBurstOnSpawn` | true | Fast ticks on candidate spike |
| `DiscoveryBurstSeconds` | 0.2 | Burst tick interval |

### Phase B combat knobs

| Key | Default | Meaning |
|-----|---------|---------|
| `OrderMinDwellSeconds` | 1.25 | Min time on an order before a change. Skipped on threat lost, broken, Ambush Charge→Kite, or a Roman/Viking cadence timer expiry |
| `OrderScoreHysteresis` | 0.15 | Ambush (and any scored pick) needs this much score lead to leave the current order. Roman cadence does not use it to freeze a press |
| `RomanWallOuter` | 18 | Legacy. Not the hold line. Cadence uses `RomanStandoffDistance` |
| `RomanWallInner` | 14 | Legacy. Not the hold line |
| `ChargeMaxSeconds` | 4.0 | Already in Phase A. Roman with live missiles re-evals to ProtectMissiles; Ambush to Kite |

### 1.0.7 banded cadence

HoldGround is rare. Romans and Vikings only plant during standoff hold, contact hold, or the 1s retreat pause, and only within `FrontHoldSlotDist` of the slot. Advance never pins because a member is already on the slot.

| Key | Default | Meaning |
|-----|---------|---------|
| `RomanStandoffDistance` | 20 | Advance to this line (meters), then Hold |
| `RomanStandoffHoldMin` | 1 | Shortest rolled standoff Hold (seconds, inclusive) |
| `RomanStandoffHoldMax` | 15 | Longest rolled standoff Hold (seconds, inclusive). Rolled once per entry |
| `RomanContactSwingRange` | 3.5 | Press until the front line is inside this band, then Hold |
| `RomanRetreatPauseSeconds` | 1 | Hold after the player opens out of swing, then Advance again |
| `VikingStandoffDistance` | 14 | Open-field standoff (choke ~12–15m) |
| `VikingIndoorsStandoff` | 12 | Crypt/indoors standoff. Live value is min(open, this) when indoors |
| `VikingStandoffHoldMin` | 1 | Shortest Viking standoff Hold |
| `VikingStandoffHoldMax` | 8 | Longest Viking standoff Hold |
| `VikingContactSwingRange` | 3.5 | Viking press-to-swing band |
| `VikingRetreatPauseSeconds` | 1 | Viking retreat pause |

```
ft set OrderMinDwellSeconds 1.25
ft set OrderScoreHysteresis 0.15
ft set RomanStandoffDistance 20
ft set RomanStandoffHoldMax 15
ft set RomanContactSwingRange 3.5
ft set RomanRetreatPauseSeconds 1
ft set VikingStandoffDistance 14
ft set VikingIndoorsStandoff 12
ft get ChargeMaxSeconds
```
