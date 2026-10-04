#!/usr/bin/env python3
import hashlib
import json
import os
import re
import subprocess
import sys
import time
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / ".artifacts" / "application-release"
CATALOG_URL = "https://sync.luoluoluo.cc.cd/v1/applications/catalog"
BASE_URL = "https://sync.luoluoluo.cc.cd"

def load_meta(path):
    p = (ROOT / path).resolve()
    meta = json.loads(p.read_text(encoding="utf-8"))
    apk = (ROOT / meta["apkPath"]).resolve()
    if not apk.is_file():
        raise SystemExit(f"APK missing: {apk}")
    return meta, apk

def android_tool(name):
    home = os.environ.get("ANDROID_HOME") or os.environ.get("ANDROID_SDK_ROOT")
    candidates = []
    if home:
        build_tools = Path(home) / "build-tools"
        if build_tools.exists():
            for version in sorted(build_tools.iterdir(), reverse=True):
                candidates.append(version / name)
    for p in candidates:
        if p.exists():
            return str(p)
    raise SystemExit(f"Android build tool not found: {name}")

def sha256_file(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()

def verify_apk(meta, apk):
    aapt = android_tool("aapt.exe" if os.name == "nt" else "aapt")
    apksigner = android_tool("apksigner.bat" if os.name == "nt" else "apksigner")
    badging = subprocess.check_output([aapt, "dump", "badging", str(apk)], text=True, errors="replace")
    first = badging.splitlines()[0] if badging else ""
    m = re.search(r"name='([^']+)' versionCode='(\d+)' versionName='([^']+)'", first)
    if not m:
        raise SystemExit("Unable to parse APK identity")
    package_name, version_code, version_name = m.group(1), int(m.group(2)), m.group(3)
    if package_name != meta["packageName"] or version_code != int(meta["versionCode"]) or version_name != meta["version"]:
        raise SystemExit(f"APK identity mismatch: {package_name} {version_code} {version_name}")
    cert_run = subprocess.run([apksigner, "verify", "--print-certs", str(apk)], text=True, errors="replace", capture_output=True)
    if cert_run.returncode != 0:
        raise SystemExit("APK signature verification failed: " + (cert_run.stderr or cert_run.stdout).strip())
    certs = (cert_run.stdout or "") + "\n" + (cert_run.stderr or "")
    cm = re.search(r"certificate SHA-256 digest:\s*([a-fA-F0-9]+)", certs, re.IGNORECASE)
    if not cm:
        raise SystemExit("APK certificate digest unavailable; apksigner output: " + certs.strip()[:500])
    certificate = cm.group(1).lower()
    if certificate != meta["certificateSha256"].lower():
        raise SystemExit(f"APK certificate mismatch: {certificate}")
    return sha256_file(apk), apk.stat().st_size

def request(url):
    return urllib.request.Request(url, headers={"User-Agent": "Yanzi-Release-CI/1.0", "Accept": "application/json,*/*"})

def fetch_json(url):
    with urllib.request.urlopen(request(url), timeout=30) as response:
        return json.load(response)

def prepare(meta_path):
    meta, apk = load_meta(meta_path)
    digest, size = verify_apk(meta, apk)
    existing = fetch_json(CATALOG_URL)
    current = next((a for a in existing.get("applications", []) if a.get("applicationId") == meta["applicationId"]), None)
    if current:
        published_code = int(current.get("versionCode", -1))
        if published_code > int(meta["versionCode"]):
            raise SystemExit("Refusing to downgrade a published companion")
        if published_code == int(meta["versionCode"]) and current.get("sha256") != digest:
            raise SystemExit("Published versionCode already has a different APK; bump the companion version")
    apps = [a for a in existing.get("applications", []) if a.get("applicationId") != meta["applicationId"]]
    entry = {
        "applicationId": meta["applicationId"],
        "name": meta["name"],
        "kind": meta["kind"],
        "version": meta["version"],
        "versionCode": int(meta["versionCode"]),
        "description": meta["description"],
        "packageName": meta["packageName"],
        "minHostVersionCode": int(meta["minHostVersionCode"]),
        "certificateSha256": meta["certificateSha256"].lower(),
        "downloadPath": "/downloads/applications/" + apk.name,
        "size": size,
        "sha256": digest,
    }
    if "dataKeys" in meta:
        entry["dataKeys"] = meta["dataKeys"]
    apps.append(entry)
    catalog = {
        "schemaVersion": 1,
        "publishedAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "applications": apps,
    }
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "entry.json").write_text(json.dumps(entry, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (OUT / "catalog.json").write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"apk": str(apk.relative_to(ROOT)).replace("\\", "/"), "asset": apk.name, "sha256": digest, "size": size}, ensure_ascii=False))

def verify(meta_path):
    meta, apk = load_meta(meta_path)
    local_digest, local_size = verify_apk(meta, apk)
    last_error = "not checked"
    for _ in range(12):
        try:
            catalog = fetch_json(CATALOG_URL)
            entry = next((a for a in catalog.get("applications", []) if a.get("applicationId") == meta["applicationId"]), None)
            if not entry:
                raise RuntimeError("catalog entry missing")
            if entry.get("version") != meta["version"] or int(entry.get("versionCode", -1)) != int(meta["versionCode"]):
                raise RuntimeError(f"catalog version mismatch: {entry.get('version')} / {entry.get('versionCode')}")
            if entry.get("sha256") != local_digest or int(entry.get("size", -1)) != local_size:
                raise RuntimeError("catalog hash/size mismatch")
            with urllib.request.urlopen(request(BASE_URL + entry["downloadPath"]), timeout=60) as response:
                remote = response.read()
            remote_digest = hashlib.sha256(remote).hexdigest()
            if remote_digest != local_digest:
                raise RuntimeError("download hash mismatch")
            print(json.dumps({"ok": True, "version": meta["version"], "versionCode": meta["versionCode"], "sha256": local_digest}))
            return
        except Exception as exc:
            last_error = str(exc)
            time.sleep(5)
    raise SystemExit(f"Published release verification failed: {last_error}")

if __name__ == "__main__":
    if len(sys.argv) != 3 or sys.argv[1] not in {"prepare", "verify"}:
        raise SystemExit("usage: application-release-ci.py prepare|verify <metadata.json>")
    globals()[sys.argv[1]](sys.argv[2])
