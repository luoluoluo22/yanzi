# Implementation Plan: Window Binding Drag Enhancements

## Overview

本计划将窗口绑定拖放增强功能分解为递增的编码任务。每个任务构建在前一个任务之上，确保代码始终处于可集成状态。核心修改集中在 `WindowBindingDropOverlayWindow`（拖放覆盖层）、`WindowBoundExtensionsService`（运行时服务）、`WindowBoundExtensionOverlayWindow`（覆盖按钮）以及数据模型类。

## Tasks

- [ ] 1. Expand data models and constants
  - [x] 1.1 Add interior position constants to `WindowBindingCorners` in `src/OpenQuickHost/AppSettingsStore.cs`
    - Add `InsideTopLeft`, `InsideTopRight`, `InsideBottomLeft`, `InsideBottomRight` constants
    - Add `IsInterior(string corner)` static method that returns true for `inside_*` values
    - Update `Normalize()` to handle the 4 new corner values (return them as-is instead of falling through to TopLeft)
    - _Requirements: 4.1_

  - [x] 1.2 Add `HoverMode` property to `WindowBindingRuleSettings` in `src/OpenQuickHost/AppSettingsStore.cs`
    - Add `public bool HoverMode { get; set; } = false;` property
    - _Requirements: 6.2_

- [x] 2. Fix Extension Overlay icon clipping
  - [x] 2.1 Update `WindowBoundExtensionOverlayWindow.xaml` to fix icon clipping
    - Change Window `Width` and `Height` from 34 to 50
    - Remove `ClipToBounds="True"` from the inner Border
    - Add `Margin="8"` to the inner Border to center the 34×34 content within the 50×50 window
    - The DropShadowEffect will now have room to render without clipping
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [ ]* 2.2 Write unit tests for icon clipping fix
    - Verify window dimensions are 50×50
    - Verify inner Border is 34×34 with Margin="8"
    - Verify no ClipToBounds on the Border containing the DropShadowEffect
    - _Requirements: 5.1, 5.2, 5.3_

- [x] 3. Rewrite `TryResolveBindingArea` for edge-vs-corner fix and interior support
  - [x] 3.1 Rewrite `TryResolveBindingArea` in `src/OpenQuickHost/WindowBindingDropOverlayWindow.xaml.cs`
    - Calculate distance to each edge (left, right, top, bottom)
    - Determine minimum edge distance; if ≤ 96px → external binding (edge band)
    - For external binding: vertical = nearest of top/bottom, horizontal = nearest of left/right
    - If point is inside window rect AND beyond 96px from all edges → interior binding
    - For interior binding: resolve quadrant based on window center (InsideTopLeft, InsideTopRight, InsideBottomLeft, InsideBottomRight)
    - If point is outside window rect AND beyond 96px from all edges → return false
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 4.2_

  - [ ]* 3.2 Write property test: Edge-band binding area resolution correctness
    - **Property 2: Edge-band binding area resolution correctness**
    - Generate random points within 96px edge bands of random window rects
    - Verify vertical component matches nearest horizontal edge, horizontal component matches nearest vertical edge
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

  - [ ]* 3.3 Write property test: Interior quadrant resolution correctness
    - **Property 3: Interior quadrant resolution correctness**
    - Generate random points inside window rect that are >96px from all edges
    - Verify returned corner matches the correct quadrant relative to window center
    - **Validates: Requirements 4.2**

- [x] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Update `GetBaseLeft` / `GetBaseTop` for interior positions
  - [x] 5.1 Modify `GetBaseLeft` and `GetBaseTop` in `src/OpenQuickHost/WindowBoundExtensionsService.cs`
    - Add interior position branches using `WindowBindingCorners.IsInterior()`
    - For interior left positions (`InsideTopLeft`, `InsideBottomLeft`): return `leftDip + marginDip`
    - For interior right positions (`InsideTopRight`, `InsideBottomRight`): return `rightDip - widthDip - marginDip`
    - For interior top positions (`InsideTopLeft`, `InsideTopRight`): return `topDip + marginDip` (add marginDip parameter to GetBaseTop)
    - For interior bottom positions (`InsideBottomLeft`, `InsideBottomRight`): return `bottomDip - heightDip - marginDip`
    - External positions retain existing logic unchanged
    - _Requirements: 4.3, 4.4_

  - [ ]* 5.2 Write property test: Interior overlay position is within window bounds
    - **Property 4: Interior overlay position is within window bounds**
    - Generate random valid window rects, DPI values (96–288), and interior corner positions
    - Verify calculated overlay position is entirely within window DIP bounds
    - **Validates: Requirements 4.3**

- [x] 6. Add drag label display to `WindowBindingDropOverlayWindow`
  - [x] 6.1 Add `DragLabel` TextBlock to `WindowBindingDropOverlayWindow.xaml`
    - Add a TextBlock element to `RootCanvas` with readable font size (≥12px), white foreground, semi-transparent dark background
    - Set `Visibility="Collapsed"` by default
    - _Requirements: 1.1, 1.2_

  - [x] 6.2 Implement drag label logic in `WindowBindingDropOverlayWindow.xaml.cs`
    - Add `UpdateDragLabel(POINT cursorPos)` method that positions the label below the cursor
    - Implement text truncation: if extension name > 20 characters, truncate to first 20 chars + "…"
    - Call `UpdateDragLabel` from `Window_DragOver` to keep label following cursor
    - Show label when drag starts, hide when drag ends or leaves
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

  - [ ]* 6.3 Write property test: Text truncation preserves short names and truncates long names
    - **Property 1: Text truncation preserves short names and truncates long names**
    - Generate random strings; verify strings ≤20 chars are unchanged, strings >20 chars become 21 chars ending with "…"
    - **Validates: Requirements 1.4**

