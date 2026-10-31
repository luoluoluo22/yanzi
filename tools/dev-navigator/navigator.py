#!/usr/bin/env python3
"""Yanzi source navigator: incremental, read-only XAML/C# feature and test routing."""
from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import time

SCHEMA = 1
SOURCE_EXTS = {".cs", ".xaml", ".ps1", ".csproj"}
EVENTS = ("Click", "Loaded", "Unloaded", "SelectionChanged", "TextChanged", "Checked",
          "Unchecked", "MouseDown", "MouseUp", "PreviewMouseDown", "PreviewMouseUp",
          "KeyDown", "KeyUp", "PreviewKeyDown", "SizeChanged", "Closing", "Opened")
TAG_RE = re.compile(r"<(?P<tag>[A-Za-z_][\w.:-]*)\b(?P<attrs>[^<>]*?)>", re.S)
ATTR_RE = re.compile(r"""(?P<name>[\w:.-]+)\s*=\s*(?P<quote>["'])(?P<value>.*?)(?P=quote)""", re.S)
METHOD_RE = re.compile(
    r"(?m)^[ \t]*(?:(?:public|private|internal|protected|static|async|virtual|override|"
    r"sealed|new|partial|extern)\s+){1,5}[\w<>,.?\[\] \t]+\s+"
    r"(?P<name>[A-Za-z_]\w*)\s*\(")
UI_LABEL_KEYS = ("Text", "Content", "Header", "ToolTip", "Title", "PlaceholderText")
IGNORE_SEGMENTS = {".git", "node_modules", "bin", "obj", ".artifacts", "third_party"}
MAX_BYTES = 1_200_000

def git(root: Path, *args: str) -> str:
    proc = subprocess.run(["git", "-C", str(root), *args], capture_output=True, check=True)
    return proc.stdout.decode("utf-8", "replace")

def tracked_files(root: Path) -> list[str]:
    raw = subprocess.run(["git", "-C", str(root), "ls-files", "-z", "--",
                          "src", "tests", "scripts", "tools"], check=True,
                         capture_output=True).stdout
    items = []
    for b in raw.split(b"\0"):
        if not b:
            continue
        name = b.decode("utf-8", "replace").replace("\\", "/")
        path = Path(name)
        if path.suffix.lower() not in SOURCE_EXTS or any(part in IGNORE_SEGMENTS for part in path.parts):
            continue
        if (root / path).is_file():
            items.append(name)
    return sorted(items)

def line_number(text: str, offset: int) -> int:
    return text.count("\n", 0, offset) + 1

def parse_file(name: str, content: str) -> dict:
    result = {"symbols": [], "ui": [], "references": []}
    if name.lower().endswith(".xaml"):
        for match in TAG_RE.finditer(content):
            attrs = {a.group("name"): a.group("value")
                     for a in ATTR_RE.finditer(match.group("attrs"))}
            if not attrs:
                continue
            handlers = {key: attrs[key] for key in EVENTS if key in attrs and
                        re.fullmatch(r"[A-Za-z_]\w*", attrs[key])}
            labels = {key: re.sub(r"\s+", " ", attrs[key])[:100]
                      for key in UI_LABEL_KEYS if key in attrs and
                      not ("{" in attrs[key] or "}" in attrs[key])}
            control_name = attrs.get("x:Name") or attrs.get("Name") or ""
            if control_name or handlers or labels:
                result["ui"].append({"line": line_number(content, match.start()),
                                     "tag": match.group("tag"),
                                     "name": control_name,
                                     "handlers": handlers, "labels": labels})
        result["ui"] = result["ui"][:4000]
    elif name.lower().endswith(".cs"):
        result["symbols"] = [{"name": m.group("name"), "line": line_number(content, m.start())}
                             for m in METHOD_RE.finditer(content)][:1200]
    # Only extract test project references, not test bodies or arbitrary source text.
    if name.endswith(".csproj"):
        result["references"] = re.findall(r'(?:ProjectReference|Compile)\s+Include="([^"]+)"', content)[:150]
    return result

def cache_path(root: Path) -> Path:
    base = os.environ.get("LOCALAPPDATA")
    location = Path(base) if base else Path(tempfile.gettempdir())
    key = hashlib.sha256(str(root.resolve()).casefold().encode("utf-8")).hexdigest()[:16]
    return location / "OpenQuickHost" / "DevNavigator" / key / "index.json"

def load_cache(path: Path) -> dict:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        if data.get("schema") == SCHEMA and isinstance(data.get("files"), dict):
            return data
    except (OSError, ValueError, TypeError):
        pass
    return {"schema": SCHEMA, "files": {}}

