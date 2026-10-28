#!/usr/bin/env python3
"""Import a user-approved PCAPdroid CSV into a private, bounded K70 dataset.

Only aggregated statistics are printed and committed as example documentation.
Raw destination IPs, hostnames, account identifiers and HTTP URLs remain in the
user's local private CSV; never upload to Git or the Yanzi cloud.
"""
import argparse
import collections
import csv
import datetime
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

REQUIRED = ("IPProto", "SrcIP", "SrcPort", "DstIp", "DstPort", "UID", "App",
            "PackageName", "Proto", "Status", "Info", "BytesSent", "BytesRcvd",
            "PktsSent", "PktsRcvd", "FirstSeen", "LastSeen")
MAX_SOURCE = 12 * 1024 * 1024
MAX_ROWS = 120000
def safe_int(value):
    try: return max(0, int(value))
    except (ValueError, TypeError): return 0

def pull_newest(adb, serial, output_dir):
    # The exporter is deliberately UI-driven; only copy user-exported CSVs.
    result = subprocess.run(
        [adb, "-s", serial, "shell", "ls", "-t", "/sdcard/Download/PCAPdroid*.csv"],
        capture_output=True, encoding="utf-8", errors="replace", timeout=14)
    paths = [x.strip() for x in result.stdout.splitlines()
             if x.strip().startswith("/sdcard/Download/PCAPdroid")
             and x.strip().endswith(".csv")]
    if result.returncode or not paths: raise RuntimeError("no_user_exported_pcapdroid_csv")
    src = paths[0]
    dst = output_dir / "pcapdroid-connections.csv"
    cp = subprocess.run([adb, "-s", serial, "pull", src, str(dst)],
                        capture_output=True, encoding="utf-8", errors="replace", timeout=25)
    if cp.returncode: raise RuntimeError("adb_pull_failed")
    return dst, src

def aggregate(path):
    total=0
    app=collections.Counter()
    proto=collections.Counter()
    status=collections.Counter()
    ports=collections.Counter()
    endpoints=set()
    info_nonempty=0
    sent=received=packets_sent=packets_received=0
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader=csv.DictReader(handle)
        header=tuple(reader.fieldnames or ())
        if not set(REQUIRED).issubset(header): raise ValueError("unexpected_csv_schema")
        for row in reader:
            total+=1
            if total>MAX_ROWS: raise ValueError("too_many_csv_rows")
            label = (row.get("App") or "unknown").strip()
            pkg = (row.get("PackageName") or "unknown").strip()
            app[(label[:100],pkg[:150])]+=1
            proto[(row.get("Proto") or "unknown").strip()[:32]]+=1
            status[(row.get("Status") or "unknown").strip()[:32]]+=1
            ports[(row.get("DstPort") or "unknown").strip()[:8]]+=1
            endpoints.add((row.get("IPProto"),row.get("DstIp"),row.get("DstPort")))
            info_nonempty+=bool((row.get("Info") or "").strip())
            sent+=safe_int(row.get("BytesSent"))
            received+=safe_int(row.get("BytesRcvd"))
            packets_sent+=safe_int(row.get("PktsSent"))
            packets_received+=safe_int(row.get("PktsRcvd"))
    return {
        "source":"PCAPdroid 2.0.2 user-exported CSV",
        "schema_version":1,
        "record_count":total,
        "fields":list(header),
        "distinct_app_package_pairs":len(app),
        "apps":[{"app":k[0],"package":k[1],"records":v}
                for k,v in app.most_common()],
        "protocols":dict(proto.most_common()),
        "statuses":dict(status.most_common()),
        "destination_port_counts":{
            key:ports.get(key,0) for key in ("443","80","53","5222","8080")
        },
        "unique_destination_ip_port_protocol":len(endpoints),
        "records_with_info_field":info_nonempty,
        "sum_connection_bytes_sent":sent,
        "sum_connection_bytes_received":received,
        "sum_connection_packets_sent":packets_sent,
        "sum_connection_packets_received":packets_received,
        "limitations":[
            "No raw packet payload: PCAPdroid was configured with No dump",
            "HTTP/TLS/DNS protocol labels do not imply decrypted HTTPS",
            "App attribution and host names are best-effort PCAPdroid analysis",
            "Rows are app connections, not messages or HTTP API calls"
        ]
    }

