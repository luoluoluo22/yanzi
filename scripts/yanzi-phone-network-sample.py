#!/usr/bin/env python3
"""Non-root Android network connection metadata sampler via user-authorized ADB.

Collects only socket headers/endpoints; never packet payload, DNS request bodies,
TLS keys, subscriber identifiers, full dumpsys output or account credentials.
Snapshots are bounded, local-only, and pruned after the configured retention.
"""
import argparse
import collections
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

def adb_output(adb: str, serial: str, *commands: str) -> str:
    cp = subprocess.run(
        [adb, "-s", serial, *commands], text=True, encoding="utf-8",
        errors="replace", capture_output=True, timeout=12, check=False,
    )
    if cp.returncode != 0 and not cp.stdout:
        raise RuntimeError(f"adb_command_failed: {commands[0]}")
    return cp.stdout

def parse_connections(ss: str):
    out = []
    for line in ss.splitlines():
        parts = line.split()
        if len(parts) < 6 or parts[0] not in {"tcp", "udp"}:
            continue
        proto, state, recv, sent, local, remote = parts[:6]
        if remote in {"*", "*:*", "0.0.0.0:*", "[::]:*", "::1:*"}:
            continue
        if remote.startswith(("127.", "[::1]", "::ffff:127.")):
            continue
        try:
            remote_port = int(remote.rsplit(":", 1)[-1])
        except (ValueError, IndexError):
            continue
        if not 0 < remote_port < 65536:
            continue
        # Preserve remote endpoint only, never local/private interface address.
        out.append({
            "protocol": proto, "state": state, "remote": remote,
            "recv_queue_bytes": int(recv) if recv.isdigit() else 0,
            "send_queue_bytes": int(sent) if sent.isdigit() else 0,
        })
    return out

def prune(root: Path, retention_days: int) -> int:
    deadline = time.time() - retention_days * 86400
    removed = 0
    for d in root.iterdir() if root.exists() else []:
        if not d.is_dir() or not d.name.startswith("probe-"):
            continue
        if d.stat().st_mtime < deadline:
            for file in d.iterdir():
                if file.is_file():
                    file.unlink()
            d.rmdir()
            removed += 1
    return removed

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--adb", default="adb")
    p.add_argument("--serial", required=True)
    p.add_argument("--seconds", type=int, default=30)
    p.add_argument("--interval", type=float, default=3.0)
    p.add_argument("--retention-days", type=int, default=7)
    p.add_argument("--max-bytes", type=int, default=2_000_000)
    p.add_argument("--output-root", default=str(Path(os.environ["LOCALAPPDATA"])
                      / "OpenQuickHost" / "NetworkCaptures" / "K70"))
    args = p.parse_args()
    if not 1 <= args.seconds <= 1800 or not 1 <= args.interval <= 60:
        p.error("seconds 1..1800, interval 1..60")
    if not 1 <= args.retention_days <= 30:
        p.error("retention-days 1..30")
    if not 2000 <= args.max_bytes <= 20_000_000:
        p.error("max-bytes 2000..20000000")
    root = Path(args.output_root)
    root.mkdir(parents=True, exist_ok=True)
    pruned = prune(root, args.retention_days)
    started = datetime.datetime.now(datetime.timezone.utc)
    folder = root / ("probe-" + started.strftime("%Y%m%d-%H%M%S"))
    folder.mkdir(parents=True, exist_ok=False)
    events_path = folder / "connections.jsonl"
    stats = collections.Counter()
    unique = set()
    samples = 0
    warnings = set()
    truncated = False
    last_vpn = False
    deadline = time.monotonic() + args.seconds
    with events_path.open("w", encoding="utf-8") as target:
        while True:
            now = datetime.datetime.now(datetime.timezone.utc)
            try:
                ss = adb_output(args.adb, args.serial, "shell", "ss", "-tun")
                routes = adb_output(args.adb, args.serial, "shell", "ip", "-o", "addr", "show")
            except (subprocess.SubprocessError, OSError, RuntimeError) as exc:
                warnings.add("adb_connection_unavailable")
                break
            last_vpn = bool(re.search(r"\btun\d+\b", routes))
            connections = parse_connections(ss)
            # Full endpoints stay on the local PC; no raw packet payload is collected.
            for c in connections:
                stats[c["protocol"]] += 1
                unique.add((c["protocol"], c["remote"]))
            event = {
                "timestamp_utc": now.isoformat(timespec="seconds"),
                "sample_index": samples + 1,
                "vpn_interface_present": last_vpn,
                "connection_count": len(connections),
                "connections": connections,
            }
            line = json.dumps(event, ensure_ascii=True, separators=(",", ":")) + "\n"
            if target.tell() + len(line.encode("utf-8")) > args.max_bytes:
                truncated = True
                break
            target.write(line)
            target.flush()
            samples += 1
            if time.monotonic() >= deadline or samples >= 600:
                break
            time.sleep(min(args.interval, max(0, deadline - time.monotonic())))
    summary = {
        "schema_version": 1,
        "tool": "yanzi-phone-network-sample",
        "device_label": "K70",
        "source": "Android ss -tun via authorized ADB (non-root)",
        "started_utc": started.isoformat(timespec="seconds"),
        "finished_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"),
        "samples": samples,
        "observations_by_protocol": dict(stats),
        "unique_remote_endpoints": len(unique),
        "vpn_interface_present_at_last_sample": last_vpn,
        "truncated": truncated,
        "pruned_old_sessions": pruned,
        "warnings": sorted(warnings),
        "data_fields": ["timestamp_utc", "protocol", "state", "remote_endpoint",
                        "recv_queue_bytes", "send_queue_bytes"],
        "NOT_CAPTURED": ["TCP payload", "HTTP bodies", "TLS plaintext",
                         "DNS query payload", "application attribution",
                         "WeChat contacts", "WeChat messages"],
        "retention_days": args.retention_days,
        "max_session_bytes": args.max_bytes,
        "local_file": str(events_path),
    }
    summary_path = folder / "summary.json"
    summary_path.write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding="utf-8")
    print("NETWORK_SAMPLE_SUMMARY=" + str(summary_path))
    print("NETWORK_SAMPLE_EVENTS=" + str(events_path))
    print("SAMPLES=" + str(samples))
    print("UNIQUE_REMOTE_ENDPOINTS=" + str(len(unique)))
    print("PROTOCOL_OBSERVATIONS=" + json.dumps(dict(stats)))
    print("VPN_INTERFACE_PRESENT=" + str(last_vpn))
    print("CAPTURE_IS_METADATA_ONLY=True")

if __name__ == "__main__":
    main()
