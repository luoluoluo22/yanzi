# Host integration

The job agent uses the browser extension's declarative fetch workflow step to call a site's own authenticated APIs while reusing the user's active browser session.

Security constraints:
- no account password storage;
- no cookie extraction or persistence;
- only current-site HTTPS subdomains are allowed;
- every remote resume write is followed by a read-back verification;
- destructive resume-node deletion remains an explicit user-confirmed action.

Required host/browser changes live in:
- browser-extension/content.js
- src/OpenQuickHost/YanziBrowserCapabilityProvider.cs

The current implementation has been validated against Zhaopin's resume endpoints under fe-api.zhaopin.com.
