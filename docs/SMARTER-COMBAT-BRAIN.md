# Smarter combat brain — whole-hog design

**Status:** Phase A shipped in **1.0.5** (2026-09-19). Phase B shipped in **1.0.6** (2026-09-22): order min-dwell, Roman + Ambush scored transitions, Charge re-eval. **1.0.7** makes HoldGround rare and replaces the Roman/Viking eternal wall with a standoff → press → contact cadence. Design-doc Phase C/D (role stagger, pluggable utilities) are not in this build.  
**Pipeline:** proven on **1.0.4** — server commander → peer-targeted `FT_MemberIntents` RPC → owning-client `CombatDriver.Drive`.  
**Scope of this doc:** decision + actuation design. **Do not** rebuild transport. **Do not** implement or deploy from this document alone.

**Live symptoms that motivate this rewrite**

| Symptom | Layer | One-line cause |
|---------|-------|----------------|
| Hold swing-spam (“blender”) | Executor | `HoldGround` → `StopMoving` + `TryDriveAttack` every tick with no swing gate |
| Formation lattice reshuffle | Commander / applicator | Slot lattice rebuilt from shrinking roster → equal-spacing grid teleports; fight “goes quiet” but stays alerted |
| Slow first detect on large spawns | Discovery | Steady tick (~0.75s) only; no burst on spawn / near-player ZDO spike |

---

## A. Design principles

### A.1 Three layers (locked)

| Layer | Owns | Must not |
|-------|------|----------|
| **Commander** (server DLL) | Squad membership, doctrine FSM, `DoctrineOrderKind` / `FormationType` / `StanceType`, assault flags, threat snapshot, slot assignment (locked) | Per-frame swing timing, local physics, hit registration |
| **Executor** (owning client / listen-host) | `MoveTo` / `StopMoving` / `LookAt` / **gated** `DoAttack`, local `UpdateTarget` sense | Re-picking doctrine orders or formations |
| **Transport** (RPC primary; ZDO optional/debug) | Intent batches keyed by packed ZDOID; stale counters | Behavior policy |

Intents stay **coarse** (order + formation + stance + role + slot + a few gates). Fine motor (arrive distance, face, swing cooldown, stagger) stays on the owner.

### A.2 Intent contract (unchanged shape, smarter meaning)

Today’s `MemberIntent` gates the executor already understands:

- `HoldGround` — stop + face; attack only if gated (today: ungated → blender)
- `DesiredPosition` — formation slot or orbit point
- `AllowVanillaChase` — close on live combat target (Charge / hot Breach)
- `PreferKeepRange` — rear / skirmish; look at threat; rare swings
- `PreferRun`, `OrderKind`, `Formation`, `Role`, assault bits

**Principle:** Commander emits *which* commandable action is in force; Executor decides *when* a single swing or step happens.

### A.3 Live bugs to fix (must land before fancy FSMs feel good)

1. **Hold swing-spam** — Face every tick; `DoAttack` only when target exists, in weapon range band, cooldown elapsed, not mid-recovery / mid-anim. Same gate for `ProtectMissiles` Front.
2. **Formation lattice reshuffle** — Lock slot indices for living members until order changes, reshuffle timer (~2–4s), or casualty-ratio threshold. Do not rebuild full equal-spacing lattice every death.
3. **Slow first detect** — Optional burst discovery tick on spawn / near-player ZDO spike; keep steady tick after first pack.

### A.4 Non-goals

- No return to sticky server ownership as default.
- No client-less design / full SSS.
- No raising spawn or raid rates.
- No transport rewrite (RPC path stays).

---

## B. Command vocabulary (shared)

Atomic **commandable actions** the executor can run. They map onto today’s `DoctrineOrderKind` + `FormationType` + `StanceType` plus a few **flags / dwell annotations** the applicator already almost has (or Phase B adds as intent bits / order metadata — not new RPCs).

### B.1 Existing enums (ground truth)

**`DoctrineOrderKind`:** `Hold`, `Advance`, `Charge`, `Flank`, `FocusFire`, `ProtectMissiles`, `RetreatAndReform`, `Kite`  

**`FormationType`:** `Loose`, `Line`, `ShieldWall`, `Wedge`, `Skirmish`, `Orb`  

**`StanceType`:** `Passive`, `Defensive`, `Aggressive`, `Fleeing`

Siege Assault v1 already aliases onto the same enum (`Encircle→Flank`, `TestBreach→Charge/Advance`, `FocusWallman→FocusFire/ProtectMissiles`, `Withdraw→RetreatAndReform/Kite`). Keep that mapping.

### B.2 Commandable actions

Each row: **purpose** · **MoveTo / Stop / Look / Attack gates** · **default formation**.

