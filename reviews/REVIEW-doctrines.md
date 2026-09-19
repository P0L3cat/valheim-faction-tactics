# Doctrine pack review — Roman, Ambush, VikingShieldWall, Steppe, InsectSiege, CharredLegion, PackHunters, ArtilleryJelly, TrollFortressHelper, SiegeDirector

**Scope:** `/workspace/faction-tactics/plugin/Doctrine/*`, `/workspace/faction-tactics/plugin/Siege/SiegeDirector.cs`  
**Lenses:** prefab matching breadth, order FSM loops / stuck states, role assignment contradictions, siege quiet vs hot split, missing null checks  
**Mode:** read-only review (this file only write). Do not push.

Severity: **Critical** | **High** | **Medium** | **Low** | **Info**

---

## Summary

| Area | Hottest issues |
|------|----------------|
| Prefab matching | Charred `IsElite("Melee")` over-promotes leaders; Roman `Skeleton*` / PackHunters `Wolf*` are intentionally broad |
| FSM loops | Ambush Flank↔Kite; Steppe Kite↔Flank; Charred Charge↔Advance; ArtilleryJelly Kite↔RetreatAndReform; PackHunters Charge↔Kite |
| Roles | Blob Elite as `Leader` leaves artillery rear formation; Ambush all-swarm squads never get a Leader |
| Siege quiet/hot | Ambush withdraw stuck Kiting at empty workbench; quiet path dead-branch; hot missiles monopolize squad order; applicator forces chase on quiet wall-breakers |
| Null checks | `AssaultStance.SelectOrder` / `EnrichWorkbenchProximity` assume non-null; most `MatchesPrefab` paths are fine |

---

## Prefab matching (too broad / too narrow)

### [Medium] CharredLegion — `IsElite` treats every `*Melee*` / `*Twitcher*` as elite

**File:** `CharredLegionDoctrine.cs` (`IsElite`)

```csharp
Contains(member.PrefabName, "Melee")
|| Contains(member.PrefabName, "Twitcher")
```

Almost every ashlands ranker matches → leadership gate is effectively “first non-missile / non-Asksvin by `InstanceId`,” not true elites. Twitchers as Leader contradict “dense ranks + rear casters.”

**Suggested patch:**

```csharp
public static bool IsElite(SquadMemberView member)
    => Contains(member.PrefabName, "Elite")
       || Contains(member.PrefabName, "Captain")
       || Contains(member.PrefabName, "Lord");
// Prefer LooksLikeLeader from discovery for Twitcher/Melee; do not treat all Melee as elite.
```

---

### [Medium] PackHunters — `IsDrake` falls through on `LooksLikeMissile`

**File:** `PackHuntersDoctrine.cs`

```csharp
=> Contains(..., "Drake") || Contains(..., "Hatchling") || member.LooksLikeMissile;
```

Any wolf that inherits / is scored `LooksLikeMissile` becomes Missile (drake overwatch). Discovery currently clears that for wolves, but Route-2 role scorers can flip it.

**Suggested patch:** drop the `LooksLikeMissile` OR; require prefab token only:

```csharp
public static bool IsDrake(SquadMemberView member)
    => Contains(member.PrefabName, "Drake")
       || Contains(member.PrefabName, "Hatchling");
```

---

### [Low] Ambush — `IsBrute` / `IsShaman` also trust LooksLike* flags

**File:** `AmbushDoctrine.cs`

`IsShaman` → `LooksLikeMissile`; `IsBrute` → `LooksLikeHeavy`. Same scorer-poison risk as PackHunters. Prefab tokens (`Shaman` / `Elite` / `Brute`) should dominate.

**Suggested patch:** prefab-first; use LooksLike* only when prefab is plain `Greydwarf` with no subtype token.

---

### [Low] InsectSiege — `IsSoldier` via `LooksLikeHeavy` can promote Ticks

**File:** `InsectSiegeDoctrine.cs`

```csharp
Contains(PrefabName, "Soldier") || member.LooksLikeHeavy
```

