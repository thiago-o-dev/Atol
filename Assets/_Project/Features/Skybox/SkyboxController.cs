using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class SkyboxController : MonoBehaviour
{
    [Header("Assign Your Skybox Materials")]
    [Tooltip("Fixed. Must be a cubemap skybox material (has _Tex / _Tint / _Exposure).")]
    public Material skybox1;
    [Tooltip("Either a cubemap skybox material OR a Vefects Galaxy material (detected automatically).")]
    public Material skybox2;

    [Tooltip("Used only when the matching Skybox material above is empty. Set through SetSkyboxPrimary/Secondary(Cubemap).")]
    public Cubemap skybox1Cubemap;
    [Tooltip("Used only when the matching Skybox material above is empty. Set through SetSkyboxPrimary/Secondary(Cubemap).")]
    public Cubemap skybox2Cubemap;

    [Range(0f, 1f)]
    public float fadeRatio = 0f;

    [Tooltip("Re-copies the Skybox 2 values every frame, so editing the Galaxy material live is reflected. Turn off to sync only when a material is swapped.")]
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

    private Material lastSky1;
    private Material lastSky2;
    private Cubemap lastCube1;
    private Cubemap lastCube2;

    private RenderTexture reflectionRT;
    private CommandBuffer bakeCmd;
    private static readonly int SkyBlendCubeId = Shader.PropertyToID("_SkyBlendCube");
    private static readonly int SkyBlendEnabledId = Shader.PropertyToID("_SkyBlendEnabled");
    private static readonly int SkyBakeFaceId = Shader.PropertyToID("_SkyBakeFace");

    // ---- property ids -------------------------------------------------------
    private static readonly int FadeId = Shader.PropertyToID("_FadeRatio");
    private static readonly int Sky2ModeId = Shader.PropertyToID("_Sky2Mode");
    private static readonly int GalaxyTexId = Shader.PropertyToID("_GalaxyTexture");

    // Galaxy properties copied 1:1 (same names in the Galaxy material and the blend shader).
    private static readonly string[] GalaxyFloats =
    {
        "_GalaxyEmissionIntensity", "_GalaxyBaseColorIntensity", "_GalaxyHueShift", "_GalaxySaturationShift",
        "_StarsTileOverall", "_StarsHueShift", "_StarsEmissionIntensity", "_StarsDistortion",
        "_StarsNoiseTileOverall",
        "_FresnelHueShift", "_FresnelEmissionIntensity", "_FresnelBias", "_FresnelScale",
        "_FresnelPower", "_FresnelInvert", "_FresnelHorizon", "_FresnelWidth",
        "_UseReflectionVector"
    };

    private static readonly string[] GalaxyColors =
    {
        "_GalaxyTint", "_StarsColor01", "_StarsColor02", "_FresnelColor"
    };

    private static readonly string[] GalaxyVectors =
    {
        "_StarsTile", "_StarsSpeed", "_StarsNoiseTile", "_StarsNoiseSpeed"
    };

    // Textures that also carry Tiling/Offset (used by the shader as _ST).
    private static readonly string[] GalaxyTexturesWithST = { "_StarsTexture", "_StarsNoiseTexture" };

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
    }

    private void Update()
    {
        if (blendMaterial == null) return;

        if (skybox1 != lastSky1 || skybox2 != lastSky2 ||
            skybox1Cubemap != lastCube1 || skybox2Cubemap != lastCube2)
            SyncMaterials(force: true);
        else if (liveSync)
            SyncSkybox2();

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

    public void SyncMaterials(bool force = false)
    {
        if (blendMaterial == null) return;

        // Skybox 1: always a cubemap.
        if (skybox1 != null)
        {
            if (skybox1.HasProperty("_Tex")) blendMaterial.SetTexture("_Tex1", skybox1.GetTexture("_Tex"));
            if (skybox1.HasProperty("_Tint")) blendMaterial.SetColor("_Tint1", skybox1.GetColor("_Tint"));
            if (skybox1.HasProperty("_Exposure")) blendMaterial.SetFloat("_Exposure1", skybox1.GetFloat("_Exposure"));
        }
        else if (skybox1Cubemap != null)
        {
            // Plain cubemap: neutral tint (0.5 is 1.0 after the shader's color-space double) and exposure 1.
            blendMaterial.SetTexture("_Tex1", skybox1Cubemap);
            blendMaterial.SetColor("_Tint1", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            blendMaterial.SetFloat("_Exposure1", 1f);
        }

        SyncSkybox2();

        lastSky1 = skybox1;
        lastSky2 = skybox2;
        lastCube1 = skybox1Cubemap;
        lastCube2 = skybox2Cubemap;
    }

    // ---- UnityEvent API ------------------------------------------------------
    // Overloads show up in the UnityEvent dropdown as (Material) and (Cubemap).
    // Setting one clears the other, so the last call always wins.

    /// <summary>Primary skybox (always a cubemap skybox material).</summary>
    public void SetSkyboxPrimary(Material material)
    {
        skybox1 = material;
        skybox1Cubemap = null;
        SyncMaterials(force: true);
    }

    public void SetSkyboxPrimary(Cubemap cubemap)
    {
        skybox1 = null;
        skybox1Cubemap = cubemap;
        SyncMaterials(force: true);
    }

    /// <summary>Secondary skybox: a cubemap skybox material or a Vefects Galaxy material.</summary>
    public void SetSkyboxSecondary(Material material)
    {
        skybox2 = material;
        skybox2Cubemap = null;
        SyncMaterials(force: true);
    }

    public void SetSkyboxSecondary(Cubemap cubemap)
    {
        skybox2 = null;
        skybox2Cubemap = cubemap;
        SyncMaterials(force: true);
    }

    public void SetFadeRatio(float value)
    {
        fadeRatio = Mathf.Clamp01(value);
    }

    private void SyncSkybox2()
    {
        if (skybox2 == null)
        {
            if (skybox2Cubemap != null)
            {
                blendMaterial.SetFloat(Sky2ModeId, 0f);
                blendMaterial.SetTexture("_Tex2", skybox2Cubemap);
                blendMaterial.SetColor("_Tint2", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                blendMaterial.SetFloat("_Exposure2", 1f);
            }
            return;
        }

        if (skybox2.HasProperty(GalaxyTexId))
        {
            blendMaterial.SetFloat(Sky2ModeId, 1f);
            CopyGalaxy(skybox2);
        }
        else
        {
            blendMaterial.SetFloat(Sky2ModeId, 0f);
            if (skybox2.HasProperty("_Tex")) blendMaterial.SetTexture("_Tex2", skybox2.GetTexture("_Tex"));
            if (skybox2.HasProperty("_Tint")) blendMaterial.SetColor("_Tint2", skybox2.GetColor("_Tint"));
            if (skybox2.HasProperty("_Exposure")) blendMaterial.SetFloat("_Exposure2", skybox2.GetFloat("_Exposure"));
        }
    }

    private void CopyGalaxy(Material src)
    {
        blendMaterial.SetTexture(GalaxyTexId, src.GetTexture(GalaxyTexId));

        foreach (var n in GalaxyTexturesWithST)
        {
            if (!src.HasProperty(n)) continue;
            blendMaterial.SetTexture(n, src.GetTexture(n));
            blendMaterial.SetTextureScale(n, src.GetTextureScale(n));
            blendMaterial.SetTextureOffset(n, src.GetTextureOffset(n));
        }

        foreach (var n in GalaxyFloats)
            if (src.HasProperty(n)) blendMaterial.SetFloat(n, src.GetFloat(n));

        foreach (var n in GalaxyColors)
            if (src.HasProperty(n)) blendMaterial.SetColor(n, src.GetColor(n));

        foreach (var n in GalaxyVectors)
            if (src.HasProperty(n)) blendMaterial.SetVector(n, src.GetVector(n));
    }
}
