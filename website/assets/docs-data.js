window.YANZI_DOCS = {
  nav: [
    { group: "用户手册", items: [
      { path: "/docs/product-overview.html", title: "产品说明" },
      { path: "/docs/getting-started.html", title: "1. 下载与安装" },
      { path: "/docs/search-guide.html", title: "2. 燕子搜索" },
      { path: "/docs/yanh-guide.html", title: "3. 燕环与鼠标面板" },
      { path: "/docs/yanm-guide.html", title: "4. 燕幕小组件" },
      { path: "/docs/sync-guide.html", title: "5. 云同步配置" },
      { path: "/docs/extension-guide.html", title: "6. 扩展安装与管理" }
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
      description: "燕子是一款免费开源的 Windows 效率启动器，通过搜索、鼠标面板、桌面小组件和云同步帮你快速操作电脑。",
      sections: [
        { title: "燕子能帮你做什么", body: ["燕子由三个核心模块组成：燕子搜索（Alt+Space 全局唤出）、燕环/鼠标面板、燕幕（桌面常驻小组件），各司其职又互相配合。"] },
        { title: "核心模块", cards: [
          ["燕子搜索", "按 Alt+Space 唤出搜索栏，输入完整中文名称秒搜应用、文件、网站和扩展。"],
          ["燕环与鼠标面板", "长按鼠标右键弹出燕环轮盘，或使用直达网格的鼠标面板，一划即达。"],
          ["燕幕（桌面组件）", "常驻桌面的浮动小组件窗口，显示时钟、便签、系统监控等信息，随时可见。"]
        ]},
        { title: "扩展系统", body: ["万物皆扩展 —— 所有功能、程序、网站或自动化流程都可以变成独立扩展。支持从扩展商店安装、自建 JSON 和分享发布。"] },
        { title: "云同步与安全", body: ["支持 WebDAV（坚果云）和 GitHub 两种同步方式，数据存储在你自己的空间。完全开源、免费无广告、隐私优先。"] },
        { title: "移动端伴侣", body: ["Android 伴侣应用可接收桌面端推送通知，实现跨设备信息联动。"] }
      ]
    },
    "/docs/getting-started.html": {
      title: "1. 下载与安装",
      description: "介绍如何下载和安装燕子启动器，以及基础系统要求。",
      sections: [
        { title: "下载与安装", body: ["前往蓝奏云下载最新版 Windows 安装包，解压并双击安装。启动后常驻系统托盘。"] }
      ]
    },
    "/docs/search-guide.html": {
      title: "2. 燕子搜索",
      description: "教你如何使用燕子搜索，包括基础搜索规则、仅支持中文名搜索的说明以及键盘操作。",
      sections: [
        { title: "基础搜索规则", body: ["目前仅支持完整中文名搜索，不支持拼音简拼、首字母及英文缩写。"] },
        { title: "常用键盘快捷键", body: ["Alt+Space 唤出，Enter 执行，Esc 关闭，Ctrl+K 打开动作菜单。"] }
      ]
    },
    "/docs/yanh-guide.html": {
      title: "3. 燕环与鼠标面板",
      description: "清晰区分燕环快捷轮盘与网格鼠标面板，介绍添加项目和管理布局的方法。",
      sections: [
        { title: "燕环快捷轮盘", body: ["长按鼠标右键约 300ms 弹出圆形轮盘快捷菜单，一划即达高频操作。"] },
        { title: "鼠标快捷面板", body: ["网格化面板，可以在空白槽位直接点击添加项目，或从启动器右键点击搜索结果选择添加。"] }
      ]
    },
    "/docs/yanm-guide.html": {
      title: "4. 燕幕小组件",
      description: "常驻桌面浮动小组件的开启、类型 and 自定义布局方式说明。",
      sections: [
        { title: "开启小组件", body: ["从系统托盘图标右键菜单或设置中开启时钟、系统监控、便签和快捷入口。"] }
      ]
    },
    "/docs/sync-guide.html": {
      title: "5. 云同步配置",
      description: "如何配置 WebDAV（以坚果云为例）与 GitHub 私有仓库进行多端数据同步。",
      sections: [
        { title: "WebDAV 坚果云配置", body: ["输入第三方授权密码进行安全云端保存。"] },
        { title: "GitHub 备份配置", body: ["使用个人私有仓库及 Repo 权限 Token 进行备份同步。"] }
      ]
    },
    "/docs/extension-guide.html": {
      title: "6. 扩展安装与管理",
      description: "介绍如何从扩展商店、剪贴板安装扩展，以及快捷键速查和故障排查。",
      sections: [
        { title: "获取扩展", body: ["支持扩展商店一键安装，或将 manifest.json 复制到剪贴板后直接粘贴导入。"] }
      ]
    },
    "/docs/extension-authoring-guide.html": {
      title: "开发指南",
      description: "了解如何为燕子编写一个扩展，从 manifest.json 到动作执行。",
      sections: [
        { title: "扩展目录结构", body: ["包含 manifest.json、图标和必要资源文件。"] }
      ]
    },
    "/docs/extension-spec.html": {
      title: "扩展规范",
      description: "燕子扩展 manifest.json 的字段说明和约定。",
      sections: [
        { title: "基础字段", body: ["id、name、version、actions。"] }
      ]
    },
    "/docs/agent-skill-spec.html": {
      title: "Agent Skill 规范",
      description: "面向 AI Agent 的 Skill 定义方式，用于描述可调用能力。",
      sections: [
        { title: "Skill 结构", body: ["描述本地动作、文件处理、网络请求。"] }
      ]
    },
    "/docs/ai-extension-test-set.html": {
      title: "AI 扩展测试集",
      description: "用于测试 AI 生成扩展能力的一组典型任务。",
      sections: [
        { title: "测试目标", body: ["通过标准任务检查生成结果是否结构正确、可搜索、可执行。"] }
      ]
    }
  }
};
