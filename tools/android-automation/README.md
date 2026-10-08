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
