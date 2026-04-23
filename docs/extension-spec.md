# 扩展规范

当前版本先支持单文件 JSON 扩展。每个扩展存放在独立目录下，最小结构如下：

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
