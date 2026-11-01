#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Converts a top-down grayscale coast sketch into a normalized world-space
/// shoreline distance field and a painted seabed-depth modifier.
/// </summary>
public sealed class ShoreDistanceFieldBaker : EditorWindow
{
    private const long LargeMaskPixelCount = 8_388_608L;
    private const int MaximumMaskDimension = 16384;

    private Texture2D islandMask;
    private SpectralOceanFFT targetOcean;
    private Vector2 worldOriginXZ = Vector2.zero;
    private Vector2 worldSize = new Vector2(8192f, 8192f);
    private float attenuationDistance = 150f;
    [SerializeField] private float maxInferredDepthMeters = 60f;
    private float landThreshold = 0.5f;

    [MenuItem("Tools/Water/Bake Shore Distance Field")]
    private static void Open()
    {
        GetWindow<ShoreDistanceFieldBaker>("Shore Distance Baker");
    }

    private void OnEnable()
    {
        // Reuse the same land threshold the sketcher used when painting the map.
        landThreshold = Mathf.Clamp(
            EditorPrefs.GetFloat(ShoreMapSketcher.PrefLandThreshold, landThreshold),
            0.01f, 0.99f);

        // Reuse the last sketch and its world mapping so artists do not need to
        // enter the same map dimensions twice after sketching an island layout.
        string lastMask = EditorPrefs.GetString(ShoreMapSketcher.PrefMaskPath, string.Empty);
        if (!string.IsNullOrEmpty(lastMask))
        {
            Texture2D lastTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(lastMask);
            if (lastTexture != null)
                islandMask = lastTexture;

            worldOriginXZ = new Vector2(
                EditorPrefs.GetFloat(ShoreMapSketcher.PrefOriginX, worldOriginXZ.x),
                EditorPrefs.GetFloat(ShoreMapSketcher.PrefOriginZ, worldOriginXZ.y));
            worldSize = new Vector2(
                Mathf.Max(1f, EditorPrefs.GetFloat(ShoreMapSketcher.PrefSizeX, worldSize.x)),
                Mathf.Max(1f, EditorPrefs.GetFloat(ShoreMapSketcher.PrefSizeZ, worldSize.y)));
        }
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Sketch / Edit Map", GUILayout.Height(26f)))
                ShoreMapSketcher.OpenWindow();
            if (GUILayout.Button("Reload Last Sketch", GUILayout.Height(26f), GUILayout.Width(140f)))
                OnEnable();
        }

        EditorGUILayout.LabelField("Terrain Sketch", EditorStyles.boldLabel);
        islandMask = (Texture2D)EditorGUILayout.ObjectField(
            "Grayscale Coast / Seabed Sketch",
            islandMask,
            typeof(Texture2D),
            false);

