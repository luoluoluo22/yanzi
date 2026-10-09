# 燕子 UI：C# 小程序宿主直连与源码去重 · 2026-10-09

## 本轮目标与已证实事实

上一轮通过为每款扩展复制约 30 行 `YanziNativeUiBridge`（反射 + 本机路径加载），暂时实现公共视觉样式，但两款源文件从 1,578 行增至 1,684 行，旧样式没有删除。

本轮把编译引用、依赖解析与缓存管理迁回燕子**宿主层**；生成两款清理后的扩展源码，不再包含反射桥接。

### 源码定量比较（真实本机原版备份、已安装 V1、优化版 V2）

| 项目 | 最初备份 | 旧迁移版本 V1 | 去重版 V2（已验证、暂存） |
|---|---:|---:|---:|
| 语义搜索 | 708 | 762 | 697 |
| 能力实验室 | 870 | 922 | 874 |
| 合计 | **1,578** | **1,684** | **1,571** |
| 两份源码内重复桥接类 | 0 | 2 | **0** |
| 手工视觉属性赋值（简单词法统计） | 71 | 71 | **58** |

- V1 → V2：净减少 **113 行（6.7%）**。
- 原始备份 → V2：净减少 **7 行（0.4%）**。不能夸大为大幅降低整体业务复杂度；后续仍有非公共的业务状态色需要手动维护。
- 公共 UI 扩展仍使用相同的控件实例与事件处理器，替换的仅是按钮、搜索框、普通卡片的视觉赋值。
- 语义搜索和能力实验室尚有非通用的业务信息展示逻辑，不应为追求更少代码而删除。

## 编译器与安全边界

- `src/OpenQuickHost/YanziSharedUiCompilation.cs`：由已有的第一方 `typeof(YanziUi).Assembly` 获取可信元数据引用及对应宿主默认加载的真实程序集，外部小程序**不能**传入任意 DLL 路径。
- `ScriptExtensionRunner.cs`：固定将 `Yanzi.UI.Wpf` 添加到 Roslyn 编译引用，缓存版本更新到 `v14-shared-ui`，把 UI DLL 的 ModuleVersionId 加进缓存指纹；新旧组件更替不会误用旧编译缓存；可收集 AssemblyLoadContext 根据准确程序集名称解析回宿主共享程序集。
- 编译器中已有的 Net90 参考程序集与运行时 CoreLib 某些类型定义发生冲突，特定 `System.Threading.Thread (net90)`、`System.Text.Encoding.Extensions (net90)` 被排除，实测 Thread / UTF8Encoding 扩展正常编译。
- 不扫描小程序目录下同名 DLL 作为公共 UI 的优先来源，优先使用已知第一方宿主程序集，避免扩展劫持公共 UI 的装载。
- 没有改变正在运行的正式燕子版本与正在使用的小程序源码。

## 已完成测试

1. `dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Release -p:SkipStopRunningApp=true`：0 错误，14 个宿主原有警告。
2. `tools/yanzi-ui-extension-adoption/CompilerVerification`：通过反射调用**真正的** `BuildCSharpMetadataReferences()`，注入宿主的实际 C# Runtime Source 与 Global Usings，用 Roslyn 对两款 V2 原版业务代码动态编译，并在可收集上下文中加载扩展入口；**2/2 成功**。
3. 另在 STA 线程实际实例化 WPF Window，装载公共深色主题，把 Button/Input 加入可渲染布局并执行 `Show/UpdateLayout`；主题 Style 解析均成功。
4. 从用户安装目录里的 V1 源码，通过新增差分补丁重建 V2，Git apply 内容一致且与 `source-hashes.v2.json` 的 SHA256 一致。
5. `install-v2.ps1` 默认**只暂存**优化版为 `*.ui-v2-pending`；SHA256 和旧源码保护通过，仍可运行的正式小程序不变。

## 可重复的自动验收入口

`tools/yanzi-ui-extension-adoption/verify.ps1` 已实现一次调用：安全暂存两款扩展 V2（不激活）、对暂存文件做 SHA256 校验、真实宿主 Roslyn 编译 2/2、STA WPF 实例渲染、公共 UI 289 项回归、独立 Runtime 18 项回归。2026-10-09 已完成全链路执行并通过。

生产保护也经过**故意尝试激活旧宿主**的负面测试：明确拒绝，且两款生产源码 SHA256 保持不变；新宿主尚未发布时不执行替换。

## 独立干净构建发布候选

- 干净 Git 工作树：`F:\Desktop\kaifa\OpenQuickHost-ui-v2-clean-check`，提交 `6fc2a6d`，无未提交文件。
- 独立宿主与 Runtime Release 构建成功；两款 V2 动态编译、STA WPF 渲染和隔离 Runtime 18 项测试成功。
- `tools/yanzi-ui-extension-adoption/release-candidate.json` 固定验证过的 Host、Runtime 和 UI DLL 的 SHA256。必须三者同时匹配，不能混入主开发目录其他未提交代码产物。
- `verify-release-candidate.ps1` 提供可重复的整套哈希与运行时验收。实测使用 Windows .exe AppHost 启动隔离 Runtime 校验程序可成功；直接通过 dotnet DLL 启动可能因其进程自检机制不同而等待超时。
- 尚**未发布和替换**正在运行的正式 Runtime，原扩展业务行为与数据不受本轮发布候选影响。

## 部署阶段与待办

当前正式 `Yanzi.Runtime.exe` 为 2026-10-08 发布目录内的旧版，尚未安装本轮新的宿主编译器。**不能仅先替换扩展为 V2**，因为旧宿主不保证编译时能直接引用新公共库。

当新宿主的独立发布与启动回归完成后，运行 `install-v2.ps1 -Activate -VerifiedHostAssembly <已验证的宿主 Yanzi.dll>`。脚本会核对宿主 DLL 与本轮测试构建的哈希，先备份 V1 文件，再激活新版本；中途失败则恢复原始文件。脚本默认暂存，绝不自动启用未经验证的 V2。

下一轮：把新宿主通过正式发布链路部署后，在真实新 Runtime 中分别打开两款小程序，检查 UI 与所有操作、日志、热重载、回退，再决定清理仍存在的业务专有手写颜色。

## 与其他未提交工作隔离

`ScriptExtensionRunner.cs` 在本轮开始前已经存在其他任务的修改（后台触发分发）。该部分既不属于 UI，也不应被本次提交顺带包含。提交时必须使用仅针对共享 UI 编译引用的精确索引补丁，避免带走用户其他正在进行的工作。
