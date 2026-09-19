# Territory + Clans — one-pager (design lock)

**Status:** design only. Do **not** implement until Faction Tactics enemy AI smoke-test lands.  
**Folds into:** Faction Tactics (Assault frontier + safe zone). Separate plugin OK if it stays a thin Territory layer FT reads.

## Problem
Players want a **hostile world** with **earned safe ground**: build a fief, hold the wall, defend it. Carpet-warding an entire farm is busywork. Biome paint alone is blotchy and hands out free safe zones.

## Pitch
**Enclose-to-claim.** A closed perimeter of **player walls** plus **vanilla wards** as dual-purpose claim anchors flood-fills the interior into a **claimed fief**. Map draws the border. Inside = soft safe zone vs faction Assault; the **wall line** is where doctrines press.

## Claim recipe
1. Build a continuous **wall loop** (gap ≤ configurable tolerance).
2. Place **vanilla wards** on/near that perimeter (same ward prefab; vanilla protection unchanged).
3. If the loop closes and min area is met → **flood-fill** interior cells/polygon → claimed.
4. Breaks, missing anchors, or destroyed segments → shrink or void until repaired.

**Not required:** wards inside the farm, biome ownership, custom claim totems.

## Wards (locked)
Existing **ward** pieces serve **both**:
- Vanilla ward protection / ACLs
- Claim **anchors** for territory math

No second totem prefab.

## Clans (multiplayer, thin v1)
- **Clan** = named roster of SteamIDs (one clan per player in v1).
- Member wards on a loop count as anchors for **that clan’s** fief.
- Shared safe zone for members; non-members get no safe-zone benefit.
- Map border tint/label per clan.
- **Defer:** clan wars, claim flips, multi-clan mixed walls (v1: majority or founder clan if mixed).

## Faction Tactics hooks
| Zone | Behavior |
|------|----------|
| **Interior (claimed)** | Soften or skip Assault; optional probe-only at gates |
| **Wall / frontier** | Primary Assault press (breachers + missile cover we already have) |
| **Unclaimed / broken claim** | Open country — full doctrine pressure |
| **Biome** | Bias *which* faction contests (Greys in BF, Draugr in Swamp) — **not** the claim shape |

**Invariant unchanged:** behavior-only; no extra spawns / RandomEvents / encounter-rate bumps.

## Map
- Draw claimed polygon (or coarse cell outline) on the minimap / map.
- Color by clan; optional name label.
- Update when wall/ward set changes (throttled rebuild).

## Guardrails (config)
- Max segment gap, min enclosed area, max claim radius/area cap
- Anchor must be within N meters of wall polyline (no center-ward infinity claim)
- Rebuild interval / dirty flags for perf
- Master enable + “debug draw borders”

## Non-goals (v1)
- Siege stairs / mob-built structures
- Player army / companion formations
- Smoke bombs / warhorns (Presence layer — later)
- Tax, diplomacy UI, Discord-synced clans (nice later)

## Suggested build order (after AI test)
1. Detect wall loops + ward anchors → claim polygon (server)
2. Persist claims; map overlay (client or shared ZDO)
3. Clan roster create/invite/leave
4. Wire `SiegeDirector` / Assault soften from “point in claim?”
5. Playtest fief defense fantasy

## Open knobs (decide in playtest)
- Soften vs full skip inside claim
- How many wards per loop minimum
- What counts as “wall” (wood/stone/stake only? doors/gates?)
- Offline decay of broken claims

## One-line summary
**Wall + wards enclose a fief; clans co-own it; Faction Tactics assaults the curtain, not the cabbage patch.**
