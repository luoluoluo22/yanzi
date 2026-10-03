"""Probe the actual Android release; reject HTTP error pages and mismatched samples."""
import argparse, concurrent.futures, hashlib, json, time, urllib.request
from pathlib import Path

def probe(item):
    name, url = item
    started = time.monotonic()
    result = {"name": name, "url": url}
    try:
        request = urllib.request.Request(url, headers={"User-Agent": "YanziClient-Mobile/0.2.46", "Range": "bytes=0-32767", "Accept-Encoding": "identity"})
        with urllib.request.urlopen(request, timeout=6) as response:
            data = response.read(32768)
            result.update(status=response.status, contentType=response.headers.get("Content-Type"), bytes=len(data), sha256=hashlib.sha256(data).hexdigest())
            result["validApkSample"] = response.status in (200, 206) and len(data) == 32768 and data[:4] == b"PK\x03\x04"
    except Exception as error:
        result.update(validApkSample=False, error=str(error))
    result["elapsedMs"] = round((time.monotonic() - started) * 1000)
    return result

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", default="0.2.45")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    github = f"https://github.com/luoluoluo22/yanzi/releases/download/android-v{args.version}/yanzi-mobile-{args.version}.apk"
    nodes = [("cloud-cdn", f"https://sync.luoluoluo.cc.cd/downloads/android/yanzi-mobile-{args.version}.apk"), ("github", github), ("gh.ddlc.top", "https://gh.ddlc.top/" + github), ("ghfast.top", "https://ghfast.top/" + github), ("kkgithub.com", github.replace("github.com", "kkgithub.com"))]
    with concurrent.futures.ThreadPoolExecutor(max_workers=len(nodes)) as executor:
        results = list(executor.map(probe, nodes))
    reference = next((r.get("sha256") for r in results if r["name"] == "cloud-cdn" and r["validApkSample"]), None)
    for result in results:
        result["matchesCloudSample"] = reference is not None and result.get("sha256") == reference
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(results, ensure_ascii=False, indent=2))
