import json
import urllib.request

import os

token = os.environ.get("GITHUB_TOKEN", "")
release_id = "358429943"
url = f"https://api.github.com/repos/luoluoluo22/yanzi/releases/{release_id}"

body_text = """# 燕子 Yanzi v0.2.24 双端更新内容

### 电脑端与手机端全平滑协同
- **【桌面路径解析】** 修复电脑端 Desktop 相对路径解析（ResolveFsPath），彻底解决手机端打开桌面文本与图片文件提示不存在的问题。
- **【跨端文件查看与高清预览】** 支持在手机端直接在线打开并编辑电脑端文本文件，图片文件提供 Base64 原生渲染高清弹窗预览。
- **【智能文件类型图标】** 基于扩展名自动匹配显示文本、图片、文件夹等现代文件类型图标。

### 手机端 UI 与交互体验重构
- **【全屏对齐精美布局】** 重构“电脑”Tab 标题、子 Tab 栏与“电脑扩展”搜索框 Padding 边距，与“燕幕”等顶级 Tab 页面 20dp 内缩边距 100% 严丝合缝。
- **【扩展网格中轴居中】** 扩展图标网格在中轴线上绝对居中对称，行间距减半，呈现更加紧凑高级的卡片界面。
- **【聊天滚屏与历史清空】** 修复聊天 Tab 顶部 Header 与底部 Input 输入栏固定锚定，支持长按聊天区域一键快捷清空全部历史记录。
- **【文件管理 Popup 上传】** 去除冗余路径描述与文字，搜索栏左侧新增 + 快捷上传 Popup 菜单（支持文件、照片与实时拍照上传）。

### 稳定性与网络并发通信强化
- **【云端中继解包优化】** 重构 Payload 提炼机制，完美解决在非局域网云端中继网络环境下的 PowerShell 与文件列表提取延迟与卡顿。
- **【视图生命周期安全】** 彻底解决 Android 端重构过程中因重复挂载 View 引起的运行时崩溃。

---
*Yanzi v0.2.24 Windows 电脑端 & Android 手机端双端同时发布*"""

payload = {
    "name": "燕子 Yanzi v0.2.24",
    "body": body_text
}

data = json.dumps(payload, ensure_ascii=False).encode('utf-8')

req = urllib.request.Request(url, data=data, headers={
    "Authorization": f"token {token}",
    "Accept": "application/vnd.github.v3+json",
    "Content-Type": "application/json; charset=utf-8"
}, method="PATCH")

with urllib.request.urlopen(req) as resp:
    print("Patch status:", resp.status)
