"""Read-only Quark recent-file cache resolver for the installed desktop client.

The real renderer sometimes stores recent_file_list data as V8-encoded values in
Chromium's IndexedDB/LevelDB. We extract ONLY the validated adjacent fields:
fid, file_name, pdir_fid. This is not a complete V8/IndexedDB decoder.

CACHED FILES CAN BE STALE. Nothing from this module may be trusted as
live cloud existence or used alone to authorize an unattended download.
"""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from collections import defaultdict
import re


class CacheResolutionError(RuntimeError):
    pass


@dataclass(frozen=True)
class CachedFidCandidate:
    file_name: str
    fid: str
    parent_fid: str
    cache_observed: bool = True
    live_verified: bool = False


_FID_PREFIX = b'"\x03fid"\x20'
_NAME_PREFIX = b'"\x09file_name"'
_PARENT_PREFIX = b'"\x08pdir_fid"\x20'
_HEX32 = re.compile(rb"[a-fA-F0-9]{32}\Z")
_SCAN = re.compile(re.escape(_FID_PREFIX) + rb"([a-fA-F0-9]{32})" +
                   re.escape(_NAME_PREFIX))
_PARENT_RE = re.compile(r"[a-fA-F0-9]{32}\Z")


def _decode_varint(data: bytes, offset: int) -> tuple[int, int]:
    value = 0
    for position in range(5):
        if offset + position >= len(data):
            raise CacheResolutionError("Truncated filename length")
        byte = data[offset + position]
        value |= (byte & 0x7F) << (7 * position)
        if not byte & 0x80:
            return value, offset + position + 1
    raise CacheResolutionError("Oversized/invalid filename length")


def _parse_all_bytes(
    data: bytes, target_filename: str | None = None
) -> list[CachedFidCandidate]:
    wanted = target_filename.encode("utf8") if target_filename is not None else None
    found: list[CachedFidCandidate] = []
    for match in _SCAN.finditer(data):
        try:
            length, start = _decode_varint(data, match.end())
        except CacheResolutionError:
            continue
        if length < 1 or length > 512 or start + length + len(_PARENT_PREFIX) + 32 > len(data):
            continue
        name_bytes = data[start:start + length]
        if wanted is not None and name_bytes != wanted:
            continue
        next_at = start + length
        if data[next_at:next_at + len(_PARENT_PREFIX)] != _PARENT_PREFIX:
            continue
        parent = data[next_at + len(_PARENT_PREFIX):
                      next_at + len(_PARENT_PREFIX) + 32]
        if not _HEX32.fullmatch(parent):
            continue
        try:
            name = name_bytes.decode("utf8", "strict")
        except UnicodeDecodeError:
            continue
        if not name or "/" in name or "\\" in name or "\x00" in name:
            continue
        found.append(CachedFidCandidate(
            file_name=name,
            fid=match.group(1).decode("ascii"),
            parent_fid=parent.decode("ascii"),
        ))
    return found


def _parse_bytes(data: bytes, target_filename: str) -> list[CachedFidCandidate]:
    if not target_filename or Path(target_filename).name != target_filename:
        raise ValueError("Expected an exact filename")
    return _parse_all_bytes(data, target_filename)


class QuarkRecentFileCache:
    def __init__(self, indexed_db_root: Path | str | None = None):
        if indexed_db_root is None:
            indexed_db_root = (
                Path.home() / "AppData" / "Local" / "Quark"
                / "User Data" / "Default" / "IndexedDB"
            )
        self.root = Path(indexed_db_root)

    def _scan(self, file_name: str | None = None) -> list[CachedFidCandidate]:
        if not self.root.is_dir():
            raise CacheResolutionError("Quark IndexedDB cache is unavailable")
        if file_name is not None and (not file_name or Path(file_name).name != file_name):
            raise ValueError("Expected an exact filename")
        matches: list[CachedFidCandidate] = []
        total_bytes = 0
        for path in sorted(self.root.rglob("*")):
            if not path.is_file() or path.suffix.lower() not in (".log", ".ldb"):
                continue
            try:
                size = path.stat().st_size
                if size > 8 * 1024 * 1024:
                    continue
                total_bytes += size
                if total_bytes > 64 * 1024 * 1024:
                    raise CacheResolutionError("IndexedDB scan budget exceeded")
                matches.extend(_parse_all_bytes(path.read_bytes(), file_name))
            except OSError:
                # Rotation/deletion while Chromium is writing is not a match.
                continue
        return matches

    def lookup_candidate(
        self, file_name: str, *, parent_fid: str | None = None
    ) -> CachedFidCandidate | None:
        if parent_fid is not None and not _PARENT_RE.fullmatch(parent_fid):
            raise ValueError("Parent cloud ID must be 32 hex characters")
        matches = [
            item for item in self._scan(file_name)
            if parent_fid is None or item.parent_fid == parent_fid
        ]
        if not matches:
            return None
        unique = {(item.fid, item.parent_fid) for item in matches}
        if len(unique) != 1:
            raise CacheResolutionError(
                "Conflicting cached file IDs or parent folders; cannot select safely"
            )
        return matches[-1]

    def list_cached_folder(
        self, parent_fid: str, *, max_results: int = 500
    ) -> list[CachedFidCandidate]:
        """List only cached candidates in a known cloud folder, not live results."""
        if not _PARENT_RE.fullmatch(parent_fid):
            raise ValueError("Parent cloud ID must be 32 hex characters")
        if not (1 <= max_results <= 5000):
            raise ValueError("max_results must be 1..5000")
        matches = [item for item in self._scan() if item.parent_fid == parent_fid]
        unique = {}
        for item in matches:
            key = (item.file_name, item.fid)
            unique[key] = item
        if len(unique) > max_results:
            raise CacheResolutionError("Result count exceeds allowed cached-list limit")
        by_name: dict[str, set[str]] = defaultdict(set)
        for candidate in unique.values():
            by_name[candidate.file_name].add(candidate.fid)
        if any(len(ids) > 1 for ids in by_name.values()):
            raise CacheResolutionError(
                "Multiple cloud file IDs share a filename in the cached folder"
            )
        return sorted(unique.values(), key=lambda item: (item.file_name, item.fid))
