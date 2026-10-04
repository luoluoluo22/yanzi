#!/usr/bin/env python3
"""Select only companion releases changed by this push; publish them sequentially."""
import json
import os
import subprocess
from pathlib import Path


def select_metadata(root, changed=None, application="all"):
    selected = []
    for path in sorted((root / "release-inputs/applications").glob("*.json")):
        meta = json.loads(path.read_text(encoding="utf-8-sig"))
        relative = path.relative_to(root).as_posix()
        if application not in ("all", meta["applicationId"]):
            continue
        if changed is None or relative in changed or meta["apkPath"] in changed:
            selected.append(relative)
    return selected


def main():
    root = Path(__file__).resolve().parents[1]
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
    if os.environ["GITHUB_EVENT_NAME"] == "workflow_dispatch":
        result = select_metadata(root, application=event.get("inputs", {}).get("application", "all"))
    else:
        before, after = event.get("before", ""), event["after"]
        if before and set(before) != {"0"}:
            changed = set(subprocess.check_output(["git", "diff", "--name-only", before, after], cwd=root, text=True).splitlines())
        else:
            changed = set(subprocess.check_output(["git", "ls-tree", "-r", "--name-only", after], cwd=root, text=True).splitlines())
        result = select_metadata(root, changed)
    print(json.dumps(result))


if __name__ == "__main__":
    main()
