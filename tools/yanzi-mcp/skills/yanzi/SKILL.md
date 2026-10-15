---
name: yanzi
description: Use Yanzi MCP for opening installed Yanzi miniapps, everyday Windows app and device operations, and explicitly requested WeChat File Transfer Assistant messages.
---

Start with yanzi_ping and discover with yanzi_catalog or the live MCP tools list. Tools and JSON schemas come directly from the running Yanzi capability catalog. Prefer these tools for Yanzi user workflows; use Raccoon for source editing, shell, build and Git development.

Open a miniapp with yanzi_extension_open using its exact ID or unambiguous name. Read status with yanzi_extension_status. A launch acknowledgement is not proof that a longer task finished; check its status/result.

yanzi_wechat_fileTransfer_sendText sends only to WeChat File Transfer Assistant. Do not claim support for arbitrary contacts. Send messages only when the human explicitly requests the message and destination. Check yanzi_wechat_status first. Never retry a send whose outcome is uncertain. Surface Yanzi's login/phone verification instructions to the user.

Honor the host capability's requiresConfirmation and existing tool approval policy. Never fabricate caller permissions, bypass authentication, or expose the local Agent API token. Do not read appsettings.local.json through tools; the bridge loads credentials privately.
