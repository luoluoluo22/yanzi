# Yanzi Browser Helper Privacy Policy

**Last updated: 20 October 2026**

Yanzi Browser Helper is a browser extension that connects a user's browser to the Yanzi desktop productivity app. It provides user-invoked browser workflows and packaged helpers for supported websites, including content filtering and optional diagnostics.

## Information the extension handles

Depending on the feature the user enables or the workflow the user requests, the extension may process:

- **Tab and page information:** tab identifiers, target URLs, page state, and page elements or text requested by a workflow. A workflow may read page content, fill specified fields, click specified controls, or return extracted results to the Yanzi desktop app.
- **Conversation or website content:** when a user invokes a workflow on a chat or content website, the requested result may include text from that page, including conversation text or other user-provided content.
- **Limited network diagnostics:** optional ChatGPT and Gemini diagnostics summarize request types, methods, status-code classes, counts, durations, and failures for extension-managed diagnostic sessions. The optional Lanzou observer is limited to supported Lanzou domains and records request metadata such as method, host, path, type, status, timestamp, non-sensitive form-field names, and a numeric task code when present. It does not intentionally read or store cookies, authorization headers, raw request bodies, or response bodies. Sensitive field names and share identifiers are filtered.
- **Local settings and troubleshooting information:** connection status, bounded troubleshooting logs, installed-helper toggles, and feature settings are stored in browser storage. Task logs may include a target URL, task action, and status.

The extension does not ask users for passwords and does not read or export browser cookies or authorization headers. Some user-requested same-site workflows may use the browser's existing signed-in session to perform a request on the user's behalf.

## How information is used and shared

The extension communicates with the Yanzi desktop service running on the same device to receive browser-workflow commands and return results. Page data returned by a workflow is provided to that local service so it can complete the requested task.

If a user enables Yanzi cloud synchronization, selected helper settings or feature data may be sent through the Yanzi desktop app to the configured Yanzi cloud service. The specific data depends on the enabled helper and sync settings. If a user chooses a workflow that sends content to an external website or AI provider, that provider processes the submitted content under its own terms and privacy policy.

The extension is not designed for advertising, cross-site ad tracking, or selling personal information. It does not intentionally transmit page content to an advertising or analytics network.

## Storage and retention

Connection and troubleshooting information is stored locally in browser storage; the extension limits its saved troubleshooting log to a recent bounded set of entries. Network-observer records are limited in number and scope. Local information remains until it is cleared through the browser or application, or the extension is removed. Data synchronized to a Yanzi cloud service is subject to the retention and deletion behavior of that service and the user's account settings.

## User choices

Users control when they invoke browser workflows and whether optional network diagnostics or cloud synchronization are enabled. Users can disable a helper, turn off synchronization, clear browser extension data, or uninstall the extension. Disabling a feature stops future processing by that feature but does not necessarily delete information already synchronized to a cloud service.

## Security and third-party services

The extension's executable scripts are included in the extension package; it does not download executable code from a remote server. The extension uses broad website access to support user-directed workflows across sites, but individual workflows should be limited to the page and data needed for the requested task. Users should avoid requesting extraction or synchronization of sensitive information unless they understand and intend that processing.

Third-party websites and services are governed by their own privacy policies. Yanzi cannot control their data practices.

## Contact and questions

For questions, privacy requests, or issue reports, please open an issue in the public Yanzi repository:

https://github.com/luoluoluo22/yanzi/issues
