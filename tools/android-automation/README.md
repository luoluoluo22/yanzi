# 燕子 Android 自动操作底座 · P0/P1 第二轮

独立的本机 Node.js 模块，USB ADB + Android UI tree + 燕子已有 `ocr.recognize` (PaddleOCR)。本轮不改动宿主、云端服务或已安装的小程序；所有截图仅保存在本机临时路径，OCR 只发送至 `127.0.0.1` 上的燕子 Agent API，完成后删除临时文件。接口令牌从用户本机配置读取，不写入日志。

## 状态与验证边界

| 能力 | 状态与证据 |
| --- | --- |
| ADB 设备连接、前台检查、触摸/滑动/返回 | 已实现，Redmi K70 真机可用 |
| UI tree 优先 + PaddleOCR 回退 | 真机微信小程序可用（UI tree 仅暴露外层容器） |
| 屏幕分辨率覆盖修正 | 真机 1080×2400，与截图对应 |
| ADBKeyboard 中文输入、验证和恢复 | **真机通过**。切换 IME 后必须重新聚焦微信输入框，写入文字后在恢复原 IME 之前进行 OCR 核验 |
| 多多买菜搜索流程 | **真机通过**。从现有结果页再次搜索“正新烤肠”，再切换“白糖”，识别并加载商品卡片 |
| 商品卡片关联和重量单价 | **真机通过**。从同一个商品卡片提取商品文字、重量、价格、按钮；信息不完整会标记 `verified:false` |
| 购物车 OCR 条目/数量解析 | **模拟测试通过**，未在真实购物车完成结构化验收 |
| 修改前后购物车数量校验 | **模拟测试通过**；非目标条目变更、读数不确定、超时均不重试 |
| 自动加入、删除、替换商品 | 尚禁用，待商品卡与真实购物车数量验证闭环 |
| 下单、支付 | 完全禁用，待独立用户授权、金额和自提点验证、幂等机制 |

当前 **25 项单元测试通过**。真实购物场景仅执行了搜索、OCR 和比价，没有再次下单，也没有自动加购。

## 本机使用

```powershell
cd F:\Desktop\kaifa\OpenQuickHost\tools\android-automation
npm test
node src/cli.mjs status
node src/cli.mjs inspect
node src/cli.mjs locate 搜索
node src/cli.mjs search 正新烤肠
node src/cli.mjs products
node src/cli.mjs compare 正新 7
node src/cli.mjs search 白糖
node src/cli.mjs plan-cart '[{"name":"正新原味烤肠"}]'
```

这些命令用于受控调试，依赖手机 USB 授权及燕子本机 Agent API 在线。`inspect` 会输出可能包含私人内容的屏幕 OCR，不应写入公开日志。连接多台手机时，请设置 `ANDROID_SERIAL`。

## 设计要点

- **绝不根据固定位置猜测控件**：只能点击唯一可识别的文字/坐标，或通过商品行证据绑定操作按钮。OCR 重名或页面跳转不一致时停止。
- **中文输入必须验真**：ADBKeyboard 的广播成功只代表广播已发出；搜索框显示预期文字并进入结果页，才算成功。完成后恢复先前输入法。
- **价格可比较，不等于已结算**：优先明确 OCR 中的“券后价/秒杀价”，按每 500g 计算；超出视窗或存在多个无法辨认价格的卡片，标注未验证。
- **写入动作不能盲重试**：`runVerifiedCartAction` 仅允许一次回调；执行前读取可信快照，执行后比对目标数量，任何结果不确定时返回 `uncertain`，要求重新读取后人工/AI 判断。
- **交易能力隔离**：`addProduct/removeProduct/submitOrder` 在当前公开流程中主动拒绝执行。开发辅助的模拟事务校验不代表可以直接下单。

## 核心接口

| 组件 | 函数 |
| --- | --- |
| `AndroidDevice` | `ensureConnected/size/tap/swipe/back/openApp/uiTree/screenshot` |
| `ScreenVision` | `recognize/locate/tapText/waitFor/inspect` |
| `AdbChineseIme` | `type(text, {focus,verify,clearExisting})` |
| `AndroidFlows` | `openWechat/openMiniProgram/searchProducts/findProduct/compareProducts/openCart/readCart` |
| `products.mjs` | `extractProductCards/chooseProduct/yuanPer500g` |
| `cart.mjs` | `extractCartRows/verifyCartChange` |
| `transaction.mjs` | `runVerifiedCartAction`，默认不自动重试 |

## 下一步

1. 对真实购物车进行**只读**商品行/数量/实际应付价识别验收，特别处理多行标题、促销原价、空购物车、弹窗与滑动分页。
2. 绑定商品卡片与真实购物车同一 SKU 的身份，并在用户允许的测试购物车里完成一次增加→验证→撤销→验证。未验证的操作持续禁用。
3. 历史订单批量结构化采集、时间周期分析、价格历史和备货预算。
4. 最后再设计独立的订单审批、提交幂等及回执能力。对任何已经发出但结果不确定的真实交易，先查询订单状态，**不得立即重新提交**。

模块独立热更新，无须停止燕子宿主；Git 提交仅限 `tools/android-automation`。
