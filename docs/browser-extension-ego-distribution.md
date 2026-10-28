# Yanzi Browser Helper: Edge store and Ego compatibility

The Edge Add-ons listing is `https://microsoftedge.microsoft.com/addons/detail/yanzi-browser-helper/cflclhbkmiepfgholcobghgapgfbigne`.
The listing's browser compatibility gate is controlled by the **store**, not the
extension manifest. A disabled Get button in Ego is not evidence that this MV3
extension is unable to run on Ego. Don't spoof User-Agent or try to bypass the store.

## Ego (Windows, Chromium 120+)

Ego supports loading unpacked Chromium extensions. Use the **main branch** copy
of `browser-extension` and keep it at a stable path for future reloads.

```powershell
ego-browser nodejs -e "await import('file:///F:/Desktop/kaifa/yanzi-release-main-1052/scripts/ego-browser-helper-install.mjs')"
```

The installer checks Chromium version when exposed by Ego and checks extension identity, verifies loading,
and never installs a duplicate over an existing installation. If another Yanzi
extension exists (including one loaded from another Git worktree), it reports
its ID/version/path and leaves it untouched to preserve extension storage and
the existing extension ID. Remove/migrate a previous copy deliberately, not
during automatic updates. **Never use multiple Ego Spaces as independent
extension profiles.** Existing Ego-installed copies can also be reloaded using
`ego-extension-dev-reload.mjs` without changing their ID.

The local build has no relation to the published Edge store version; verify both.

## Packaging a store update

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-browser-extension-release.ps1
```

The result is `.artifacts/browser-extension-release/Yanzi-Browser-Helper-VERSION-edge-chromium.zip`;
it contains only the extension assets and a root-level manifest. Syntax is checked.
Upload the ZIP as an **update** of the existing extension in Partner Center, or
use Microsoft's Edge Add-ons Update REST API if product GUID and API key/client
ID are configured. The public extension ID is **not** the product GUID.
After submission, review certification status and separately verify that the
public store page shows the newly approved version. Updating main or creating
the ZIP does **not** publish a new version to Microsoft.

## QA checklist

1. Verify zip has the root manifest and is free of files from `scratch`.
2. Verify `manifest_version=3` and `minimum_chrome_version` <= Ego Chromium.
3. Install/reload in Ego; verify version, ID stability, enabled state, popup,
   connection to the Yanzi desktop agent, and web requests.
4. Check Microsoft Edge regression separately (store-installed extension).
5. Verify the Partner Center approval **then** the public store version.
