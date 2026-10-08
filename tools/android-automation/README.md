# v0.3.2 · 真实历史订单列表与商品明细验收（2026-10-08）

**已实际完成：**
- Redmi K70 多多买菜真实“订单列表 → 全部”识别通过；点击并验证“展开一周前的订单”后，`pdd.orders.collect` 只读翻页采集 8 个屏幕页面，覆盖 2026-10 至 2026-01。采集 24 条订单候选记录，**包含跨屏未识别完整的候选，不能当作 24 笔唯一订单或完整账单**。同一天多订单（2026-08-20 的 3 件 ¥27.95 和 21 件 ¥124.37）得到区分。
- 改善先用后付金额语义：“当前实付 ¥0”不等于订单总金额；有明确未来扣款行时分别返回 `paidShown` 和 `deferredPaymentDue`，并使用实际商品金额。OCR 将小数点漏读成不可信的大整数时标为未验证，绝不直接将疑似“¥3724”当成真实交易总额。
- 新增 `src/order-detail.mjs`：对已打开的“订单详情”进行只读明细采集，必要时只点击“展开”，自动向下滚动，抽取每个商品的规格、名称、实付单价与数量，并对跨屏残缺标题做安全合并。**联系人、电话、提货地址、二维码均不出现在结构化输出中；订单编号只输出哈希。**
- 2026-02-21 真实订单：3 屏采集 **10/10 件**，每项数量和价格已验证，程序汇总 **¥52.39**，与订单页面 **¥52.39** 一致，且检出订单 ID 哈希，返回 `complete:true`。
- 新增 `pdd.orders.detail` 只读能力声明，已和 `pdd.orders.collect` 一起纳入 v0.3.2 的 6 项能力契约。**运行中的燕子扩展提供者仍存在旧能力名称占用问题；这些新增 C# 能力尚未证明已经在当前宿主成功注册。** 不应重启整个燕子来隐式绕过问题。
- 本地 Node 代码 **56 项测试全部通过**，语法检查通过。未加购物车、未下单、未确认提货、未付款、未删除订单。

## 开发调试

```powershell
cd F:\Desktop\kaifa\OpenQuickHost\tools\android-automation
npm test
# 手机停留在“全部订单”页面：
node src/capability-cli.mjs pdd.orders.collect eyJtYXhQYWdlcyI6OH0=
# 手机停留在某一条“订单详情”页面：
node src/cli.mjs order-detail
# 正式扩展更新（会先备份扩展目录，运行中的 C# 提供者不会因此被安全重载）
./install-extension.ps1 -Force
```

`complete:true` 对明细需要：订单编号的哈希、声明件数与商品数量一致、所有商品数量/实付价可读、累计金额与订单实付金额一致；否则返回 `complete:false`。对历史列表没有全量证据时始终不得声称数据完整。禁用任何实际交易/付款命令。

**后续优先级：** 修复燕子扩展能力的热重载名称冲突，使新增只读接口正式可调用；之后做带状态回退的“订单列表→指定订单详情→返回列表”多单采集，并支持用户许可后的本地结构化历史数据保存（不在 Git 仓库存储个人订单）。

---

# v0.3.1 · 订单采集与燕子调用链修复（2026-10-08）

- **商品搜索从燕子能力入口已实测通过**：修正 Node 子进程向 C# 返回 JSON 的换行及 Windows 编码兼容问题，`pdd.product.search` 返回 4 款正新相关烤肠，无需重启宿主。
- 新增只读采集 `pdd.orders.collect`（`maxPages:1..8`；可选 `openIfNeeded:true`）。需要已进入**可验证的订单列表**；如果在首页、页面未跳转或 OCR 不确定则停止。默认最多扫描 5 页，页面不动/达到上限立即结束。
- 避免将日期、价格、数量相同的不同订单误合并：优先订单号判定身份，没有订单号则使用完整可见文本哈希，且不能声称全量采集。页面未显示明确结束和唯一订单身份时返回 `complete:false`。
- 新 `pdd.orders.collect` 的 C# 能力声明已打包安装，但 **当前宿主仍运行旧版 C# 提供者，其能力可能需下次受控重新加载才会出现在目录**。独立 Node 命令已就绪，不能宣称新能力已正式联通。
- 订单入口真机点击仍未进入列表；保留为导航阻塞。尚未真实读取订单列表和商品明细。
- 全量 Node 自动化测试 46/46 通过。购物车加购/交易能力未新开放；不下单、不付款。


**本次运行态限制：** 尝试打开新版扩展时，连接器报告能力名称冲突（`yanzi_android_device_status`）。本机能力目录仍为原来的 4 项；新增加的 `pdd.orders.collect` 未被当前运行态注册。禁止靠重复启动或全局重启解决；先排查扩展能力租约及规范化名称冲突。现有 4 项不受此次文件更新影响。

