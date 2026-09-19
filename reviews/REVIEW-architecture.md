# Faction Tactics — architecture review

**Repo:** `/workspace/faction-tactics`  
**Date:** 2026-09-18 (CT)  
**Scope:** Encounter-rate invariant; `SquadDirector` / `SiegeDirector`; doctrine FSM edges; Harmony TODOs; multiplayer authority; config footguns.  
**Method:** Read-only review of `plugin/**/*.cs` + `ARCHITECTURE.md` / `README.md`. Report only (no large rewrites).

Severity: **blocker** | **major** | **minor** | **nit**

---

## Executive summary

| Focus | Verdict |
|-------|---------|
| Encounter-rate invariant | **OK in code** — no spawn / `RandomEvent` / raid APIs. Soft UX risk when assault + smarter AI press bases. |
| SquadDirector / SiegeDirector | **Major bugs** — FSM state wiped every tick; casualties never filled; `DiscoveryRadius` unused; global `FindObjectsOfType` scans. |
| Doctrine FSM | **Major** — `previous` / casualty / broken paths mostly dead; `IsLowestId` can skip Leader; null snapshot unguarded in packs. |
| Harmony TODOs | **Blocker for real Valheim** — patches register but `MoveTo` / `StopMoving` are commented no-ops; method names unverified. |
| Multiplayer | **Mostly OK** — `ZNet.IsServer()` tick gate; clients still `PatchAll` with empty intents. |
| Config | **Several footguns** — unused radius, dual min-size, wide workbench tokens, everything on by default. |

---

## 1. Encounter-rate invariant

### [nit] Structurally respected

**Refs:** `ARCHITECTURE.md` (encounter-rate section), `plugin/Siege/SiegeDirector.cs` (HARD INVARIANT comment), repo-wide search

- No creature spawn, `RandomEvent`, invasion, or encounter-queue calls in `plugin/**/*.cs`.
- Siege Assault only retargets **already discovered** Ambush / Viking squads near a workbench.

**Fix:** Keep a packaging CI deny-list (`rg`) for spawn/event APIs. Optional test that stub `EnrichWorkbenchProximity` never invents mobs.

### [major] Soft invariant — assault / doctrine can *feel* like more pressure

**Refs:** `plugin/Siege/SiegeDirector.cs` (`LooksLikeWorkbench`, `WorkbenchRange` default 48), `plugin/Config/PluginConfig.cs` (`EnableSiegeAssault`, `EnablePackHunters`, doctrine enables default `true`)

- Greydwarfs/Draugr already in-world within ~48 m of crafting stations enter Assault and press structures (once Harmony steers).
- PackHunters / aggressive FSMs raise perceived difficulty without changing spawn rates.

**Concrete fixes:**

1. cfg description: “Does not spawn or start raids; may make nearby mobs fight / push bases harder.”
2. Default `EnableSiegeAssault` (and optionally `EnablePackHunters`) to `false` until Harmony steering is validated; or shrink `WorkbenchTriggerRange` (24–32) and require `ThreatCount > 0` before quiet assault.
3. Tighten `LooksLikeWorkbench` to a player workbench/forge whitelist (avoid broad `CraftingStation` / cauldron / artisans matches).

---

## 2. SquadDirector / SiegeDirector

### [blocker] Squad FSM state discarded every tick

**Refs:** `plugin/Squad/SquadDiscovery.cs` (`Discover` always `new SquadUnit`, `SquadId = $"{doctrine.Id}-{++_nextSquadSerial}"`), `plugin/Squad/SquadDirector.cs` (`Tick`: `_active.Clear()` then rediscover; sets `squad.PreviousOrderKind` only on the throwaway instance), `plugin/Squad/SquadUnit.cs` (`PreviousOrderKind`, `AgeSeconds`)

- New squad objects each tick → `PreviousOrderKind` is always null when snapshotted via `SquadSnapshot.FromSquad`.
- `AgeSeconds += dt` never accumulates across ticks.
- Serial IDs grow without bound and never correlate to the same cluster.

**Impact:** Ambush Charge→`RetreatAndReform`, Viking post-Charge reform, `AssaultStance` withdraw/re-approach, PackHunters post-Charge `Kite`, ArtilleryJelly Charge→`Kite`, Charred Charge→`Advance` — **all `previous`-driven transitions fail across ticks**. FSMs collapse to distance/role heuristics.

**Concrete fix:**

1. Persist `Dictionary<string, SquadRuntimeState>` on `SquadDirector` (`PreviousOrderKind`, `PeakAlive`, `AgeSeconds`, optional centroid).
2. Stable key = doctrine id + sorted member instance ids (or primary ZDOID), **not** ephemeral `SquadId`.
3. After `Discover()`, merge state onto new `SquadUnit` before `Propose`.
4. Prune keys unseen for N ticks.

