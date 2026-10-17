# K70 USB 连接常亮与 10 页公开推荐内容深度测试

日期：2026-10-17。机型 Redmi K70（23113RKC6C）；Android USB 调试已授权，ADB 序列号 `e3e68545`。本轮不涉及微信、私信、支付、密码或 App 私有数据库；所有个性化推荐截图和正文仅保存在本地电脑私有目录，不提交 Git。

## 1. USB 常亮设置：已在 K70 真机写入并验收

- 原始 `settings get global stay_on_while_plugged_in`：**0**，未开启开发者“保持唤醒”。
- 原始系统熄屏设置 `screen_off_timeout`：**600000 毫秒 = 10 分钟**。
- 实际执行 `adb shell settings put global stay_on_while_plugged_in 2`，仅启用 **USB powered**（不包含 AC 电源位 1、无线充电位 4）。手机无需保持与桌面软件持续通信。
- **USB 实际连接时** Android 返回 `mStayOn=true` 与 `mStayOnWhilePluggedInSetting=2`，原屏幕自动休眠时长仍为 600000 毫秒。
- **通过系统电池服务模拟 USB 断开** `dumpsys battery unplug` 后，Android 返回 `USB powered=false` 和 `mStayOn=false`。紧接着执行 `dumpsys battery reset` 清除模拟，Android 恢复真实 USB 供电状态 `USB powered=true` 与 `mStayOn=true`。
- 这是**真实系统电源管理行为的模拟拔线测试**，不是物理拔掉 USB 的实测。真实拔线后如果没有其他 USB 供电，保持唤醒条件消失，将使用原来的 10 分钟熄屏规则。
- **重要限制：Android 的此原生设置只判断 USB 是否供电，并不严格判断 USB 调试会话是否在线。普通 USB 充电器也可能触发常亮。** 若未来要求“必须是真实 ADB 调试连接才常亮，纯 USB 充电要正常熄屏”，需另建本机/手机双端状态检测机制；本次未实现该更严限制。

重现与恢复：

```powershell
$adb = 'F:\SDK\platform-tools\adb.exe'
& $adb -s e3e68545 shell settings get global stay_on_while_plugged_in
& $adb -s e3e68545 shell settings get system screen_off_timeout

# 启用 USB 供电常亮（当前 K70 已开启）
& $adb -s e3e68545 shell settings put global stay_on_while_plugged_in 2

# 若想回到这次修改前的原始行为（非执行指令，供人工恢复）
& $adb -s e3e68545 shell settings put global stay_on_while_plugged_in 0
```

## 2. 10 页公开推荐内容研究

延续此前的两页摸底（详见 [公开推荐内容字段验收](android-public-feed-content-20261017.md)），通过只读滚动测试 B 站、小红书、抖音各 10 个可见屏幕。每页仅保存 ADB 截图和能获取到的 Android 无障碍文字；未点击任何视频或笔记详情，也未点赞、评论、发送消息、修改收藏或触发支付。

| 真机指标 | B 站 | 小红书 | 抖音 |
| --- | ---: | ---: | ---: |
| 连续采样的屏幕页数 | 10 | 10 | 10 |
| 本地截图数 | 10 | 10 | 10 |
| 成功获取无障碍 XML 的页数 | 0 | **10** | 0 |
| 识别出的卡片出现次数 | 未能测量 | **66** | 未能测量 |
| 标题和作者去重后的条目 | 未能测量 | **46** | 未能测量 |
| 重复出现的卡片 | 未能测量 | **20** | 未能测量 |
| 已提取卡片中标题有值 | 未能测量 | 66/66 | 未能测量 |
| 已提取卡片中作者有值 | 未能测量 | 66/66 | 未能测量 |
| 已提取卡片中点赞数字有值 | 未能测量 | **62/66（93.94%）** | 未能测量 |

**关键区分**：小红书的 66/66 标题和作者是“已经被算法识别为卡片的条目中字段有值”，并不是“截图中全部卡片的提取召回率 100%”；仍可能有部分可见卡片没有暴露文本。仅有“赞”而无数量的控件，点赞字段记录为缺失。

小红书每页新识别卡片数按页 1–10 依次为 **6、6、4、4、4、4、4、5、4、5**，合计 46；10 页累计识别的 66 次卡片出现中，20 次是滚动造成的重复。这说明必须跨页去重，不能把“控件数量”视为内容条数。

### B 站和抖音失败原因

本轮两款 App 的动态图像/视频页面持续刷新，Android 的传统 `uiautomator dump` 返回 **`ERROR: could not get idle state.`**。一次燕子云端 `mobile.screen.tree` 备用测试也发生请求超时，所以没有将这一轮视为有效的结构化数据提取成功。两款 App 先前 **2 页较小范围** 的界面内容提取已曾成功；不能由此声称它们总是不可读。相反，应区别**动态 UI 导出机制的时序问题**与**App 有意禁止内容访问**这两类情况。

本轮退化机制在无障碍失败时仍保留仅本地的截图，不把失败页面虚报为“0 张推荐卡片”。以后可能通过燕子自有 AccessibilityService 的直接节点快照、降低界面空闲等待、或针对截图做单屏 OCR/视觉识别实验来补齐；不需要先尝试破解 HTTPS。

## 3. 数据存储和复用

所有研究输出位于 **`%LOCALAPPDATA%\OpenQuickHost\NetworkCaptures\K70\`**，原始卡片标题、用户名、截图都只在这台电脑：

- `visible-feed-deep-bilibili-20261017-140845/`：10 张 B 站截图，结构化 XML 不可用。
- `visible-feed-deep-xiaohongshu-20261017-141007/`：10 张小红书截图、10 页控件候选、66 次卡片信息和 46 条去重结果。
- `visible-feed-deep-douyin-20261017-141145/`：10 张抖音截图，结构化 XML 不可用。
- `app-visible-feed-deep-index.json`：统一统计索引，以 `null` 表示无法测量，而非填零。

已编写两份研究脚本：
- `scripts/yanzi-visible-feed-deep-probe.py`：从解锁亮屏的目标 App 首页顺次采样，最多 12 页，检查锁屏和应用前台，支持截图退化并避免错误计数。
- `scripts/yanzi-visible-feed-deep-analyze.py`：离线统计每款 App 的页面成功率、字段完整率（仅已提取卡片）、重复数量和截图证据路径，输出到个人电脑目录，不将原文提交 Git。

隐私与保留期：私有截图合计约 58 MB。深度采集脚本启动时会清理超过 **7 天**且只包含本工具预期文件名的旧实验目录。注意，这属于**下一次运行时清理**，不是后台准时自动删除。如果以后需要长期采集，应另设计自动到期清理及抽样方式，避免图像无限增长。

## 4. 本轮最终结论

1. USB 供电常亮设置已经生效，Android 的电源状态切换与恢复已经验证，屏幕原始超时保留。
2. 小红书推荐页支持连续 10 页的无障碍结构化提取，至少能获得标题、作者、部分点赞数，且需要跨页去重。
3. B 站和抖音可连续获取屏幕截图，但在本轮的高动态界面下传统 UIAutomator XML 导出不稳定；应优先研究**直接 AccessibilityService 快照**或图片识别兜底，而不是再反复抓 HTTPS 数据包。
