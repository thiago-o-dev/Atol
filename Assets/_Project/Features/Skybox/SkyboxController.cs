using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Four skybox slots (Primary, Secondary, Tertiary, Quaternary). Each slot can hold a skybox Material
/// (cubemap material, or a Vefects Galaxy material) or a plain Cubemap.
/// ActivePrimary / ActiveSecondary choose which two slots are the ends of the fade:
/// fadeRatio 0 = slot ActivePrimary, fadeRatio 1 = slot ActiveSecondary.
/// </summary>
[ExecuteAlways]
public class SkyboxController : MonoBehaviour
{
    public const int SlotCount = 4;

    [Header("Skybox Slots (Material: cubemap skybox or Galaxy)")]
    public Material skybox1;
    public Material skybox2;
    public Material skybox3;
    public Material skybox4;

    [Header("Skybox Slots (Cubemap, used only if the slot's Material is empty)")]
    public Cubemap skybox1Cubemap;
    public Cubemap skybox2Cubemap;
    public Cubemap skybox3Cubemap;
    public Cubemap skybox4Cubemap;

    [Header("Which slots are blended (0 = Primary, 1 = Secondary, 2 = Tertiary, 3 = Quaternary)")]
    [Range(0, 3)] public int activePrimary = 0;
    [Range(0, 3)] public int activeSecondary = 1;

    [Tooltip("0 = ActivePrimary slot, 1 = ActiveSecondary slot.")]
    [Range(0f, 1f)]
    public float fadeRatio = 0f;

    [Tooltip("Re-copies the active slots every frame, so editing a Galaxy/skybox material live is reflected. Turn off to sync only when something is swapped.")]
    public bool liveSync = true;

    [Header("Realtime Reflection Cubemap (for Wave / water)")]
    [Tooltip("Renders the blended skybox into a cubemap and publishes it as the global _SkyBlendCube.")]
    public bool bakeReflectionCube = true;
    [Tooltip("Face size. 128 is plenty for blurred water reflections.")]
    public int reflectionResolution = 128;
    [Tooltip("Re-bake every N frames (1 = every frame). The Galaxy stars animate, so keep this small while it is visible.")]
    [Range(1, 8)] public int bakeEveryNFrames = 1;
    public RenderTexture ReflectionCube => reflectionRT;

    [Header("Internal Blend Material")]
    [SerializeField] private Material blendMaterial;

    // Change detection
    private readonly Material[] lastMats = new Material[SlotCount];
    private readonly Cubemap[] lastCubes = new Cubemap[SlotCount];
    private int lastActiveA = -1;
    private int lastActiveB = -1;

    private RenderTexture reflectionRT;
    private CommandBuffer bakeCmd;
    private static readonly int SkyBlendCubeId = Shader.PropertyToID("_SkyBlendCube");
    private static readonly int SkyBlendEnabledId = Shader.PropertyToID("_SkyBlendEnabled");
    private static readonly int SkyBakeFaceId = Shader.PropertyToID("_SkyBakeFace");

    // ---- property ids -------------------------------------------------------
    private static readonly int FadeId = Shader.PropertyToID("_FadeRatio");
    private static readonly int Sky1ModeId = Shader.PropertyToID("_Sky1Mode");
    private static readonly int Sky2ModeId = Shader.PropertyToID("_Sky2Mode");

    // Galaxy properties copied 1:1. Side 2 of the blend shader uses the same names as the
    // Galaxy material; side 1 uses the same names with the suffix "_S1".
    private static readonly string[] GalaxyFloats =
    {
        "_GalaxyEmissionIntensity", "_GalaxyBaseColorIntensity", "_GalaxyHueShift", "_GalaxySaturationShift",
        "_StarsTileOverall", "_StarsHueShift", "_StarsEmissionIntensity", "_StarsDistortion",
        "_StarsNoiseTileOverall",
        "_FresnelHueShift", "_FresnelEmissionIntensity", "_FresnelBias", "_FresnelScale",
        "_FresnelPower", "_FresnelInvert", "_FresnelHorizon", "_FresnelWidth",
        "_UseReflectionVector"
    };
    private static readonly string[] GalaxyColors = { "_GalaxyTint", "_StarsColor01", "_StarsColor02", "_FresnelColor" };
    private static readonly string[] GalaxyVectors = { "_StarsTile", "_StarsSpeed", "_StarsNoiseTile", "_StarsNoiseSpeed" };
    // Textures that also carry Tiling/Offset (used by the shader as _ST).
    private static readonly string[] GalaxyTexturesWithST = { "_StarsTexture", "_StarsNoiseTexture" };

    private class GalaxyIds
    {
        public int galaxyTex;
        public int[] floats, colors, vectors, stTextures;

        public GalaxyIds(string suffix)
        {
            galaxyTex = Shader.PropertyToID("_GalaxyTexture" + suffix);
            floats = Build(GalaxyFloats, suffix);
            colors = Build(GalaxyColors, suffix);
            vectors = Build(GalaxyVectors, suffix);
            stTextures = Build(GalaxyTexturesWithST, suffix);
        }