A Tick with `LooksLikeHeavy` becomes Front/Leader. `IsTick` exists but is unused in `AssignRole`.

**Suggested patch:**

```csharp
if (IsTick(member)) { member.LooksLikeFlanker = true; return SquadRole.Flanker; }
// IsSoldier: Soldier token only (optionally Seeker + LooksLikeHeavy), never Tick.
```

---

### [Low] Steppe — `Fuling*` prefix likely never matches vanilla

Vanilla plains goblins are `Goblin*`. `Fuling` is display-name only. Harmless dual prefix; document or drop `Fuling` to avoid false confidence.

---

### [Info] Roman `Skeleton*` / PackHunters `Wolf*` / ArtilleryJelly `Blob*` — broad by design

Acceptable for v1. Watch mod collisions (`Skeleton_*` bosses, `WolfCub`, `BlobTar` as Mistlands sibling — usually still wanted). Ambush correctly excludes `Root` / `Greyling`. Viking `Draugr*` has no exclude for non-combat props (none known in vanilla).

---

### [Info] Registry first-match — no prefix overlap today

`DoctrinePackRegistry.ResolveByPrefab` returns first enabled match. Current prefixes do not collide; keep new packs from sharing prefixes without an exclude list (Ambush pattern).

---

## Order FSM loops / stuck states

### [High] Ambush — Flank ↔ Kite oscillation when target not isolated

**File:** `AmbushDoctrine.cs` `SelectOrder` (~L133–143)

1. Close range, swarm: `previous != Flank && != Charge` → **Flank**  
2. Next tick: `previous == Flank && !TargetIsolated` → **Kite**  
3. Next: `previous == Kite` → Flank again  

Flash Charge path is gated; anxiety peel never settles.

**Suggested patch:** sticky peel or hysteresis:

```csharp
if (previous == DoctrineOrderKind.Flank && !snapshot.TargetIsolated)
    return DoctrineOrderKind.Kite;
if (previous == DoctrineOrderKind.Kite
    && snapshot.NearestThreatDistance <= flashRange
    && !snapshot.TargetIsolated)
    return DoctrineOrderKind.Kite; // hold peel until gap or isolate
```

---

### [High] Steppe — Kite ↔ Flank when `NearestThreatDistance < 8`

**File:** `SteppeDoctrine.cs` (~L102–106)

```csharp
if (previous == Kite)
    return hasSkirmishers ? Flank : RetreatAndReform;
return Kite;
```

Pressed + skirmishers → alternate every tick.

**Suggested patch:** require gap before re-Flank, or 2-tick minimum Kite:

```csharp
if (d < 8f)
{
    if (previous == DoctrineOrderKind.Kite || previous == DoctrineOrderKind.RetreatAndReform)
        return DoctrineOrderKind.Kite; // stay peeled until d >= 8
    return DoctrineOrderKind.Kite;
}
```

---

### [High] CharredLegion — Charge ↔ Advance every tick after commit

**File:** `CharredLegionDoctrine.cs` (~L76–77, L103–104)

```csharp
if (previous == Charge) return Advance;
// ...
if (hasRanks && d <= ChargeRange) return Charge;
```

No reform hold → ping-pong.

**Suggested patch:** mirror Viking — Charge → `RetreatAndReform` (or Hold wall) for one beat; only re-Charge after reform / range reopen:

```csharp
if (previous == DoctrineOrderKind.Charge)
    return DoctrineOrderKind.RetreatAndReform;
if (previous == DoctrineOrderKind.RetreatAndReform)
    return snapshot.NearestThreatDistance > snapshot.ChargeRange
        ? DoctrineOrderKind.Advance
        : DoctrineOrderKind.Hold;
```

---

### [Medium] PackHunters — Charge ↔ Kite on isolated targets

**File:** `PackHuntersDoctrine.cs` (~L69–70, L95–99)

Charge → Kite → (still isolated) → Charge.

