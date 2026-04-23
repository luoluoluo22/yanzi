# 桌面启动器

一个面向个人效率场景的桌面启动器原型，目标是成为 Quicker 的开源替代方向之一。

当前能力：

- 全局热键呼出启动器面板
- 本地命令执行
- 单文件 JSON 扩展
- 本地扩展增删改查
- Cloudflare 云同步
- 扩展打包上传与下载
- 用户登录与本机安全存储
- 参数化命令，例如 `谷歌 今天的新闻`

## 本地运行

```powershell
dotnet build
bin\Debug\net9.0-windows\OpenQuickHost.exe
```

## 目录说明

- `MainWindow.xaml` / `MainWindow.xaml.cs`：启动器主界面与交互
- `SettingsWindow.xaml` / `SettingsWindow.xaml.cs`：设置窗口
- `Sync/`：云同步、扩展读取、会话与凭据存储
- `cloudflare/`：Cloudflare Workers 同步后端
- `docs/`：产品说明与扩展规范
- `website/`：官网静态站

## 文档

- [产品说明](docs/product-overview.md)
- [扩展规范](docs/extension-spec.md)
- [使用说明](docs/getting-started.md)
