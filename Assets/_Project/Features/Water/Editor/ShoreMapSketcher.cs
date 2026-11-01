#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Small top-down paint tool for creating a black-land / white-water mask
/// consumed by ShoreDistanceFieldBaker. Brush size is expressed in world metres.
/// </summary>
public sealed class ShoreMapSketcher : EditorWindow
{
    public const string PrefMaskPath = "CoastalBreakers.ShoreMapSketcher.LastMaskPath";
    public const string PrefOriginX = "CoastalBreakers.ShoreMapSketcher.OriginX";
    public const string PrefOriginZ = "CoastalBreakers.ShoreMapSketcher.OriginZ";
    public const string PrefSizeX = "CoastalBreakers.ShoreMapSketcher.SizeX";
    public const string PrefSizeZ = "CoastalBreakers.ShoreMapSketcher.SizeZ";
    public const string PrefLandThreshold = "CoastalBreakers.ShoreMapSketcher.LandThreshold";

    private enum PaintMode { Land, ShallowShelf, DeepWater }

    [SerializeField] private int resolution = 512;
    [SerializeField] private Vector2 worldOriginXZ = Vector2.zero;
    [SerializeField] private Vector2 worldSizeXZ = new Vector2(8192f, 8192f);
    [SerializeField] private float brushRadiusMeters = 90f;
    [SerializeField, Range(0f, 0.98f)] private float brushHardness = 0.72f;
    [SerializeField, Range(0.01f, 1f)] private float brushOpacity = 1f;
    [SerializeField, Range(0.01f, 0.99f)] private float landThreshold = 0.5f;
    [SerializeField, Range(0.02f, 1f)] private float shallowShelfDepthFraction = 0.15f;
    [SerializeField] private PaintMode paintMode = PaintMode.Land;
    [SerializeField] private Texture2D sourceMask;

    private Texture2D workingTexture;
    private Color32[] pixels;
    private bool isPainting;
    private byte activePaintValue;
    private bool hasUnsavedChanges;

    [MenuItem("Tools/Water/Sketch Shore Map")]
    public static void OpenWindow()
    {
        ShoreMapSketcher window = GetWindow<ShoreMapSketcher>("Shore Map Sketcher");
        window.minSize = new Vector2(380f, 560f);
        window.Show();
    }

    private void OnEnable()
    {
        LoadLastMapping();
        EnsureWorkingTexture();
        if (sourceMask != null && sourceMask.isReadable)
            LoadSourceMask();
    }

