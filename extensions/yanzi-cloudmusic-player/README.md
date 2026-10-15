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

## 1.3.0：授权试听音源刷新与双歌单断点续播

- 本地源码参考工具 REA 的定位是逆向分析辅助工具，并非网易云音乐播放服务。此小程序的数据实际由本机网易云客户端的歌单缓存导入。
- 远程歌曲每次开始播放会重新请求当前可用 URL，附带一次性请求参数避免旧网络缓存；不尝试绕过会员、版权或 DRM 权限。源服务若只提供 30～45 秒试听片段，仍只能播放这一片段。
- 老版 IndexedDB 里未经时长验证的试听内容不再被当作完整音频读取。新增缓存会校验实际音频长度与歌曲标注时长，不满足完整度要求的试听片段不会标为完整版，也不会重新写入已验证缓存。
- 已有物理本地音频如果经元数据确认只是试听片段，会尝试重新获取当前可公开访问的播放地址；失败时保留原本可听的片段。
- 采用仅本机 localStorage 的 yanzi_music_resume_v2，分别记录 recommend/favorites 的歌曲 ID 和播放秒数。切歌、暂停、拖动进度、窗口隐藏、关闭时保存，后台自动节流；继续播放时等待音频元数据，再在合法时长范围内恢复进度。
- 点击“我的喜欢”或“每日推荐”会切换到相应歌单的上次歌曲和位置。数据刷新不会重新载入第一首或打断现有播放。
- 专属扩展的 actions 除常规播放控制外增加 favorites/recommend，供经授权的本地能力接口验证与使用；不会出现在公共默认功能里。
- 单元测试：node --test tests/music-session.test.cjs
- 2026-10-15 本机实测：“我的喜欢”暂停在 111.79 秒，停止后重开恢复到 111.7 秒；切到“每日推荐”再返回仍保持同一歌单同一进度。