- [x] 7. Add drop position preview icon
  - [x] 7.1 Add `PreviewIcon` element to `WindowBindingDropOverlayWindow.xaml`
    - Add a Border (34×34, 50% opacity) with icon content to `RootCanvas`
    - Set `Visibility="Collapsed"` by default
    - _Requirements: 2.1, 2.2_

  - [x] 7.2 Implement preview icon positioning in `WindowBindingDropOverlayWindow.xaml.cs`
    - Add `ShowPreviewIcon(RECT rect, uint dpi, string corner)` method
    - Calculate position using the same logic as `GetBaseLeft`/`GetBaseTop` from `WindowBoundExtensionsService` (extract shared static method or duplicate logic)
    - Account for DPI scaling and margin settings
    - Call from `ShowMarker` when a valid binding area is detected
    - Add `HidePreviewIcon()` called when cursor leaves binding areas
    - _Requirements: 2.1, 2.3, 2.4, 2.5_

  - [ ]* 7.3 Write property test: Preview icon position matches service positioning
    - **Property 5: Preview icon position matches service positioning**
    - Generate random window rects, DPI values, corners, and margin settings
    - Verify preview icon position equals `GetBaseLeft`/`GetBaseTop` output for same inputs
    - **Validates: Requirements 2.5**

- [x] 8. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Add visual distinction for interior binding zones
  - [x] 9.1 Update `ShowMarker` in `WindowBindingDropOverlayWindow.xaml.cs` to distinguish interior zones
    - When resolved corner is an interior position, change `CornerMarker` background/border color (e.g., different accent color or dashed border)
    - Update `CornerText` to display interior position labels (e.g., "内左上", "内右上", "内左下", "内右下")
    - _Requirements: 4.5_

- [x] 10. Implement Hover Display Mode
  - [x] 10.1 Add context menu items for Hover Mode in `MainWindow`
    - In `ShowWindowBindingContextMenu`, add "悬停时显示" menu item when HoverMode is false
    - Add "始终显示" menu item when HoverMode is true
    - On click, toggle `HoverMode` in the rule settings and persist
    - _Requirements: 6.1, 6.7_

  - [x] 10.2 Add fade-in/fade-out animations to `WindowBoundExtensionOverlayWindow`
    - Add `FadeIn` storyboard: Opacity 0→1, 200ms duration
    - Add `FadeOut` storyboard: Opacity 1→0, 300ms duration
    - Add public methods `AnimateFadeIn()` and `AnimateFadeOut(Action onCompleted)` to trigger animations
    - _Requirements: 6.4, 6.5_

  - [x] 10.3 Implement hover detection and visibility management in `WindowBoundExtensionsService`
    - Add `IsInHoverDetectionZone(WindowBoundExtensionOverlayWindow overlay)` method: check if cursor is within ±20px of overlay bounds
    - Add `_hideTimers` dictionary to track per-rule delayed hide timers (500ms)
    - In `Tick()`, for rules with `HoverMode=true`: if cursor in detection zone → show with fade-in and cancel any pending hide; if cursor outside → schedule hide after 500ms with fade-out
    - Guard against timer firing after overlay is closed
    - _Requirements: 6.3, 6.4, 6.5, 6.6_

  - [ ]* 10.4 Write property test: Hover detection zone boundary correctness
    - **Property 6: Hover detection zone boundary correctness**
    - Generate random overlay positions (left, top, width, height) and cursor positions
    - Verify `IsInHoverDetectionZone` returns true iff cursor x ∈ [left-20, left+width+20] AND y ∈ [top-20, top+height+20]
    - **Validates: Requirements 6.4, 6.6**

  - [ ]* 10.5 Write unit tests for hover mode timer behavior
    - Test 500ms delay is configured correctly
    - Test cancel-hide when cursor re-enters zone
    - Test fade-in/fade-out animation durations (200ms / 300ms)
    - _Requirements: 6.5_

- [x] 11. Wire interior overlay position tracking on window move/resize
  - [x] 11.1 Ensure `WindowBoundExtensionsService.Tick()` correctly updates interior overlays on target window move/resize
    - The existing `WinEventHook` for `EventObjectLocationchange` already triggers `SafeTick` → `UpdateOverlayPosition`
    - Verify that `UpdateOverlayPosition` with interior corners correctly recalculates position using updated `GetBaseLeft`/`GetBaseTop`
    - If needed, add explicit handling to ensure interior overlays stay within window bounds after resize
    - _Requirements: 4.4_

- [x] 12. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (FsCheck with xUnit)
- The icon clipping fix (Task 2) is independent and can be done early for quick visual improvement
- Interior binding (Tasks 1, 3, 5, 9, 11) forms a logical chain that must be done in order
- Hover mode (Task 10) depends on the data model expansion (Task 1.2) but is otherwise independent