    private void OnDisable()
    {
        isPainting = false;
        if (workingTexture != null)
            DestroyImmediate(workingTexture);
        workingTexture = null;
        pixels = null;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Sketch a top-down island / coast mask", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Sketch coastlines and a shallow-water shelf: black = land, dark gray = shallow, " +
            "white = deep. The baker creates both a shoreline distance field and a painted " +
            "seabed-depth modifier. Top-down mapping: left/right = world X, bottom/top = world Z. " +
            "Brush radius is measured in world metres.",
            MessageType.Info);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Existing Sketch", EditorStyles.boldLabel);
        sourceMask = (Texture2D)EditorGUILayout.ObjectField(
            new GUIContent("Source Map", "Optional existing grayscale map: black land, gray shallow shelves, white deep water."),
            sourceMask, typeof(Texture2D), false);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Load Source Mask"))
                LoadSourceMask();
            if (GUILayout.Button("Use Blank Map"))
                Fill(255);
        }
        if (sourceMask != null && !sourceMask.isReadable)
            EditorGUILayout.HelpBox("Source mask is not readable. Enable Read/Write in its Texture Import Settings to edit it.", MessageType.Warning);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Map Dimensions", EditorStyles.boldLabel);
        int nextResolution = EditorGUILayout.IntPopup(
            "Texture Resolution", resolution,
            new[] { "256", "512", "1024", "2048" },
            new[] { 256, 512, 1024, 2048 });
        if (nextResolution != resolution)
        {
            if (hasUnsavedChanges && !EditorUtility.DisplayDialog(
                    "Resize Shore Map?",
                    "Resizing clears the current sketch. Continue?",
                    "Resize", "Cancel"))
            {
                nextResolution = resolution;
            }
            else
            {
                resolution = nextResolution;
                CreateWorkingTexture(fillWhite: true);
                hasUnsavedChanges = true;
            }
        }

        worldOriginXZ = EditorGUILayout.Vector2Field(
            new GUIContent("World Origin XZ", "Bottom-left corner of the map in world coordinates."),
            worldOriginXZ);
        worldSizeXZ = EditorGUILayout.Vector2Field(
            new GUIContent("World Size XZ", "Physical map width (X) and depth (Z), in metres."),
            worldSizeXZ);
        worldSizeXZ.x = Mathf.Max(1f, worldSizeXZ.x);
        worldSizeXZ.y = Mathf.Max(1f, worldSizeXZ.y);
        landThreshold = EditorGUILayout.Slider(
            new GUIContent("Land / Water Threshold", "Must match the Baker's Black / Land Sensitivity. The Baker loads this value automatically after saving the sketch."),
            landThreshold, 0.01f, 0.99f);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
        paintMode = (PaintMode)GUILayout.Toolbar((int)paintMode,
            new[] { "Land / Coast", "Shallow Shelf", "Deep Water" });
        if (paintMode == PaintMode.ShallowShelf)
        {
            shallowShelfDepthFraction = EditorGUILayout.Slider(
                new GUIContent("Shelf Depth (% of Max)", "Painted shallow-water depth as a fraction of the Baker's Maximum Inferred Water Depth. Example: 15% of a 60 m maximum gives about 9 m depth."),
                shallowShelfDepthFraction, 0.02f, 1f);
        }
        brushRadiusMeters = EditorGUILayout.Slider(
            new GUIContent("Brush Radius (m)", "Actual world-space brush radius, independent of map resolution."),
            brushRadiusMeters, 1f, Mathf.Max(2f, Mathf.Min(worldSizeXZ.x, worldSizeXZ.y) * 0.25f));
        brushHardness = EditorGUILayout.Slider(
            new GUIContent("Brush Hardness", "0 = very soft edge, 1 = hard edge."),
            brushHardness, 0f, 0.98f);
        brushOpacity = EditorGUILayout.Slider("Brush Opacity", brushOpacity, 0.01f, 1f);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Fill Deep Water"))
                Fill(255);
            if (GUILayout.Button("Fill Shallow"))
                Fill(GetPaintValue(PaintMode.ShallowShelf));
            if (GUILayout.Button("Fill Land"))
                Fill(0);
            if (GUILayout.Button("Clear / New Map"))
            {
                if (!hasUnsavedChanges || EditorUtility.DisplayDialog(
                        "Clear Shore Map?", "This will erase the current sketch.", "Clear", "Cancel"))
                    Fill(255);
            }
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Map Canvas", EditorStyles.boldLabel);
        float canvasWidth = Mathf.Clamp(position.width - 24f, 300f, 560f);
        float worldAspect = Mathf.Clamp(worldSizeXZ.x / Mathf.Max(worldSizeXZ.y, 1f), 0.55f, 1.8f);
        float canvasHeight = canvasWidth / worldAspect;
        if (canvasHeight > 520f)
        {
            canvasHeight = 520f;
            canvasWidth = canvasHeight * worldAspect;
        }
        Rect canvasRect = GUILayoutUtility.GetRect(
            canvasWidth, canvasHeight,
            GUILayout.Width(canvasWidth), GUILayout.Height(canvasHeight));

        if (workingTexture != null)
            GUI.DrawTexture(canvasRect, workingTexture, ScaleMode.StretchToFill, false);
        else
            EditorGUI.DrawRect(canvasRect, Color.white);

        Handles.BeginGUI();
        Handles.color = new Color(0f, 0.65f, 1f, 0.9f);
        Handles.DrawAAPolyLine(2f,
            new Vector3(canvasRect.xMin, canvasRect.yMin),
            new Vector3(canvasRect.xMax, canvasRect.yMin),
            new Vector3(canvasRect.xMax, canvasRect.yMax),
            new Vector3(canvasRect.xMin, canvasRect.yMax),
            new Vector3(canvasRect.xMin, canvasRect.yMin));
        Handles.EndGUI();

        HandleCanvasInput(canvasRect);

        EditorGUILayout.HelpBox(
            "Black = land, dark gray = shallow shelf, white = deep water. Left mouse paints " +
            "the selected mode; right mouse temporarily paints deep water. Mouse wheel " +
            "changes brush radius. The canvas bottom edge represents the " +
            "world's negative-Z side; top edge represents positive Z.",
            MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = workingTexture != null;
            if (GUILayout.Button("Save Mask to Assets", GUILayout.Height(32f)))
                SaveMask();
            GUI.enabled = true;
        }

        if (hasUnsavedChanges)
            EditorGUILayout.HelpBox("Unsaved sketch changes.", MessageType.Warning);
    }

    private void HandleCanvasInput(Rect canvasRect)
    {
        Event e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.ScrollWheel && canvasRect.Contains(e.mousePosition))
        {
            brushRadiusMeters = Mathf.Clamp(
                brushRadiusMeters * (1f - e.delta.y * 0.06f),
                1f,
                Mathf.Max(2f, Mathf.Min(worldSizeXZ.x, worldSizeXZ.y) * 0.25f));
            Repaint();
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDown && canvasRect.Contains(e.mousePosition) &&
            (e.button == 0 || e.button == 1))
        {
            isPainting = true;
            activePaintValue = e.button == 1 ? (byte)255 : GetPaintValue(paintMode);
            PaintAt(e.mousePosition, canvasRect, activePaintValue);
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && isPainting)
        {
            PaintAt(e.mousePosition, canvasRect, activePaintValue);
            e.Use();
        }
        else if (e.type == EventType.MouseUp && isPainting)
        {
            isPainting = false;
            e.Use();
        }
        else if (e.type == EventType.MouseLeaveWindow)
        {
            isPainting = false;
        }
    }

    private byte GetPaintValue(PaintMode mode)
    {
        switch (mode)
        {
            case PaintMode.Land:
                return 0;
            case PaintMode.ShallowShelf:
            {
                // Grayscale above the land threshold encodes a fraction of the
                // Baker's maximum inferred water depth. This keeps the sketch
                // controls understandable in metres / percentages rather than
                // requiring the artist to guess an arbitrary gray value.
                float gray = landThreshold + shallowShelfDepthFraction * (1f - landThreshold);
                return (byte)Mathf.Clamp(Mathf.RoundToInt(gray * 255f), 0, 255);
            }
            default:
                return 255;
        }
    }

    private void PaintAt(Vector2 mouse, Rect rect, byte target)
    {
        if (pixels == null || workingTexture == null || !rect.Contains(mouse))
            return;

        // A circular brush in metres becomes an ellipse in pixels when the map's
        // world X/Z aspect ratio differs from its texture aspect ratio.
        float nx = Mathf.Clamp01((mouse.x - rect.xMin) / rect.width);
        float nz = Mathf.Clamp01((rect.yMax - mouse.y) / rect.height);
        int centerX = Mathf.Clamp(Mathf.RoundToInt(nx * (resolution - 1)), 0, resolution - 1);
        int centerY = Mathf.Clamp(Mathf.RoundToInt(nz * (resolution - 1)), 0, resolution - 1);

        float radiusX = Mathf.Max(1f, brushRadiusMeters / worldSizeXZ.x * resolution);
        float radiusY = Mathf.Max(1f, brushRadiusMeters / worldSizeXZ.y * resolution);
        int minX = Mathf.Max(0, Mathf.FloorToInt(centerX - radiusX));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt(centerX + radiusX));
        int minY = Mathf.Max(0, Mathf.FloorToInt(centerY - radiusY));
        int maxY = Mathf.Min(resolution - 1, Mathf.CeilToInt(centerY + radiusY));
        for (int y = minY; y <= maxY; y++)
        {
            float dy = (y - centerY) / radiusY;
            for (int x = minX; x <= maxX; x++)
            {
                float dx = (x - centerX) / radiusX;
                float radial = Mathf.Sqrt(dx * dx + dy * dy);
                if (radial >= 1f)
                    continue;

                float falloff = 1f - Mathf.SmoothStep(brushHardness, 1f, radial);
                float alpha = falloff * brushOpacity;
                int index = y * resolution + x;
                byte oldValue = pixels[index].r;
                byte value = (byte)Mathf.Clamp(
                    Mathf.RoundToInt(Mathf.Lerp(oldValue, target, alpha)), 0, 255);
                pixels[index] = new Color32(value, value, value, 255);
            }
        }

        workingTexture.SetPixels32(pixels);
        workingTexture.Apply(false, false);
        hasUnsavedChanges = true;
        Repaint();
    }

    private void LoadSourceMask()
    {
        if (sourceMask == null)
        {
            EditorUtility.DisplayDialog("Load Shore Map", "Assign a readable source mask first.", "OK");
            return;
        }
        if (!sourceMask.isReadable)
        {
            EditorUtility.DisplayDialog(
                "Load Shore Map",
                "The source mask is not readable. Select it in the Project window, enable Read/Write, click Apply, then load it again.",
                "OK");
            return;
        }

        try
        {
            Color32[] sourcePixels = sourceMask.GetPixels32();
            int sourceWidth = sourceMask.width;
            int sourceHeight = sourceMask.height;
            if (sourcePixels == null || sourcePixels.Length != sourceWidth * sourceHeight)
                throw new InvalidOperationException("Source mask pixel data is invalid.");

            EnsureWorkingTexture();
            for (int y = 0; y < resolution; y++)
            {
                int sourceY = Mathf.Clamp(
                    Mathf.RoundToInt((y / (float)(resolution - 1)) * (sourceHeight - 1)),
                    0, sourceHeight - 1);
                for (int x = 0; x < resolution; x++)
                {
                    int sourceX = Mathf.Clamp(
                        Mathf.RoundToInt((x / (float)(resolution - 1)) * (sourceWidth - 1)),
                        0, sourceWidth - 1);
                    Color32 color = sourcePixels[sourceY * sourceWidth + sourceX];
                    byte gray = (byte)Mathf.RoundToInt(
                        (color.r + color.g + color.b) / 3f);
                    pixels[y * resolution + x] = new Color32(gray, gray, gray, 255);
                }
            }

            workingTexture.SetPixels32(pixels);
            workingTexture.Apply(false, false);
            hasUnsavedChanges = false;
            Repaint();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex, this);
            EditorUtility.DisplayDialog("Load Shore Map Failed", ex.Message, "OK");
        }
    }

    private void Fill(byte value)
    {
        EnsureWorkingTexture();
        Color32 color = new Color32(value, value, value, 255);
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = color;
        workingTexture.SetPixels32(pixels);
        workingTexture.Apply(false, false);
        hasUnsavedChanges = true;
    }

    private void EnsureWorkingTexture()
    {
        if (workingTexture != null && pixels != null)
            return;
        CreateWorkingTexture(fillWhite: true);
    }

    private void CreateWorkingTexture(bool fillWhite)
    {
        if (workingTexture != null)
            DestroyImmediate(workingTexture);

        resolution = Mathf.Clamp(resolution, 256, 2048);
        workingTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
        {
            name = "ShoreMapSketch_Working",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        pixels = new Color32[resolution * resolution];
        Color32 initial = fillWhite
            ? new Color32(255, 255, 255, 255)
            : new Color32(0, 0, 0, 255);
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = initial;
        workingTexture.SetPixels32(pixels);
        workingTexture.Apply(false, false);
    }

    private void SaveMask()
    {
        EnsureWorkingTexture();
        string assetPath = EditorUtility.SaveFilePanelInProject(
            "Save Shoreline Sketch",
            sourceMask != null ? sourceMask.name : "ShorelineMask",
            "png",
            "Save the black-land / white-water mask inside the project's Assets folder.");
        if (string.IsNullOrEmpty(assetPath))
            return;

        string absolutePath = null;
        try
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            absolutePath = Path.GetFullPath(Path.Combine(
                projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
            string assetsRoot = Path.GetFullPath(Application.dataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string prefix = assetsRoot + Path.DirectorySeparatorChar;
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!absolutePath.StartsWith(prefix, comparison))
                throw new InvalidOperationException("Selected path is outside this project's Assets folder: " + absolutePath);

            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            workingTexture.SetPixels32(pixels);
            workingTexture.Apply(false, false);
            byte[] bytes = workingTexture.EncodeToPNG();
            if (bytes == null || bytes.Length == 0)
                throw new IOException("Could not encode shoreline sketch as PNG.");
            File.WriteAllBytes(absolutePath, bytes);
            if (!File.Exists(absolutePath) || new FileInfo(absolutePath).Length != bytes.Length)
                throw new IOException("Shoreline PNG was not written correctly: " + absolutePath);

            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Unity did not create a TextureImporter for: " + assetPath);

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.isReadable = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, resolution);
            importer.SaveAndReimport();

            Texture2D saved = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (saved == null || saved.width != resolution || saved.height != resolution)
                throw new InvalidOperationException("The saved sketch did not import at the requested resolution.");

            sourceMask = saved;
            EditorPrefs.SetString(PrefMaskPath, assetPath);
            EditorPrefs.SetFloat(PrefOriginX, worldOriginXZ.x);
            EditorPrefs.SetFloat(PrefOriginZ, worldOriginXZ.y);
            EditorPrefs.SetFloat(PrefSizeX, worldSizeXZ.x);
            EditorPrefs.SetFloat(PrefSizeZ, worldSizeXZ.y);
            EditorPrefs.SetFloat(PrefLandThreshold, landThreshold);

            hasUnsavedChanges = false;
            EditorGUIUtility.PingObject(saved);
            Selection.activeObject = saved;
            Debug.Log(
                "Shore map sketch saved.\nAsset: " + assetPath +
                "\nResolution: " + resolution + " x " + resolution +
                "\nWorld origin XZ: " + worldOriginXZ +
                "\nWorld size XZ: " + worldSizeXZ +
                "\nNext: open Tools > Water > Bake Shore Distance Field.", saved);
        }
        catch (Exception ex)
        {
            Debug.LogError(
                "ShoreMapSketcher failed to save.\nAsset: " + assetPath +
                "\nFile: " + (absolutePath ?? "<unresolved>") +
                "\n" + ex, this);
            EditorUtility.DisplayDialog(
                "Shore Map Save Failed",
                ex.GetType().Name + ": " + ex.Message + "\n\nSee the Console for details.",
                "OK");
        }
    }

    private void LoadLastMapping()
    {
        string lastMask = EditorPrefs.GetString(PrefMaskPath, string.Empty);
        if (!string.IsNullOrEmpty(lastMask))
        {
            sourceMask = AssetDatabase.LoadAssetAtPath<Texture2D>(lastMask);
            worldOriginXZ = new Vector2(
                EditorPrefs.GetFloat(PrefOriginX, 0f),
                EditorPrefs.GetFloat(PrefOriginZ, 0f));
            worldSizeXZ = new Vector2(
                Mathf.Max(1f, EditorPrefs.GetFloat(PrefSizeX, 8192f)),
                Mathf.Max(1f, EditorPrefs.GetFloat(PrefSizeZ, 8192f)));
            landThreshold = Mathf.Clamp(
                EditorPrefs.GetFloat(PrefLandThreshold, 0.5f), 0.01f, 0.99f);
        }
    }
}
#endif
