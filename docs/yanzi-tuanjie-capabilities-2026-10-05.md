# 燕子：团结引擎专有能力（2026-10-05）

## 已实现能力

- `tuanjie.status`
- `tuanjie.openProject`
- `tuanjie.batch.executeMethod`

## 本机版本

Editor：
`F:\备用软件\unity\2022.3.61t13\Editor\Tuanjie.exe`

- FileVersion：2022.3.61.15132323
- ProductVersion：2022.3.61t13_e6e6a3640b39

## 技术路线

团结引擎与 Unity 的 batchmode 机制兼容，因此共享同一套经过参数隔离的执行器，但能力命名空间独立，避免把两个实际软件混成一个。

支持：

- 打开现有项目
- batchmode / nographics
- executeMethod
- 独立日志
- 超时和进程树回收

项目必须包含 `ProjectSettings/ProjectVersion.txt`。

## 正式验收

建立独立测试项目：

`artifacts/tuanjie-capability-project`

静态方法：

`YanziTuanjieCapabilityTest.Run`

正式调用：

`tuanjie.batch.executeMethod`

结果：

- success=true
- exitCode=0
- 团结引擎真实加载并编译项目
- 写出 `yanzi-tuanjie-capability-ok.txt`
- 文件内容：`Tuanjie=2022.3.61t13`

因此团结引擎不是只复用 Unity 的理论接口，而是单独做过真实执行验收。
