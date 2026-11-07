# 仓库文件放置约定

- 根目录保留 README、许可证、Agent 入口、解决方案及需从根目录发现的工具配置。
- 开发操作文档放在 `docs/development/`，参见[本地常用命令](local-commands.md)。命令默认从仓库根目录执行。
- README 和文档截图放在 `docs/assets/screenshots/`；独立部署网站需要的图片保留在 `website/assets/`。
- UI 验收记录放在 `docs/ui-unification/`，使用说明业务和日期的文件名，参见[任务浮窗视觉验收](../ui-unification/task-progress-design-qa-2026-10-07.md)。
- 开发、打包、验证脚本放在 `scripts/`。macOS 应用打包从根目录运行 `./scripts/create_app_bundle.sh`。
- 测试夹具随所属测试工具维护；源码、SDK、协议、第三方依赖和平台工程维持各自既有目录。
- 用户小程序源码及业务数据遵守[小程序工作流](../miniapps/DEVELOPMENT_WORKFLOW.md)，不因目录整理迁入宿主源码树。

移动文档或资源时同步更新 README、文档链接、构建脚本及材料生成脚本。不要仅根据文件名或日期移动仍有特定发现路径的配置、发布元数据或历史验收证据。
