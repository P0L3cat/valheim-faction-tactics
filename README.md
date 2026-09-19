# Faction Tactics

Valheim **BepInEx 5 + HarmonyX** plugin: doctrine packs + squad FSM (Route 1), with Route 2 scorer hooks and Route 3 `ICommander` / `SquadOrder` seams.

**Implemented packs:** Roman, Ambush (+ Troll fortress), VikingShieldWall, Steppe, InsectSiege, CharredLegion, PackHunters, ArtilleryJelly.  
**Siege Assault v1:** Assault-only for Ambush + VikingShieldWall (workbench trigger).

See [ARCHITECTURE.md](./ARCHITECTURE.md) for the product lock.

## Encounter rate

This mod **only tunes AI behavior**. It does **not** spawn mobs, trigger extra raids, or change vanilla encounter rates. Siege "assault" means: if greydwarfs/draugr are *already* near your workbench, they fight smarter — nothing new is summoned.

**Feel note:** smarter nearby mobs (assault + doctrine) can still *feel* like more base pressure. That is behavior-only; spawn rates are unchanged. See `ARCHITECTURE.md` encounter-rate section.

## Faction table

| Pack | Prefabs | Enable flag | Key FSM |
|------|---------|-------------|---------|
| Roman | `Skeleton*` | `EnableRoman` | Hold → Advance → FocusFire/ProtectMissiles → Charge/Flank → Reform |
| Ambush | `Greydwarf*` | `EnableAmbush` | Hold → Flank → Charge (flash) → Reform → Kite (+ Troll synergy) |
| VikingShieldWall | `Draugr*` | `EnableVikingShieldWall` | Shield wall Hold → Advance → archers FocusFire → Charge → Reform (choke bias indoors) |
| Steppe | `Fuling*` / `Goblin*` | `EnableSteppe` | Kite → volley FocusFire → Flank encircle; berserk Charge only on cut-off; village orbit |
| InsectSiege | `Seeker*` / `Tick*` / `Gjall*` | `EnableInsectSiege` | Soldiers Advance, Seekers Flank, Gjall FocusFire; soften near Dvergr |
| CharredLegion | `Charred*` / `Asksvin*` | `EnableCharredLegion` | Dense ranks + rear casters; Asksvin cavalry Flank (not in rank) |
| PackHunters | `Wolf*` / `Drake*` / `Hatchling*` | `EnablePackHunters` | Wolves Flank/Charge; Drake FocusFire overwatch / punish clump |
| ArtilleryJelly | `Blob*` | `EnableArtilleryJelly` | Keep range FocusFire / Kite; never melee Charge |

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

When `$(ValheimDir)/valheim_Data/Managed/assembly_valheim.dll` exists, the project defines `VALHEIM_REFS` and links:

| Assembly | HintPath |
|----------|----------|
| `assembly_valheim` | `$(ValheimDir)/valheim_Data/Managed/` |
| `assembly_utils` | same |
| `UnityEngine`, `UnityEngine.CoreModule` | same |
| `BepInEx`, `0Harmony` | `$(ValheimDir)/BepInEx/core/` |

On Linux, paths use forward slashes (see `refs/README.md` for a local symlink `ValheimInstall` tree).

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
  "description": "Doctrine packs + squad FSM for Valheim humanoid factions.",
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
| `Squad.DiscoveryRadius` | 64 | Ally scan radius from local players (≠ cluster radius) |
| `Squad.SquadClusterRadius` | 18 | Cluster distance |
| `Doctrine.EnableRoman` | true | Skeleton* Roman |
| `Doctrine.EnableAmbush` | true | Greydwarf* Ambush |
| `Doctrine.EnableVikingShieldWall` | true | Draugr* VikingShieldWall |
| `Doctrine.EnableSteppe` | true | Fuling*/Goblin* Steppe |
| `Doctrine.EnableInsectSiege` | true | Seeker*/Tick*/Gjall* InsectSiege |
| `Doctrine.EnableCharredLegion` | true | Charred*/Asksvin* CharredLegion |
| `Doctrine.EnablePackHunters` | true | Wolf*/Drake*/Hatchling* PackHunters |
| `Doctrine.EnableArtilleryJelly` | true | Blob* ArtilleryJelly |
| `Doctrine.EnableTrollSynergy` | true | Greys orbit/peel near Troll |
| `Doctrine.TrollSynergyRange` | 28 | Troll proximity (m) |
| `Doctrine.StructureDefenseRange` | 24 | Steppe NearStructure placeholder (m) |
| `Doctrine.DvergrSoftenRange` | 30 | InsectSiege Dvergr soften (m) |
| `Siege.EnableSiegeAssault` | true | Assault v1 master (no Defense/Raid yet) |
| `Siege.WorkbenchTriggerRange` | 48 | Workbench / crafting-station detect (m) |
| `Siege.SiegeMinSquadSize` | 3 | Min size to enter Assault stance |
| `Siege.EnableSiegeAmbush` | true | Greydwarf Ambush may assault |
| `Siege.EnableSiegeViking` | true | Draugr VikingShieldWall may assault |
| `Debug.DebugLogging` | false | Verbose orders |
| `AmbushAmbienceTemp.EnableAmbushAmbienceTemp` | true | TEMP BF ambush fog + neck-hair message; **set false for silent ambush** |
| `AmbushAmbienceTemp.AmbushAmbienceFogEnvironment` | Misty | EnvMan force env (empty = skip fog) |
| `AmbushAmbienceTemp.AmbushAmbienceMessage` | the hair on your neck stands up | Center message (per threatened player) |
| `AmbushAmbienceTemp.AmbushAmbienceMessageCooldownSeconds` | 90 | Per-player message cooldown |
| `AmbushAmbienceTemp.AmbushAmbiencePlayerRange` | 40 | Trigger range (m) |
| `AmbushAmbienceTemp.AmbushAmbienceMinSquadSize` | 3 | Min alive Ambush squad size |

