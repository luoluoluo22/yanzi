# Android 0.2.48 navigation and slim release

- Removed the top-left connection button and remote-operation heading from the desktop dashboard.
- Desktop bottom tab now presents LAN online (green), LAN reconnecting (orange), cloud online (blue), or offline (red), with accessible state text.
- First desktop-tab tap selects the page; a repeated tap opens the existing LanPairingActivity with its recheck control.
- Text, photo and file sends hold a navigation visibility lease until their asynchronous operation finishes. A finally block releases each lease for success/failure; overlapping sends only restore the bar after the final operation. AI loading also hides it until completion/cancellation.
- Ships the earlier confirmed-device-removal logout, explicit login enrollment and Android/account stable identity changes.
- All variants now exclude the offline Vosk wake model/native library by default; ordinary system speech input remains. Opt back in via -PYANZI_BUNDLE_WAKE_MODEL=true.

## Verification

OnePlus GM1900 isolated Dev package 0.2.48-dev: native instrumentation passed four connection states, page selection/repeated-tap details, retained recheck control, simultaneous send leases, AI stop restoration, and actual text send lifecycle. Host logs recorded the test text with message ID c1031fed571a400ead715a1814f1b4ac. Captured and inspected the Dev screenshot; top status/title are absent and the bottom tab shows green LAN state. Production app installation and data remained unchanged.

Dev and release compilation passed, including release lintVital. Release package cc.luoluoluo.yanzi.mobile, code 48/name 0.2.48. APK contains no Vosk model entries or libvosk.so. Signing certificate SHA256 matches previous releases: 8a0ec0b84d1a05edcc89dd020bf81901f9ed7f083887db7c05201c59b31e1ee3.

Release APK bytes: 7229629
SHA256: 57298202f541e968b89e62ead91fac2ac110c51dff9aa8e736eca34a01d45fc4

Published GitHub Release android-v0.2.48 and the R2 APP update manifest. The complete public APK download matched both the local APK and GitHub asset SHA256: 57298202f541e968b89e62ead91fac2ac110c51dff9aa8e736eca34a01d45fc4. Public manifest points to android-v0.2.48 with draft=false; APK size is 7,229,629 bytes.

The final 0.2.48-dev desktop execution suite passed APP/LAN calendar execution (d76b8348434a42ddbec7b4a372a76a19), cloud execution (msg_3aaf3724a480b60a0d55ca08), removed-registration detection/background rejection, and isolated credential cleanup. Latest Dev remains installed; production phone APK was not overwritten.
