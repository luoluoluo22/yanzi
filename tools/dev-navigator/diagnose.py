#!/usr/bin/env python3
"""Evidence-scoped incident triage: description + safe log fingerprints + Git + QA.

Not OCR. Never guesses a root cause, uploads logs, reads credential files, or
treats stale QA as passing. Input logs are restricted to local Yanzi logs.
"""
from __future__ import annotations

import argparse
import collections
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import subprocess
import sys

import knowledge
import navigator
import preflight

MAX_LOG_BYTES = 16_000_000
MAX_LOG_TAIL_BYTES = 256_000
MAX_QUERY_CHARS = 800
MAX_SOURCE_HITS = 8
SIGS = (
    ("CREDENTIALS_MISSING", re.compile(r"\bCREDENTIALS_MISSING\b", re.I), ["ai-provider-settings"],
     "检查请求所用鉴权方式和密钥是否实际进入请求（不输出密钥）"),
    ("HTTP_401", re.compile(r"\b(?:HTTP\s*[:=]?\s*401|status(?:code)?\s*[:=]\s*401|401|UNAUTHENTICATED)\b", re.I), ["ai-provider-settings"],
     "核对供应商接口路径与原生/兼容协议的鉴权差异"),
    ("HTTP_429", re.compile(r"\b(?:HTTP\s*[:=]?\s*429|status(?:code)?\s*[:=]\s*429|RESOURCE_EXHAUSTED)\b", re.I), ["ai-chat"],
     "检查供应商配额和限流策略；不要重试风暴"),
    ("TIMEOUT", re.compile(r"\b(?:timeout|timed[\s-]?out|超时|一直在生成中)\b", re.I), ["ai-chat"],
     "对比连接、流式首字节与结束阶段的耗时"),
    ("MODEL_LIST", re.compile(r"(?:模型列表|模型菜单|模型滚动|model[ _-]?menu|scrollable|scrollviewer)", re.I), ["ai-model-menu"],
     "核对菜单 ScrollViewer 与列表高度及当前选中项"),
    ("WINDOW_POSITION", re.compile(r"(?:窗口居中|标签左移|左右扩展|置于前面|被遮挡|window.?owner)", re.I), ["ai-window-layout"],
     "核对窗口所有权、Z 序及尺寸转换"),
    ("CLIPBOARD", re.compile(r"(?:剪贴板|ctrl\+c|ctrl\+v|clipboard)", re.I), ["clipboard-sync"],
     "核对本机剪贴板监听器与其他应用的占用情况"),
)
LOG_LOCATIONS = {"ai-chat.log", "runtime.log", "shell.log", "dev-debug.log", "cloud-sync-diagnostics.log"}


def signature_hits(value: str) -> list[dict]:
    hits = []
    for key, expression, features, next_step in SIGS:
        if expression.search(value):
            hits.append({"id": key, "features": features, "nextCheck": next_step})
    return hits


def validate_log_path(path: Path, *, local_data: Path | None = None) -> Path:
    local_data = local_data or Path(os.environ.get("LOCALAPPDATA", tempfile_dir()))
    logs = (local_data / "OpenQuickHost" / "logs").resolve()
    real = path.resolve(strict=True)
    if real.parent != logs or real.name.casefold() not in LOG_LOCATIONS:
        raise ValueError("Log path must be a recognized file directly under %LOCALAPPDATA%/OpenQuickHost/logs")
    if not real.is_file() or real.stat().st_size > MAX_LOG_BYTES:
        raise ValueError("Log missing or too large (16 MB maximum); narrow the evidence first")
    return real


def tempfile_dir() -> str:
    import tempfile
    return tempfile.gettempdir()


def sample_log(path: Path, *, local_data: Path | None = None, tail_lines: int = 400) -> dict:
    safe = validate_log_path(path, local_data=local_data)
    # Ingestion reads the local file but never returns untrusted raw lines. Only
    # deterministic known signatures and aggregate counts leave this function.
    with safe.open("rb") as stream:
        stream.seek(max(0, safe.stat().st_size - MAX_LOG_TAIL_BYTES))
        data = stream.read(MAX_LOG_TAIL_BYTES)
    text = data.decode("utf-8-sig", errors="replace")
    lines = collections.deque(text.splitlines(), maxlen=min(max(1, tail_lines), 1000))
    last = "\n".join(lines)
    return {"file": safe.name, "linesExamined": len(lines),
            "fingerprints": [item["id"] for item in signature_hits(last)],
            "rawLogIncluded": False}


def recent_changes(root: Path, paths: list[str], *, limit: int = 8) -> list[dict]:
    if not paths:
        return []
    selected = sorted(set(p for p in paths if p.startswith(("src/", "tests/", "scripts/", "tools/"))))
    if not selected:
        return []
    args = ["git", "-C", str(root), "log", "--no-merges", "--format=COMMIT:%h%x09%aI%x09%s",
            "--name-only", "-n", "35", "--", *selected[:40]]
    raw = subprocess.run(args, capture_output=True, check=True).stdout.decode("utf-8", "replace")
    results: list[dict] = []
    current = None
    for line in raw.splitlines():
        if line.startswith("COMMIT:"):
            if current and current["files"]:
                results.append(current)
            fields = line[7:].split("\t", 2)
            current = {"commit": fields[0], "date": fields[1][:10], "subject": fields[2][:110],
                       "files": []} if len(fields) == 3 else None
        elif current and line.strip().startswith(("src/", "scripts/", "tools/", "tests/")):
            current["files"].append(line.strip())
    if current and current["files"]:
        results.append(current)
    return results[:limit]


