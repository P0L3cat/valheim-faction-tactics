# PROOF 1.0.11 — In-game observable contracts (red-team packet)

**Version under test:** git `3589619` / product **1.0.11 Theater**  
**Branch that carries this packet:** `ft-narvi-proof`  
**Audience:** Nate (eyes in Valheim) + any GitHub reviewer who wants to red-team the claims.

---

## 0. Build / binary under test (CRITICAL)

### Thunderstore 1.0.11 is poisoned

Do **not** install or trust Thunderstore `FactionTactics` / `FactionTactics.Client` labeled 1.0.11 for this Proof. That channel was known poisoned relative to git `3589619`. A green Thunderstore plate does **not** prove these contracts.

**Allowed binaries only:**

1. GitHub release artifacts built from (or containing) tip **`3589619`** (or a later commit that is a fast-forward of this Proof branch and still claims product `1.0.11` with the same Theater math), **or**
2. GPortal FTP / dedicated `BepInEx/plugins` DLLs that you can hash-match to that release / commit (server `FactionTactics.dll` + client `FactionTactics.Client.dll`).

**Before any spawn:**

```text
ft status
```

Expect a line with `product=1.0.11` (and/or load log `Faction Tactics 1.0.11`). If version is wrong or missing → **STOP**. Wrong binary = every claim below is void.

This Proof packet does **not** plate, deploy, or bounce. It is paper + offline tests + a walk Nate can run after *someone else* plates the correct DLLs.

---

## 1. Honest scope

| Layer | What it proves | What it does **not** prove |
|-------|----------------|----------------------------|
| Offline xUnit (`InGameObservableContractTests` + friends) | Intent flags, sticky id/dwell math, FormUp magnet DesiredPosition, Theater role assignment, PreferRun = !HoldGround on intents | Valheim rendering, Sprint/Walk animation, client locomotion ownership, dedicated ZDO fights, actual camera feel |
| Heartbeat / `ft status` / BepInEx log | Discovery alive, order histograms, `theater: pin=… flank=… harass=…`, per-squad `theater=Pin` tags | That mobs *look* like a wall/orbit to a human |
| In-game spawn + watch | Camera: advance sprint into slots, Hold plant, Ambush orbit YOU, skeleton Pin front vs greydwarf Harass/Flank kite, Deathrush Charge | That logs alone are enough (they are not) |

**In-game Proof = spawn + watch + heartbeat/`ft status` lines.** Offline green without eyes is not a ship gate for “what Nate will see.”

---

## 2. What offline CANNOT prove (separate, explicit)

Reviewers: treat anything in this section as **live-only**. Do not accept an offline green as substitute.

1. **Pixels / animation.** PreferRun is a bool on `MemberIntent`. Offline asserts the flag and DesiredPosition motion under `PlayerPathSim` stepping. It cannot see Valheim Sprint vs Walk clips or ragdoll.
2. **Client vs dedicated ownership.** If the admin client still drives `MonsterAI.UpdateAI` / position, Nate can see vanilla bum-rush while server heartbeat shows perfect `orders=[Advance=…] theater=pin=1`. That failure mode is documented historically (`docs/VANILLA-FEEL-0.2.1-HYPOTHESIS.md`, `docs/DEDICATED-DISCOVERY-RESEARCH.md`) and is **out of scope** for these offline contracts.
3. **Prefab→doctrine routing in the live world.** Offline uses `FakeSnapshots.MakeSquad(..., "Skeleton"|"Greydwarf"|"Greyling")` with a forced doctrine. Live Proof depends on `DoctrinePackRegistry.ResolveByPrefab` + Enable\* knobs. Wrong prefab family or disabled doctrine → false “spawn 8 did nothing.”
4. **Terrain / navmesh / collision.** Slot math is flat `Vector3`. Cliffs, trees, and pathfinding can break visual FormUp even when DesiredPosition is correct.
5. **Multiplayer clock / tick skip.** Sticky dwell uses real `dt`. Heavy lag or paused dedicated can stretch “&lt;1s run-past” into a real switch. Use wall-clock eyes, not just tick counts.
6. **Thunderstore / wrong DLL.** See §0. Offline tests always run against the repo tip; they never certify what is installed on GPortal.

