# 燕子小程序设备策略（公共宿主能力）

本功能属于 **宿主共用机制**，适用于 C#、PowerShell、WebView 和本地扩展目录中的其他小程序，不需要各个小程序自行判断电脑名称。

## 三个相互独立的策略

每个小程序的 `manifest.json` 可添加：

```json
{
  "devicePolicy": {
    "mode": "selected",
    "deviceIds": ["desktop-66655fb4194741b3a3d8e82310551759"],
    "packageSync": "targets",
    "dataSync": "device"
  }
}
```

| 字段 | 可选值 | 含义 |
| --- | --- | --- |
| `mode` | `all`, `selected` | 所有设备均可显示运行，或仅列表内的稳定设备 ID 可运行 |
| `deviceIds` | 稳定设备 ID 数组 | 只有 `selected` 时有效；空数组**拒绝所有设备**，避免配置不完整时误运行 |
| `packageSync` | `all`, `targets`, `none` | 允许任意授权设备同步包、仅目标设备自动安装并在云端备份、完全不自动上传下载 |
| `dataSync` | `shared`, `device` | 使用原有账号/个人数据云同步，或强制本机读写且不进行账号同步和个人云备份 |

**向后兼容：** 未声明 `devicePolicy` 的现有小程序采用 `all + all + shared`，维持原行为。未知的运行模式或包同步值按照拒绝处理。

设备标识由 `DeviceIdentityStore` 生成并保存在本机的 `device-identity.json`，不要用可能修改的电脑名称作为长期主键。可以通过 `extension.device-policies` 能力查看本机 ID 和全部本地小程序的策略（包括在本机不可运行但留存的安装包）。

## 设备专有和数据专有是不同概念

- **所有电脑共用代码、共用数据：** `all / all / shared`，例如需要跨设备一致的笔记。
- **所有电脑共用代码、各自保存数据：** `all / all / device`，例如某些电脑本地使用习惯。
- **仅台式机运行：** `selected` + 台式机 ID + `targets / device`。台式机能备份代码包，笔记本不启动、不显示、不自动安装，数据不跨设备。
- **完全本地的小程序：** `selected` + `none / device`。既不参加代码包自动云同步，也不参加数据同步。

## 实现边界

- **显示与运行：** `LocalExtensionCatalog.LoadCommands` 在加载候选命令时过滤设备；`ScriptExtensionRunner` 在实际执行时再次检查，`AppExtensionWindow` 创建/激活时也有保护，防止旧缓存/后台调用绕过界面。宿主启动扩展时使用经过过滤的命令。
- **账号私有小程序库：** 账户批量同步上传会尊重 `packageSync`，拉取时预检远端 manifest；`ExtensionInstallService` 在解包后、替换原目录前再次拒绝未授权设备。
- **个人云备份：** 只对允许同步的包构建快照；读取 WebDAV 包后根据内嵌 manifest 再判定安装资格。不同设备暂时可能共享远端索引，但不会把未授权代码部署到目标设备。**不把“该电脑无安装”理解为云端删除**。
- **数据隔离：** `ExtensionStorageService` 按策略强制 `local`，包括小程序显式要求 `cloud`/`both` 的普通存储 API；`AccountExtensionDataStore` 和个人云备份底层额外禁止设备独立数据读写和云端恢复，避免 WebView 的直接账号存储桥接绕过策略。原始数据保留在各自设备的 `ExtensionStorage/{extensionId}`。
- **显式账号接口：** 当小程序选择 `dataSync:device` 时，专为账号云设计的 `storage.accountRead/accountWrite` 不会隐式泄漏数据到账号，也不会假装远程写入成功，而是返回账号不可用；需要设备本地数据应改用 `storage.get/put` 或标准扩展存储接口。
- **本地配置防泄漏：** 自动打包忽略 `debug.log`、`rice-cooker-link.json`、`*.local.json` 和 `*.user.json`。使用其他命名规则的敏感文件应通过独立存储目录管理，避免直接写入小程序源码目录。
- **更新策略安全：** 修改模式不会自动删除其他设备已有代码和数据，只阻断后续自动启动、显示及云端数据读写；老版本宿主必须更新后才具备执行时拦截能力，不能仅靠新 manifest 保证老宿主隐藏。
- **权限说明：** 这里是设备定向运行与数据路由策略，不是抵抗恶意用户复制小程序源码的 DRM。其他账户若另行获取小程序文件，无法以此替代 OS 权限或服务器鉴权。

## 验收

从仓库根目录执行：

```powershell
dotnet run --project tests/ExtensionDevicePolicyVerification/ExtensionDevicePolicyVerification.csproj
dotnet build src/OpenQuickHost/OpenQuickHost.csproj
```

独立测试包含共用默认值、目标电脑与非目标电脑、限制安装/运行、包同步、数据隔离、无效策略及目录穿越。构建完成不等于已更新任何一台电脑的正式宿主；须通过正式发布和两端运行时验收，特别验证账号库与个人备份不会重新覆盖设备配置。