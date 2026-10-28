# Yanzi UI 0.5.1 · Capsule geometry correction

Date: 2026-10-08

## Purpose

Previous buttons using CornerRadius=999 technically rounded their edges, but short text labels and low horizontal padding made the whole component look oval. An intentional capsule consists of two straight horizontal lines joined by circular end caps.

## Geometry (WPF DIP)

| Component | Outer height | Min width | Chrome height | End radius | Minimum straight middle |
| --- | ---: | ---: | ---: | ---: | ---: |
| Pill | 32 | 96 | 28 | 14 | 64 |
| Chip | 24 | 76 | 20 | 10 | 52 |
| Segmented first | 32 | 112 | 32 | 15 left | 82 |
| Segmented last | 32 | 48 | 32 | 15 right | 18 |

Pill and Chip Chrome are inset by 2 DIP all around, while outer focus rings use radii 16 and 12. Exact Chrome radii are half their painted heights. Public styles enforce minimum widths and horizontal padding; Gallery no longer overrides these to zero.

## Reusable APIs

- Yanzi.Button.Pill.*: opt-in style with Height 32, MinWidth 96, Padding 18/5, Chrome radius 14.
- Yanzi.Button.Chip.*: dedicated shared template with Height 24, MinWidth 76, Padding 13/2, Chrome radius 10.
- YanziSegmentedButtonGroup: radius 16 outer shell and radius 15 left/right caps; minimum widths 112 first and 48 remaining.
- Standard business Button styles unchanged.

## Automated and manual acceptance

- UI Gallery Release build: 0 warnings, 0 errors.
- Component verification: 141 passing checks including geometries, template corners, light theme and click actions.
- Windows UI Automation: 81 passing assertions across 15 pages.
- Isolated Runtime: 18 passing integration checks, 0 errors, 14 preexisting host warnings.
- Independent gallery 0.5.1 installed as a desktop shortcut and Yanzi extension, without replacing production Runtime.

Final desktop image: F:\Desktop\Yanzi-UI-视觉对照\yanzi-capsule-0.5.1-final.png

The change corrects capsule proportions; exact font rendering, DPI behavior and shadcn's web animation/behavior remain separate comparison tasks.
