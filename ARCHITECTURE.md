# Faction Tactics — architecture lock (2026-09-18)

## Product decisions
- **Route 1 (build now):** doctrine packs + squad FSM
- **Route 2 (framework only):** utility AI / behavior-tree hooks — interfaces only, no full BT
- **Route 3 (do not block):** tiny RWKV/LLM commander via `ICommander` later — **no model code in v0**
- **Implemented packs:** Roman, Ambush (+ TrollFortress addon), VikingShieldWall, Steppe, InsectSiege, CharredLegion, PackHunters, ArtilleryJelly
- **Siege Assault v1:** Assault-only; Ambush + Viking; workbench trigger; role-split when players present

## Layers
```
[ICommander]  ← ScriptedCommander (v0) | future LlmCommander (documented, not coded)
      ↓ emits SquadOrder (JSON-serializable DTO)
[SquadDirector]  tick ~0.5–1s: discover allies, min-size gate, assign roles, apply doctrine
      ↓  (+ SiegeDirector: workbench → AssaultStance for Ambush / Viking)
[DoctrinePack]  Roman / Ambush / VikingShieldWall / Steppe / InsectSiege /
                CharredLegion / PackHunters / ArtilleryJelly
      ↓  (+ TrollFortressHelper consulted by Ambush / snapshot enrichment)
[OrderApplicator]  Harmony patches: MonsterAI path/target/stance (+ assault role split)
```

### Layer contracts

| Layer | Responsibility | Key types |
|-------|----------------|-----------|
| **ICommander** | Propose high-level squad intent from a snapshot | `ICommander`, `ScriptedCommander`, `SquadOrder` |
| **SquadDirector** | Discover/group mobs, gate on min size, tick FSM, consult scorers; enrich troll / env heuristics | `SquadDirector`, `Squad`, `SquadSnapshot` |
| **DoctrinePack** | Role rules + order vocabulary per faction aesthetic | `IDoctrinePack`, pack classes below |
| **TrollFortress** | Thin addon: greys orbit/peel when Troll nearby; lone troll = vanilla | `TrollFortressHelper` |
| **SiegeDirector** | Assault v1: workbench trigger → AssaultStance (Ambush + Viking only) | `SiegeDirector`, `AssaultStance` |
| **OrderApplicator** | Translate `SquadOrder` into MonsterAI overrides via Harmony | `OrderApplicator`, `MonsterAIPatches` |

## Route 1 — doctrine packs + squad FSM (NOW)

1. **Discovery** — scan nearby same-faction humanoids (prefab family), cluster into squads by proximity.
2. **Min-size gate** — if `|squad| < Config.MinSquadSize`, leave members on vanilla MonsterAI.
3. **Role assignment** — doctrine maps prefab / equipment heuristics → `front | missile | flanker | leader`.
4. **FSM tick** — commander proposes `SquadOrder`; director applies via OrderApplicator.
5. **Orders:** `hold`, `advance`, `charge`, `flank`, `focus-fire`, `protect-missiles`, `retreat-and-reform`, `kite`.

## Faction doctrine table

| Doctrine | Id | Prefab family | Status | Key FSM states |
|----------|-----|---------------|--------|----------------|
| **Roman** | `roman` | `Skeleton*` | Full | Hold → Advance → ProtectMissiles/FocusFire → Charge/Flank → RetreatAndReform |
| **Ambush** | `ambush` | `Greydwarf*` (not Root/Greyling) | Full | Hold (lurk) → FocusFire → Flank → Charge (flash) → RetreatAndReform → Kite / re-Hold |
| **TrollFortress** | *(addon)* | `Troll*` near Ambush | Helper | Orbit Flank/Kite; lone troll = vanilla (not a pack) |
| **VikingShieldWall** | `viking-shieldwall` | `Draugr*` | Full | Hold (wall) → Advance → ProtectMissiles/FocusFire → Charge → RetreatAndReform; **IndoorsOrCrypt** choke bias |
| **Steppe** | `steppe` | `Fuling*` / `Goblin*` | Full | Kite → FocusFire (volley) → Flank (encircle) → Charge only if cut-off; **NearStructure** tighter orbit |
| **InsectSiege** | `insect-siege` | `Seeker*` / `Tick*` / `Gjall*` | Full | Advance → Gjall FocusFire → Flank → Charge; **NearDvergr** soften (Hold/Kite, less commit) |
| **CharredLegion** | `charred-legion` | `Charred*` / `Asksvin*` | Full | Hold/Advance dense ranks → FocusFire (casters) → Flank (Asksvin cavalry) → Charge |
| **PackHunters** | `pack-hunters` | `Wolf*` / `Drake*` / `Hatchling*` | Full | FocusFire (Drake overwatch) → Flank encircle → Charge (hamstring) → Kite |
| **ArtilleryJelly** | `artillery-jelly` | `Blob*` | Full | Hold/Advance → FocusFire (zone denial) → Kite if pressed; **never Charge**; PreferKeepRange |