### [blocker] `CasualtyRatio` / `IsBroken` never leave defaults

**Refs:** `plugin/Squad/SquadDirector.cs` `AssessThreats` (hardcodes `CasualtyRatio = 0f`, `IsBroken = false`; never updates under `VALHEIM_REFS`), all `plugin/Doctrine/*Doctrine.cs` anxiety branches, `plugin/Siege/SiegeDirector.cs` `AssaultStance` withdraw on casualties

- Discovery only lists living members → ratio cannot be inferred without historical peak size.
- Every pack’s “retreat on casualties / broken” path is unreachable in production.

**Concrete fix:**

1. Track `PeakAlive` per persistent squad key; `CasualtyRatio = 1f - alive / PeakAlive` (reset when threat clears / squad reforms).
2. `IsBroken` when ratio ≥ doctrine threshold or alive drops below `MinSquadSize` after having been ≥ it.
3. Until then, `#warning` or trim dead branches so designers are not misled.

### [major] `DiscoveryRadius` config unused

**Refs:** `plugin/Config/PluginConfig.cs` (`DiscoveryRadius` default 40), `plugin/Squad/SquadDiscovery.cs` `CollectCandidates` (`FindObjectsOfType<MonsterAI>()` with no radius filter; clustering uses `SquadClusterRadius` only)

**Fix:** Filter candidates by distance to any local player (or active sector) using `PluginConfig.DiscoveryRadius.Value`. Document that cluster radius ≠ discovery radius.

### [major] Per-tick global `FindObjectsOfType` (server hitch risk)

**Refs:**

- `SquadDiscovery.CollectCandidates` — all `MonsterAI`
- `SquadDirector` troll / dvergr enrichment — all `MonsterAI` again
- Structure / workbench detect — all `Piece`
- Player-near-assault — all `Player`
- `AssessThreats` also invoked from `RefineRolesWithScorer` (double pass per squad)

**Fix:** One world snapshot per director tick; reuse for discovery + enrichment + siege. Prefer Valheim player/piece registries under `VALHEIM_REFS`.

### [major] Intent map never pruned

**Refs:** `plugin/Orders/OrderApplicator.cs` (`Intents` ConcurrentDictionary), `plugin/HarmonyPatches/MonsterAIPatches.cs` `TryResolveIntent`

- Intents written every apply; not cleared when mobs die, fall below min size, or leave a cluster.
- With unstable ids (below), stale intents can attach to recycled hashes.

**Fix:** After each tick, remove keys not rewritten this tick; clear intents for squads that failed the min-size gate.

### [major] Unstable member / intent identity

**Refs:** `SquadDiscovery.BuildView` (`InstanceId = ch.GetHashCode()`), `MonsterAIPatches.TryResolveIntent` (`id = ch.GetHashCode(); // TODO: stable ZDO id`)

**Fix:** Shared helper using Character/ZDO stable id; use for discovery + Harmony + intents.

### [minor] Assault size gate duplicated

**Refs:** `SquadDirector.AssessThreats` (`AssaultActive` via alive count + `SiegeDirector.MinAssaultSquadSize`), `SiegeDirector.ShouldEnterAssault` (`snapshot.MemberCount`)

- Usually aligned (`MemberCount` = alive), but two code paths.

**Fix:** Single helper `IsAssaultEligible(snapshot)` used for both flag and order override.

### [minor] Quiet assault already collapsed (good) — keep the invariant

**Refs:** `SiegeDirector.AssaultStance.SelectOrder` (`!PlayersNearAssault` → `Advance` after withdraw handling)

- Quiet path correctly avoids permanent `Kite` around empty bases. Keep that when wiring Harmony.

### [nit] `_registry` unused in `SquadDirector.Tick`

**Refs:** `SquadDirector.Tick` (`_ = _registry`)

**Fix:** Remove field if unused, or use for hot-reload enable checks.

### [nit] Siege stub path is safe for invariant

**Refs:** `SiegeDirector.EnrichWorkbenchProximity` `#else` → `NearWorkbench = false`

Assault stays off in stub/CI unless tests inject flags — good.

---

## 3. Doctrine FSM edge cases

### [major] Null snapshot unguarded in packs

**Refs:** all `IDoctrinePack.SelectOrder` / `AssignRole`; `AssaultStance.SelectOrder` null-checks; `TrollFortressHelper.HasNearbyTroll` null-checks; packs generally do not

**Fix:** Guard in `ScriptedCommander.Propose`: `if (snapshot == null || snapshot.MemberCount <= 0) return null;`

### [major] Size 0 / below-min

**Refs:** `SquadDirector.Tick` (`Members.Count < MinSquadSize` → vanilla), `SquadSnapshot.FromSquad` (`MemberCount = alive`)

- Below min → vanilla (correct).
- Prefer gating on alive count (not list count) for consistency with siege.

