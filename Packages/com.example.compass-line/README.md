# Compass Line UI

A drop-in horizontal compass strip for **Unity 6.5 / 6000.5.x** (package version **1.0.4**), built with the built-in **uGUI** system. It uses only `UnityEngine` and `UnityEngine.UI`; it does not require TextMeshPro, the Input System, or third-party assets.

## Features

- Horizontal compass strip with configurable width (default **256 UI units**).
- Uses a generated, plain white **256 × 2 px line** when no background sprite is assigned. Assign any Sprite to use your own background.
- Adds a matching equilateral triangle at each end of the line; the left points inward to the right and the right points inward to the left. Edge triangles use the line tint and can be hidden or resized.
- Portuguese cardinal abbreviations **N / L / S / O**, or English **N / E / S / W**. Optional full names are available.
- Transform-based location markers with an individual icon Sprite and active/inactive flag.
- Inactive markers fade out smoothly. Markers entering the **100 m** proximity exclusion zone fade out while growing slightly; when they leave that zone, they fade back in while shrinking to their intended size.
- Proximity checks and distance-based sizing always use horizontal **XZ** distance, ignoring elevation.
- Optional per-location Landmark diamonds: a square rotated 45 degrees, with per-landmark size and Y offset (defaults: **10** size and **0** Y offset).
- Distance-based sizing: marker size starts shrinking after **150 m**, reaches the configured minimum scale at **700 m**, and never exceeds **1.0**. The defaults are 150 m, 700 m, and 0.5.
- Fades labels, degree ticks, and location icons as they approach either strip edge. Marker exit/re-entry uses a configurable 2° hysteresis dead band by default to prevent flashing when a target jitters at the visible-arc boundary.
- Degree ticks and labels default to **0, 45, 90, ... 315** below the line.
- Exposes `SetLocationActive()`, `SetLocationTargetActiveWithFade()`, and `RefreshLocations()` for runtime control.
- Adds a menu item at **GameObject → UI → Compass Line**.

## Install

### Option A — embedded package (recommended)

1. Extract the `com.example.compass-line` folder into your project's `Packages/` directory, preserving that folder name.
2. Unity resolves the package and compiles the scripts through the included Runtime and Editor assembly definitions (`.asmdef`). The package manifest declares `com.unity.ugui` 2.5.0, which is the Unity 6.5 uGUI package version.
3. In the Hierarchy, use **GameObject → UI → Compass Line**. It creates a Canvas if the scene does not already contain one.
4. Press Play. The generated line and its labels/markers are created at runtime.

### Option B — copy the scripts

Copy `Runtime/CompassLineUI.cs` into `Assets/Scripts/` and `Editor/CompassLineMenu.cs` into `Assets/Editor/`. Make sure the built-in **Unity UI (uGUI)** package is enabled. Then use the menu item described above, or add `CompassLineUI` to a `RectTransform` child of a Canvas yourself.

## Setup

1. Select the **Compass Line** object.
2. Set **Heading Source** to the player or camera Transform whose yaw defines the direction the player is facing. If left empty, the component tries `Camera.main`, then falls back to its own Transform.
3. Optionally set **Distance Origin** to the player's ground/root Transform. If empty, it uses Heading Source.
4. Add entries under **Locations**. For each entry, assign:
   - `id`: a unique string used by runtime calls.
   - `target`: the world Transform to point toward.
   - `icon`: the marker Sprite.
   - `isActive`: turn off to fade the marker out.
   - `isLandmark`: enable the separate 45-degree diamond above the line.
   - `landmarkDiamondSize` and `landmarkYOffset`: customize that landmark's diamond size and vertical offset independently. Defaults are **10** and **0**.
5. Choose **Language** (`Portuguese` or `English`). `Show Full Cardinal Names` changes the abbreviations into Norte/Leste/Sul/Oeste or North/East/South/West.
6. Assign a custom **Background Sprite** to replace the default line. Adjust `Background Height` to suit that image.
7. Adjust **Visible Arc Degrees**, **Marker Y Offset**, **Marker Size**, and **Edge Fade Start** to match the HUD.
8. In **Marker Animation**, tune **Marker Transition Duration** and **Proximity Hide Scale**. The latter controls how much a marker grows as it fades out for being too close.
9. In **Distance-based Marker Sizing**, set the distance where shrinking begins, the distance where minimum size is reached, and the minimum scale. Minimum scale is clamped to 1 or less; maximum scale is always 1.

## Runtime control

```csharp
using CompassLine;
using UnityEngine;

public class CompassLocationExample : MonoBehaviour
{
    [SerializeField] private CompassLineUI compass;

    public void HideQuestMarker()
    {
        compass.SetLocationActive("quest_01", false);
    }

    public void ShowQuestMarker()
    {
        compass.SetLocationActive("quest_01", true);
    }

    // Prefer this wrapper when the target GameObject itself should be disabled.
    // It lets the marker finish fading out before SetActive(false) is applied.
    public void DisableQuestTargetAfterFade()
    {
        compass.SetLocationTargetActiveWithFade("quest_01", false);
    }

    public void EnableQuestTarget()
    {
        compass.SetLocationTargetActiveWithFade("quest_01", true);
    }

    public void AddTemporaryMarker(Transform target, Sprite icon)
    {
        compass.AddLocation(new CompassLineUI.CompassLocation
        {
            id = "temporary_marker",
            target = target,
            icon = icon,
            isActive = true
        });
    }

    public void RemoveTemporaryMarker()
    {
        compass.RemoveLocation("temporary_marker");
    }
}
```

