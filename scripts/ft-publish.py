#!/usr/bin/env python3
"""Build Faction Tactics packs, GitHub Release, and Thunderstore publish.

Usage (from repo root or anywhere):
  scripts/ft-publish.py              # pack + gh release + thunderstore
  scripts/ft-publish.py --skip-build
  scripts/ft-publish.py --github-only
  scripts/ft-publish.py --thunderstore-only
  scripts/ft-publish.py --dry-run

Secrets (never printed):
  THUNDERSTORE_API_TOKEN — env or box-secrets.json card
  gh auth for GitHub releases (P0L3cat/valheim-faction-tactics)
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.request
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
DIST = REPO / "dist"
TS_CLIENT = REPO / "thunderstore" / "client"
TS_SERVER = REPO / "thunderstore" / "server"
ICON = REPO / "thunderstore" / "icon.png"
SECRETS = Path("/home/box/sand-data/box-secrets.json")
TEAM = "FactionTactics"
GH_REPO = "P0L3cat/valheim-faction-tactics"


def die(msg: str, code: int = 1) -> None:
    print(f"ERROR: {msg}", file=sys.stderr)
    raise SystemExit(code)


def run(cmd: list[str], **kw) -> None:
    print("+", " ".join(cmd))
    subprocess.check_call(cmd, cwd=str(REPO), **kw)


def read_version() -> str:
    plugin = (REPO / "plugin" / "Plugin.cs").read_text(encoding="utf-8")
    m = re.search(r'PluginVersion\s*=\s*"([^"]+)"', plugin)
    if not m:
        die("PluginVersion not found in plugin/Plugin.cs")
    return m.group(1)


def sync_manifest_versions(version: str) -> None:
    for path in (TS_CLIENT / "manifest.json", TS_SERVER / "manifest.json"):
        data = json.loads(path.read_text(encoding="utf-8"))
        data["version_number"] = version
        # Keep descriptions pointing at matching peer version lightly
        desc = data.get("description") or ""
        desc = re.sub(r"\d+\.\d+\.\d+", version, desc)
        data["description"] = desc
        path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
        print(f"manifest {path.name} → {version}")


def thunderstore_token() -> str:
    tok = os.environ.get("THUNDERSTORE_API_TOKEN")
    if tok:
        return tok
    if SECRETS.exists():
        card = json.loads(SECRETS.read_text()).get("card") or {}
        tok = card.get("THUNDERSTORE_API_TOKEN")
        if tok:
            return tok
    die("THUNDERSTORE_API_TOKEN missing (env or box-secrets)")


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def zip_pack(dest: Path, files: list[tuple[str, Path]]) -> None:
    if dest.exists():
        dest.unlink()
    with zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED) as z:
        for arc, src in files:
            if not src.exists():
                die(f"missing {src}")
            z.write(src, arc)
    print(f"OK {dest.name} ({dest.stat().st_size} bytes)")


def build_packs(version: str) -> dict[str, Path]:
    DIST.mkdir(parents=True, exist_ok=True)
    # Prefer thunderstore staged DLLs; else copy from bin/Release
    server_dll = TS_SERVER / "FactionTactics.dll"
    client_dll = TS_CLIENT / "FactionTactics.Client.dll"
    if not server_dll.exists():
        cand = REPO / "plugin" / "bin" / "Release" / "FactionTactics.dll"
        if cand.exists():
            server_dll.write_bytes(cand.read_bytes())
    if not client_dll.exists():
        cand = REPO / "client" / "bin" / "Release" / "FactionTactics.Client.dll"
        if cand.exists():
            client_dll.write_bytes(cand.read_bytes())
    if not server_dll.exists() or not client_dll.exists():
        die("DLLs missing — run build first (ft-build / dotnet Release)")

    icon = ICON if ICON.exists() else TS_SERVER / "icon.png"
    if not icon.exists():
        die("thunderstore/icon.png missing")

    # Ensure icon in both package dirs
    for d in (TS_CLIENT, TS_SERVER):
        (d / "icon.png").write_bytes(icon.read_bytes())

    readme = TS_SERVER / "README.md"
    if not readme.exists():
        readme = DIST / "INSTALL.md"

    outs = {
        "server_ts": DIST / "FactionTactics-Server-Thunderstore.zip",
        "client_ts": DIST / "FactionTactics-Client-Thunderstore.zip",
        "server": DIST / "FactionTactics-Server.zip",
        "client": DIST / "FactionTactics-Client.zip",
    }
    zip_pack(
        outs["server_ts"],
        [
            ("manifest.json", TS_SERVER / "manifest.json"),
            ("README.md", TS_SERVER / "README.md"),
            ("icon.png", TS_SERVER / "icon.png"),
            ("FactionTactics.dll", server_dll),
        ],
    )
    zip_pack(
        outs["client_ts"],
        [
            ("manifest.json", TS_CLIENT / "manifest.json"),
            ("README.md", TS_CLIENT / "README.md"),
            ("icon.png", TS_CLIENT / "icon.png"),
            ("FactionTactics.Client.dll", client_dll),
        ],
    )
    # Plain zips
    install = DIST / "INSTALL.md"
    zip_pack(
        outs["server"],
        [
            ("FactionTactics.dll", server_dll),
            ("INSTALL.md", install if install.exists() else TS_SERVER / "README.md"),
        ],
    )
    zip_pack(
        outs["client"],
        [
            ("FactionTactics.Client.dll", client_dll),
            ("INSTALL.md", install if install.exists() else TS_CLIENT / "README.md"),
        ],
    )
    sums = DIST / "SHA256SUMS.txt"
    lines = []
    for p in sorted(DIST.glob("FactionTactics-*.zip")):
        lines.append(f"{sha256(p)}  {p.name}")
    sums.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"OK {sums.name}")
    return outs


def github_release(version: str, outs: dict[str, Path], dry: bool) -> None:
    tag = f"v{version}"
    assets = [
        outs["server"],
        outs["client"],
        outs["server_ts"],
        outs["client_ts"],
        DIST / "INSTALL.md",
        DIST / "SHA256SUMS.txt",
    ]
    assets = [a for a in assets if a.exists()]
    notes = f"""Faction Tactics {version}

