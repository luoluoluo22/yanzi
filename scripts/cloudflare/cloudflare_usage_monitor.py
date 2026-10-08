#!/usr/bin/env python3
"""Read-only, bounded Cloudflare usage analytics collector for Yanzi (Python 3.10+)."""
import argparse
import datetime as dt
import json
import os
import pathlib
import stat
import sys
import tempfile
import urllib.error
import urllib.request

ACCOUNT_ID = "cc88cc0084b504db93ccd9462af37212"
DATABASE_ID = "512f99ac-4f5f-4e30-affb-58fc4da94bfa"
WORKER = "yanzi-sync"
LIMITS = {"rows_read": 5_000_000, "rows_written": 100_000, "workers_requests": 100_000}
ROOT = pathlib.Path("/var/lib/yanzi-cloud-monitor")
CREDS = pathlib.Path("/etc/yanzi-cloud-monitor/credentials.env")

QUERY = """
query Usage($tag:String!, $start:Date!, $end:Date!, $from:Time!, $to:Time!, $db:String!) {
 viewer { accounts(filter:{accountTag:$tag}) {
  allD1: d1AnalyticsAdaptiveGroups(limit:1000,filter:{date_geq:$start,date_leq:$end}) {
   dimensions { date databaseId }
   sum { rowsRead rowsWritten readQueries writeQueries }
  }
  yanziD1: d1AnalyticsAdaptiveGroups(limit:1000,filter:{date_geq:$start,date_leq:$end,databaseId:$db}) {
   dimensions { date databaseId }
   sum { rowsRead rowsWritten readQueries writeQueries }
  }
  workers: workersInvocationsAdaptive(limit:10000,filter:{datetime_geq:$from,datetime_lt:$to}) {
   dimensions { datetimeHour scriptName status }
   sum { requests errors }
  }
 }}
}
"""

def load_token(path):
    # Refuse secrets accessible by group/other users.
    if path.exists():
        if stat.S_IMODE(path.stat().st_mode) & 0o077:
            raise RuntimeError("credential_permissions_insecure")
        for line in path.read_text(encoding="utf-8").splitlines():
            if line.startswith("CLOUDFLARE_API_TOKEN="):
                value = line.partition("=")[2].strip().strip("'\"")
                if value:
                    return value
    value = os.environ.get("CLOUDFLARE_API_TOKEN", "")
    if value:
        return value
    raise RuntimeError("credential_missing")

def fetch(token, start, end):
    today = dt.datetime.now(dt.timezone.utc).date()
    if end > today + dt.timedelta(days=1):
        raise RuntimeError("invalid_date_range")
    variables = {
        "tag": ACCOUNT_ID, "db": DATABASE_ID,
        "start": start.isoformat(), "end": end.isoformat(),
        "from": start.isoformat() + "T00:00:00Z",
        "to": min(dt.datetime.now(dt.timezone.utc), dt.datetime.combine(
            end + dt.timedelta(days=1), dt.time(), tzinfo=dt.timezone.utc
        )).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }
    body = json.dumps({"query": QUERY, "variables": variables}).encode("utf-8")
    req = urllib.request.Request(
        "https://api.cloudflare.com/client/v4/graphql", data=body,
        headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=40) as response:
            payload = json.load(response)
    except urllib.error.HTTPError as exc:
        raise RuntimeError("cloudflare_http_" + str(exc.code)) from None
    except urllib.error.URLError:
        raise RuntimeError("cloudflare_connection_failed") from None
    if payload.get("errors"):
        # Do not print response bodies that may contain sensitive request details.
        raise RuntimeError("cloudflare_graphql_error")
    accounts = payload.get("data", {}).get("viewer", {}).get("accounts", [])
    if len(accounts) != 1:
        raise RuntimeError("cloudflare_account_not_found")
    account = accounts[0]
    if len(account.get("workers", [])) >= 10000 or len(account.get("allD1", [])) >= 1000:
        raise RuntimeError("analytics_group_limit_reached")
    return account

def aggregate(account, date):
    day = date.isoformat()
    all_db = {"rows_read": 0, "rows_written": 0, "read_queries": 0, "write_queries": 0}
    yanzi_db = dict(all_db)
    columns = {
        "rows_read": "rowsRead", "rows_written": "rowsWritten",
        "read_queries": "readQueries", "write_queries": "writeQueries",
    }
    for name, result in (("allD1", all_db), ("yanziD1", yanzi_db)):
        for group in account.get(name, []):
            if group.get("dimensions", {}).get("date") != day:
                continue
            for dest, field in columns.items():
                result[dest] += int(group.get("sum", {}).get(field) or 0)
    requests, errors, yanzi_requests = 0, 0, 0
    per_script = {}
    for group in account.get("workers", []):
        dimensions = group.get("dimensions", {})
        if not str(dimensions.get("datetimeHour", "")).startswith(day):
            continue
        value = int(group.get("sum", {}).get("requests") or 0)
        requests += value
        errors += int(group.get("sum", {}).get("errors") or 0)
        script = str(dimensions.get("scriptName") or "(unnamed)")
        per_script[script] = per_script.get(script, 0) + value
        if script == WORKER:
            yanzi_requests += value
    return {
        "d1_all": all_db, "d1_yanzi": yanzi_db,
        "workers_requests": requests, "yanzi_requests": yanzi_requests,
        "workers_errors": errors,
        "workers_by_script": dict(sorted(per_script.items(), key=lambda x: -x[1])),
    }

