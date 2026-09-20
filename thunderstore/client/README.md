# Faction Tactics 1.0.3 — Install (Ungrull / anyone)

## What you need

| Who | Zip | Drops into |
|-----|-----|------------|
| **Dedicated / listen-host server** | `FactionTactics-Server.zip` | `BepInEx/plugins/FactionTactics.dll` |
| **Every player client** | `FactionTactics-Client.zip` | `BepInEx/plugins/FactionTactics.Client.dll` |

**Both must be the same product version (1.0.3).** Schema mismatch logs a loud error and ignores intents.

Requires **BepInEx 5** (HarmonyX). Behavior-only: does **not** raise spawn/raid/encounter rates.

## Server (GPortal / dedicated / listen-host)

1. Install BepInEx 5 on the Valheim dedicated (or listen-host) profile.
2. Unzip `FactionTactics-Server.zip` into the game root (or copy `FactionTactics.dll` into `BepInEx/plugins/`).
3. Optional: Thunderstore / r2modman — import `FactionTactics-Server-Thunderstore.zip` into the **server** profile (add `icon.png` before Thunderstore upload; see `ICON.txt`).
4. Boot once; confirm log: `FactionTactics 1.0.3 loaded (1.0.3 hybrid commander…`.

Sticky enemy ownership / force server ownership stay **default OFF** (debug-only). Hybrid combat: server writes ZDO intents; owning client Drive.

## Players (game clients)

1. Unzip `FactionTactics-Client.zip` → `BepInEx/plugins/FactionTactics.Client.dll` (or import Thunderstore client pack into the **game** profile).
2. Confirm log: `FactionTactics.Client 1.0.3 loaded — owning-client combat executor…`.
3. Join the server that runs matching **1.0.3** server DLL.

Listen-host: install **both** DLLs on the host machine.

## Doctrines (v1)

Roman, **Death-Rush** (Meadows Greyling), Ambush, VikingShieldWall, Steppe, InsectSiege, CharredLegion, PackHunters, ArtilleryJelly.  
Siege Assault v1: Ambush + VikingShieldWall near workbench (no extra spawns).

Death-Rush: Greylings bee-line Charge and fight to the death (no kite/retreat). Optional vanilla alert SFX (`EnableDeathRushScream`).

## Console knobs (1.0.3)

On **dedicated / listen-host** (server DLL), open the Valheim console and use:

- `ft help` — list knobs
- `ft get MinSquadSize` / `ft set MinSquadSize 2` — live ConfigEntry (persisted)
- `ft reload` — reload cfg from disk
- `ft status` — version, squads, ZDO counters

Changes apply on the next SquadDirector tick (no DLL redeploy). See `docs/CONSOLE-KNOBS.md`.

## Troubleshooting

If you see `[FactionTactics] Intent schema mismatch` — reinstall **both** packs from the same 1.0.3 release. Do not mix 0.3.x / 0.2.x with 1.0.3.
