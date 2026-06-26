window.YANZI_DOCS = {
  nav: [
    { group: "用户手册", items: [
      { path: "/docs/product-overview.html", title: "产品说明" },
      { path: "/docs/getting-started.html", title: "快速上手" }
    ]},
    { group: "开发者文档", items: [
      { path: "/docs/extension-authoring-guide.html", title: "开发指南" },
      { path: "/docs/extension-spec.html", title: "扩展规范" },
      { path: "/docs/agent-skill-spec.html", title: "Agent Skill 规范" },
      { path: "/docs/ai-extension-test-set.html", title: "AI 扩展测试集" }
    ]}
  ],
  pages: {
    "/docs/product-overview.html": {
      title: "产品说明",
      description: "燕子是一款免费开源的 Windows 效率启动器，支持全局搜索、鼠标快捷面板、扩展商店和个人配置同步。",
      sections: [
        { title: "燕子的核心设计理念", body: ["燕子的核心方向是“万物皆扩展”。常用软件、网页入口、命令动作和自动化流程，都可以被整理为独立扩展，并通过搜索或鼠标面板快速执行。"] },
        { title: "主要能力", cards: [
          ["全局搜索", "快速查找应用、文件、网站和扩展。"],
          ["鼠标快捷面板", "把高频操作放在鼠标附近，减少重复点击。"],
          ["扩展系统", "把工作流模块化，便于安装、卸载、分享和升级。"],
          ["云端同步", "支持个人 WebDAV 配置，在多设备之间同步设置。"]
        ]},
        { title: "开源与安全", body: ["项目源码公开，方便审计和二次开发。个人数据默认保存在本地，云同步由用户自行配置。"] }
      ]
    },
    "/docs/getting-started.html": {
      title: "快速上手",
      description: "从下载、安装到常用快捷键，帮助你快速开始使用燕子启动器。",
      sections: [
        { title: "下载与安装", body: ["前往蓝奏云下载 Windows 版本安装包，提取码为 62yn。安装完成后，按照提示启动燕子即可。"] },
        { title: "常用快捷键", cards: [
          ["唤出启动器", "Alt + Space"],
          ["搜索应用或扩展", "直接输入关键词或拼音首字母"],
          ["执行选中项", "Enter"],
          ["鼠标快捷面板", "长按鼠标右键"]
        ]},
        { title: "云同步", body: ["你可以在设置里配置 WebDAV，同步扩展、快捷面板与个人配置。建议使用自己的网盘空间保存数据。"] }
      ]
    },
    "/docs/extension-authoring-guide.html": {
      title: "开发指南",
      description: "了解如何为燕子编写一个扩展，从 manifest.json 到动作执行。",
      sections: [
        { title: "扩展目录结构", body: ["一个扩展通常包含 manifest.json、图标和必要资源文件。燕子会读取 manifest 中的名称、入口、关键词和执行方式。"] },
        { title: "创建第一个扩展", body: ["先从简单动作开始，确认扩展可以被搜索和执行，再逐步增加参数、图标和复杂逻辑。"] },
        { title: "调试建议", body: ["保持扩展 ID 稳定，字段命名清晰。出现错误时先检查 manifest 是否为合法 JSON。"] }
      ]
    },
    "/docs/extension-spec.html": {
      title: "扩展规范",
      description: "燕子扩展 manifest.json 的字段说明和约定。",
      sections: [
        { title: "基础字段", body: ["id 是扩展唯一标识，name 是显示名称，version 是版本号，actions 是可执行动作列表。"] },
        { title: "动作定义", body: ["每个动作可以包含标题、描述、关键词、命令或 URL。用户搜索到动作后，可以直接执行。"] },
        { title: "兼容性建议", body: ["扩展 ID 保持稳定；不确定的能力应通过版本号或 capabilities 声明。"] }
      ]
    },
    "/docs/agent-skill-spec.html": {
      title: "Agent Skill 规范",
      description: "面向 AI Agent 的 Skill 定义方式，用于描述可调用能力。",
      sections: [
        { title: "Skill 是什么", body: ["Skill 是给自动化流程调用的能力描述，可以对应本地动作、文件处理、网络请求或系统能力。"] },
        { title: "推荐字段", body: ["name 表示能力名称，description 表示能力说明，input_schema 表示输入参数结构，executor 表示执行方式。"] },
        { title: "安全边界", body: ["涉及文件、网络或系统动作的 Skill 应明确提示权限，避免隐式执行高风险操作。"] }
      ]
    },
    "/docs/ai-extension-test-set.html": {
      title: "AI 扩展测试集",
      description: "用于测试 AI 生成扩展能力的一组典型任务。",
      sections: [
        { title: "测试目标", body: ["通过标准任务检查生成结果是否结构正确、可搜索、可执行、可维护。"] },
        { title: "示例任务", cards: [
          ["网站入口", "生成一个打开常用网站的扩展。"],
          ["文件处理", "生成一个调用 PowerShell 的文件处理扩展。"],
          ["参数输入", "生成一个带参数输入的搜索扩展。"],
          ["网络查询", "生成一个调用 HTTP API 的查询扩展。"]
        ]},
        { title: "验收标准", body: ["manifest 可解析、动作可展示、命令可执行、错误信息清晰、没有危险默认行为。"] }
      ]
    }
  }
};