本机调用（在 `tools/android-automation` 目录）：

```powershell
# 必须先确认手机上是多多买菜订单列表
$payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('{"maxPages":5}'))
node src/capability-cli.mjs pdd.orders.collect $payload
```

**调用说明：** `recognized:true` 表示识别到订单列表，`pagesScanned` 是实际扫描页数；`complete:false` 表示尚不能证明历史订单采集完整。未经用户明确要求不要上传订单原始画面或完整编号。待订单页面真机验收后再开发历史订单逐项商品明细读取。

---

# 燕子 Android 购物能力 · v0.3.0（增量进度）

这次新增 **独立燕子扩展**，不修改或重启宿主核心程序。安装在
`%LOCALAPPDATA%\OpenQuickHost\Extensions\yanzi-android-automation`，后端调用本目录
`src/capability-cli.mjs`，代码、声明和安装脚本随仓库保存，不依赖固定的个人电脑路径。

### 已验证的燕子能力调用

| 能力 | 安全范围 | 实际验证 |
|---|---|---|
| `android.device.status` | 只读 | 燕子本机 Agent API 调用成功，返回已连接 Android 状态 |
| `pdd.cart.inspect` | 只读 UI 操作；不加购 | 能力入口调用成功；不是购物车页面时返回 `recognized:false` |
| `pdd.orders.preview` | 只读当前可见订单页 | 能力入口调用成功；不是订单列表时返回 `recognized:false`；不声称采集所有订单 |
| `pdd.product.search` | 搜索/读价格；不加购 | **模块单独执行成功，燕子能力入口仍存在 C# 旧版包装层解析错误，待宿主安全重载后复测** |

能力目录确认上述 4 个提供者均是 `yanzi-android-automation`。没有注册任何 `pay`、`submitOrder` 或 `checkout` 功能。C# 包装层按单线程顺序使用手机，同一时刻不并发执行多个 UI 任务。

`orders.mjs` 已新增保守的订单页 OCR 结构化解析（日期、件数、实付、状态）。目前仅通过模拟 OCR 单元测试；当前手机不在订单列表时正确拒绝推断。**完整分页采集与订单商品明细仍未验收，绝不能从当前屏幕推断所有历史订单。**

### 安装或更新

```powershell
cd F:\Desktop\kaifa\OpenQuickHost\tools\android-automation
npm test
./install-extension.ps1
# 已安装且已停止扩展后：
./install-extension.ps1 -Force
```

扩展运行入口 `extension/provider.cs` 使用现有 `YanziActionContext.Capabilities.Register`；Node 脚本从扩展相对路径调用，不暴露任意 ADB shell。扩展的 `startup.mode` 是 `on_app_launch`，启动后向能力网络注册上述四个入口。当前燕子 `extension.status` 对这个后台脚本的 `isRunning` 回报仍为 false，因此应以 **能力目录以及实际能力调用** 验证，而不是仅根据状态字段判定。更新运行中的 C# 包装器需要可控地重新加载扩展；本轮为避免打断其他任务，未重启燕子主程序。

若调用时报错，先在本地读取扩展目录的 `startup-error.log` 或 `capability-error.log`，不要把日志内容上传至公开环境。已经出现一次旧版 Schema 的 `maxLength` 不兼容，已在仓库声明中修复。

### 剩余工作

1. 完成 C# 运行时新版本的安全加载与 `pdd.product.search` 从燕子端到端调用验收。
2. 在订单真实页面测试 `pdd.orders.preview`，随后扩展为受控翻页采集和完整订单 SKU 明细。
3. 加入操作审计和设备/应用焦点并发锁，完成购物车多商品跨页验证。
4. 最后才为购物交易设计每笔明确授权和幂等机制；现在的扩展没有下单权限。

---

# 燕子 Android 自动操作底座（0.2.0）

本模块是可独立运行的 Windows → USB ADB → Android 微信小程序自动化库。底层复用燕子的本机 PaddleOCR（`ocr.recognize`）；UI 树能够识别时优先使用 UI 树，不另行重复安装 OCR 引擎。目标是把“截图→定位→点击→核对”封装成可以多次复用的操作闭环。

**注意：代码库中的能力与已注册在燕子宿主的能力不同。** 当前模块位于 `tools/android-automation/`，可从 Node CLI 或同一进程的 Node API 调用；尚未注册为正式的燕子小程序或云端工具。修改均隔离于其他燕子源码，避免影响正在运行的主程序。

## 这轮实际完成的能力

