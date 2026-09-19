# Combat authority 0.2 — replace, do not augment

**Lock (Nate, 2026-09-19):** stop backpacking on vanilla. Two instruction sets is unacceptable. Faction Tactics is the **only** brain for squad members that carry a `MemberIntent`.

## Rule

| Actor | When | Authority |
|-------|------|-----------|
| **FT (doctrine → OrderApplicator → Harmony)** | `OrderApplicator` has a live `MemberIntent` for that MonsterAI | **Sole** mover/looker/attacker |
| **Vanilla `MonsterAI.UpdateAI`** | No intent (below `MinSquadSize`, unmatched prefab, intent expired) | Full vanilla |
| **Ownership / discovery** | Always | Unchanged (`EnemyOwnershipDirector`, `SquadDiscovery`, registry) |

There is no “postfix nudge after vanilla chase.” If intent exists, vanilla UpdateAI does not run.

## Harmony design

### `MonsterAI.UpdateAI` — Prefix (0.2.0)

```
Prefix(MonsterAI ai, float dt):
  hitCount++
  registry.Register(ai)
  if !TryResolveIntent(ai) → return true   // vanilla brain
  DriveControlledAI(ai, intent, dt)        // FT brain
  return false                             // skip vanilla entirely
```

`DriveControlledAI`:

1. **Sense** — call private `UpdateTarget(Humanoid, dt, out hear, out see)` so target acquisition still works without running the rest of vanilla UpdateAI.
2. **Steer** — `StopMoving` / Traverse `MoveTo` / `LookAt` from intent (`HoldGround`, formation slot, kite/keep-range, charge close).
3. **Attack** — when a combat target exists and the order is not pure withdraw, call private `DoAttack(Character, isFriend: false)` (SelectBestAttack stays inside DoAttack’s vanilla helper path as invoked by DoAttack itself, or FT calls SelectBestAttack + CanUseAttack when needed).

No `SuppressVanillaChase` target-nulling on the controlled path: that was a 0.1.11 fight-after-vanilla hack. Clearing `m_targetCreature` after postfix prevented chase **and** starved attacks. Under Prefix skip, keep the target for `DoAttack`.

### `BaseAI.MoveTo` — Prefix (retained)

Belt-and-suspenders if anything else calls `MoveTo` on an intent-owned AI: HoldGround skips; line Front / PreferKeepRange rewrite `point` → `DesiredPosition`. FT’s own `CallMoveTo` is compatible (already passes the slot/threat point).

### Unchanged

- Registry: OnEnable / Awake / AddInstance / UpdateAI re-register; OnDestroy unregister; **no** OnDisable clear.
- `EnemyOwnershipDirector` PoC.
- Squad discovery / clustering / doctrine FSM / Siege Assault.

## Intent field semantics (0.2)

| Field | Meaning under Prefix authority |
|-------|--------------------------------|
| `HoldGround` | FT `StopMoving` + face threat/slot |
| `DesiredPosition` | FT `MoveTo` destination (slot or threat) |
| `PreferKeepRange` | Stay on slot / kite band; look at threat |
| `AllowVanillaChase` | **Misnomer retained:** FT may close on threat (Charge / hot assault). Does **not** mean “run vanilla UpdateAI.” |
| `PreferRun` | Passed to `MoveTo` run flag |

## Doctrine primacy (unchanged contracts, tighter gates)

- **Roman + `RomanPreferRanged`:** Hold / ProtectMissiles / FocusFire primacy. `ShouldCharge` returns true **only** on last-resort casualties — never default Charge when PreferRanged (even if missiles are gone).
- **Ambush:** Kite / Flank / Orb primacy; Charge only via strict flash (`TargetIsolated` / stagger / rare aged-Flank envelope). After Charge → Kite.

## Config

- `Squad.DiscoveryRadius` default **64** (docs previously advertised 40; bind default restored/confirmed at 64+).

## Success signal

Dedicated smoke with peers + skeletons/greys: orders show doctrine-correct kinds (not eternal Charge), members hold/orbit instead of bum-rush, and they still **attack** (Prefix skip must not leave statues). Log line names actual doctrines, e.g. `doctrines=[roman:8]` — never a hard-coded “Roman/doctrine match OK.”

## Version

Plugin **0.2.0**. Do not deploy while Ungrull is online (no bounce).