If a claim cannot be proven offline, the matching test either asserts the math that *is* offline-provable **or** fails with an Assert message that names the **live Proof step** below. We do **not** fake green.

---

## 3. Claims table (summary)

| # | Claim | Offline proof (test name) | What you should see | How to verify |
|---|--------|---------------------------|---------------------|---------------|
| 1 | PreferRun = !HoldGround — advancing packs sprint into slots; planted Hold stands | `Observable_PreferRun_equals_not_HoldGround_Advance_runs_Hold_plants` (PlayerPathSim multi-tick, PreferRunViolations==0, Desired leaves spawn) + sibling `PreferRun_true_when_not_holding` / `PreferRun_mirrors_not_HoldGround` | Advance: members run into lattice; StandoffHold near slot: Front/Leader plant | Eyes + intents math offline; live: no freeze-in-blob on spawn Advance |
| 2 | Ambush sticky + sustained dwell — Greydwarfs orbit YOU; brief 2nd player closer doesn’t flip; sustained closer does | `Observable_Ambush_orbit_tracks_walking_player_slots_stay_near_player` (MeanDesiredToSticky band + angular variance) + `Observable_sticky_ignores_subsecond_hysteresis_spike` (3 ticks @ dt=0.25 zero flaps; sustained → one switch) + sibling `Sticky_hysteresis_under_zigzag_zero_flaps_then_one_switch` / `Sticky_requires_sustained_hysteresis_breach_not_single_spike` | Orbit tracks your walk; &lt;1s run-past of ally doesn’t re-anchor | Spawn greydwarfs; walk; have friend sprint past &lt;1s; then stand closer ≥1s |
| 3 | Pack-as-Unit FormUp magnet — straggler greys/skeletons PreferRun hard back into formation when you kite | `Observable_straggler_runs_PreferRun_into_lattice_when_player_kites` (Desired→centroid distance) + siblings `Hold_order_magnets_far_member_with_PreferRun`, `Ambush_Flank_magnets_far_member_toward_slot`, `FormUp_magnet_snaps_far_member_to_slot_with_PreferRun`, `Zero_magnet_distance_leaves_far_member_unsnapped`, `Charge_is_excluded_from_FormUp_magnet` | Far member sprints toward pack slots, not forever solo chasing you | Kite; watch straggler close diameter |
| 4 | Theater Pin/Flank/Harass — skeletons hold front (Pin), greys kite/orbit (Harass/Flank); Deathrush always charges | `Observable_Roman_and_Ambush_theater_assigns_Pin_and_Harass_when_coengaged` + `Observable_DeathRush_stays_Charge_while_theater_assigns_others` + siblings `Solo_pack_and_split_focus_get_no_role`, `Outside_coengage_radius_does_not_join`, `Hysteresis_holds_pin_against_a_closer_arrival_until_dwell`, `TwoRomans_closer_pins_farther_flanks` | Mixed spawn: line in front, greys off-axis/orbit, greylings bee-line | `spawn skeleton 8` + `spawn greydwarf 8` (+ optional greyling); `ft status` theater counts |
| 5 | Sticky dwell — run past another player for &lt;1s shouldn’t re-anchor Ambush | `Observable_sticky_ignores_subsecond_hysteresis_spike` + `Sticky_requires_sustained_hysteresis_breach_not_single_spike` (explicit dt=0.25; interrupted dwell; mid-dwell candidate reset) | Orbit stays on original sticky through sub-second spike | Two players; brief pass; sticky id / orbit focus unchanged |

**Azog R1 + R2 closed** on `ft-narvi-proof` (harden-only: Observable Facts + sibling PackAsUnit/Version108/109/Theater/sticky tests; no production doctrine changes).

Defaults that gate live feel: `AmbushAnchorHysteresis=10`, `AmbushStickySwitchDwellSeconds=1.0`, `FormUpMagnetDistance=3.5`, `TheaterRoleDwellSeconds=2.5`, `TheaterCoEngageRadius=48`.