def assess(today, yesterday):
    values = {
        "rows_read": today["d1_all"]["rows_read"],
        "rows_written": today["d1_all"]["rows_written"],
        "workers_requests": today["workers_requests"],
    }
    prior = {
        "rows_read": yesterday["d1_all"]["rows_read"],
        "rows_written": yesterday["d1_all"]["rows_written"],
        "workers_requests": yesterday["workers_requests"],
    }
    metrics = {}
    alerts = []
    for key, value in values.items():
        cap = LIMITS[key]
        used_pct = round(100.0 * value / cap, 2)
        delta = round(100.0 * (value - prior[key]) / prior[key], 2) if prior[key] else None
        metrics[key] = {
            "used": value, "limit": cap, "used_percent": used_pct,
            "remaining": max(0, cap - value), "previous": prior[key], "change_percent": delta,
        }
        if used_pct >= 90:
            alerts.append({"severity": "critical", "metric": key, "reason": "quota_near_limit"})
        elif used_pct >= 75:
            alerts.append({"severity": "warning", "metric": key, "reason": "quota_75_percent"})
        if delta is not None and delta >= 25 and used_pct >= 20:
            alerts.append({"severity": "watch", "metric": key, "reason": "usage_regression"})
    if today["workers_errors"]:
        alerts.append({"severity": "watch", "metric": "workers_errors", "reason": "worker_errors"})
    return metrics, alerts

def write_json(path, data):
    path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    fd, tmpname = tempfile.mkstemp(prefix=".tmp-", dir=str(path.parent))
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump(data, stream, ensure_ascii=False, indent=2, sort_keys=True)
            stream.write("\n")
        os.chmod(tmpname, 0o600)
        os.replace(tmpname, path)
    finally:
        if os.path.exists(tmpname):
            os.unlink(tmpname)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mode", choices=("daily", "hourly"), default="daily")
    parser.add_argument("--date", help="UTC day YYYY-MM-DD; default previous complete day for daily")
    parser.add_argument("--credentials", type=pathlib.Path, default=CREDS)
    parser.add_argument("--output", type=pathlib.Path, default=ROOT)
    args = parser.parse_args()
    now = dt.datetime.now(dt.timezone.utc)
    day = dt.date.fromisoformat(args.date) if args.date else now.date() - dt.timedelta(days=int(args.mode == "daily"))
    if day > now.date():
        raise RuntimeError("future_date_not_supported")
    try:
        token = load_token(args.credentials)
        data = fetch(token, day - dt.timedelta(days=1), day)
        current = aggregate(data, day)
        previous = aggregate(data, day - dt.timedelta(days=1))
        metrics, alerts = assess(current, previous)
        report = {
            "date_utc": day.isoformat(), "mode": args.mode, "generated_at_utc": now.isoformat(),
            "complete": day < now.date(), "account_id": ACCOUNT_ID, "database_id": DATABASE_ID,
            "metrics": metrics, "yanzi_d1": current["d1_yanzi"],
            "yanzi_workers_requests": current["yanzi_requests"],
            "workers_errors": current["workers_errors"], "workers_by_script": current["workers_by_script"],
            "alerts": alerts, "source": "cloudflare_graphql_api",
        }
        if args.mode == "daily":
            write_json(args.output / "daily" / (day.isoformat() + ".json"), report)
            write_json(args.output / "latest.json", report)
        else:
            write_json(args.output / "live.json", report)
        print(json.dumps({
            "status": "ok", "date_utc": day.isoformat(), "mode": args.mode,
            "complete": report["complete"], "metrics": metrics, "alerts": alerts,
        }, ensure_ascii=False))
        return 0
    except Exception as exc:
        # Never log tokens, request headers or Cloudflare response bodies.
        failure = {"status": "failed", "mode": args.mode, "date_utc": day.isoformat(),
                   "generated_at_utc": now.isoformat(), "error_code": str(exc).splitlines()[0][:90]}
        write_json(args.output / "last_error.json", failure)
        print(json.dumps(failure), file=sys.stderr)
        return 2

if __name__ == "__main__":
    sys.exit(main())
