<!-- Faction Tactics 1.0.1: ZDO-only commander discovery (no live MonsterAI required). Sticky/ownership default OFF. -->
# Server combat AI roadmap — 0.3.0 hybrid commander / executor

**Date:** 2026-09-19 (America/Chicago)  
**Status:** 0.3.0 implemented offline — **no GPortal deploy** from this change.

## 1.0.1 fix (ZDO-only commander)

Dedicated with sticky/ownership **OFF** often has `monsterAI=0` → no squads → no `ft_iv` writes → client `schema mismatch (zdo=0 local=2)` and vanilla feel.

**Fix:** `ValheimWorldScan.EnumerateEnemyZdosNearPlayers` + `SquadDiscovery` ZDO-backed members (`NativeHandle=ZDO`). `OrderApplicator` already calls `IntentZdoSync.Write(zdo, …)`. Heartbeat: `zdoCandidates`, `zdoIntentsWritten`, `schemaWrites`. Client executor unchanged.


## Pivot (Nate)

IronGate client-side combat = smooth fights. Full sticky server ownership (0.2.3) → statues / unhittable.  
**Do not** optimize for server-owned `UpdateAI` as the primary combat path.

## Architecture (0.3.0)

| Role | Assembly | Responsibility |
|------|----------|----------------|
| **Commander** | `FactionTactics.dll` (server) | Discovery, squads, doctrines, FSM, siege hooks. Writes `MemberIntent` to enemy **ZDO custom fields**. Sticky/ownership **default false**. Does **not** sole-brain combat on dedicated. |
| **Executor** | `FactionTactics.Client.dll` (players) | Reads ZDO intents. `Drive` MoveTo/Hold/LookAt/DoAttack only when **local IsOwner**. Prefix-skips vanilla chase when intent present. Physics + hits stay client-owned. |
| **Shared** | Linked sources | `IntentZdoCodec` schema v1, `IntentZdoSync`, `CombatDriver`, `FtVersion` (product **0.3.0**). Schema mismatch → loud log + ignore intents. |

### Install (Ungrull-friendly)

- `dist/FactionTactics-Server.zip` + `dist/FactionTactics-Client.zip` (+ Thunderstore variants).
- One-page `INSTALL.md` inside each zip.
- Discord blurb: `dist/DISCORD-BLURB.md` (do not post unless asked).

### Config defaults (safe)

- `EnableEnemyServerOwnership` = **false**
- `EnableStickyEnemyOwnership` = **false**
- `EnableZdoIntentSync` = **true**
- `EnableOwnerCombatExecutor` = **true** (client / listen host)

Sticky/ownership code remains behind those flags for debug capture only.

## Proven earlier (keep)

- Enemy ZDO claim + CreateObject (when flags on)
- Discovery → squads; Prefix sole-brain when owner
- Form-up-before-Hold (0.2.2)

## Failed live (0.2.3 sticky) — lesson

Sticky flipped `enemyClientOwned=0` but mobs immobile / unaware / unhittable. Thin ownership ≠ full SSS simulation. Hybrid keeps IsOwner on clients.

## Non-goals

- GPortal FTP/bounce from this work
- Full SSS install
- Raising spawn/raid rates