        private static int[] Build(string[] names, string suffix)
        {
            var ids = new int[names.Length];
            for (int i = 0; i < names.Length; i++) ids[i] = Shader.PropertyToID(names[i] + suffix);
            return ids;
        }
    }

    private static readonly GalaxyIds SourceIds = new GalaxyIds("");   // names in the Galaxy material
    private static readonly GalaxyIds Side1Ids = new GalaxyIds("_S1"); // names in the blend shader, side 1
    private static readonly GalaxyIds Side2Ids = SourceIds;            // names in the blend shader, side 2

    private void OnEnable()
    {
        SyncMaterials(force: true);
    }

    private void OnDisable()
    {
        Shader.SetGlobalFloat(SkyBlendEnabledId, 0f);
        ReleaseReflection();
    }

    private void OnValidate()
    {
        reflectionResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(reflectionResolution), 16, 1024);
        activePrimary = Mathf.Clamp(activePrimary, 0, SlotCount - 1);
        activeSecondary = Mathf.Clamp(activeSecondary, 0, SlotCount - 1);
    }

    private void Update()
    {
        if (blendMaterial == null) return;

        if (SlotsChanged())
            SyncMaterials(force: true);
        else if (liveSync)
            ApplySides();

        blendMaterial.SetFloat(FadeId, fadeRatio);

        if (RenderSettings.skybox != blendMaterial)
            RenderSettings.skybox = blendMaterial;

        if (bakeReflectionCube)
        {
            if (Time.frameCount % bakeEveryNFrames == 0 || reflectionRT == null)
                BakeReflection();
        }
        else
        {
            Shader.SetGlobalFloat(SkyBlendEnabledId, 0f);
            ReleaseReflection();
        }
    }

    private void BakeReflection()
    {
        int pass = blendMaterial.FindPass("CubeBake");
        if (pass < 0) return; // old shader without the bake pass

        if (reflectionRT == null || reflectionRT.width != reflectionResolution)
        {
            ReleaseReflection();
            reflectionRT = new RenderTexture(reflectionResolution, reflectionResolution, 0,
                RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = "SkyBlendCube",
                dimension = TextureDimension.Cube,
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            reflectionRT.Create();
        }

        if (bakeCmd == null) bakeCmd = new CommandBuffer { name = "Bake Skybox Cubemap" };
        bakeCmd.Clear();
        for (int face = 0; face < 6; face++)
        {
            bakeCmd.SetRenderTarget(reflectionRT, 0, (CubemapFace)face);
            bakeCmd.SetGlobalFloat(SkyBakeFaceId, face);
            bakeCmd.DrawProcedural(Matrix4x4.identity, blendMaterial, pass, MeshTopology.Triangles, 3);
        }
        Graphics.ExecuteCommandBuffer(bakeCmd);
        reflectionRT.GenerateMips();

        Shader.SetGlobalTexture(SkyBlendCubeId, reflectionRT);
        Shader.SetGlobalFloat(SkyBlendEnabledId, 1f);
    }

    private void ReleaseReflection()
    {
        if (reflectionRT != null)
        {
            reflectionRT.Release();
            if (Application.isPlaying) Destroy(reflectionRT); else DestroyImmediate(reflectionRT);
            reflectionRT = null;
        }
        if (bakeCmd != null) { bakeCmd.Release(); bakeCmd = null; }
    }

    // ---- slot helpers ----------------------------------------------------------

    private Material GetMat(int i)
    {
        switch (i) { case 0: return skybox1; case 1: return skybox2; case 2: return skybox3; default: return skybox4; }
    }

    private Cubemap GetCube(int i)
    {
        switch (i) { case 0: return skybox1Cubemap; case 1: return skybox2Cubemap; case 2: return skybox3Cubemap; default: return skybox4Cubemap; }
    }

    private bool SlotsChanged()
    {
        if (activePrimary != lastActiveA || activeSecondary != lastActiveB) return true;
        for (int i = 0; i < SlotCount; i++)
            if (GetMat(i) != lastMats[i] || GetCube(i) != lastCubes[i]) return true;
        return false;
    }

    public void SyncMaterials(bool force = false)
    {
        if (blendMaterial == null) return;

        ApplySides();

        for (int i = 0; i < SlotCount; i++) { lastMats[i] = GetMat(i); lastCubes[i] = GetCube(i); }
        lastActiveA = activePrimary;
        lastActiveB = activeSecondary;
    }

    // Both sides of the shader accept a cubemap or a Galaxy, so any two slots can be blended
    // (including two different Galaxies at once).
    private void ApplySides()
    {
        ApplySide(Mathf.Clamp(activePrimary, 0, SlotCount - 1), 1);
        ApplySide(Mathf.Clamp(activeSecondary, 0, SlotCount - 1), 2);
    }

    private static readonly Color NeutralTint = new Color(0.5f, 0.5f, 0.5f, 0.5f);

    private void ApplySide(int slot, int side)
    {
        string suffix = side == 1 ? "1" : "2";
        int modeId = side == 1 ? Sky1ModeId : Sky2ModeId;
        var m = GetMat(slot);

        if (m != null && m.HasProperty(SourceIds.galaxyTex))
        {
            blendMaterial.SetFloat(modeId, 1f);
            CopyGalaxy(m, side == 1 ? Side1Ids : Side2Ids);
            return;
        }

        blendMaterial.SetFloat(modeId, 0f);
        if (m != null)
        {
            if (m.HasProperty("_Tex")) blendMaterial.SetTexture("_Tex" + suffix, m.GetTexture("_Tex"));
            if (m.HasProperty("_Tint")) blendMaterial.SetColor("_Tint" + suffix, m.GetColor("_Tint"));
            if (m.HasProperty("_Exposure")) blendMaterial.SetFloat("_Exposure" + suffix, m.GetFloat("_Exposure"));
            return;
        }

        var c = GetCube(slot);
        if (c != null)
        {
            // Plain cubemap: neutral tint (0.5 becomes 1.0 after the shader's color-space double), exposure 1.
            blendMaterial.SetTexture("_Tex" + suffix, c);
            blendMaterial.SetColor("_Tint" + suffix, NeutralTint);
            blendMaterial.SetFloat("_Exposure" + suffix, 1f);
        }
    }

    // ---- UnityEvent API ------------------------------------------------------
    // Each slot has a (Material) and a (Cubemap) overload; setting one clears the other,
    // so the last call wins. Slots can be filled while inactive and selected later with SetActive*.

    private void SetSlot(int slot, Material m, Cubemap c)
    {
        switch (slot)
        {
            case 0: skybox1 = m; skybox1Cubemap = c; break;
            case 1: skybox2 = m; skybox2Cubemap = c; break;
            case 2: skybox3 = m; skybox3Cubemap = c; break;
            default: skybox4 = m; skybox4Cubemap = c; break;
        }
        SyncMaterials(force: true);
    }

    public void SetSkyboxPrimary(Material material)      => SetSlot(0, material, null);
    public void SetSkyboxPrimary(Cubemap cubemap)        => SetSlot(0, null, cubemap);
    public void SetSkyboxSecondary(Material material)    => SetSlot(1, material, null);
    public void SetSkyboxSecondary(Cubemap cubemap)      => SetSlot(1, null, cubemap);
    public void SetSkyboxTertiary(Material material)     => SetSlot(2, material, null);
    public void SetSkyboxTertiary(Cubemap cubemap)       => SetSlot(2, null, cubemap);
    public void SetSkyboxQuaternary(Material material)   => SetSlot(3, material, null);
    public void SetSkyboxQuaternary(Cubemap cubemap)     => SetSlot(3, null, cubemap);

    /// <summary>Slot (0-3) used at fadeRatio 0.</summary>
    public void SetActivePrimary(int slot)
    {
        activePrimary = Mathf.Clamp(slot, 0, SlotCount - 1);
        SyncMaterials(force: true);
    }

    /// <summary>Slot (0-3) used at fadeRatio 1.</summary>
    public void SetActiveSecondary(int slot)
    {
        activeSecondary = Mathf.Clamp(slot, 0, SlotCount - 1);
        SyncMaterials(force: true);
    }

    public void SetFadeRatio(float value)
    {
        fadeRatio = Mathf.Clamp01(value);
    }

    public void Fade1SecondRatio(float targetRatio)
    {
        StartCoroutine(FadeRatioCoroutine(targetRatio));
    }

    private System.Collections.IEnumerator FadeRatioCoroutine(float targetRatio, float duration = 1f)
    {
        float startRatio = fadeRatio;
        float elapsed = 0f;
        targetRatio = Mathf.Clamp01(targetRatio);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            fadeRatio = Mathf.Lerp(startRatio, targetRatio, Mathf.SmoothStep(0f, duration, t));

            yield return null;
        }

        fadeRatio = targetRatio;
    }
    private void CopyGalaxy(Material src, GalaxyIds dst)
    {
        blendMaterial.SetTexture(dst.galaxyTex, src.GetTexture(SourceIds.galaxyTex));

        for (int i = 0; i < SourceIds.stTextures.Length; i++)
        {
            int sid = SourceIds.stTextures[i], did = dst.stTextures[i];
            if (!src.HasProperty(sid)) continue;
            blendMaterial.SetTexture(did, src.GetTexture(sid));
            blendMaterial.SetTextureScale(did, src.GetTextureScale(sid));
            blendMaterial.SetTextureOffset(did, src.GetTextureOffset(sid));
        }

        for (int i = 0; i < SourceIds.floats.Length; i++)
            if (src.HasProperty(SourceIds.floats[i])) blendMaterial.SetFloat(dst.floats[i], src.GetFloat(SourceIds.floats[i]));

        for (int i = 0; i < SourceIds.colors.Length; i++)
            if (src.HasProperty(SourceIds.colors[i])) blendMaterial.SetColor(dst.colors[i], src.GetColor(SourceIds.colors[i]));

        for (int i = 0; i < SourceIds.vectors.Length; i++)
            if (src.HasProperty(SourceIds.vectors[i])) blendMaterial.SetVector(dst.vectors[i], src.GetVector(SourceIds.vectors[i]));
    }
}
