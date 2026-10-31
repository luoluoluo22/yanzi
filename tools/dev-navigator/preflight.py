#!/usr/bin/env python3
"""Conservative, one-shot developer preflight for the Yanzi source navigator.

Read-only by default. Optional execution is limited to known in-repo test entrypoints.
UI smoke runs against the existing hidden-desktop runner, not the live Yanzi window.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import subprocess
import sys
import time

import navigator

# Only verified isolated test projects may be run by the preflight executor.
SAFE_PROJECTS = frozenset({
    "tests/ModelMenuScrollVerification/ModelMenuScrollVerification.csproj",
    "tests/AiWindowResizeGeometryVerification/AiWindowResizeGeometryVerification.csproj",
    "tests/GeminiNativeChatVerification/GeminiNativeChatVerification.csproj",
})
HIDDEN_UI_SCRIPT = "scripts/verify-yanzi-ui-hidden.ps1"
HIDDEN_SCENARIOS = {"ai-model-menu": "ai-chat-model"}
UI_POLICY = {
    "ai-chat": "hidden shared-control smoke is supplementary; it does not verify real Gemini requests",
    "ai-model-menu": "isolated synthetic 50+ model picker verifies scroll/hierarchy, but not installed AI window or real mouse wheel",
    "ai-window-layout": "hidden shared-control smoke does not verify the real AI tab/popup geometry; add a dedicated WPF window scenario",
    "ai-provider-settings": "generic hidden UI smoke does not verify persisted keys or settings window ownership",
    "clipboard-sync": "hidden desktop does not isolate the system clipboard or physical input; use dedicated isolated/real-device QA",
    "smart-action": "hidden desktop does not reproduce physical mouse gestures; requires isolated real-device UI QA",
    "browser-helper": "hidden desktop does not reproduce browser-extension UI/permissions",
}
UI_PATHS = ("src/OpenQuickHost/MainWindow", "src/OpenQuickHost/SettingsWindow",
            "src/Yanzi.UI.Wpf/", "src/Yanzi.UiTesting/")
CHOOSE_NEXT = [
    "确认目标行为及可测量验收条件；不要把业务描述直接当作实现细节。",
    "核对当前运行实例、真实配置与版本，确定最小可修改范围。",
    "检查共享控件、同步/持久化及异步路径是否有隐藏影响。",
    "修改后运行对应的专项测试，必要时独立执行隐藏桌面或真机 UI 验收。",
    "完成差异复核并通过 PR/CI 门禁；生产发布需独立授权。",
]


def collect_known_tests(root: Path, feature_items: list[dict], affected: dict) -> list[dict]:
    paths = set()
    for feature in feature_items:
        paths.update(feature.get("tests", []))
    paths.update(affected["suggestedTests"])
    tests = []
    for path in sorted(paths):
        p = root / path
        if not p.is_file():
            tests.append({"path": path, "kind": "missing", "execution": "not-runnable",
                          "reason": "Referenced test entrypoint does not exist"})
            continue
        safe = path in SAFE_PROJECTS
        if path.endswith(".csproj"):
            cmd = ["dotnet", "run", "--project", path, "-c", "Release"]
            kind = "dotnet"
        elif path.endswith(".ps1"):
            cmd = ["powershell.exe", "-NoProfile", "-NonInteractive",
                   "-ExecutionPolicy", "Bypass", "-File", path]
            kind = "powershell"
        else:
            continue
        tests.append({"path": path, "kind": kind, "command": cmd,
                      "execution": "safe-targeted" if safe else "review-before-running",
                      "reason": "" if safe else "Not in isolated-test allowlist; inspect side effects first"})
    return tests


def make_plan(root: Path, *, query: str = "", ui_query: str = "", paths: list[str] | None = None,
              base: str = "", expect: list[str] | None = None,
              preserve: list[str] | None = None, index: dict | None = None,
              features: list[dict] | None = None) -> dict:
    if not any([query.strip(), ui_query.strip(), paths, base]):
        raise ValueError("Supply -Query, -UiQuery, -Paths or -Base; blank preflight cannot determine scope")
    index = index or navigator.build_index(root)
    features = features if features is not None else navigator.features_from(root)
    matches = navigator.search_features(query, index, features) if query.strip() else []
    controls = navigator.ui_lookup(ui_query, index) if ui_query.strip() else []
    changed = list(paths or [])
    if base:
        changed += navigator.changed_paths(root, base)
    affected = navigator.impact(root, changed, index, features)
    ids = {candidate["id"] for candidate in matches if candidate["id"] != "ui-evidence"}
    ids.update(a["id"] for a in affected["affected"])
    # Ground a UI query in a curated feature by matching its actual XAML/handler files.
    for control in controls:
        files = {control["path"], *(d["path"] for d in control["definitions"])}
        for feature in features:
            known = set(navigator.enriched_feature(feature, index)["files"])
            if known.intersection(files) and set(feature.get("handlers", [])).intersection(
                    control["handlers"].values()):
                ids.add(feature["id"])
    selected_features = [navigator.enriched_feature(f, index)
                         for f in features if f["id"] in ids]
    source_candidates = sorted({file for f in selected_features for file in f["files"]} |
                               {file for c in controls
                                for file in [c["path"], *(d["path"] for d in c["definitions"])]})
    tests = collect_known_tests(root, selected_features, affected)
    ui_ids = sorted(i for i in ids if i in UI_POLICY)
    modified_ui = any(path.startswith(UI_PATHS) or path.endswith(".xaml")
                      for path in affected["changed"])
    ui_recommended = bool(ui_ids or modified_ui)
    smoke_available = (root / HIDDEN_UI_SCRIPT).is_file()
    scenario = next((HIDDEN_SCENARIOS[i] for i in ui_ids if i in HIDDEN_SCENARIOS), "shared-ui")
    ui_coverage = {
        "recommended": ui_recommended,
        "scope": "isolated-model-hierarchy" if scenario == "ai-chat-model" else "generic-shared-controls-only",
        "scenario": scenario,
        "script": HIDDEN_UI_SCRIPT if smoke_available else None,
        "command": (["powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy",
                     "Bypass", "-File", HIDDEN_UI_SCRIPT, "-Scenario", scenario, "-Mode", "hidden"]
                    if smoke_available and ui_recommended else None),
        "automaticallyCoversFeature": False,
        "gaps": [UI_POLICY[i] for i in ui_ids] or (
            ["UI changes require a dedicated scenario; shared-controls smoke is not sufficient"]
            if ui_recommended else []),
    }
    evidence = bool(selected_features or controls)
    concerns = []
    if not evidence:
        concerns.append("UNKNOWN_FEATURE: no grounded source route; inspect source manually")
    if any(x["id"] == "ui-evidence" for x in matches):
        concerns.append("UNMAPPED_UI: grounded XAML evidence but business owner/tests unknown")
    if affected["unmapped"]:
        concerns.append("UNMAPPED_CHANGES: changed files not assigned to known features")
    if not expect:
        concerns.append("ACCEPTANCE_UNDEFINED: define observable expected behavior before editing")
    if ui_recommended:
        concerns.append("UI_COVERAGE_GAP: isolated UI tests cannot prove installed-app physical interaction")
    return {
        "schema": 1,
        "task": {"description": query, "uiQuery": ui_query,
                 "acceptance": list(expect or []), "preserve": list(preserve or [])},
        "indexStats": index["stats"],
        "source": {"features": matches[:5], "uiControls": controls[:10],
                   "candidateFiles": source_candidates[:80],
                   "mappedFeatureIds": sorted(ids)},
        "impact": affected,
        "validation": {"tests": tests, "hiddenUiSmoke": ui_coverage,
                       "realDeviceVerificationRequired": ui_recommended,
                       "testExecutionIsOptIn": True},
        "review": {"status": "needs-review" if concerns else "candidate-ready",
                   "concerns": concerns, "steps": CHOOSE_NEXT},
    }


def run_opt_in_tests(root: Path, plan: dict, *, targeted: bool, hidden_ui: bool) -> dict:
    result = {"targeted": [], "hiddenUiSmoke": None}
    if targeted:
        for test in plan["validation"]["tests"]:
            if test["execution"] != "safe-targeted":
                continue
            result["targeted"].append(run_checked(root, test["command"], test["path"], 150))
    if hidden_ui:
        smoke = plan["validation"]["hiddenUiSmoke"]
        if not smoke["recommended"] or not smoke["command"]:
            result["hiddenUiSmoke"] = {"status": "not-applicable",
                                        "note": "No relevant verified WPF smoke route"}
        else:
            result["hiddenUiSmoke"] = run_checked(root, smoke["command"], "shared-controls-smoke", 240)
    return result


def run_checked(root: Path, command: list[str], label: str, timeout: int) -> dict:
    started = time.perf_counter()
    try:
        completed = subprocess.run(command, cwd=root, capture_output=True, text=True,
                                   errors="replace", timeout=timeout, check=False)
        return {"name": label, "status": "passed" if completed.returncode == 0 else "failed",
                "exitCode": completed.returncode, "elapsedSeconds": round(time.perf_counter()-started, 2),
                "outputTail": (completed.stdout + "\n" + completed.stderr)[-2500:]}
    except (OSError, subprocess.TimeoutExpired) as error:
        return {"name": label, "status": "error", "elapsedSeconds": round(time.perf_counter()-started, 2),
                "reason": type(error).__name__}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Single-command developer preflight: locate → impact → tests.")
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--query", default="")
    parser.add_argument("--ui-query", default="")
    parser.add_argument("--path", action="append", default=[])
    parser.add_argument("--base", default="")
    parser.add_argument("--expect", action="append", default=[])
    parser.add_argument("--preserve", action="append", default=[])
    parser.add_argument("--run-targeted", action="store_true")
    parser.add_argument("--run-hidden-ui", action="store_true")
    parser.add_argument("--out", type=Path, default=None, help="Optional report file; no report is saved by default")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    root = args.repo_root.resolve()
    try:
        plan = make_plan(root, query=args.query, ui_query=args.ui_query,
                         paths=args.path, base=args.base, expect=args.expect,
                         preserve=args.preserve)
        if args.run_targeted or args.run_hidden_ui:
            plan["execution"] = run_opt_in_tests(root, plan,
                                                  targeted=args.run_targeted,
                                                  hidden_ui=args.run_hidden_ui)
        if args.out:
            out = args.out if args.out.is_absolute() else root / args.out
            if root == out or (root in out.parents and not (
                    out.relative_to(root).parts[0] in (".tmp", ".artifacts"))):
                raise ValueError("Report inside source tree must live under .tmp/ or .artifacts/")
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_text(json.dumps(plan, ensure_ascii=False, indent=2), encoding="utf-8")
        if args.json:
            print(json.dumps(plan, ensure_ascii=False, indent=2))
        else:
            print("DEV_PREFLIGHT_STATUS=" + plan["review"]["status"])
            print("FEATURES=" + ", ".join(plan["source"]["mappedFeatureIds"]))
            for f in plan["source"]["candidateFiles"][:18]: print("SOURCE", f)
            for t in plan["validation"]["tests"]:
                print("TEST", t["execution"], t["path"])
            if plan["validation"]["hiddenUiSmoke"]["recommended"]:
                print("UI_SMOKE=optional/" + plan["validation"]["hiddenUiSmoke"]["scope"] + "; installed-app UI verification still required")
            for c in plan["review"]["concerns"]: print("REVIEW", c)
            for test in plan.get("execution", {}).get("targeted", []):
                print("TEST_RESULT", test["name"], test["status"])
            hidden = plan.get("execution", {}).get("hiddenUiSmoke")
            if hidden: print("HIDDEN_UI_RESULT", hidden["status"])
            if args.out: print("REPORT=" + str(out))
        if any(run["status"] in ("failed", "error") for run in
               plan.get("execution", {}).get("targeted", [])):
            return 1
        hidden = plan.get("execution", {}).get("hiddenUiSmoke")
        if hidden and hidden["status"] in ("failed", "error"):
            return 1
        return 0
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print("DEV_PREFLIGHT_ERROR: " + str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
