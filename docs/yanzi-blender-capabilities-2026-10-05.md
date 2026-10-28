# 燕子：Blender 专有能力（2026-10-05）

## 已实现能力

- `blender.status`
- `blender.open`
- `blender.scene.inspect`
- `blender.renderFrame`
- `blender.python.run`

## 技术路线

本机 Blender：

`C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`

版本：Blender 5.2.1 LTS。

所有生产型能力优先使用 Blender 官方后台 CLI，不使用 GUI 坐标。

## 能力说明

### blender.scene.inspect

后台载入 .blend，通过 bpy 输出结构化 JSON：

- scene
- objects + type
- camera
- frameStart / frameEnd / frameCurrent
- renderEngine
- resolution

### blender.renderFrame

后台载入 .blend，设置明确 frame 和 outputPath，并执行：

`bpy.ops.render.render(write_still=True)`

结果以输出文件真实存在及字节数确认。

### blender.python.run

运行本地 .py，可选带入 .blend。

这是任意 Blender Python 执行能力，因此：

- riskLevel=high
- 权限包含 `code.execute`
- 只接受实际存在的本地 .py 文件

## 重要修复

初次真实验收发现：Blender 中 Python 抛异常时，进程本身可能仍以 exitCode=0 退出。

因此所有 Python/表达式型后台调用都加入：

`--python-exit-code 1`

从此 Python 异常会真正映射为能力失败，不会出现“脚本炸了但燕子报告成功”的假阳性。

另外，本机 Blender 5.2.1 的 Eevee 枚举实际为：

`BLENDER_EEVEE`

而不是一些版本使用的 `BLENDER_EEVEE_NEXT`。

## 正式验收

测试脚本通过正式 `blender.python.run` 创建：

- `artifacts/blender-capability-test.blend`：96053 bytes

`blender.scene.inspect` 返回：

- scene = YanziCapabilityTest
- objects = Cube(MESH), Light(LIGHT), Camera(CAMERA)
- camera = Camera
- frameStart = 1
- frameEnd = 250
- renderEngine = BLENDER_EEVEE
- resolution = 64 × 64

`blender.renderFrame` 输出：

- `artifacts/blender-capability-render.png`
- 3544 bytes
- rendered=true
- exitCode=0

桌面、Runtime、Runtime Verifier 构建通过；Yanzi.CapabilityVerification 65/65。
