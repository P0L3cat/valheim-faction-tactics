# Faction Tactics 0.3.0 — Install (Ungrull / anyone)

Hybrid **commander (server) + executor (client)**. Sticky server ownership is **OFF** by default so combat stays client-owned (smooth hits / motion).

## What you need

| Role | Zip | DLL |
|------|-----|-----|
| Dedicated / GPortal server | `FactionTactics-Server.zip` | `FactionTactics.dll` |
| Every player (incl. admins) | `FactionTactics-Client.zip` | `FactionTactics.Client.dll` |

**Both must be the same product version (0.3.0).** Schema mismatch logs a loud error and ignores intents.

Dependency: **BepInEx 5** (Pack from Thunderstore). ValheimPlus is optional and not required.

## Server (r2modman / manual)

1. Profile = **Dedicated server** (or copy into the server’s `BepInEx/plugins`).
2. Install **BepInExPack** if missing.
3. Drop **`FactionTactics.dll`** into `BepInEx/plugins/` (not the Client DLL).
4. Boot once; confirm log: `FactionTactics 0.3.0 loaded (0.3.0 hybrid commander…`.
5. Config `BepInEx/config/com.nate.factiontactics.cfg`:
   - `EnableEnemyServerOwnership = false` (default)
   - `EnableStickyEnemyOwnership = false` (default)
   - `EnableZdoIntentSync = true`
6. **Do not** put `FactionTactics.Client.dll` on a headless dedicated host (it idles, but keep the server pack clean).

## Client (every player — r2modman)

1. Game profile → Mods → Import the **Client** zip / Thunderstore client pack, **or** copy `FactionTactics.Client.dll` into the game’s `BepInEx/plugins/`.
2. Confirm log: `FactionTactics.Client 0.3.0 loaded — owning-client combat executor…`.
3. Join the FT server. Formations / Hold / MoveTo come from server ZDO intents; your client still **owns** nearby enemies for physics + hits.

## Listen server (host in-game)

Install **both** DLLs on the host PC (Server + Client packs), or Server alone (listen host runs executor when not dedicated). Players still need the Client DLL.

## Version mismatch

If you see `[FactionTactics] Intent schema mismatch` — reinstall **both** packs from the same 0.3.0 release. Do not mix 0.2.x server with 0.3 client.

## Not included / not done by this pack

- No GPortal auto-deploy
- No raise of spawn/raid rates
- Sticky ownership remains debug-only behind config flags
