# 小程序模型服务注册规范（v1）

燕子宿主从本地已安装小程序的 `manifest.json` 顶层 `modelServices` 数组读取声明，而不是维护写死在宿主的服务商数组。小程序无需运行，更无需打开 UI。

示例：

```json
{
  "id": "my-model-tool",
  "modelServices": [
    {
      "id": "local-model",
      "name": "我的模型服务",
      "providerType": "OpenAI",
      "baseUrl": "http://127.0.0.1:12345/v1",
      "models": ["my-model"],
      "selectedModel": "my-model",
      "startMode": "on-demand"
    }
  ]
}
```

- 宿主自动生成稳定的服务商 ID：`miniapp:<小程序ID>:<模型服务ID>`，并记录 `ownerExtensionId`。
- 启动 / 打开设置时扫描安装清单并注册；更新清单后使用常规小程序刷新流程同步到模型服务界面；删除小程序清单/目录会在下一次扫描时撤销**其拥有**的服务商，而不会触碰用户手动添加的服务商。
- 可注册多个服务。服务商的启用开关、用户已选模型保留；名称、接口和声明的默认模型属于小程序。
- `providerType` 和 `baseUrl` 提供 OpenAI 兼容 API 的读取配置。默认不自动为第三方小程序启动进程；服务生命周期由小程序管理（`startMode` 是描述性策略）。
- 绝不能把 API 密钥硬编码到 manifest。用户自己配置一般服务商密钥；仅宿主认可的内置适配器可在清单中声明 `"credential": "agy-local"`，自动从**本机**取得 AGY 回环 API Token，远端无法声明此模式。
- AGY 的本地端点 `http://127.0.0.1:18764/v1` 由宿主内部 `YanziAgyModelBridge` 按需启动（查看模型和发消息时自动启动），**不需要打开管理窗口**。若 CLI 在设备上缺失，可从小程序设置页主动按需安装。未执行安装时，不会为了展示模型服务而下载额外程序。
- 当前 manifest 注册是声明式行为，并非允许小程序对任意服务商调用无鉴权注册/删除 API；注册/撤销严格限定归属，防止小程序删除其他服务商。

回归：`dotnet run --project tests/ModelServiceRegistrySmoke/ModelServiceRegistrySmoke.csproj -c Release`。
