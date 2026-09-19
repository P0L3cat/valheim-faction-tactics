# Valheim references (not in git)

Game and BepInEx assemblies are **not** committed (see root `.gitignore` `refs/` rules and `*.dll`).

## Layout expected by the csproj

```
$(ValheimDir)/valheim_Data/Managed/assembly_valheim.dll
$(ValheimDir)/BepInEx/core/0Harmony.dll
$(ValheimDir)/BepInEx/core/BepInEx.dll
```

## Local symlink tree (this box)

```bash
# After unpacking Managed + BepInEx/core under refs/valheim-refs-stable/:
mkdir -p refs/ValheimInstall/valheim_Data
ln -sfn "$(pwd)/refs/valheim-refs-stable/Managed" refs/ValheimInstall/valheim_Data/Managed
ln -sfn "$(pwd)/refs/valheim-refs-stable/BepInEx" refs/ValheimInstall/BepInEx
```

Build:

```bash
dotnet build plugin/FactionTactics.csproj -c Release \
  -p:ValheimDir="$(pwd)/refs/ValheimInstall"
```

Or point `ValheimDir` at a real Starlink/Steam Valheim install with BepInEx 5.
