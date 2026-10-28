# 燕子：WPS Office 专有能力（2026-10-05）

## 已实现能力

- `wps.status`
- `wps.open`
- `wps.writer.create`
- `wps.spreadsheet.create`
- `wps.presentation.create`
- `wps.exportPdf`

## 技术路线

本机 WPS 12.1.0.28505 注册了 Office 兼容 COM：

- Writer：`KWPS.Application`
- 表格：`KET.Application`
- 演示：`KWPP.Application`

因此不使用鼠标 UI 自动化，而直接使用 WPS COM 对象模型。

真实探测结果：

- KWPS.Application：可创建，Name=Microsoft Word，Version=12.0
- KET.Application：可创建，Name=Microsoft Excel，Version=12.0
- KWPP.Application：可创建，Name=Microsoft PowerPoint，Version=12.0

## 能力

### wps.writer.create

无界面创建 DOCX，可写入正文。
默认拒绝覆盖已有文件，显式 `overwrite=true` 才允许覆盖。

### wps.spreadsheet.create

无界面创建 XLSX。
支持：

- sheetName
- cells：使用 A1、B2 等地址赋值

JSON 字符串、数字、布尔值都会映射到 Value2。

### wps.presentation.create

无界面创建 PPTX，并生成标题页，可设置 title / subtitle。

### wps.exportPdf

按扩展名自动路由：

- DOC/DOCX/WPS → Writer COM
- XLS/XLSX/ET/CSV → Spreadsheet COM
- PPT/PPTX/DPS → Presentation COM

Writer 使用 ExportAsFixedFormat；表格使用 ExportAsFixedFormat；演示使用 PDF SaveAs 格式。

### wps.open

根据扩展名选择：

- wps.exe
- et.exe
- wpp.exe
- wpspdf.exe

而不是依赖 Windows 默认关联。

## 正式 Runtime 验收

2026-10-05 通过正式 Capability API：

- `wps.status`：Writer/Spreadsheet/Presentation 均为 true
- 创建 `formal-writer.docx`：10150 bytes
- 创建 `formal-sheet.xlsx`：9657 bytes
- 创建 `formal-presentation.pptx`：60349 bytes
- Writer 导出 `formal-writer.pdf`：50317 bytes

测试产物位于：

`artifacts/wps-capability-api/`

桌面、Runtime、Runtime Verifier 构建通过；Yanzi.CapabilityVerification 65/65。