Hybrid server commander + client combat executor. Same version on both sides.

Thunderstore: FactionTactics_Server / FactionTactics_Client under team FactionTactics.
"""
    if dry:
        print(f"[dry-run] gh release {tag} assets={[a.name for a in assets]}")
        return
    # Create or upload-to-existing
    view = subprocess.run(
        ["gh", "release", "view", tag, "--repo", GH_REPO],
        cwd=str(REPO),
        capture_output=True,
        text=True,
    )
    if view.returncode != 0:
        cmd = [
            "gh", "release", "create", tag,
            "--repo", GH_REPO,
            "--title", f"Faction Tactics {version}",
            "--notes", notes,
        ] + [str(a) for a in assets]
        run(cmd)
    else:
        for a in assets:
            run(["gh", "release", "upload", tag, str(a), "--repo", GH_REPO, "--clobber"])
    print(f"OK GitHub https://github.com/{GH_REPO}/releases/tag/{tag}")


def api(token: str, method: str, path: str, body=None, timeout: int = 180):
    data = None if body is None else json.dumps(body).encode()
    headers = {"Authorization": f"Bearer {token}", "User-Agent": "FactionTactics-Publish/1.0"}
    if body is not None:
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(
        f"https://thunderstore.io{path}", data=data, method=method, headers=headers
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            raw = resp.read()
            return resp.status, (json.loads(raw.decode()) if raw else {})
    except urllib.error.HTTPError as e:
        return e.code, {"error": e.read().decode(errors="replace")[:2000]}


def thunderstore_publish(zip_path: Path, categories: list[str], token: str, dry: bool) -> None:
    print(f"Thunderstore ← {zip_path.name}")
    if dry:
        print(f"[dry-run] categories={categories}")
        return
    code, init = api(
        token,
        "POST",
        "/api/experimental/usermedia/initiate-upload/",
        {"filename": zip_path.name, "file_size_bytes": zip_path.stat().st_size},
    )
    if code not in (200, 201):
        die(f"initiate-upload failed: {code} {init}")
    uid = init["user_media"]["uuid"]
    blob = zip_path.read_bytes()
    finished = []
    for part in init["upload_urls"]:
        put = urllib.request.Request(
            part["url"],
            data=blob,
            method="PUT",
            headers={"Content-Type": "application/octet-stream", "User-Agent": "FactionTactics-Publish/1.0"},
        )
        with urllib.request.urlopen(put, timeout=180) as resp:
            finished.append({"PartNumber": part["part_number"], "ETag": resp.headers.get("ETag")})
    code, _ = api(token, "POST", f"/api/experimental/usermedia/{uid}/finish-upload/", {"parts": finished})
    if code >= 400:
        die(f"finish-upload failed: {code}")
    body = {
        "author_name": TEAM,
        "communities": ["valheim"],
        "has_nsfw_content": False,
        "upload_uuid": uid,
        "categories": categories,
        "community_categories": {"valheim": categories},
    }
    code, sub = api(token, "POST", "/api/experimental/submission/submit/", body)
    if code >= 400:
        die(f"submit failed: {code} {sub}")
    pv = sub.get("package_version") or {}
    print(f"OK {pv.get('full_name')} {pv.get('download_url')}")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--skip-build", action="store_true", help="Do not run dotnet build")
    ap.add_argument("--github-only", action="store_true")
    ap.add_argument("--thunderstore-only", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--version", help="Override version (default: Plugin.cs)")
    args = ap.parse_args()

    version = args.version or read_version()
    print(f"version={version}")
    sync_manifest_versions(version)

    if not args.skip_build and not args.thunderstore_only:
        # Prefer repo build scripts if present
        build_sh = Path("/home/box/sand-data/agents/5dfc8cba-19f8-4da6-a155-a3c011796b89/bin/ft-build.sh")
        if build_sh.exists():
            run(["bash", str(build_sh)])
        else:
            run(["dotnet", "build", "plugin/FactionTactics.csproj", "-c", "Release", "-v:q"])
            run(["dotnet", "build", "client/FactionTactics.Client.csproj", "-c", "Release", "-v:q"])
        # Stage DLLs into thunderstore folders
        srv = REPO / "plugin" / "bin" / "Release" / "FactionTactics.dll"
        cli = REPO / "client" / "bin" / "Release" / "FactionTactics.Client.dll"
        if srv.exists():
            (TS_SERVER / "FactionTactics.dll").write_bytes(srv.read_bytes())
        if cli.exists():
            (TS_CLIENT / "FactionTactics.Client.dll").write_bytes(cli.read_bytes())

    outs = build_packs(version)

    do_gh = not args.thunderstore_only
    do_ts = not args.github_only
    if do_gh:
        github_release(version, outs, args.dry_run)
    if do_ts:
        tok = thunderstore_token()
        thunderstore_publish(
            outs["server_ts"], ["server-side", "enemies", "tweaks"], tok, args.dry_run
        )
        thunderstore_publish(
            outs["client_ts"], ["client-side", "enemies", "tweaks"], tok, args.dry_run
        )
    print("done.")


if __name__ == "__main__":
    main()