def build_index(root: Path, *, force: bool = False, cache_file: Path | None = None) -> dict:
    cache_file = cache_file or cache_path(root)
    previous = load_cache(cache_file)
    names = tracked_files(root)
    result = {}
    changed = 0
    start = time.perf_counter()
    for name in names:
        path = root / name
        try:
            st = path.stat()
            if st.st_size > MAX_BYTES:
                continue
            old = previous["files"].get(name)
            if (not force and old and old.get("mtime_ns") == st.st_mtime_ns and
                old.get("size") == st.st_size):
                result[name] = old
                continue
            value = parse_file(name, path.read_text(encoding="utf-8-sig", errors="replace"))
            result[name] = {"mtime_ns": st.st_mtime_ns, "size": st.st_size, **value}
            changed += 1
        except (OSError, UnicodeError):
            continue
    data = {"schema": SCHEMA, "files": result, "stats": {
        "files": len(result), "scanned": changed, "reused": len(result) - changed,
        "elapsedMs": round((time.perf_counter() - start)*1000, 1)}}
    cache_file.parent.mkdir(parents=True, exist_ok=True)
    fd, temp = tempfile.mkstemp(prefix="index-", suffix=".tmp", dir=str(cache_file.parent))
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as output:
            json.dump(data, output, ensure_ascii=False, separators=(",", ":"))
        os.replace(temp, cache_file)
    finally:
        if os.path.exists(temp):
            os.unlink(temp)
    return data

def features_from(root: Path) -> list[dict]:
    path = root / "tools" / "dev-navigator" / "features.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    return data["features"]

def pattern_match(path: str, patterns: list[str]) -> bool:
    return any(fnmatch.fnmatchcase(path.casefold(), p.casefold()) for p in patterns)

def enriched_feature(feature: dict, index: dict) -> dict:
    patterns = feature.get("paths", [])
    matches = [name for name in index["files"] if pattern_match(name, patterns)]
    # XAML event handlers provide direct grounded navigation to implementation methods.
    handler_names = set(feature.get("handlers", []))
    for name, data in index["files"].items():
        if name.endswith(".xaml"):
            if any(any(h in handler_names for h in ui["handlers"].values())
                   for ui in data["ui"]):
                matches.append(name)
        elif name.endswith(".cs") and handler_names:
            if any(symbol["name"] in handler_names for symbol in data["symbols"]):
                matches.append(name)
    output = {k: feature[k] for k in ("id", "title", "aliases") if k in feature}
    output["files"] = sorted(set(matches))
    output["tests"] = sorted(set(name for name in index["files"]
                                 if pattern_match(name, feature.get("tests", []))
                                 and (name.endswith(".csproj") or name.endswith(".ps1"))))
    output["handlers"] = sorted(handler_names)
    return output

def score_feature(query: str, feature: dict) -> int:
    q = query.casefold().strip()
    if not q:
        return 0
    names = [feature["title"], feature["id"]] + feature.get("aliases", [])
    scores = []
    for text in names:
        candidate = text.casefold()
        if q == candidate:
            scores.append(120)
        elif candidate in q:
            scores.append(85)
        elif q in candidate and len(q) >= 2:
            scores.append(65)
    # Fragments help natural phrasing without treating one generic character as proof.
    parts = [t for t in re.split(r"[\s,，、/：:（）()]+", q) if len(t) >= 2]
    for part in parts:
        if any(part in candidate.casefold() for candidate in names):
            scores.append(20)
    return max(scores, default=0)

def search_features(query: str, index: dict, features: list[dict], limit: int = 5) -> list[dict]:
    results = []
    for feature in features:
        score = score_feature(query, feature)
        if score:
            results.append({"score": score, **enriched_feature(feature, index)})
    if not results:
        # An unknown feature can still be grounded in an actual XAML control.
        # Do not infer tests or fabricate a feature owner from fuzzy text alone.
        for control in ui_lookup(query, index, limit=limit):
            files = [control["path"]] + [d["path"] for d in control["definitions"]]
            results.append({"score": min(control["score"], 55), "id": "ui-evidence",
                            "title": "XAML 控件候选（未登记业务映射）",
                            "aliases": [], "files": sorted(set(files)), "tests": [],
                            "handlers": list(control["handlers"].values()),
                            "evidence": {"path": control["path"], "line": control["line"]}})
    return sorted(results, key=lambda x: (-x["score"], x["title"]))[:limit]

