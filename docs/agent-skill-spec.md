# Agent Skill 规范

这份文档定义了燕子当前对外提供给本机 AI agent 使用的 `skill` 约定、导出规则和本地扩展 API。

目标不是把 agent 跑在燕子内部，而是让外部 agent 能：

- 读取燕子内置的 skill 提示
- 按约定把 skill 安装到自己的工作目录或全局目录
- 通过本机 HTTP API 查看、创建、编辑、删除扩展

## 1. 内置 Skill 根目录

燕子程序内置的 skill 根目录是：

```text
skills/
```

当前内置了一个用于开发扩展的 skill：

```text
skills/
  yanzi-extension-dev/
    SKILL.md
    agents/
      openai.yaml
    references/
      local-agent-api.md
      manifest.md
      script-extensions.md
```

## 2. Skill 目录结构

每个 skill 至少要包含：

```text
my-skill/
  SKILL.md
```

推荐结构：

```text
my-skill/
  SKILL.md
  agents/
    openai.yaml
  references/
    *.md
```

字段说明：

- `SKILL.md`
  skill 主说明文件，描述何时使用、如何使用、约束和工作流。
- `agents/openai.yaml`
  给支持 YAML 元信息的 agent 提供展示名、简介、默认提示。
- `references/*.md`
  长文档引用，避免把所有细节都堆进 `SKILL.md`。

## 3. 当前导出规则

燕子左下角菜单里的“导出 Skill”会把程序内置的整个 `skills/` 目录复制到目标 agent 的约定目录。

### 项目级导出

项目级导出时，用户选择的是“项目根目录”。

导出映射：

| Agent | 项目级目标路径 |
| --- | --- |
| Codex | `.codex/skills` |
| Antigravity | `.agents/skills` |
| Trae | `.trae/skills` |

例如选择项目根目录 `D:\MyProject`，导出结果可能是：

```text
D:\MyProject\.codex\skills\
D:\MyProject\.agents\skills\
D:\MyProject\.trae\skills\
```

### 全局导出

全局导出时，燕子不会再要求用户选路径，而是直接导出到当前 Windows 用户目录下的约定位置。

导出映射：

| Agent | 全局级目标路径 |
| --- | --- |
| Codex | `%USERPROFILE%\.codex\skills` |
| Antigravity | `%USERPROFILE%\.gemini\antigravity\skills` |
| Trae | `%USERPROFILE%\.trae\skills` |

## 4. 导出内容语义

导出时复制的是整个 `skills/` 根目录，不是单个 skill 文件夹。

也就是说，目标目录下最终会出现：

```text
<agent skill root>/
  yanzi-extension-dev/
    SKILL.md
    agents/
    references/
```

这样做的原因：

- 方便一次导出多个 skill
- 保持和大多数 agent 的 skill 根目录习惯一致
- 后续新增 skill 时不需要改导出模型

## 5. 面向 Agent 的扩展工作流

当前推荐外部 agent 按下面顺序工作：

1. 先读取导出的 `yanzi-extension-dev/SKILL.md`
2. 按需读取 `references/manifest.md`
3. 如果要写脚本扩展，再读取 `references/script-extensions.md`
4. 如果要直接操作本地扩展，再读取 `references/local-agent-api.md`
5. 通过本机 HTTP API 对扩展做 CRUD

## 6. 本地 Agent API

燕子提供本机 HTTP API 给外部 agent 使用，默认监听：

```text
http://127.0.0.1:53919
```

请求头：

```text
X-Yanzi-Token: <token>
```

当前用途：

- 列出扩展
- 获取模板
- 读取指定扩展
- 创建扩展
- 替换扩展
- 重命名扩展
- 设置或清空快捷键
- 删除扩展

主要接口：

- `GET /health`
- `GET /v1/extensions`
- `GET /v1/extensions/template`
- `GET /v1/extensions/{id}`
- `POST /v1/extensions`
- `PUT /v1/extensions/{id}`
- `PATCH /v1/extensions/{id}/rename`
- `PATCH /v1/extensions/{id}/shortcut`
- `DELETE /v1/extensions/{id}`

详细请求体见：

- [skills/yanzi-extension-dev/references/local-agent-api.md](../skills/yanzi-extension-dev/references/local-agent-api.md)

## 7. 扩展类型

当前支持两类扩展：

### 单文件 JSON 扩展

最小结构：

```text
Extensions/
  my-extension/
    manifest.json
```

适合：

- 打开文件
- 打开目录
- 打开 URL
- 搜索前缀类命令
- 轻量内联动作

单 JSON 轻量动作建议优先使用：

- `runtime: "csharp"`
- `entryMode: "inline"`
- `script.source`

### 脚本扩展

最小结构：

```text
Extensions/
  my-script/
    manifest.json
    main.ps1
```

当前运行时：

- `csharp`
- `powershell`

脚本扩展适合：

- 处理快捷面板传入的选中文本
- 读取剪贴板
- 获取前台窗口
- 访问本地系统能力
- 做更复杂的自动化逻辑

## 8. 规范约束

### 对燕子本身

- 只负责发现、同步、管理、执行扩展
- 不审核用户分享的脚本内容
- 对外提供 skill 和本机 API，方便 agent 编写扩展

### 对外部 agent

- 优先通过本机 API 改扩展，而不是直接猜测运行目录
- 修改扩展协议时，要同步更新 skill 文档
- 新扩展优先使用 C# 内联动作，除非任务明确需要 Windows shell 自动化
- 如果是 PowerShell 脚本，`param(...)` 必须放在文件开头
- 如果脚本需要中文输出，必须注意文件编码和 stdout 编码

## 9. 后续扩展方向

后续这份规范预计还会补这些内容：

- 更多 agent 的导出目录约定
- skill 版本号和兼容性标记
- OpenAPI 形式的本机 API 描述
- 脚本扩展的调试协议
- Python 等更多运行时
