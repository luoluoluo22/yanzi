# 本地常用命令

这些命令默认在仓库根目录执行：`F:\Desktop\kaifa\OpenQuickHost`。

## 构建与验证

```powershell
dotnet build
```


## 生成自包含单文件和 Velopack 一键安装包及升级资产：

```powershell
.\scripts\publish-installer.ps1 -Version 0.2.6
```


输出位置：

```text
.artifacts\publish\win-x64\
```

## 上传安装包到 GitHub Release

默认遍历并批量上传 `.artifacts\installer` 下的 `Setup.exe`、`full.nupkg` 升级包以及 `releases.win.json` 元数据至 `luoluoluo22/yanzi` 仓库的 `v版本号` Release 附件下，打通远端自更新。

```powershell
# 必须先配置 GITHUB_TOKEN 环境变量，随后执行上传
$env:GITHUB_TOKEN = "你的 GitHub Token"
.\scripts\upload-release-installer.ps1 -Version 0.2.0
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

指定安装包路径：

```powershell
.\scripts\upload-release-installer.ps1 -Version 0.1.0 -InstallerPath .\.artifacts\installer\YanziSetup-0.1.0.exe
```

当前机器代理对 `uploads.github.com` 大文件上传不稳定。上传脚本默认会在当前 PowerShell 进程内清空 `HTTP_PROXY`、`HTTPS_PROXY`、`ALL_PROXY`、`NO_PROXY`，让系统网络/TUN 接管。如果以后代理已稳定，可以保留代理：

```powershell
.\scripts\upload-release-installer.ps1 -Version 0.1.0 -KeepProxy
```

## macOS 平台构建与打包

### 1. 编译自包含 Release 版本的 x64 二进制文件
```bash
/Users/mac/.dotnet/dotnet publish src/Yanzi.Avalonia/Yanzi.Avalonia.csproj -c Release -r osx-x64 --self-contained -o publish
```
```

### 2. 生成标准 `.app` 应用包结构
```bash
./create_app_bundle.sh
```
该脚本会将 `publish/` 目录下的程序文件使用 `ditto` 拷贝至 `Yanzi.app/Contents/MacOS/` 并正确设置可执行权限、放置 `Info.plist` 和 `yanzi.icns` 图标资源，且完整保留其底层相对动态链接库软链。

### 3. 打包压缩生成 `.dmg` 拖拽式安装镜像
```bash
rm -rf dmg_temp && mkdir -p dmg_temp && ditto Yanzi.app dmg_temp/Yanzi.app && ln -s /Applications dmg_temp/Applications && hdiutil create -volname "燕子启动器" -srcfolder dmg_temp -ov -format UDZO Yanzi.dmg && rm -rf dmg_temp
```
该命令会建立一个临时目录并软链接 `/Applications` 文件夹，最终在根目录下生成 UDZO 压缩格式的高保真 `Yanzi.dmg` 镜像，用户只需双击打开并拖拽 `Yanzi.app` 即可轻松安装。

## 更新官网

官网是纯静态文件，源码在 `website/`。

> [!NOTE]
> **自动部署与环境变量自动绑定**：本项目已在 Cloudflare Pages/Workers 绑定了 GitHub 仓库。在本地配置了 Git `pre-push` 钩子，每次执行 `git push` 时会运行 `.\scripts\sync-cloudflare-secrets.ps1` 自动解析 `.env` 并在 Cloudflare 云端同步绑定 Secrets（如 `RESEND_API_KEY`、`AUTH_TOKEN_SECRET` 等）并激活最新 Worker 版本。

### 本地手动发布（备份方案）

如果遇到特殊情况需要进行本地手动发布或紧急回退，可以使用以下脚本：

```powershell
.\scripts\publish-website.ps1
.\scripts\sync-cloudflare-secrets.ps1
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
