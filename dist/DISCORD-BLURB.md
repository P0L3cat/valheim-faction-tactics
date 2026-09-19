**Faction Tactics 0.3.0 (hybrid)** — server writes squad intents to ZDOs; **players install the Client DLL** so combat stays client-owned (no more sticky-server statues).

1. Server: BepInEx 5 + `FactionTactics-Server.zip` → `BepInEx/plugins/FactionTactics.dll`
2. Everyone who joins: `FactionTactics-Client.zip` → `FactionTactics.Client.dll`
3. Same **0.3.0** on both sides (mismatch fails loudly in log)

GitHub artifacts: _(attach release link)_  
Thunderstore: _(when published)_ Server pack + Client pack  
r2modman: import each zip into the right profile (server vs game).
