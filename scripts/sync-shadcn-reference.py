#!/usr/bin/env python3
"""Download upstream shadcn Base UI reference files, NOT install npm or alter WPF UI.

Usage:
  python scripts/sync-shadcn-reference.py
  python scripts/sync-shadcn-reference.py --verify
  python scripts/sync-shadcn-reference.py --update

Registry payloads are live snapshots, while example/stylesheet files are pinned to
an upstream git SHA. Every downloaded byte is hashed in manifest.json. Repository
content is untrusted reference data and must not be executed as part of this script.
"""
from __future__ import annotations

import argparse
import concurrent.futures as futures
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import tempfile
from collections import Counter
from urllib.parse import quote

import requests

REPO = Path(__file__).resolve().parent.parent
CATALOG = REPO / "docs/yanzi-ui-official-catalog/official-reference-snapshot.json"
TARGET = REPO / "docs/yanzi-ui-official-catalog/reference-package"
REGISTRY = "https://ui.shadcn.com/r/styles"
API = "https://api.github.com/repos/shadcn-ui/ui/commits/main"
RAW = "https://raw.githubusercontent.com/shadcn-ui/ui"
TIMEOUT = 25
EXPECTED = 64
# Official documentation content built by composition or typography rules, no standalone item.
COMPOSITIONS = {"data-table", "date-picker", "typography"}
NO_EXAMPLES = {"data-table", "date-picker", "direction", "typography"}


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def fetch(url: str) -> bytes:
    response = requests.get(url, timeout=TIMEOUT, headers={
        "Accept": "application/json" if url == API else "*/*",
        "User-Agent": "Yanzi-UI-Reference-Snapshot/1.0"
    })
    response.raise_for_status()
    if len(response.content) > 3_000_000:
        raise RuntimeError(f"Unexpectedly large individual source: {url}")
    return response.content


def stage_file(directory: Path, relative: str, content: bytes, checksums: dict[str, str]) -> None:
    path = directory / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)
    checksums[relative.replace("\\", "/")] = sha256(content)


def load_catalog() -> list[dict]:
    catalog = json.loads(CATALOG.read_text(encoding="utf-8-sig"))
    components = catalog.get("items", [])
    if len(components) != EXPECTED or len({item["name"] for item in components}) != EXPECTED:
        raise RuntimeError("Local 64-component official catalog is incomplete.")
    return components


def audit(directory: Path) -> dict:
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    expected_paths = manifest["checksums"]
    mismatches = []
    for path, checksum in expected_paths.items():
        file = directory / path
        if not file.is_file() or sha256(file.read_bytes()) != checksum:
            mismatches.append(path)
    if mismatches:
        raise RuntimeError(f"Reference file missing or modified: {', '.join(mismatches[:12])}")
    entries = manifest["components"]
    if len(entries) != EXPECTED or len({i["slug"] for i in entries}) != EXPECTED:
        raise RuntimeError("64 official component entries required.")
    for component in entries:
        if component["kind"] == "registry":
            data = json.loads((directory / component["registry_path"]).read_text(encoding="utf-8"))
            if not data.get("files") or data.get("name") != component["slug"]:
                raise RuntimeError("Corrupted component registry item: " + component["slug"])
        elif component["slug"] not in COMPOSITIONS:
            raise RuntimeError("Unexpected missing registry item: " + component["slug"])
    if manifest["downloaded_registry"] != EXPECTED - len(COMPOSITIONS):
        raise RuntimeError("Registry count does not match official baseline.")
    if manifest["downloaded_examples"] != EXPECTED - len(NO_EXAMPLES):
        raise RuntimeError("Example count does not match official baseline.")
    themes = json.loads((directory / "theme/design-tokens.json").read_text(encoding="utf-8"))
    if len(themes["light"]) < 15 or len(themes["dark"]) < 15:
        raise RuntimeError("Theme token extraction is incomplete.")
    print("VERIFIED:", len(entries), "catalog items;",
          manifest["downloaded_registry"], "registry packages;",
          manifest["downloaded_examples"], "pinned examples;",
          len(expected_paths), "SHA256-checked files")
    return manifest


def css_tokens(css: str, selector: str) -> dict[str, str]:
    match = re.search(re.escape(selector) + r"\s*\{", css)
    if not match:
        raise RuntimeError(f"Missing CSS token section {selector}")
    rest = css[match.end():]
    # shadcn default globals CSS places primitive declarations in non-nested blocks.
    chunk = rest.split("}", 1)[0]
    return dict(re.findall(r"(?m)^\s*(--[\w-]+)\s*:\s*([^;]+);", chunk))


