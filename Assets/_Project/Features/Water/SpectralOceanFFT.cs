using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// Drives a three-band Tessendorf / Phillips-spectrum ocean using GPU compute.
/// Attach this to the same GameObject as the MeshRenderer that uses Wave.shader.
///
/// Directions are XZ vectors: Vector2.x = world X, Vector2.y = world Z.
/// All patch lengths and wavelengths are in Unity world units (normally metres).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshRenderer))]
public sealed class SpectralOceanFFT : MonoBehaviour
{
    [Serializable]
    private sealed class CascadeSettings
    {
        [Min(1f)] public float patchLength = 2048f;
        [Min(0.1f)] public float minWavelength = 24f;
        [Min(0.2f)] public float maxWavelength = 1800f;
        [Min(0f)] public float amplitudeScale = 1.7f;

        [NonSerialized] public RenderTexture h0;
        [NonSerialized] public RenderTexture fft0A;
        [NonSerialized] public RenderTexture fft0B;
        [NonSerialized] public RenderTexture fftZA;
        [NonSerialized] public RenderTexture fftZB;
        [NonSerialized] public RenderTexture displacement;
        [NonSerialized] public RenderTexture normal;
    }

    [Header("GPU Simulation")]
    [SerializeField] private ComputeShader oceanCompute;
    [SerializeField, Range(64, 512)] private int resolution = 256;

    [Header("Wind / Spectrum")]
    [Tooltip("World-space XZ wind direction. X = world X, Y = world Z.")]
    [SerializeField] private Vector2 windDirectionXZ = new Vector2(1f, 0.15f);
    [Tooltip("Wind speed in metres per second. Higher values move spectral energy toward longer, stronger waves.")]
    [SerializeField, Min(0.1f)] private float windSpeed = 26f;
    [Tooltip("Phillips spectrum energy multiplier. Increase this for a more violent sea.")]
    [SerializeField, Min(0f)] private float phillipsAmplitude = 0.0008f;
    [Tooltip("Suppresses very short waves to keep the spectrum well behaved.")]
    [SerializeField, Min(0f)] private float smallWaveDamping = 0.3f;
    [SerializeField, Range(0f, 1f)] private float backwindDamping = 0.07f;
    [SerializeField] private int randomSeed = 12345;

    [Header("Surface Shape")]
    [Tooltip("Horizontal displacement multiplier. Higher values create sharper, more curled crests.")]
    [SerializeField, Range(0f, 3f)] private float choppiness = 1.65f;
    [Tooltip("Foam begins where the horizontal displacement Jacobian drops below this value.")]
    [SerializeField, Range(0.5f, 1.2f)] private float foamBias = 0.98f;
    [SerializeField, Min(0f)] private float foamGain = 3f;

    [Header("Frequency Cascades")]
    [Tooltip("Keep exactly three cascades. Large and medium bands displace geometry; the small band adds normals and foam.")]
    [SerializeField] private CascadeSettings[] cascades =
    {
        new CascadeSettings { patchLength = 2048f, minWavelength = 24f,  maxWavelength = 1800f, amplitudeScale = 1.7f },
        new CascadeSettings { patchLength = 512f,  minWavelength = 8f,   maxWavelength = 100f,  amplitudeScale = 2.0f },
        new CascadeSettings { patchLength = 128f,  minWavelength = 1.5f, maxWavelength = 12f,   amplitudeScale = 4.0f }
    };

    private static readonly int[] DisplacementIds =
    {
        Shader.PropertyToID("_OceanDisplacement0"),
        Shader.PropertyToID("_OceanDisplacement1"),
        Shader.PropertyToID("_OceanDisplacement2")
    };

    private static readonly int[] NormalIds =
    {
        Shader.PropertyToID("_OceanNormal0"),
        Shader.PropertyToID("_OceanNormal1"),
        Shader.PropertyToID("_OceanNormal2")
    };

    private static readonly int HalfTexelId = Shader.PropertyToID("_OceanHalfTexel");
    private static readonly int OceanEnabledId = Shader.PropertyToID("_OceanEnabled");

