# Microsoft Edge Add-ons 发布：Description 校验阻塞记录（2026-10-20）

## 状态

- 当前提交：Yanzi Browser Helper 0.5.62
- Partner Center 当前状态：**In review**
- 发布审核尚未完成；不要在本次审核进行时为了追查历史错误而取消提交。
- 本记录是事后复盘。原始故障当时没有保存 HAR、DevTools Console、Network 请求或带具体字段标记的完整截图，所以**根因未能被证实**。

## 用户可见的错误

Partner Center 的英文商店详情页显示：

> Description field(s) require your attention. Please update the field(s).

同一页面右上角的 Save draft 为灰色。页面上的扩展名称、长描述和正方形 Logo 可见，但当时没有成功捕获指出具体无效字段的红色叉号/字段级验证提示。

之后 Partner Center 的 Extension overview 显示版本 0.5.62、状态 **In review**，并显示 Packages、Availability、Properties、Privacy、Store listings 等步骤均为绿色完成。因此本次提交最终已成功进入审核，但历史错误的具体触发原因仍未知。

## 已核实事实

1. 原包版本 0.5.61 上传后，微软显示 package verified。
2. 随后创建了 0.5.62 英文名称包，将 manifest 的 name 改为 `Yanzi Browser Helper`，description 改为英文短描述。检查时该 manifest description 约 90 个字符，未配置 `default_locale`，也未包含 `_locales` 目录。
3. Partner Center 的商店详情长描述是独立填写的字段；当时可见文本超过 250 字符，但页面仍显示泛化的 Description 错误横幅。
4. 在一次测试中，曾尝试使用键盘注入修改描述，最后可见文本末尾出现了意外字符 `x`。这不是已证实的根因，也不能解释最初错误的全部过程。
5. 后来 overview 显示 0.5.62 为 In review，说明本次提交的必要步骤最终通过了 Partner Center 的前置校验。

## 根因判断

**已确认：** 当时 Partner Center 将问题归类到 Description 字段组。

**尚未确认：** 具体是描述内容、未保存的表单状态、描述中的 URL 可达性、页面端校验状态未刷新，还是 Partner Center 自身问题。不能仅凭现有证据断言是中文扩展名造成的。

微软官方文档说明：
- manifest 的 `name` 和 `description` 会填充商店详情中的对应字段；Short description 也与 manifest/package 有关。
- 商店长描述每个语言必填，长度要求为 250–10,000 字符，Logo 每个语言必填。
- 详情页显示 “Fields require your attention” 时，应向下滚动并找到带叉号的具体字段。
- 2026 年 1 月的 Partner Center 更新说明：描述中的不可访问 URL 可能触发验证错误；成功保存后 Save draft 也会变灰，因此按钮颜色本身不能证明校验失败。

官方参考：
- 发布扩展与字段要求：https://learn.microsoft.com/en-us/microsoft-edge/extensions/publish/publish-extension
- Partner Center 更新记录：https://learn.microsoft.com/en-us/microsoft-edge/extensions/release-notes/
- Edge 扩展开发者政策：https://learn.microsoft.com/en-us/legal/microsoft-edge/extensions/developer-policies

## 下次复现时的诊断步骤

1. 记录提交版本、ZIP 的 SHA-256、manifest 原文、语言清单及上传时间。
2. 遇到错误横幅时，不要立刻重写字段或刷新页面；先截取完整页面，向下滚动定位所有带叉号的字段，并保存每个错误提示。
3. 检查描述字段中的 URL 是否存在，以及该 URL 是否可从公开网络访问；同时检查隐私政策 URL 和 Support URL（如填写）。
4. 若错误仍无法定位，先保存可复现步骤与截图，再采集浏览器 Console/Network 记录或 HAR。导出前必须移除 Cookie、Authorization、访问令牌和其他个人信息。
5. 每次只修改一个可疑因素，重新保存并观察状态，避免同时改名称、manifest、描述和图片后无法确定真正原因。
6. Save draft 变灰时，先检查成功保存提示与页面步骤状态；不要仅凭灰色按钮判断失败。
7. 提交进入 In review 后，保留版本和状态截图，不要取消正在进行的审核来追查已消失的前置校验错误。

## 复盘结论

本次问题最终绕过/消失并进入审核，但**不能宣称根因已解决或已查明**。后续应将“捕获字段级错误、保存可复现证据、一次只改一个变量”作为发布流程的固定步骤。