### Snapshot heuristic flags (B/C biases)

| Flag | Used by | Source |
|------|---------|--------|
| `IndoorsOrCrypt` | VikingShieldWall | VALHEIM_REFS buried-height / dungeon heuristic; else stub `false` |
| `NearStructure` | Steppe | Piece name scan within `StructureDefenseRange`; else stub `false` |
| `NearDvergr` / `NearbyDvergrCount` | InsectSiege | `Dvergr*` / `Dverger*` within `DvergrSoftenRange` |
| `NearbyTrollCount` / `NearestTrollDistance` | Ambush + TrollFortress | Existing troll proximity |
| `NearWorkbench` / `NearestWorkbenchDistance` | Siege Assault | Piece / CraftingStation heuristic within `WorkbenchTriggerRange`; stub `false` |
| `PlayersNearAssault` | Siege Assault | Players in assault bubble → role split vs quiet vanilla structure |
| `AssaultActive` | Siege Assault | NearWorkbench + eligible doctrine + `SiegeMinSquadSize` |

## Black Forest — Ambush predators + Troll fortress

### AmbushDoctrine (`Greydwarf*`)
Prefab heuristics (verify in-game):
| Prefab | Role | Behavior |
|--------|------|----------|
| `Greydwarf` | Flanker (swarm) | Multi-angle flash; refuse slugfest |
| `Greydwarf_Elite` / Brute | Front / Leader (heavy) | Commit **only** if target isolated / staggered / low |
| `Greydwarf_Shaman` | Missile | Opener / poison pressure; hang back |
| Excludes | — | `Greydwarf_Root*`, `Greyling*` |

**FSM (mapped onto shared order vocab — no Ambush/Disperse/ReAmbush kinds):**
1. **Hold** — lurk/hide until players enter pocket (or re-ambush after disperse gap).
2. **FocusFire / ProtectMissiles** — shaman opener while pack envelopes.
3. **Flank** — multi-angle approach into pocket.
4. **Charge** — short flash commit (swarm after Flank; brute only if `TargetIsolated` / `ThreatStaggeredOrLow`).
5. **RetreatAndReform** — disperse after flash or anxiety casualties (~22%).
6. **Kite** — peel when still hot after disperse, or refuse frontal slugfest.

Stance bias: **high anxiety, low slugfest** (break contact early vs Roman).

### Troll mobile fortress (addon, not a solo doctrine)
- When a **Troll** is within `TrollSynergyRange` of a greydwarf squad: greys **orbit as skirmishers** (`Flank` / `Kite`), peel/draw off troll backside, avoid blocking troll path (no frontal `Hold`/`Charge` stack).
- **Lone troll** (no greys): **no** Faction Tactics override — trolls are not registered as a doctrine pack.
- Implementation: `TrollFortressHelper` + snapshot fields filled by `SquadDirector`; Ambush FSM consults helper first. Route 2 scorers may read the same fields; default remains `NullScorer`. Route 3 `ICommander` untouched.

### TEMP Ambush ambience (`AmbushAmbienceTemp`)
Troubleshooting wire, **not** the proper silent ambush. When a qualifying Ambush squad (≥ `AmbushAmbienceMinSquadSize`) is within `AmbushAmbiencePlayerRange` of a player (Black Forest preferred), `AmbushAmbienceDirector` may `EnvMan.SetForceEnvironment` (`AmbushAmbienceFogEnvironment`, default `Misty`) and show a per-player center message (cooldown). Fog clears via `SetForceEnvironment("")` when no threat remains or the flag is off. **Disable with `EnableAmbushAmbienceTemp=false`.**

