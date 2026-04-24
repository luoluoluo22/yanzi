# Manifest Reference

Common fields:

```json
{
  "id": "my-extension",
  "name": "My Extension",
  "version": "0.1.0",
  "category": "扩展",
  "description": "What this extension does",
  "keywords": ["keyword-1", "keyword-2"],
  "globalShortcut": "Ctrl+Alt+T",
  "hotkeyBehavior": "show-view"
}
```

JSON extension example:

```json
{
  "id": "open-docs",
  "name": "打开文档",
  "openTarget": "F:\\Desktop\\docs\\README.txt"
}
```

Query command example:

```json
{
  "id": "google-search",
  "name": "谷歌",
  "queryPrefixes": ["谷歌", "google", "gg"],
  "queryTargetTemplate": "https://www.google.com/search?q={query}"
}
```

Script extension example:

```json
{
  "id": "script-clipboard",
  "name": "读取剪贴板",
  "runtime": "powershell",
  "entry": "main.ps1",
  "permissions": ["clipboard.read"]
}
```

Hosted script example:

```json
{
  "id": "sample-translate",
  "name": "双栏翻译",
  "runtime": "powershell",
  "entry": "main.ps1",
  "hostedView": {
    "type": "split-workbench",
    "title": "双栏翻译",
    "actionType": "script",
    "inputLabel": "原文",
    "outputLabel": "译文",
    "actionButtonText": "开始翻译"
  }
}
```