| Action | Maps primarily to | Purpose | MoveTo | Stop | Look | Attack | Formation |
|--------|-------------------|---------|--------|------|------|--------|-----------|
| **FormUp** | `Hold`/`Advance` + not yet at slot | Reach locked slots before discipline | Yes → slot | On arrive | Threat or forward | Off / rare | Line / ShieldWall |
| **HoldFacing** | `Hold` | Pin feet; present wall | No | Yes | Threat every tick | **Gated** melee/missile | ShieldWall / Line |
| **AdvanceLine** | `Advance` | Close gap as a line into wall band | Yes → slot creeping toward threat | Near slot | Threat | Off until band | ShieldWall / Line |
| **ThrowVolley** (soft) | `FocusFire` | Soften at mid range; no commit | Slot / rear | Prefer | Threat | Missile gated; Front rare | ShieldWall + rear Missile |
| **PressContact** | `ProtectMissiles` | Front pins contact band; missiles keep range | Front: slot; Missile: rear | Front yes | Threat | Front gated; Missile PreferKeepRange | ShieldWall |
| **ChargeWedge** | `Charge` + `Wedge` | Shock commit along a point | Threat / apex | No | Threat | In range, gated | Wedge |
| **RefuseFlank** | `Flank` / `Hold` + refuse flag | Refuse one wing; refuse lateral slots | Lateral refuse slots | On arrive | Threat | Gated | Line / ShieldWall skewed |
| **Orbit / Skirmish** | `Flank` + `Orb`/`Skirmish` | Circle envelope; never HoldGround | Orbit tangents | No | Threat | Soft / rare | Orb / Skirmish |
| **KiteBand** | `Kite` | Peel to comfort band | Away to band | No | Threat | Missile soft; melee rare | Skirmish / Loose |
| **FeignPull** | `Kite` then `Flank`/`Charge` | Fake break → re-engage | Back then cut-in | No | Threat | Flash only on return | Skirmish → Wedge |
| **Encircle** | `Flank` + `Orb` | Close the ring | Outer → inner orbit | No | Threat | Soft until isolate | Orb |
| **FlashCharge** | short `Charge` | Brief contact, then doctrine re-eval | Threat | No | Threat | In range | Loose / Wedge |
| **Peel** | `RetreatAndReform` / `Kite` | Break contact, reform | Away / reform point | Brief | Away / threat | Off | Loose |
| **Breach** | `Charge`/`Advance` + assault hot | Hot push on structure / gap | Breach point / player | No | Target | Hot AllowVanillaChase | Loose / Wedge |
| **CoverBreachers** | `ProtectMissiles` / `FocusFire` | Cover roles while others breach | Cover slots | Prefer | Threat / wall | Soft | Skirmish rear |
| **DeathRushClose** | `Charge` always | Bee-line close; never Hold grid | Threat | Never while threat | Threat | Aggressive gated | Loose |
| **FocusIsolate** | `FocusFire` / `Charge` on isolate | Collapse on cut-off player | Isolate target | No | Target | Commit | Skirmish / Loose |
| **StandOffZone** | `FocusFire`/`Hold` + PreferKeepRange | Artillery comfort annulus | Keep 10–22m band | Prefer | Threat | Zone denial | Loose / Skirmish |
| **PressureWave** | `Advance`→`Flank`→`Charge` | Insect multi-role pressure | Role slots | Role-dependent | Threat | Role-gated | Loose / Orb |

**New flags (optional, Phase B)** — still coarse intents, no transport change:

- `AttackPolicy`: `None` | `GatedMelee` | `GatedMissile` | `Aggressive` (Death-Rush)
- `SlotLockEpoch` / reshuffle token
- `ActionDwellSeconds` (commander-side; executor just obeys while intent live)
- `RefuseWing` (−1 / +1) for RefuseFlank

### B.3 Executor actuation sketch (all doctrines share)

```
sense local target (owner UpdateTarget)
if HoldGround:                    # HoldFacing / PressContact Front
  LookAt(threat or slot forward)
  if CanSwing(intent, target, cd, stagger): DoAttack
  else StopMoving
elif PreferKeepRange:             # ThrowVolley / StandOff / Cover / Orbit missiles
  MoveTo(slot or band point)
  LookAt(threat)
  if CanSwing(... missile policy): DoAttack
elif AllowVanillaChase:           # ChargeWedge / FlashCharge / Breach / DeathRushClose
  MoveTo(threat)
  if CanSwing(...): DoAttack
elif need slot (FormUp / AdvanceLine):
  MoveTo(slot); LookAt(threat)
else:
  MoveTo(DesiredPosition); gated attack per policy
```

- Per-member cooldown table: `instanceId → nextSwingTime`
- Squad stagger: `slotIndex * SwingStaggerMs` so walls don’t animate as one blender

---

## C. Per-doctrine high-level tactics (historical → Valheim)

Range bands (shared unless doctrine overrides):

- **Far:** `> AdvanceRange` / doctrine outer (~18–40m)
- **Mid:** wall / harassment band (~8–18m)
- **Contact:** `≤ ChargeRange` / inner (~3–8m)

Assault: **quiet** (near workbench, light-touch Advance) vs **hot** (committed Breach).

---

### C.1 Roman (`roman` — Skeleton*)

#### 1. Historical inspiration
Roman manipular / cohort advance: **triplex acies** depth idea (front holds, rear supports), **pila** soften before contact, **scutum wall** presents a continuous face. **Testudo** is a *missile-defense march formation*, not a melee blender — do not model it as “everyone swing forever while stopped.” Discipline > individual heroics; Charge is a last resort when the missile option collapses.

