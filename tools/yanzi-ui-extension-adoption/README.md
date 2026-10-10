# 燕子小程序公共 UI 接入：首批两款 WPF 小程序

最后核对：2026-10-09

## 评估结果

| 类型 | 小程序 | 本轮决策 | 原因 |
| --- | --- | --- | --- |
| 标准 WPF 窗口 | 语义搜索 `semantic-search` | **已接入** | 入口搜索框和搜索/索引按钮可直接复用通用 Input/Button |
| 标准 WPF 窗口 | 能力实验室 `capability-lab` | **已接入** | 普通按钮、状态卡片、日志卡片符合通用 Card/Button 语义 |
| WPF 状态面板 | 键盘修改器、闲置任务看板 | 后续候选 | 需要在启停、热键、状态刷新期间测试现有窗口行为 |
| 高交互 WPF | 剪贴板、任务栏日历、灵感白板 | 分阶段迁移 | 依赖精确尺寸、鼠标焦点、窗口停靠、画布和热重载，不宜整页批量替换 |
| 独立图片编辑工具 | 燕子截图 | 后续专项 | 绘图画布、工具栏和 OCR 进度不能套用通用表单组件 |
| Web / mobile-js | 笔记、Web 小程序 | 暂不采用 WPF 控件 | 应复用相同设计 Token，但通过各自前端框架实现视觉组件 |

## 改造

不修改用户数据、搜索算法或扩展权限：仅在窗口初始化时，尝试通过运行时反射加载同一份 `Yanzi.UI.Wpf.dll`，调用 `YanziUi.ApplyTo(window, Dark)`。成功时仅将适合的原控件改为公共资源键 `Yanzi.Input`、`Yanzi.Button.Primary`、`Yanzi.Button.Secondary`，并为卡片设置 `Yanzi.Color.Card` / `Yanzi.Color.Border`。**不触发“更新索引”“运行全部实验”等操作**。

正式燕子中的 Roslyn C# 小程序编译器目前不自动引用 `Yanzi.UI.Wpf`，因此直接 `using Yanzi.UI.Wpf` 失败。公共桥接使用反射避免编译期依赖；库缺失时自动保留旧外观，不阻断原有窗口运行。公共组件实际实现仍只有同一份 DLL。

共享库部署路径：

`%LOCALAPPDATA%\OpenQuickHost\SharedUI\Yanzi.UI.Wpf.dll`

脚本安装并不覆盖正式燕子的 Runtime。发布新版燕子时应另外将此 DLL 纳入正式扩展资源部署流程，避免依赖固定的开发机路径。尚未对所有小程序进行强制切换。

## 版本化与安装

已安装扩展的源码在用户配置目录而非 Git 仓库中，所以**仅保存原地备份还不够**。这份目录保存了 SHA256 固定的两份小补丁、安装脚本和原版/改版文件的校验值。

在仓库根目录完成 `Yanzi.UI.Wpf` Release 构建后：

```powershell
.\tools\yanzi-ui-extension-adoption\install.ps1
```

脚本对每个扩展：

1. 检查当前源码 SHA256：如果已经是目标版本则跳过，如果不是原版或改版则停止（不能覆盖用户的新改动）。
2. 对原版先备份，再使用 Git apply 应用精确补丁。兼容 Windows 上 Git 自动转换 CRLF 的情况。
3. 用目标 SHA256 检查改版字节；失败则立即恢复备份。
4. 最后复制 Release 共享 DLL，并校验 DLL SHA256。

临时目录里已完成“原版 → 安装 → 重复安装”全流程验证，两次都成功；本机两个小程序当前已经使用改版源码。

## 已完成实机验证

- 能力实验室 `extension.open` 返回成功，实际 `能力实验室 · 燕子` 窗口出现；操作按钮和卡片真实渲染。
- 语义搜索先停止旧后台实例，再重新 `extension.open`；`语义搜索` 窗口出现，搜索框支持实际键盘焦点和文本设置/恢复。
- Windows UI Automation 查到了“搜索”“更新索引”“运行全部实验”“检查”按钮，均仍可用。
- 运行中的正式燕子宿主未被替换；小程序未调用索引重建、依赖安装或批量实验。
- 实机截图：
  - `F:\Desktop\Yanzi-UI-视觉对照\miniapp-capability-lab-shared-ui.png`
  - `F:\Desktop\Yanzi-UI-视觉对照\miniapp-semantic-search-shared-ui.png`

## 回退

原始入口代码的备份在各小程序安装目录下：

`semantic.cs.backup-ui-20261009-000956` 和 `CapabilityLab.cs.backup-ui-20261009-000956`。

如出现故障，停止相应小程序，然后将其对应备份恢复为 `semantic.cs` / `CapabilityLab.cs`，重开小程序即可恢复旧界面，不需要卸载燕子。两份备份路径的校验值保存在 `source-hashes.json`。