---

## 4. Per-claim red-team sheets

### Claim 1 — PreferRun = !HoldGround

**Threat model / how it can lie**

- Heartbeat shows `orders=[Advance=1]` while every Front has `HoldGround=true` (historical 0.2.1 “label without look”).
- Offline only checks intent bools; a poisoned client DLL can ignore PreferRun and still walk/chase vanilla.
- Reviewer greens a single-tick Apply without multi-tick stepping → misses “never leaves spawn blob.”

**Offline assert**

- `Observable_PreferRun_equals_not_HoldGround_Advance_runs_Hold_plants` in `InGameObservableContractTests.cs` — multi-tick PlayerPathSim Advance; PreferRunViolations==0; Desired leaves spawn pile; Hold plant is Front/Leader when AssignedRole available.
- Sibling: `PreferRun_true_when_not_holding` (Version108, multi-tick Desired leaves spawn) / `PreferRun_mirrors_not_HoldGround`.

**Expected camera / behavior**

- After `spawn skeleton 8` near you with Roman enabled: pack **runs** into a line / standoff, does **not** freeze as a spawn clump.
- When doctrine reaches StandoffHold / ContactHold and Fronts are on slot: those Front/Leader **stand** (HoldGround); PreferRun false only then.
- Ambush / Death-Rush: should not plant (PreferRun stays true while moving).

**Spawn / console**

```text
spawn skeleton 8
ft status
```

**Log / heartbeat fragments**

```text
FactionTactics heartbeat: … orders=[Advance=1] formations=[ShieldWall=1] …
# or later Hold during standoff:
… orders=[Hold=1] formations=[ShieldWall=1] …
[ft] theater: pin=0 flank=0 harass=0   # solo pack — no co-engage theater job required for this claim
```

Per-squad director line may show `[Roman] … → Advance (ShieldWall/…)` then later `→ Hold`.

**Pass**

- Eyes: Advance members clearly relocate into formation (sprint/run), not StopMoving in the spawn pile.
- Offline: every intent has `PreferRun == !HoldGround`; Advance intents PreferRun true / HoldGround false; planted Hold near slot PreferRun false / HoldGround true for Front/Leader.

**Fail**

- Spawn blob freezes immediately on Hold with no form-up motion.
- Offline PreferRunViolations &gt; 0.

**Known false greens**

- `orders=[Hold=1]` alone (0.2.1 lied).
- Offline green + Thunderstore poisoned client.
- Watching rear missiles (they never HoldGround by design) and concluding “nobody plants.”

**Live Proof step if offline insufficient:** after plate of GH/`3589619` DLLs, spawn skeletons, film 10s of Advance form-up, then wait for standoff plant.

---

### Claim 2 — Ambush sticky orbit + sustained dwell

**Threat model / how it can lie**

- Sticky id flaps every tick between two players → orbit looks epileptic; logs may still show `orders=[Flank=1]`.
- Single hysteresis breach without dwell steals sticky on a shoulder-check (pre-1.0.10 class bug).
- Orbit DesiredPositions lag old centroid while sticky walks away (“glued behind”).

**Offline asserts**

- `Observable_Ambush_orbit_tracks_walking_player_slots_stay_near_player` — MeanDesiredToSticky band + angular variance of Desired around sticky
- `Observable_sticky_ignores_subsecond_hysteresis_spike` — CountStickyFlaps==0 for 3 ticks @ dt=0.25; sustained ≥dwell → exactly one switch
- Sibling: `Sticky_hysteresis_under_zigzag_zero_flaps_then_one_switch`

**Expected camera / behavior**

- `spawn greydwarf 8` (or greydwarf pack): they **orbit YOU** (Orb/Flank/Kite pocket), slots follow as you walk.
- Second player briefly closer (&lt; `AmbushStickySwitchDwellSeconds`, default 1s), even if hysteresis+ meters closer: orbit **stays on you**.
- Second player stays hysteresis+ closer for ≥1s continuous: sticky **may** switch once; then stable.

