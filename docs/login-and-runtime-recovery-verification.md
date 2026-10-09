# 登录和扩展异步回调恢复验证

## 修复行为

- 自动登录在提交会话后释放认证锁，再恢复账号凭证库。凭证库内部的认证检查不再等待同一把锁。
- 凭证库恢复最多等待 12 秒；超时保留已成功的登录，调用方取消仍向上传播。
- 用户主动登录时，以原有设备 ID 请求恢复已移除的设备登记。后台心跳继续使用默认登记请求，不能自行恢复已移除设备。云端仍校验账号所有者权限。
- 源码 C# 扩展中的 `async void` 方法、局部函数、事件和定时器等回调增加异常边界。共享运行时记录扩展 ID、标题和异常；记录异常时再次失败也不会终止进程。
- `Task` 返回的异步函数继续把异常交给调用方。编译缓存版本升级，已有源码扩展会重新编译应用保护。

此保护适用于宿主编译的源码扩展。预编译 DLL 仍须自行捕获异步回调异常。保护不会修复扩展自己的不存在目录、写死的其他电脑路径或服务启动失败；这类扩展应修正配置后再启用自动启动。

## 回归入口

在 Windows 和 .NET 9 环境执行：

```powershell
dotnet build src/Yanzi.NetworkRetryVerification/Yanzi.NetworkRetryVerification.csproj -c Debug
dotnet run --project src/Yanzi.NetworkRetryVerification --no-build -- --login-recovery
dotnet run --project src/Yanzi.NetworkRetryVerification --no-build -- --async-callbacks
dotnet run --project src/Yanzi.NetworkRetryVerification --no-build
```

登录验证使用临时隔离数据目录和本地 HTTP 桩，不连接线上账号。回调验证在独立进程实际触发定时器及各类回调错误，同时检查 `Task` 异常没有被吞掉。

共享运行时集成验证在独立数据目录中实际编译、加载源码扩展，触发回调及错误记录再次抛异常，检查日志和运行时存活，同时验证界面退出/重启、能力、本地 API 和取消等行为：

```powershell
dotnet build src/Yanzi.Runtime/Yanzi.Runtime.csproj -c Debug
dotnet build src/Yanzi.RuntimeVerification/Yanzi.RuntimeVerification.csproj -c Debug
& ./src/Yanzi.RuntimeVerification/bin/Debug/net9.0-windows/Yanzi.RuntimeVerification.exe ./src/Yanzi.Runtime/bin/Debug/net9.0-windows/Yanzi.Runtime.exe
```

云端设备恢复权限验证：

```powershell
cd cloudflare
node --test src/device-reconnect.test.mjs
```

验证只允许账号所有者显式恢复登记；普通后台请求、设备授权及其他账号不能恢复。
