# 燕子 (Yanzi) 效率启动器项目发布指南

本指南旨在规范项目的发布流程，并说明如何规避 Windows 环境下的中文乱码问题。

## ⚠️ 核心注意事项：如何避免 Release 说明乱码

在 Windows PowerShell 5.1 (中文版 Windows 默认环境) 下，如果 `.ps1` 脚本包含中文字符且保存为 **无 BOM 的 UTF-8** 编码，PowerShell 在加载脚本时会默认将其识别为 **GBK** 编码。这会导致脚本内的硬编码中文字符在内存中被直接损坏并产生乱码，后续通过 `gh` 上传更新说明时就会把损坏的字符显示到 GitHub Release 页面上。

### 规避方案：

1. **统一脚本编码**：
   - 任何涉及中文字符的 `.ps1` 脚本，在编辑后**必须**以 **UTF-8 with BOM** 编码保存。
   - 在 VS Code 中，可在右下角点击编码格式，选择“通过编码保存 (Save with Encoding)” -> “UTF-8 with BOM”。

2. **推荐 API 方式发布与修改 Release Notes**：
   - 为彻底规避由于控制台、本地临时文件系统或 `gh` 客户端在 Go 运行时对字符集解码的兼容偏差，推荐直接在 PowerShell 中使用 API 发送标准的 JSON 格式请求更新 Release 说明：
   ```powershell
   $token = "YOUR_GITHUB_TOKEN"
   $headers = @{
       "Authorization" = "token $token"
       "Accept"        = "application/vnd.github.v3+json"
   }
   $body = @{ body = "这里是干净无乱码的中文 Markdown 更新说明" } | ConvertTo-Json
   # 345105664 为对应的 release id
   Invoke-RestMethod -Uri "https://api.github.com/repos/luoluoluo22/yanzi/releases/345105664" -Method Patch -Headers $headers -Body $body -ContentType "application/json; charset=utf-8" -Proxy "http://127.0.0.1:7890"
   ```

---

## 🚀 完整发布新版本步骤 (v0.2.12+)

在当前仓库的根目录下执行以下三步即可发布客户端和更新说明：

### 第一步：编译并打包 Windows 客户端
在当前的 PowerShell 进程中指定新版本号并进行 Velopack 打包：
```powershell
powershell -File .\scripts\publish-installer.ps1 -Version "0.2.12"
```
运行成功后，会在 `.artifacts\installer\` 目录下生成 `Yanzi-win-Setup-0.2.12.exe` 和 `Yanzi-win-Portable-0.2.12.zip`。

### 第二步：配置代理并执行 GitHub Release 上传
国内直连 GitHub API 易发生 EOF 网络阻断。为了解决 Go 语言在代理环境下的 HTTP/2 握手 EOF 问题，**必须禁用 HTTP/2 强制使用 HTTP/1.1**，并保留代理：
```powershell
# 1. 注入代理及禁用 HTTP/2 客户端的环境变量
$env:HTTP_PROXY="http://127.0.0.1:7890"
$env:HTTPS_PROXY="http://127.0.0.1:7890"
$env:GODEBUG="http2client=0"

# 2. 必须以同进程 (In-Process) 方式运行，并附带 -KeepProxy 开关以防止脚本内清空代理
.\scripts\upload-release-installer.ps1 -Version "0.2.12" -KeepProxy
```

### 第三步：发布文档网站
由于已删除了 `publish-website.ps1`，现在只需直接将文档和代码推送到 GitHub 的 `main` 分支。
```powershell
git add .
git commit -m "docs: release description and checks"
git push origin main
```
Cloudflare Pages 将自动检测并拉取最新的 `website` 目录完成线上部署。
