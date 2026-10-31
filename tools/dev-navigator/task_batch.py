#!/usr/bin/env python3
"""One-call development triage with bounded timings and explicit test authorization.

This orchestrates the existing navigator/preflight/diagnose modules. It is NOT
an arbitrary-command runner or a remote MCP server, and never deploys or edits
project source. Caller can batch one host shell invocation.
"""
from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
import json
from pathlib import Path
import sys
import time

import diagnose
import knowledge
import navigator
import preflight

MAX_INPUT = 800
STAGE_COUNT_LIMIT = 12
FAIL_CAUSES = {
    "TimeoutExpired": "stage-timeout",
    "CalledProcessError": "git-or-subprocess-failed",
    "OSError": "resource-unavailable",
    "ValueError": "input-or-scope-invalid",
    "RuntimeError": "stage-runtime-error",
}


def failure_code(error: Exception) -> str:
    # Never return exception message: it may contain input text or a secret.
    return FAIL_CAUSES.get(type(error).__name__, "stage-exception")


def safe_output(report: dict) -> dict:
    """Keep the public report free of raw task text, URLs, keys and test logs."""
    return {
        "schema": report["schema"],
        "status": report["status"],
        "failureReason": report.get("failureReason"),
        "revision": report.get("revision"),
        "summary": report.get("summary"),
        "stages": report["stages"],
        "diagnosis": report.get("diagnosis"),
        "preflight": report.get("preflight"),
        "validation": report.get("validation"),
        "metrics": report.get("metrics"),
        "boundaries": report["boundaries"],
    }


