# 本地常用命令

这些命令默认在仓库根目录执行：`F:\Desktop\kaifa\OpenQuickHost`。

## 构建与验证

```powershell
dotnet build OpenQuickHost.sln
```

```powershell
.\scripts\verify-extension-package.ps1
```

如果要验证 WebDAV 扩展包上传/下载回环：

```powershell
.\scripts\verify-extension-package.ps1 -WebDav
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
npx wrangler@latest pages deploy .\website --project-name openquickhost-site --branch main
```

如果当前终端是非交互式环境，需要先设置 Cloudflare API Token：

```powershell
$env:CLOUDFLARE_API_TOKEN = "你的 Cloudflare API Token"
$env:CLOUDFLARE_ACCOUNT_ID = "cc88cc0084b504db93ccd9462af37212"
```

当前机器代理偶尔会导致 Cloudflare/GitHub 上传链路 TLS 失败。遇到 `fetch failed` 或 EOF 时，优先切换代理策略后重试。

## GitHub 认证

上传 Release 需要 GitHub CLI：

```powershell
gh auth status
gh auth login -h github.com
```

如果 `gh auth status` 显示 keyring token 失效，先重新登录再上传。
