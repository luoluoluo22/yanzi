# Requirements Document

## Introduction

本文档定义了窗口绑定拖放体验增强功能的需求。该功能涵盖五个改进方向：拖拽时展示文字标签、预落盘预览图标、拖放位置修复（边缘与角落判定）、窗口内绑定支持、以及悬停显示模式。项目为 WPF 桌面应用（C#/.NET），核心涉及 `WindowBindingDropOverlayWindow`（拖放全屏覆盖层）、`WindowBoundExtensionsService`（运行时绑定管理）和 `WindowBoundExtensionOverlayWindow`（绑定后的覆盖按钮）。

## Glossary

- **Drop_Overlay**: `WindowBindingDropOverlayWindow`，拖放过程中覆盖整个桌面的透明窗口，负责检测目标窗口和绑定区域
- **Binding_Service**: `WindowBoundExtensionsService`，管理绑定规则的运行时服务，负责覆盖按钮的生命周期和定位
- **Extension_Overlay**: `WindowBoundExtensionOverlayWindow`，绑定后显示在目标窗口旁的 34×34 覆盖按钮
- **Binding_Area**: 目标窗口边缘 96px 范围内的可绑定区域，由 `TryResolveBindingArea` 判定
- **Corner_Marker**: 拖放过程中显示的矩形高亮区域，指示当前绑定位置
- **Binding_Position**: 绑定位置标识，当前为四角模型（TopLeft、TopRight、BottomLeft、BottomRight），将扩展为包含边缘和窗口内部位置
- **Preview_Icon**: 拖放过程中在目标位置显示的预览图标，展示扩展最终落盘后的外观
- **Hover_Mode**: 悬停显示模式，绑定的扩展默认隐藏，仅在鼠标移至对应位置时显示
- **Quick_Panel**: `QuickPanelWindow`，主面板窗口，拖拽操作的发起点

## Requirements

### Requirement 1: Drag Label Display

**User Story:** As a user, I want to see a text label below the extension icon during drag, so that I can clearly identify which extension I am dragging.

#### Acceptance Criteria

1. WHEN a drag operation is initiated from the Quick_Panel, THE Drop_Overlay SHALL display the extension name as a text label below the drag cursor icon
2. THE Drop_Overlay SHALL render the text label with a readable font size (no smaller than 12px) and sufficient contrast against the desktop background
3. WHILE the drag operation is in progress, THE Drop_Overlay SHALL keep the text label aligned below the extension icon and following the cursor position
4. IF the extension name exceeds 20 characters, THEN THE Drop_Overlay SHALL truncate the text with an ellipsis

### Requirement 2: Drop Position Preview Icon

**User Story:** As a user, I want to see a preview of the extension overlay icon at the exact landing position while dragging over a target window, so that I can confirm where the extension will appear before dropping.

#### Acceptance Criteria

1. WHEN the cursor enters a valid Binding_Area during drag, THE Drop_Overlay SHALL display a Preview_Icon at the calculated base position where the Extension_Overlay will appear after binding
2. THE Drop_Overlay SHALL render the Preview_Icon with the same dimensions as the Extension_Overlay (34×34 pixels) and at reduced opacity (50%) to distinguish it from actual bound extensions
3. WHEN the cursor moves between different Binding_Areas of the same target window, THE Drop_Overlay SHALL update the Preview_Icon position to reflect the new target Binding_Position
4. WHEN the cursor leaves all valid Binding_Areas, THE Drop_Overlay SHALL hide the Preview_Icon
5. THE Drop_Overlay SHALL calculate the Preview_Icon position using the same logic as `GetBaseLeft` and `GetBaseTop` in the Binding_Service, accounting for DPI scaling and margin settings

### Requirement 3: Edge-vs-Corner Binding Area Resolution Fix

**User Story:** As a user, I want the drop position to correctly resolve to the intended edge side when I drop near the top or bottom edge of a window, so that the extension binds to the correct position.

#### Acceptance Criteria

