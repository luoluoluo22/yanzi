# 燕子 Yanzi 1.0.10（2026-10-06）

## 发布一致性修复

v1.0.9 打包过程中，浏览器扩展源码在“桌面 payload 已生成”之后、Git 提交之前从：
- 浏览器助手 0.5.33 → 0.5.34
- 小红书网页小程序 0.7.6 → 0.7.7

因此 v1.0.9 的 tag 源码比安装包内实际 payload 超前一个小版本。

1.0.10 不引入新的业务逻辑，重新以已经稳定的 0.5.34 / 0.7.7 源码生成完整桌面 payload，确保：

源码 = Git tag = 安装包

## 验证
- browser-extension/manifest.json：0.5.34
- webapps/catalog.json：xiaohongshu.filter 0.7.7
- content.js 内嵌版本：0.7.7
- 小红书 content/background/runtime-background 通过 node --check
- git diff --check 通过
- v1.0.9 工程 CI 已通过；本次只做版本与打包一致性修复