## Siege Assault v1 (Assault only)

**Mode:** Assault only — no Defense / Raid Event yet.  
**Trigger:** player **workbenches** / crafting stations near participating squads (`WorkbenchTriggerRange`). Detection is a VALHEIM_REFS Piece + CraftingStation heuristic; stub builds leave `NearWorkbench=false`.  
**First factions:** **Ambush** (Greydwarf / Black Forest) + **VikingShieldWall** (Draugr / Swamp).  
**Meadows:** no siege. **Higher biomes:** do not enable siege flags yet (`EnableSiegeAmbush` / `EnableSiegeViking` only).

### Building damage / role split
- **No players near assault** (`!PlayersNearAssault`): light-touch `Advance` — **do not fight vanilla** structure targeting (`AllowVanillaStructure` on intents).
- **Players present:** split roles in `OrderApplicator`:
  - **Wall-breakers** (Front / Leader / melee Flanker / brute) → press structure / breach (`Charge`/`Advance`/`Flank`, `AssaultWallBreaker`).
  - **Ranged / missile** → defend those wall-attackers (`ProtectMissiles` / `FocusFire` = FocusWallman; `AssaultMissileCover`, keep range — not everyone chewing walls).

### Assault FSM (mapped onto existing order vocab)

| Siege intent | OrderKind | When |
|--------------|-----------|------|
| Withdraw | `RetreatAndReform` / `Kite` | Broken / anxiety / post-flash (Ambush) |
| TestBreach (approach) | `Advance` | Quiet assault, or closing on workbench |
| Encircle | `Flank` | Ambush hot approach / pre-commit |
| TestBreach (commit) | `Charge` | Hot commit on breach band |
| FocusWallman | `FocusFire` / `ProtectMissiles` | Hot + missiles — cover breachers from players |

Flow: workbench detect → `AssaultActive` → `SiegeDirector.TrySelectAssaultOrder` overrides doctrine `SelectOrder` in `ScriptedCommander` → applicator role-split.

### Config (`Siege` section)
- `EnableSiegeAssault` (master)
- `WorkbenchTriggerRange` (default 48)
- `SiegeMinSquadSize` (default 3)
- `EnableSiegeAmbush` / `EnableSiegeViking`

## Route 2 hooks (no full BT yet)
- `IRoleScorer` / `IActionScorer` consulted by `SquadDirector` before committing an order / role.
- Default: `NullScorer` (FSM-only) so Route 1 ships without utility AI or behavior trees.
- Future: plug in scorers without changing commander/doctrine contracts (troll proximity + env flags already on snapshot).

## Route 3 hooks (framework unblocked; no model)
- `SquadOrder` DTO: `formation`, `stance`, `focusTargetId`, `roleOverrides`, `orderKind`, optional metadata.
- `ICommander.Propose(SquadSnapshot) -> SquadOrder?`
- **v0:** `ScriptedCommander` runs doctrine FSM locally.
- **Later:** `LlmCommander` (RWKV/LLM) fills the **same** DTO — document only; **do not ship model weights, clients, or inference code** in this repo until explicitly green-lit.

### LlmCommander (future — design only)
- Input: compact `SquadSnapshot` (counts by role, threat vector, morale proxy, last order).
- Output: `SquadOrder` JSON matching the DTO schema.
- Constraints: server-authoritative host only; rate-limit proposes (e.g. 1–2 Hz max); fallback to `ScriptedCommander` on timeout/parse failure.
- Out of scope for v0: model selection, tokenizer, networking to inference runtime.

## Encounter-rate invariant (product)

Faction Tactics **does not** create encounters. It does not spawn creatures, queue raid waves, or call vanilla `RandomEvent` / invasion APIs. Vanilla spawners and events keep their rates. This mod only changes **how already-present mobs fight** (open-field doctrine + optional assault stance when those mobs are already near a workbench).