#### 2. Theory of victory in Valheim
Against 1–3 players with no cavalry: win by **forcing the player to eat arrows while facing a pinned Front line**. Close only into the **14–18m wall band**, then **PressContact + ThrowVolley**. Never blob-Charge as default. If missiles die and casualties spike, rare ChargeWedge or Peel/reform.

#### 3. Maneuver catalog
| Maneuver | Sequence of actions |
|----------|---------------------|
| **Wall approach** | FormUp → AdvanceLine → HoldFacing |
| **Pila posture** | ThrowVolley (+ Front HoldFacing) |
| **Missile screen** | PressContact (Front) + ThrowVolley (Missile) |
| **Last-resort shock** | ChargeWedge (short dwell) → HoldFacing or Peel |
| **Break & reform** | Peel → FormUp → AdvanceLine |

#### 4. Conditions / logic flow
```
if threatCount == 0: HoldFacing / FormUp
if broken OR casualtyRatio >= 0.45: Peel (RetreatAndReform); dwell >= OrderMinDwell
if d > WallOuter (18): AdvanceLine
if hasMissiles OR PreferRanged:
  if ShouldCharge(lastResort): ChargeWedge; dwell ~2–4s then re-eval
  elif d <= WallOuter AND (missileThreatened OR d <= WallInner OR prev in Protect/Focus):
    PressContact   # ProtectMissiles
  else: ThrowVolley  # FocusFire
else:  # no missiles
  if d > WallInner (14): AdvanceLine
  elif ShouldCharge: ChargeWedge
  else: HoldFacing
# hysteresis: if prev==Charge and d<=ChargeCommitBand(5) and still eligible → stay ChargeWedge
```
Dwells: `OrderMinDwellSeconds` on every transition; ChargeWedge hard max ~4s.

#### 5. Anti-patterns
- **Blender Hold:** HoldFacing without swing gate (live bug).
- **Eternal Hold grid** while missiles could fire — prefer PressContact/ThrowVolley inside wall band.
- **Early Charge** with missiles alive — forbidden unless last-resort casualties.
- **Lattice reshuffle** after each archer death → Front teleports; looks like the wall “gave up.”

---

### C.2 Ambush (`ambush` — Greydwarf*, not Root/Greyling)

#### 1. Historical inspiration
Guerrilla / woodland predator pattern: **encircle-harass**, refuse decisive melee, strike the isolated, then vanish. Closer to partisan ambush and wolf-pack anxiety than a shield wall. Mass frontal Charge is the failure mode.

#### 2. Theory of victory in Valheim
Win by **orbit + kite anxiety** against 1–3 players in Black Forest: keep the player turning, poke with shamans, flash only on isolate/stagger. After any Charge, **immediately KiteBand** then re-Encircle. Never stand in a Hold lattice and blender.

#### 3. Maneuver catalog
| Maneuver | Sequence |
|----------|----------|
| **Lurk pocket** | FormUp (loose) / KiteBand far |
| **Encircle** | Orbit/Skirmish → Encircle |
| **Harassment loop** | Encircle → FlashCharge → KiteBand → Encircle |
| **Anxiety break** | Peel / KiteBand when casualties ≥ AnxietyCasualties |
| **Isolate collapse** | FocusIsolate / FlashCharge only if isolate flag |

#### 4. Conditions / logic flow
```
outer = AmbushOuterPocket (~18); inner = AmbushInnerBand (~8); reGap = AmbushReEncircleGap (~14)
if threatCount == 0: HoldFacing(loose) / lurk
if casualtyRatio >= AnxietyCasualties (0.22): KiteBand or Hold lurk; dwell
if d > outer: KiteBand / Hold lurk
if d in (inner, outer]: Encircle (Flank + Orb); min Flank age before flash = MinFlankAgeForFlash (3s)
if isolate OR (flankAge >= 3 AND d <= EnvelopeFlashRange(5) AND casualties < 0.12):
  FlashCharge; then FORCE KiteBand next eval
if d < inner and pressed: KiteBand
after Charge: always KiteBand; if gap > reGap → Encircle again
# never: immediate Charge from Flank without isolate
```

#### 5. Anti-patterns
- Blob bum-rush Charge from first contact.
- HoldGround on flankers (applicator already avoids; keep it).
- Post-kill **grid reshuffle** that collapses Orb into a quiet equal-spacing clump still “alerted.”
- Charge↔Hold flicker without dwell.

---

### C.3 VikingShieldWall (`viking-shieldwall` — Draugr*)

#### 1. Historical inspiration
**Skjaldborg** (shield-fort): locked shields, depth, archers behind. Shock from a **svinfylking / wedge** when the wall commits. Norse preference for **ambush and sudden charge** once the line is set — not Mongol kiting. Crypt/choke = hold the door, don’t parade in the open.

#### 2. Theory of victory in Valheim
Win by **presenting a readable Draugr wall**, soft arrows from rear, then **ChargeWedge** into contact once inside charge band. Indoors/crypt: prefer choke HoldFacing over open AdvanceLine. After Charge, **Peel/reform** beat (avoid Charge↔Advance ping-pong).