        if (islandMask != null && !islandMask.isReadable)
        {
            EditorGUILayout.HelpBox(
                "The selected mask is not readable. Select it in the Project window, " +
                "enable Read/Write in its Texture Import Settings, click Apply, then bake again.",
                MessageType.Error);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("World Mapping", EditorStyles.boldLabel);
        targetOcean = (SpectralOceanFFT)EditorGUILayout.ObjectField(
            new GUIContent("Target Ocean (auto-assign)",
                "Optional. Assigns the baked texture and all matching world mapping values to this ocean component."),
            targetOcean,
            typeof(SpectralOceanFFT),
            true);
        worldOriginXZ = EditorGUILayout.Vector2Field("World Origin XZ (bottom-left)", worldOriginXZ);
        worldSize = EditorGUILayout.Vector2Field("World Size XZ", worldSize);
        attenuationDistance = EditorGUILayout.FloatField(
            "Coastal Fade Distance (m)", attenuationDistance);
        maxInferredDepthMeters = EditorGUILayout.FloatField(
            new GUIContent("Maximum Inferred Water Depth (m)",
                "Caps the default distance-based depth estimate. Painted gray shelf values multiply this depth."),
            maxInferredDepthMeters);
        maxInferredDepthMeters = Mathf.Max(1f, maxInferredDepthMeters);
        landThreshold = EditorGUILayout.Slider(
            new GUIContent("Black / Land Sensitivity",
                "Pixels darker than this grayscale threshold are classified as land. Raise it to include antialiased or dark-gray shoreline pixels, expanding the generated shore boundary and foam influence."),
            landThreshold, 0.01f, 0.99f);

        EditorGUILayout.HelpBox(
            "Use a top-down grayscale map covering the same area as the ocean. Paint land " +
            "black, shallow shelf regions dark gray, and deep water white. The Sketcher saves " +
            "its Land / Water Threshold and the Baker loads that value automatically. The baker writes " +
            "two textures: a normalized shoreline distance field and a seabed depth modifier. " +
            "Gray water values reduce the inferred water depth and make waves shoal/break sooner. On " +
            "SpectralOceanFFT, the texture is sampled in world XZ. World Origin XZ is the " +
            "bottom-left map corner; World Size XZ is its physical width/depth. If Target " +
            "Ocean is assigned, the baker writes the texture, origin, size and distance range " +
            "to that component automatically. Otherwise enter those same values manually. " +
            "Raise Black / Land Sensitivity to classify more dark-gray source pixels as land. " +
            "Set Maximum Inferred Water Depth to control the depth estimate for white/deep-water areas.",
            MessageType.Info);

        bool validWorldSize = IsFinite(worldSize.x) && IsFinite(worldSize.y) &&
                              worldSize.x > 0f && worldSize.y > 0f;
        bool validFade = IsFinite(attenuationDistance) && attenuationDistance > 0f;
        bool validDepth = IsFinite(maxInferredDepthMeters) && maxInferredDepthMeters > 0f;
        bool validMask = islandMask != null && islandMask.isReadable &&
                         islandMask.width > 0 && islandMask.height > 0 &&
                         islandMask.width <= MaximumMaskDimension &&
                         islandMask.height <= MaximumMaskDimension;

        using (new EditorGUI.DisabledScope(!validMask || !validWorldSize || !validFade || !validDepth))
        {
            if (GUILayout.Button("Bake Shore + Seabed Maps", GUILayout.Height(34f)))
                Bake();
        }
    }

