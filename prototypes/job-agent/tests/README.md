# 求职小程序安全回归测试

运行：
`dotnet restore Smoke.csproj --configfile NuGet.Config`
`dotnet run --project Smoke.csproj --no-restore`

以真实的 `JobAgent.cs` 源码编译并使用合成数据验证持久化与消息安全。不会登录智联、投递简历、发送消息或触及正式用户数据。