**Suggested patch:** after Charge, require `NearestThreatDistance > ChargeRange` (or a reform order) before another Charge.

---

### [Medium] ArtilleryJelly — Kite ↔ RetreatAndReform when `d < comfortMin`

**File:** `ArtilleryJellyDoctrine.cs` (~L78–82)

No special case for `previous == RetreatAndReform` → falls back into Kite if still pressed.

**Suggested patch:**

```csharp
if (d < comfortMin)
{
    if (previous == DoctrineOrderKind.RetreatAndReform
        || previous == DoctrineOrderKind.Kite)
        return DoctrineOrderKind.RetreatAndReform;
    return DoctrineOrderKind.Kite;
}
```

---

### [Medium] InsectSiege — `NearDvergr` bypasses broken / casualty withdraw

**File:** `InsectSiegeDoctrine.cs` (~L72–82)

`NearDvergr` block runs **before** `IsBroken || CasualtyRatio`. Soften forever even when shattered.

**Suggested patch:** evaluate break/retreat before (or inside) Dvergr soften:

```csharp
if (snapshot.IsBroken || snapshot.CasualtyRatio >= 0.40f)
    return DoctrineOrderKind.RetreatAndReform;
if (snapshot.NearDvergr) { /* soften */ }
```

---

### [Low] Roman — no post-Charge reform

Unlike Viking/Ambush, Roman stays in Charge until range/break changes. Can look like endless wedge; optional `Charge → RetreatAndReform` beat for “disciplined reform” fantasy.

---

### [Low] Viking indoors — Hold/FocusFire until `chargeBand` (≤7)

By design (choke bias). Risk: archers FocusFire forever if players hover at 8–10m and never enter band. Acceptable; consider Advance creep if `d < advanceBand` for too many ticks.

---

### [Info] Ambush / Viking post-Charge → RetreatAndReform

Good anti-slugfest. Ambush re-ambush Hold when `d > reAmbushGap` is sound.

---

## Role assignment contradictions

### [High] ArtilleryJelly — Elite/Oozer as `Leader` but doctrine is all-missile artillery

**File:** `ArtilleryJellyDoctrine.cs` `AssignRole`

Sets `LooksLikeMissile = true` then may return `Leader`. `CountByRole(Missile)` skips Leader; `OrderApplicator` formation puts Leader at depth `-1.5` not artillery rear `-5.5`; siege missile-cover uses `AssignedRole == Missile` only.

**Suggested patch:** always `SquadRole.Missile`; encode alpha via `LooksLikeLeader` only, or add a non-formation “leader flag” without changing slot:

```csharp
member.LooksLikeMissile = true;
member.LooksLikeLeader = IsElite(member) || IsLowestId(member, squad);
return SquadRole.Missile;
```

---

### [Medium] Ambush — pure swarm squads never assign Leader

Normals always `Flanker`. Leader only from Brute path. Swarm-only packs have `CountByRole(Leader) == 0` (OK for hasBrute if you only care Front|Leader, but alpha heuristics / scorers expect a Leader).

**Suggested patch:** if no brute in squad after pass, promote lowest-id Flanker to Leader (second pass or “no Front/Leader yet” branch on last swarm member).

---

### [Medium] Charred Asksvin vs FlankOpportunity / Charge

Asksvin forced Flanker (good). Squad with **only** Asksvin: `hasRanks` false → close range returns Flank not Charge (comment intent). Mixed ranks: FlankOpportunity can delay Charge one tick (OK). Ensure scorer cannot reassign Asksvin → Front (`RefineRolesWithScorer` can pick any role with score > 0).

**Suggested patch:** doctrine-legal role filter in scorer, or re-assert Asksvin → Flanker after refine.

---

### [Low] PackHunters — single wolf is Leader not Flanker

`hasWolves` includes Leader (good). Formation treats Leader as near-rank, not wide orbit — weak for “encircle.” Prefer Leader still using Flanker slot for wolves, or set `LooksLikeFlanker` and teach applicator.

---

