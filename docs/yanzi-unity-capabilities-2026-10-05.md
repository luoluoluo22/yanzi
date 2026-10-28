# 燕子：Unity 专有能力（2026-10-05）

## 已实现能力

- `unity.status`
- `unity.openProject`
- `unity.batch.executeMethod`

## 本机版本

Editor：
`F:\备用软件\2022.3.62f3c1-x86_64\Editor\Unity.exe`

- FileVersion：2022.3.62.1451004
- ProductVersion：2022.3.62f3c1_1623fc0bbb97

## 技术路线

Unity 的高价值能力直接使用官方命令行：

- `-projectPath`
- `-batchmode`
- `-nographics`
- `-executeMethod`
- `-logFile`
- `-quit`

不使用 GUI 坐标。

项目路径必须实际存在：

`ProjectSettings/ProjectVersion.txt`

否则拒绝执行。

`executeMethod` 只允许合法 C# 静态方法完整名，不把用户输入拼入 shell。

## 风险

`unity.batch.executeMethod` 可以执行项目中的 Editor 代码，因此：

- riskLevel=high
- 权限包含 code.execute
- 有明确 timeout
- 超时后杀掉进程树
- 每次执行有独立日志路径

## 正式验收

建立最小独立测试项目：

`artifacts/unity-capability-project`

项目内静态方法：

`YanziCapabilityTest.Run`

通过正式 Runtime API：

`unity.batch.executeMethod`

实际结果：

- success=true
- exitCode=0
- Unity 真正加载/编译项目
- 方法写出 `yanzi-unity-capability-ok.txt`
- 文件内容：`Unity=2022.3.62f3c1`

这证明能力不是“CLI 能启动”，而是 Unity 项目代码真实被执行。

桌面、Runtime、Runtime Verifier 构建通过；Yanzi.CapabilityVerification 65/65。
