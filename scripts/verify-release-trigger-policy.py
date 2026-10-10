#!/usr/bin/env python3
"""Guard the separation between code integration and public application releases.

A normal push to main must not automatically publish an installer or companion
application. Backend deployment remains a separately documented exception.
"""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
MANUAL_RELEASE_WORKFLOWS = (
    ".github/workflows/publish-application-release.yml",
    ".github/workflows/publish-desktop-v1018.yml",
    ".github/workflows/publish-desktop-v1019.yml",
    ".github/workflows/publish-mobile-and-notes.yml",
)

def workflow_triggers(path: str) -> str:
    source = (ROOT / path).read_text(encoding="utf-8-sig")
    match = re.search(r"(?m)^on:\s*\n((?:^[ \t].*(?:\n|$)|^\s*$)*)", source)
    if not match:
        raise AssertionError(f"{path}: missing top-level on: block")
    return match.group(1)


def verify() -> None:
    for relative in MANUAL_RELEASE_WORKFLOWS:
        triggers = workflow_triggers(relative)
        if re.search(r"(?m)^  push\s*:", triggers):
            raise AssertionError(f"{relative}: automatic push-triggered release is forbidden")
        if not re.search(r"(?m)^  workflow_dispatch\s*:", triggers):
            raise AssertionError(f"{relative}: explicit workflow_dispatch required")
        print(f"PASS manual-release-only: {relative}")

    docs = [
        "AGENTS.md",
        ".agents/AGENTS.md",
        "docs/development-integration-release-policy.md",
        "docs/continuous-improvement/PRODUCTION_DEPLOYMENT_POLICY.md",
    ]
    for relative in docs:
        if not (ROOT / relative).is_file():
            raise AssertionError(f"missing policy entrypoint: {relative}")
    policy = (ROOT / "docs/development-integration-release-policy.md").read_text(encoding="utf-8")
    for phrase in ("按需发布", "合并前", "Cloudflare", "WIP Commit"):
        if phrase not in policy:
            raise AssertionError(f"missing required policy item: {phrase}")
    print("PASS development integration policy documentation")


if __name__ == "__main__":
    verify()