**Spawn / console**

```text
spawn greydwarf 8
ft status
# two-player: friend runs through your pack &lt;1s then leaves
# then friend stands clearly closer than you for ≥2s
```

**Log / heartbeat fragments**

```text
… orders=[Flank=1] formations=[Orb=1] …
# or Kite/Skirmish when harassing in pocket
… orders=[Kite=1] formations=[Skirmish=1] …
[ft] orders: [ambush:Flank=1]   # example ft status shape
```

Sticky id is not always printed on heartbeat; judge by **orbit focus** (eyes) + offline sticky id history. Director lines: `[Ambush] … → Flank (Orb/…)`.

**Pass**

- Offline: sticky id constant on single walking player; mean Desired→sticky within band (~20m); subsecond spike: zero sticky flaps; sustained closer: exactly one switch then stable.
- Live: greys circle the sticky player; brief pass does not flip the cloud mid-orbit.

**Fail**

- Orbit glued to spawn point while you walk 40m+.
- Orbit snaps to passer on a &lt;1s brush.

**Known false greens**

- One-player-only test that never stresses dwell.
- Counting order name `Flank` without watching whether slots track the player.
- Friend never actually hysteresis+ closer (still outside 10m advantage) — dwell never armed; “didn’t flip” is vacuous.

**Live Proof step:** Meadows/Black Forest, two clients, timed shoulder-check with a stopwatch.

---

### Claim 3 — Pack-as-Unit FormUp magnet (straggler PreferRun into lattice)

**Threat model / how it can lie**

- Straggler PreferRuns toward **player** forever (vanilla chase) while heartbeat still says Advance/Flank.
- Magnet disabled (`FormUpMagnetDistance=0`) or overridden by chase distractors → diameter never shrinks.
- Merge/discovery splits straggler into a 1-mob “squad” that never receives FormUp.

**Offline assert**

- `Observable_straggler_runs_PreferRun_into_lattice_when_player_kites` — Desired distance to pack centroid (not soft Desired.x)
- Siblings: `PackAsUnitEdgeTests.Hold_order_magnets_far_member_with_PreferRun`, `Ambush_Flank_magnets_far_member_toward_slot`, `FormUp_magnet_snaps_far_member_to_slot_with_PreferRun`; Charge excluded via `Charge_is_excluded_from_FormUp_magnet`

**Expected camera / behavior**

- Pack with one member left far behind; you kite sideways/away: straggler **sprints back into the formation lattice**, PreferRun true, HoldGround false while snapping.
- Pack diameter shrinks or far member’s Desired snaps near centroid/slots within multi-tick window.

**Spawn / console**

```text
spawn skeleton 8
# or green: spawn greydwarf 8
# pull one / kite so one trails 20–40m, then keep moving
ft get FormUpMagnetDistance   # expect 3.5 unless you changed it
```

**Log / heartbeat fragments**

```text
… orders=[Advance=1] …   # roman
… orders=[Flank=1] …     # ambush
# no specific “magnet=” counter on heartbeat — eyes + offline
```

**Pass**

- Offline: far member PreferRun true, HoldGround false, Desired moves toward lattice; late gap to centroid ≪ early gap.
- Live: obvious “catch up to the wall/orbit,” not a permanent solo.

**Fail**

- Straggler ignores pack and only chase-kites the player.
- Offline Desired stays at world X≈40 while slots are near 0.

**Known false greens**

- Asserting PreferRun true without checking Desired is the **slot**, not the threat point.
- Tiny magnet distance in cfg on live server while tests force 3.5f.

**Live Proof step:** kite skeletons until one is obviously detached; watch re-form.

---

### Claim 4 — Theater Pin / Flank / Harass (+ Deathrush exempt)

**Threat model / how it can lie**

