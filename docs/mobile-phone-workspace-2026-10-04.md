# 手机工作区与内嵌应用商店 0.2.49

用户要求应用商店在手机 Tab 内加载、移除文档入口、将电脑及手机小程序统一移至手机 Tab，并清理重复电脑标签后发布。

## 实现

- 手机工作区保留小程序、应用商店、终端三个子页面。ApplicationCatalogView 是主 Activity 内的真实原生组件，复用目录/应用库/下载与系统安装确认；ApplicationCatalogActivity 仅保留兼容入口，手机导航不再启动它。
- 商店按需加载、保留搜索与分类状态；回到前台重算安装状态，重开系统安装授权时保留待安装包。切换账号清除旧目录并等待旧请求结束后载入新账号，原签名/版本/SHA256 验证保留。
- 统一目录 extensionsContainer 移入手机 pager，不复制一份电脑列表；原手机优先、电脑远程及长按选择运行位置的逻辑保留，新建手机脚本入口仍可用。
- 手机文档入口移除；电脑页面只保留聊天、文件和终端。内部旧索引 1 及旧小程序快捷入口转到手机统一目录，文件/终端索引映射经回归验证。电脑单运行时图标下重复“电脑”字样移除。
- 手机小程序下拉刷新统一目录；商店使用自己的刷新操作，避免外层下拉抢内部滚动。其他聊天、AI 和底部导航行为不改。

## 验证

- Dev、AndroidTest 构建成功；独立 managed worktree 正式 Release 构建与 lintVitalRelease 成功。
- API30 ApplicationStoreVerification：推荐横排、分类、组合搜索、无结果、账号应用库筛选通过。
- API30 与 OnePlus Dev PhoneWorkspaceVerification：主 Activity 内真实商店、统一小程序所属页面、移除文档、电脑 pager 三页、文件/终端映射、旧入口跳转均通过。测试等待页面布局完成后检查挂载，不将 ViewPager 初始未布局误判为失败。
- OnePlus 0.2.49-dev DesktopExecutionVerification：APP_ROUTE 与 CLOUD_ROUTE 日历实际执行均 completed；临时设备删除检测与后台禁止重新登记通过，临时登记已清理，现有登录保持。
- 一加仅覆盖 Dev；正式 0.2.48 的版本和 codePath 不变，不清用户数据，不操作 K70，不绕过系统安装确认。
- 正式 APK：cc.luoluoluo.yanzi.mobile / code49 / 0.2.49；证书 8a0ec0b84d1a05edcc89dd020bf81901f9ed7f083887db7c05201c59b31e1ee3，与历史生产签名一致。
- APK SHA256 f65b13c41a2742f7573ef0a5fd0aa834683759e1c1dd051877781b287d8bbeef。
- 全局 lintDev 的历史推送/其他界面错误仍存在；上次完整 lint 商店类没有 errors，本次 Release lintVital 已成功，未以 UI 修改处理无关业务。

## 发布路径

仅提交本次 Android 源码、测试、版本与本文档，主目录其他工作保留。使用 GitHub Release android-v0.2.49 和现有 Publish mobile and notes CI 发布公开 APK/更新清单；不进行本地 Worker 部署。

## 公开发布结果

GitHub android-v0.2.49 与公开更新清单均已发布；Publish mobile and notes 37185687290 全部成功。mobile-release-ci.py 的公开 APK 实际下载校验通过（PUBLIC_MOBILE_VERIFIED=0.2.49），GitHub 与公开 APK SHA256 均为 f65b13c41a2742f7573ef0a5fd0aa834683759e1c1dd051877781b287d8bbeef，大小 7,233,729 字节。发布源码提交 081f4b81de17287dd8860b0eb69fc48743581733。
