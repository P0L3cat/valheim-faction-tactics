# Faction Tactics

Valheim **BepInEx 5 + HarmonyX** plugin: doctrine packs + squad FSM (Route 1), with Route 2 scorer hooks and Route 3 `ICommander` / `SquadOrder` seams.

**Implemented:** Skeleton → **Roman**; Greydwarf* → **Ambush** (+ Troll mobile-fortress synergy).  
**Stubs:** Draugr → Viking, Fuling → Mongol.

See [ARCHITECTURE.md](./ARCHITECTURE.md) for the product lock.

## Open / build

### Requirements
- .NET SDK 6+ (project targets `net472`; NuGet `Microsoft.NETFramework.ReferenceAssemblies` enables Linux/macOS stub builds)
- For a **playable** build: Valheim install with **BepInEx 5** (HarmonyX / `0Harmony.dll` under `BepInEx/core`)

### Open
- Visual Studio / Rider / VS Code: open `FactionTactics.sln` (or `plugin/FactionTactics.csproj`)
- Or open the `faction-tactics` folder and point the IDE at that solution

### Build **with** Valheim references (for real install)

```bash
dotnet build plugin/FactionTactics.csproj -c Release \
  -p:ValheimDir="/path/to/Valheim"
```

Windows example:

```bat
dotnet build plugin\FactionTactics.csproj -c Release ^
  -p:ValheimDir="C:\Program Files (x86)\Steam\steamapps\common\Valheim"
```

When `$(ValheimDir)\valheim_Data\Managed\assembly_valheim.dll` exists, the project defines `VALHEIM_REFS` and links:

| Assembly | HintPath |
|----------|----------|
| `assembly_valheim` | `$(ValheimDir)\valheim_Data\Managed\` |
| `assembly_utils` | same |
| `UnityEngine`, `UnityEngine.CoreModule` | same |
| `BepInEx`, `0Harmony` | `$(ValheimDir)\BepInEx\core\` |

Override `BepInExDir` if your pack layout differs.

### Build **without** game DLLs (source review / CI smoke)

```bash
dotnet build plugin/FactionTactics.csproj -c Release
```

Without Valheim refs, `FactionTactics.Stubs` supplies minimal BepInEx / Harmony / Unity / Character / MonsterAI stand-ins. Core doctrine/squad/commander code is real; Harmony MonsterAI patches are skipped at runtime in stub mode.

Output: `plugin/bin/Release/FactionTactics.dll`

## Install

1. Build with Valheim refs (Release).
2. Copy **`FactionTactics.dll`** into the server/client:

```
<Valheim>/BepInEx/plugins/FactionTactics/FactionTactics.dll
```

   Flat `BepInEx/plugins/FactionTactics.dll` also works.

3. Launch once to generate `BepInEx/config/com.nate.factiontactics.cfg`.
4. Dedicated / GPortal: install on the **server** (server-authoritative AI). GPortal instance notes: `2099145` → `BepInEx/plugins`.

### Zip for drop-in

After a local Release build with Valheim refs:

```
FactionTactics/
  FactionTactics.dll
```

Zip that folder (or the DLL alone) and extract under `BepInEx/plugins/`.

## Thunderstore-style layout (notes)

Not a published pack yet — when packaging:

```
factiontactics-FactionTactics-<version>/
  manifest.json          # name, version_number, website_url, description, dependencies
  README.md
  icon.png               # 256x256
  plugins/
    FactionTactics.dll
```

Example `manifest.json` fields:

```json
{
  "name": "FactionTactics",
  "version_number": "0.1.0",
  "website_url": "",
  "description": "Doctrine packs + squad FSM for Valheim humanoid factions (Roman + Ambush).",
  "dependencies": [
    "denikson-BepInExPack_Valheim-5.4.2202"
  ]
}
```

Pin the BepInExPack dependency version to whatever your host uses.

## Config (BepInEx)

| Key | Default | Meaning |
|-----|---------|---------|
| `General.EnablePlugin` | true | Master switch |
| `General.TickIntervalSeconds` | 0.75 | SquadDirector tick |
| `Squad.MinSquadSize` | 3 | Below → vanilla AI |
| `Squad.DiscoveryRadius` | 40 | Ally scan radius |
| `Squad.SquadClusterRadius` | 18 | Cluster distance |
| `Doctrine.EnableRoman` | true | Skeleton* |
| `Doctrine.EnableAmbush` | true | Greydwarf* Ambush |
| `Doctrine.EnableTrollSynergy` | true | Greys orbit/peel near Troll |
| `Doctrine.TrollSynergyRange` | 28 | Troll proximity (m) |
| `Doctrine.EnableViking` | false | Draugr* stub |
| `Doctrine.EnableMongol` | false | Fuling* stub |
| `Debug.DebugLogging` | false | Verbose orders |

## Black Forest (Ambush + Troll fortress)

**Ambush predators** (`Greydwarf`, `Greydwarf_Elite`/`Brute`, `Greydwarf_Shaman`):
- Refuse fair fights: **Hold** (hide) until players enter the pocket → **Flank** (multi-angle) → **Charge** (flash) → **RetreatAndReform** (disperse) → **Hold** again (re-ambush).
- Swarm = Flanker; Shaman = Missile (opener / hang back via FocusFire/ProtectMissiles); Brute = Front/Leader and only flash-commits if the target looks isolated/staggered/low.
- High anxiety: early disperse (~22% casualties) and **Kite** instead of slugfest.

**Troll mobile fortress** (addon, not a solo doctrine):
- Troll near greydwarf squad(s) within `TrollSynergyRange` → greys **Flank/Kite** as skirmishers, peel backside, clear troll path.
- Lone troll → vanilla MonsterAI (no Ambush pack ownership of `Troll*`).

## Test notes

1. **Dedicated server** with BepInEx + this DLL; join as client.
2. Spawn / find a pack of **Skeletons** (≥ `MinSquadSize`).
3. Enable `Debug.DebugLogging` — expect log lines like  
   `[Roman] roman-1 n=5 → Hold (ShieldWall/Defensive)`.
4. Engage: orders should move Hold → Advance → FocusFire/ProtectMissiles → Charge/Flank; heavy losses → RetreatAndReform.
5. Solo skeleton (&lt; min size) should behave **vanilla**.
6. **Black Forest:** spawn Greydwarfs + optional Troll — expect `[Ambush] … → Hold/Flank/Charge/Kite`; with troll nearby, synergy prefers Flank/Kite over frontal Hold/Charge.
7. Harmony method names are **hypotheses** — if patches fail to apply, check BepInEx log and verify `MonsterAI.UpdateAI` / target APIs with ILSpy against your build (see TODOs in `HarmonyPatches/MonsterAIPatches.cs`).

## Layers (quick)

```
ICommander (ScriptedCommander)
  → SquadDirector (discover, min-size, scorers, troll proximity)
    → DoctrinePack (Roman / Ambush / Viking stub / Mongol stub)
      → TrollFortressHelper (Ambush synergy only)
      → OrderApplicator → Harmony MonsterAI intents
```

Route 2: `IRoleScorer` / `IActionScorer` default to `NullScorer`.  
Route 3: `SquadOrder` DTO + `ICommander`; `LlmCommander` documented only (`Commander/LlmCommander.NOTES.md`) — **no model code**.

## Repo layout

```
faction-tactics/
  ARCHITECTURE.md
  README.md
  plugin/
    FactionTactics.csproj
    Plugin.cs
    Config/
    Commander/
    Doctrine/          # Roman, Ambush, TrollFortressHelper, Viking, Mongol
    Squad/
    Orders/
    HarmonyPatches/
    Stubs/
```
