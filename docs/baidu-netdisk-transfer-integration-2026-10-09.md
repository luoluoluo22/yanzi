# 百度网盘桌面传输闭环与燕子能力

日期：2026-10-09

## 结果与边界

- 通过燕子已有能力 `baiduNetdisk.upload` 调用 Windows 官方 Shell “上传到百度网盘”操作，成功将 `AI-baidu-desktop-roundtrip-20261009.txt`（107 bytes）交付给已登录百度网盘 8.8.3.101。原能力只承诺 `triggered_unconfirmed`。
- 百度官方客户端“传输 → 上传 → 已完成”独立显示该测试文件；本机 `upload.db` 中 `upload_history_file` 有相同本地路径、云端路径、107 bytes、`error_code=0`、上传开始/结束时间；对应上传活动表没有残留。
- 通过百度官方客户端云端首页找到测试文件，点击下载。下载历史 `transmission.db.download_history_file` 的云端路径与上传匹配，大小 107 bytes、`error_code=0`、有结束时间。下载文件已存在于默认下载目录 `F:\Backup\Downloads`，与原文件的 SHA-256 完全一致。
- 以上**证实客户端完成记录及实际上传下载回环**。它不代表查询了实时云端对象 API；因此能力输出始终包含 `liveCloudExistenceVerified=false`。
- 访问方式不导出 Cookies/Token，SQLite 用只读 `mode=ro` 打开，不写客户端状态。
- 上传/下载表在应用数据的账户子目录下；不能直接假设存在根目录。多个匹配账户目录拒绝自动选择。

## 燕子统一能力

此前已有：
- `baiduNetdisk.status`、`baiduNetdisk.open`、`baiduNetdisk.upload`。

本轮新增：
- `baiduNetdisk.transferStatus`：只读。输入 `{"kind":"upload","path":"完整本地文件路径"}`，或 `kind=download`；可加 `afterSeconds` 排除旧历史。输出 `found`、`completed`、`cloudPath`、`size`、`errorCode`、时间戳及证据来源。
- `baiduNetdisk.roundtripVerify`：只读。输入 `{"originalLocalFile":"原始本地文件","downloadedFile":"下载文件"}`，在本地两个历史记录间核对同一云端路径、相同文件大小、`error_code=0` 与两个文件内容 SHA-256。

源文件：
- `src/OpenQuickHost/YanziBaiduTransferCapabilityProvider.cs`
- `src/OpenQuickHost/CapabilityScripts/BaiduNetdisk/baidu_transfer_index.py`
- `src/OpenQuickHost/CapabilityScripts/BaiduNetdisk/baidu_capability_host.py`
- `src/Yanzi.CapabilityVerification/BaiduTransferCapabilityVerification.cs`
- `tests/BaiduNetdisk/test_transfer_index.py`

部署：`OpenQuickHost.csproj` 将桥接 Python 随宿主构建及发布。桥接使用 `python -I` 与固定本地脚本，JSON 入参和 JSON 结果，不提供通用 Shell。

## 验收

```powershell
python -m unittest discover -s tests\BaiduNetdisk -p test_transfer_index.py -v
dotnet build src\Yanzi.CapabilityVerification\Yanzi.CapabilityVerification.csproj --no-restore -c Debug -p:SkipStopRunningApp=true -p:OutputPath=F:\Desktop\cloud-drive-eval-20261009\yanzi-baidu-verifier
F:\Desktop\cloud-drive-eval-20261009\yanzi-baidu-verifier\Yanzi.CapabilityVerification.exe --baidu-transfer
```

- Python 测试 8/8 通过（包含真实传输历史、SHA-256、拒绝错误码、历史过期、多账户冲突）。
- 统一宿主调用 19/19 通过（注册、读取实际记录、跨记录校验、权限拒绝）。
- 首次宿主编译 **0 error**，测试资料仅匹配自己创建的测试文件。
- 当前运行中的燕子曾通过 `yanzi_catalog` 实际列出上述两项能力且标注 `available=true`；后续仍需继续保证 Git 与发布包持久化。

## 后续

1. 增加组合 `uploadVerified`（发起 Shell 上传后监听本机真实 FINISH 记录，严格限制超时与防重复）。
2. 继续研究百度官方客户端文件列表、路径定位、精确下载操作。不能将“提交下载”当作“下载成功”。
3. 下载默认由百度客户端设置决定；未经用户授权不得全量扫描和搬移私人网盘文件。
