# Changelog

## 1.0.4

- Fixed repeated marker flashing when targets cross the visible compass arc boundary. Added a per-marker angular hysteresis latch: once outside, a marker must move safely inside the arc before fading back in.
- Keep fading markers positioned at the viewport edge so `RectMask2D` does not abruptly clip them before the fade completes.
- Preserve the same spatial edge-alpha multiplier through `OutsideArc`, inactive, invalid, and mid-transition state changes; the fade extends smoothly by the hysteresis angle instead of jumping back to full opacity.
- Keep marker Graphics enabled for the full fade transition; opacity is controlled by CanvasRenderer color alpha and Graphics are disabled only after the fade reaches zero. This avoids per-frame enable/disable toggling near the edge-fade threshold.
- Added configurable `Outside Arc Hysteresis Degrees` (default 2°).

## 1.0.3

- Removed marker visual resets from `OnDisable`; interrupted fades and scale animations are preserved when the compass component is disabled/re-enabled, preventing the marker flash/reset.
- Added `SetLocationTargetActiveWithFade()` as a wrapper for deactivating/reactivating a location's target GameObject after the marker fade completes.
- Changed the default Landmark Y offset from 12 to 0; Landmark diamond size remains 10.
- Added matching equilateral triangles to both ends of the compass line. The left triangle points right and the right triangle points left, both using the line tint for a seamless join.
- Added configurable edge triangle visibility and size.


## 1.0.2

- Added smooth fade-out for inactive markers.
- Added proximity fade-out with a configurable grow effect and fade-in with a shrink-back effect when the target leaves the exclusion radius.
- Added explicit UI marker scale restoration on animation completion, component disable/destruction, and marker rebuild/removal. World target Transforms are never scaled or rotated.
- Added optional Landmark diamonds: 45-degree rotated squares with per-location size and Y offset.
- Added XZ distance-based marker sizing with configurable start distance, end distance, and minimum scale (maximum is clamped to 1).
- Added unscaled-time animation option (enabled by default).

## 1.0.1

- Added Runtime and Editor assembly definitions for package-folder compilation.
- Replaced deprecated `FindFirstObjectByType` with `FindAnyObjectByType` in the creation menu.

## 1.0.0

- Initial horizontal compass strip, cardinal labels, location icons, edge fading, and degree ticks.
