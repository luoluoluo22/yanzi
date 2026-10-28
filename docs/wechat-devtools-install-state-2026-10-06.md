# 微信开发者工具状态（2026-10-06）

扫描发现开始菜单仍存在“微信开发者工具”快捷方式，但快捷方式 Target：

C:\Program Files (x86)\Tencent\微信web开发者工具\微信开发者工具.exe

当前已经不存在。

安装目录中仅发现卸载器，未发现主程序和 CLI。

结论：这是残留快捷方式/残留安装记录，不应被“应用已安装”判定为可运行实例。
后续应交给 KnownApplicationInstallerService / 应用安装能力处理：
1. 发现 shortcut target 不存在；
2. 标记 stale installation；
3. 提示或自动重新安装；
4. 重装后重新扫描 CLI；
5. 再注册微信开发者工具专有能力（打开项目、预览、上传等）。