#### 3. Maneuver catalog
| Maneuver | Sequence |
|----------|----------|
| **Skjaldborg form** | FormUp → HoldFacing (ShieldWall) |
| **March to contact** | AdvanceLine → HoldFacing |
| **Archer support** | ThrowVolley behind wall |
| **Wedge shock** | ChargeWedge → Peel (RetreatAndReform) → FormUp |
| **Choke hold** | HoldFacing (indoors bias) |
| **Siege (cross-cut)** | CoverBreachers / Breach when Assault hot |

#### 4. Conditions / logic flow
```
if threatCount == 0: FormUp / HoldFacing
if broken / high casualties: Peel
if IndoorsOrCrypt: prefer HoldFacing at choke; AdvanceLine only if d >> mid
if d > AdvanceRange: AdvanceLine
if hasMissiles and d > ChargeRange: ThrowVolley or PressContact
if d <= ChargeRange: ChargeWedge
if prev == Charge: Peel/reform one beat before Advance/Hold again
dwell: Charge min ~2s max ~5s; reform dwell >= OrderMinDwell
```

#### 5. Anti-patterns
- Blender Hold in the wall (same executor bug).
- Charge every tick without reform beat → ping-pong.
- Reshuffling ShieldWall slots mid-fight after each kill → “quiet grid.”
- Open-field Advance parade inside crypts.

---

### C.4 Steppe (`steppe` — Fuling* / Goblin*; ex-Mongol)

#### 1. Historical inspiration
Mongol / steppe horse-archer doctrine: **waves**, **caracole-like pass**, **feigned retreat**, then **encircle** and collapse on the cut-off. Berserkers only when the prey is isolated. Village/totem defense tightens the orbit (not a Roman wall).

#### 2. Theory of victory in Valheim
No real cavalry — simulate with **mobile Flanker slots + PreferKeepRange missiles**. Win by **KiteBand when pressed**, volley at comfort, Encircle on open field, FocusIsolate / FlashCharge only on cut-offs. Near structure/totem: tighter defense orbit, less deep feign.

#### 3. Maneuver catalog
| Maneuver | Sequence |
|----------|----------|
| **Caracole pass** | Orbit/Skirmish → ThrowVolley → KiteBand |
| **Feigned retreat** | FeignPull (KiteBand → FlashCharge/Encircle) |
| **Wave encircle** | Encircle → FocusIsolate |
| **Village defense** | Orbit tight → ThrowVolley → Peel if broken |
| **Berserker commit** | FlashCharge / ChargeWedge only on isolate |

#### 4. Conditions / logic flow
```
if threatCount == 0: FormUp loose / Hold
if broken: Peel
if nearStructureOrTotem: tighter Orbit; prefer ThrowVolley; KiteBand if d <= contact
if open field and d <= contact (pressed): KiteBand
if mid band: Orbit / Encircle + ThrowVolley
if FlankOpportunity OR isolate: FocusIsolate / FlashCharge then KiteBand
if prev == Charge: force KiteBand or Encircle (no sticky Charge)
never default ChargeWedge as Roman last-wall — Steppe Charge is opportunistic only
```

#### 5. Anti-patterns
- Standing Hold grid when pressed (should Kite).
- Berserker blob Charge with full squad.
- Feign that never returns (endless Kite with no re-engage).
- Lattice rebuild that kills orbit tangents.

---

### C.5 PackHunters (`pack-hunters` — Wolf*, Drake*/Hatchling)

#### 1. Historical inspiration
Pack hunters: **encircle, hamstring, isolate**, then collapse. Aerial/overwatch analogue (Drake) punishes clumps — not a shield wall. Patience until the prey separates.

#### 2. Theory of victory in Valheim
Wolves **Encircle / Orbit**; Drake **StandOffZone / ThrowVolley** overwatch. Win by **FocusIsolate** when a player peels, then Charge/Flash on that target. Avoid frontal blender against a tight trio — wait for separation.

#### 3. Maneuver catalog
| Maneuver | Sequence |
|----------|----------|
| **Pack orbit** | Encircle / Orbit |
| **Overwatch** | StandOffZone (Drake Missile) |
| **Hamstring** | Flank pressure → FocusIsolate |
| **Collapse** | FlashCharge / ChargeWedge on isolate |
| **Break** | Peel if pack broken |

#### 4. Conditions / logic flow
```
if threatCount == 0: loose FormUp
if broken: Peel
if mid/far: Encircle (wolves) + ThrowVolley/StandOff (drake)
if FlankOpportunity or isolate at mid/contact: FocusIsolate then FlashCharge
if d <= ChargeRange and isolate: ChargeWedge
else if pressed without isolate: keep Orbit / KiteBand (wolves) — do not Hold grid
```

#### 5. Anti-patterns
- Whole pack ChargeWedge on a clumped trio with no isolate.
- Drake diving into melee (should PreferKeepRange).
- Post-kill equal-spacing lattice that destroys pack geometry.

---

### C.6 ArtilleryJelly (`artillery-jelly` — Blob*)

#### 1. Historical inspiration
Stand-off artillery / zone denial: keep the enemy in the killing ground, never chase into your own fire. Closer to siege engines and chemical denial than infantry.

#### 2. Theory of victory in Valheim
Win by **comfort annulus (~10–22m)** poison/zone pressure. **Never Charge**. If pressed inside comfortMin → **KiteBand**. If broken → Peel. All members are Missile-role; no Front Hold lattice.

