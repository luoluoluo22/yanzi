# 燕子本机 LLM API（OpenAI Chat Completions 兼容）

新增接口复用现有 Agent API 监听器、端口、Bearer Token 和「模型服务」供应商配置，不维护第二份密钥。它仅供本机应用访问：服务端对非回环客户端已有拒绝策略。

## 使用

1. 在燕子设置中启用 **Agent API**，为 Agent API 设置**非默认的私有 Token**。历史示例 token \`yanzi-local-dev-token\` 不允许调用 LLM API，防止任意本地软件轻易消费付费额度。
2. 在「模型服务」中启用供应商、录入 API URL/Key，并将模型加入该供应商的模型列表。
3. 取本机实际配置的 Agent API 端口（例如 \`42980\`；不同安装配置可能不同），访问 \`http://127.0.0.1:<port>/v1\`。

**模型发现**

\`\`\`bash
curl http://127.0.0.1:42980/v1/models -H "Authorization: Bearer YOUR_PRIVATE_AGENT_API_TOKEN"
\`\`\`

每个模型返回 \`id="<providerId>/<upstreamModelId>"\`、\`owned_by\`、\`provider_id\`。同名模型必须使用该带供应商前缀的 ID 进行选择。唯一模型名也可直接用作 \`model\`；\`default\` 使用燕子当前选定的供应商和模型。

**普通对话**

\`\`\`bash
curl http://127.0.0.1:42980/v1/chat/completions \
  -H "Authorization: Bearer YOUR_PRIVATE_AGENT_API_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"model":"default","messages":[{"role":"user","content":"你好"}]}'
\`\`\`

**流式调用**

\`\`\`bash
curl -N http://127.0.0.1:42980/v1/chat/completions \
  -H "Authorization: Bearer YOUR_PRIVATE_AGENT_API_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"model":"default","stream":true,"messages":[{"role":"user","content":"你好"}]}'
\`\`\`

**Python OpenAI SDK**

\`\`\`python
from openai import OpenAI

client = OpenAI(
    base_url="http://127.0.0.1:42980/v1",
    api_key="YOUR_PRIVATE_AGENT_API_TOKEN",
)
response = client.chat.completions.create(
    model="default",
    messages=[{"role": "user", "content": "你好"}],
)
print(response.choices[0].message.content)
\`\`\`

## 实现与边界

- OpenAI-compatible / New API / CherryIN / Ollama / Ego ChatGPT 类供应商：透传标准 \`/chat/completions\` JSON、工具调用和 SSE。供应商须真正支持 OpenAI chat/completions 协议。
- Anthropic、Gemini：文本聊天适配成统一返回格式；\`stream=true\` 会返回符合 SSE 规范的一次性结果，目前**不是原生逐 token 流式**，且暂不支持其工具调用、图片内容块、复杂结构消息。明确拒绝无法无损转换的输入，避免静默丢字段。
- \`OpenAI-Response\` 类型的原生 Responses API 暂未适配；错误码为 \`unsupported_provider_protocol\`，不错误地将它当作 chat completions。
- Azure OpenAI 需要服务商给出兼容的 \`/chat/completions\` URL（含正确部署和 api-version 参数）。本版未提供专属 Azure 签名/版本拼装。
- 最大请求 JSON 为 1 MiB；上游请求超时 4 分钟。无法访问提供商返回结构化错误，不会返回其 Key 或原始错误正文。
- API 只读取燕子模型服务配置。修改供应商、启用状态和模型列表后无需重启 API 服务；下一次请求重新读取配置。
- **本功能不代表提供免费模型额度。** 调用消耗的是所选供应商现有账户的额度/套餐资源；错误、限流及计费规则由供应商决定。