## Black Forest (Ambush + Troll fortress)

**Ambush predators** (`Greydwarf`, `Greydwarf_Elite`/`Brute`, `Greydwarf_Shaman`):
- Refuse fair fights: **Hold** (hide) until players enter the pocket → **Flank** (multi-angle) → **Charge** (flash) → **RetreatAndReform** (disperse) → **Hold** again (re-ambush).
- Swarm = Flanker; Shaman = Missile (opener / hang back via FocusFire/ProtectMissiles); Brute = Front/Leader and only flash-commits if the target looks isolated/staggered/low.
- High anxiety: early disperse (~22% casualties) and **Kite** instead of slugfest.

**Troll mobile fortress** (addon, not a solo doctrine):
- Troll near greydwarf squad(s) within `TrollSynergyRange` → greys **Flank/Kite** as skirmishers, peel backside, clear troll path.
- Lone troll → vanilla MonsterAI (no Ambush pack ownership of `Troll*`).

**TEMP ambush ambience** (`AmbushAmbienceTemp`): fog + neck-hair center message while a greydwarf Ambush squad is near a player. This is a troubleshooting wire — disable with `EnableAmbushAmbienceTemp=false` for proper silent ambush.

## Siege Assault v1

**Assault only** (no Defense / Raid Event). Triggered when Ambush or VikingShieldWall squads detect a nearby **player workbench** / crafting station.

| Situation | Behavior |
|-----------|----------|
| No players near assault | Light-touch `Advance` — **allow vanilla** structure targeting (don't fight MonsterAI wall chewing) |
| Players present | **Role split:** Front/Leader/Flanker (wall-breakers) press breach (`Charge`/`Advance`/`Flank`); Missile cover them (`ProtectMissiles`/`FocusFire`) — not everyone on walls |

Order mapping: Encircle→`Flank`, TestBreach→`Charge`/`Advance`, FocusWallman→`FocusFire`/`ProtectMissiles`, Withdraw→`RetreatAndReform`/`Kite`.

Meadows: no siege. Higher biomes: siege faction flags not enabled yet.

## Test notes

1. **Dedicated server** with BepInEx + this DLL; join as client.
2. Spawn / find a pack of **Skeletons** (≥ `MinSquadSize`).
3. Enable `Debug.DebugLogging` — expect log lines like  
   `[Roman] roman-1 n=5 → Hold (ShieldWall/Defensive)`.
4. Engage: orders should move Hold → Advance → FocusFire/ProtectMissiles → Charge/Flank; heavy losses → RetreatAndReform.
5. Solo skeleton (&lt; min size) should behave **vanilla**.
6. **Black Forest:** spawn Greydwarfs + optional Troll — expect `[Ambush] … → Hold/Flank/Charge/Kite`; with troll nearby, synergy prefers Flank/Kite over frontal Hold/Charge.
7. **Other packs:** Draugr shield wall, Fuling kite/encircle, Mistlands insect siege (soften near Dvergr), Charred ranks + Asksvin flank, Wolf/Drake pack hunt, Blob keep-range FocusFire.
8. **Siege Assault:** place a workbench near Greydwarfs or Draugr (≥ `SiegeMinSquadSize`) — expect `[Ambush] Assault/quiet|hot …` or Viking; with player present, missiles FocusFire/ProtectMissiles while fronts Charge/Advance.
9. **VALHEIM_REFS wired:** Mono.Cecil-verified signatures — `MonsterAI.UpdateAI` Postfix calls public `StopMoving` and protected `MoveTo` via Traverse; stable ids via `ValheimIds.ToLong(GetZDOID())` (`(UserID&0xFFFFFFFF)<<32 | ID`). Still **needs in-game smoke test** (HoldGround stop, PreferKeepRange kite slot, vanilla chase when `AllowVanillaChase`). If patches fail to apply, check BepInEx log against your build.

## Layers (quick)

```
ICommander (ScriptedCommander)
  → SquadDirector (discover, min-size, scorers, env/troll/workbench proximity)
    → SiegeDirector / AssaultStance (Ambush + Viking when NearWorkbench)
    → DoctrinePack (all factions above)
      → TrollFortressHelper (Ambush synergy only)
      → OrderApplicator → Harmony MonsterAI intents (+ assault role split)
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
    Doctrine/          # Roman, Ambush, VikingShieldWall, Steppe, InsectSiege,
                       # CharredLegion, PackHunters, ArtilleryJelly, TrollFortressHelper
    Squad/
    Siege/             # SiegeDirector, AssaultStance (Assault v1)
    Ambience/          # TEMP AmbushAmbienceDirector (fog + message)
    Orders/
    Util/
    HarmonyPatches/
    Stubs/
```
