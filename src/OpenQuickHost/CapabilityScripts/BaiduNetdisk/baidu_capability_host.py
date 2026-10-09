"""Restricted JSON bridge for Baidu's read-only official desktop history."""
from __future__ import annotations
from pathlib import Path
import json
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))


def dispatch(value: dict):
    from baidu_transfer_index import BaiduTransferIndex,sha256_file
    op=value.get("operation")
    if not isinstance(op,str):
        raise ValueError("operation must be specified")
    if op=="downloadExact":
        if value.get("confirm") is not True:
            raise PermissionError("Baidu desktop download requires confirm=true")
        from baidu_desktop_download import download_exact
        return download_exact(
            filename=value.get("filename"),
            expected_cloud_path=value.get("expectedCloudPath"),
            expected_size=value.get("expectedSize"),
            expected_sha256=value.get("expectedSha256"),
            copy_to_folder=value.get("copyToFolder"),
            timeout_seconds=value.get("timeoutSeconds",90),
        )
    if op=="searchExact":
        from baidu_desktop_search import search_exact_visible
        filename=value.get("filename")
        wait=value.get("waitSeconds",12)
        return search_exact_visible(filename,wait_seconds=wait)
    idx=BaiduTransferIndex()
    if op=="transferStatus":
        kind=value.get("kind")
        if kind not in ("upload","download"):
            raise ValueError("kind must be upload or download")
        path=value.get("path")
        if not isinstance(path,str) or not path:
            raise ValueError("path must be a nonempty local filename")
        local=Path(path).resolve(strict=False)
        after=value.get("afterSeconds",0)
        if not isinstance(after,int) or after<0:
            raise ValueError("afterSeconds must be a nonnegative integer")
        record=idx.resolve_latest(kind,local,after_seconds=after) if local.is_file() else None
        if record is None:
            active=idx.resolve_active(kind,local,after_seconds=after)
            if active is not None:
                return {
                    "found":True, "completed":False,"failed":False,
                    "phase":"active", "kind":kind,
                    "localPath":active.local_path,
                    "cloudPath":active.server_path,
                    "size":active.file_size,
                    "completedBytes":active.complete_size,
                    "progressPercent":active.progress_percent,
                    "clientStatusCode":active.status_code,
                    "errorCode":active.error_code,
                    "source":"baidu-desktop-active-transfer",
                    "liveCloudExistenceVerified":False,
                }
            return {"found":False,"completed":False,"failed":False,
                    "phase":"unknown","direction":kind}
        failed=record.error_code!=0 and record.finished_at>=record.started_at>0
        return {
            "found":True,
            "completed":record.completed,
            "failed":failed,
            "phase":"failed" if failed else "completed" if record.completed else "unknown",
            "kind":kind,
            "localPath":record.local_path,
            "cloudPath":record.server_path,
            "size":record.file_size,
            "errorCode":record.error_code,
            "startedAt":record.started_at,
            "finishedAt":record.finished_at,
            "source":"baidu-desktop-transfer-history",
            "liveCloudExistenceVerified":False,
        }
    if op=="roundtripVerify":
        original=value.get("originalLocalFile")
        downloaded=value.get("downloadedFile")
        if not isinstance(original,str) or not isinstance(downloaded,str):
            raise ValueError("Both source and downloaded paths must be supplied")
        return idx.verify_roundtrip(original,downloaded)
    raise ValueError("Unsupported Baidu read-only operation")


def main():
    try:
        req=json.loads(sys.stdin.read(1024*1024))
        if not isinstance(req,dict):raise ValueError("Expected a JSON object")
        print(json.dumps({"ok":True,"result":dispatch(req)},ensure_ascii=False))
    except Exception as e:
        print(json.dumps({
            "ok":False,
            "code":type(e).__name__,
            "error":str(e)[:220],
        },ensure_ascii=False))
        return 2
    return 0


if __name__=="__main__":
    sys.exit(main())
