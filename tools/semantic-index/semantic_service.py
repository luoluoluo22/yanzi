#!/usr/bin/env python3
from __future__ import annotations

import argparse
import gc
import hashlib
import json
import os
import sqlite3
import threading
import time
import traceback
from dataclasses import dataclass
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

SERVICE_VERSION = "0.1.0"
MODEL_ID = "google/embeddinggemma-2"
IMAGE_EXTS = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}
DEFAULT_DIM = 768


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def atomic_write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(temp, path)


@dataclass
class RootSpec:
    path: str
    recursive: bool = True

    @staticmethod
    def from_value(value: Any) -> "RootSpec":
        if isinstance(value, str):
            return RootSpec(value, True)
        if isinstance(value, dict):
            return RootSpec(str(value.get("path") or ""), bool(value.get("recursive", True)))
        return RootSpec("", True)


class SemanticIndex:
    def __init__(self, config_path: Path):
        self.config_path = config_path.resolve()
        self.config = load_json(self.config_path)
        self.data_dir = Path(self.config.get("dataDir") or self.config_path.parent).resolve()
        self.data_dir.mkdir(parents=True, exist_ok=True)
        self.db_path = Path(self.config.get("dbPath") or (self.data_dir / "semantic-index.sqlite")).resolve()
        self.model_cache = Path(self.config.get("modelCache") or (self.data_dir / "model-cache")).resolve()
        self.model_cache.mkdir(parents=True, exist_ok=True)
        self.default_min_score = float(self.config.get("minScore", 0.68))
        self.vision_tokens = int(self.config.get("visionTokens", 140))
        self.idle_unload_seconds = int(self.config.get("idleUnloadSeconds", 300))
        self.idle_exit_seconds = int(self.config.get("idleExitSeconds", 300))
        self.scan_before_search = bool(self.config.get("scanBeforeSearch", True))
        self.watch_interval_seconds = int(self.config.get("watchIntervalSeconds", 90))
        self.roots = [RootSpec.from_value(v) for v in self.config.get("roots", [])]
        self.roots = [r for r in self.roots if r.path]
        self._model = None
        self._model_lock = threading.RLock()
        self._inference_lock = threading.Lock()
        self._db_lock = threading.RLock()
        self._activity_lock = threading.Lock()
        self._active_operations = 0
        self._last_model_use = 0.0
        self._ever_loaded = False
        self._shutdown_callback = None
        self._stop = threading.Event()
        self._last_scan_result: dict[str, Any] | None = None
        self._ensure_db()

    def _connect(self) -> sqlite3.Connection:
        conn = sqlite3.connect(self.db_path, timeout=30)
        conn.row_factory = sqlite3.Row
        conn.execute("PRAGMA journal_mode=WAL")
        conn.execute("PRAGMA synchronous=NORMAL")
        return conn

    def _ensure_db(self) -> None:
        with self._db_lock, self._connect() as conn:
            conn.executescript(
                """
                CREATE TABLE IF NOT EXISTS files (
                    path TEXT PRIMARY KEY,
                    kind TEXT NOT NULL,
                    size INTEGER NOT NULL,
                    mtime_ns INTEGER NOT NULL,
                    embedding BLOB NOT NULL,
                    dim INTEGER NOT NULL,
                    indexed_at_utc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_files_kind ON files(kind);
                CREATE TABLE IF NOT EXISTS meta (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """
            )
            conn.execute(
                "INSERT OR REPLACE INTO meta(key,value) VALUES('schema_version','1')"
            )

    def _begin_activity(self) -> None:
        with self._activity_lock:
            self._active_operations += 1
            self._last_model_use = time.monotonic()

    def _end_activity(self) -> None:
        with self._activity_lock:
            self._active_operations = max(0, self._active_operations - 1)
            self._last_model_use = time.monotonic()

    def _load_model(self):
        with self._model_lock:
            if self._model is not None:
                self._last_model_use = time.monotonic()
                return self._model
            os.environ["HF_HOME"] = str(self.model_cache)
            os.environ["HUGGINGFACE_HUB_CACHE"] = str(self.model_cache / "hub")
            import torch
            from sentence_transformers import SentenceTransformer

            threads = int(self.config.get("torchThreads", min(6, os.cpu_count() or 1)))
            torch.set_num_threads(max(1, threads))
            self._model = SentenceTransformer(
                MODEL_ID,
                device="cpu",
                config_kwargs={"audio_config": None},
                model_kwargs={"torch_dtype": torch.float32},
            )
            self._ever_loaded = True
            self._last_model_use = time.monotonic()
            return self._model

    def unload_model_if_idle(self, force: bool = False) -> bool:
        with self._activity_lock:
            active = self._active_operations
            idle_for = time.monotonic() - self._last_model_use if self._last_model_use else 10**9
        if not force and (active > 0 or idle_for < self.idle_unload_seconds):
            return False
        with self._model_lock:
            if self._model is None:
                return False
            self._model = None
            gc.collect()
            try:
                import torch
                if hasattr(torch, "cuda"):
                    torch.cuda.empty_cache()
            except Exception:
                pass
            return True

    def _encode_image(self, path: Path):
        import numpy as np

        with self._inference_lock:
            model = self._load_model()
            vector = model.encode(
                {"image": str(path)},
                processing_kwargs={"image": {"max_soft_tokens": self.vision_tokens}},
                normalize_embeddings=True,
                show_progress_bar=False,
            )
        return np.asarray(vector, dtype=np.float32)

    def _encode_query(self, query: str):
        import numpy as np

        with self._inference_lock:
            model = self._load_model()
            vector = model.encode(
                query,
                prompt_name="SearchQuery",
                normalize_embeddings=True,
                show_progress_bar=False,
            )
        return np.asarray(vector, dtype=np.float32)

    def _iter_images(self, root: RootSpec):
        root_path = Path(os.path.expandvars(root.path)).expanduser()
        if not root_path.exists() or not root_path.is_dir():
            return
        if root.recursive:
            iterator = root_path.rglob("*")
        else:
            iterator = root_path.iterdir()
        for path in iterator:
            try:
                if path.is_file() and path.suffix.lower() in IMAGE_EXTS:
                    yield path.resolve()
            except (OSError, PermissionError):
                continue

    def scan(self, roots: list[RootSpec] | None = None, prune_missing: bool = True) -> dict[str, Any]:
        start = time.perf_counter()
        roots = roots or self.roots
        discovered: dict[str, tuple[int, int]] = {}
        errors: list[dict[str, str]] = []

        for root in roots:
            try:
                for path in self._iter_images(root) or []:
                    try:
                        stat = path.stat()
                        discovered[str(path)] = (int(stat.st_size), int(stat.st_mtime_ns))
                    except Exception as exc:
                        errors.append({"path": str(path), "error": str(exc)})
            except Exception as exc:
                errors.append({"path": root.path, "error": str(exc)})

        with self._db_lock, self._connect() as conn:
            existing_rows = conn.execute(
                "SELECT path,size,mtime_ns FROM files WHERE kind='image'"
            ).fetchall()
        existing = {
            row["path"]: (int(row["size"]), int(row["mtime_ns"])) for row in existing_rows
        }

        changed = [
            path for path, signature in discovered.items()
            if existing.get(path) != signature
        ]
        unchanged = len(discovered) - len(changed)
        added = sum(1 for path in changed if path not in existing)
        updated = len(changed) - added

        if changed:
            self._begin_activity()
            try:
                for path_text in changed:
                    path = Path(path_text)
                    try:
                        vector = self._encode_image(path)
                        size, mtime_ns = discovered[path_text]
                        with self._db_lock, self._connect() as conn:
                            conn.execute(
                                """
                                INSERT INTO files(path,kind,size,mtime_ns,embedding,dim,indexed_at_utc)
                                VALUES(?,?,?,?,?,?,?)
                                ON CONFLICT(path) DO UPDATE SET
                                  kind=excluded.kind,
                                  size=excluded.size,
                                  mtime_ns=excluded.mtime_ns,
                                  embedding=excluded.embedding,
                                  dim=excluded.dim,
                                  indexed_at_utc=excluded.indexed_at_utc
                                """,
                                (
                                    path_text,
                                    "image",
                                    size,
                                    mtime_ns,
                                    vector.tobytes(),
                                    int(vector.shape[0]),
                                    utc_now(),
                                ),
                            )
                    except Exception as exc:
                        errors.append({"path": path_text, "error": f"{type(exc).__name__}: {exc}"})
            finally:
                self._end_activity()

        removed = 0
        if prune_missing and roots:
            normalized_roots = []
            for root in roots:
                try:
                    normalized_roots.append(str(Path(os.path.expandvars(root.path)).expanduser().resolve()))
                except Exception:
                    pass
            stale = []
            for old_path in existing:
                in_scope = any(
                    old_path.lower() == rp.lower()
                    or old_path.lower().startswith(rp.rstrip("\\/").lower() + os.sep.lower())
                    for rp in normalized_roots
                )
                if in_scope and old_path not in discovered:
                    stale.append(old_path)
            if stale:
                with self._db_lock, self._connect() as conn:
                    conn.executemany("DELETE FROM files WHERE path=?", [(p,) for p in stale])
                removed = len(stale)

        result = {
            "ok": True,
            "roots": [{"path": r.path, "recursive": r.recursive} for r in roots],
            "discovered": len(discovered),
            "changed": len(changed),
            "added": added,
            "updated": updated,
            "unchanged": unchanged,
            "removed": removed,
            "errors": errors[:50],
            "errorCount": len(errors),
            "elapsedMs": int((time.perf_counter() - start) * 1000),
            "completedAtUtc": utc_now(),
        }
        self._last_scan_result = result
        with self._db_lock, self._connect() as conn:
            conn.execute(
                "INSERT OR REPLACE INTO meta(key,value) VALUES('last_scan',?)",
                (json.dumps(result, ensure_ascii=False),),
            )
        return result

    def search(self, query: str, top_k: int = 8, min_score: float | None = None) -> dict[str, Any]:
        import numpy as np

        query = (query or "").strip()
        if not query:
            raise ValueError("query is required")
        top_k = max(1, min(int(top_k), 50))
        threshold = self.default_min_score if min_score is None else float(min_score)
        threshold = max(-1.0, min(threshold, 1.0))

        with self._db_lock, self._connect() as conn:
            rows = conn.execute(
                "SELECT path,size,mtime_ns,embedding,dim,indexed_at_utc FROM files WHERE kind='image'"
            ).fetchall()
        if not rows:
            return {
                "ok": True,
                "query": query,
                "threshold": threshold,
                "hasConfidentMatch": False,
                "items": [],
                "count": 0,
                "indexedCount": 0,
                "reason": "index_empty",
            }

        self._begin_activity()
        try:
            q = self._encode_query(query)
            scored: list[tuple[float, sqlite3.Row]] = []
            for row in rows:
                vec = np.frombuffer(row["embedding"], dtype=np.float32, count=int(row["dim"]))
                if vec.shape[0] != q.shape[0]:
                    continue
                scored.append((float(np.dot(vec, q)), row))
        finally:
            self._end_activity()

        scored.sort(key=lambda item: item[0], reverse=True)
        nearest = scored[:top_k]
        accepted = [(score, row) for score, row in nearest if score >= threshold]
        top_score = nearest[0][0] if nearest else None
        items = [
            {
                "path": row["path"],
                "name": Path(row["path"]).name,
                "score": round(score, 6),
                "kind": "image",
                "size": int(row["size"]),
                "modifiedAtUtc": datetime.fromtimestamp(
                    int(row["mtime_ns"]) / 1_000_000_000, tz=timezone.utc
                ).isoformat(),
                "indexedAtUtc": row["indexed_at_utc"],
            }
            for score, row in accepted
        ]
        return {
            "ok": True,
            "query": query,
            "threshold": threshold,
            "hasConfidentMatch": bool(items),
            "topScore": round(top_score, 6) if top_score is not None else None,
            "items": items,
            "count": len(items),
            "indexedCount": len(rows),
            "reason": None if items else "no_confident_match",
        }

    def import_legacy_npz(self, meta_path: Path, npz_path: Path) -> dict[str, Any]:
        import numpy as np

        if not meta_path.exists() or not npz_path.exists():
            return {"ok": False, "imported": 0, "reason": "legacy_files_missing"}
        meta = load_json(meta_path)
        paths = meta.get("paths") or []
        embeddings = np.load(npz_path)["embeddings"]
        imported = 0
        skipped = 0
        with self._db_lock, self._connect() as conn:
            for idx, path_text in enumerate(paths):
                if idx >= len(embeddings):
                    break
                path = Path(path_text)
                if not path.exists():
                    skipped += 1
                    continue
                try:
                    stat = path.stat()
                    vector = np.asarray(embeddings[idx], dtype=np.float32)
                    conn.execute(
                        """
                        INSERT OR REPLACE INTO files(path,kind,size,mtime_ns,embedding,dim,indexed_at_utc)
                        VALUES(?,?,?,?,?,?,?)
                        """,
                        (
                            str(path.resolve()),
                            "image",
                            int(stat.st_size),
                            int(stat.st_mtime_ns),
                            vector.tobytes(),
                            int(vector.shape[0]),
                            utc_now(),
                        ),
                    )
                    imported += 1
                except Exception:
                    skipped += 1
        return {"ok": True, "imported": imported, "skipped": skipped}

    def status(self) -> dict[str, Any]:
        with self._db_lock, self._connect() as conn:
            count = int(conn.execute("SELECT COUNT(*) FROM files WHERE kind='image'").fetchone()[0])
            last_scan_row = conn.execute("SELECT value FROM meta WHERE key='last_scan'").fetchone()
        last_scan = None
        if last_scan_row:
            try:
                last_scan = json.loads(last_scan_row[0])
            except Exception:
                pass
        with self._model_lock:
            model_loaded = self._model is not None
        return {
            "ok": True,
            "version": SERVICE_VERSION,
            "modelId": MODEL_ID,
            "modelLoaded": model_loaded,
            "indexedCount": count,
            "minScore": self.default_min_score,
            "visionTokens": self.vision_tokens,
            "idleUnloadSeconds": self.idle_unload_seconds,
            "idleExitSeconds": self.idle_exit_seconds,
            "scanBeforeSearch": self.scan_before_search,
            "watchIntervalSeconds": self.watch_interval_seconds,
            "roots": [{"path": r.path, "recursive": r.recursive} for r in self.roots],
            "dbPath": str(self.db_path),
            "modelCache": str(self.model_cache),
            "lastScan": last_scan,
        }

    def set_shutdown_callback(self, callback) -> None:
        self._shutdown_callback = callback

    def background_loop(self) -> None:
        next_scan = time.monotonic() + max(10, self.watch_interval_seconds)
        while not self._stop.wait(5):
            with self._activity_lock:
                active = self._active_operations
                idle_for = time.monotonic() - self._last_model_use if self._last_model_use else 0.0
            if self.idle_exit_seconds > 0 and self._ever_loaded and active == 0 and idle_for >= self.idle_exit_seconds:
                try:
                    self.unload_model_if_idle(force=True)
                finally:
                    callback = self._shutdown_callback
                    if callback is not None:
                        callback()
                    return
            try:
                self.unload_model_if_idle()
            except Exception:
                pass
            if self.watch_interval_seconds > 0 and time.monotonic() >= next_scan:
                try:
                    self.scan()
                except Exception:
                    traceback.print_exc()
                next_scan = time.monotonic() + max(10, self.watch_interval_seconds)

    def stop(self) -> None:
        self._stop.set()
        self.unload_model_if_idle(force=True)