### [Low] Sequential assign + `IsLowestId`

Assignment is `OrderBy InstanceId` (`SquadDiscovery.AssignRoles`). Patterns like `!hasLeader && (LooksLikeLeader || IsElite || IsLowestId)` make the first eligible member Leader reliably; later elites never steal. Fine, but Charred `IsElite("Melee")` makes “eligible” too wide (see prefab finding).

---

### [Info] Mutating `LooksLike*` inside AssignRole

Ambush/Viking/Steppe/Insect/Charred/Pack/Jelly mutate view flags during assign. Discovery already set them — redundant and surprising for scorers reading flags mid-pass. Prefer read-only assign.

---

## Siege quiet vs hot split

### [Critical] Ambush Assault — Withdraw stuck Kiting at quiet workbench

**File:** `SiegeDirector.cs` `AssaultStance.SelectOrder` (~L248–255, L257–265)

After `RetreatAndReform`, if workbench still near (`NearestWorkbenchDistance <= WorkbenchComfortGap`) Ambush returns **Kite**, even when:

- `PlayersNearAssault == false` (quiet), and  
- `NearestThreatDistance` is huge / no threats  

Quiet Advance branch is never reached because withdraw handling runs first. Greydwarfs kite forever around an empty base instead of light-touch structure Advance.

**Suggested patch:**

```csharp
if (previous == DoctrineOrderKind.RetreatAndReform)
{
    if (!snapshot.PlayersNearAssault)
        return DoctrineOrderKind.Advance; // quiet: resume vanilla-structure press
    if (snapshot.NearestThreatDistance > snapshot.AdvanceRange
        && snapshot.NearestWorkbenchDistance > WorkbenchComfortGap(snapshot))
        return DoctrineOrderKind.Advance;
    return ambush ? DoctrineOrderKind.Kite : DoctrineOrderKind.Advance;
}
```

---

### [High] Quiet path — dead identical branches; no Hold/FocusFire clear

**File:** `AssaultStance` (~L259–264)

```csharp
if (!snapshot.PlayersNearAssault)
{
    if (previous == FocusFire || previous == ProtectMissiles)
        return Advance;
    return Advance;
}
```

Both arms Advance. Hot→quiet transition does not explicitly drop missile-cover presentation (applicator uses `AssaultPlayersPresent`, so cover flags clear — OK). Still confusing; collapse to single `return Advance`.

---

### [High] Hot path — any Missile + `ThreatCount > 0` monopolizes squad OrderKind

**File:** `AssaultStance` (~L277–283)

```csharp
if (hasMissiles && (ThreatCount > 0 || MissileThreatened || d <= AdvanceRange))
    return ProtectMissiles / FocusFire;
```

With players present and AI engaged, squad order never Charge/Flank. Wall-breakers depend entirely on `OrderApplicator` `AssaultWallBreaker` forcing chase. Fragile if patches key off `OrderKind` for breach.

**Suggested patch:** prefer breach orders when breakers exist and workbench in commit range; let applicator keep missiles on cover:

```csharp
if (hasBreakers && snapshot.NearestWorkbenchDistance <= BreachCommitRange)
{
    // TestBreach / Encircle at squad level; applicator keeps missiles PreferKeepRange
    ...
}
else if (hasMissiles && snapshot.MissileThreatened) { ... FocusWallman ... }
```

---

### [High] Quiet wall-breakers still force `allowChase = true`

**File:** `OrderApplicator.cs` (~L94–99) — adjacent to SiegeDirector contract

```csharp
if (isWallBreaker) { allowChase = true; keepRange = false; }
```

`isWallBreaker` is true whenever `AssaultActive && !missile`, **including quiet**. Contradicts “don’t fight vanilla structure targeting” despite `AllowVanillaStructure = true`.

**Suggested patch:**

```csharp
if (isWallBreaker)
{
    if (order.AllowVanillaStructure)
    {
        allowChase = false; // leave vanilla structure AI alone
        keepRange = false;
    }
    else
    {
        allowChase = true;
        keepRange = false;
    }
}
```

