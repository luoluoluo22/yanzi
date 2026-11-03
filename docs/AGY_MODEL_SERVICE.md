# AGY CLI 模型服务集成（开发中）

架构：
- YanziAgyModelBridge：宿主内按需启动的 HTTP 服务，监听 127.0.0.1:18764。
- YanziAgyManagedInstaller：优先复用 %LOCALAPPDATA%/agy/bin/agy.exe；缺失时从官方 manifest 下载并验证 SHA-512，安装进燕子专属受管目录。
- YanziManagedDependencyLifecycle：卸载时仅移除燕子安装、具有合法所有权收据且 SHA-256 未变化的 CLI。保留用户自装 AGY、Google 登录资料。
- 模型服务可添加 AGY CLI 服务商；AI 对话自动按需开启本地 HTTP 适配。
- 另有原生小程序 YanziMiniApps/agy-model-service，供用户操作和查看状态。

API：
GET /health、GET /v1/models、POST /v1/chat/completions。使用机器本地随机 Bearer Token。
stream=true 目前按 SSE 协议返回，但为 AGY 完成后单次推送答案，尚非逐 Token 输出。

测试：
dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Release
dotnet run --project tests/AgyBridgeSmoke/AgyBridgeSmoke.csproj -c Release
已完成宿主编译和 .NET 桥接器真实聊天测试。

待验收：小程序宿主动态编译与窗口测试；无 AGY 干净设备按需安装与卸载测试；PR 合并与正式安装。

安全：AGY 运行于 --mode plan，不默认自动批准执行权限。现有 AGY 登录凭证沿用用户本机配置，不上传给燕子。
