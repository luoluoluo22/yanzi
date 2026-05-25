# 本地常用命令

这些命令默认在仓库根目录执行：`F:\Desktop\kaifa\OpenQuickHost`。

## 构建与验证

```powershell
dotnet build
```


## 生成发布包

生成自包含单文件和 Inno Setup 一键安装包：

```powershell
.\scripts\publish-installer.ps1 -Version 0.1.0
```

只生成便携版 `Yanzi.exe`，不生成安装包：

```powershell
.\scripts\publish-installer.ps1 -Version 0.1.0 -SkipInstaller
```

输出位置：

```text
.artifacts\publish\win-x64\Yanzi.exe
.artifacts\installer\YanziSetup-0.1.0.exe
```

## 上传安装包到 GitHub Release

默认上传 `.artifacts\installer\YanziSetup-版本号.exe` 到 `luoluoluo22/yanzi` 的 `v版本号` Release。脚本会先创建或更新 Release，再上传安装包，最后发布 Release。

```powershell
.\scripts\upload-release-installer.ps1 -Version 0.1.0
```

只创建/更新草稿 Release，不正式发布：

```powershell
.\scripts\upload-release-installer.ps1 -Version 0.1.0 -Draft
```

指定安装包路径：

```powershell
.\scripts\upload-release-installer.ps1 -Version 0.1.0 -InstallerPath .\.artifacts\installer\YanziSetup-0.1.0.exe
```

当前机器代理对 `uploads.github.com` 大文件上传不稳定。上传脚本默认会在当前 PowerShell 进程内清空 `HTTP_PROXY`、`HTTPS_PROXY`、`ALL_PROXY`、`NO_PROXY`，让系统网络/TUN 接管。如果以后代理已稳定，可以保留代理：

```powershell
.\scripts\upload-release-installer.ps1 -Version 0.1.0 -KeepProxy
```

## 完整发布流程

```powershell
dotnet build OpenQuickHost.sln
.\scripts\verify-extension-package.ps1
.\scripts\publish-installer.ps1 -Version 0.1.0
.\scripts\upload-release-installer.ps1 -Version 0.1.0
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
