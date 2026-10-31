#!/usr/bin/env python3
"""Fail-closed path classifier for Yanzi Engineering foundation CI.

Only *exclusively* approved, independent development navigation/diagnosis files
may take the fast lane. Anything else (including classifier/CI policy changes)
runs the complete existing Windows, worker and SDK suite.
"""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import re
import subprocess
import sys

EXACT = frozenset({
    "scripts/dev-locate.ps1",
    "scripts/dev-preflight.ps1",
    "scripts/dev-knowledge.ps1",
    "scripts/dev-diagnose.ps1",
    "scripts/test-dev-navigator.ps1",
    "scripts/test-dev-preflight.ps1",
    "scripts/test-dev-knowledge.ps1",
    "scripts/test-dev-diagnose.ps1",
    "docs/dev-preflight.md",
    "docs/dev-knowledge.md",
    "docs/dev-diagnostics.md",
})
DEV_PREFIX = "tools/dev-navigator/"
SHA = re.compile(r"^[0-9a-fA-F]{40}$")


def allowed(path: str) -> bool:
    # Use literal Git relative paths only; never normalize away unsafe components.
    if not isinstance(path, str) or not path or "\\" in path:
        return False
    if path.startswith("/") or "//" in path or any(
            segment in ("", ".", "..") for segment in path.split("/")):
        return False
    if path in EXACT:
        return True
    return path.startswith(DEV_PREFIX) and path.endswith((".py", ".json", ".md"))


def classify(paths: list[str]) -> tuple[str, str]:
    if not paths:
        return "full", "empty-or-unavailable-diff"
    if len(paths) > 500:
        return "full", "too-many-changes"
    unknown = sorted(p for p in paths if not allowed(p))
    if unknown:
        return "full", f"outside-allowlist:{len(unknown)}"
    return "dev-tools", "allowlisted-dev-navigator-only"


def changed_paths(repo: Path, base: str, head: str) -> list[str]:
    if not SHA.fullmatch(base) or not SHA.fullmatch(head) or base == head:
        raise ValueError("invalid revision pair")
    # --no-renames: a move is a deletion plus creation. A move out of the
    # approved directories must therefore force full CI.
    completed = subprocess.run(
        ["git", "-C", str(repo), "diff", "--no-renames", "--name-only",
         "--diff-filter=ACDMRT", "-z", base, head, "--"],
        check=True, capture_output=True, timeout=30)
    return [p.decode("utf-8", "strict") for p in completed.stdout.split(b"\x00") if p]


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description="Conservative GitHub Actions CI tier classifier")
    p.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[1])
    p.add_argument("--event", choices=["push", "pull_request"], required=True)
    p.add_argument("--base", default="")
    p.add_argument("--head", default="")
    p.add_argument("--output", type=Path, default=None, help="GitHub Actions output path")
    a = p.parse_args(argv)
    tier, reason = "full", "no-trusted-diff"
    paths = []
    try:
        paths = changed_paths(a.repo_root.resolve(), a.base, a.head)
        tier, reason = classify(paths)
    except (OSError, ValueError, subprocess.CalledProcessError, UnicodeError,
            subprocess.TimeoutExpired):
        # A missing commit, invalid input or API/checkout issue must never
        # make the test suite smaller.
        tier, reason = "full", "diff-unavailable-fail-closed"
    lines = [f"tier={tier}", f"reason={reason}", f"changed_count={len(paths)}"]
    if a.output:
        with a.output.open("a", encoding="utf-8") as output:
            output.write("\n".join(lines) + "\n")
    for line in lines:
        print("CI_CLASSIFICATION " + line)
    return 0


if __name__ == "__main__":
    sys.exit(main())