#### 3. Maneuver catalog
| Maneuver | Sequence |
|----------|----------|
| **Stand-off** | StandOffZone / ThrowVolley |
| **Pressed** | KiteBand to restore annulus |
| **Break** | Peel (RetreatAndReform) |
| **Idle** | HoldFacing soft (no blender — attack gated / rare) |

#### 4. Conditions / logic flow
```
comfortMin=10; comfortMax=22
if threatCount == 0: Hold soft
if broken: Peel
if d < comfortMin: KiteBand
if d > comfortMax: AdvanceLine / MoveTo band (gentle close) then StandOffZone
else: FocusFire StandOffZone
NEVER Charge / DeathRushClose / HoldGround blender
```

#### 5. Anti-patterns
- Melee chase into the player (Charge).
- HoldGround swing spam at 5m.
- Equal-spacing “wall” formation — stay Loose/Skirmish.

---

### C.7 InsectSiege (`insect-siege` — Tick/Seeker/Gjall family per pack)

#### 1. Historical inspiration
Insect / swarm siege pressure: soften with ranged/bombard, probe flanks, then **pressure waves**. Soften near fortified analogues (Dvergr) — reduce commit, don’t suicide the wave.

#### 2. Theory of victory in Valheim
**Gjall ThrowVolley** softens; **Seekers/Ticks Orbit/Encircle**; **soldiers AdvanceLine → Charge**. Near Dvergr soften range: Hold/Kite instead of full commit. Win by layered pressure, not one blender Hold.

#### 3. Maneuver catalog
| Maneuver | Sequence |
|----------|----------|
| **Soften** | ThrowVolley (Gjall) |
| **Envelope** | Orbit / Flank (Seekers) |
| **Pressure wave** | AdvanceLine → ChargeWedge |
| **Dvergr soften** | Hold / KiteBand / FocusFire (reduced commit) |
| **Break** | Peel |

#### 4. Conditions / logic flow
```
if threatCount == 0: Hold
if broken / high casualties: Peel
if near Dvergr (DvergrSoftenRange):
  if d <= ChargeRange: KiteBand else if hasGjall: ThrowVolley else Hold
else:
  if d > AdvanceRange: AdvanceLine (soldiers)
  if hasGjall and d > ChargeRange: ThrowVolley; optionally Flank after Focus dwell
  if hasFlankers and d > ChargeRange*0.7 and prev != Charge: Orbit/Flank
  if d <= ChargeRange: ChargeWedge if soldiers OR prev==Flank else keep Flank
```

#### 5. Anti-patterns
- Full Charge into Dvergr killboxes.
- All roles HoldGround in one lattice.
- FocusFire forever with no envelope (Gjall-only stare).

---

### C.8 CharredLegion (`charred-legion` — Charred* + Asksvin flankers)

#### 1. Historical inspiration
Charred “legion”: dense ranks advance under rear fire; **cavalry/Asksvin** work the flanks (Valheim’s stand-in for horse). Triplex-like: ranks Hold/Advance, casters Focus, flankers refuse/encircle, then Charge ranks. Not Death-Rush fanaticism.

#### 2. Theory of victory in Valheim
Win with **readable rank Advance**, rear FocusFire, Asksvin **RefuseFlank / Orbit**, then **ChargeWedge** from ranks. After Charge, **reform beat** (mirror Viking) to kill ping-pong. Asksvin never force Skirmish-only squad order when ranks want Charge — FlankOpportunity opens a Flank beat alongside.

#### 3. Maneuver catalog
| Maneuver | Sequence |
|----------|----------|
| **Dense approach** | FormUp → AdvanceLine |
| **Caster support** | ThrowVolley / PressContact |
| **Cavalry beat** | RefuseFlank / Orbit (Asksvin) |
| **Rank shock** | ChargeWedge → Peel/reform → Advance/Hold |
| **Break** | Peel |

#### 4. Conditions / logic flow
```
if threatCount == 0: HoldFacing
if broken: Peel
if prev == Charge: RetreatAndReform one beat
if prev == RetreatAndReform: Advance if d > ChargeRange else Hold
if d > AdvanceRange: AdvanceLine
if hasCasters and d > ChargeRange: ProtectMissiles or FocusFire
if hasCavalry and FlankOpportunity and prev != Flank and d mid: Flank beat
if hasRanks and d <= ChargeRange: ChargeWedge
elif hasCavalry and d <= ChargeRange: Flank
else: AdvanceLine
```

#### 5. Anti-patterns
- Asksvin HoldGround in the rank lattice.
- Charge↔Advance ping-pong without reform.
- Ignoring FlankOpportunity and face-tanking only.

---

### C.9 DeathRush (`death-rush` — Greyling only)

#### 1. Historical inspiration
Fanatic close assault / “death or glory” rush — historical analogues in desperate infantry charges and religious-fanatic tropes, but **player-facing name stays Death-Rush** (no religious wording). No kite, no shield wall, no clever orbit.

#### 2. Theory of victory in Valheim
Meadows Greylings **win only by closing**. Theory: overwhelm with continuous contact pressure. Idle lurk only with **no threat**. Any threat → **DeathRushClose** forever (including “broken”). Optional scream SFX on alert.

