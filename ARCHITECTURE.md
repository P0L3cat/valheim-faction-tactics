# Faction Tactics — architecture lock (2026-09-18)

## Product decisions
- **Route 1 (build now):** doctrine packs + squad FSM
- **Route 2 (framework only):** utility AI / behavior-tree hooks — interfaces only, no full BT
- **Route 3 (do not block):** tiny RWKV/LLM commander via `ICommander` later — **no model code in v0**
- **First spike:** Skeleton → Roman (fully implemented)
- **Black Forest:** Greydwarf* → Ambush (fully implemented) + Troll mobile-fortress synergy (addon)
- **Stubs:** Draugr → Viking; Fuling → Mongol

## Layers
```
[ICommander]  ← ScriptedCommander (v0) | future LlmCommander (documented, not coded)
      ↓ emits SquadOrder (JSON-serializable DTO)
[SquadDirector]  tick ~0.5–1s: discover allies, min-size gate, assign roles, apply doctrine
      ↓
[DoctrinePack]  Roman (full) / Ambush (full) / Viking (stub) / Mongol (stub)
      ↓  (+ TrollFortressHelper consulted by Ambush / snapshot enrichment)
[OrderApplicator]  Harmony patches: MonsterAI path/target/stance
```

### Layer contracts

| Layer | Responsibility | Key types |
|-------|----------------|-----------|
| **ICommander** | Propose high-level squad intent from a snapshot | `ICommander`, `ScriptedCommander`, `SquadOrder` |
| **SquadDirector** | Discover/group mobs, gate on min size, tick FSM, consult scorers; enrich troll proximity | `SquadDirector`, `Squad`, `SquadSnapshot` |
| **DoctrinePack** | Role rules + order vocabulary per faction aesthetic | `IDoctrinePack`, `RomanDoctrine`, `AmbushDoctrine`, … |
| **TrollFortress** | Thin addon: greys orbit/peel when Troll nearby; lone troll = vanilla | `TrollFortressHelper` |
| **OrderApplicator** | Translate `SquadOrder` into MonsterAI overrides via Harmony | `OrderApplicator`, `MonsterAIPatches` |

## Route 1 — doctrine packs + squad FSM (NOW)

1. **Discovery** — scan nearby same-faction humanoids (prefab family), cluster into squads by proximity.
2. **Min-size gate** — if `|squad| < Config.MinSquadSize`, leave members on vanilla MonsterAI.
3. **Role assignment** — doctrine maps prefab / equipment heuristics → `front | missile | flanker | leader`.
4. **FSM tick** — commander proposes `SquadOrder`; director applies via OrderApplicator.
5. **Orders:** `hold`, `advance`, `charge`, `flank`, `focus-fire`, `protect-missiles`, `retreat-and-reform`, `kite`.

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
- Implementation: `TrollFortressHelper` + snapshot fields (`NearbyTrollCount`, `NearestTrollDistance`) filled by `SquadDirector`; Ambush FSM consults helper first. Route 2 scorers may read the same fields; default remains `NullScorer`. Route 3 `ICommander` untouched.

## Route 2 hooks (no full BT yet)
- `IRoleScorer` / `IActionScorer` consulted by `SquadDirector` before committing an order / role.
- Default: `NullScorer` (FSM-only) so Route 1 ships without utility AI or behavior trees.
- Future: plug in scorers without changing commander/doctrine contracts (troll proximity already on snapshot).

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

## Valheim constraints
- Server-authoritative dedicated host (apply AI overrides on the machine that owns the mobs).
- Per-mob `MonsterAI`; we add **squad intent**, not per-frame neural nets.
- Min squad size gate → else vanilla AI.
- HarmonyX on BepInEx 5; patch hypotheses documented in `HarmonyPatches` with TODOs until validated in-game.

## Prefab map
| Doctrine | Prefab family | Status |
|----------|---------------|--------|
| Roman | `Skeleton*` | **Implement** |
| Ambush | `Greydwarf*` (not Root / Greyling) | **Implement** |
| TrollFortress | `Troll*` near Ambush squads | Addon helper (not a pack) |
| Viking | `Draugr*` | Stub pack |
| Mongol (steppe) | `Fuling*` / `Goblin*` | Stub pack |

## Config (BepInEx)
- Enable/disable plugin
- Tick interval (default 0.75s)
- Min squad size
- Discovery radius
- Per-doctrine enable flags (`EnableRoman`, `EnableAmbush`, `EnableViking`, `EnableMongol`)
- `EnableTrollSynergy` + `TrollSynergyRange`
- Debug logging

## Install / packaging
- Build output → `BepInEx/plugins/FactionTactics/` (or flat `FactionTactics.dll` + optional `plugins` folder)
- GPortal instance `2099145` BepInEx/plugins after local build with Valheim refs
- Thunderstore-style layout: see README (manifest.json notes; not generated as a full pack yet)

## Compile strategy without game DLLs
- Core namespaces (`Doctrine`, `Squad`, `Orders`, `Commander`, `Config`) have **zero** Valheim assembly references.
- `HarmonyPatches` + game adapters compile under `VALHEIM_REFS` when `ValheimDir` points at a real install.
- Without refs: `Stubs/` provides minimal stand-ins so the solution stays coherent; patches are `#if VALHEIM_REFS` gated.

## Namespace map
```
FactionTactics                 Plugin entry
FactionTactics.Doctrine        Packs, roles, order kinds, TrollFortressHelper
FactionTactics.Squad           Director, discovery, scorers, snapshot
FactionTactics.Orders          SquadOrder DTO, applicator
FactionTactics.Commander       ICommander, ScriptedCommander
FactionTactics.HarmonyPatches  MonsterAI Harmony stubs/TODOs
FactionTactics.Config          BepInEx bindings
FactionTactics.Stubs           Compile-without-game-DLLs types
```