Changes to an existing entry's `isActive`, `isLandmark`, `landmarkDiamondSize`, or `landmarkYOffset` values are picked up automatically. `AddLocation()` and `RemoveLocation()` manage runtime markers and refresh the marker views; call `RefreshLocations()` after directly editing the serialized list at runtime.

### Animation and scale safeguards

- The compass never changes the scale, rotation, or active state of a location's world Transform. Only generated UI `RectTransform` objects are animated.
- `isActive = false`, an inactive target GameObject, or an invalid location causes a fade-out transition.
- Entering the minimum-distance zone causes a fade-out plus a configurable scale-up. Leaving that zone causes a fade-in while the UI marker shrinks back to its normal scale.
- The marker's normal distance-scaled dimensions are stored in `RectTransform.sizeDelta`; animation is a temporary local-scale multiplier. When that multiplier reaches 1, it is explicitly restored to `Vector3.one`.
- UI marker scales are reset before marker rebuild/removal and when the compass component is destroyed. The component deliberately does not reset visuals in `OnDisable`; this preserves an in-progress fade/scale animation and prevents a flash when the compass is disabled and re-enabled. Marker visibility at the arc boundary also uses hysteresis, so tiny heading changes cannot repeatedly reverse the fade.
- Use `SetLocationTargetActiveWithFade(id, false)` instead of directly disabling the target GameObject when the marker should fade away first. The target is disabled after the transition duration. Calling the wrapper with `true` cancels a pending disable, activates the target, and fades the marker back in.
- Animations use unscaled time by default, so they continue while `Time.timeScale` is zero. Turn off **Use Unscaled Time** to use regular gameplay time.

### Landmark setup

For each location, enable **Is Landmark**. The component creates an additional UI square above the line, rotated 45 degrees. Set that entry's **Landmark Diamond Size** and **Landmark Y Offset** independently. The diamond follows the same edge fade, proximity fade, and distance-based scale as its location marker. A landmark can be shown even if its icon Sprite is left empty.

### Line-end decorations

The compass builds two procedural equilateral triangle sprites at runtime. The left triangle points right and the right triangle points left, so both point inward along the line. Their tint matches `Background Color`, and the geometry overlaps the line by design so no seam is visible. Use `Show Edge Triangles` to hide both or `Edge Triangle Size` to resize them. They remain enabled with either the generated line or a custom background sprite.

### Distance-based size defaults

- At or below **150 m**, the marker uses its intended size (scale 1).
- Between **150 m** and **700 m**, the size interpolates linearly toward the minimum scale.
- At or beyond **700 m**, the marker stays at the configured minimum scale (default **0.5**).
- Set **Minimum Distance Scale** between 0.01 and 1.0. A value of 1 disables distance shrinking.

## Important coordinate assumptions

- **North is world +Z** and **East is world +X**. Set `North Yaw Offset` if your level uses another world orientation.
- The distance filter assumes **1 Unity unit = 1 metre**. Set `Minimum Visible Distance` to change the 100 m threshold.
- Proximity filtering and distance-based sizing always use horizontal XZ distance, ignoring height differences.
- `Visible Arc Degrees = 180` means 90 degrees to the left and right of the heading source. Use 360 to show the entire compass at once.
- The script uses UI units. With the default CanvasScaler on the menu-created Canvas, these are scaled against a 1920 × 1080 reference resolution.

## Layout controls

- `Compass Width`: horizontal strip width; default 256.
- `Background Height`: line/custom image height; default 2.
- `Show Edge Triangles` and `Edge Triangle Size`: control the inward-pointing equilateral decorations at both line ends; default size is 12.
- `Marker Y Offset`: vertical offset of location icons from the line; positive is upward.
- `Cardinal Label Y Offset`: cardinal-label height.
- `Tick Y Offset`, `Tick Length`, and `Degree Label Y Offset`: degree scale layout below the line.
- `Degree Step`: tick interval in degrees; default 45.

The UI is generated when the component enters Play Mode, so the visual elements are runtime-generated rather than shown as a static prefab preview in Edit Mode.

## Updating from version 1.0.3

Replace the entire old `Packages/com.example.compass-line` folder with the folder from version 1.0.4. This update fixes repeated fade restarts and opacity jumps when a marker crosses the visible-arc boundary. It adds a configurable `Outside Arc Hysteresis Degrees` setting (default 2°), keeps the marker inside the mask while fading, continues the same edge-alpha fade slightly beyond the nominal arc, and avoids toggling the underlying UI Graphics at near-zero edge alpha. No changes are required to existing `CompassLocation` runtime calls.

## Updating from version 1.0.0

Version 1.0.0 did not include the assembly definition files required for scripts stored under `Packages/`. To update, replace the entire old `Packages/com.example.compass-line` folder with the folder from version 1.0.4 (do not copy only the `.cs` files). The new package includes:

- `Runtime/com.example.compass-line.Runtime.asmdef`, which compiles the `CompassLineUI` component and references `UnityEngine.UI`.
- `Editor/com.example.compass-line.Editor.asmdef`, which is Editor-only and explicitly references the Runtime assembly.
- A menu script that uses `FindAnyObjectByType<Canvas>()` and references `global::CompassLine.CompassLineUI`.

After replacing the folder, return to Unity and allow the package to recompile. The menu item is **GameObject → UI → Compass Line**.

## Troubleshooting: menu item cannot find CompassLineUI

If Unity reports `CS0246` for `CompassLineUI`, confirm that both `.asmdef` files above exist in their exact folders and that the complete package folder was replaced. In the Inspector, the Runtime assembly must compile successfully before the Editor menu assembly can reference it. Also confirm that `com.unity.ugui` is installed/resolved in Package Manager.