**Fix:** Also skip `Propose` when `MemberCount == 0`.

### [major] Leader assignment: `IsLowestId` includes missiles

**Refs:** `DoctrinePackBase.IsLowestId`, `RomanDoctrine.AssignRole`, `VikingShieldWallDoctrine.AssignRole`, `CharredLegionDoctrine.AssignRole`, `AmbushDoctrine.AssignRole`, `SquadDiscovery.AssignRoles` (clears then assigns ordered by `InstanceId`)

- `IsLowestId` scans **entire** squad, including Missile.
- If lowest id is shaman/archer and no `LooksLikeLeader`, **no Leader** is assigned (common for Roman skeletons).
- Roles fully reassigned every tick (deterministic today; flickers once real `IRoleScorer` scores &gt; 0).

**Concrete fix:**

1. Two-pass: assign Missile first; Leader among remaining; then Front/Flanker.
2. Or `IsLowestId` only among role-eligible candidates.
3. When scorers are non-null, only change role if score delta &gt; ε or freeze N ticks.

### [major] `previous`-driven transitions dead without persistent state

**Refs:** `AmbushDoctrine.SelectOrder` (post-Charge disperse; Flank→Charge via `previous == Flank`), `VikingShieldWallDoctrine`, `AssaultStance`, `PackHuntersDoctrine`, `ArtilleryJellyDoctrine`, `CharredLegionDoctrine`, `SteppeDoctrine`

Depends on squad-state persistence blocker above.

**Extra Ambush note:** swarm flash Charge requires `previous == Flank` (`ShouldCommitFlash`) — unreachable across ticks → swarm rarely Charges; only brute path with `TargetIsolated` / `ThreatStaggeredOrLow` may.

### [minor] ThreatCount flattened to 0/1

**Refs:** `SquadDirector.AssessThreats` (`if (engaged > 0) ThreatCount = 1`)

Multi-player fights never look like `ThreatCount >= 2`.

**Fix:** Count distinct target characters by ZDOID.

### [minor] `ThreatStaggeredOrLow` stagger half incomplete

**Refs:** `TryThreatConditionFlags` (health ratio OK; stagger fields only inspected/discarded)

**Fix:** Read real stagger state once confirmed, or rename to low-HP-only until then.

### [nit] Steppe null-threat both branches Hold

**Refs:** `SteppeDoctrine.SelectOrder` (`ThreatCount <= 0` → Hold whether or not `NearStructure`)

**Fix:** Collapse; optional Hold vs short Flank orbit when `NearStructure`.

### [nit] Null scorers correctly no-op

**Refs:** `NullRoleScorer` / `NullActionScorer`, `RefineRolesWithScorer` (`bestScore > 0f`), `MaybeRescoreOrder` (`currentScore == 0f` → return)

Good for Route 1. When real scorers land, `MaybeRescoreOrder` mutates `OrderKind` without remapping formation/stance — **major** then; rebuild presentation via `MapPresentation` or reject rescoring until that exists.

---

## 4. Harmony TODOs — what breaks in real Valheim

### [blocker] Patches apply but do not steer AI

**Refs:** `plugin/HarmonyPatches/MonsterAIPatches.cs` `UpdateAI_Postfix`

- `__instance.MoveTo(...)` / `__instance.StopMoving()` calls are **commented out**.
- `AllowVanillaChase` discarded (`_ = intent.AllowVanillaChase`).
- With `VALHEIM_REFS`, log says patches applied; **gameplay stays vanilla** aside from intent bookkeeping.

**Concrete fix:**

1. ILSpy-verify `MonsterAI.UpdateAI(float)` and `BaseAI.MoveTo` / `StopMoving` for the target build.
2. Uncomment real MoveTo/Stop when `HoldGround` / `PreferKeepRange`.
3. Implement empty `BaseAI_MoveTo_Patch` to redirect destination when intent present and `!AllowVanillaChase`.
4. Prefix/postfix target selection for `FocusTargetId`, missile cover, `AllowVanillaStructure`.

### [blocker] String Harmony targets can fail or miss at load

**Refs:** `[HarmonyPatch("UpdateAI")]`, `[HarmonyPatch("SetAlerted")]`, `MonsterAIPatches.Apply` → `harmony.PatchAll(...)`

- Names are documented hypotheses. Wrong name/signature → exception or silent miss.
- `SetAlerted_Prefix` is a pure placeholder.

**Fix:** Bind via `AccessTools.Method` + null check; log and skip missing methods; try/catch per patch in `Apply()`. Remove or implement `SetAlerted`.

### [major] Intent id mismatch risk

**Refs:** applicator keys `member.InstanceId`; patch uses `ch.GetHashCode()`

Same today; any unilateral change breaks all steering.

**Fix:** One `IdUtil.FromCharacter(Character)` used everywhere.