- Two packs co-engage but Theater disabled / radius too small → both frontal bum-rush; logs show two Advances and `theater: pin=0 flank=0 harass=0`.
- Death-Rush incorrectly takes Pin and steals the line job from Romans.
- Solo pack gets a theater role (should be None) — reviewer thinks Pin is “always on.”
- Order histogram alone: `Advance` + `Kite` without `theater=` tags — could be doctrine coincidence, not TheaterCommander.

**Offline asserts**

- `Observable_Roman_and_Ambush_theater_assigns_Pin_and_Harass_when_coengaged`
- `Observable_DeathRush_stays_Charge_while_theater_assigns_others`
- Theater siblings (cite by name): `Solo_pack_and_split_focus_get_no_role`, `Outside_coengage_radius_does_not_join`, `Hysteresis_holds_pin_against_a_closer_arrival_until_dwell` (multi-tick Assign(0.5)×N age), `TwoRomans_closer_pins_farther_flanks`

**Expected camera / behavior**

- Near you: `spawn skeleton 8` **and** `spawn greydwarf 8` (same fight, within ~48m).
  - Skeletons (Roman): **Pin** — hold/advance the front axis toward you.
  - Greydwarfs (Ambush): **Harass** (closer skirmish) or **Flank** — kite/orbit off-axis, not a second shield wall on your nose.
- Optional `spawn greyling 8` (Death-Rush): **always Charge** bee-line; `TheaterRole.None` (never Pin).

**Spawn / console**

```text
spawn skeleton 8
spawn greydwarf 8
# optional:
spawn greyling 8
ft status
```

**Log / heartbeat fragments**

```text
[ft] theater: pin=1 flank=0 harass=1
# or pin=1 flank=1 harass=0 depending on distances / third pack
[ft] orders: [ambush:Kite=1, roman:Advance=1]   # illustrative
# director / verbose lines:
[Roman] … → Advance (…) theater=Pin
[Ambush] … → Kite (…) theater=Harass
[Death Rush] … → Charge (…)          # no theater= tag
FactionTactics heartbeat: … orders=[Advance=1,Kite=1] …   # histogram may omit theater; use ft status
```

**Pass**

- Offline multi-tick: Roman `TheaterRole.Pin`, Ambush `Harass` (or Flank when farther), Death-Rush `None` + order Charge every tick while co-engaged.
- Live: front line vs orbit/kite split is obvious on camera; greylings never “hold wall.”

**Fail**

- Both packs stack on the same frontal Hold.
- Greylings plant or take Pin.
- `ft status` stays `theater: pin=0 flank=0 harass=0` while two packs are clearly on you inside 48m (with Enable\* on).

**Known false greens**

- Spawning only skeletons (no second doctrine) — Theater correctly assigns **no** role; that is not Pin.
- Packs farther than `TheaterCoEngageRadius` (default 48).
- Reading heartbeat `orders=` without `ft status` theater line.

**Live Proof step:** co-spawn skeleton+greydwarf on GH/`3589619` plate; screenshot `ft status` theater line + camera.

---

### Claim 5 — Sticky dwell: &lt;1s run-past must not re-anchor

**Threat model / how it can lie**

- Hysteresis alone without dwell: one physics tick with friend 15m closer steals sticky permanently.
- Test uses dt≥dwell so “one tick” accidentally satisfies dwell → false confidence.
- Live: friend loops around you continuously → dwell accumulates → legitimate switch misread as “flap.”

**Offline assert**

- `Observable_sticky_ignores_subsecond_hysteresis_spike`
- `Sticky_requires_sustained_hysteresis_breach_not_single_spike` — explicit `deltaTime=0.25`; interrupted dwell clears candidate; mid-dwell candidate change resets accumulation

**Expected camera / behavior**

- Ambush orbit locked on player A.
- Player B runs past (hysteresis+ closer) for **&lt;1s** wall-clock then leaves: orbit **stays on A**.
- If B remains closer for ≥1s: one clean switch to B (not claim-5 failure).

**Spawn / console**

```text
spawn greydwarf 8
# A = sticky host; B = runner with stopwatch
ft get AmbushStickySwitchDwellSeconds   # expect 1
ft get AmbushAnchorHysteresis           # expect 10
```

