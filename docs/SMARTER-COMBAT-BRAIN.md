# Smarter combat brain (post-pipeline)

**Status:** outline only (2026-09-19). Pipeline proven on 1.0.4: server commander → peer RPC → client Drive. Do not rebuild transport; rebuild **decision + actuation**.

**Live symptoms that motivate this:** Hold pins + attack every tick → swing spam in place; formation rebuild from shrinking roster → equal-spacing grid that “goes quiet” but stays alerted; slow first detect on large spawns.

---

## 1. Separation of concerns

| Layer | Owns | Must not |
|-------|------|----------|
| **Commander** (server) | Squad membership, doctrine FSM, order/formation/stance, assault flags | Per-frame swing timing, local physics |
| **Executor** (owning client) | MoveTo / StopMoving / LookAt / gated DoAttack, local target sense | Re-picking doctrine orders |
| **Transport** (RPC) | Intent batches by ZDOID | Behavior policy |

Intents stay coarse (order + slot + flags). Fine motor stays on the client.

---

## 2. Actuation fixes (quick wins → 1.0.5-class)

1. **Hold / ProtectMissiles attack gate**
   - Face threat every tick.
   - `DoAttack` only when: have target, in weapon range band, cooldown elapsed, LOS/not mid-recovery.
   - No swing spam while `HoldGround`.

2. **Formation stability**
   - Lock slot indices for living members until order changes or reshuffle timer (~2–4s) or casualty ratio crosses threshold.
   - Don’t rebuild full equal-spacing lattice every tick as roster shrinks mid-fight.

3. **Charge / Advance hygiene**
   - Charge: run to threat, attack in range, short duration then doctrine re-evaluate.
   - Advance: move to slots first; only then Hold-like discipline.

4. **Discovery latency**
   - Faster first pack: optional burst tick on spawn/near-player ZDO spike; keep steady tick at 0.75s after.

---

## 3. Smarter commander (doctrine brain)

### 3.1 Shared threat model
Per squad, each tick (or 2–3 Hz):
- Primary threat (nearest hostile player / focus)
- Range bands: contact / mid / far
- Casualties + broken
- Friendly missile presence
- Terrain hints already partially there (indoors, near structure)

### 3.2 Order machine (replace brittle if-ladders)
- Explicit states with min dwell time (anti-flicker).
- Transitions scored, not only first-match.
- Roman example: Hold / ProtectMissiles default → Advance to close gap → FocusFire if missiles → Charge only last resort.
- Ambush: Orb/Kite/Flank primary; Charge flash then back to Kite.
- Death-Rush: always close; never Hold grid.

### 3.3 Role clarity
- Front/Leader: wall slots, limited attack.
- Missile: keep-range, PreferKeepRange, rarer swings.
- Flanker: lateral slots, not centroid lattice.

---

## 4. Smarter executor (client Drive)

```
sense local target (owner UpdateTarget)
if HoldGround:
  look at threat/slot
  if CanSwing(intent, target, cd): DoAttack
  else StopMoving
elif need slot:
  MoveTo(slot); look threat
elif AllowClose:
  MoveTo(threat); CanSwing → DoAttack
```

- Per-member cooldown table (instance id → nextSwingTime).
- Stagger squad swings slightly (slot index * 50–100ms) so the wall doesn’t animate as one blender.

---

## 5. Perception upgrades (later)

- Server: still ZDO-only discovery; improve prefab→doctrine and near-player radius.
- Client: trust RPC; ignore stale intents (`stale` counter today); request resync RPC if cache empty but alerted (optional).
- Debug: `ft status` already shows rpcRecv/rpcDrives — add `swings` / `holdBlocks` counters.

---

## 6. Phased delivery

| Phase | Ship | Success |
|-------|------|---------|
| **A** | Hold attack throttle + face threat; slot lock | No blender spam; wall holds without grid teleport every death |
| **B** | Order dwell + Roman/Ambush transition scores | Fewer Charge/Hold flips; greys kite instead of stand-swing |
| **C** | Role-staggered swings + missile keep-range | Readable formations in video |
| **D** | Utility hooks / route-2 prep | Scores pluggable without rewriting RPC |

---

## 7. Non-goals

- No return to sticky server ownership as default.
- No client-less design.
- No raising spawn/raid rates.
- No full SSS.

---

## 8. Knobs (expose via `ft set` when implementing)

- `HoldAttackCooldown` / `HoldAttackRangeFactor`
- `FormationReshuffleSeconds`
- `OrderMinDwellSeconds`
- `SwingStaggerMs`
- Existing doctrine enables + radii stay.

---

*Pipeline locked. Brain next. Implement phase A first when tokens allow.*