#### 3. Maneuver catalog
| Maneuver | Sequence |
|----------|----------|
| **Idle** | HoldFacing soft (no threat only) |
| **Rush** | DeathRushClose (Charge + AllowVanillaChase) |
| — | *No* KiteBand, Peel, Encircle, PressContact |

#### 4. Conditions / logic flow
```
if threatCount == 0: HoldFacing (loose); no blender needed if no target
else: DeathRushClose ALWAYS
# never: Kite, RetreatAndReform, PreferKeepRange, HoldGround while threat exists
# attack still gated by cooldown/stagger so rush isn’t a visual blender soup
```

#### 5. Anti-patterns
- Hold lattice / FormUp grid while players are visible (“quiet alerted clump”).
- Accidental Kite/Retreat from shared FSM helpers.
- Treating Greydwarf Ambush logic as Greyling (prefab exclude must stay).

---

### C.10 Cross-cutting: Siege Assault (role-split)

Applies when `EnableSiegeAssault` and Ambush/Viking (config) near workbench — **no extra spawns**.

| Role | Quiet assault | Hot assault |
|------|---------------|-------------|
| **Breachers** (Front/Leader) | AdvanceLine light-touch; low AllowVanillaChase | Breach (Charge/Advance + chase players, not just structures) |
| **Cover** (Missile) | CoverBreachers / ThrowVolley PreferKeepRange | Same; never HoldGround |
| **Envelope** (Flanker, Ambush) | Encircle / Orbit on approaches | FlashCharge on peelers; KiteBand if hot fails |
| **Withdraw** | Peel / KiteBand | RetreatAndReform; re-FormUp off trigger |

**Logic sketch:**
```
if nearWorkbench and siegeEnabled and squadSize >= SiegeMinSquadSize:
  if assaultHot: Breachers=Breach; Cover=CoverBreachers; Envelope=Encircle/FlashCharge
  else: light AdvanceLine / FocusFire; leave structure AI alone when quiet
Roman near workbench: do NOT enter Assault (existing test invariant)
```

Anti-patterns: quiet squads AllowVanillaChase into walls; missiles HoldGround; Ambush abandoning orbit for wall lattice during siege.

---

## D. Global perception + FSM

### D.1 Shared threat snapshot (per squad, ~2–3 Hz or tick)

| Field | Use |
|-------|-----|
| `NearestThreatDistance` / primary threat id | Range bands |
| `ThreatCount` | Idle vs fight |
| `RangeBand` ∈ {Contact, Mid, Far} | Derived |
| `CasualtyRatio`, `PeakAlive`, `IsBroken` | Peel / last resort |
| `CountByRole(Missile/Front/Flanker/Leader)` | Doctrine branches |
| `MissileThreatened` | ProtectMissiles |
| `FlankOpportunity`, `Isolate` | Ambush / Pack / Steppe flash |
| `IndoorsOrCrypt`, `NearStructure`, `NearDvergr`, `NearWorkbench` | Terrain / siege / soften |
| `AssaultHot` / `AssaultQuiet` | Siege role-split |
| `PreviousOrderKind`, `OrderAgeSeconds` | Hysteresis / dwell |
| `MissilesPresent` | Roman / Viking / Charred |

Discovery still ZDO-only on dedicated; improve prefab→doctrine and near-player radius. Optional **burst tick** on spawn / ZDO spike for first detect.

### D.2 Order machine rules (all doctrines)

1. **Min dwell** — `OrderMinDwellSeconds` (default ~1.0–1.5s) before any order change except: threat lost, broken, or explicit “force” (Ambush post-Charge → Kite).
2. **Scored transitions (Phase B)** — replace brittle first-match ladders with score vectors; pick max score if `score > current + hysteresis`.
3. **Charge hygiene** — Charge/FlashCharge: run to threat, attack in range, **short duration**, then doctrine re-evaluate (except DeathRush).
4. **Advance hygiene** — Move to slots first (FormUp); only then HoldFacing discipline.

### D.3 Formation slot lock

```
on order change OR reshuffleTimer >= FormationReshuffleSeconds OR casualtyDelta >= threshold:
  recompute locked slots for living members
else:
  keep prior slot index per instanceId; leave holes for dead
```
Do **not** renormalize to equal spacing every death mid-fight.

### D.4 Swing cooldown / stagger (executor)

- `HoldAttackCooldown` / weapon-aware cooldown floor
- `HoldAttackRangeFactor` — must be in melee/missile band
- `SwingStaggerMs * slotIndex` (and maybe role bias: Missile +50ms)
- Skip if mid-recovery / no LOS soft-check if cheap
- Debug counters: `swings`, `holdBlocks` alongside existing `rpcRecv` / `rpcDrives`

### D.5 Stale intent / resync (later)

Client trusts RPC; ignore stale intents (existing stale counter). Optional: request resync if cache empty but alerted.

---

## E. Phased implementation A–D