## 运行中程序集的安全升级

2026-10-09 最终复核发现：即使关闭扩展窗口，长期运行的正式燕子宿主仍持有已加载的 `Yanzi.UI.Wpf.dll`，Windows 不允许覆盖。安装脚本不会强制结束宿主，也不会冒险覆盖锁定文件。它将新版 DLL 写入校验过的 `Yanzi.UI.Wpf.dll.pending`，再调用 Windows `MoveFileEx` (`MOVEFILE_DELAY_UNTIL_REBOOT | MOVEFILE_REPLACE_EXISTING`) 预约**下一次开机**时原子替换；预约已得到 Windows 成功返回，实际替换仍须下次启动后确认。当前兼容版本已经实机验证，可继续运行至关机。

## V2：从小程序反射桥接升级为宿主直接引用（本轮）

新版本的宿主（`ScriptExtensionRunner`）把公共 UI 的固定程序集引用、可收集上下文解析及缓存版本管理集中在一个位置；小程序代码中不再需要粘贴 `YanziNativeUiBridge`。

- `CompilerVerification`：使用真正宿主的动态 Roslyn 引用与注入源文件，编译两款去重源码并验证真实 WPF 深色主题加载；默认读取小程序目录里 **持久化暂存且 SHA256 校验的 `.ui-v2-pending` 文件**，不再依赖临时目录。
- `verify.ps1`：在仓库根目录执行 `& .\tools\yanzi-ui-extension-adoption\verify.ps1`，一键完成安全暂存、直接编译、STA WPF 渲染、289 项公共组件检查及 18 项隔离 Runtime 回归。`-SkipRuntime` 可用于缩短日常快速测试。
- `patches/*-v2.patch` 与 `source-hashes.v2.json`：从已安装的 V1 升级到 V2 的增量、可核验补丁。
- `install-v2.ps1`：**默认只暂存，正式扩展源码不发生变化**。只有确认新的宿主版本已发布时，才允许用 `-Activate -VerifiedHostAssembly <Yanzi.dll 路径>` 激活；新宿主未部署前请不要启用。

量化结果：语义搜索 762 → 697 行、能力实验室 922 → 874 行，两款合计从 1,684 减到 1,571 行。详见 `docs/yanzi-ui-source-dedup-2026-10-09.md`。

## 独立可发布候选（未激活）

从 Git `6fc2a6d` 创建了不含其他未提交改动的独立工作树，构建出 Runtime、Host 和公共 UI DLL，三个构建产物 SHA256 记录在 `release-candidate.json`。可以用：

```powershell
.\tools\yanzi-ui-extension-adoption\verify-release-candidate.ps1
```

复测会验证三份产物的**准确 SHA256**、两款扩展真实 Roslyn 动态编译、STA WPF 样式解析和 18 项 Runtime 隔离生命周期检查。复测曾因通过 `dotnet <verifier.dll>` 启动测试而超时；修复为运行验证程序自身的 Windows `.exe` 后，整条链路已通过。

正式小程序升级还需要先让正式 Runtime 使用经过验证的候选 Host + Runtime + UI DLL 三件套，安装脚本也会再次核对正在运行的进程哈希。此时 `install-v2.ps1` 只暂存源码，不主动部署或重启正式 Runtime。

## 真实宿主编译与安全暂存（2026-10-09）

`CompilerVerification` 现增加实际 `ScriptExtensionRunner.PreparePortableAssetsAsync` 的两款 V2 动态编译和重复编译缓存一致性验证，不仅运行测试程序自行创建的 Roslyn 编译。

对已验证的干净 Runtime 发布候选，执行：

```powershell
.\tools\yanzi-ui-extension-adoption\verify-release-candidate.ps1
.\tools\yanzi-ui-extension-adoption\stage-release-candidate.ps1
```

第二条命令仅将完整版本存入 `%LOCALAPPDATA%\YanziRuntime\staged\ui-v2-<commit>`，哈希核对 Runtime / Shell 全量文件，不会修改 `runtime.json` 或停掉正式程序。独立快照额外通过 Runtime 18 项测试。

通用 `scripts/install-shared-runtime.ps1` 的非激活路径已改为真正只暂存；`verify-runtime-staging.ps1` 用临时安装目录重复验收正式指针不变。**这不是正式上线**：只有新版宿主先成功激活并完成健康检查，才能启用两款 V2 小程序源码。

## 后续限制

本轮改的是两套 WPF 窗口的主要标准控件，特殊结果列表及任务操作面板没有做全量像素级评估。项目的扩展编译器未来可以统一提供一个安全的共享 UI 加载服务，避免每款小程序都维护几行反射桥接。