def orchestrate(
    root: Path, *, query: str, ui_query: str = "", screenshot_note: str = "",
    paths: list[str] | None = None, base: str = "", accept: list[str] | None = None,
    preserve: list[str] | None = None, run_targeted: bool = False,
    run_hidden_ui: bool = False, parallel: bool = True,
    clock=time.perf_counter,
) -> dict:
    if not query.strip() or len(query) > MAX_INPUT or len(ui_query) > MAX_INPUT or len(screenshot_note) > MAX_INPUT:
        raise ValueError("query required, maximum 800 characters each")
    if not root.is_dir():
        raise ValueError("repository directory missing")
    accept = list(accept or [])
    preserve = list(preserve or [])
    started = clock()
    stages: list[dict] = []
    report: dict = {
        "schema": 1, "status": "incomplete",
        "boundaries": {
            "changesSource": False,
            "publishesOrInstalls": False,
            "invokesArbitraryShell": False,
            "remoteMcpInvocationCountObserved": False,
            "hostMcpInvocationsRequiredByDesign": 1,
            "qaOnInstalledClient": "not-verified",
            "diagnosisRootCauseConfirmed": False,
            "rawLogsOrCredentialsReturned": False,
        },
    }

    def timed(name: str, operation):
        begin = clock()
        try:
            result = operation()
            stages.append({"stage": name, "status": "passed",
                           "elapsedMs": round((clock()-begin)*1000, 2)})
            return result
        except Exception as exc:
            stages.append({"stage": name, "status": "failed",
                           "elapsedMs": round((clock()-begin)*1000, 2),
                           "reason": failure_code(exc)})
            raise

    try:
        # Read-only Git identity; do not infer whether client is installed.
        git_state = timed("git-revision", lambda: knowledge.revision(root))
        report["revision"] = {"commit": git_state["commit"][:12],
                               "dirty": git_state["dirty"]}
        idx, feature_list = timed("index", lambda: (
            navigator.build_index(root), navigator.features_from(root)))
        report["summary"] = {"filesIndexed": idx["stats"].get("files"),
                             "featuresKnown": len(feature_list),
                             "indexStats": idx["stats"]}
        diagnosis_input = {"description": query, "screenshot_note": screenshot_note,
                           "index": idx, "features": feature_list}
        preflight_input = {"query": query, "ui_query": ui_query,
                           "paths": list(paths or []), "base": base,
                           "expect": accept, "preserve": preserve,
                           "index": idx, "features": feature_list}
        # Independent stages share immutable index. ThreadPool usage is opt-in
        # so timing is a comparison, not a claim of guaranteed speedup.
        if parallel:
            with ThreadPoolExecutor(max_workers=2) as pool:
                diag_job = pool.submit(timed, "diagnosis", lambda:
                    diagnose.analyze(root, **diagnosis_input))
                plan_job = pool.submit(timed, "preflight", lambda:
                    preflight.make_plan(root, **preflight_input))
                diag = diag_job.result()
                plan = plan_job.result()
        else:
            diag = timed("diagnosis", lambda: diagnose.analyze(root, **diagnosis_input))
            plan = timed("preflight", lambda: preflight.make_plan(root, **preflight_input))
        report["diagnosis"] = {
            "status": diag["status"], "signals": diag["signals"],
            "featureIds": [x["id"] for x in diag["features"]],
            "changedCommits": [x["commit"] for x in diag["recentChanges"][:6]],
            "nextChecks": diag["nextChecks"][:8],
        }
        report["preflight"] = {
            "status": plan["review"]["status"],
            "featureIds": plan["source"]["mappedFeatureIds"],
            "candidateFiles": plan["source"]["candidateFiles"][:35],
            "suggestedTests": [
                {"path": t["path"], "execution": t["execution"]}
                for t in plan["validation"]["tests"]
            ],
            "concerns": plan["review"]["concerns"],
            "hiddenUiScenario": (plan["validation"]["hiddenUiSmoke"]["scenario"]
                                 if plan["validation"]["hiddenUiSmoke"]["recommended"] else None),
        }
        if run_targeted or run_hidden_ui:
            execution = timed("isolated-tests", lambda: preflight.run_opt_in_tests(
                root, plan, targeted=run_targeted, hidden_ui=run_hidden_ui))
            actual = list(execution.get("targeted", []))
            if execution.get("hiddenUiSmoke"):
                actual.append(execution["hiddenUiSmoke"])
            failed = [x for x in actual if x["status"] in ("failed", "error")]
            report["validation"] = {
                "status": "failed" if failed else "passed",
                "results": [
                    {"name": x.get("name", "hidden-ui"), "status": x["status"],
                     "elapsedSeconds": x.get("elapsedSeconds"),
                     "exitCode": x.get("exitCode"),
                     "reason": x.get("reason")}
                    for x in actual
                ],
                "qaIsolatedOnly": True,
            }
            # Ledger follows existing provenance rules and may fail independently.
            recorded = timed("qa-ledger", lambda: knowledge.record_executed(
                root, plan, execution))
            report["validation"]["recorded"] = len(recorded)
            if failed:
                report["status"] = "failed-tests"
        else:
            report["validation"] = {"status": "not-requested", "recorded": 0}
        report["qaHistory"] = timed("qa-history", lambda: knowledge.qa_status(
            root, plan["source"]["mappedFeatureIds"]))
        # Only emit a summarized current state, never user/task text.
        current = report.pop("qaHistory")
        report["summary"]["currentIsolatedChecks"] = {
            feature: value["isolatedChecksCurrent"]
            for feature, value in current.items()
        }
        if report["status"] != "failed-tests":
            report["status"] = "candidate-plan-ready"
    except Exception as exc:
        report["status"] = "stage-failed"
        report["failureReason"] = failure_code(exc)
    finally:
        # Sorting avoids nondeterministic order when diagnosis/preflight run in parallel.
        order = {"git-revision": 0, "index": 1, "diagnosis": 2, "preflight": 3,
                 "isolated-tests": 4, "qa-ledger": 5, "qa-history": 6}
        stages.sort(key=lambda x: order.get(x["stage"], 99))
        report["stages"] = stages[:STAGE_COUNT_LIMIT]
        report["metrics"] = {
            "localStagesCompleted": len([x for x in stages if x["status"] == "passed"]),
            "localStagesFailed": len([x for x in stages if x["status"] == "failed"]),
            "elapsedTotalMs": round((clock()-started)*1000, 2),
            "parallelReadOnlyStages": parallel,
            "sourceIndexBuiltOnce": len([x for x in stages if x["stage"] == "index"]) == 1,
            "actualRemoteMcpCallCount": None,
            "measurementScope": "local orchestrator wall time, not MCP relay/network overhead",
        }
    return report


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Batch developer diagnosis, preflight and QA with timing")
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--query", required=True)
    parser.add_argument("--ui-query", default="")
    parser.add_argument("--screenshot-note", default="")
    parser.add_argument("--path", action="append", default=[])
    parser.add_argument("--base", default="")
    parser.add_argument("--expect", action="append", default=[])
    parser.add_argument("--preserve", action="append", default=[])
    parser.add_argument("--sequential", action="store_true", help="Benchmark baseline without parallel read-only stages")
    parser.add_argument("--run-targeted", action="store_true")
    parser.add_argument("--run-hidden-ui", action="store_true")
    parser.add_argument("--json", action="store_true")
    a = parser.parse_args(argv)
    report = orchestrate(
        a.repo_root.resolve(), query=a.query, ui_query=a.ui_query,
        screenshot_note=a.screenshot_note, paths=a.path, base=a.base,
        accept=a.expect, preserve=a.preserve, parallel=not a.sequential,
        run_targeted=a.run_targeted, run_hidden_ui=a.run_hidden_ui,
    )
    visible = safe_output(report)
    if a.json:
        print(json.dumps(visible, ensure_ascii=False, indent=2))
    else:
        print("DEV_TASK_BATCH=" + report["status"])
        for item in report["stages"]:
            print("STAGE", item["stage"], item["status"], str(item["elapsedMs"])+"ms",
                  item.get("reason", ""))
        for file in report.get("preflight", {}).get("candidateFiles", [])[:12]:
            print("SOURCE", file)
        for test in report.get("preflight", {}).get("suggestedTests", [])[:12]:
            print("TEST", test["execution"], test["path"])
        print("METRIC elapsedTotalMs=" + str(report["metrics"]["elapsedTotalMs"]))
        print("LIMITATION local processing only; MCP network/relay not included")
    return 0 if report["status"] == "candidate-plan-ready" else 1


if __name__ == "__main__":
    sys.exit(main())