**Soft “feel” caveat:** enabling Siege Assault and aggressive doctrine packs can make bases *feel* under more pressure (nearby greydwarfs/draugr press structures, pack FSMs commit harder) without changing spawn rates. Operators who want vanilla-adjacent threat density should leave `EnableSiegeAssault` / aggressive packs off until Harmony steering is validated.

### Squad FSM persistence (architecture)

`SquadDiscovery` rebuilds `SquadUnit` each tick. `SquadDirector` keeps `SquadRuntimeState` keyed by doctrine + member-id overlap so `PreviousOrderKind`, `AgeSeconds`, and `PeakAlive` survive across ticks. `CasualtyRatio` / `IsBroken` are derived from `PeakAlive` vs current alive (and optional `HealthRatio`) so anxiety/retreat branches fire in offline sims and on dedicated.

`DiscoveryRadius` filters allies near local players; `SquadClusterRadius` only controls clustering among candidates.

## Valheim constraints
- Server-authoritative dedicated host (apply AI overrides on the machine that owns the mobs).
- Per-mob `MonsterAI`; we add **squad intent**, not per-frame neural nets.
- Min squad size gate → else vanilla AI.
- HarmonyX on BepInEx 5; `MonsterAIPatches` wired against verified `BaseAI.MoveTo` / `StopMoving` / `MonsterAI.UpdateAI` (VALHEIM_REFS). Stable member ids: `ValheimIds.ToLong(ZDOID)`. **Needs in-game smoke test.**

## Config (BepInEx)
- Enable/disable plugin
- Tick interval (default 0.75s)
- Min squad size
- Discovery radius
- Per-doctrine enable flags (`EnableRoman`, `EnableAmbush`, `EnableVikingShieldWall`, `EnableSteppe`, `EnableInsectSiege`, `EnableCharredLegion`, `EnablePackHunters`, `EnableArtilleryJelly`)
- `EnableTrollSynergy` + `TrollSynergyRange`
- `StructureDefenseRange` (Steppe NearStructure placeholder)
- `DvergrSoftenRange` (InsectSiege soften)
- Siege Assault: `EnableSiegeAssault`, `WorkbenchTriggerRange`, `SiegeMinSquadSize`, `EnableSiegeAmbush`, `EnableSiegeViking`
- Debug logging
- TEMP Ambush ambience (`AmbushAmbienceTemp`): `EnableAmbushAmbienceTemp` (disable for silent ambush), fog env, message, cooldown, player range, min squad size

## Install / packaging
- Build output → `BepInEx/plugins/FactionTactics/` (or flat `FactionTactics.dll` + optional `plugins` folder)
- GPortal instance `2099145` BepInEx/plugins after local build with Valheim refs
- Thunderstore-style layout: see README (manifest.json notes; not generated as a full pack yet)

## Compile strategy without game DLLs
- Core namespaces (`Doctrine`, `Squad`, `Orders`, `Commander`, `Config`) have **zero** Valheim assembly references.
- `HarmonyPatches` + game adapters compile under `VALHEIM_REFS` when `ValheimDir` points at a real install (Linux-friendly `/` paths; see `refs/README.md`).
- Without refs: `Stubs/` provides minimal stand-ins so the solution stays coherent; patches are `#if VALHEIM_REFS` gated.

## Namespace map
```
FactionTactics                 Plugin entry
FactionTactics.Doctrine        Packs, roles, order kinds, TrollFortressHelper
FactionTactics.Squad           Director, discovery, scorers, snapshot
FactionTactics.Siege           SiegeDirector, AssaultStance (Assault v1)
FactionTactics.Ambience        AmbushAmbienceDirector (TEMP BF fog + neck-hair; config-gated)
FactionTactics.Orders          SquadOrder DTO, applicator
FactionTactics.Commander       ICommander, ScriptedCommander
FactionTactics.Util             ValheimIds ZDOID→long packing
FactionTactics.HarmonyPatches  MonsterAI UpdateAI MoveTo/StopMoving (VALHEIM_REFS)
FactionTactics.Config          BepInEx bindings
FactionTactics.Stubs           Compile-without-game-DLLs types
```

## Related design
- Territory + clans (design only): [docs/TERRITORY-ONEPAGER.md](docs/TERRITORY-ONEPAGER.md)

- Product roadmap: [docs/ROADMAP.md](docs/ROADMAP.md)
