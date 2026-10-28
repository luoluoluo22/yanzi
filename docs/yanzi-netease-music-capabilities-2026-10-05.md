# 燕子：网易云音乐专有能力（2026-10-05）

## 已实现能力

- `netease.status`
- `netease.playPause`
- `netease.next`
- `netease.previous`
- `netease.stop`
- `netease.playFile`
- `netease.openUri`

## 技术路线

本机网易云音乐：

`C:\Program Files\NetEase\CloudMusic\cloudmusic.exe`

Windows 注册信息显示：

- 本地音频关联使用 `cloudmusic.exe --play=<file>`
- 注册了 `orpheus://` 协议，内部交给 `cloudmusic.exe --webcmd=<uri>`

播放控制使用 Windows 标准媒体键，并在发送前先把网易云音乐主窗口激活，降低控制错播放器的概率。

## 当前曲目

网易云音乐主窗口标题直接暴露：

`歌曲 - 歌手`

Provider 会拆成：

- track
- artist
- raw windowTitle

## 真实验收

2026-10-05：

- installed = true
- running = true
- 当前窗口：`幸福了 然后呢 - 黄丽玲`
- track = `幸福了 然后呢`
- artist = `黄丽玲`

正式 API 连续调用两次 `netease.playPause` 均成功，第二次恢复原播放/暂停状态，当前曲目未变化。

本地文件播放仅接受本机注册的音频后缀。
`netease.openUri` 只允许 `orpheus://` scheme，拒绝把任意 URI 交给 shell。

桌面、Runtime、Runtime Verifier 构建通过；Yanzi.CapabilityVerification 65/65。