1. WHEN the cursor is within the top edge band and horizontally in the left half of the target window, THE Drop_Overlay SHALL resolve the Binding_Position as TopLeft
2. WHEN the cursor is within the top edge band and horizontally in the right half of the target window, THE Drop_Overlay SHALL resolve the Binding_Position as TopRight
3. WHEN the cursor is within the bottom edge band and horizontally in the left half of the target window, THE Drop_Overlay SHALL resolve the Binding_Position as BottomLeft
4. WHEN the cursor is within the bottom edge band and horizontally in the right half of the target window, THE Drop_Overlay SHALL resolve the Binding_Position as BottomRight
5. WHEN the cursor is within both an edge band and a side band simultaneously, THE Drop_Overlay SHALL prioritize the band whose edge is closest to the cursor position
6. WHEN the cursor is equidistant from two edges, THE Drop_Overlay SHALL resolve using the horizontal position relative to the window center as the tiebreaker

### Requirement 4: Interior Window Binding Support

**User Story:** As a user, I want to bind extensions inside the window area (not just at the external edges/corners), so that I can place extension overlays within the window content area for more flexible positioning.

#### Acceptance Criteria

1. THE Binding_Service SHALL support Binding_Position values beyond the four external corners, including interior positions (e.g., InsideTopLeft, InsideTopRight, InsideBottomLeft, InsideBottomRight)
2. WHEN the cursor is inside the target window content area during drag (beyond the 96px edge band), THE Drop_Overlay SHALL resolve the Binding_Position to the nearest interior quadrant
3. THE Binding_Service SHALL position interior Extension_Overlays inside the target window bounds, using the window's interior corner as the base position with the configured margin applied inward
4. WHILE the target window is moved or resized, THE Binding_Service SHALL update interior Extension_Overlay positions to maintain their relative placement within the window bounds
5. THE Drop_Overlay SHALL visually distinguish interior binding zones from exterior edge zones using a different Corner_Marker style or color

### Requirement 5: Extension Overlay Icon Clipping Fix

**User Story:** As a user, I want the bound extension overlay icon to render fully without being clipped on the left and right sides, so that the icon appears complete and professional.

#### Acceptance Criteria

1. THE Extension_Overlay SHALL render the extension icon without any visible clipping on any side (left, right, top, bottom)
2. THE Extension_Overlay SHALL ensure the DropShadowEffect is not clipped by the parent container's bounds
3. THE Extension_Overlay SHALL maintain the 34×34 visual size of the icon area while providing sufficient rendering space for effects and anti-aliasing
4. WHEN the Extension_Overlay uses an image icon (HasImageIcon), THE icon SHALL be rendered with adequate internal margin to prevent edge clipping from the rounded CornerRadius

### Requirement 6: Hover Display Mode

**User Story:** As a user, I want to set bound extensions to "hidden by default" mode via right-click context menu, so that the overlay only appears when I move my mouse to the corresponding position, keeping my desktop clean.

#### Acceptance Criteria

1. WHEN the user right-clicks on an Extension_Overlay, THE Extension_Overlay SHALL display a context menu option labeled "悬停时显示" (Show on Hover)
2. WHEN the user selects "悬停时显示" from the context menu, THE Binding_Service SHALL persist the Hover_Mode setting for that binding rule
3. WHILE a binding rule has Hover_Mode enabled, THE Binding_Service SHALL hide the Extension_Overlay by default when the target window is in the foreground
4. WHILE a binding rule has Hover_Mode enabled AND the cursor enters a detection zone around the Extension_Overlay's base position, THE Binding_Service SHALL show the Extension_Overlay with a fade-in animation
5. WHEN the cursor leaves the detection zone AND the Extension_Overlay is not being interacted with, THE Binding_Service SHALL hide the Extension_Overlay with a fade-out animation after a 500ms delay
6. THE Binding_Service SHALL define the detection zone as a rectangular area extending 20 pixels beyond the Extension_Overlay's base position in each direction
7. IF the user right-clicks the Extension_Overlay while in Hover_Mode, THEN THE Extension_Overlay SHALL display a context menu option labeled "始终显示" (Always Show) to disable Hover_Mode
