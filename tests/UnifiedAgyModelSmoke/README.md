# 已安装燕子 AGY 统一 API 端到端测试

在 Windows 上已正式安装并运行燕子，且本地 AGY CLI 已登录时执行：

```powershell
dotnet run --project tests/UnifiedAgyModelSmoke/UnifiedAgyModelSmoke.csproj -c Release
```

测试由当前 Windows 用户在本机读取燕子独立的 LLM API 凭证，仅发送到 `127.0.0.1:42980`，不打印或上传密钥。验证 `GET /v1/models` 包含 AGY 小程序所有的注册项，以及在没有预先打开小程序窗口时 `POST /v1/chat/completions` 真实返回 `OK`。测试会实际调用模型，可能产生调用成本；不纳入默认 CI。
