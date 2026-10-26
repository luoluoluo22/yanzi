# 抖音屏蔽：最小开发背景

## 身份

- 名称：抖音屏蔽
- 稳定 ID：`douyin.filter`
- 目录版本：`0.2.9`
- 站点匹配：`https://www.douyin.com/*`
- 页面入口：`browser-extension/webapps/douyin-filter/content.js`
- 目录声明：`browser-extension/webapps/catalog.json` 中对应 ID 的条目。
- 同步键：`rules`、`settings`。

## 代码导航

- 页面脚本顶部定义 `APP_ID`、规则和设置键、站点选择器及默认规则。
- `extractMeta`、`extractLiveMeta` 负责提取当前视频/直播及商品相关信息；`findMatch`、`scopeEnabled` 负责匹配规则。
- `applyMatchHighlight` 与关键词范围生成逻辑负责高亮命中内容；跳过逻辑管理自动跳过和失败重试。
- 悬浮控制面板支持拖动、折叠，位置和折叠状态分别使用 `yanzi.douyin.filter.panel-position.v1` 与 `yanzi.douyin.filter.panel-collapsed.v1` 保存。
- 数据通过 `yanzi_webapp_storage_get`、`yanzi_webapp_storage_set` 与扩展运行时交互。

## 已确认的行为约束

- 自动跳过初始关闭（`filteringEnabled = false`），必须由用户明确开启后才开始自动跳过；不要为了“默认更智能”而改变这个安全默认值。
- 规则包含标题/标签/作者/商品等匹配范围，也会识别直播和广告标记。改动站点 DOM 选择器时，要考虑抖音页面结构会变化，并对缺失节点安全降级。
- 面板位置、折叠状态和规则/设置是不同类型的数据；不要把 UI 状态误写到云同步规则结构中。

## 验证

```powershell
node --check browser-extension/webapps/douyin-filter/content.js
node --check browser-extension/webapps/runtime-background.js
node -e "const c=JSON.parse(require('fs').readFileSync('browser-extension/webapps/catalog.json','utf8')); if(!c.apps.some(a=>a.id==='douyin.filter')) process.exit(1); console.log('catalog entry valid')"
git diff --check
```

涉及真实页面的修改，还应在抖音页面验证规则匹配、高亮、直播/商品/广告识别、自动跳过开关和重试逻辑。先检查当前 Git 状态并以实际代码为准，不要猜测未存在的函数或路径。
