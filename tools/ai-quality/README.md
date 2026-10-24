# AI 使用质量：本地匿名基线采集

从历史归档分支 `ai-loop/ai-quality` 整合的独立只读脚本。它读取用户指定的 AI 话题 JSON 和宿主日志，只输出汇总计数、匿名消息长度与少量事件元信息，不会连接在线服务或更改配置。

## 使用

```powershell
powershell -NoProfile -File .\tools\ai-quality\collect-baseline.ps1 -TopicsPath <本机话题JSON路径> -ShellLogPath <本机shell.log路径>
```

输出至标准输出（JSON），不包含话题标题、对话正文、Token、模型凭证、URL 或源文件路径。只运行于用户授权的本机数据，**不要上传原始聊天记录或日志**。统计样本仅表示字符数和部分事件，不等于人工评价或可比较的质量得分。

## 自测

```powershell
powershell -NoProfile -File .\tools\ai-quality\collect-baseline.test.ps1
```

自测仅使用临时生成的虚构对话及日志，不依赖真实个人数据。