def download_item(item: dict, commit: str, style: str) -> tuple[dict, dict[str, bytes]]:
    name = item["name"]
    slug = name.lower().replace(" ", "-")
    result = {
        "name": name, "slug": slug,
        "official_docs": item["official_url"],
        "wpf_api": item["api"],
        "kind": "composition" if slug in COMPOSITIONS else "registry",
        "registry_path": None,
        "example_path": None,
        "registry_dependencies": [],
        "npm_dependencies": [],
    }
    files: dict[str, bytes] = {}
    if slug not in COMPOSITIONS:
        url = f"{REGISTRY}/{style}/{quote(slug)}.json"
        content = fetch(url)
        payload = json.loads(content)
        if payload.get("name") != slug or not payload.get("files"):
            raise RuntimeError("Unexpected upstream registry response: " + url)
        # Preserve registry JSON including original TSX source, links, dependency list.
        path = f"registry/{slug}.json"
        files[path] = content
        result["registry_path"] = path
        result["registry_url"] = url
        result["registry_dependencies"] = payload.get("registryDependencies", [])
        result["npm_dependencies"] = payload.get("dependencies", [])
        result["source_files"] = [x["path"] for x in payload["files"]]
    else:
        result["note"] = "Official composition or typography docs; no standalone base-nova registry package."

    if slug not in NO_EXAMPLES:
        example_repo_path = f"apps/v4/registry/bases/base/examples/{slug}-example.tsx"
        example_url = f"{RAW}/{commit}/{example_repo_path}"
        raw = fetch(example_url)
        path = f"examples/{slug}-example.tsx"
        files[path] = raw
        result["example_path"] = path
        result["example_url"] = example_url
    else:
        result["example_note"] = "No single -example.tsx in the official base examples directory."
    return result, files


def sync(update: bool, style: str) -> None:
    if TARGET.exists():
        if not update:
            print("Reference already exists. Run --verify or pass --update to explicitly refresh.")
            audit(TARGET)
            return
        audit(TARGET)  # Protect edits to reference files, do not silently discard them.

    source_items = load_catalog()
    upstream = json.loads(fetch(API))
    commit = upstream["sha"]
    if not re.fullmatch(r"[a-f0-9]{40}", commit):
        raise RuntimeError("Invalid upstream commit reference")

    # Work in a temporary folder, never expose a partial package as authoritative.
    TARGET.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="shadcn-upstream-", dir=TARGET.parent) as temporary:
        folder = Path(temporary) / "reference-package"
        folder.mkdir()
        checksums: dict[str, str] = {}
        with futures.ThreadPoolExecutor(max_workers=8) as executor:
            batches = list(executor.map(lambda x: download_item(x, commit, style), source_items))
        components = []
        for descriptor, files in batches:
            components.append(descriptor)
            for path, data in files.items():
                stage_file(folder, path, data, checksums)

        extra = {
            "theme/globals.css": f"{RAW}/{commit}/apps/v4/app/globals.css",
            "theme/components.json": f"{RAW}/{commit}/apps/v4/components.json",
            "LICENSE.md": f"{RAW}/{commit}/LICENSE.md",
        }
        for path, url in extra.items():
            stage_file(folder, path, fetch(url), checksums)
        css = (folder / "theme/globals.css").read_text(encoding="utf-8")
        tokens = {
            "source": extra["theme/globals.css"],
            "notes": ("CSS values, not native WPF colors. Some use oklch() and var() "
                      "and require an explicit semantic conversion before WPF use."),
            "light": css_tokens(css, ":root"),
            "dark": css_tokens(css, ".dark"),
            "theme_inline": css_tokens(css, "@theme inline"),
        }
        stage_file(folder, "theme/design-tokens.json",
                   (json.dumps(tokens, indent=2, ensure_ascii=False) + "\n").encode(), checksums)

        manifest = {
            "project": "shadcn/ui",
            "official_repository": "https://github.com/shadcn-ui/ui",
            "upstream_sha": commit,
            "upstream_commit_url": f"https://github.com/shadcn-ui/ui/commit/{commit}",
            "registry_style": style,
            "registry_mode": "live immutable local snapshot",
            "reference_timestamp_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
            "design": "Base UI / Tailwind design reference, not WPF-executable code",
            "catalog_count": EXPECTED,
            "downloaded_registry": sum(x["registry_path"] is not None for x in components),
            "downloaded_examples": sum(x["example_path"] is not None for x in components),
            "registry_compositions_without_single_package": sorted(COMPOSITIONS),
            "examples_without_single_file": sorted(NO_EXAMPLES),
            "components": components,
            "checksums": checksums,
        }
        (folder / "manifest.json").write_text(
            json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        audit(folder)

        backup = TARGET.with_name(TARGET.name + ".previous")
        if backup.exists():
            raise RuntimeError(f"An earlier backup exists, please review before update: {backup}")
        if TARGET.exists():
            TARGET.rename(backup)
        try:
            os.replace(folder, TARGET)
        except Exception:
            if backup.exists():
                backup.rename(TARGET)
            raise
        if backup.exists():
            shutil.rmtree(backup)
        print("DOWNLOADED:", str(TARGET))
        print("UPSTREAM COMMIT:", commit)
        print("STYLES:", style, "(live JSON locally SHA256-pinned; examples pinned to git commit)")
        print("Missing registry items are explicitly tracked as compositions, not fake successes.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", action="store_true", help="Offline integrity verification")
    parser.add_argument("--update", action="store_true", help="Refresh existing package after integrity check")
    parser.add_argument("--style", default="base-nova",
                        choices=["base-nova", "base-vega", "base-maia", "base-mira"],
                        help="Choose one official Base UI style reference")
    args = parser.parse_args()
    if args.verify:
        audit(TARGET)
    else:
        sync(args.update, args.style)


if __name__ == "__main__":
    main()
