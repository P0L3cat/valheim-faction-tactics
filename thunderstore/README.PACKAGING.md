# Thunderstore packaging notes

1. Build Release with Valheim refs.
2. Create folder `plugins/FactionTactics.dll` (or nested `plugins/FactionTactics/FactionTactics.dll`).
3. Add `manifest.json`, `icon.png` (256²), package `README.md`.
4. Zip **contents** (manifest at zip root), upload to Thunderstore / Thunderstore Mod Manager.

Dependencies: BepInExPack for Valheim (denikson). Do not bundle BepInEx or Harmony DLLs inside the pack.
