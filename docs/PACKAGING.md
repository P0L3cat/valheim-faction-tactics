# Packaging 1.0.4

## Artifacts

| File | Use |
|------|-----|
| `dist/FactionTactics-Server.zip` | GitHub Release / manual server install |
| `dist/FactionTactics-Client.zip` | GitHub Release / players |
| `dist/FactionTactics-Server-Thunderstore.zip` | Upload to Thunderstore (add icon.png first) |
| `dist/FactionTactics-Client-Thunderstore.zip` | Upload to Thunderstore (add icon.png first) |
| `dist/INSTALL.md` | Canonical install steps |
| `dist/DISCORD-BLURB.md` | #modding paste (don't post unless asked) |
| `dist/SHA256SUMS.txt` | DLL / zip hashes |

## Build

```bash
dotnet build plugin/FactionTactics.csproj -c Release
dotnet build client/FactionTactics.Client.csproj -c Release
```

Requires `refs/ValheimInstall` (VALHEIM_REFS). Version: `FtVersion.ProductVersion` = `1.0.4` in both assemblies. Intent schema **v2** (DeathRush flag).
