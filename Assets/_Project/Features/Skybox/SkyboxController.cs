using UnityEngine;

[ExecuteAlways]
public class SkyboxController : MonoBehaviour
{
    [Header("Assign Your Skybox Materials")]
    [Tooltip("Fixed. Must be a cubemap skybox material (has _Tex / _Tint / _Exposure).")]
    public Material skybox1;
    [Tooltip("Either a cubemap skybox material OR a Vefects Galaxy material (detected automatically).")]
    public Material skybox2;

    [Range(0f, 1f)]
    public float fadeRatio = 0f;

    [Tooltip("Re-copies the Skybox 2 values every frame, so editing the Galaxy material live is reflected. Turn off to sync only when a material is swapped.")]
    public bool liveSync = true;

    [Header("Internal Blend Material")]
    [SerializeField] private Material blendMaterial;

    private Material lastSky1;
    private Material lastSky2;

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

    private void Update()
    {
        if (blendMaterial == null) return;

        if (skybox1 != lastSky1 || skybox2 != lastSky2)
            SyncMaterials(force: true);
        else if (liveSync)
            SyncSkybox2();

        blendMaterial.SetFloat(FadeId, fadeRatio);

        if (RenderSettings.skybox != blendMaterial)
            RenderSettings.skybox = blendMaterial;
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

        SyncSkybox2();

        lastSky1 = skybox1;
        lastSky2 = skybox2;
    }

    private void SyncSkybox2()
    {
        if (skybox2 == null) return;

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