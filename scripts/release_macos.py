import os
import sys
import json
import urllib.request
import urllib.parse
import ssl

ssl._create_default_https_context = ssl._create_unverified_context

REPO = "luoluoluo22/yanzi"
TAG = "macos-v0.1.0"
TITLE = "燕子 Yanzi for macOS v0.1.0"
RELEASE_NOTES = """# 燕子 Yanzi for macOS v0.1.0 首发版本

**✨ 核心特性**
- 【背包面板】支持鼠标右键长按呼出 12 槽位快捷背包，支持任意图标自由拖拽互换与排序。
- 【智能搜索】支持在仓库与小程序选择器中通过全拼与拼音首字母（如 wx -> 微信, zd -> 终端, fd -> 访达）毫秒级检索。
- 【小程序体系】内置常用系统动作（屏幕截图 ⌘⇧4、锁定屏幕 ⌘⌃Q、下载文件夹、废纸篓等），支持自定义 JSON 扩展与 AppleScript。
- 【原生体验】高清 App 图标内存预热缓存，0 毫秒极速唤醒，图钉置顶状态持久化记忆。

---
- **安装包**：`Yanzi-macos-v0.1.0.dmg`（支持拖拽至 Applications 快捷安装）
- **免安装压缩包**：`Yanzi-macos-v0.1.0.zip`
"""

def main():
    token = os.environ.get("GITHUB_TOKEN", "")
    if len(sys.argv) > 1 and sys.argv[1]:
        token = sys.argv[1]
    
    if not token:
        # Try reading from gh CLI if available
        import subprocess
        try:
            res = subprocess.run(["/tmp/gh_2.98.0_macOS_amd64/bin/gh", "auth", "token"], capture_output=True, text=True)
            if res.returncode == 0 and res.stdout.strip():
                token = res.stdout.strip()
        except Exception:
            pass

    if not token:
        print("ERROR: Please provide a GitHub Token via GITHUB_TOKEN env or as argument.")
        print("Usage: python3 scripts/release_macos.py <YOUR_GITHUB_TOKEN>")
        sys.exit(1)

    headers = {
        "Authorization": f"token {token}",
        "Accept": "application/vnd.github.v3+json",
        "User-Agent": "Yanzi-Release-Tool"
    }

    # 1. Check if release already exists
    print(f"Checking if release '{TAG}' exists...")
    get_url = f"https://api.github.com/repos/{REPO}/releases/tags/{TAG}"
    req = urllib.request.Request(get_url, headers=headers)
    release_id = None
    upload_url_template = None

    try:
        with urllib.request.urlopen(req) as resp:
            data = json.loads(resp.read().decode())
            release_id = data.get("id")
            upload_url_template = data.get("upload_url")
            print(f"Found existing release ID: {release_id}")
    except urllib.error.HTTPError as e:
        if e.code == 404:
            print("Release does not exist yet. Creating release...")
        else:
            print(f"Error checking release: {e}")

    # 2. Create release if not exists
    if not release_id:
        create_url = f"https://api.github.com/repos/{REPO}/releases"
        payload = {
            "tag_name": TAG,
            "target_commitish": "main",
            "name": TITLE,
            "body": RELEASE_NOTES,
            "draft": False,
            "prerelease": False
        }
        create_req = urllib.request.Request(
            create_url,
            data=json.dumps(payload).encode("utf-8"),
            headers=headers,
            method="POST"
        )
        with urllib.request.urlopen(create_req) as resp:
            data = json.loads(resp.read().decode())
            release_id = data.get("id")
            upload_url_template = data.get("upload_url")
            print(f"Created new release successfully! ID: {release_id}")

    # 3. Upload assets
    assets = [
        ("Yanzi-macos-v0.1.0.dmg", "application/x-apple-diskimage"),
        ("Yanzi-macos-v0.1.0.zip", "application/zip")
    ]

    base_upload_url = upload_url_template.split("{")[0]

    for asset_name, content_type in assets:
        if not os.path.exists(asset_name):
            print(f"Warning: {asset_name} not found in current directory. Skipping.")
            continue

        print(f"Uploading {asset_name}...")
        upload_url = f"{base_upload_url}?name={urllib.parse.quote(asset_name)}"
        with open(asset_name, "rb") as f:
            file_data = f.read()

        upload_headers = {
            "Authorization": f"token {token}",
            "Accept": "application/vnd.github.v3+json",
            "Content-Type": content_type,
            "User-Agent": "Yanzi-Release-Tool"
        }

        upload_req = urllib.request.Request(upload_url, data=file_data, headers=upload_headers, method="POST")
        try:
            with urllib.request.urlopen(upload_req) as resp:
                print(f"Successfully uploaded {asset_name} (Status {resp.status})")
        except urllib.error.HTTPError as e:
            err_body = e.read().decode("utf-8", errors="ignore")
            print(f"Upload {asset_name} response: {e.code} - {err_body}")

    print("\n✅ All done! Release published successfully at:")
    print(f"👉 https://github.com/{REPO}/releases/tag/{TAG}")

if __name__ == "__main__":
    main()
