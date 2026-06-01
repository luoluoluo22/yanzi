# 本地常用命令

这些命令默认在仓库根目录执行：`F:\Desktop\kaifa\OpenQuickHost`。

## 构建与验证

```powershell
dotnet build
```


## 生成自包含单文件和 Velopack 一键安装包及升级资产：

```powershell
# 打包命令，vpk 工具需要事先全局安装（dotnet tool install -g vpk）
.\scripts\publish-installer.ps1 -Version 0.2.0
```

只生成便携版 Payload 目录，不进行打包：

```powershell
.\scripts\publish-installer.ps1 -Version 0.2.0 -SkipInstaller
```

输出位置：

```text
.artifacts\publish\win-x64\      (便携 Payload 目录，包含主 Yanzi.exe 及依赖 DLL)
.artifacts\installer\Yanzi-win-Setup.exe  (一键极速安装程序，已规避乱码)
.artifacts\installer\Yanzi-0.2.0-full.nupkg (全量自更新包)
.artifacts\installer\releases.win.json    (自更新索引核心文件)
```

## 上传安装包到 GitHub Release

默认遍历并批量上传 `.artifacts\installer` 下的 `Setup.exe`、`full.nupkg` 升级包以及 `releases.win.json` 元数据至 `luoluoluo22/yanzi` 仓库的 `v版本号` Release 附件下，打通远端自更新。

```powershell
# 必须先配置 GITHUB_TOKEN 环境变量，随后执行上传
$env:GITHUB_TOKEN = "你的 GitHub Token"
.\scripts\upload-release-installer.ps1 -Version 0.2.0
```

只创建/更新草稿 Release，不正式发布：

```powershell
.\scripts\upload-release-installer.ps1 -Version 0.2.0 -Draft
```

【核心推荐】当前机器代理对 `uploads.github.com` 大文件上传极其依赖系统代理。上传脚本内部已完美集成了防卡死静默重定向设计，且强烈建议在上传时**追加 -KeepProxy 参数**以完美使用您本机的系统翻墙代理（Clash 等默认 http://127.0.0.1:7890 端口）进行几秒钟内的闪电式秒级上传：

```powershell
$env:GITHUB_TOKEN = "你的 GitHub Token"
.\scripts\upload-release-installer.ps1 -Version 0.2.0 -KeepProxy
```

```powershell
# 1. 确保代码通过编译
dotnet build OpenQuickHost.sln

# 2. 生成最新版 Velopack 原生发布包与更新元数据
.\scripts\publish-installer.ps1 -Version 0.2.0

# 3. 设置 Token 并通过高速代理一键发布并上传至 GitHub
$env:GITHUB_TOKEN = "你的 GitHub Token"
.\scripts\upload-release-installer.ps1 -Version 0.2.0 -KeepProxy
```

## 更新官网

官网是纯静态文件，源码在 `website/`。

```powershell
.\scripts\publish-website.ps1
```

发布脚本会自动读取根目录 `.env` 里的环境变量，再调用 Cloudflare Pages 发布。建议在 `.env` 中至少写入：

```dotenv
CLOUDFLARE_API_TOKEN=你的 Cloudflare API Token
```

如果 `.env` 里只有一行裸 token，没有 `CLOUDFLARE_API_TOKEN=` 前缀，发布脚本也会自动把它当作 `CLOUDFLARE_API_TOKEN` 使用。

当前脚本默认会使用本项目的 Cloudflare Account ID：

```text
cc88cc0084b504db93ccd9462af37212
```

如需自定义项目名、分支或网站目录：

```powershell
.\scripts\publish-website.ps1 -ProjectName openquickhost-site -Branch main -SitePath .\website
```

`.env` 已加入 `.gitignore`，不要上传到 GitHub。

当前机器代理偶尔会导致 Cloudflare/GitHub 上传链路 TLS 失败。遇到 `fetch failed` 或 EOF 时，优先切换代理策略后重试。

## 移动端 MVP

安卓工程在 `mobile/android`。当前仓库不提交 Gradle Wrapper，建议用 Android Studio 打开该目录并同步 Gradle。

```powershell
# 如果本机已经安装 Gradle，也可以直接构建
gradle -p .\mobile\android assembleDebug
```

移动端消息队列需要先发布 Cloudflare Worker 并应用 D1 迁移：

```powershell
cd cloudflare
npx wrangler d1 migrations apply openquickhost-sync-db --remote
npx wrangler deploy
cd ..
```

当前机器 Maven/Google TLS 链路不稳定时，可以使用不依赖 Gradle/Maven 的兜底构建脚本直接产出 debug APK：

```powershell
.\scripts\build-android-mvp.ps1
```

输出位置：

```text
mobile\android\app\build\manual-debug\yanzi-mobile-debug.apk
```

## GitHub 认证

上传 Release 需要 GitHub CLI：

```powershell
gh auth status
gh auth login -h github.com
```

如果 `gh auth status` 显示 keyring token 失效，先重新登录再上传。