    private static readonly int[] PatchLengthIds =
    {
        Shader.PropertyToID("_OceanPatchLength0"),
        Shader.PropertyToID("_OceanPatchLength1"),
        Shader.PropertyToID("_OceanPatchLength2")
    };

    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock propertyBlock;

    private int kernelInit;
    private int kernelEvolve;
    private int kernelFftX;
    private int kernelFftY;
    private int kernelBuildMaps;
    private int log2Resolution;

    private bool resourcesReady;
    private bool rebuildRequested = true;
    private bool spectrumDirty = true;

    private void OnEnable()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        propertyBlock = new MaterialPropertyBlock();
        rebuildRequested = true;
        spectrumDirty = true;
    }

    private void OnDisable()
    {
        ReleaseResources();
    }

    private void OnValidate()
    {
        resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Clamp(resolution, 64, 512)), 64, 512);
        windSpeed = Mathf.Max(0.1f, windSpeed);
        phillipsAmplitude = Mathf.Max(0f, phillipsAmplitude);
        smallWaveDamping = Mathf.Max(0f, smallWaveDamping);
        foamGain = Mathf.Max(0f, foamGain);

        if (cascades != null)
        {
            foreach (CascadeSettings cascade in cascades)
            {
                if (cascade == null)
                    continue;

                cascade.patchLength = Mathf.Max(1f, cascade.patchLength);
                cascade.minWavelength = Mathf.Max(0.1f, cascade.minWavelength);
                cascade.maxWavelength = Mathf.Max(cascade.minWavelength + 0.1f, cascade.maxWavelength);
                cascade.amplitudeScale = Mathf.Max(0f, cascade.amplitudeScale);
            }
        }

        rebuildRequested = true;
        spectrumDirty = true;
    }

    private void Update()
    {
        if (rebuildRequested || !resourcesReady)
        {
            if (!InitializeResources())
                return;
        }

        SetCommonParameters();

        if (spectrumDirty)
        {
            for (int i = 0; i < cascades.Length; i++)
                GenerateInitialSpectrum(cascades[i], i);

            spectrumDirty = false;
        }

        for (int i = 0; i < cascades.Length; i++)
            SimulateCascade(cascades[i], i);
    }

    private bool InitializeResources()
    {
        ReleaseResources();

        if (oceanCompute == null)
        {
            Debug.LogError("SpectralOceanFFT: Assign OceanFFT.compute in the Inspector.", this);
            rebuildRequested = false;
            return false;
        }

        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.LogError("SpectralOceanFFT: This platform does not support compute shaders.", this);
            rebuildRequested = false;
            return false;
        }

        if (cascades == null || cascades.Length != 3)
        {
            Debug.LogError("SpectralOceanFFT: This shader expects exactly three frequency cascades.", this);
            return false;
        }

        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Clamp(resolution, 64, 512)), 64, 512);
        log2Resolution = Mathf.RoundToInt(Mathf.Log(resolution, 2f));

        kernelInit = oceanCompute.FindKernel("InitSpectrum");
        kernelEvolve = oceanCompute.FindKernel("EvolveSpectrum");
        kernelFftX = oceanCompute.FindKernel("FFT_X");
        kernelFftY = oceanCompute.FindKernel("FFT_Y");
        kernelBuildMaps = oceanCompute.FindKernel("BuildMaps");

        for (int i = 0; i < cascades.Length; i++)
        {
            CascadeSettings c = cascades[i];
            string suffix = "_" + i;

            c.h0 = CreateTexture(GraphicsFormat.R32G32_SFloat, "Ocean_H0" + suffix, false);
            c.fft0A = CreateTexture(GraphicsFormat.R32G32B32A32_SFloat, "Ocean_FFT0_A" + suffix, false);
            c.fft0B = CreateTexture(GraphicsFormat.R32G32B32A32_SFloat, "Ocean_FFT0_B" + suffix, false);
            c.fftZA = CreateTexture(GraphicsFormat.R32G32_SFloat, "Ocean_FFTZ_A" + suffix, false);
            c.fftZB = CreateTexture(GraphicsFormat.R32G32_SFloat, "Ocean_FFTZ_B" + suffix, false);
            c.displacement = CreateTexture(GraphicsFormat.R32G32B32A32_SFloat, "Ocean_Displacement" + suffix, true);
            c.normal = CreateTexture(GraphicsFormat.R32G32B32A32_SFloat, "Ocean_Normal" + suffix, true);

            if (c.h0 == null || c.fft0A == null || c.fft0B == null ||
                c.fftZA == null || c.fftZB == null ||
                c.displacement == null || c.normal == null)
            {
                Debug.LogError("SpectralOceanFFT: Failed to allocate one or more GPU textures.", this);
                ReleaseResources();
                return false;
            }
        }

        resourcesReady = true;
        rebuildRequested = false;
        spectrumDirty = true;
        ApplyMaterialTextures();
        return true;
    }

    private RenderTexture CreateTexture(GraphicsFormat format, string textureName, bool mipmapped)
    {
        RenderTextureDescriptor descriptor = new RenderTextureDescriptor(
            resolution, resolution, format, 0);

        descriptor.enableRandomWrite = true;
        descriptor.msaaSamples = 1;
        descriptor.useMipMap = mipmapped;
        descriptor.autoGenerateMips = false;

        RenderTexture texture = new RenderTexture(descriptor)
        {
            name = textureName,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = mipmapped ? FilterMode.Trilinear : FilterMode.Point,
            anisoLevel = 0
        };

        if (!texture.Create())
        {
            Debug.LogError("SpectralOceanFFT: Could not create RenderTexture '" + textureName + "'.", this);
            Destroy(texture);
            return null;
        }

        return texture;
    }

    private void SetCommonParameters()
    {
        Vector2 wind = windDirectionXZ.sqrMagnitude > 0.000001f
            ? windDirectionXZ.normalized
            : Vector2.right;

        oceanCompute.SetInt("_Resolution", resolution);
        oceanCompute.SetInt("_Log2Resolution", log2Resolution);
        oceanCompute.SetInt("_Seed", randomSeed);
        oceanCompute.SetVector("_WindDirection", new Vector4(wind.x, wind.y, 0f, 0f));
        oceanCompute.SetFloat("_WindSpeed", windSpeed);
        oceanCompute.SetFloat("_PhillipsAmplitude", phillipsAmplitude);
        oceanCompute.SetFloat("_SmallWaveDamping", smallWaveDamping);
        oceanCompute.SetFloat("_BackwindDamping", backwindDamping);
        oceanCompute.SetFloat("_TimeSeconds", Time.time);
        oceanCompute.SetFloat("_Choppiness", choppiness);
        oceanCompute.SetFloat("_FoamBias", foamBias);
        oceanCompute.SetFloat("_FoamGain", foamGain);
    }

    private void SetCascadeParameters(CascadeSettings c, int cascadeIndex)
    {
        oceanCompute.SetFloat("_PatchLength", c.patchLength);
        oceanCompute.SetFloat("_MinWavelength", c.minWavelength);
        oceanCompute.SetFloat("_MaxWavelength", c.maxWavelength);
        oceanCompute.SetFloat("_CascadeScale", c.amplitudeScale);
        oceanCompute.SetInt("_Seed", unchecked(randomSeed + cascadeIndex * 92821));
    }

    private void GenerateInitialSpectrum(CascadeSettings c, int cascadeIndex)
    {
        SetCascadeParameters(c, cascadeIndex);
        oceanCompute.SetTexture(kernelInit, "_H0Write", c.h0);
        oceanCompute.Dispatch(kernelInit, resolution / 8, resolution / 8, 1);
    }

    private void SimulateCascade(CascadeSettings c, int cascadeIndex)
    {
        SetCascadeParameters(c, cascadeIndex);

        // Evolve h0(k) into h(k,t), plus the two horizontal displacement spectra.
        oceanCompute.SetTexture(kernelEvolve, "_H0Read", c.h0);
        oceanCompute.SetTexture(kernelEvolve, "_FFT0Write", c.fft0A);
        oceanCompute.SetTexture(kernelEvolve, "_FFTZWrite", c.fftZA);
        oceanCompute.Dispatch(kernelEvolve, resolution / 8, resolution / 8, 1);

        // Inverse FFT along X: one group per row.
        oceanCompute.SetTexture(kernelFftX, "_FFT0Read", c.fft0A);
        oceanCompute.SetTexture(kernelFftX, "_FFTZRead", c.fftZA);
        oceanCompute.SetTexture(kernelFftX, "_FFT0Write", c.fft0B);
        oceanCompute.SetTexture(kernelFftX, "_FFTZWrite", c.fftZB);
        oceanCompute.Dispatch(kernelFftX, 1, resolution, 1);

        // Inverse FFT along Z: one group per column.
        oceanCompute.SetTexture(kernelFftY, "_FFT0Read", c.fft0B);
        oceanCompute.SetTexture(kernelFftY, "_FFTZRead", c.fftZB);
        oceanCompute.SetTexture(kernelFftY, "_FFT0Write", c.fft0A);
        oceanCompute.SetTexture(kernelFftY, "_FFTZWrite", c.fftZA);
        oceanCompute.Dispatch(kernelFftY, resolution, 1, 1);

        // Convert inverse-FFT output into displacement, normals and whitecap mask.
        oceanCompute.SetTexture(kernelBuildMaps, "_FFT0Read", c.fft0A);
        oceanCompute.SetTexture(kernelBuildMaps, "_FFTZRead", c.fftZA);
        oceanCompute.SetTexture(kernelBuildMaps, "_DisplacementWrite", c.displacement);
        oceanCompute.SetTexture(kernelBuildMaps, "_NormalWrite", c.normal);
        oceanCompute.Dispatch(kernelBuildMaps, resolution / 8, resolution / 8, 1);

        // The maps are sampled at multiple distances in the fragment shader.
        c.displacement.GenerateMips();
        c.normal.GenerateMips();
    }

    private void ApplyMaterialTextures()
    {
        if (meshRenderer == null || cascades == null || cascades.Length != 3)
            return;

        meshRenderer.GetPropertyBlock(propertyBlock);

        for (int i = 0; i < 3; i++)
        {
            CascadeSettings cascade = cascades[i];
            if (cascade == null || cascade.displacement == null || cascade.normal == null)
            {
                Debug.LogError("SpectralOceanFFT: Cannot bind incomplete cascade textures.", this);
                return;
            }

            propertyBlock.SetTexture(DisplacementIds[i], cascade.displacement);
            propertyBlock.SetTexture(NormalIds[i], cascade.normal);
            propertyBlock.SetFloat(PatchLengthIds[i], cascade.patchLength);
        }

        // FFT samples live at texel centers; this offset aligns world-space grid
        // coordinates with those centers when using bilinear texture sampling.
        propertyBlock.SetFloat(HalfTexelId, 0.5f / resolution);
        propertyBlock.SetFloat(OceanEnabledId, 1f);
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    private void ReleaseResources()
    {
        resourcesReady = false;

        if (cascades != null)
        {
            foreach (CascadeSettings c in cascades)
            {
                if (c == null)
                    continue;

                ReleaseTexture(ref c.h0);
                ReleaseTexture(ref c.fft0A);
                ReleaseTexture(ref c.fft0B);
                ReleaseTexture(ref c.fftZA);
                ReleaseTexture(ref c.fftZB);
                ReleaseTexture(ref c.displacement);
                ReleaseTexture(ref c.normal);
            }
        }

        if (meshRenderer != null && propertyBlock != null)
        {
            // MaterialPropertyBlock.SetTexture does not accept null in current
            // Unity versions. Bind valid fallback textures instead, then tell
            // the material to render a flat, undisplaced surface until the next
            // successful initialization. This is safe on first initialization,
            // failed allocations, repeated disable calls, and domain reloads.
            Texture fallback = Texture2D.blackTexture;

            meshRenderer.GetPropertyBlock(propertyBlock);
            for (int i = 0; i < 3; i++)
            {
                propertyBlock.SetTexture(DisplacementIds[i], fallback);
                propertyBlock.SetTexture(NormalIds[i], fallback);
            }
            propertyBlock.SetFloat(OceanEnabledId, 0f);
            meshRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    private void ReleaseTexture(ref RenderTexture texture)
    {
        if (texture == null)
            return;

        texture.Release();
        Destroy(texture);
        texture = null;
    }
}