| Phase | Ship | Success criteria |
|-------|------|------------------|
| **A — Actuation** | Hold/ProtectMissiles attack throttle + face threat; formation slot lock; Charge/Advance hygiene; optional discovery burst | No blender spam; wall holds without grid teleport every death; faster first pack on big spawns |
| **B — Scored FSM** | **Shipped 1.0.6.** Order min-dwell; scored transitions for **Roman** + **Ambush**; Charge flash timers | Fewer Charge/Hold flips; greys kite instead of stand-swing; Roman stays missile-line |
| **C — Roles** | Role-staggered swings; missile keep-range polish; flanker lateral slots (not centroid lattice); Siege role-split clarity | Readable formations on video; Asksvin/Ambush flankers stay mobile |
| **D — Utility hooks** | Pluggable score hooks / route-2 prep; doctrine packs register transition utilities without rewriting RPC | New doctrine behaviors without transport changes |

**Implement Phase A first** when tokens allow. Pipeline stays locked.

### Phase B shipped (1.0.6)

- `OrderMinDwellSeconds` (default 1.25) holds the current order before a change. Exceptions: threat lost (`ThreatCount == 0`), broken (`IsBroken`, or casualty ≥ 0.45 leaving into Retreat/Kite), and explicit force (Ambush previous Charge → Kite).
- Roman and Ambush score candidate orders and switch only when `best > current + OrderScoreHysteresis` (default 0.15). Other doctrines keep their FSM. `IActionScorer` is added onto **legal** scores only (vetoed Charge/Kite stay vetoed). `NullScorer` changes nothing.
- Charge/FlashCharge still hard-caps at `ChargeMaxSeconds`, then re-evals: Ambush → Kite, Roman with missiles still up → ProtectMissiles, jelly → FocusFire, others → Peel. **DeathRush never leaves Charge while a threat exists.**
- Ambush flash, only from Flank/FocusFire inside the inner band: `TargetIsolated` OR `ThreatStaggeredOrLow` OR `FlankOpportunity` (one player in the contact band, or players split > `FlankSplitMeters`). Otherwise contact is Kite, not Hold. After every Charge, force Kite (dwell bypass). No blob Charge on first contact.
- `ft set` keys: `OrderMinDwellSeconds`, `OrderScoreHysteresis`, `RomanWallOuter` (18), `RomanWallInner` (14). `ChargeMaxSeconds` was already settable.
- **1.0.7** banded cadence: `RomanStandoffDistance` (20), hold roll `RomanStandoffHoldMin/Max` (1/15), `RomanContactSwingRange` (3.5), `RomanRetreatPauseSeconds` (1). Viking mirrors that on `VikingStandoffDistance` (14) / indoors 12 / hold max 8. `RomanWallOuter/Inner` stay bound but do not pin the wall. Advance never sets HoldGround for standing on a slot.
- Phase C/D (role stagger, flanker slots, pluggable utilities beyond the existing scorer hook) are not in this build.

---

## F. `ft set` knobs list

Existing knobs (keep): doctrine enables, radii, siege, hybrid/RPC, DeathRush scream, Ambush pockets, tick/discovery — see `docs/CONSOLE-KNOBS.md`.

**New / planned knobs** (expose via `ft set` when implementing):

| Key | Phase | Meaning / default sketch |
|-----|-------|--------------------------|
| `HoldAttackCooldown` | A | Seconds between Hold/Protect Front swings (e.g. 0.85) |
| `HoldAttackRangeFactor` | A | Scale of weapon range required to swing on Hold (e.g. 1.0) |
| `FormationReshuffleSeconds` | A | Slot lock lifetime before rebuild (e.g. 3.0) |
| `FormationCasualtyReshuffle` | A | Casualty delta that forces reshuffle (e.g. 0.25) |
| `OrderMinDwellSeconds` | B | Min time on an order before change (e.g. 1.25) |
| `OrderScoreHysteresis` | B | Score margin to flip orders (e.g. 0.15) |
| `ChargeMaxSeconds` | B | Hard cap on Charge/FlashCharge (e.g. 4.0); DeathRush exempt or higher |
| `SwingStaggerMs` | C | Per-slot stagger (e.g. 75) |
| `DiscoveryBurstOnSpawn` | A | Bool; burst tick on pack spike |
| `DiscoveryBurstSeconds` | A | Burst window (e.g. 0.2) then back to `TickIntervalSeconds` |
| `RomanWallOuter` / `RomanWallInner` | B | Optional live overrides (defaults 18 / 14) |
| `AmbushOuterPocket` | exists | Keep |
| `AmbushInnerBand` | exists | Keep |
| `AmbushReEncircleGap` | exists | Keep |

**Debug counters** (status, not necessarily settable): `swings`, `holdBlocks`, `slotLocks`, `orderFlips`, existing `rpcRecv` / `rpcDrives` / `ownerDrives`.

---

## Appendix — mapping cheat sheet

| Fancy action | OrderKind | Formation | Key intent flags |
|--------------|-----------|-----------|------------------|
| FormUp | Hold/Advance | Line/ShieldWall | !HoldGround until arrive |
| HoldFacing | Hold | ShieldWall/Line | HoldGround + gated attack |
| AdvanceLine | Advance | ShieldWall/Line | MoveTo slot |
| ThrowVolley | FocusFire | ShieldWall/Skirmish | PreferKeepRange missiles |
| PressContact | ProtectMissiles | ShieldWall | Front HoldGround; Missile keep range |
| ChargeWedge | Charge | Wedge | AllowVanillaChase |
| Orbit/Skirmish | Flank | Orb/Skirmish | PreferKeepRange; !HoldGround |
| KiteBand | Kite | Skirmish/Loose | PreferKeepRange |
| FeignPull | Kite→Flank/Charge | Skirmish→Wedge | dwell chain |
| Encircle | Flank | Orb | orbit slots |
| FlashCharge | Charge (short) | Loose/Wedge | then force Kite/reform |
| Peel | RetreatAndReform | Loose | !attack |
| Breach | Charge/Advance | Loose/Wedge | Assault hot + chase |
| CoverBreachers | Protect/Focus | Skirmish | PreferKeepRange |
| DeathRushClose | Charge | Loose | AllowVanillaChase; never HoldGround with threat |
| StandOffZone | FocusFire/Hold | Loose | PreferKeepRange; never Charge |

