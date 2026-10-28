# 燕记声音记忆：本地录音 MVP（2026-10-11）

## 目标与边界
- Android 原生录音，不依赖外部第三方录音 APP，不上传至燕子云端。
- 只能由用户在前台明确点击「开始录音」启动；录音有持续通知及停止入口。
- Android 14+ 在前台启动 microphone 类型前台服务，锁屏后允许继续；不绕过系统麦克风指示及后台启动限制。
- 本版本仅完成录音链路，未支持 12 小时定时、AI 转写、VAD、加密、导出/跨设备同步与服务器储存。

## 实现
- `VoiceMemoryActivity.java`：入口在「我的 → 声音记忆」，手动开始/停止、列表与 WAV 播放。
- `VoiceMemoryService.java`：独立 `AudioRecord` 前台服务，16 kHz / PCM 16-bit / mono，PARTIAL_WAKE_LOCK，系统通知提供「停止录音」。
- `VoiceMemoryStore.java`：每 15 分钟分段写入 `.wav.partial`，最终修复 RIFF 数据长度后重命名为 `.wav`；重启查看页面时恢复尚未完成的分段。
- 路径：`Android/data/<applicationId>/files/Music/VoiceMemory/`，按设备、APP 包名本地隔离；云端备份与设备迁移排除这一目录。
- Android QA 测试包可将分段时长改成 8 秒，发布环境仍强制 15 分钟。QA 包名 `cc.luoluoluo.yanzi.mobile.voiceqa`；正式版 / 常规 Dev 版不暴露录音 Activity 给其他应用。
- 为隔离测试包，`YANZI_ANDROID_DEV_SUFFIX=.voiceqa` 构建时生成独立包名和名称 `燕记录音测试`；普通 Dev 构建维持默认 `.dev`。

## 构建 QA 测试包
在 `mobile/android`：
```powershell
$env:ORG_GRADLE_PROJECT_YANZI_ANDROID_DEV_SUFFIX = '.voiceqa'
.\gradlew.bat :app:assembleDev --offline
adb -s 81f7e66d install -r app\build\outputs\apk\dev\app-dev.apk
```
请勿卸载 / 降级已有 `cc.luoluoluo.yanzi.mobile.dev` 来替代 QA；原设备上的 Dev 版本为 0.2.55-dev。

## 2026-10-11 实机结果
- 一加 7 / Android 11 / ADB 81f7e66d。与正式版及常规 Dev 版并存。
- 录音前台服务实际为 `isForeground=true`；Android audio service 显示 MIC 16000Hz PCM 正常采集。
- QA 8 秒分段连续生成多个完整 WAV；停止录音后无活跃麦克风，最后不足一段的数据能封存。
- 录音列表的「播放」操作能启动 MediaPlayer，Android 媒体服务可检测到测试包的播放会话。
- 熄屏约 18 秒后 `mWakefulness=Dozing`，麦克风仍 active，持续产生 WAV。
- 故意强制停止 QA 进程留下的 `.wav.partial`，重新启动 VoiceMemoryActivity 后自动修复成有效 WAV，ffprobe 验证恢复段为 3.072 秒、16 kHz / 单声道 PCM。
- 尚未完成脱离 USB 的电池续航测试及 12 小时稳定性测试。

## 后续优先顺序
1. 在用户可见的录音状态下评估 1–12 小时续航/中断/磁盘占用，并支持低功耗 Opus/AAC。
2. 添加录音分段索引与用户确认的删除、导出、本地加密。
3. 离线 VAD 只做语音区间标注，不在采集端不可逆删除声音。
4. 本地转写、说话人分离和棘轮索引由异步任务执行，失败不影响录音线程。
5. 对可能录入的其他人，应明确告知并取得适当同意；提供易用的一键暂停。