def prune_private(root, days, preserve):
    cutoff=time.time()-days*86400
    removed=0
    for folder in root.glob("pcapdroid-*"):
        if folder==preserve or not folder.is_dir():continue
        if folder.stat().st_mtime>=cutoff:continue
        # Only delete this tool's known generated files, not other user data.
        known={"pcapdroid-connections.csv","summary.json"}
        if not all(item.is_file() and item.name in known for item in folder.iterdir()):
            continue
        for item in folder.iterdir():item.unlink()
        folder.rmdir();removed+=1
    return removed

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--csv",type=Path,help="Already exported CSV on this Windows machine")
    parser.add_argument("--pull-latest",action="store_true",help="ADB pull latest /Download/PCAPdroid*.csv")
    parser.add_argument("--adb",default="adb")
    parser.add_argument("--serial",default="e3e68545")
    parser.add_argument("--retention-days",type=int,default=7)
    parser.add_argument("--output-root",type=Path,
        default=Path(os.environ.get("LOCALAPPDATA", ".")) /
            "OpenQuickHost" / "NetworkCaptures" / "K70")
    args=parser.parse_args()
    if bool(args.csv) == args.pull_latest:
        parser.error("Choose exactly one of --csv or --pull-latest")
    if not 1<=args.retention_days<=30:parser.error("retention-days must be 1..30")
    root=args.output_root.resolve()
    root.mkdir(parents=True,exist_ok=True)
    if args.csv:
        input_file=args.csv.resolve()
        if not input_file.is_file():raise FileNotFoundError(input_file)
        existing=(input_file.parent.resolve() == root or input_file.parent.parent.resolve()==root)
        if existing:
            session=input_file.parent
            csv_file=input_file
        else:
            session=root / ("pcapdroid-" + datetime.datetime.now().strftime("%Y%m%d-%H%M%S"))
            session.mkdir(parents=True,exist_ok=False)
            csv_file=session/"pcapdroid-connections.csv"
            shutil.copy2(input_file,csv_file)
        phone_source=None
    else:
        session=root / ("pcapdroid-" + datetime.datetime.now().strftime("%Y%m%d-%H%M%S"))
        session.mkdir(parents=True,exist_ok=False)
        csv_file,phone_source=pull_newest(args.adb,args.serial,session)
    size=csv_file.stat().st_size
    if not 64<=size<=MAX_SOURCE:
        raise ValueError("csv_size_out_of_bounds")
    checksum=hashlib.sha256(csv_file.read_bytes()).hexdigest()
    # Avoid accumulating duplicate raw exports when the same phone file is pulled again.
    previous=root/"latest-pcapdroid-summary.json"
    if args.pull_latest and previous.is_file():
        try:
            earlier=json.loads(previous.read_text(encoding="utf-8"))
            existing=Path(earlier.get("csv_file", ""))
            if (earlier.get("csv_sha256")==checksum and existing.is_file()
                    and hashlib.sha256(existing.read_bytes()).hexdigest()==checksum):
                csv_file.unlink()
                session.rmdir()
                print("CAPTURE_ALREADY_IMPORTED=True")
                print("PREVIOUS_LOCAL_CSV="+str(existing))
                print("PREVIOUS_LOCAL_SUMMARY="+str(existing.parent/"summary.json"))
                return
        except (OSError,ValueError,KeyError,TypeError):
            pass
    summary=aggregate(csv_file)
    summary["imported_at"]=datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds")
    summary["csv_file"]=str(csv_file)
    summary["csv_bytes"]=size
    summary["csv_sha256"]=checksum
    summary["retention_days"]=args.retention_days
    summary["raw_csv_local_only"]=True
    summary["phone_export_path"]=phone_source
    summary["previous_sessions_pruned"]=prune_private(root,args.retention_days,session)
    target=session/"summary.json"
    target.write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding="utf-8")
    (root/"latest-pcapdroid-summary.json").write_text(
        json.dumps(summary,ensure_ascii=False,indent=2),encoding="utf-8")
    print("CAPTURE_ROWS="+str(summary["record_count"]))
    print("APP_GROUPS="+str(summary["distinct_app_package_pairs"]))
    print("PROTOCOL_COUNTS="+json.dumps(summary["protocols"],ensure_ascii=False))
    print("INFO_FILLED="+str(summary["records_with_info_field"]))
    print("LOCAL_SUMMARY="+str(target))
    print("LOCAL_RAW_CSV="+str(csv_file))
    print("RAW_CSV_LOCAL_ONLY=True")
    print("SOURCE_SHA256="+summary["csv_sha256"])

if __name__=="__main__":
    main()
