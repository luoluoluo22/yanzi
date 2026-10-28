# Yanzi UI 0.5.5 - Badge small-capsule correction

Date: 2026-10-08

## Purpose

After fixing the larger Pill/Chip controls, the fourth row on the gallery homepage (Badge, Secondary, Outline) still looked oval. These are status badges, not actionable buttons; they must remain non-focusable and must not gain accidental click behavior.

## Fix

Only the shared `Yanzi.Badge` WPF template has been adjusted:

- Fixed visual height: 22 WPF DIPs.
- Exact circular end radius: 11 DIP, instead of `CornerRadius=999`.
- Horizontal padding: 12 DIP each side, with minimum width 52 DIP.
- The center remains visibly straight while the width continues to follow each label.
- All six variants, icons and loading spinner reuse the same template; semantic colors and public APIs remain unchanged.

The following widths were measured in a real WPF layout: Badge approximately 60.3 DIP, Outline 66.1 DIP and Secondary 83.2 DIP. Width increases for longer text and shrinks again after changing text to a shorter string.

## Verification

- Gallery Release build: 0 errors, 0 warnings.
- WPF component checks: 165/165 passing.
- Gallery UI Automation: 85 checks across all 15 pages.
- Standalone Runtime integration: 18 checks passing; host still has its preexisting 14 warnings.
- Independent preview version 0.5.5 installed; production Runtime not replaced.
- Real screenshot: `F:\Desktop\Yanzi-UI-视觉对照\yanzi-badge-straight-capsule-0.5.5.png`.

The immediately preceding 0.5.4 search-input caret alignment adjustment was also retained. Source status was scoped to the UI library, gallery, tests and installer; unrelated local work was left untouched.
