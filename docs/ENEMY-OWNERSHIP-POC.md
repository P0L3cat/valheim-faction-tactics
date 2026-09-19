# Enemy ownership PoC (0.1.9)

**Date:** 2026-09-19 (CT)  
**Status:** thin slice — server-only; do **not** treat as production networking.

## Goal

On dedicated Linux, Faction Tactics needs live `MonsterAI` so `UpdateAI` / OrderApplicator can run. Vanilla dedicated holds enemy ZDOs but often never creates GameObjects (`prefabLive=0`, `updateAIHits=0`). Full **Serverside Simulations** was aborted; this PoC steals only the enemy slice.

## What we stole from SSS

From decompiled SSS `CreateDestroyObjects` / `ReleaseNearbyZDOS` patterns (local zip under `/workspace/valheim-mods/sss/extracted/` — reference only):

1. Walk **connected peers** and use `peer.GetRefPos()` (not `ZNet.GetReferencePosition()`).
2. `ZDOMan.FindSectorObjects` around each peer.
3. `zdo.SetOwner(ZNet.GetUID())` when the server is not already owner.
4. Ensure a live instance exists via the game’s **`ZNetScene.CreateObject(ZDO)`** (private — called through reflection), not a hand-rolled Instantiate.

## What we left vanilla

- **No** `ZoneSystem.Update` Prefix/replace (no `CreateLocalZones` for all peers).
- **No** wholesale `ZNetScene.CreateDestroyObjects` replace.
- **No** ship / WearNTear / terrain / piece ownership transfer.
- Filter is **enemy prefabs only** (`ValheimWorldScan.IsLikelyEnemyPrefabZdo` / `KnownEnemyPrefabs`).

## Config

| Key | Default | Notes |
|-----|---------|--------|
| `Dedicated.EnableEnemyServerOwnership` | **true** (PoC) | Master gate; turn off if desync/CPU issues. |
| `Dedicated.EnemyOwnershipIntervalSeconds` | `1.0` | Scan cadence. |
| `Dedicated.EnemyOwnershipMaxCreatesPerTick` | `16` | Cap CreateObject calls per pass. |

## Heartbeat

`enemyOwned=` `enemyLive=` `enemyMai=` alongside existing counters. Success signal: with peers online and enemies nearby, `enemyLive` / `enemyMai` and `updateAIHits` should rise vs 0.1.8 dedicated baselines.

## Risks (Valheim ~1.0.15 / current refs)

- Double-simulation if a client still believes it owns the creature.
- CPU spikes if many enemies near many peers (mitigated by create cap + interval).
- `CreateObject` is private — signature drift across patches breaks reflection (falls back to ownership-only).
- Fighting other networking mods (FGN, BetterNetworking, full SSS) if co-installed.
- Does **not** load zones/terrain; distant spawn systems may still be client-only.

## Deploy

Build locally; **parent deploys to GPortal only after Nate approves bounce.** This PoC ships in the DLL only — no auto-deploy from this change.


## 0.1.10 discovery capture

Smoke on 0.1.9 proved ownership works (`enemyMai>0`, `updateAIHits` climbing, sceneDump shows Skeleton+MonsterAI) but `registry=0` / `monsterAI=0` / `squads=0`.

Fixes:
- `MonsterAIRegistry` stores by instance ID; **OnDisable no longer unregisters** (OnDestroy only).
- `EnemyOwnershipDirector.SnapshotLiveMonsterAIs()` feeds `EnumerateMonsterAIs` as primary.
- Early harvest of `BaseAI.GetAllInstances` / `BaseAIInstances` / `Instances` (same list MonoUpdaters uses).
- Loud SquadDiscovery logs when UpdateAI hits but candidates stay 0.

Success: `monsterAI`/`candidates`/`squads`/`orders` non-zero for ~8 skeletons near a peer (Roman Hold/ProtectMissiles).


## 0.1.11 hold-line steering

Live 0.1.10: capture worked (`monsterAI=8` / `squads=1` / `orders=[Hold]` / `registry=8`) but Nate still saw vanilla bum-rush — postfix `StopMoving` alone does not override chase.

Fixes:
- `BaseAI.MoveTo` **Prefix**: HoldGround skips MoveTo; line Front / PreferKeepRange redirects `point` → formation slot.
- `MonsterAI.UpdateAI` postfix: after StopMoving/CallMoveTo, **SuppressVanillaChase** — Traverse clear `m_targetCreature` / `m_targetStatic`, set `m_lastKnownTargetPos` to slot; `SetHuntPlayer(false)` only if hunting; **no** `SetAlerted(false)`.
- RomanDoctrine: missiles + threat in wall band → prefer **ProtectMissiles** (not eternal Hold); Charge still rare.
- OrderApplicator: Front Hold/ProtectMissiles → `HoldGround=true`, `DesiredPosition` = threat-facing formation slot; missiles PreferKeepRange (no HoldGround).
- SquadDiscovery: suppress false `updateAIHits>0 but EnumerateMonsterAIs=0` when registry/ownership/peak already has MAs.

Success: same spawn shows orders progressing (Hold/ProtectMissiles/FocusFire) and Nate feels a line instead of a bum-rush.
