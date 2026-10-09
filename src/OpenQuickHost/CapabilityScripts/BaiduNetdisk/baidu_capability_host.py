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
    idx=BaiduTransferIndex()
    if op=="transferStatus":
        kind=value.get("kind")
        if kind not in ("upload","download"):
            raise ValueError("kind must be upload or download")
        path=value.get("path")
        if not isinstance(path,str) or not path:
            raise ValueError("path must be a nonempty local filename")
        local=Path(path).resolve(strict=True)
        after=value.get("afterSeconds",0)
        if not isinstance(after,int) or after<0:
            raise ValueError("afterSeconds must be a nonnegative integer")
        record=idx.resolve_completed(kind,local,after_seconds=after)
        if record is None:
            return {"found":False,"completed":False,"direction":kind}
        return {
            "found":True,
            "completed":record.completed,
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
