# Compass Line UI

A drop-in horizontal compass strip for **Unity 6.5 / 6000.5.x** (package version **1.0.1**), built with the built-in **uGUI** system. It uses only `UnityEngine` and `UnityEngine.UI`; it does not require TextMeshPro, the Input System, or third-party assets.

## Features

- Horizontal compass strip with configurable width (default **256 UI units**).
- Uses a generated, plain white **256 × 2 px line** when no background sprite is assigned. Assign any Sprite to use your own background.
- Portuguese cardinal abbreviations **N / L / S / O**, or English **N / E / S / W**. Optional full names are available.
- Transform-based location markers with an individual icon Sprite and active/inactive flag.
- Hides a location when it is closer than **100 metres** by default, when its target is inactive, when its `isActive` flag is false, or when it falls outside the visible compass arc.
- Fades labels, degree ticks, and location icons as they approach either strip edge.
- Degree ticks and labels default to **0, 45, 90, ... 315** below the line.
- Exposes `SetLocationActive()` and `RefreshLocations()` for runtime control.
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
   - `isActive`: turn off to hide the marker.
5. Choose **Language** (`Portuguese` or `English`). `Show Full Cardinal Names` changes the abbreviations into Norte/Leste/Sul/Oeste or North/East/South/West.
6. Assign a custom **Background Sprite** to replace the default line. Adjust `Background Height` to suit that image.
7. Adjust **Visible Arc Degrees**, **Marker Y Offset**, **Marker Size**, and **Edge Fade Start** to match the HUD.

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

Changes to an existing entry's `isActive` value are picked up automatically. `AddLocation()` and `RemoveLocation()` manage runtime markers and refresh the marker views; call `RefreshLocations()` after directly editing the serialized list at runtime.

## Important coordinate assumptions

- **North is world +Z** and **East is world +X**. Set `North Yaw Offset` if your level uses another world orientation.
- The distance filter assumes **1 Unity unit = 1 metre**. Set `Minimum Visible Distance` to change the 100 m threshold.
- By default, distance is measured on the horizontal XZ plane, ignoring height differences. Disable `Use Horizontal Distance` to use full 3D distance.
- `Visible Arc Degrees = 180` means 90 degrees to the left and right of the heading source. Use 360 to show the entire compass at once.
- The script uses UI units. With the default CanvasScaler on the menu-created Canvas, these are scaled against a 1920 × 1080 reference resolution.

## Layout controls

- `Compass Width`: horizontal strip width; default 256.
- `Background Height`: line/custom image height; default 2.
- `Marker Y Offset`: vertical offset of location icons from the line; positive is upward.
- `Cardinal Label Y Offset`: cardinal-label height.
- `Tick Y Offset`, `Tick Length`, and `Degree Label Y Offset`: degree scale layout below the line.
- `Degree Step`: tick interval in degrees; default 45.

The UI is generated when the component enters Play Mode, so the visual elements are runtime-generated rather than shown as a static prefab preview in Edit Mode.

## Updating from version 1.0.0

Version 1.0.0 did not include the assembly definition files required for scripts stored under `Packages/`. To update, replace the entire old `Packages/com.example.compass-line` folder with the folder from version 1.0.1 (do not copy only the `.cs` files). The new package includes:

- `Runtime/com.example.compass-line.Runtime.asmdef`, which compiles the `CompassLineUI` component and references `UnityEngine.UI`.
- `Editor/com.example.compass-line.Editor.asmdef`, which is Editor-only and explicitly references the Runtime assembly.
- A menu script that uses `FindAnyObjectByType<Canvas>()` and references `global::CompassLine.CompassLineUI`.

After replacing the folder, return to Unity and allow the package to recompile. The menu item is **GameObject → UI → Compass Line**.

## Troubleshooting: menu item cannot find CompassLineUI

If Unity reports `CS0246` for `CompassLineUI`, confirm that both `.asmdef` files above exist in their exact folders and that the complete package folder was replaced. In the Inspector, the Runtime assembly must compile successfully before the Editor menu assembly can reference it. Also confirm that `com.unity.ugui` is installed/resolved in Package Manager.
