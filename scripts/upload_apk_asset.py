import os
import urllib.request

token = os.environ.get("GITHUB_TOKEN", "")
release_id = "358429943"
apk_path = r"f:\Desktop\kaifa\OpenQuickHost\mobile\android\app\build\outputs\apk\debug\app-debug.apk"
asset_name = "Yanzi-android-v0.2.24.apk"

upload_url = f"https://uploads.github.com/repos/luoluoluo22/yanzi/releases/{release_id}/assets?name={asset_name}"

with open(apk_path, "rb") as f:
    apk_data = f.read()

req = urllib.request.Request(upload_url, data=apk_data, headers={
    "Authorization": f"token {token}",
    "Accept": "application/vnd.github.v3+json",
    "Content-Type": "application/vnd.android.package-archive"
}, method="POST")

try:
    with urllib.request.urlopen(req) as resp:
        print("Upload APK status:", resp.status)
except Exception as e:
    print("Upload exception:", e)
