# 燕子 Yanzi 1.0.9（2026-10-06）

## 浏览器助手
- 浏览器助手版本：0.5.33。

## 小红书网页小程序
- xiaohongshu.filter：0.7.6。
- “兴趣主题”面板扩展为“兴趣与历史”。
- 新增 AI 历史笔记列表。
- 支持按“全部 / 感兴趣 / 不感兴趣 / 未反馈”筛选。
- 展示主题、生成时间、展示状态以及局部文字反馈统计。
- 保持现有兴趣主题设置与下一轮 AI 补货逻辑不变。

## 验证
- manifest / catalog / content script 版本一致。
- 新增历史 UI 对应 CSS 与 DOM 均存在。
- content.js、background.js、runtime-background.js 通过 node --check。
- git diff --check 通过。
