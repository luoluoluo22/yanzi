# 燕子 → Windows 全程序环境变量

## 行为

- 燕子成为主实例后，在启动服务/小程序前调用 `WindowsEnvironmentVariablePublisher.Synchronize()`。
- 燕子设置保存环境变量时，以及账号密钥保险箱恢复环境变量时，重新同步到 Windows 当前用户环境：`EnvironmentVariableTarget.User`（HKCU\\Environment）。
- 变量对新启动的独立 PowerShell、CMD、VS Code、Python、Node 等进程可用，不限于燕子调用的子进程。广播 `WM_SETTINGCHANGE/Environment` 通知 Explorer 刷新；已经运行的终端可能需要重开。
- 无需管理员权限。此功能不是 Windows 机器级 `EnvironmentVariableTarget.Machine`，不向其他 Windows 用户账户或系统服务发布凭证。

## 安全边界

- 燕子原始值仍保存于 DPAPI 用户加密文件，云端使用现有 E2EE 机制；**发布到 Windows 用户环境的副本位于注册表，不能继续视为 DPAPI 加密存储**。同 Windows 用户下的程序可读取这些值。
- 继续排除 ProviderOnlySecrets（目前含 `CNBLOGS_TOKEN`）和保留的内部 `YANZI_*` 上下文/令牌。
- 防止 `PATH`、`TEMP`、`SYSTEMROOT` 等基础变量被设置编辑器覆盖，忽略空值，以免同步尚未获取到值时误删除系统变量。
- 对已有外部不同值，不抢占所有权。只清理由燕子实际写入且当前值未被外部变更的变量，避免损坏其他程序配置。
- `windows-environment-ownership.json` 只记录变量名和 SHA-256 摘要，不保存密钥明文；修改值会更新摘要。
- 编辑燕子变量可能导致一般用户凭证出现在 Windows 环境配置里。需要仅限燕子内部使用的敏感凭证，应使用专用 capability/vault，而不是普通共享环境变量。

## 验收

`dotnet build src/Yanzi.CapabilityVerification/Yanzi.CapabilityVerification.csproj -c Release`

`dotnet run --project src/Yanzi.CapabilityVerification/Yanzi.CapabilityVerification.csproj -c Release --no-build -- --windows-env`

测试使用独立临时配置根目录及随机变量名，检查新建、更新、删除、外部同名冲突和 `CNBLOGS_TOKEN` 隔离，并在 `finally` 中清理变量。通过标准：`WINDOWS_USER_ENV_SYNC=PASSED`。

新安装的正式版启动后验证：`[Environment]::GetEnvironmentVariable('<变量名>', 'User')` 与燕子管理的对应值相同；测试输出中不要打印真实密钥。
