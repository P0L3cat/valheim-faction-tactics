# Faction Tactics — RTS POV Roadmap (BIG strides)

**Date:** 2026-09-22 (America/Chicago)  
**Owners:** Nate (playtest / promote), Groknir (plate / deploy)  
**Status:** Nate locked mountain order — Pack-as-Unit first, then Theater Commander.  
**Ship rule:** Cooks commit + test + Release DLLs. Parent plates GH / Thunderstore / GPortal. No cook-side deploy.

---

## North star

Next play feels like a classic RTS **from one unit's POV**. Packs act as units. PreferRun is the default gait. Ambush orbits the player. Romans press as a line. No mindless wanderers. Multi-squad contact reads as a battlefield, not a bag of aggro AI.

---

## Global invariants (every version)

| Invariant | Rule |
|-----------|------|
| Behavior-only | No spawn/raid-rate hikes; difficulty = smarter packs, not more bodies. |
| PreferRun | `PreferRun = !HoldGround` all doctrines. Run whenever not planted. |
| HoldGround | Rare / banded only (1.0.7 cadence). Advance never pins via near-slot alone. |
| Ownership | Sticky / server enemy ownership **default OFF**. Hybrid commander → peer `FT_MemberIntents` → owning-client CombatDriver. |
| Transport | No ZDO primary path. No IntentRpc schema rewrite unless compile-forced. |
| Scope | Enemy behavior v1. No territory / settlements / LLM brains. |
| Ship | Cooks do not GH-release / Thunderstore / GPortal. |

---

## Mountain order (Nate lock 2026-09-22)

1. **Pack-as-Unit** (1.0.9) — whole squad = one body  
2. **Theater Commander** (1.0.11+) — multi-squad battle awareness  
3. **Doctrine Spectacle** — readable identity plays  
4. **Pressure Economy** — threat that breathes with the player  
5. **Living War** (1.0.13 / possible 1.1.0 rename by Nate) — integration polish

Do **not** rename to 1.1.0 without Nate.

---

## Version ladder

### 1.0.8 — Foundation (SHIPPED)

PreferRun default, straggler merge (`SquadMergeRadius` 40), Ambush sticky player orbit + hysteresis (`AmbushAnchorHysteresis` 10).

**Playtest:** Romans run into FormUp; scattered skeletons join the wall; Greydwarfs orbit **you**.

### 1.0.9 — Pack-as-Unit (HUGE stride) ← SHIPPED

North star: every squad moves as **ONE BODY**.

- Shared facing toward threat / sticky player for the whole formation while advancing.
- FormUp magnet: members far from slot PreferRun hard into slot; absolute on FormUp/Advance/Hold-approach.
- Synchronized close: Roman/Viking line keeps **relative slot geometry** while PreferRun advancing (shared anchor shift); no peel-off wanderers.
- Reattach dropouts within `SquadMergeRadius` same tick; never clear mid-fight members to vanilla if still near pack / discovery.
- Ambush ring stays coherent around sticky player (static ring OK — rotating tempo is a later mountain).

**Acceptance:** A 20–25 skeleton spawn forms one readable wall that jogs into slots together. Greydwarf pack orbits you as a ring-unit, not a huddle.

### 1.0.10 — Ambush sticky dwell + Narvi suite ← this cook

Sustained hysteresis breach for Ambush sticky player switching (`AmbushStickySwitchDwellSeconds` default 1.0s). Single-tick spikes do not steal sticky; invalid/dead/OOR still switches immediately. Narvi player-path + Pack-as-Unit edge tests merged green.

**Note:** Theater Commander cook was still in flight on Bazzite at ship time — promoted to 1.0.11 so sticky dwell could land without blocking.

### 1.0.11 — Theater Commander

Multi-squad awareness: don't all freeze/huddle the same; assign complementary jobs (pin / flank / reserve); readable order dwell; no global brain dump.

### 1.0.12 — Doctrine Spectacle

Each doctrine's "set piece" is unmistakable: Roman line press, Ambush orbit tempo (phase angle advances), Viking choke mirror, DeathRush bee-line. Facing / keep-distance polish so formations *read*.

### 1.0.13 — Pressure Economy

Contact pressure that feels like an RTS engagement: Approach→Press as one unit; Ambush kite→re-encircle without huddle; breathe with player positioning; no spawn hikes.

### 1.0.14 — Living War (ship candidate)

Integration polish, regress tests for 8–13, knob defaults tuned for RTS POV, changelog/README ready for possible Nate rename to 1.1.0 (cook leaves version at 1.0.14).

---

## Out of scope until Nate says otherwise

Theater implementation before Pack-as-Unit commits. Spawn/rate levers. Sticky ownership default ON. LLM/inference. Settlements / territory v2.
