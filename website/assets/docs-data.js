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
      description: "燕子是一款免费开源的 Windows 效率启动器，通过搜索、鼠标面板、桌面小组件和 AI 助手帮你快速操作电脑。",
      sections: [
        { title: "燕子能帮你做什么", body: ["燕子由四个核心模块组成：燕子搜索（Alt+Space 全局唤出）、燕环（鼠标快捷面板）、燕幕（桌面常驻小组件）、燕语（AI 智能助手），各司其职又互相配合。"] },
        { title: "核心模块", cards: [
          ["燕子搜索", "按 Alt+Space 唤出搜索栏，输入关键词或拼音首字母秒搜应用、文件、网站和扩展。"],
          ["燕环（鼠标面板）", "长按鼠标右键弹出网格快捷面板，把高频操作放在光标旁边，一划即达。"],
          ["燕幕（桌面组件）", "常驻桌面的浮动小组件窗口，显示时钟、便签、系统监控等信息，随时可见。"],
          ["燕语（AI 助手）", "用自然语言描述需求，AI 自动帮你生成专属扩展，无需编写代码。"]
        ]},
        { title: "扩展系统", body: ["万物皆扩展 —— 所有功能、程序、网站或自动化流程都可以变成独立扩展。支持从扩展商店安装、自建 JSON、AI 生成和分享发布。"] },
        { title: "云同步与安全", body: ["支持 WebDAV（坚果云）和 GitHub 两种同步方式，数据存储在你自己的空间。完全开源、免费无广告、隐私优先。"] },
        { title: "移动端伴侣", body: ["Android 伴侣应用可接收桌面端推送通知，实现跨设备信息联动。"] }
      ]
    },
    "/docs/getting-started.html": {
      title: "快速上手",
      description: "从下载安装到上手全部核心功能，10 分钟完成新手入门。",
      sections: [
        { title: "下载与安装", body: ["前往蓝奏云下载 Windows 安装包（提取码 62yn），双击安装后燕子会在系统托盘启动。支持 Win10/11 64位，无需额外运行时。"] },
        { title: "燕子搜索（启动器）", body: ["按 Alt+Space 唤出搜索栏，输入中文、拼音首字母或英文搜索。支持参数化搜索（如 谷歌 关键词）和 @别名 范围搜索。"] },
        { title: "常用快捷键", cards: [
          ["唤出/隐藏启动器", "Alt + Space"],
          ["执行选中项", "Enter"],
          ["上下切换", "Up / Down"],
          ["关闭启动器", "Esc"],
          ["动作菜单", "Ctrl + K"],
          ["唤出燕环", "长按鼠标右键"]
        ]},
        { title: "燕环（鼠标快捷面板）", body: ["长按鼠标右键约 300ms 弹出网格面板。通过搜索结果右键菜单添加项目，支持分组管理和拖拽排列。"] },
        { title: "燕幕（桌面小组件）", body: ["从系统托盘或设置中开启。支持时钟、系统监控、便签、快捷入口等小组件，可拖拽定位和调整大小。"] },
        { title: "燕语（AI 助手）", body: ["用自然语言描述需求，AI 自动生成扩展配置。例如说"帮我做一个打开百度的扩展"，AI 会生成完整的 manifest.json。"] },
        { title: "云同步", body: ["支持 WebDAV（推荐坚果云）和 GitHub 两种方式同步扩展、面板布局和个人配置。在设置页面填写对应的服务器地址和凭据即可启用。"] },
        { title: "安装扩展", body: ["从扩展商店一键安装，或将 manifest.json 复制到剪贴板后直接粘贴导入。通过 Ctrl+K 动作菜单管理已安装的扩展。"] }
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
