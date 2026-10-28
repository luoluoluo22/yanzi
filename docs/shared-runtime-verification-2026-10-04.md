# 共享 Runtime 验证（2026-10-04）

Release Runtime、Release 稳定界面、Debug 开发界面均构建成功。主宿主构建有 22 个警告，0 个错误；未发布安装包。

隔离进程集成验证：17 项通过，覆盖界面退出/重启、能力执行所在进程、小程序实例保留、本地 API、并发/无效协议请求、临时编辑器试运行、无效测试目录拒绝和跨进程取消。

模式验证：Debug Dev、Release Stable、Release --dev 均通过。

实际生产数据运行验证：Runtime PID 20848，稳定与开发界面均连接。两个界面全部退出后，Runtime 进程与 instanceId 不变；yanzi-capture 与 clipboard-history 的运行 instanceId 均不变；API、调度、手机消息后台任务继续运行。已重新启动两个界面。

现有数据目录和小程序保持原位。测试未读取剪贴板历史内容，也未触发截图。手机验证检查后台任务存活，未模拟手机端收发或验证云端网络连通性。

当前位置记录：%LocalAppData%\YanziRuntime\runtime.json；日常使用 scripts/dev-desktop-loop.ps1。Runtime 本身更新需要 scripts/install-shared-runtime.ps1 -Activate。
