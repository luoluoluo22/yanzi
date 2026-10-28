#!/usr/bin/env python3
"""Read-only R2 capacity watcher. Never lists private keys or deletes objects."""
import argparse
import datetime as dt
import json
import os
from pathlib import Path
import stat
import tempfile
import urllib.request

ACCOUNT_ID = "cc88cc0084b504db93ccd9462af37212"
CREDS = Path("/etc/yanzi-cloud-monitor/credentials.env")
OUTPUT = Path("/var/lib/yanzi-cloud-monitor")
BUCKETS = ("openquickhost-sync-assets", "img")
CAPACITY_WARN_BYTES = 9_000_000_000
DAILY_GROWTH_WARN_BYTES = 200_000_000
STALE_LIMIT = dt.timedelta(hours=2)
QUERY = """
query R2Storage($account:String!,$start:Time!,$end:Time!){
 viewer{accounts(filter:{accountTag:$account}){
  data:r2StorageAdaptiveGroups(limit:1000,filter:{datetime_geq:$start,datetime_leq:$end},orderBy:[datetime_DESC]){
   dimensions{bucketName datetime}
   max{payloadSize metadataSize objectCount}
  }
 }}
}
"""


def load_token():
    if CREDS.exists():
        if stat.S_IMODE(CREDS.stat().st_mode) & 0o077:
            raise RuntimeError("credential_permissions_insecure")
        for line in CREDS.read_text(encoding="utf-8").splitlines():
            if line.startswith("CLOUDFLARE_API_TOKEN="):
                token = line.partition("=")[2].strip().strip("'\"")
                if token:
                    return token
    raise RuntimeError("credential_missing")


def read_samples(token, now):
    start = dt.datetime.combine(now.date() - dt.timedelta(days=1), dt.time(), dt.timezone.utc)
    variables = {
        "account": ACCOUNT_ID,
        "start": start.strftime("%Y-%m-%dT%H:%M:%SZ"),
        "end": now.strftime("%Y-%m-%dT%H:%M:%SZ"),
    }
    body = json.dumps({"query": QUERY, "variables": variables}).encode("utf-8")
    request = urllib.request.Request(
        "https://api.cloudflare.com/client/v4/graphql",
        data=body,
        headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"},
    )
    with urllib.request.urlopen(request, timeout=40) as response:
        result = json.load(response)
    if result.get("errors"):
        raise RuntimeError("r2_graphql_error")
    accounts = result.get("data", {}).get("viewer", {}).get("accounts") or []
    if len(accounts) != 1:
        raise RuntimeError("r2_account_not_found")
    rows = accounts[0].get("data")
    if not isinstance(rows, list) or not rows or len(rows) >= 1000:
        raise RuntimeError("r2_samples_missing_or_truncated")
    return rows


