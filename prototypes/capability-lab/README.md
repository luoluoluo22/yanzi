# 能力实验室

这是用于验证燕子“宿主基础能力 Provider”设计的桌面小程序。

## 为什么 manifest 不直接写 requires

普通业务小程序应该使用：

```json
"requires": ["python>=3.12", "node>=22", "ffmpeg>=7"]
```

宿主会在小程序启动前自动补齐依赖。

能力实验室的目标不同：它要把“检测 → 下载/安装 → PATH 刷新 → 版本验证”过程展示出来，因此故意不声明这些预启动依赖，而是以 `system.install` 权限调用：

- `dependency.status`
- `dependency.progress`
- `dependency.ensure`

## 实验

- Python：纯标准库生成 Mandelbrot PGM 图，不依赖 Pillow。
- Node.js：8 路并行、每路 64 轮 SHA-256 链，验证异步运行时与内置 crypto。
- npm：独立检查 npm CLI 版本；离线安装本地 file: 依赖，不触发生命周期脚本。
- npx：独立检查 npx CLI 版本，并用 --offline --no-install 执行刚安装的本地命令，校验输出；不从网络下载包。

Node 卡片会分别显示 npm/npx 版本与状态；测试目录运行后自动清理，仅保留 npm-npx-result.txt 验证摘要。便携式 Node 的子进程 PATH 会在实验内补上对应 Node 目录，不改系统环境变量。

- FFmpeg：用 lavfi 从零生成 2 秒测试图像 + 523.25Hz 音调视频，再截取缩略图。
- Git：建立临时仓库，将最近实验产物摘要写入“时间胶囊”并提交。

所有实验产物写入小程序自己的 ExtensionDataDirectory，不污染项目源码。