    private void Bake()
    {
        if (islandMask == null)
        {
            ReportError("No island mask is assigned.");
            return;
        }

        if (!islandMask.isReadable)
        {
            ReportError(
                "The island mask is not readable. Select it in the Project window, " +
                "enable Read/Write in Texture Import Settings, click Apply, then bake again.");
            return;
        }

        if (!IsFinite(worldSize.x) || !IsFinite(worldSize.y) ||
            worldSize.x <= 0f || worldSize.y <= 0f ||
            !IsFinite(attenuationDistance) || attenuationDistance <= 0f ||
            !IsFinite(maxInferredDepthMeters) || maxInferredDepthMeters <= 0f)
        {
            ReportError("World Size XZ, Coastal Fade Distance and Maximum Inferred Water Depth must be finite positive values.");
            return;
        }

        int width = islandMask.width;
        int height = islandMask.height;
        if (width <= 0 || height <= 0 ||
            width > MaximumMaskDimension || height > MaximumMaskDimension)
        {
            ReportError("The mask dimensions are invalid or exceed 16384 pixels on one axis.");
            return;
        }

        long pixelCountLong = (long)width * height;
        if (pixelCountLong > int.MaxValue)
        {
            ReportError("The mask contains too many pixels for this baker.");
            return;
        }

        if (pixelCountLong > LargeMaskPixelCount &&
            !EditorUtility.DisplayDialog(
                "Large shoreline mask",
                "This mask has " + pixelCountLong.ToString("N0") +
                " pixels. Baking it can temporarily use several hundred megabytes of RAM. Continue?",
                "Continue", "Cancel"))
        {
            return;
        }

        string assetPath = EditorUtility.SaveFilePanelInProject(
            "Save Shore Distance Field",
            "ShoreDistanceField",
            "png",
            "Choose where to save the generated shoreline distance texture.");

        if (string.IsNullOrEmpty(assetPath))
            return;

        Texture2D result = null;
        Texture2D depthResult = null;
        string absolutePath = null;
        string depthAssetPath = null;
        string depthAbsolutePath = null;

        try
        {
            // SaveFilePanelInProject returns a project-relative path (Assets/...).
            // Resolve it from Application.dataPath, never from Environment.CurrentDirectory.
            absolutePath = ResolveProjectAssetPath(assetPath);
            string outputDirectory = Path.GetDirectoryName(absolutePath);
            if (string.IsNullOrEmpty(outputDirectory))
                throw new IOException("Could not resolve the output directory for: " + assetPath);

            Directory.CreateDirectory(outputDirectory);

            Color32[] source = islandMask.GetPixels32(0);
            int count = (int)pixelCountLong;
            bool[] land = new bool[count];
            Color32[] depthModifierPixels = new Color32[count];
            bool hasLand = false;
            bool hasWater = false;
            bool landTouchesBorder = false;

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int index = row + x;
                    float gray = (source[index].r + source[index].g + source[index].b) /
                                 (3f * 255f);
                    bool isLand = gray < landThreshold;
                    land[index] = isLand;

                    // Source grayscale above the land threshold paints seabed depth:
                    // threshold = shallow, white = deep. For a plain black/white map,
                    // every water pixel stays at 1 and the shader uses inferred depth.
                    float paintedDepth = isLand
                        ? 0f
                        : Mathf.Clamp01((gray - landThreshold) / Mathf.Max(1f - landThreshold, 0.001f));
                    byte depthByte = (byte)Mathf.RoundToInt(paintedDepth * 255f);
                    depthModifierPixels[index] = new Color32(depthByte, depthByte, depthByte, 255);

                    hasLand |= isLand;
                    hasWater |= !isLand;

                    if (isLand && (x == 0 || y == 0 || x == width - 1 || y == height - 1))
                        landTouchesBorder = true;
                }
            }

            if (!hasLand)
            {
                Debug.LogWarning(
                    "ShoreDistanceFieldBaker: No land pixels were detected. The output will be " +
                    "entirely white, so it will not create coastal breakers. Check mask colors and Land Threshold.",
                    this);
            }
            else if (!hasWater)
            {
                Debug.LogWarning(
                    "ShoreDistanceFieldBaker: Every pixel was classified as land. The output will be " +
                    "entirely black. Check mask colors and Land Threshold.",
                    this);
            }

            if (landTouchesBorder)
            {
                Debug.LogWarning(
                    "ShoreDistanceFieldBaker: Land touches the edge of the mask. The runtime shader " +
                    "treats positions outside this map as deep water, so this can create an abrupt " +
                    "shore-distance discontinuity at the map boundary. Include a margin of water " +
                    "around islands when possible.",
                    this);
            }

