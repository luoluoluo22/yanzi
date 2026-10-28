# 0.2.51 小程序分区与深色内嵌商店

用户澄清：手机小程序仅含本机运行方式，电脑小程序仍在电脑 Tab；商店需要深色，去掉内嵌重复标题和手机工作区说明标题。本版纠正 0.2.49 的目录整体迁移。

- 手机本机 mobile-js 网格与电脑小程序目录恢复分别挂载；电脑 pager 恢复聊天/小程序/文件/终端四页，旧小程序快捷入口仍指向电脑。手机文档入口继续移除，商店仍直接嵌入手机子 Tab。
- 电脑列表只展示 hasDesktopRuntime 的条目，手机单运行时条目排除；双运行时应用可在各自页面出现。电脑卡片点击明确调用远程执行，手机本机卡片按原手机脚本执行。电脑页不再展示冗余运行位置标签。
- 商店使用 YanziUiKit 深色背景、卡片、浅色文字与绿色操作；嵌入页从搜索/刷新开始，不重复应用商店标题；手机应用与工具说明标题去掉，商店分类/推荐/搜索/完整介绍与安装校验保留。

## 验证与发布

- Dev、AndroidTest、独立工作树 Release 与 lintVitalRelease 构建通过。
- API30 ApplicationStoreVerification 推荐、分类、搜索、无结果、应用库筛选通过。
- API30 与真实 OnePlus PhoneWorkspaceVerification 通过：两侧挂载归属、深色表面、没有重复标题/文档/说明文字、电脑四页切换、手机单运行时排除、双运行时包含、原列表和搜索恢复。分类夹具只渲染，不执行、不持久化。
- OnePlus 0.2.50-dev 实际日历 APP_ROUTE 与 CLOUD_ROUTE completed；测试临时设备登记已移除，原账号未改。生产 OnePlus 0.2.48 版本/codePath 不变，不清数据、不操作 K70。
- 正式 cc.luoluoluo.yanzi.mobile / code51 / 0.2.51，沿用历史生产证书 8a0ec0b84d1a05edcc89dd020bf81901f9ed7f083887db7c05201c59b31e1ee3。
- APK SHA256 2b7eb905c65d256fa9f95f5bc9f3426b4fa3b50ef7c69b300fd1a018170e3543。
- 使用 GitHub android-v0.2.51 与现有 Publish mobile and notes CI 更新公开 APK/更新清单，不手动部署 Worker；其他本地变更不进入本次提交。

- 真机额外检查发现原固定列宽裁切第四项，0.2.51 按可用宽度计算四列；最终真机网格视口边界回归通过，四个本机小程序完整呈现。0.2.50 已上传后不覆盖同版本 APK，使用更高版本修正。

公开结果：main f0772b37e0f6ca6fcd0fcd9c097ca0e2dc38c190；GitHub android-v0.2.51 与更新清单已发布，Publish mobile and notes 37186397863 success；实际公开 APK 下载校验 PUBLIC_MOBILE_VERIFIED=0.2.51。大小 7,233,725 字节，SHA256 与正式包/GitHub 完全一致。最终截图 %TEMP%/yanzi-store-dark-051.png、yanzi-local-programs-051.png、yanzi-desktop-programs-051.png。
