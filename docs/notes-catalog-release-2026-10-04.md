# Notes application catalog 0.1.5

## Cause

The public application catalog and the physical phone's production Notes APK both remained at 0.1.4/code 5. The local 0.1.5/code 6 package had not been committed or published. DEX inspection confirms that 0.1.4's actions menu includes deletion and conflict actions, but omits desktop handoff; 0.1.5's actions menu includes 在电脑上打开. Updating the host APK does not replace this separately installed companion.

The automatic application publication workflow was hardcoded to the album metadata and APK path even though notes input changes triggered it. It now selects only metadata/APKs changed in the push, publishes sequentially to preserve catalog entries, and supports explicit manual selection. Added guards refuse downgrades and replacement of a published versionCode with a different APK.

## Verification and publication

- Built Notes Dev and Release from the current extension-owned sources. Isolated Dev was installed on OnePlus; existing note-card long press displayed 在电脑上打开. Production Notes 0.1.4 and its data were preserved.
- Release-selection checks passed notes JSON, notes APK, unrelated changes, manual notes selection, and multiple applications. Downgrade and same-code/different-hash publication guards passed. Workflow YAML parsed successfully.
- GitHub Actions run 37171525911 succeeded with only publish (release-inputs/applications/yanzi-notes.json).
- Public catalog now advertises 0.1.5/code 6. Full public download passed package, signature, size and SHA256 verification against the committed APK.
- APK size: 44,555 bytes. SHA256: 3d84c3b2867c11cd59acf98884ef6473df9be0cd9702fbe4a267ef2228c1c70e.
- Download: https://sync.luoluoluo.cc.cd/downloads/applications/yanzi-notes-0.1.5.apk.

Users should refresh the host's Phone > Application Center and download/install Notes 0.1.5 over the existing app, without uninstalling it. On the phone, long press a note card to open its actions menu.
