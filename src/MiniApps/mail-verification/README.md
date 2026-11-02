# 验证码 · mail-verification

燕子原生 C# 小程序，支持 QQ 邮箱和 Gmail 多账户绑定、加密凭证、并行检索以及最近 60 分钟内最新验证码筛选。

## 入口和部署

- `manifest.json`：小程序入口及版本信息。
- `mail.cs`：窗体、邮箱账户管理、IMAP 读取、验证码识别。
- 安装到燕子宿主的 `%LOCALAPPDATA%\OpenQuickHost\Extensions\mail-verification\`，由宿主加载和编译，**不要**把真实授权码、debug.log 或本地加密凭证文件提交到 Git。
- 多账户密文使用宿主 `AppEnvironmentVariableStore` 的 `YANZI_MAIL_ACCOUNTS_V2` 字段。宿主 v1.0.89 起将该字段纳入 `ProviderOnlySecrets`，避免发布到 Windows 用户环境变量。
- 保留旧版 QQ/Gmail 单账户密钥的读取迁移，但删除账户后以新列表为准。

## 公共 UI

所有主窗口、账户管理、编辑及帮助窗口调用 `YanziUi.ApplyTo`，使用 `Yanzi.UI.Wpf` 的按钮、表单标签、选择器、卡片、列表和危险操作确认弹窗，避免独立维护 WPF 样式。对动态邮箱列表，使用公共 `Yanzi.Select` 样式兼容可变选项。

## 验证

1. 编译 `mail.cs`，确认对共享 `Yanzi.UI.Wpf` 的引用可用。
2. 用模拟账户覆盖旧配置迁移、新增、删除、重复账号及最新验证码排序。
3. 在正式宿主重新呼出，确认 QQ/Gmail 真实连接与设置输入、滚动、复制功能。
4. 确认敏感值仅在加密宿主保存，未出现于普通系统环境变量或日志。

邮件到达自动推送 / IMAP IDLE 后台监听尚未包含在本版本中。
