# Yanzi UI 0.7.4：Context Menu / Dialog / Drawer 深度交互

日期：2026-10-08。对照基准：https://ui.shadcn.com/docs/components

## 本轮实现

1. **Context Menu：互斥 Radio 与一级子菜单**。在 `YanziContextMenu` 中新增 `AddRadioGroup`，底层复用自绘 `YanziRadioGroup`，支持初始选项、组内互斥、回调；新增 `AddSubmenu`，二级操作显示在同一个 WPF Popup 中的右侧附加面板。支持父项鼠标悬停/点击展开、Right 箭头进入、Left 箭头返回、上下箭头在当前面板移动，以及点击子操作后关闭菜单。既没有引入 Windows 原生 `MenuItem`，也不会打开新系统窗口。
2. **Dialog：焦点约束和焦点恢复**。`YanziContentDialog` 卡片设为 `KeyboardNavigationMode.Cycle`，Tab / Shift+Tab 在模态卡片内循环；退出弹窗后只尝试把焦点还给同一个 owner 窗口里之前聚焦的元素，不跨窗口抢焦点。原有 `YanziDialog.Confirm` 保持兼容。
3. **Drawer：真实拖动和三档吸附**。`YanziSheetOverlay` 底部 Drawer 的 27 DIP 可抓取区支持鼠标和 WPF 触控 manipulation；向上拖动增加高度、向下拖动降低高度；松开后吸附到 40%、65%、90% 三档，低于可用高度的 22% 时关闭。新增供回归测试和外部编程调用的 `SnapDrawerTo` / `CompleteDrawerDrag`，非法档位被拒绝。评估页面板内部额外有 40/65/90% 快捷按钮，用于迅速确认响应。

## 已经验证

- Release 构建：0 错误、0 警告。
- 公共组件总检查：**252 项通过**。
- 64 个官网目录页面：**64/64** 实机访问通过；64 项都保留独立预览、官网参考入口、六维核对与备注保存。
- 旧 15 个专题页面：**102 项**自动化断言通过。
- Dialog Cancel / Save 和焦点循环、Context Menu Radio 互斥 / 同 Popup 二级操作、Drawer 三档吸附与低位关闭阈值均有专项检查。
- **桌面实机**：二级菜单可向右展开，“Copy link” 项在主菜单右侧可见；Drawer 65%、90%、40% 三档按钮使面板上沿分别处于不同高度且顺序正确，可关闭。
- Runtime：隔离构建 0 错误，已存在的宿主代码有 14 个警告；18 项隔离兼容测试通过，不安装或替换用户正式运行的 Runtime。
- 官网核查：64/64 参考链接返回 HTTP 200，名称及顺序均与本地注册表一致。

## 视觉参考截图

- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-context-submenu-v0.7.4.png`：菜单可见二级面板、Radio 组、快捷键、勾选项。
- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-context-menu-v0.7.3-open.png`：上一轮菜单基础形态。
- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-drawer-v0.7.3-open.png`：底部面板原始形态；本轮增加抓取与三档按钮。

## 尚未通过、不能冒充官网一致的事项

- Context Menu：多级嵌套子菜单、子菜单悬停关闭延迟、鼠标安全移动区域、靠窗口/屏幕边缘时翻转布局及详细无障碍菜单角色。
- Dialog：遮罩和动画的逐帧像素对照、焦点循环在复杂嵌套控件及第三方输入法下的实机验证、RTL。
- Drawer：鼠标高度吸附已实测，但**真实触屏设备上的手势与内部滚动冲突仍未验证**；速度阈值、惯性和吸附回弹动画还需开发。
- **全部 64 个组件的官网逐像素视觉和全状态正式通过数量仍为 0**。页面可操作、单元/回归测试通过不代表验收签收。

## 工程边界

本轮只修改公共 UI 及独立评估中心；本地提交、保留用户其他未提交文件，不推送远端，也不替换正式 Runtime。下一轮优先把窗口边缘的子菜单避让和复杂控件焦点循环做完整，然后在同样 DPI/缩放状态下进行截图差异标注。
