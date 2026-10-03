"""Validate GitHub APK provenance, then verify the public update channel."""
import importlib.util
import json
import re
import sys
from pathlib import Path

spec = importlib.util.spec_from_file_location("application_release", Path(__file__).with_name("application-release-ci.py"))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)
OUT = release.ROOT / ".artifacts" / "mobile-release"


def prepare(version):
    info = json.loads((OUT / "release.json").read_text(encoding="utf-8"))
    if info.get("draft") or info.get("prerelease") or info.get("tag_name") != "android-v" + version:
        raise SystemExit("Release identity mismatch")
    apk = OUT / ("yanzi-mobile-" + version + ".apk")
    metadata = {
        "packageName": "cc.luoluoluo.yanzi.mobile", "version": version,
        "versionCode": int(version.split(".")[-1]),
        "certificateSha256": "8a0ec0b84d1a05edcc89dd020bf81901f9ed7f083887db7c05201c59b31e1ee3",
    }
    digest, size = release.verify_apk(metadata, apk)
    asset = next((a for a in info.get("assets", []) if a.get("name") == apk.name), None)
    if not asset or asset.get("size") != size or asset.get("digest") != "sha256:" + digest:
        raise SystemExit("GitHub release asset hash/size mismatch")
    manifest = [{"tag_name": info["tag_name"], "draft": False, "body": info.get("body", ""),
                 "assets": [{"name": apk.name, "digest": "sha256:" + digest,
                             "browser_download_url": release.BASE_URL + "/downloads/android/" + apk.name}]}]
    (OUT / "releases.json").write_text(json.dumps(manifest, ensure_ascii=False), encoding="utf-8")
    print("MOBILE_RELEASE_VALIDATED=" + version)


def verify(version):
    local = release.sha256_file(OUT / ("yanzi-mobile-" + version + ".apk"))
    manifest = release.fetch_json(release.BASE_URL + "/downloads/android/releases.json")
    if not manifest or manifest[0].get("tag_name") != "android-v" + version:
        raise SystemExit("Published mobile version mismatch")
    asset = manifest[0]["assets"][0]
    expected_url = release.BASE_URL + "/downloads/android/yanzi-mobile-" + version + ".apk"
    if asset.get("digest") != "sha256:" + local or asset.get("browser_download_url") != expected_url:
        raise SystemExit("Published mobile asset mismatch")
    with release.urllib.request.urlopen(release.request(expected_url), timeout=90) as response:
        import hashlib
        remote = hashlib.sha256(response.read()).hexdigest()
    if remote != local:
        raise SystemExit("Public mobile download hash mismatch")
    print("PUBLIC_MOBILE_VERIFIED=" + version)


if __name__ == "__main__":
    if len(sys.argv) != 3 or sys.argv[1] not in {"prepare", "verify"} or not re.fullmatch(r"\d+\.\d+\.\d+", sys.argv[2]):
        raise SystemExit("usage: mobile-release-ci.py prepare|verify <version>")
    globals()[sys.argv[1]](sys.argv[2])
