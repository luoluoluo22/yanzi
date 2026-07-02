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

## 🚀 完整发布新版本步骤 (v0.2.15+)

现在，发版已被完全自动化！您只需在当前仓库的根目录下执行一键发布指令：

### 唯一发布指令：
```powershell
.\scripts\release.ps1
```

### 自动化执行细节：
- **版本号自动对齐**：脚本会自动读取 `src/OpenQuickHost/OpenQuickHost.csproj` 中的 `<Version>` 版本节点，无需再手工指定 `-Version`。
- **免人工防乱码**：在资源上传结束后，脚本会自动调用 GitHub API 对创建的 Release 的 `name` 和 `body` 进行 UTF-8 编码的 Patch 覆写，确保线上中文 100% 正确展示。
- **代理支持**：脚本默认开启了 `-KeepProxy` 并注入了 `$env:GODEBUG="http2client=0"`，在国内网络环境下也能稳定、闪电式上传。

---

## 🤖 AI 助手编写更新说明规约

当 AI 助手帮用户发布新版本时，**必须**严格遵循以下规范编写更新说明并自动执行发布：

### 1. 自动提炼变动内容
AI 助手在准备发布前，应首先分析本次提交的改动（或通过运行 `git log` 查看最新改动），智能归纳出当前版本更新的核心特性。**严禁直接使用空模版或无实际业务含义的占位符。**

### 2. 更新说明格式与排版规范
更新说明（Markdown 格式）应当极具中文业务体感，具体结构规范如下：
- **标题**：必须为 `# 燕子 Yanzi v[版本号] 更新内容`。
- **功能分类**：使用带有合适 Emoji 的粗体字做大分类，如：
  - `**✨ 核心功能更新**`
  - `**🎛️ 轮盘功能升级**`
  - `**⚡ 性能与稳定优化**`
- **列表条目**：每项改动使用无序列表表示，重点词汇使用 `【】` 括起来，例如：
  - `- 【高清晰图标】记事本程序默认不再使用矢量图标替代，而是通过新增 Windows 原生的 `IShellItemImageFactory` COM 接口，直接从系统提取 256x256 分辨率的现代高清晰 Fluent 原生记事本图标（包括其他系统自带或第三方 EXE 程序的高清图标）。`
- **结尾签名**：包含安装包名称与 SHA256 哈希值验证。

### 3. 全自动静默发布操作流
为彻底免除人机交互时的弹窗打扰，AI 助手在发版时的操作步骤如下：
1. **自动撰写日志**：AI 助手根据本规约写好 Markdown 更新日志，并以 **UTF-8 with BOM** 编码直接写入项目根目录下的 **`RELEASE_NOTES.md`** 文件中。
2. **运行发布**：在 PowerShell 控制台执行 `.\scripts\release.ps1`。
3. **静默原理**：总控脚本检测到本地已存在 `RELEASE_NOTES.md` 且内容非空，**将不会弹窗拉起记事本**，而是直接静默提取它，并在发布成功后将其清理。整个流程 100% 全自动完成。
