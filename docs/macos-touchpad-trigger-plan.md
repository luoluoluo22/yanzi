# macOS Touchpad Trigger Plan

Yanzi's macOS radial menu should treat the touchpad as the primary trigger surface. A mouse secondary-button long press is still useful as a fallback, but it should not define the architecture.

## Current Direction

Keep the radial menu UI in `Yanzi.Avalonia`, and keep macOS input capture in `Yanzi.Platform.Mac`.

`Yanzi.Avalonia` should only consume high-level activation events:

- activation requested
- activation released
- activation source

It should not know whether the event came from CoreGraphics, AppKit, a Swift helper, or a future private multitouch bridge.

## Trigger Priority

1. **Secondary-click long press**

   This works today with both a mouse right button and common trackpad secondary-click settings such as two-finger click/tap. macOS exposes this as a secondary mouse event, so it is the lowest-risk baseline.

2. **Trackpad gesture backend**

   Add a macOS-specific backend for gestures after the UI and activation contract are stable. Good candidates:

   - one finger pressed, then another finger moves to open the radial menu
   - four-finger movement, if we later decide it is preferable
   - two-finger edge swipe and hold
   - two-finger small circular gesture
   - force click, where supported and detectable

   The backend should emit the same activation events as the secondary-click listener.

   The current CoreGraphics event tap backend cannot reliably distinguish global trackpad finger count. The first formal implementation uses a dedicated `MacTrackpadGestureInputTriggerListener` that watches public left-button drag and scroll-wheel event streams while the primary button is held. If that does not expose enough signal or causes too many false positives, the next step is a Swift/AppKit helper, and only then private MultitouchSupport APIs if public events prove insufficient.

3. **Advanced native helper**

   If public CoreGraphics/AppKit events are not enough, move the gesture capture into a Swift/AppKit helper process and communicate with the .NET app through IPC. This keeps native risk out of the Avalonia UI process.

## Avoid For Now

- Do not rely on Avalonia pointer events for global trackpad activation. They only work while the app owns the pointer/window.
- Do not put raw macOS P/Invoke into `Yanzi.Avalonia`.
- Do not couple radial menu behavior to right-button naming. Use activation events instead.
- Avoid private multitouch APIs until the public-event path is proven insufficient.

## Near-Term Implementation

- `Yanzi.Shared` owns activation contracts and settings.
- `Yanzi.Platform.Mac` owns macOS event capture.
- `Yanzi.Avalonia` subscribes to activation events and shows/hides the radial menu.

This lets us replace the macOS backend later without rewriting the menu UI.