### [major] Stub builds hide Harmony regressions

**Refs:** `#if VALHEIM_REFS`, `Stubs/BepInExStubs.cs`, README compile-without-game-DLLs

**Fix:** Mandatory smoke on dedicated with `VALHEIM_REFS`; log which methods successfully bound.

### [minor] Assault / PreferKeepRange flags unused by patches

**Refs:** `MemberIntent.AssaultWallBreaker`, `AssaultMissileCover`, `AllowVanillaStructure`, `PreferKeepRange` — set in `OrderApplicator`, unread by Harmony bodies

Role-split and quiet-assault policy exist only in the DTO until patches honor them.

---

## 5. Multiplayer / server authority

### [minor] Server tick gate correct; clients still patch

**Refs:** `plugin/Plugin.cs` `Update` (`ZNet.instance != null && !IsServer()` → return), `Awake` always Harmony if `EnablePlugin`

- Dedicated / listen: director ticks on server only — good.
- Clients with the DLL still `PatchAll`; intents empty → postfix early-out. Wasteful, usually harmless.
- When `ZNet.instance == null` (early load), tick is **not** skipped (gate only applies when instance exists and client).

**Fix:**

1. Defer `PatchAll` until first server tick.
2. Treat `ZNet.instance == null` as do-not-tick after world load (or wait for a scene callback).

### [minor] No per-mob ownership filter

**Refs:** discovery / applicator

Assumes server simulates all matching `MonsterAI`. Usually true; exotic AI-migration mods could double-apply.

**Fix:** Optional `ZNetView.IsOwner()` when ZDOs are wired.

### [nit] Route 3 LlmCommander host-only — documented, not coded

**Refs:** `ARCHITECTURE.md`, `Commander/LlmCommander.NOTES.md`

Keep constraint when implemented.

---

## 6. Config footguns

| Sev | Footgun | Refs | Fix |
|-----|---------|------|-----|
| **major** | `DiscoveryRadius` advertised (README/ARCHITECTURE) but unused — operators think scans are limited | `PluginConfig.cs`, `SquadDiscovery.cs` | Wire filter or remove/rename |
| **major** | `MinSquadSize` vs `SiegeMinSquadSize` can diverge (`siege < min` ⇒ assault never arms) | `PluginConfig.cs`, directors | Clamp siege ≥ squad min or log once |
| **major** | `WorkbenchTriggerRange` 48 + broad tokens (`forge`, `cauldron`, `artisans`, any `CraftingStation`) | `SiegeDirector.LooksLikeWorkbench` | Lower default; whitelist workbench/forge prefabs |
| **major** | All doctrines + siege + troll synergy **default true** → global AI change on install | `PluginConfig.Bind` | Ship siege off; enable few packs for v0.1 |
| **minor** | `TickIntervalSeconds` can be 0.1 with full-scene scans → hitch | `Plugin.cs` clamp, discovery | Clamp ≥0.5 until spatial index; warn in description |
| **minor** | `EnablePlugin` false skips Harmony at Awake; runtime toggle does not unpatch | `Plugin.cs` | Document restart required |
| **minor** | `DebugLogging` on busy servers → spam | `PluginConfig` | Rate-limit order logs |
| **nit** | `PluginConfig.*?.Value ?? true` masks unbound config in tests | doctrines / siege | Fail fast if Bind not called |

---

## Recommended fix order

1. Persist squad FSM state (`PreviousOrderKind`, `PeakAlive`) across ticks.  
2. Compute `CasualtyRatio` / `IsBroken`.  
3. Make Harmony actually `MoveTo` / retarget (or stop claiming patches applied).  
4. Stable ZDO ids + intent pruning.  
5. Wire `DiscoveryRadius` + single per-tick world snapshot.  
6. Tighten siege defaults / workbench detection.  
7. Two-pass Leader assignment excluding missiles.

---

## What looks solid

- Layering: `ICommander` → `SquadDirector` → doctrine / `SiegeDirector` → `OrderApplicator` → Harmony.
- Encounter-rate hard rule documented and not violated by spawn APIs.
- Quiet vs hot assault split in `SquadOrder` / `OrderApplicator` is coherent on paper.
- `TrollFortressHelper` leaves lone trolls vanilla (not a pack).
- Stub / `VALHEIM_REFS` split keeps solution coherent without game DLLs.
- Min-squad-size → vanilla AI is the right product gate once discovery is radius-limited.
- `ScriptedCommander` correctly prefers Assault override then doctrine `SelectOrder`.

---

## Not verified in this pass

- ILSpy confirmation of `MonsterAI` / `BaseAI` names for a specific Valheim build.
- Runtime profiling on a populated dedicated server.
- Prefab string accuracy (`Greydwarf_Elite` vs in-game names, Asksvin spelling, etc.).
