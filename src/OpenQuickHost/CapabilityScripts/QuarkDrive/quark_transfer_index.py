"""Read-only Quark desktop transfer-task index.

Reads ONLY Quark's local upload.db / download.db metadata. Does not call the
Quark private IPC, inspect account credentials, or modify SQLite databases.

A successful upload task returns the remote file fid *after* the client has
reported FINISH and all bytes transferred; a successful download task can
be correlated with the same fid and a local output path.

This is a transfer-history resolver, NOT a general-purpose cloud file browser.
"""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Optional
import re
import sqlite3
import time


class TransferIndexError(RuntimeError):
    pass


class AmbiguousTransfer(TransferIndexError):
    pass


@dataclass(frozen=True)
class TransferRecord:
    task_id: str
    name: str
    fid: str
    dir_fid: str
    size: int
    local_path: str
    status: str
    progress: float
    finished_size: int
    created_at_ms: int
    finished_at_ms: int

    @property
    def completed(self) -> bool:
        return (
            self.status == "FINISH"
            and self.progress >= 100.0
            and self.finished_size == self.size
            and self.finished_at_ms > 0
            and bool(re.fullmatch(r"[a-fA-F0-9]{32}", self.fid))
        )


class QuarkTransferIndex:
    """Read local task state through parameterized, SELECT-only queries."""

    def __init__(self, persistence_dir: Optional[Path | str] = None):
        if persistence_dir is None:
            persistence_dir = (
                Path.home() / "AppData" / "Local" / "Quark"
                / "User Data" / "persistence"
            )
        root = Path(persistence_dir)
        if (root / "upload.db").is_file() and (root / "download.db").is_file():
            self.account_dir = root
        else:
            dirs = [p for p in root.iterdir() if p.is_dir()] if root.is_dir() else []
            matches = [
                p for p in dirs
                if (p / "upload.db").is_file() and (p / "download.db").is_file()
            ]
            if len(matches) != 1:
                raise AmbiguousTransfer(
                    "Unable to uniquely identify Quark's current account transfer "
                    "database; specify a known account persistence directory"
                )
            self.account_dir = matches[0]

    def _lookup(self, kind: str, *, name: str, local_path: Path) -> list[TransferRecord]:
        if kind not in ("upload", "download"):
            raise ValueError("Only upload/download transfer tasks are allowed")
        if not name or name != Path(name).name:
            raise ValueError("Expected a plain filename")
        path = local_path.resolve()
        table = "upload_task" if kind == "upload" else "download_task"
        database = self.account_dir / (kind + ".db")
        if not database.is_file():
            raise TransferIndexError("Quark transfer database not found")
        # SQLITE_OPEN_READONLY (never uses rwc/CREATE); no SQL interpolated from callers.
        db = sqlite3.connect(database.as_uri() + "?mode=ro", uri=True, timeout=4)
        db.row_factory = sqlite3.Row
        try:
            records = db.execute(
                "SELECT id, name, fid, dirFid, size, path, status, progress, "
                "finishSize, createTime, finishTime FROM " + table
                + " WHERE name=? AND path=? ORDER BY createTime DESC LIMIT 50",
                (name, str(path)),
            ).fetchall()
        finally:
            db.close()
        return [
            TransferRecord(
                task_id=str(row["id"] or ""),
                name=str(row["name"] or ""),
                fid=str(row["fid"] or ""),
                dir_fid=str(row["dirFid"] or ""),
                size=int(row["size"] or 0),
                local_path=str(row["path"] or ""),
                status=str(row["status"] or ""),
                progress=float(row["progress"] or 0),
                finished_size=int(row["finishSize"] or 0),
                created_at_ms=int(row["createTime"] or 0),
                finished_at_ms=int(row["finishTime"] or 0),
            )
            for row in records
        ]

    def resolve_completed_upload(
        self,
        local_file: Path | str,
        *,
        after_ms: int = 0,
    ) -> Optional[TransferRecord]:
        path = Path(local_file).resolve(strict=True)
        if not path.is_file():
            raise ValueError("Upload source must be a regular file")
        possible = self._lookup("upload", name=path.name, local_path=path)
        candidates = [
            r for r in possible
            if r.size == path.stat().st_size and r.completed
            and r.created_at_ms >= after_ms
        ]
        if not candidates:
            return None
        fids = set(r.fid for r in candidates)
        if len(fids) != 1:
            raise AmbiguousTransfer("Same local file has multiple cloud IDs")
        return candidates[0]

    def resolve_completed_download(
        self,
        local_file: Path | str,
        *,
        expected_fid: Optional[str] = None,
        after_ms: int = 0,
    ) -> Optional[TransferRecord]:
        path = Path(local_file).resolve(strict=True)
        if not path.is_file():
            raise ValueError("Download target must be a regular file")
        possible = self._lookup("download", name=path.name, local_path=path)
        candidates = [
            r for r in possible
            if r.completed and r.size == path.stat().st_size
            and r.created_at_ms >= after_ms
            and (expected_fid is None or r.fid == expected_fid)
        ]
        if not candidates:
            return None
        fids = set(r.fid for r in candidates)
        if len(fids) != 1:
            raise AmbiguousTransfer("Download metadata has multiple cloud IDs")
        return candidates[0]

    def wait_for_upload(
        self,
        local_file: Path | str,
        *,
        after_ms: int,
        timeout: float = 90.0,
        interval: float = 0.65,
    ) -> TransferRecord:
        limit = time.monotonic() + timeout
        while time.monotonic() < limit:
            match = self.resolve_completed_upload(local_file, after_ms=after_ms)
            if match:
                return match
            time.sleep(interval)
        raise TransferIndexError(
            "Timed out waiting for Quark's persisted FINISH status "
            "for the exact local upload"
        )