| 功能 | 验收情况 |
| --- | --- |
| USB ADB 连接、设备串号和屏幕尺寸 | 真机通过；有多设备时禁止默认随意选择 |
| 微信状态检测、截图、点击、滑动 | 真机通过，故障时返回可识别错误 |
| PaddleOCR 中文 UI 元素定位 | 真机通过；匹配不唯一时拒绝猜测 |
| ADBKeyboard 中文搜索 | 真机通过；切换输入法后重新聚焦、验证文字，再恢复输入法 |
| 搜索“正新烤肠”“白糖”并读取商品 | 真机通过；读取品牌/名称/规格/促销价及按钮 |
| 比价（每 500g 价格） | 真机通过；价格信息不完整时跳过该候选 |
| 购物车结构化快照 | 真机通过，支持购物车有货商品和折叠下架商品计数 |
| 自动加购 → 验证 → 删除 → 验证 | **真机通过**，以宸欢白砂糖 468g、¥5.99 做 1 份临时测试 |
| 删除确认弹窗处理 | 真机通过，确认前核实文案，避免点“全部删除” |
| 商家原有下架商品保护 | 真机两次循环后仍为 2 件，测试商品数量恢复为 0 |
| 更换商品事务 `replaceCartItem` | 逻辑与模拟测试通过，尚未真机换两种不同商品验收 |
| 真实下单/支付 | **禁用**，没有向任何用户订单重复付款或提交 |

## 核心流程

`PddCartAutomation.testCycle()` 一次调用依次执行：

1. 进入购物车，OCR 验证完整可见商品、数量、价格和下架商品数量。
2. 确认测试商品原先不存在，关闭购物车。
3. 在已显示的搜索结果中匹配唯一 SKU、规格、价格和“加入购物车”按钮。
4. 点击一次，再打开购物车，验证目标数量由 0 变 1，其他商品完全未变。
5. 根据该商品右侧数量控件所在行确定减号位置；只在识别到“确认删除该商品吗？”的弹窗时确认。
6. 再读购物车，验证目标数量 1 变 0，原有商品和下架商品数量与基线一致。
7. 任何执行结果不确定时返回 `uncertain`，**绝不立即重新点击、重复加购、重复提交**。

`replaceCartItem({from,to,maxPrice,approved:true})` 使用“先加新商品并验证，再删旧商品并验证”的顺序，避免提前删除旧商品。该函数假定页面已经打开新商品的搜索结果。如果删除阶段不确定，返回 `partial`，保留现状等待核对，不在不确定状态下回滚或重试。

## 命令行（本机）

```powershell
cd F:\Desktop\kaifa\OpenQuickHost\tools\android-automation
npm test
node src/cli.mjs status
node src/cli.mjs inspect
node src/cli.mjs search 正新烤肠
node src/cli.mjs products
node src/cli.mjs compare 正新 7
node src/cli.mjs cart-inspect
# 有权限、确定购物车中没有该测试商品并且搜索结果显示同 SKU 时：
node src/cli.mjs cart-cycle-test --ack-test-mutation
```

`cart-inspect` 会打开购物车浮层，但不改变商品数据。最后一条是**真实购物车改动测试**，必须显式传入标记；它会尝试临时添加一份测试白糖并删除，不用于生产购物或支付。不能在不清楚当前手机页面的情况下执行。多个设备连接时先设置 `ANDROID_SERIAL`。

## Node API

```js
import {PddCartAutomation} from './src/pdd-cart.mjs';
const pdd=new PddCartAutomation(device,vision);

// 已位于新商品搜索结果页，并取得本次交易明确授权
const result=await pdd.replaceCartItem({
  from:{name:'安井原味火山石烤肠',weightG:700},
  to:{name:'正新原味脆皮爆汁烤肠',weightG:500},
  maxPrice:7,
  approved:true
});
if(result.status!=='confirmed') {
  // 查询当前购物车并人工复核，不直接重试
}
```

只有 `confirmed` 代表完整核验通过。商品价格以**当时的 UI 显示**为准，券后或秒杀价格不代表最终结算价格；真正交易必须另外经过金额、自提点和支付方式审批。

## 测试结果与风险边界

2026-10-08：本地单元测试 **33/33 通过**。两轮真机加购—移除流程已恢复现场购物车，最终正常购物车 0 件、原有下架商品 2 件；未再次购买或付款。真机的一次自动化操作因同时识别到“0元下单”和“先用后付”而返回 `uncertain`，没有重复加购；修正入口筛选后复核并移除，之后整轮自动化完成。

尚需支持：跨页购物车采集、弹窗/页面布局变更适配、购物车多个同名不同规格 SKU、输入焦点冲突、更多手机机型验收、商品价格时效和库存校验、购物车逐笔交易审计。正式下单能力保持禁用。

来源代码及测试参见 `src/pdd-cart.mjs`、`src/products.mjs`、`src/cart.mjs`、`src/transaction.mjs` 和 `test/`。不保存用户个人购物历史或商品界面的整张截图，不输出本地 Agent API Token。