**Log / heartbeat fragments**

Same Ambush Flank/Kite lines as claim 2. No dedicated sticky-id field on stock heartbeat — **eyes + offline**. Optional future log: sticky keeps/reclaims counters exist for enemy ownership, not Ambush player sticky.

**Pass**

- Offline with `dt=0.25`, spike lasting &lt;1s total: `CountStickyFlaps == 0`, sticky id remains A; after sustained ≥1s closer: one flap to B.
- Live stopwatch: &lt;1s pass does not flip orbit.

**Fail**

- Orbit jumps to B on a brush.
- Offline sticky id changes on the spike ticks.

**Known false greens**

- Spike that is not actually hysteresis+ closer (e.g. only 5m closer with hysteresis 10).
- Using `deltaTime=null` in a one-shot Update that applies full tick interval as dwell credit incorrectly in a custom harness (production sim passes explicit dt).

**Live Proof step:** two-player stopwatch brush; do not accept “we think it was under a second.”

---

## 5. Concrete walk (Nate, after correct plate)

1. Confirm binary: `ft status` → `product=1.0.11` (GH release / GPortal FTP only — **not Thunderstore**).
2. Claim 1: `spawn skeleton 8` → watch form-up run, then plant on standoff.
3. Claim 3: kite until one straggler; watch PreferRun rejoin.
4. Claim 2+5: `spawn greydwarf 8`; walk; friend &lt;1s pass; then sustained closer.
5. Claim 4: `spawn skeleton 8` + `spawn greydwarf 8` (+ optional `spawn greyling 8`); `ft status` → `theater: pin=… harass=…`; eyes confirm roles.
6. Optional log pull: `ft-smoke.py` (ops helper) — latest heartbeat must show non-zero `squads` / `monsterAI` / `candidates` and orders that match the walk; **theater line comes from `ft status`**, not always from the compact heartbeat regex.

### `ft-smoke.py` pass/fail (ops)

- **Pass (capture):** `squads>0`, `monsterAI>0`, `candidates>0`, orders not empty/`none`.
- **Not sufficient alone:** capture OK does not prove PreferRun, sticky dwell, or Theater camera split.
- **Fail:** no heartbeat / `squads=0` after spawn → discovery/ownership problem; stop and fix plate before judging doctrine claims.

---

## 6. Offline gate

```bash
export PATH=/home/box/.dotnet:$PATH
cd /workspace/faction-tactics
dotnet test tests/FactionTactics.Tests/FactionTactics.Tests.csproj -c Release
```

Focus class: `FactionTactics.Tests.InGameObservableContractTests`.  
Any Assert message that names a **live Proof step** is an intentional “cannot fake green” marker — treat as docs debt / live gate, not as a silent skip.

---

## 7. Reviewer cheat sheet (how to red-team this PR)

1. Diff this doc against `InGameObservableContractTests.cs` — every claim row must name a real `[Fact]`.
2. Run Release tests; zero failures.
3. Confirm no production doctrine changes except test-only hooks if compile-required (this branch should be docs+tests).
4. Reject any “Proof” that cites Thunderstore 1.0.11.
5. Reject heartbeat-only screenshots without camera notes for claims 1–5.
6. Ask: does any test assert Valheim animation? If yes, it is lying — offline cannot.

---

## 8. Traceability

| Item | Value |
|------|-------|
| Git tip this packet targets | `3589619` |
| Product | `1.0.11` (`FtVersion.ProductVersion` / `Plugin.PluginVersion`) |
| Feature | Theater Commander Pin/Flank/Harass; sticky dwell; FormUp magnet; PreferRun=!HoldGround |
| Offline entry | `tests/FactionTactics.Tests/InGameObservableContractTests.cs` |
| Sim harness | `tests/FactionTactics.Tests/Sim/PlayerPathSim.cs` |
| Live knobs | `AmbushAnchorHysteresis`, `AmbushStickySwitchDwellSeconds`, `FormUpMagnetDistance`, `TheaterRoleDwellSeconds`, `TheaterCoEngageRadius` |
