import os
import sys
import json
import urllib.request
import urllib.parse
import ssl

ssl._create_default_https_context = ssl._create_unverified_context

REPO = "luoluoluo22/yanzi"
VERSION = "0.2.0"
TAG = f"macos-v{VERSION}"
TITLE = f"燕子 Yanzi for macOS v{VERSION}"
RELEASE_NOTES = f"""# 燕子 Yanzi for macOS v{VERSION} 现代化升级版本

**✨ 核心更新与对齐特性**

1. **🎨 深浅外观与色彩主题全面对齐**
   - 完美适配 macOS 明亮浅色模式与暗黑深色模式，修复浅色模式下背包与卡片色彩对比度。
   - 全局组件尺寸、圆角与排版严格遵循 Windows 标准规范。

2. **🎒 背包（原鼠标面板）深度重构**
   - 全面更名为「背包」，支持右键长按快速唤醒。
   - 12 槽位图标支持自由拖拽排序与互换，点击空槽位默认直接唤起创建小程序向导。

3. **🧩 小程序构建向导与 AI 生成双模式**
   - **向导模式**：内置 6 大扩展类型（打开网址/程序、网页搜索、粘贴短语、AppleScript 脚本、Shell 终端、模拟按键）与丰富预设模板。
   - **右侧实时预览区**：包含 48x48 实时外观卡片与名称、图标、分类、描述等即时属性配置。
   - **AI 生成 / JSON 源码模式**：一键生成并复制大模型系统提示词，配备格式化、复制、查找与单处/全局替换工具栏及实时语法校验。

4. **🔄 原生自动更新与多通道加速**
   - 接入 GitHub Releases 自动版本检查与国内镜像加速下载（`ghfast.top`）。
   - 支持后台分片下载与 macOS 原生原子无缝应用更新重启。

---

- **安装镜像**：`Yanzi-macos-v{VERSION}.dmg`（拖拽至 Applications 即可安装）
- **便携压缩包**：`Yanzi-macos-v{VERSION}.zip`
"""

def main():
    token = os.environ.get("GITHUB_TOKEN", "")
    if len(sys.argv) > 1 and sys.argv[1]:
        token = sys.argv[1]
    
    if not token:
        # Try reading from env.md if exists
        try:
            if os.path.exists("env.md"):
                with open("env.md", "r", encoding="utf-8") as f:
                    content = f.read()
                    import re
                    m = re.search(r"ghp_[a-zA-Z0-9]+", content)
                    if m:
                        token = m.group(0)
        except Exception:
            pass

    if not token:
        print("ERROR: GitHub Token not found. Please provide via GITHUB_TOKEN env or argument.")
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
            "target_commitish": "feature/mac-dev",
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
    else:
        # Update existing release title and notes in UTF-8
        print(f"Updating release notes for ID: {release_id}...")
        patch_url = f"https://api.github.com/repos/{REPO}/releases/{release_id}"
        patch_payload = {
            "name": TITLE,
            "body": RELEASE_NOTES
        }
        patch_req = urllib.request.Request(
            patch_url,
            data=json.dumps(patch_payload).encode("utf-8"),
            headers=headers,
            method="PATCH"
        )
        try:
            with urllib.request.urlopen(patch_req) as resp:
                print("Updated release metadata successfully.")
        except Exception as e:
            print(f"Warning: Failed to update release metadata: {e}")

    # 3. Upload assets
    assets = [
        (f"Yanzi-macos-v{VERSION}.dmg", "application/x-apple-diskimage"),
        (f"Yanzi-macos-v{VERSION}.zip", "application/zip")
    ]

    base_upload_url = upload_url_template.split("{")[0]

    for asset_name, content_type in assets:
        if not os.path.exists(asset_name):
            print(f"Warning: {asset_name} not found in current directory. Skipping.")
            continue

        print(f"Uploading {asset_name} ({os.path.getsize(asset_name) / (1024*1024):.1f} MB)...")
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
