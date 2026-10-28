# 燕子：Visual Studio Code 专有能力（2026-10-05）

## 已实现能力

- `vscode.status`
- `vscode.open`
- `vscode.diff`
- `vscode.extensions.list`
- `vscode.extensions.install`

## 关键实现

本机 VS Code 是 User Setup，主程序：

`C:\Users\Administrator\AppData\Local\Programs\Microsoft VS Code\Code.exe`

不能直接把 `Code.exe --list-extensions` 当 CLI 调用，否则 Electron 会进入 GUI 主进程。

本机 `code.cmd` 实际做法是：

1. 设置 `ELECTRON_RUN_AS_NODE=1`
2. 调用 `Code.exe`
3. 第一个参数指向当前版本目录下的 `resources/app/out/cli.js`

燕子 Provider 会动态扫描当前版本哈希目录，不把版本号写死，因此 VS Code 更新后仍可重新发现真实 CLI。

## 能力说明

### vscode.status

返回：

- 是否安装
- 是否正在运行
- Code.exe 路径
- cli.js 路径
- VS Code 版本
- commit
- architecture

### vscode.open

支持：

- 文件
- 文件夹
- workspace
- 跳到指定 line / column
- reuse-window
- new-window

所有输入路径先做存在性校验，不把任意字符串交给 shell。

### vscode.diff

使用官方 `--diff <left> <right>`，两个文件都必须实际存在。

### vscode.extensions.list

使用：

`--list-extensions --show-versions`

返回结构化：

`[{ id, version }]`

### vscode.extensions.install

使用官方：

`--install-extension <id-or-vsix>`

支持 `force`。

这是会修改 VS Code 扩展状态的能力，因此标为 medium risk，并要求 application.run + network.write 权限。

## 真实验收

2026-10-05：

- VS Code 版本：1.109.4
- 架构：x64
- 已安装扩展：17
- `vscode.open` 成功打开 `F:\Desktop\kaifa\OpenQuickHost`
- `vscode.diff` 成功打开两份测试文本比较
- 所有 CLI 调用 exitCode = 0

未为了测试而安装额外扩展，避免污染开发环境。

桌面 build、Runtime、Runtime Verifier 均通过；Yanzi.CapabilityVerification 65/65。