---

### [Medium] Hot Ambush — Charge always followed by RetreatAndReform

**File:** `AssaultStance` (~L242–246)

Same as field Ambush flash. Combined with withdraw/Kite bug above, flash-breach never settles into quiet structure chew after players leave.

---

### [Medium] `PlayersNearAssault` vs `ThreatCount` desync

Player bubble scan can mark players present while MonsterAI has no target (`ThreatCount == 0`), or vice versa (catch fallback sets players from ThreatCount). Quiet/hot gates only on `PlayersNearAssault`. Document invariant or set `PlayersNearAssault |= ThreatCount > 0` when NearWorkbench for hot role-split safety (catch path already does on failure).

---

### [Low] Quiet assault never uses Hold

Always Advance. Fine for “press structures,” but Ambush fantasy of lurking at unfinished bases is lost under siege. Optional: Hold if `NearestWorkbenchDistance > BreachCommitRange` and no pieces in melee.

---

### [Info] Eligibility gate looks correct

`ShouldEnterAssault`: master flag, `NearWorkbench`, Ambush/Viking ids, min squad size. ScriptedCommander sets `AllowVanillaStructure = assault && !PlayersNearAssault` consistently with director intent.

---

## Missing null checks

### [Medium] `AssaultStance.SelectOrder` — no null `snapshot`

**File:** `SiegeDirector.cs`

`ShouldEnterAssault` null-checks; `SelectOrder` does not. Direct test calls NRE.

**Suggested patch:** `if (snapshot == null) return DoctrineOrderKind.Hold;`

---

### [Medium] `SiegeDirector.EnrichWorkbenchProximity` — no null `assessment`

Writes `assessment.NearWorkbench` immediately. Guard: `if (assessment == null) return;`

---

### [Low] Doctrine `AssignRole` / `SelectOrder` — no null `member` / `squad` / `snapshot`

Callers (`SquadDiscovery`, `ScriptedCommander`) pass live objects. Defensive guards would harden unit tests:

```csharp
if (member == null || squad == null) return SquadRole.Unassigned;
if (snapshot == null) return DoctrineOrderKind.Hold;
```

Roman/Ambush/etc. `MatchesPrefab` already handle null/empty names. `Contains` helpers null-check `name`. Good.

---

### [Low] `TrollFortressHelper.SuggestSynergyOrder`

`HasNearbyTroll` null-checks snapshot; OK. `IsTrollPrefab` OK. Synergy when `ThreatCount <= 0` returns **Flank** (never Hold) — intentional path-clearing; can surprise if snapshot partially filled in tests.

---

### [Low] `DoctrinePackRegistry.GetById` is case-sensitive; siege eligibility uses `Eq` ignore-case

Mismatch if config/LLM emits `Ambush` vs `ambush`. Align `GetById` with ordinal ignore-case.

---

### [Info] `PluginConfig.*.Value` null-conditional

Doctrines use `PluginConfig.EnableX?.Value ?? true` — good. Avoid naked `.Value` in new packs.

---

## Suggested patch priority

1. **Critical:** AssaultStance withdraw → quiet Advance when `!PlayersNearAssault` (Ambush Kite stuck).  
2. **High:** OrderApplicator quiet wall-breaker chase vs `AllowVanillaStructure`.  
3. **High:** Ambush Flank↔Kite and Steppe Kite↔Flank hysteresis.  
4. **High:** Charred Charge↔Advance reform beat.  
5. **High:** ArtilleryJelly Leader→always Missile (+ formation).  
6. **High:** Hot assault — don’t let FocusWallman starve TestBreach squad orders when breakers + workbench in range.  
7. **Medium:** InsectSiege broken-before-Dvergr; PackHunters/ArtilleryJelly peel stickiness; Charred `IsElite`; PackHunters `IsDrake`; null guards on Siege entry points.

---

## Per-pack cheat sheet

