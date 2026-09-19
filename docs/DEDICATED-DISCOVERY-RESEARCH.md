# Dedicated-server MonsterAI discovery research

**Date:** 2026-09-18 (CT)  
**Context:** GPortal Linux `valheim_server`, BepInEx 5, Faction Tactics server-side behavior mod.  
**Symptom:** `Character.GetAllCharacters` / `s_characters` empty or player-only; `BaseAI.GetAllInstances` empty; `FindObjectsOfType` / `FindObjectsOfTypeAll<MonsterAI>` = 0; ZDOMan has enemy prefab ZDOs but `ZNetScene.FindInstance` → null (`prefabLive=0`); Harmony postfix on `MonsterAI.UpdateAI` / `BaseAI.UpdateAI` never fires (`updateAIHits=0`). Combat still looks vanilla for players. Skeletons spawned via Server Devcommands.

This doc summarizes online research (WebSearch + WebFetch) into *why* that pattern happens and what popular mods do about it.

---

## 1. Known dedicated-server quirks (Character / BaseAI instance lists)

### Official / community model of authority

Vanilla dedicated Valheim is **not** server-authoritative for creature AI:

> “The dedicated server is responsible for relaying RPC communication… **The server does not simulate any of the game logic—it is not server-side authoritative.** The authority role is claimed by the clients on locally isolated areas they interact with.”

Sources:

