# ChatGPT 图片生成与文件传递实测记录（2026-10-04）

## 结论

ChatGPT 后台工作台已经完成两条新能力的真实闭环验证：

1. 图片生成：`chatgpt_image`
2. 本机文件传递：`chatgpt_send + files[]`

## 图片生成

真实请求：

- action: `chatgpt_image`
- 非临时聊天
- 图片任务默认监听 900 秒
- 最大允许 1200 秒

真实页面曾显示生成进度 65%，随后完成一张白色燕子图片。

最终通过 `chatgpt_capture_images` 从原聊天补抓成功：

- 原始页面资源：`blob:https://chatgpt.com/...`
- 尺寸：1254 × 1254
- MIME：image/png
- 大小：1,166,530 bytes
- captureMethod：fetch
- 本地缓存文件：`bdb981e8-d84c-4ea5-abb8-3a3e5a33790d.png`
- 本地资源 URL：`http://127.0.0.1:53921/assets/bdb981e8-d84c-4ea5-abb8-3a3e5a33790d.png`

说明完整链路已经成立：

`prompt → ChatGPT 图片生成 → 页面生成图 → blob 抓取 → dataUrl → 53921 本地资产缓存 → localUrl`

图片识别规则已扩展为：

- 官方 `generated-image-preview/gallery` 标记；
- 助手回复中的大图；
- main 区域中尺寸明显属于生成内容的大图兜底；
- 排除用户消息图片和小头像。

## 文件传递协议

任务支持：

```json
{
  "action": "chatgpt_send",
  "prompt": "请读取我上传的文件",
  "files": [
    {
      "path": "<本机绝对路径>",
      "name": "example.png"
    }
  ]
}
```

也支持字符串简写：

```json
{
  "files": ["<本机绝对路径>"]
}
```

限制：

- 一次最多 4 个文件；
- 单文件最大 8MB；
- 总原始大小最大 12MB；
- WebSocket 现有 maxPayload 为 20MB；
- 支持常见图片、PDF、txt/md/json/csv、docx/xlsx/pptx；
- 其他类型按 `application/octet-stream` 传递，由 ChatGPT 页面决定是否接受。

服务端工作流：

`本机 path → 53921 读取文件 → base64 → WebSocket → 浏览器助手 → File/DataTransfer → ChatGPT input[type=file] → 等待上传稳定 → 发送 prompt`

任务结果新增：

`uploadedFiles[]`

其中记录：

- name
- mimeType
- byteLength

不会把本机路径注入 ChatGPT 页面。

## 真实上传验证

### PNG

上传刚刚生成并缓存的燕子 PNG：

- 文件名：`yanzi-image-test.png`
- MIME：image/png
- 大小：1,166,530 bytes

提示词要求 ChatGPT 只判断图片主体，不依据文件名猜测。

真实返回：

> 图片的主体是一只展翅飞翔的白色燕子。

任务状态：`success`

说明 ChatGPT 实际读取了图片内容。

### TXT

测试文件包含唯一验证码：

`YANZI-FILE-20261004-ALPHA`

提示词要求只返回文件中的验证码。

真实返回：

`YANZI-FILE-20261004-ALPHA`

任务状态：`success`

任务结束后工作台标签页按 `closeAfter=true` 自动关闭。

说明文件传递不是图片特例，普通文本文件链路同样成立。

## 后续方向

下一阶段可以直接基于这两条能力实现：

1. 小红书本地笔记先批量生成文字；
2. 对选中的笔记单独调用 `chatgpt_image`；
3. 返回 `images[].localUrl`；
4. 写回本地笔记 `imageUrl`；
5. 混排卡片直接显示图文；
6. 进一步支持“上传参考图 + 生成改图/衍生图”的工作流。
