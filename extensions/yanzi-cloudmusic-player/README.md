# 个人小程序：网易云音乐（非燕子内置功能）

这是个人电脑上安装的 WebView 小程序，**不属于燕子默认小程序清单，也不面向普通用户自动分发**。

- 安装路径：`%LOCALAPPDATA%\OpenQuickHost\Extensions\yanzi-cloudmusic-player`
- 个人数据：歌单、音频缓存、播放历史保存在 `%LOCALAPPDATA%\OpenQuickHost\ExtensionStorage\yanzi-cloudmusic-player`，不进入 Git
- 授权方式：`devicePolicy.mode=selected`；本源码 `deviceIds=[]`，复制安装后默认不能运行，必须在获授权设备上单独设置；已安装的私人清单保留本机授权
- 同步策略：`packageSync=none`、`dataSync=device`；不会自动分发小程序包或个人音频
- 扩展接口：`app.controls.actions=[status, play, pause, toggle, next, prev]`，由小程序自己的 `window.yanziExtensionControl(action)` 实现
- 燕环个人绑定：`appact::yanzi-cloudmusic-player::prev`、`appact::yanzi-cloudmusic-player::toggle`、`appact::yanzi-cloudmusic-player::next`
- 宿主只提供通用 `extension.action` 与 WebView 生命周期管理；并不知道此私人小程序的名称，也没有专用的音乐 API
- 用户关闭小程序窗口仅隐藏；“正在运行的小程序”管理功能可真正终止 WebView
- `control.ps1` 是旧版兼容脚本；新燕环控制不依赖该脚本，亦不启动第二个燕子主进程
- `app/data.js` 是空歌单占位文件，真实数据仅由本机播放器运行时维护

## 本地验证（2026-10-15）

本机 `Yanzi 1.0.29` + 小程序 `1.2.1`：通过浣熊验证从未运行状态直接后台播放、媒体进度 3 秒推进、上一首、下一首、暂停、未声明动作拒绝和停止清理，最终后台实例数为 0。

仅应保存在私有代码库；不可复制到公开燕子仓库或默认安装包。
