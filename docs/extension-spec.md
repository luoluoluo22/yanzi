# 扩展规范

当前版本支持两类扩展：

- 单文件 JSON 扩展
- 目录脚本扩展

每个扩展存放在独立目录下，最小结构如下：

```text
Extensions/
  my-extension/
    manifest.json
```

## 最小示例

```json
{
  "id": "my-json-extension",
  "name": "我的 JSON 扩展",
  "version": "0.1.0",
  "category": "扩展",
  "description": "示例：打开本地文档或目录。",
  "keywords": ["json", "extension"],
  "openTarget": "F:\\Desktop\\OpenQuickHost\\docs\\README.txt"
}
```

## 脚本扩展

如果你把燕子当作脚本管理器来用，可以让扩展直接执行脚本。

当前第一版脚本运行时支持：

- `powershell`

目录结构示例：

```text
Extensions/
  foreground-window/
    manifest.json
    main.ps1
```

示例：

```json
{
  "id": "foreground-window",
  "name": "前台窗口信息",
  "version": "0.1.0",
  "category": "脚本",
  "description": "读取当前前台窗口标题和进程。",
  "keywords": ["window", "foreground", "前台窗口"],
  "runtime": "powershell",
  "entry": "main.ps1",
  "permissions": ["window.foreground"],
  "globalShortcut": "Ctrl+Alt+W"
}
```

脚本入口会收到这些参数和环境变量：

- 参数 `-InputText`
- 参数 `-ContextPath`
- 环境变量 `YANZI_INPUT`
- 环境变量 `YANZI_CONTEXT_PATH`
- 环境变量 `YANZI_EXTENSION_ID`
- 环境变量 `YANZI_EXTENSION_DIR`
- 环境变量 `YANZI_LAUNCH_SOURCE`

`ContextPath` 指向一个临时 JSON 文件，里面包含：

- `extensionId`
- `title`
- `extensionDirectory`
- `inputText`
- `launchSource`
- `now`
- `permissions`

## 宿主视图扩展

当扩展不仅要“执行一次动作”，还需要在启动器内部承载一个交互界面时，可以声明 `hostedView`。

当前版本先支持一种宿主视图：

- `split-workbench`
  启动后在当前窗口切换为双栏工作区
  左侧输入
  右侧输出

示例：

```json
{
  "id": "sample-translate",
  "name": "双栏翻译",
  "version": "0.1.0",
  "category": "扩展",
  "description": "在当前窗口中打开翻译工作区。",
  "keywords": ["翻译", "translate", "translator"],
  "globalShortcut": "Ctrl+Alt+T",
  "hostedView": {
    "type": "split-workbench",
    "title": "双栏翻译",
    "description": "左侧输入原文，右侧显示插件输出。",
    "inputLabel": "原文",
    "inputPlaceholder": "输入要处理的文本...",
    "outputLabel": "译文",
    "actionButtonText": "开始翻译",
    "actionType": "mock-translate",
    "emptyState": "这里会显示插件结果。"
  }
}
```

## 字段说明

### `id`

- 类型：`string`
- 必填
- 扩展唯一标识
- 建议使用短横线命名，例如 `google-search`

### `name`

- 类型：`string`
- 必填
- 启动器展示名称

### `version`

- 类型：`string`
- 选填
- 默认 `0.1.0`

### `category`

- 类型：`string`
- 选填
- 启动器展示分类，例如 `扩展`、`搜索`、`目录`

### `description`

- 类型：`string`
- 选填
- 条目说明文字

### `keywords`

- 类型：`string[]`
- 选填
- 用于搜索命中
- 建议同时写中文、英文、拼音缩写

### `openTarget`

- 类型：`string`
- 选填
- 执行目标
- 可用于：
  - 本地文件
  - 本地目录
  - URL
  - 系统协议，例如 `ms-settings:`

### `hostedView`

- 类型：`object`
- 选填
- 用于声明插件自己的宿主界面
- 如果存在，用户执行该扩展时，启动器不会关闭，而是在当前窗口切换到插件视图

当前支持字段：

- `type`
  当前支持 `split-workbench`
- `title`
  视图标题
- `description`
  视图说明
- `inputLabel`
  左侧输入区标题
- `inputPlaceholder`
  左侧输入区占位文案
- `outputLabel`
  右侧输出区标题
- `actionButtonText`
  右下角动作按钮文字
- `actionType`
  宿主内置执行器类型
- `outputTemplate`
  当 `actionType = template` 时，用 `{input}` 作为替换占位符
- `emptyState`
  输入为空时显示的默认说明

当前宿主内置的 `actionType`：

- `mock-translate`
- `template`
- `uppercase`
- `reverse`
- `script`

当 `actionType = script` 时，宿主会执行当前扩展的脚本入口，并把标准输出显示在右侧结果区。

### `globalShortcut`

- 类型：`string`
- 选填
- 为扩展注册系统级全局快捷键
- 当前支持组合键形式：
  - `Ctrl+Alt+T`
  - `Ctrl+Shift+1`
  - `Alt+Space`

规则：

- 至少包含一个修饰键：`Ctrl / Alt / Shift / Win`
- 最后一个片段必须是主键
- 不建议和系统保留快捷键冲突

行为：

- 如果扩展声明了 `hostedView`，触发快捷键后会直接打开对应插件界面
- 如果扩展是普通动作型扩展，触发快捷键后会直接执行，不拉起启动器

### `runtime`

- 类型：`string`
- 选填
- 当前支持：`powershell`

### `entry`

- 类型：`string`
- 选填
- 脚本入口文件，相对于扩展目录
- 例如：`main.ps1`

### `permissions`

- 类型：`string[]`
- 选填
- 用于声明脚本可能使用的系统能力
- 当前不会阻止执行，只用于描述和后续分享展示
- 示例：
  - `clipboard.read`
  - `window.foreground`
  - `filesystem`
  - `network`

### `hotkeyBehavior`

- 类型：`string`
- 选填
- 用于覆盖快捷键触发时的默认行为

当前保留值：

- `show-view`
  强制在快捷键触发时先显示宿主界面

## 参数化命令

当前宿主已经支持“命令前缀 + 参数”的执行模型。

例如：

```text
谷歌 今天的新闻
```

含义是：

- `谷歌` 命中某个参数化命令
- `今天的新闻` 作为剩余参数
- 最终拼接到命令模板中执行

后续建议扩展规范支持下面两个字段：

```json
{
  "queryPrefixes": ["谷歌", "guge", "gg", "google"],
  "queryTargetTemplate": "https://www.google.com/search?q={query}"
}
```

这样扩展作者就能自己定义：

- 中文前缀
- 拼音前缀
- 英文缩写
- 参数执行模板

## 云同步行为

当用户在客户端中新增或修改扩展后，宿主会：

1. 写入本地扩展目录
2. 加载到命令列表
3. 在已登录状态下自动同步到云端

云端当前存储：

- 扩展元数据
- 当前用户安装记录
- 扩展包归档

## 兼容建议

- `id` 一旦发布后尽量不要修改
- `name` 可以修改
- `keywords` 里同时保留中文和拼音别名
- 路径类 `openTarget` 尽量指向稳定位置
