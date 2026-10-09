"""Read-only Baidu Netdisk desktop transfer history (Windows 8.8.3.101).

Uses official desktop client's SQLite upload.db and transmission.db.
No login credentials, cookies, network calls, or writes to client state.
Results are client task-history evidence, NOT cloud-file existence proofs.
"""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Optional
import hashlib
import os
import sqlite3


class BaiduTransferIndexError(RuntimeError):
    pass


@dataclass(frozen=True)
class BaiduTransferRecord:
    kind: str
    local_path: str
    server_path: str
    file_size: int
    error_code: int
    started_at: int
    finished_at: int

    @property
    def completed(self) -> bool:
        return (
            self.file_size >= 0
            and self.error_code == 0
            and self.started_at > 0
            and self.finished_at >= self.started_at
            and bool(self.server_path)
        )


def sha256_file(path: Path | str) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


class BaiduTransferIndex:
    def __init__(self, client_root: Path | str | None = None):
        root = Path(client_root) if client_root is not None else (
            Path(os.environ["APPDATA"]) / "baidu" / "BaiduNetdisk"
        )
        if (root / "upload.db").is_file() and (root / "transmission.db").is_file():
            self.db_dir = root
        else:
            if not root.is_dir():
                raise BaiduTransferIndexError("Baidu client data directory missing")
            # Never guess which account DB belongs to the active account.
            # Baidu 8.x stores per-account transfer DBs a few directory levels
            # beneath the roaming application root, not as direct children.
            # Identify a UNIQUE DB pair; do not guess a different account.
            candidates = [p.parent for p in root.rglob("upload.db")
                          if p.is_file()]
            matches = [p for p in candidates
                       if (p / "transmission.db").is_file()]
            if len(matches) != 1:
                raise BaiduTransferIndexError(
                    "Unable to uniquely identify one Baidu desktop account "
                    "transfer database"
                )
            self.db_dir = matches[0]

    def _db(self, kind: str):
        if kind not in ("upload", "download"):
            raise ValueError("kind must be 'upload' or 'download'")
        file = "upload.db" if kind == "upload" else "transmission.db"
        database = self.db_dir / file
        if not database.is_file():
            raise BaiduTransferIndexError("Expected Baidu transfer history DB missing")
        conn = sqlite3.connect(database.as_uri() + "?mode=ro", uri=True, timeout=4)
        conn.row_factory = sqlite3.Row
        return conn

    def _history(self, kind: str, *, local_path: str | Path,
                 after_seconds: int = 0) -> list[BaiduTransferRecord]:
        if kind not in ("upload", "download"):
            raise ValueError("Invalid transfer direction")
        local = Path(local_path).resolve(strict=True)
        if not local.is_file():
            raise ValueError("Only existing local files can be verified")
        table = "upload_history_file" if kind == "upload" else "download_history_file"
        conn = self._db(kind)
        try:
            rows = conn.execute(
                f"SELECT local_path,server_path,isdir,file_size,op_starttime,"
                f"op_endtime,error_code FROM {table} "
                "WHERE local_path=? AND isdir=0 AND op_starttime>=? "
                "ORDER BY op_endtime DESC LIMIT 30",
                (str(local), after_seconds),
            ).fetchall()
        finally:
            conn.close()
        results = []
        for row in rows:
            results.append(BaiduTransferRecord(
                kind=kind,
                local_path=str(row["local_path"] or ""),
                server_path=str(row["server_path"] or ""),
                file_size=int(row["file_size"] or 0),
                error_code=(int(row["error_code"])
                            if row["error_code"] is not None else -1),
                started_at=int(row["op_starttime"] or 0),
                finished_at=int(row["op_endtime"] or 0),
            ))
        return results

    def resolve_completed(self, kind: str, local_path: str | Path,
                          *, after_seconds: int = 0,
                          expected_server_path: Optional[str] = None,
                          expected_size: Optional[int] = None
                          ) -> Optional[BaiduTransferRecord]:
        path = Path(local_path).resolve(strict=True)
        local_size = path.stat().st_size
        # If caller did not supply an expected size, insist it matches local file.
        expected = expected_size if expected_size is not None else local_size
        matches = [
            row for row in self._history(
                kind, local_path=path, after_seconds=after_seconds
            )
            if row.completed and row.file_size == expected
            and (expected_server_path is None
                 or row.server_path == expected_server_path)
        ]
        if not matches:
            return None
        unique_remote = {record.server_path for record in matches}
        if len(unique_remote) != 1:
            raise BaiduTransferIndexError(
                "Ambiguous same-local-file history with multiple cloud paths"
            )
        return matches[0]

    def verify_roundtrip(self, source: Path | str,
                         downloaded: Path | str) -> dict:
        original = Path(source).resolve(strict=True)
        target = Path(downloaded).resolve(strict=True)
        if original == target:
            raise ValueError("Download path must differ from original file path")
        upload = self.resolve_completed("upload", original)
        if upload is None:
            raise BaiduTransferIndexError("No completed upload record for source")
        download = self.resolve_completed(
            "download", target, expected_server_path=upload.server_path,
            expected_size=original.stat().st_size,
        )
        if download is None:
            raise BaiduTransferIndexError(
                "No completed download record matching the cloud server path"
            )
        original_hash = sha256_file(original)
        returned_hash = sha256_file(target)
        if original_hash != returned_hash:
            raise BaiduTransferIndexError("Downloaded SHA-256 differs from source")
        return {
            "confirmed": True,
            "client": "Baidu Netdisk Desktop",
            "sourcePath": str(original),
            "downloadPath": str(target),
            "cloudPath": upload.server_path,
            "bytes": original.stat().st_size,
            "sha256": original_hash,
            "upload": {"completed": True, "startedAt": upload.started_at,
                       "finishedAt": upload.finished_at},
            "download": {"completed": True, "startedAt": download.started_at,
                         "finishedAt": download.finished_at},
            "evidence": "desktop-transfer-history+sha256",
            "liveCloudExistenceVerified": False,
        }
