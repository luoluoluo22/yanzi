# 燕子 OCR 通用能力

日期：2026-10-07

## 目标

OCR 是燕子的基础视觉能力，不绑定微信、截图或某一个小程序。调用方只负责提供图片或图片区域，OCR Provider 统一负责模型安装、推理、坐标和生命周期。

正式引擎：

- 推理核心：`Sdcb.SimdPaddleOCR 1.4.2`
- 模型：`PP-OCRv6 Small`
- 运行方式：纯 C# / .NET，本地离线推理
- 模型策略：按需下载，燕子安装包不内置模型
- 模型来源：NuGet 上的 `Sdcb.SimdPaddleOCR.Models.ChineseV6Small 1.0.0` 与 `Sdcb.SimdPaddleOCR.Models.TextLineOrientation 1.0.0`

## 生命周期

首次调用 `ocr.ensure` 或任意识别能力时：

1. 检查模型缓存。
2. 当前进程首次使用已有缓存时执行 SHA-256 校验。
3. 缺失或校验失败时，从 NuGet flat-container 下载模型包。
4. 只解出模型 DLL 到燕子数据目录，临时 nupkg 随后删除。
5. `ocr.ensure` 只安装模型，不加载推理引擎。

首次识别时才加载推理引擎。连续 5 分钟无 OCR 请求时自动释放推理引擎；也可显式调用 `ocr.unload`。模型磁盘缓存不会因此删除。

数据目录遵循当前 Runtime Profile：

```text
<Yanzi Data Root>/Models/ocr/ppocr-v6-small/
```

例如正式版通常位于：

```text
%LocalAppData%/OpenQuickHost/Models/ocr/ppocr-v6-small/
```

## 注册能力

### ocr.status

返回：

- 模型是否已安装：`installed`
- 推理引擎是否已加载：`loaded`
- 模型缓存目录
- 空闲卸载时间
- 最近使用时间

### ocr.ensure

确保模型存在并通过校验。

重要语义：

- 模型已经有效时：`downloaded=false`
- 首次安装或修复时：`downloaded=true`
- 调用完成后仍应保持 `loaded=false`，除非之前已有识别任务加载了引擎

### ocr.recognize

输入：

```json
{
  "imagePath": "C:\\path\\image.png",
  "includeLines": true
}
```

输出包括：

- `text`：完整识别文本
- `detectedCount`
- 原图尺寸
- 实际识别区域
- `elapsedMs`
- 每行文字
- 每行四点 polygon
- 每行外接 box
- `classificationConfidence`
- `recognitionScore`

注意：当前 Sdcb.SimdPaddleOCR 1.4.2 的 `RecognitionScore` 在实测中不是稳定的 0~1 概率，因此燕子明确以 `recognitionScoreScale: "engine-native"` 暴露，不能把它直接当百分比置信度使用。后续若升级运行时并确认其分数语义，再单独增加归一化 confidence 字段。

### ocr.recognizeRegion

输入：

```json
{
  "imagePath": "C:\\path\\image.png",
  "x": 20,
  "y": 20,
  "width": 650,
  "height": 60,
  "includeLines": true
}
```

只识别指定矩形。返回的 box / polygon 已自动换算回原始图片坐标系，因此上层能力无需二次平移。

### ocr.unload

释放当前推理引擎。模型缓存保留，下一次识别自动重新加载。

## 微信接入原则

微信 Provider 不应该自己维护另一套 OCR。后续微信监听链路应复用统一能力：

```text
微信窗口
  ↓ 截图 / 局部截图
ocr.recognizeRegion
  ↓ text + polygon + box
微信布局解析器
  ↓ 会话列表 / 联系人 / 消息气泡
wechat.message.received 等事件
```

OCR 只负责“图上哪里有什么文字”。“这个文字属于谁、是不是新消息、是不是会话列表”属于微信结构解析层。

## 2026-10-07 实机验收

测试机：Intel Core i5-12400F / Windows 10 / .NET 9。

已完成：

- Debug 构建通过。
- `capability.list` 可发现 5 个 `ocr.*` 能力。
- 全空数据目录首次 `ocr.ensure` 实际下载约 27.1 MB nupkg，模型解包后约 32 MB，并通过 SHA-256 校验。
- 第二次 `ocr.ensure` 不重复下载。
- 固定中文基准图识别文本完全正确。
- `ocr.recognizeRegion` 可只识别目标行，且坐标保持在原图坐标系。
- 当前新版微信 690×707 实际窗口截图识别到 58 个文字区域，并识别出“文件传输助手”及本次自生成测试消息前缀。
- `ocr.unload` 后 `loaded=false`、`installed=true`。
- 卸载后再次识别可自动重新加载并成功返回。

## 许可

`Sdcb.SimdPaddleOCR` 项目代码采用 Apache-2.0；PP-OCRv6 / TextLineOrientation 模型来自 PaddleOCR 生态。发布时应保留对应第三方许可与归属说明。