class Handler(BaseHTTPRequestHandler):
    server_version = "YanziSemanticIndex/" + SERVICE_VERSION

    @property
    def app(self) -> SemanticIndex:
        return self.server.app  # type: ignore[attr-defined]

    @property
    def expected_token(self) -> str:
        return self.server.token  # type: ignore[attr-defined]

    def log_message(self, format: str, *args: Any) -> None:
        return

    def _authorized(self) -> bool:
        if not self.expected_token:
            return True
        return self.headers.get("X-Yanzi-Semantic-Token", "") == self.expected_token

    def _read_json(self) -> dict[str, Any]:
        length = int(self.headers.get("Content-Length") or 0)
        if length <= 0:
            return {}
        raw = self.rfile.read(min(length, 4 * 1024 * 1024))
        value = json.loads(raw.decode("utf-8"))
        if not isinstance(value, dict):
            raise ValueError("JSON body must be an object")
        return value

    def _json(self, status: int, value: Any) -> None:
        raw = json.dumps(value, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(raw)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(raw)

    def do_GET(self) -> None:
        path = urlparse(self.path).path
        if path == "/health":
            self._json(200, self.app.status())
            return
        if not self._authorized():
            self._json(401, {"ok": False, "error": "unauthorized"})
            return
        if path == "/status":
            self._json(200, self.app.status())
            return
        self._json(404, {"ok": False, "error": "not_found"})

    def do_POST(self) -> None:
        path = urlparse(self.path).path
        if not self._authorized():
            self._json(401, {"ok": False, "error": "unauthorized"})
            return
        try:
            body = self._read_json()
            if path == "/search":
                if self.app.scan_before_search:
                    self.app.scan()
                result = self.app.search(
                    str(body.get("query") or ""),
                    int(body.get("topK") or 8),
                    body.get("minScore"),
                )
                self._json(200, result)
                return
            if path == "/scan":
                roots_value = body.get("roots")
                roots = None
                if isinstance(roots_value, list):
                    roots = [RootSpec.from_value(v) for v in roots_value]
                    roots = [r for r in roots if r.path]
                result = self.app.scan(
                    roots=roots,
                    prune_missing=bool(body.get("pruneMissing", True)),
                )
                self._json(200, result)
                return
            if path == "/model/unload":
                unloaded = self.app.unload_model_if_idle(force=True)
                self._json(200, {"ok": True, "unloaded": unloaded})
                return
            self._json(404, {"ok": False, "error": "not_found"})
        except ValueError as exc:
            self._json(400, {"ok": False, "error": str(exc)})
        except Exception as exc:
            traceback.print_exc()
            self._json(500, {"ok": False, "error": type(exc).__name__, "detail": str(exc)})


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--config", required=True)
    ap.add_argument("--import-meta")
    ap.add_argument("--import-npz")
    ap.add_argument("--scan-once", action="store_true")
    ap.add_argument("--search")
    args = ap.parse_args()

    config_path = Path(args.config)
    app = SemanticIndex(config_path)

    if args.import_meta and args.import_npz:
        result = app.import_legacy_npz(Path(args.import_meta), Path(args.import_npz))
        print(json.dumps(result, ensure_ascii=False))
        if not result.get("ok"):
            return 2

    if args.scan_once:
        print(json.dumps(app.scan(), ensure_ascii=False))
        return 0

    if args.search:
        print(json.dumps(app.search(args.search), ensure_ascii=False))
        return 0

    host = str(app.config.get("host") or "127.0.0.1")
    port = int(app.config.get("port") or 53931)
    token = str(app.config.get("token") or "")
    server = ThreadingHTTPServer((host, port), Handler)
    server.app = app  # type: ignore[attr-defined]
    server.token = token  # type: ignore[attr-defined]
    app.set_shutdown_callback(server.shutdown)

    bg = threading.Thread(target=app.background_loop, name="semantic-index-maintenance", daemon=True)
    bg.start()
    print(json.dumps({"ok": True, "listening": f"http://{host}:{port}", "version": SERVICE_VERSION}), flush=True)
    try:
        server.serve_forever(poll_interval=0.5)
    except KeyboardInterrupt:
        pass
    finally:
        server.shutdown()
        server.server_close()
        app.stop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