- [Valheim Wiki — Dedicated servers (weirdgloop)](https://valheim.weirdgloop.org/w/Dedicated_servers)
- [Valheim Fandom — Dedicated servers](https://valheim.fandom.com/wiki/Dedicated_servers) (same wording; “chunk-master” client owns area simulation)

Practical consequences on a headless dedicated process:

| Observation | Why it fits vanilla |
|-------------|---------------------|
| ZDOMan lists many enemy prefab ZDOs | Server is the **persistence / relay** of world state |
| `ZNetScene.FindInstance` → null (`prefabLive=0`) | Server never (or rarely) **creates the live GameObject** for that ZDO; clients do |
| `Character.s_characters` / `GetAllCharacters` ≈ empty or players | Characters register when `Character`/`MonsterAI` MonoBehaviours Awake/OnEnable on **this** process |
| `BaseAI.GetAllInstances` / `m_instances` empty | Same — list is filled by live AI components on this process |
| `FindObjectsOfType* <MonsterAI>` = 0 | No live MonsterAI components in the server Unity scene |
| `updateAIHits=0` / `baseAIUpdateHits=0` | `UpdateAI` only runs on the process that owns & simulates the creature |

Players still see vanilla combat because **their clients** own the zone, instantiate MonsterAI, run `UpdateAI`, and replicate results via ZDOs/RPCs.

### Zone / spawn ownership (related)

From the Valheim Modding Wiki spawning notes:

- `_ZoneCtrl` / `SpawnSystem` uses an **IsOwner** check — only one peer controls spawning for that zone controller.
- Instantiation path: `ZoneSystem.Update` → `CreateLocalZones` / `CreateGhostZones` → `SpawnZone`.
- Ghost zones: zoneCtrl may be destroyed almost immediately after init (no lasting local simulation).

Source: [Valheim-Modding Wiki — Spawning](https://github.com/Valheim-Modding/Wiki/wiki/Spawning)  
(Mirror: [github-wiki-see Spawning](https://github-wiki-see.page/m/Valheim-Modding/Wiki/wiki/Spawning))

### Mod-loader caveats (not the root cause here, but check once)

- Confirm `BepInEx/LogOutput.log` is fresh on the host: [Server Troubleshooting](https://github.com/Valheim-Modding/Wiki/wiki/Server-Troubleshooting)
- GPortal / hosts sometimes need a BepInEx / ValheimPlus toggle for plugins to load.

Faction Tactics already logs heartbeats with `prefabZdos` / `prefabLive` / `updateAIHits`, so the plugin **is** loading; the empty MonoBehaviour lists are architectural, not “plugin dead.”

---

## 2. How popular server AI / networking mods find or create creatures

### Pattern A — Force the dedicated server to **load zones + take ZDO ownership** (then live instances appear)

**Serverside Simulations** (`MVP.Valheim_Serverside_Simulations` / [ddormer/valheim-serverside](https://github.com/ddormer/valheim-serverside)):

- Premise: vanilla hands simulation of an area to the first client that enters; this mod creates terrain/monsters **on the server** so the server owns and simulates them.
- Decompiled core patches ([Thunderstore source](https://old.thunderstore.io/c/valheim/p/mvp/Serverside_Simulations/source/)):

| Hook | Pattern |
|------|---------|
| `ZNetScene.CreateDestroyObjects` **Prefix (replace)** | For each connected peer, `FindSectorObjects` around `peer.GetRefPos()`, then call `CreateObjects` / `RemoveObjects` — **server instantiates what clients would** |
| `ZoneSystem.Update` **Prefix (replace)** | Server calls `CreateLocalZones(peer.GetRefPos())` for every peer |
| `ZoneSystem.IsActiveAreaLoaded` | Active area considered loaded if all peers’ zones exist |
| `ZDOMan.ReleaseNearbyZDOS` **Prefix (replace)** | If ZDO in a peer active area and unowned / owner not in area → **`item.SetOwner(ZNet.GetUID())`** (server takes ownership) |
| `ZNetScene.OutsideActiveArea` | Outside = outside **all** peers’ active areas |
| `SpawnSystem.UpdateSpawning` transpile | Bypass `Player.m_localPlayer == null` early-outs so spawners run on dedicated |
| Ships | Prefer driver ownership; idle ships → server owner |

Dev notes from README:

- `Player.m_localPlayer` is always null on dedicated.
- `ZNet.instance.GetReferencePosition()` is **not** a player position on dedicated (outside world).
- HUD/graphics should be behind `IsDedicated()` checks.

**FiresGhettoNetworking** ([Thunderstore](https://thunderstore.io/c/valheim/p/VerdantsAscent/FiresGhettoNetworking/)) — modern successor mindset:

- Explicit: **“Ownership is simulation: whoever owns a creature runs its AI, pathing, attacks, movement physics and death checks.”**
- Even with **Server-Side Simulation ON**, by default: *“mobs are still simulated by the nearest client… The server decides when and where they spawn… but it is not running their brains.”*
- Separate, discouraged switch: **ZDO ownership transfer** (broad or selective) moves creature brains onto the server.

### Pattern B — Discover via **live instance hooks**, not ZDO-only walks

Once (and only if) the process has live objects:

1. **Harmony postfix** `MonsterAI` / `BaseAI` / `Character` **Awake / OnEnable / OnDisable** → maintain a private registry (Faction Tactics already does this in `MonsterAIRegistry`).
2. **`ZNetScene.AddInstance` postfix** → when a `ZNetView` is registered into `m_instances`, harvest `MonsterAI` / `BaseAI` / `Character` from that view (FT already patches this).
3. Vanilla lists: `Character.GetAllCharacters()`, `BaseAI.GetAllInstances()` / reflected `m_instances`, `ZNetScene.m_instances` — **only populated for objects created on this process**.
4. **Do not** treat `ZNet.GetAllCharacterZDOS()` as mob discovery — it returns **player** character ZDOs only (already documented in `ValheimWorldScan`).

### Pattern C — ZDO-level behavior (no MonsterAI)

Some server mods steer world state **without** MonoBehaviours: read/write ZDO fields, send RPCs, filter `SetTarget` / damage RPCs (FGN RPC AoI router). That can change *what clients see*, but Faction Tactics’ OrderApplicator is built around **live `MonsterAI` MoveTo / target overrides** — ZDO-only steering would be a different architecture.

### Pattern D — Content / AI config mods (MonsterDB, VillageNPCs, etc.)

- [MonsterDB AI wiki](https://github.com/RustyMods/MonsterDB/wiki/Configure:-AI): `MonsterAI` vs `AnimalAI` both inherit `BaseAI`; they configure **prefab** AI fields — they assume a real AI component exists when the creature is simulated.
- VillageNPCs / custom NPC mods set `MonsterAI` fields when **building/spawning** prefabs on a process that already has live instances (usually client or serverside-sim world).

None of these magically populate MonsterAI on a vanilla dedicated process that never called `CreateObjects` for those ZDOs.

---

## 3. Can spawned / Devcommands creatures exist client-side only (or without server MonsterAI)?

**Yes — that is the normal dedicated case.**

- Spawning (vanilla SpawnSystem, CreatureSpawner, or admin `spawn` via [Server Devcommands](https://thunderstore.io/c/valheim/p/JereKuusela/Server_devcommands/)) ultimately creates or mutates **ZDOs** that peers replicate.
- The peer that **owns** the ZDO (typically a nearby client / chunk-master) runs `ZNetScene` create path → GameObject + `ZNetView` + `Character` + `MonsterAI`.
- On vanilla dedicated: server may hold the ZDO in ZDOMan (hence `prefabZdos` ≫ 0) while **never** calling `CreateObject` / `AddInstance` for that ZDO → `FindInstance` null, no MonsterAI, no `UpdateAI`.

So Devcommands skeletons fighting “normally” for players does **not** imply server-side MonsterAI exists. It implies **client-side** MonsterAI exists for the owner client.

Whether the *spawn RPC itself* was issued by server or client is secondary; **ownership + CreateDestroyObjects** decide where the live AI lives.

---

## 4. ZDO ownership and forcing live MonsterAI on dedicated

### Rule of thumb

> **Live MonsterAI on a process ⇔ that process has instantiated the ZNetView for the ZDO (usually after owning / being in active create range).**

Vanilla dedicated:

1. Often does **not** load peer-centered zones the way a client does (`GetReferencePosition` useless; CreateDestroyObjects keyed off local ref).
2. Releases / assigns ownership so **clients** simulate nearby persistent ZDOs.
3. Therefore: ZDOs present, instances absent, UpdateAI never runs on server.

### How Serverside Simulations forces it

From decompiled `ZDOMan_ReleaseNearbyZDOS_Patch`:

- Near any peer’s active area + persistent ZDO + (unowned or owner not in peer active area) → **`zdo.SetOwner(ZNet.GetUID())`** (server UID).
- Combined with patched `CreateDestroyObjects` that creates objects around **all peers**, the server then has live GameObjects and thus MonsterAI / BaseAI UpdateAI loops.

FGN’s “ZDO ownership transfer” is the same idea (with warnings about rigidbodies / ships).

### DIY ownership / instantiate (without shipping a full networking mod)

High-level recipe (experimental; easy to fight vanilla handoff):

1. Resolve enemy ZDOs via ZDOMan (prefab hash / sector) — FT already can count these.
2. For ZDOs near any `ZNetPeer.GetRefPos()`:
   - `zdo.SetOwner(ZNet.instance.GetUID())` or `ZNetView.ClaimOwnership()` once a view exists.
3. Ensure `ZNetScene` actually **creates** the instance:
   - Either patch `CreateDestroyObjects` like Serverside Simulations, or call into create helpers carefully (fragile across patches).
4. After `AddInstance`, registry / `FindInstance` / `GetComponent<MonsterAI>()` should become non-null and `UpdateAI` postfixes should start incrementing.

**Risks:** double-simulation if a client still thinks it owns the AI; ships/carts physics; CPU blow-up on GPortal; fighting other networking mods (FGN, BetterNetworking, VCP). Prefer **depending on / documenting** an existing serverside-sim stack over reimplementing ownership transfer in Faction Tactics.

---

## 5. Concrete recommended fix path for Faction Tactics

### Root-cause ranking (for our GPortal symptoms)

1. **Highest confidence — Vanilla dedicated never simulates MonsterAI**  
   Matches wiki + Serverside Simulations + FGN docs + our counters (`prefabZdos>0`, `prefabLive=0`, `updateAIHits=0`). Combat feels vanilla because **clients** run AI.

2. **High — Zone create/destroy never runs for peer positions on dedicated**  
   Without peer-centered `CreateDestroyObjects` / `CreateLocalZones`, even server-owned ZDOs may never become live instances.

3. **Medium — Soft-dependency on Serverside Simulations / FGN ownership transfer**  
   If the host does not run those, Faction Tactics cannot see MonsterAI with scan-only approaches.

4. **Lower — Wrong spawn path / client-only Devcommands instantiation**  
   Possible nuance for *who* creates the first ZDO, but does not change the ownership/simulation model.

5. **Lowest — Harmony patch miss / wrong method**  
   Unlikely if both MonsterAI and BaseAI UpdateAI hit counts stay 0 while registry Awake/AddInstance also see nothing; would revisit only if live instances exist but UpdateAI still silent.

### Recommended product / engineering paths

| Path | Effort | Notes |
|------|--------|-------|
| **A. Soft-require Serverside Simulations or FGN “ownership transfer / SSS”** | Low (ops) | Document: “Faction Tactics AI steering needs server-owned live MonsterAI.” Smoke test with SSS installed → expect `prefabLive` and `updateAIHits` > 0. |
| **B. Optional companion patch (subset of SSS Core)** | Medium | Peer-centered `CreateDestroyObjects` + `ReleaseNearbyZDOS` SetOwner(server) behind config `ForceServerCreatureSimulation`. Only on dedicated. Heavy CPU warning. |
| **C. Dual-mode architecture** | High | Server emits squad orders as RPCs / ZDO intents; **client-side** Faction Tactics (or thin client plugin) applies MonsterAI overrides where UpdateAI actually runs. Fits vanilla authority; needs client install. |
| **D. Keep ZDO-only discovery for telemetry** | Done-ish | Useful for “mobs exist in world” dashboards; **cannot** drive OrderApplicator without live AI. |

**Recommended default:** Path **A** short-term (prove hypothesis), then **B or C** based on whether Nate wants zero client deps (B) or vanilla-compatible lightweight server (C).

---

## What to try next (short list)

1. **Install Serverside Simulations (or FGN with selective ZDO ownership transfer) on the GPortal dedicated only**, restart, spawn skeletons again, watch Faction Tactics heartbeat: expect `prefabLive` ↑ and `updateAIHits` / `baseAIUpdateHits` ↑. If yes → hypothesis #1/#2 confirmed; document soft dependency.

2. **Add a one-shot diagnostic** (server log): for N enemy ZDOs near each peer, print `owner`, `HasOwner`, `ZNetScene.FindInstance != null`, distance to nearest peer. Confirms “unowned/client-owned + no instance” without guessing.

3. **If SSS is not acceptable:** prototype config-gated peer-centered create + `SetOwner(ZNet.GetUID())` for enemy prefab hashes only (skip ships/pieces), measure CPU and whether `MonsterAIRegistry` / UpdateAI hits appear; abort if clients desync.

4. **Parallel experiment for Path C:** tiny client plugin that only applies `MemberIntent` from server RPC — validates OrderApplicator on the process that actually runs UpdateAI.

5. **Do not** invest more scan paths (`FindObjectsOfTypeAll`, more ZDO walks) — they cannot invent MonoBehaviours the server never created.

---

## Key links

| Topic | URL |
|-------|-----|
| Dedicated not authoritative | https://valheim.weirdgloop.org/w/Dedicated_servers |
| Same (Fandom) | https://valheim.fandom.com/wiki/Dedicated_servers |
| Serverside Simulations README | https://github.com/ddormer/valheim-serverside |
| SSS decompiled patches | https://old.thunderstore.io/c/valheim/p/mvp/Serverside_Simulations/source/ |
| FGN ownership = simulation | https://thunderstore.io/c/valheim/p/VerdantsAscent/FiresGhettoNetworking/ |
| Spawning / ZoneCtrl IsOwner | https://github.com/Valheim-Modding/Wiki/wiki/Spawning |
| BepInEx server load checks | https://github.com/Valheim-Modding/Wiki/wiki/Server-Troubleshooting |
| Server Devcommands | https://thunderstore.io/c/valheim/p/JereKuusela/Server_devcommands/ |
| MonsterAI vs AnimalAI fields | https://github.com/RustyMods/MonsterDB/wiki/Configure:-AI |
| Jotunn instance type helpers | https://github.com/Valheim-Modding/Jotunn/blob/prod/JotunnLib/Extensions/ZNetExtension.cs |

---

## Relation to current Faction Tactics code

Already aligned with this research:

- `ValheimWorldScan` documents empty `GetAllCharacters` / FindObjectsOfType on dedicated and walks ZDOMan + BaseAI lists.
- `MonsterAIRegistry` + patches on Awake/OnEnable/`ZNetScene.AddInstance` + UpdateAI hit counters are the **right** discovery model **if** live instances exist.
- Heartbeat fields `prefabZdos` / `prefabLive` / `updateAIHits` are the smoking gun for “ZDO without simulation.”

Missing piece is **not** another enumerator — it is **making the dedicated process create + own creature instances**, or **moving OrderApplicator to the owning client**.

---

## 0.1.9 thin enemy-ownership PoC

Nate aborted full SSS. Faction Tactics 0.1.9 adds `EnemyOwnershipDirector` (config `EnableEnemyServerOwnership`, default true for PoC): peer-centered `FindSectorObjects` → enemy filter → `SetOwner(server)` → reflect `ZNetScene.CreateObject` only. No ZoneSystem / CreateDestroyObjects replace; ships/trees/pieces stay client-auth. Details: [`ENEMY-OWNERSHIP-POC.md`](./ENEMY-OWNERSHIP-POC.md).
