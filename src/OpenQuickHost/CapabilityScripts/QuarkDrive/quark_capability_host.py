"""Yanzi-hosted Quark adapter bridge. One JSON request via stdin, one JSON result.

Does NOT read or export Quark cookies, auth tokens or private file contents.
No generic command execution, untrusted method dispatch or private HTTP APIs.
It runs only bundled, previously verified Quark desktop helper functions.
"""
from __future__ import annotations

import json
from pathlib import Path
import sys
# -I disables PYTHONPATH and the working directory; explicitly allow only the
# trusted sibling helper modules shipped in this same signed release payload.
sys.path.insert(0, str(Path(__file__).resolve().parent))


def _string(req, key: str) -> str:
    value = req.get(key)
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{key} must be a non-empty string")
    return value.strip()


def _require_confirmed(req):
    if req.get("confirm") is not True:
        raise PermissionError("This operation requires explicit confirm=true")


def _serialize_task(record):
    if record is None:
        return {"found": False, "completed": False}
    return {
        "found": True,
        "completed": record.completed,
        "name": record.name,
        "fid": record.fid,
        "dirFid": record.dir_fid,
        "size": record.size,
        "status": record.status,
        "progress": record.progress,
        "finishedBytes": record.finished_size,
        "finishedAt": record.finished_at_ms,
    }


def dispatch(req: dict):
    op = _string(req, "operation")
    if op == "probe":
        import importlib.util
        modules = ("win32gui", "psutil", "uiautomation", "pynput", "cv2", "PIL", "numpy")
        missing = [name for name in modules if importlib.util.find_spec(name) is None]
        return {
            "available": not missing,
            "missingPythonModules": missing,
            "adapterVersion": "0.1.0",
            "transport": "local-desktop-client",
            "headless": False,
            "liveGlobalSearch": False,
        }

    from quark_transfer_index import QuarkTransferIndex
    if op == "transferStatus":
        local_file = Path(_string(req, "path")).expanduser().resolve(strict=True)
        kind = _string(req, "kind")
        idx = QuarkTransferIndex()
        if kind == "upload":
            return _serialize_task(idx.resolve_completed_upload(local_file))
        if kind == "download":
            return _serialize_task(idx.resolve_completed_download(local_file))
        raise ValueError("kind must be upload or download")

    if op == "cachedLookup":
        from quark_recent_file_cache import QuarkRecentFileCache
        name = _string(req, "filename")
        parent = req.get("parentFid")
        if parent is not None and not isinstance(parent, str):
            raise ValueError("parentFid must be a string")
        candidate = QuarkRecentFileCache().lookup_candidate(name, parent_fid=parent)
        if candidate is None:
            return {"found": False, "liveVerified": False}
        return {
            "found": True,
            "name": candidate.file_name,
            "fid": candidate.fid,
            "parentFid": candidate.parent_fid,
            "liveVerified": False,
            "source": "local-cache",
        }

    if op == "cachedFolder":
        from quark_recent_file_cache import QuarkRecentFileCache
        parent = _string(req, "parentFid")
        items = QuarkRecentFileCache().list_cached_folder(parent, max_results=100)
        return {
            "parentFid": parent,
            "count": len(items),
            "liveVerified": False,
            "source": "local-cache",
            "items": [
                {"name": a.file_name, "fid": a.fid, "parentFid": a.parent_fid,
                 "liveVerified": False} for a in items
            ],
        }

    if op == "uploadVerified":
        _require_confirmed(req)
        from quark_drive_adapter import QuarkDesktopAdapter
        path = Path(_string(req, "path")).resolve(strict=True)
        if not path.is_file():
            raise ValueError("The upload source must be a regular file")
        if path.stat().st_size > 2 * 1024 * 1024 * 1024:
            raise ValueError("File is too large for this experimental adapter (2GiB limit)")
        # Do not treat file-dialog acceptance as a completed cloud transfer.
        result = QuarkDesktopAdapter().upload_file(
            path, wait_for_cloud=True, timeout=float(req.get("timeoutSeconds", 90))
        )
        return {
            "submitted": result.submitted,
            "remoteConfirmed": result.remote_confirmed,
            "fid": result.remote_fid,
            "filename": path.name,
            "size": path.stat().st_size,
            "client": "Quark Desktop",
        }

    if op == "downloadVerified":
        _require_confirmed(req)
        from quark_drive_adapter import QuarkDesktopAdapter, sha256_file
        path = Path(_string(req, "originalLocalFile")).resolve(strict=True)
        folder = Path(_string(req, "targetFolder")).resolve(strict=True)
        if not path.is_file() or not folder.is_dir():
            raise ValueError("Original local file and target directory must exist")
        target = folder / path.name
        if target.exists():
            raise FileExistsError("Destination filename already exists; overwrite prohibited")
        if path.stat().st_size > 2 * 1024 * 1024 * 1024:
            raise ValueError("File exceeds experimental adapter's 2GiB limit")
        # Target must currently be visible in the Quark folder; source file
        # must have prior completed upload, matching fid and cache entry.
        result = QuarkDesktopAdapter().download_by_name(
            path.name, folder, original_local_file=path,
            timeout=float(req.get("timeoutSeconds", 90))
        )
        if not (result.submitted and result.remote_confirmed and result.remote_fid):
            raise RuntimeError("Download completion was not verified")
        return {
            "submitted": True,
            "remoteConfirmed": True,
            "fid": result.remote_fid,
            "filename": path.name,
            "targetPath": str(target),
            "sha256": sha256_file(target),
            "client": "Quark Desktop",
        }

    if op == "globalSearchDownloadVerified":
        _require_confirmed(req)
        from quark_global_search import download_verified_by_cloud_search
        source = Path(_string(req, "originalLocalFile")).resolve(strict=True)
        target_folder = Path(_string(req, "targetFolder")).resolve(strict=True)
        if not source.is_file() or not target_folder.is_dir():
            raise ValueError("Source must be a file and destination an existing directory")
        if (target_folder / source.name).exists():
            raise FileExistsError("Destination already contains this filename")
        if source.stat().st_size > 2 * 1024 * 1024 * 1024:
            raise ValueError("File exceeds experimental adapter's 2GiB limit")
        # Search and transfer are performed by the logged-in desktop app.
        # Completion is verified against exact original source SHA and
        # Quark's read-only upload/download task fid records.
        return download_verified_by_cloud_search(
            source, target_folder,
            timeout=float(req.get("timeoutSeconds", 90)),
        )

    raise ValueError(f"Unsupported Quark operation: {op}")


def main():
    try:
        payload = sys.stdin.read(1048576)
        req = json.loads(payload)
        if not isinstance(req, dict):
            raise ValueError("Request must be a JSON object")
        result = dispatch(req)
        print(json.dumps({"ok": True, "result": result}, ensure_ascii=False))
    except Exception as error:
        # Avoid stack traces and raw diagnostic output including private paths
        # escaping the capability API. Reject with a short safe error code.
        print(json.dumps({
            "ok": False,
            "error": str(error)[:240],
            "code": type(error).__name__,
        }, ensure_ascii=False))
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
