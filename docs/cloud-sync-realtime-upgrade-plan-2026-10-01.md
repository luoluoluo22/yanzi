# 燕子云同步实时化升级方案 2026-10-01

## 背景

当前燕子云同步已经具备：

- 用户身份认证
- Object Sync 数据同步
- revision 增量同步
- 历史版本恢复
- Windows / Android 双端同步基础能力

当前同步模式以客户端主动拉取为主，可以保证最终一致性，但实时通知能力不足。

## 本阶段目标

在不破坏现有 Object Sync 架构的情况下，引入实时事件层。

目标：

1. 提升手机与电脑状态同步实时性
2. 增加 Android 通知权限和系统通知能力
3. 完善双向设备在线状态确认
4. 为未来电脑控制、智能家居联动提供事件基础

## 当前架构

```
Windows 燕子
      |
      v
Cloudflare Worker
      |
      v
D1 Object Sync
      |
      v
Android 燕子
```

特点：

- 数据可靠
- 支持离线恢复
- revision 可追踪

不足：

- 事件通知存在轮询延迟
- 无法主动唤醒客户端

## 升级架构

```
                 Cloudflare

        Object Sync      Event Layer
             |                |
             |                |
        数据一致性       实时消息通知
             |                |
             +-------+--------+
                     |
              Windows / Android
```

## 开发阶段

### Phase 1 基础验证

- 检查 Android 通知权限流程
- 验证手机通知显示
- 验证 Windows -> 手机状态同步
- 验证 手机 -> Windows 状态同步
- 记录实际延迟

### Phase 2 设备状态模型

统一设备状态：

```json
{
  "deviceId": "xxx",
  "platform": "windows/android",
  "online": true,
  "lastSeenAt": "utc",
  "lastAction": "sync"
}
```

### Phase 3 实时事件层

增加：

- 设备事件
- 操作请求
- 执行结果回传

例如：

手机请求关机：

```
Android
  |
 event
  |
Cloud
  |
Windows
  |
执行
  |
result event
  |
Android通知
```

## 验收标准

- 手机修改状态，电脑可确认
- 电脑修改状态，手机可确认
- 通知权限正常申请
- 通知消息正常显示
- 在线状态时间准确
- 不影响现有同步可靠性

## 注意事项

实时事件层只负责通知和指令，不替代 Object Sync。

Object Sync 负责最终一致性。

Event Layer 负责实时性。
