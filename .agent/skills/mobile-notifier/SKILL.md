---
name: mobile-notifier
description: Use this skill when the user asks to push a notification, send a message to their mobile phone, or communicate with the Android Yanzi app from the PC.
---

# Mobile Notifier

## Goal
To send a system notification directly to the user's Android phone via the local network Yanzi API, completely bypassing the cloud.

## Instructions
- Ensure the user's PC and Android phone are on the same local network and have successfully discovered each other.
- Formulate a JSON payload with `title` and `body` fields.
- Send an HTTP POST request to the local Yanzi PC Agent API at `http://127.0.0.1:53919/v1/notify`.
- **CRITICAL**: The request MUST include the header `Authorization: Bearer yanzi-local-dev-token`.
- **CRITICAL**: The payload MUST be sent as a strict UTF-8 encoded byte array. If using PowerShell, do not use a raw string for `-Body`, as PowerShell 5.1 will default to GBK/ISO-8859-1 which causes `???` gibberish on the phone. Convert the string to UTF-8 bytes first.

## Examples

### PowerShell Example
```powershell
$body = '{"title":"构建完成","body":"您的前端项目已经打包完毕，可以发布了！"}'
$bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
Invoke-RestMethod -Uri http://127.0.0.1:53919/v1/notify -Method Post -Headers @{"Authorization"="Bearer yanzi-local-dev-token"} -Body $bytes -ContentType "application/json; charset=utf-8"
```

### Python Example
```python
import requests
headers = {
    "Authorization": "Bearer yanzi-local-dev-token",
    "Content-Type": "application/json; charset=utf-8"
}
data = {"title": "构建完成", "body": "您的前端项目已经打包完毕，可以发布了！"}
# requests automatically handles dict to UTF-8 json serialization
requests.post("http://127.0.0.1:53919/v1/notify", headers=headers, json=data)
```

## Constraints
- Never omit the `Authorization` header.
- Never send text using local ANSI/GBK encodings.
- The phone must have the Yanzi App running and the local network permission enabled.
