# 燕子 Android 通用操作底座（P0/P1 第一轮）

本地独立 Node.js 模块，不改动燕子主窗口、移动 APP 或生产服务。仅通过 USB ADB 控制本人授权的 Android 设备；复用燕子宿主的 `ocr.recognize` (PaddleOCR)，不另外安装识别引擎。截图作为本机临时文件传给 `127.0.0.1` 上的燕子 Agent API，完成后删除；API Token 不输出日志。

## 当前进度

| 能力 | 状态 |
| --- | --- |
| ADB 设备发现、串号限定、多设备防误操作 | 已实现、单测及真机验收 |
| 前台检测、启动微信、点击、滑动、返回 | 基础接口已实现 |
| 获取截图、自动删除临时文件 | 已实现 |
| Android UI tree 读取和解析 | 已实现；微信小程序实际不提供文字节点 |
| OCR 检索文字与坐标、重复候选拒绝点击 | 已实现、真机识别通过 |
| 分辨率覆盖修正 | 已实现，真机 1080×2400 与截图一致 |
| 微信小程序入口导航 | 已实现原型，尚未覆盖全部页面状态 |
| 商品搜索 | 原型；真实中文输入广播未能写入文本，阻止自动搜索 |
| 购物车读取 | 只读接口原型；尚缺跨页采集和商品行级绑定 |
| 自动增删/替换购物车商品 | 当前禁用，待前后数量核验 |
| 自动下单/支付 | 当前禁用，待单独授权与幂等交易设计 |

**真机结果：** 新模块已识别 Redmi K70 上微信小程序的 46 行 OCR 文本，屏幕尺寸读取与截图统一。Android 手机上已安装 ADBKeyboard，切换/恢复原输入法通过验证，但 `ADB_INPUT_TEXT` 广播没有实际写入搜索框；这不是成功的中文输入，不能跳过校验。

## 使用

```powershell
cd F:\Desktop\kaifa\OpenQuickHost\tools\android-automation
npm test
node src/cli.mjs status
node src/cli.mjs inspect
node src/cli.mjs locate 购物车
node src/cli.mjs open-wechat
node src/cli.mjs open-mini 多多买菜
node src/cli.mjs plan-cart '[{"name":"正新原味烤肠"}]'
```

多台手机时指定 `$env:ANDROID_SERIAL='你的设备序列号'`。`inspect` 会输出当前页面文字，可能包含私人信息，只在本人电脑本地运行，不贴到公开日志。需要燕子本机 Agent API 在线才能 OCR。

`search <中文词>` 当前仅用于受控调试，因微信小程序历史搜索会影响输入路径，尚未完成真机验收；搜索步骤若不满足屏幕条件会主动失败。**任何按钮点击的执行结果都需要后续屏幕状态核验，而不是把点击成功当作业务成功。**

## 复用接口

- `AndroidDevice.ensureConnected/foreground/size/openApp/tap/swipe/back/screenshot/uiTree`
- `ScreenVision.recognize/locate/tapText/waitFor/inspect`
- `AdbChineseIme.type`（广播请求已发送 != 文字确实输入）
- `AndroidFlows.openWechat/openMiniProgram/searchProducts/findProduct/openCart/readCart/prepareCart`

## 下一步

1. 解决中文输入：验证 ADBKeyboard 与小程序输入框的焦点/权限，或改用燕子 APP 的授权输入桥；完成真实中文搜索的端到端验收。
2. 识别商品卡片：将文字、规格、价格、库存、商品按钮绑定为一个商品对象，用购物车数量变化验证添加/删除。错误时停机并重读。
3. 历史订单分页采集与结构化保存、单位价格比较、预算约束、批量购物计划。
4. 购物车事务回执、订单授权与幂等提交；交易能力在独立审批之前保持关闭。

不覆写或重启正在开发的燕子宿主；当前所有自动化测试均为模拟页面、无真实商品交易。
