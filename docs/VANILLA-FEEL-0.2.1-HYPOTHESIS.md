# Why 0.2.1 logged ShieldWall but felt vanilla (2026-09-19)

## Live evidence (GPortal, no bounce)

After `spawn skeleton 8` under **0.2.1**:

- Heartbeat: `discovered=1 active=1 squads=1 orders=[Hold=1] formations=[ShieldWall=1]`
- Discovery: `doctrines=[roman:8]` then pack bled `8→6→4→2→1` while order stayed **Hold**
- Ambush pack later: `orders=[Flank=1] formations=[Orb=1]` — same “label without look”
- Nate: “feels normal. vanilla… Didn't see a single shield wall”

So the **director and doctrine FSM worked**. The **visual line never formed**.

## Primary hypothesis (smoking gun in code)

**Hold set `HoldGround=true` immediately**, so `DriveControlledAI` called `StopMoving()` and never walked members to ShieldWall slots.

| Step | What happens |
|------|----------------|
| OrderApplicator (0.2.1) | For Front on `Hold` / `ProtectMissiles`: **always** `holdGround = true` (also re-forced in `frontHoldSlot` block) |
| DriveControlledAI | `if (intent.HoldGround) { ai.StopMoving(); … return; }` — **no `MoveTo(slot)`** |
| Spawn blob | Skeletons spawn clustered → freeze in a clump → look like “not a wall” / vanilla huddle |

Comment at `OrderApplicator` already said *“HoldGround when within FrontHoldSlotDist of slot; else MoveTo slot”* — the code did the opposite for Hold.

**Fix (0.2.2, committed offline):** `holdGround = distToSlot <= FrontHoldSlotDist` for Hold/ProtectMissiles/Roman Advance/FocusFire Front. Far from slot → `DesiredPosition = slot`, `HoldGround = false` → `MoveTo` forms the line, then pin.

## Secondary hypothesis (still open)

Vanilla dedicated servers are **client-authoritative for creature AI** in the local zone (`docs/DEDICATED-DISCOVERY-RESEARCH.md`). Admin client may not run FactionTactics. Server sole-brain + ZDO ownership can fight client vanilla `UpdateAI`. Even after form-up works on the server, if the client still drives locomotion, Nate sees vanilla rush.

**Prove later (no bounce now):**

1. After 0.2.2 bounce: if a clear wall appears → primary was enough.
2. If still vanilla → install FT on admin client *or* harden server ZDO position authority / SSS-class ownership so client cannot overwrite FT MoveTo.

## Not the failure mode this time

- Not “squads=0” (that was 0.2.0; fixed in 0.2.1).
- Not “wrong doctrine” (roman:8 / Hold / ShieldWall were correct in logs).
