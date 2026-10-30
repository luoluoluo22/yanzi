# 燕子本机 LLM API（OpenAI Chat Completions 兼容）

燕子在现有 Agent API 端口上提供 OpenAI 兼容的模型接口。由「模型服务」统一管理上游供应商和模型，调用应用只需要本机地址、独立 LLM Token 和模型名称，不接触上游供应商 Key。

## 开通与调用

1. 在「燕子设置 → Agent API」保持本机 API 启用，找到「LLM API 专用密钥」，点击「复制 LLM 密钥」。首次使用会生成 256-bit 随机 Token，并以 Windows 当前用户 DPAPI 加密保存。各台电脑的 Token 独立，不能直接跨电脑复制加密文件。
2. 在「模型服务」中启用供应商并配置有效 API Key、Base URL 和模型列表。供应商 Key 由现有 AI 凭证库管理；它们不会通过 LLM API 返回。
3. API 地址是 `http://127.0.0.1:<AgentApiPort>/v1`。两台既有设备实际配置的端口为 `42980`，但其他安装可能使用不同端口。
4. 使用 **LLM 专用 Token** 作为 `Authorization: Bearer ...`；历史 Agent API Token（包括默认值）不能访问模型接口，原 Agent API 的调用方不受影响。

### 模型发现

```bash
curl "http://127.0.0.1:42980/v1/models" \
  -H "Authorization: Bearer YOUR_LOCAL_LLM_TOKEN"
```

模型 ID 采用 `<providerId>/<modelId>` 避免同名冲突；唯一模型名也允许直接填写。使用 `model="default"` 时按「模型服务」中当前活跃的服务商和模型路由。只列出已启用供应商的配置模型。

### 非流式

```bash
curl "http://127.0.0.1:42980/v1/chat/completions" \
  -H "Authorization: Bearer YOUR_LOCAL_LLM_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"model":"default","messages":[{"role":"user","content":"你好"}]}'
```

### 流式 SSE

```bash
curl -N "http://127.0.0.1:42980/v1/chat/completions" \
  -H "Authorization: Bearer YOUR_LOCAL_LLM_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"model":"default","stream":true,"messages":[{"role":"user","content":"你好"}]}'
```

### Python OpenAI SDK

```python
from openai import OpenAI

client = OpenAI(
    base_url="http://127.0.0.1:42980/v1",
    api_key="YOUR_LOCAL_LLM_TOKEN"
)
answer = client.chat.completions.create(
    model="default",
    messages=[{"role": "user", "content": "你好"}],
)
print(answer.choices[0].message.content)
```

## 实现范围及限制

- 提供 `GET /v1/models` 和 `POST /v1/chat/completions`；复用现有 Agent API 的本机监听器、端口及本机限制。LLM 专用鉴权与通用 Agent API Token 严格隔离。
- 对 OpenAI Chat Completions 兼容服务商进行 JSON 和 SSE 转发。多模型同名时要求使用 `providerId/modelId`。
- Anthropic 和 Gemini 原生协议已支持纯文本消息适配，但其 `stream=true` 目前是一次性 SSE，不是逐 token 透传，暂不支持工具调用和多模态内容。
- 原生 OpenAI Responses API 和 Azure OpenAI 的专属鉴权/部署参数未单独适配，必须使用供应商提供的 Chat Completions 兼容入口。
- 请求体上限 1 MiB，上游响应最长等待约四分钟。不把上游 Key 或原始错误正文返回给客户端。API 不提供额外免费模型额度，实际使用受上游供应商计费和限制约束。
- 密钥仅通过本机设置按钮供用户主动复制，永不嵌入公开 API 文档或日志。密钥文件损坏时拒绝访问，不能静默重置导致已接入应用失效。