            Color32[] output = new Color32[count];
            if (!hasLand)
            {
                for (int i = 0; i < count; i++)
                    output[i] = new Color32(255, 255, 255, 255);
            }
            else
            {
                float[] horizontal = new float[count];
                int maxDimension = Mathf.Max(width, height);
                float[] f = new float[maxDimension];
                float[] d = new float[maxDimension];
                int[] v = new int[maxDimension];
                float[] z = new float[maxDimension + 1];

                // First transform along world X for each image row.
                float xStep = worldSize.x / width;
                for (int y = 0; y < height; y++)
                {
                    int row = y * width;
                    for (int x = 0; x < width; x++)
                        f[x] = land[row + x] ? 0f : float.PositiveInfinity;

                    DistanceTransform1D(f, d, width, xStep, v, z);
                    for (int x = 0; x < width; x++)
                        horizontal[row + x] = d[x];
                }

                // Then transform along world Z for each image column.
                float zStep = worldSize.y / height;
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                        f[y] = horizontal[y * width + x];

                    DistanceTransform1D(f, d, height, zStep, v, z);
                    for (int y = 0; y < height; y++)
                    {
                        int index = y * width + x;
                        float distance = Mathf.Sqrt(Mathf.Max(0f, d[y]));
                        float normalized = land[index]
                            ? 0f
                            : Mathf.Clamp01(distance / attenuationDistance);
                        byte value = (byte)Mathf.RoundToInt(normalized * 255f);
                        output[index] = new Color32(value, value, value, 255);
                    }
                }
            }

            result = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = Path.GetFileNameWithoutExtension(assetPath)
            };
            result.SetPixels32(output);
            result.Apply(false, false);

            byte[] pngBytes = result.EncodeToPNG();
            if (pngBytes == null || pngBytes.Length == 0)
                throw new IOException("Texture2D.EncodeToPNG returned no image data.");

            File.WriteAllBytes(absolutePath, pngBytes);
            var writtenFile = new FileInfo(absolutePath);
            if (!writtenFile.Exists || writtenFile.Length != pngBytes.Length)
                throw new IOException("The PNG was not written completely to: " + absolutePath);

            // Force this exact asset to import synchronously before accessing its importer.
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException(
                    "The PNG exists on disk but Unity did not create a TextureImporter for '" +
                    assetPath + "'. Check the Unity Console for import errors.");

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;

            // Prevent Unity's default 2048 max-size setting from silently reducing
            // the baked distance field when a larger source mask is used.
            int desiredMaxSize = Mathf.Clamp(
                Mathf.NextPowerOfTwo(Mathf.Max(width, height)), 32, MaximumMaskDimension);
            importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, desiredMaxSize);
            importer.SaveAndReimport();

            Texture2D baked = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (baked == null)
                throw new InvalidOperationException(
                    "The PNG was written, but AssetDatabase.LoadAssetAtPath returned null for '" +
                    assetPath + "'. Check the Unity Console for texture import errors.");

            if (baked.width != width || baked.height != height)
                throw new InvalidOperationException(
                    "Unity imported the distance field at " + baked.width + "x" + baked.height +
                    " instead of the source mask size " + width + "x" + height +
                    ". Check the platform texture-size override.");

            // Write a second, artist-painted depth modifier beside the distance map.
            // The shader multiplies its distance-derived seabed depth by this texture.
            string outputDirectoryAsset = Path.GetDirectoryName(assetPath);
            string depthName = Path.GetFileNameWithoutExtension(assetPath) + "_DepthModifier.png";
            depthAssetPath = string.IsNullOrEmpty(outputDirectoryAsset)
                ? depthName
                : outputDirectoryAsset.Replace('\\', '/') + "/" + depthName;
            depthAbsolutePath = ResolveProjectAssetPath(depthAssetPath);
            depthResult = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = Path.GetFileNameWithoutExtension(depthAssetPath)
            };
            depthResult.SetPixels32(depthModifierPixels);
            depthResult.Apply(false, false);
            byte[] depthPngBytes = depthResult.EncodeToPNG();
            if (depthPngBytes == null || depthPngBytes.Length == 0)
                throw new IOException("Could not encode the painted seabed depth modifier as PNG.");
            File.WriteAllBytes(depthAbsolutePath, depthPngBytes);
            var writtenDepthFile = new FileInfo(depthAbsolutePath);
            if (!writtenDepthFile.Exists || writtenDepthFile.Length != depthPngBytes.Length)
                throw new IOException("The seabed depth modifier was not written completely: " + depthAbsolutePath);

            AssetDatabase.ImportAsset(
                depthAssetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            TextureImporter depthImporter = AssetImporter.GetAtPath(depthAssetPath) as TextureImporter;
            if (depthImporter == null)
                throw new InvalidOperationException(
                    "Unity did not create a TextureImporter for the seabed depth modifier: " + depthAssetPath);
            ConfigureDataTextureImporter(depthImporter, width, height);

            Texture2D bakedDepth = AssetDatabase.LoadAssetAtPath<Texture2D>(depthAssetPath);
            if (bakedDepth == null || bakedDepth.width != width || bakedDepth.height != height)
                throw new InvalidOperationException(
                    "The painted seabed depth modifier failed to import at the expected resolution: " + depthAssetPath);

            bool assignedToOcean = AssignBakedMapToTargetOcean(baked, bakedDepth);

            EditorGUIUtility.PingObject(baked);
            Selection.activeObject = baked;

            Debug.Log(
                "Shore distance field and seabed modifier baked successfully.\nDistance asset: " + assetPath +
                "\nFile: " + absolutePath +
                "\nResolution: " + width + " x " + height +
                "\nCoastal fade distance: " + attenuationDistance + " m" +
                "\nWorld origin XZ: " + worldOriginXZ +
                "\nWorld size XZ: " + worldSize +
                "\nDepth modifier: " + depthAssetPath +
                "\nMaximum inferred depth: " + maxInferredDepthMeters + " m" +
                (assignedToOcean
                    ? "\nAssigned distance/depth textures and mapping values to: " + targetOcean.name
                    : "\nNo Target Ocean was assigned; manually set Shore Distance Texture, " +
                      "Painted Seabed Depth Modifier, Maximum Inferred Water Depth, Shore Map Origin XZ, " +
                      "Shore Map Size XZ and Shore Distance Range Meters."),
                baked);
        }
        catch (Exception exception)
        {
            string details = "Shore distance field bake failed.\nAsset path: " + assetPath +
                             "\nResolved file path: " + (absolutePath ?? "<not resolved>") +
                             "\nDepth modifier asset path: " + (depthAssetPath ?? "<not created>") +
                             "\nDepth modifier file path: " + (depthAbsolutePath ?? "<not resolved>") +
                             "\nReason: " + exception;
            Debug.LogError(details, this);
            EditorUtility.DisplayDialog(
                "Shore Distance Bake Failed",
                exception.GetType().Name + ": " + exception.Message +
                "\n\nSee the Unity Console for the full error and stack trace.",
                "OK");
        }
        finally
        {
            if (result != null)
                DestroyImmediate(result);
            if (depthResult != null)
                DestroyImmediate(depthResult);
        }
    }

    private static void ConfigureDataTextureImporter(TextureImporter importer, int width, int height)
    {
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.isReadable = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        int desiredMaxSize = Mathf.Clamp(
            Mathf.NextPowerOfTwo(Mathf.Max(width, height)), 32, MaximumMaskDimension);
        importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, desiredMaxSize);
        importer.SaveAndReimport();
    }

    private bool AssignBakedMapToTargetOcean(Texture2D baked, Texture2D bakedDepth)
    {
        if (targetOcean == null)
            return false;

        Undo.RecordObject(targetOcean, "Assign Baked Shore Distance Field");
        SerializedObject serializedOcean = new SerializedObject(targetOcean);
        serializedOcean.Update();

        SetRequiredObjectReference(serializedOcean, "shoreDistanceTexture", baked);
        SetRequiredObjectReference(serializedOcean, "shoreDepthModifierTexture", bakedDepth);
        SetRequiredFloat(serializedOcean, "shoreMaxDepthMeters", maxInferredDepthMeters);
        SetRequiredVector2(serializedOcean, "shoreMapOriginXZ", worldOriginXZ);
        SetRequiredVector2(serializedOcean, "shoreMapSizeXZ", worldSize);
        SetRequiredFloat(serializedOcean, "shoreDistanceRangeMeters", attenuationDistance);

        serializedOcean.ApplyModifiedProperties();
        EditorUtility.SetDirty(targetOcean);

        if (targetOcean.gameObject.scene.IsValid() && targetOcean.gameObject.scene.isLoaded)
            EditorSceneManager.MarkSceneDirty(targetOcean.gameObject.scene);

        return true;
    }

    private static void SetRequiredObjectReference(
        SerializedObject serializedObject, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
            throw new MissingFieldException(
                "SpectralOceanFFT is missing serialized field '" + propertyName + "'.");
        property.objectReferenceValue = value;
    }

    private static void SetRequiredVector2(
        SerializedObject serializedObject, string propertyName, Vector2 value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
            throw new MissingFieldException(
                "SpectralOceanFFT is missing serialized field '" + propertyName + "'.");
        property.vector2Value = value;
    }

    private static void SetRequiredFloat(
        SerializedObject serializedObject, string propertyName, float value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
            throw new MissingFieldException(
                "SpectralOceanFFT is missing serialized field '" + propertyName + "'.");
        property.floatValue = value;
    }

    private static string ResolveProjectAssetPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            throw new ArgumentException("The selected asset path is empty.", nameof(assetPath));

        string normalized = assetPath.Replace('\\', '/');
        if (!normalized.StartsWith("Assets/", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "SaveFilePanelInProject returned a path outside Assets/: " + assetPath);

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string assetsRoot = Path.GetFullPath(Application.dataPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string absolutePath = Path.GetFullPath(Path.Combine(
            projectRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar)));

        string assetsPrefix = assetsRoot + Path.DirectorySeparatorChar;
        StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!absolutePath.StartsWith(assetsPrefix, comparison))
            throw new InvalidOperationException(
                "The selected output path resolves outside this project's Assets folder: " +
                absolutePath);

        return absolutePath;
    }

    private void ReportError(string message)
    {
        Debug.LogError("ShoreDistanceFieldBaker: " + message, this);
        EditorUtility.DisplayDialog("Shore Distance Bake Failed", message, "OK");
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    // Exact squared Euclidean distance transform in O(n), with anisotropic
    // pixel spacing so non-square world mappings still produce metric distances.
    private static void DistanceTransform1D(
        float[] f,
        float[] d,
        int length,
        float coordinateStep,
        int[] v,
        float[] z)
    {
        const float infinity = 1e20f;

        // Find the first valid feature. A scanline with no land stays infinite;
        // starting the lower-envelope algorithm at infinity would cause INF-INF NaNs.
        int firstFeature = -1;
        for (int q = 0; q < length; q++)
        {
            if (f[q] < infinity * 0.5f)
            {
                firstFeature = q;
                break;
            }
        }

        if (firstFeature < 0)
        {
            for (int q = 0; q < length; q++)
                d[q] = infinity;
            return;
        }

        int k = 0;
        v[0] = firstFeature;
        z[0] = float.NegativeInfinity;
        z[1] = float.PositiveInfinity;

        for (int q = firstFeature + 1; q < length; q++)
        {
            if (f[q] >= infinity * 0.5f)
                continue;

            float qX = q * coordinateStep;
            float intersection;

            while (true)
            {
                int vk = v[k];
                float vkX = vk * coordinateStep;
                intersection = ((f[q] + qX * qX) - (f[vk] + vkX * vkX)) /
                               (2f * (qX - vkX));

                if (intersection > z[k] || k == 0)
                    break;
                k--;
            }

            if (intersection <= z[k])
            {
                v[0] = q;
                z[0] = float.NegativeInfinity;
                z[1] = float.PositiveInfinity;
                k = 0;
            }
            else
            {
                k++;
                v[k] = q;
                z[k] = intersection;
                z[k + 1] = float.PositiveInfinity;
            }
        }

        k = 0;
        for (int q = 0; q < length; q++)
        {
            float qX = q * coordinateStep;
            while (z[k + 1] < qX)
                k++;

            float delta = qX - v[k] * coordinateStep;
            d[q] = delta * delta + f[v[k]];
        }
    }
}
#endif
