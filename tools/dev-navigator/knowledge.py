#!/usr/bin/env python3
"""Read-only UI coverage discovery and locally scoped, run-provenanced QA ledger.

Only explicit execution returns from the safe preflight runner are ingested; manual
"passed" assertions, inferred links, and installed-app UI success are never synthesized.
"""
from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile

import navigator

VERSION = 1
LEDGER_LIMIT = 250
EXPECTED_SCOPES = {
    "targeted": "isolated-dotnet-test",
    "hiddenUiSmoke": "isolated-hidden-desktop-wpf",
}


def revision(root: Path) -> dict:
    head = navigator.git(root, "rev-parse", "HEAD").strip()
    dirty = bool(navigator.git(root, "status", "--porcelain", "--untracked-files=normal").strip())
    return {"commit": head, "dirty": dirty}


def ledger_path(root: Path) -> Path:
    basedir = Path(os.environ.get("LOCALAPPDATA") or tempfile.gettempdir())
    # All worktrees of the same Git repository share the QA ledger. The cache
    # for parsed source remains worktree-local, but execution evidence is repo-wide.
    git_common = navigator.git(root, "rev-parse", "--git-common-dir").strip()
    common_path = Path(git_common)
    if not common_path.is_absolute():
        common_path = root / common_path
    key = hashlib.sha256(str(common_path.resolve()).casefold().encode("utf-8")).hexdigest()[:16]
    return basedir / "OpenQuickHost" / "DevNavigator" / key / "qa-ledger.json"


def load_ledger(path: Path) -> list[dict]:
    if not path.exists():
        return []
    obj = json.loads(path.read_text(encoding="utf-8"))
    if (not isinstance(obj, dict) or obj.get("schema") != VERSION or
            not isinstance(obj.get("records"), list)):
        raise ValueError("Unrecognized QA ledger; refusing to overwrite")
    return obj["records"]


def save_ledger(path: Path, records: list[dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    # Local cache is intentionally outside the repository and never synchronized.
    temp_path = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", delete=False,
                                         dir=path.parent, prefix="qa-", suffix=".tmp") as fp:
            temp_path = Path(fp.name)
            json.dump({"schema": VERSION, "records": records[-LEDGER_LIMIT:]}, fp,
                      ensure_ascii=False, separators=(",", ":"))
        os.replace(temp_path, path)
    finally:
        if temp_path and temp_path.exists():
            temp_path.unlink()


def record_executed(root: Path, plan: dict, execution: dict, *,
                    ledger: Path | None = None, source: dict | None = None) -> list[dict]:
    """Append only results produced by preflight's explicit test execution branch.

    This function is called exclusively after run_opt_in_tests returned. The ledger
    distinguishes isolated smoke/tests and does not claim a real installed UI check.
    """
    source = source or revision(root)
    feature_ids = [x for x in plan["source"]["mappedFeatureIds"] if x != "ui-evidence"]
    if not feature_ids:
        return []
    permitted = {
        item["path"]: item["command"]
        for item in plan["validation"]["tests"] if item["execution"] == "safe-targeted"
    }
    permitted_hidden = plan["validation"]["hiddenUiSmoke"].get("command")
    out: list[dict] = []
    now = datetime.now(timezone.utc).isoformat()
    for item in execution.get("targeted", []):
        path = item.get("name")
        if path not in permitted or item.get("status") not in {"passed", "failed", "error"}:
            continue
        out.append({
            "atUtc": now, "features": feature_ids, "scope": EXPECTED_SCOPES["targeted"],
            "test": path, "status": item["status"], "commit": source["commit"],
            "dirty": source["dirty"], "exitCode": item.get("exitCode"),
        })
    hidden = execution.get("hiddenUiSmoke")
    if (permitted_hidden and isinstance(hidden, dict)
            and hidden.get("status") in {"passed", "failed", "error"}):
        out.append({
            "atUtc": now, "features": feature_ids,
            "scope": EXPECTED_SCOPES["hiddenUiSmoke"],
            "test": plan["validation"]["hiddenUiSmoke"].get("scenario") or "shared-ui",
            "status": hidden["status"], "commit": source["commit"],
            "dirty": source["dirty"], "exitCode": hidden.get("exitCode"),
        })
    if out:
        path = ledger or ledger_path(root)
        records = load_ledger(path)
        save_ledger(path, records + out)
    return out


def qa_status(root: Path, feature_ids: list[str], *,
              ledger: Path | None = None, source: dict | None = None) -> dict:
    source = source or revision(root)
    path = ledger or ledger_path(root)
    records = load_ledger(path)
    result = {}
    for feature in feature_ids:
        events = [x for x in records if feature in x.get("features", [])]
        latest: dict[str, dict] = {}
        for event in events:
            scope = event.get("scope")
            key = (scope, event.get("test"))
            if key not in latest or str(event.get("atUtc", "")) >= str(latest[key].get("atUtc", "")):
                latest[key] = event
        statuses = []
        for (scope, _test), event in sorted(latest.items()):
            fresh = not source["dirty"] and not event.get("dirty", True) and (
                event.get("commit") == source["commit"])
            statuses.append({
                "scope": scope, "test": event.get("test"), "status": event.get("status"),
                "freshForCurrentCommit": fresh, "atUtc": event.get("atUtc"),
                "commit": str(event.get("commit", ""))[:12],
                "verifiedInstalledAppUi": False,
            })
        result[feature] = {
            "records": statuses,
            "isolatedChecksCurrent": bool(statuses) and all(
                e["freshForCurrentCommit"] and e["status"] == "passed" for e in statuses),
            "installedAppUi": "not-verified",
        }
    return result


def discover_ui(index: dict, features: list[dict], limit: int = 30, query: str = "") -> dict:
    """Produce *candidates* only, never automatically promote to business ownership."""
    mapped_handlers = {
        h for feature in features for h in feature.get("handlers", [])
    }
    # The exact owning XAML file itself is insufficient evidence: one large WPF
    # window contains numerous unrelated features.
    mapped = []
    unmapped = []
    for path, data in index["files"].items():
        if not path.endswith(".xaml"):
            continue
        for control in data.get("ui", []):
            handlers = sorted(set(control.get("handlers", {}).values()))
            if not handlers or not (control.get("name") or control.get("labels")):
                continue
            evidence = {
                "path": path, "line": control["line"], "name": control.get("name", ""),
                "handlers": handlers,
                "labels": control.get("labels", {}),
            }
            (mapped if set(handlers).intersection(mapped_handlers) else unmapped).append(evidence)
    unmapped.sort(key=lambda x: (x["path"], x["line"], x["name"]))
    q = query.strip().casefold()
    matching = [x for x in unmapped if not q or q in " ".join([
        x["path"], x["name"], *x["handlers"], *x["labels"].values()
    ]).casefold()]
    return {
        "scope": "XAML named/labeled controls with explicit event handlers only",
        "knownHandlersMapped": len(mapped),
        "unmappedCandidates": len(unmapped),
        "mappedFraction": round(len(mapped) / max(1, len(mapped) + len(unmapped)), 4),
        "filter": query,
        "matchedCandidates": len(matching),
        "candidates": matching[:max(0, min(limit, 100))],
        "note": "Candidate relationships need manual review; no automatic update of features.json.",
    }