def ui_lookup(query: str, index: dict, limit: int = 15) -> list[dict]:
    q = query.casefold().strip()
    if not q:
        return []
    matches = []
    for path, data in index["files"].items():
        for control in data["ui"]:
            corpus = [control["name"], *control["handlers"].values(),
                      *control["labels"].values()]
            score = 100 if any(q == value.casefold() for value in corpus) else (
                55 if any(q in value.casefold() for value in corpus) else 0)
            if score:
                handlers = list(control["handlers"].values())
                definitions = [{"path": cs_path, "line": sym["line"]}
                               for cs_path, info in index["files"].items()
                               if cs_path.endswith(".cs")
                               for sym in info["symbols"] if sym["name"] in handlers]
                matches.append({"score": score, "path": path, **control,
                                "definitions": definitions[:12]})
    return sorted(matches, key=lambda x: (-x["score"], x["path"], x["line"]))[:limit]

def normalize_paths(root: Path, paths: list[str]) -> list[str]:
    out = []
    for value in paths:
        s = value.replace("\\", "/").strip()
        p = Path(s)
        if p.is_absolute():
            try: s = p.resolve().relative_to(root.resolve()).as_posix()
            except ValueError: continue
        if s.startswith("../") or s.startswith("/"):
            continue
        if s:
            out.append(s)
    return sorted(set(out))

def changed_paths(root: Path, base: str) -> list[str]:
    if base.startswith("-"):
        raise ValueError("invalid Git diff revision")
    names = git(root, "diff", "--name-only", "--diff-filter=ACMR", base, "--")
    return normalize_paths(root, names.splitlines())

def impact(root: Path, paths: list[str], index: dict, features: list[dict]) -> dict:
    changed = normalize_paths(root, paths)
    affected = []
    tests = set()
    for feature in features:
        enriched = enriched_feature(feature, index)
        overlap = sorted(set(changed) & set(enriched["files"]))
        if overlap:
            affected.append({"id": feature["id"], "title": feature["title"],
                             "matchedFiles": overlap, "suggestedTests": enriched["tests"]})
            tests.update(enriched["tests"])
    matched = set(f for f in changed for item in affected for f in item["matchedFiles"])
    # Tests for changed files outside curated features are not silently considered covered.
    return {"changed": changed, "affected": affected, "suggestedTests": sorted(tests),
            "unmapped": sorted(set(changed)-matched),
            "note": "Heuristic routing only; review shared dependencies and perform UI QA where appropriate."}

def format_output(command: str, payload) -> None:
    if command == "index":
        print(json.dumps(payload, ensure_ascii=False))
    elif command == "find":
        if not payload:
            print("NO_CONFIDENT_FEATURE: Try an exact UI label or handler via 'ui'.")
        for item in payload:
            print(f"{item['title']} [{item['id']}] score={item['score']}")
            for f in item["files"][:18]: print("  FILE", f)
            for f in item["tests"][:10]: print("  TEST", f)
    elif command == "ui":
        if not payload: print("NO_UI_MATCH")
        for item in payload:
            print(f"{item['path']}:{item['line']} {item['tag']} "
                  f"name={item['name']} handlers={item['handlers']} labels={item['labels']}")
            for d in item["definitions"]: print(f"  HANDLER {d['path']}:{d['line']}")
    elif command == "impact":
        print("CHANGED", ", ".join(payload["changed"]))
        for entry in payload["affected"]:
            print(f"FEATURE {entry['title']}: {', '.join(entry['matchedFiles'])}")
        for t in payload["suggestedTests"]: print("TEST", t)
        for f in payload["unmapped"]: print("REVIEW_UNMAPPED", f)
        print(payload["note"])

def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Incremental XAML/C# developer navigation.")
    parser.add_argument("mode", choices=["index", "find", "ui", "impact"])
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--query", default="")
    parser.add_argument("--path", dest="paths", action="append", default=[])
    parser.add_argument("--base", default="", help="Git revision for changed-file impact")
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    root = args.repo_root.resolve()
    try:
        index = build_index(root, force=args.force)
        features = features_from(root)
        if args.mode == "index":
            payload = index["stats"]
        elif args.mode == "find":
            payload = search_features(args.query, index, features)
        elif args.mode == "ui":
            payload = ui_lookup(args.query, index)
        else:
            paths = args.paths + (changed_paths(root, args.base) if args.base else [])
            payload = impact(root, paths, index, features)
        if args.json: print(json.dumps(payload, ensure_ascii=False, indent=2))
        else: format_output(args.mode, payload)
        return 0
    except (OSError, ValueError, subprocess.CalledProcessError) as exc:
        print(f"DEV_NAV_ERROR: {exc}", file=sys.stderr)
        return 2

if __name__ == "__main__":
    raise SystemExit(main())