def snapshot(rows, now):
    dates = (now.date(), now.date() - dt.timedelta(days=1))
    latest = {d: {} for d in dates}
    for item in rows:
        dims = item.get("dimensions") or {}
        bucket = dims.get("bucketName")
        if bucket not in BUCKETS:
            continue
        stamp = dims.get("datetime")
        try:
            timestamp = dt.datetime.fromisoformat(stamp.replace("Z", "+00:00"))
        except (ValueError, AttributeError):
            continue
        day = timestamp.date()
        if day not in latest:
            continue
        high = latest[day].get(bucket)
        if high is None or timestamp > high[0]:
            stats = item.get("max") or {}
            if not all(isinstance(stats.get(k), (int, float)) and stats[k] >= 0 for k in
                       ("payloadSize", "metadataSize", "objectCount")):
                continue
            latest[day][bucket] = (timestamp, stats)

    today = latest[dates[0]]
    if set(today) != set(BUCKETS):
        raise RuntimeError("r2_current_bucket_sample_incomplete")
    oldest = min(x[0] for x in today.values())
    if now - oldest > STALE_LIMIT or oldest > now + dt.timedelta(minutes=5):
        raise RuntimeError("r2_sample_stale")

    current_bytes = sum(x[1]["payloadSize"] + x[1]["metadataSize"] for x in today.values())
    current_objects = sum(x[1]["objectCount"] for x in today.values())
    previous = latest[dates[1]]
    previous_bytes = (
        sum(x[1]["payloadSize"] + x[1]["metadataSize"] for x in previous.values())
        if set(previous) == set(BUCKETS) else None
    )
    delta = current_bytes - previous_bytes if previous_bytes is not None else None
    return {
        "date_utc": dates[0].isoformat(),
        "sampled_at_utc": max(x[0] for x in today.values()).isoformat(),
        "oldest_bucket_sample_at_utc": oldest.isoformat(),
        "used_bytes": int(current_bytes),
        "object_count": int(current_objects),
        "daily_change_bytes": int(delta) if delta is not None else None,
        "daily_change_baseline_available": previous_bytes is not None,
        "buckets": {
            bucket: {
                "bytes": int(x[1]["payloadSize"] + x[1]["metadataSize"]),
                "object_count": int(x[1]["objectCount"]),
                "sampled_at_utc": x[0].isoformat(),
            } for bucket, x in today.items()
        },
    }


def breach_codes(data):
    result = []
    if data["used_bytes"] >= CAPACITY_WARN_BYTES:
        result.append("r2_storage_9gb")
    growth = data["daily_change_bytes"]
    if growth is not None and growth >= DAILY_GROWTH_WARN_BYTES:
        result.append("r2_daily_growth_200mb")
    return result


def atomically_save(path, obj):
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    fd, temp = tempfile.mkstemp(dir=str(path.parent), prefix=".r2-temp-")
    try:
        with os.fdopen(fd, "w", encoding="utf8") as file:
            json.dump(obj, file, ensure_ascii=False, indent=2, sort_keys=True)
            file.write("\n")
        os.chmod(temp, 0o600)
        os.replace(temp, path)
    finally:
        if os.path.exists(temp):
            os.unlink(temp)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=OUTPUT)
    args = parser.parse_args()
    now = dt.datetime.now(dt.timezone.utc)
    try:
        result = snapshot(read_samples(load_token(), now), now)
        result["checked_at_utc"] = now.isoformat()
        result["status"] = "ok"
        result["source"] = "cloudflare_r2_storage_graphql"
        active = breach_codes(result)
        state_file = args.output / "r2-alert-state.json"
        try:
            state = json.loads(state_file.read_text())
            if not isinstance(state.get("days"), dict):
                state = {"days": {}}
        except (OSError, ValueError, TypeError):
            state = {"days": {}}
        day = result["date_utc"]
        warned = set(state["days"].get(day, []))
        new_codes = [code for code in active if code not in warned]
        result["active_thresholds"] = active
        result["new_thresholds"] = new_codes
        atomically_save(args.output / "r2-live.json", result)
        if new_codes:
            state["days"][day] = sorted(warned.union(new_codes))
            state["days"] = {d: codes for d, codes in state["days"].items() if d >= (now.date()-dt.timedelta(days=14)).isoformat()}
            atomically_save(state_file, state)
        print(json.dumps({k:result[k] for k in ("status","checked_at_utc","used_bytes","daily_change_bytes","object_count","active_thresholds","new_thresholds")},ensure_ascii=False))
        return 0
    except Exception as error:
        # Do not overwrite a previous valid value with zero, or log API/credential contents.
        failure={"status":"failed","checked_at_utc":now.isoformat(),"error_class":type(error).__name__,"error_code":str(error).splitlines()[0][:70]}
        atomically_save(args.output / "r2-last-error.json", failure)
        print(json.dumps(failure),file=__import__("sys").stderr)
        return 2


if __name__=="__main__":
    raise SystemExit(main())