---

*Pipeline locked (1.0.4). Brain next. Design-only — implement Phase A when ready; no deploy from this doc.*

---

## Design lock (Nate review, 2026-09-19 CT)

**Verdict:** Solid. Layers stay clean. **Do not code the fancy FSM until swing gate + slot lock land.**

### What works (keep)
- Commander owns *which* action; owning client owns *when* a swing/step happens (matches 1.0.4 RPC).
- Coarse intents keep the same shape; meaning rides `HoldGround` / `PreferKeepRange` / `AllowVanillaChase`.
- Per-doctrine: theory of victory, maneuvers, if-ladder, anti-patterns.
- Slot lock + dwell + Charge max time target blender Hold and “quiet alerted grid.”
- Phases A–D are shippable; A is small and measurable.

### Phase A must-land (before any scored FSM)
1. **Swing gate** — `DoAttack` only if: target exists, in weapon band, cooldown done, not mid-anim/recovery. Same for ProtectMissiles Front. Per-instance `nextSwingTime` + `slotIndex * SwingStaggerMs`. Without this every HoldFacing doctrine is a blender.
2. **Slot lock** — Keep `instanceId → slotIndex` until order change, `FormationReshuffleSeconds` (~3s), or casualty delta (~0.25). **Leave holes.** Do not rebuild equal-spacing on every death.
3. **Charge / Advance hygiene** — FormUp to slots first. Charge is short, then re-eval (**DeathRush excepted**). After Charge, Viking/Charred get one Peel/reform beat.
4. **Optional discovery burst** — One short tick window on spawn / near-player ZDO spike; then back to 0.75s.

Ship those four; current doctrines will already feel better.

### Risks (do not “fix” by growing ladders)
- Shared Charge/Hold/Protect across Roman/Ambush/Viking/Charred → flip without min dwell + hysteresis. **Phase B** scored transitions; do not grow if-ladders first.
- Flankers must not inherit Front `HoldGround` (applicator invariant; keep in tests).
- Range bands are magic (14/18 Roman, 8/18 Ambush, 10–22 jelly) — expose knobs; playtest one biome at a time.
- Siege quiet must not `AllowVanillaChase` into walls; Roman stays out of Assault — same invariant tests.
- DeathRush: never shared “broken → Peel”; gate Peel on doctrine.

### Doctrine notes (short)
- **Roman:** PressContact + ThrowVolley in wall band = identity; Charge last resort.
- **Ambush:** Force Kite after every FlashCharge; never HoldGround on flankers.
- **Viking / Charred:** Reform beat or Charge↔Advance ping-pong.
- **Steppe / Pack:** Charge opportunistic only; Orbit dies if slot lock fails.
- **Jelly:** Never Charge; never Front lattice; comfort annulus is the brain.
- **Insect:** Dvergr soften path must beat Charge in first-match ladders.
- **DeathRush:** Hard override; protect from shared Peel helpers.

### Open questions — **locked by Nate 2026-09-19** (before Phase B)
| Q | Answer (confirmed) |
|---|-------------------|
| Isolate / FlankOpportunity formula? | Cheap: **one player in contact band**, OR **players split > `FlankSplitMeters` (default 12)** → FlashCharge/FlankOpportunity true. |
| FormUp when slots empty? | **Advance under the hood** (MoveTo slots, no HoldGround) until within `FrontHoldSlotDist`; then HoldFacing. |
| Missile LOS? | **Skip until Phase C.** Owner may face/shoot without LOS gate in A/B. |
| Listen-host vs dedicated swing gate? | **Same `CombatDriver` path** for the gate on every owning peer (client or listen-host). |

### Phase A checklist
- [x] Gate `TryDriveAttack` on Hold / Protect Front (+ cooldown + stagger) — **1.0.5**
- [x] Slot lock + holes (no per-death lattice rebuild) — **1.0.5**
- [x] Charge max seconds + post-Charge Peel on Viking/Charred (not DeathRush) — **1.0.5**
- [x] Debug counters: `swings`, `holdBlocks`, `slotLocks` — **1.0.5**
- [x] Optional discovery burst — **1.0.5**
- [x] Client admin `ft` RPC (`FT_ConfigCmd`) — **1.0.5**
- [ ] Video: Skeleton wall, Greydwarf orbit, Draugr crypt, Greyling rush

**Phase A shipped in 1.0.5** (2026-09-19). **Phase B shipped in 1.0.6** (dwell, hysteresis, Roman + Ambush scores, flank-opportunity flash). Phase C/D not started.