def analyze(root: Path, *, description: str, screenshot_note: str = "", log_path: Path | None = None,
            index: dict | None = None, features: list[dict] | None = None,
            log_evidence: dict | None = None, include_recent: bool = True,
            qa_override: dict | None = None) -> dict:
    if not description.strip() and not screenshot_note.strip() and not log_path and not log_evidence:
        raise ValueError("Provide a symptom description, screenshot notes or a recognized log")
    if len(description) > MAX_QUERY_CHARS or len(screenshot_note) > MAX_QUERY_CHARS:
        raise ValueError("Symptom text too large; provide a short description, without credentials")
    index = index if index is not None else navigator.build_index(root)
    features = features if features is not None else navigator.features_from(root)
    log = log_evidence or (sample_log(log_path) if log_path else None)
    # Avoid sending or writing input text: only trusted signature IDs are output.
    found = signature_hits(description + "\n" + screenshot_note)
    log_ids = set(log["fingerprints"] if log else [])
    ids = list(dict.fromkeys([x["id"] for x in found] + sorted(log_ids)))
    signals = {item[0]: item for item in SIGS}
    matching_signals = [signals[k] for k in ids if k in signals]
    query_features = [x for x in navigator.search_features(description, index, features, limit=6)
                      if x["score"] >= 40] if description else []
    notes_features = [x for x in navigator.search_features(screenshot_note, index, features, limit=6)
                      if x["score"] >= 40] if screenshot_note else []
    matched = {item["id"] for item in query_features + notes_features if item["id"] != "ui-evidence"}
    for sig in matching_signals:
        matched.update(sig[2])
    candidates = [navigator.enriched_feature(f, index) for f in features if f["id"] in matched]
    priority = {f["id"]: 0 for f in candidates}
    for sig in matching_signals:
        for f in sig[2]:
            priority[f] = priority.get(f, 0) + 50
    for item in query_features + notes_features:
        if item["id"] in priority:
            priority[item["id"]] += item["score"]
    candidates.sort(key=lambda x: (-priority[x["id"]], x["id"]))
    top = candidates[:5]
    candidate_paths = sorted({p for f in top for p in f["files"]
                              if p.endswith((".cs", ".xaml", ".ps1"))})
    ui_query = [item for item in (description, screenshot_note) if item and len(item) < 70]
    ui = navigator.ui_lookup(ui_query[0], index, limit=4) if ui_query else []
    if not candidates and ui:
        candidate_paths.extend(x["path"] for x in ui)
    # Avoid implying recently changed files prove causation.
    commits = recent_changes(root, candidate_paths[:25]) if include_recent else []
    qa = (knowledge.qa_status(root, [f["id"] for f in top])
          if qa_override is None else qa_override)
    steps = []
    for item in matching_signals:
        if item[3] not in steps:
            steps.append(item[3])
    if not steps:
        steps.append("先确认精确控件/错误文本与最小复现步骤，避免猜测根因")
    steps += [
        "先核对用户实际运行版本与源码提交，避免在错误工作树修改",
        "按候选源码和近期改动做只读检查，不把修改时间当作根因证据",
        "制定可测验收条件，运行对应专项测试，必要时补真机 QA",
    ]
    tests = sorted({p for f in top for p in f["tests"]})
    return {
        "schema": 1, "status": "triage-candidate-not-confirmed",
        "input": {"descriptionPresent": bool(description), "screenshotNotePresent": bool(screenshot_note),
                  "screenshotImageAnalyzed": False, "log": log},
        "signals": ids, "features": [{"id": f["id"], "title": f["title"],
                     "score": priority[f["id"]], "files": f["files"][:15],
                     "tests": f["tests"][:10]} for f in top],
        "uiEvidence": [{"path": x["path"], "line": x["line"],
                        "name": x["name"], "handlers": x["handlers"]} for x in ui],
        "recentChanges": commits, "qaHistory": qa, "suggestedTests": tests,
        "nextChecks": steps, "unresolved": [
            "候选不等于根因；需要重现或日志阶段证据",
            "未读取远程电脑日志、未检查截图像素、未执行真实设备 UI 操作",
        ],
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description="One-command evidence-based Yanzi incident triage")
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--description", default="")
    parser.add_argument("--screenshot-note", default="", help="Human-observed screenshot text; not OCR")
    parser.add_argument("--log", type=Path, default=None, help="Explicit, local allowlisted log path")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    try:
        result = analyze(args.repo_root.resolve(), description=args.description,
                         screenshot_note=args.screenshot_note, log_path=args.log)
        if args.json:
            print(json.dumps(result, ensure_ascii=False, indent=2))
        else:
            print("TRIAGE_STATUS=" + result["status"])
            print("SIGNALS=" + ",".join(result["signals"]))
            for f in result["features"]:
                print("FEATURE", f["id"], "score=" + str(f["score"]))
                for path in f["files"][:8]: print("  SOURCE", path)
            for test in result["suggestedTests"]: print("TEST", test)
            for item in result["recentChanges"][:5]:
                print("RECENT_CHANGE", item["commit"], item["date"], ",".join(item["files"][:3]))
            for step in result["nextChecks"]: print("CHECK", step)
        return 0
    except (ValueError, OSError, subprocess.CalledProcessError) as exc:
        print("DEV_DIAGNOSE_ERROR: " + str(exc), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
