# 原生智能体工具协议

Agent 模式使用 Responses API 的 tools / function_call / function_call_output；call_id 关联调用与结果，后续请求保留 reasoning/encrypted_content。只执行完整 response.completed 中通过校验的调用。文本、Markdown、JSON 示例均不作为命令执行；失败不降级、不自动重试。普通聊天仍走原有通道。

参考 [OpenAI function calling](https://developers.openai.com/api/docs/guides/function-calling) 与 [Codex shell schema](https://github.com/openai/codex/blob/main/codex-rs/core/src/tools/handlers/shell_spec.rs)。这是协议与工具命名对齐，不是完整复制 Codex 运行时。

## 提示词与能力

- 旧燕子默认提示词按内容识别，先归档到用户数据目录 PromptBackups/legacy-yanzi-SHA256.txt，再清空该字段；其他账号和设置不随之重置。设置可留空，不再补回旧默认值。
- 通用智能体说明与实际工具 schema 由程序提供；用户补充偏好独立附加。
- 不再默认注入小程序列表、创建小程序流程或旧 execute_command JSON 协议。燕子业务 Skill 尚未实现。
- 当前提供 exec_command、write_stdin、terminate_command。PowerShell 5.1、普通管道、当前用户权限；没有 PTY、权限提升或文件/网络沙箱。session_id 为宿主字符串 ID，terminate_command 是宿主扩展。会话最长 120 秒且轮次结束自动清理。
- max_output_tokens 为近似输出预算（每 token 最多两个字符），实际截断仍按字符计数；不得宣称精确 token 计费。

## 渠道兼容

Agent 服务地址需支持 /responses。原生 Gemini 地址只支持现有普通聊天。Cookie 本地桥新增 /v1/responses 原样透传请求、SSE 与错误，后端 404/405 映射为明确的 501；不会启动仅支持文本的 DOM 对话去冒充工具调用。当前 Cookie 后端若没有 Responses 支持，仍不能执行 Agent 工具，需单独升级后端或选择支持此协议的渠道。

## 验证

`scripts/test-ai-tool-experience.ps1` 包含真实 shell 加模拟模型的原生调用闭环、错误与重复调用、增量输出、取消、卡片持久化、旧提示词迁移、普通 Gemini 聊天与 WPF 回归。模拟模型通过不等于真实供应商工具验收。`scripts/qa-ui-windows.ps1` 记录干净提交 WPF 门禁，不能替代用户桌面交互验收。