| Pack | Prefab | FSM | Roles | Siege |
|------|--------|-----|-------|-------|
| Roman | Skeleton* broad OK | No post-charge reform | Solid | N/A |
| Ambush | Greydwarf + excludes OK | Flank↔Kite; Charge→reform OK | Swarm no Leader | Withdraw/quiet **Critical** |
| VikingShieldWall | Draugr* OK | Charge→reform OK; crypt Hold | Solid | Quiet Advance OK; hot missile monopoly |
| Steppe | Goblin*/Fuling* | Kite↔Flank | Berserker Leader OK | N/A |
| InsectSiege | Seeker/Tick/Gjall | Dvergr skips break | Tick/Soldier risk | N/A |
| CharredLegion | Charred/Asksvin | Charge↔Advance | IsElite Melee; Asksvin OK | N/A |
| PackHunters | Wolf/Drake/Hatchling | Charge↔Kite | IsDrake LooksLike; lone Leader | N/A |
| ArtilleryJelly | Blob* | Kite↔Reform | Leader vs Missile | N/A |
| TrollFortressHelper | Troll* | Flank/Kite orbit | N/A (helper) | Interacts with Ambush before siege |
| SiegeDirector | Ambush+Viking | Quiet/hot split bugs | Breakers via applicator | See Critical/High |

---

## Fixed (2026-09-18)

Addressed in code (this pass). Build: `dotnet build plugin/FactionTactics.csproj -c Release` clean (0 warnings).

| Severity | Finding | Fix |
|----------|---------|-----|
| **Critical** | Ambush Assault withdraw → permanent Kite at quiet workbench | `AssaultStance.SelectOrder`: after `RetreatAndReform`, if `!PlayersNearAssault` → **Advance** (quiet light-touch / AllowVanillaStructure). Quiet dead-branch collapsed to single Advance. |
| **High** | Quiet wall-breakers force `allowChase` vs vanilla structure AI | `OrderApplicator`: when `order.AllowVanillaStructure`, wall-breakers set `allowChase = false` / `keepRange = false` (PreferAllowVanillaStructure). Hot path still forces chase. |
| **High** | Ambush Flank↔Kite oscillation | Sticky peel: while `previous == Kite` && in flash range && `!TargetIsolated` → stay **Kite**; Flank entry gated off prior Kite. |
| **High** | Steppe Kite↔Flank when `d < 8` | Stay **Kite** while pressed (`previous` Kite or RetreatAndReform); only leave peel when `d >= 8`. |
| **High** | Charred Charge↔Advance every tick | Charge → **RetreatAndReform** beat; then Advance if gap > ChargeRange else Hold (Viking-style). |
| **High** | ArtilleryJelly Elite as Leader breaks rear slots | `AssignRole` always returns **Missile**; alpha via `LooksLikeLeader` only. |
| **High** | Hot FocusWallman starving TestBreach | Breach commit first when `hasBreakers && NearestWorkbenchDistance <= BreachCommitRange`; missile FocusWallman only when not committing breach. |
| **Medium** (cheap) | Charred `IsElite("Melee"/"Twitcher")` too broad | Elite tokens: Elite / Captain / Lord only. |
| **Medium** (cheap) | InsectSiege NearDvergr skips broken | Evaluate `IsBroken` / casualty withdraw **before** Dvergr soften. |
| **Medium** (cheap) | Null guards | `AssaultStance.SelectOrder` null snapshot → Hold; `EnrichWorkbenchProximity` null assessment → return. |

### Deferred (not in this pass)

- PackHunters Charge↔Kite stickiness; ArtilleryJelly Kite↔RetreatAndReform peel stickiness
- PackHunters `IsDrake` LooksLikeMissile fallthrough; Ambush/Insect LooksLike* role poison; Ambush swarm-only Leader
- Roman post-Charge reform; registry GetById case-sensitivity; other Low/Info items

*No offline test project present in tree; Release build only.*

*Originally generated by doctrine-focused code review; Fixed section added after patch pass.*
