using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

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
    public enum ShoreDebugView
    {
        Final = 0,
        Distance = 1,
        WaveAttenuation = 2,
        BreakerBand = 3,
        FoamCoverage = 4,
        SeabedDepth = 5
    }


    /// <summary>
    /// Asynchronously sampled position on the animated FFT surface. Position is
    /// the displaced world position corresponding to BasePosition.xz.
    /// </summary>
    public struct SurfaceSample
    {
        public Vector3 BasePosition;
        public Vector3 Position;
        public Vector3 Normal;
        public bool IsValid;
        public float SampleTime;
        public float Height => Position.y;
    }

    private const int MaxSurfaceQueriesPerBatch = 256;
    private const int SurfaceQuerySlotCount = 8;
    private const int MaxPendingCameraQueries = 8;
    private const int MaxPendingPhysicsQueries = 24;
    private const int MaxPendingSurfaceQueries = MaxPendingCameraQueries + MaxPendingPhysicsQueries;

    [StructLayout(LayoutKind.Sequential)]
    private struct GpuSurfaceQueryResult
    {
        public Vector4 displacement;
        public Vector4 normal;
    }

    private sealed class PendingSurfaceQuery
    {
        public Vector3[] positions;
        public Action<SurfaceSample[]> callback;
    }

    private sealed class SurfaceQuerySlice
    {
        public PendingSurfaceQuery query;
        public int offset;
        public float sampleTime;
        public float waterLevelY;
    }

    private sealed class SurfaceQuerySlot
    {
        public ComputeBuffer positions;
        public ComputeBuffer results;
        public bool inFlight;
        public bool disposeWhenComplete;
        public int resultCount;
        public List<SurfaceQuerySlice> slices;

        public void Dispose()
        {
            if (positions != null)
            {
                positions.Release();
                positions = null;
            }
            if (results != null)
            {
                results.Release();
                results = null;
            }
        }
    }

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
    [Header("Vertex Displacement Culling")]
    [Tooltip("Expands the MeshRenderer's local bounds in all directions so GPU-displaced waves are not incorrectly frustum-culled. Increase this for extreme storms or very large waves.")]
    [SerializeField, Min(1f)] private float rendererBoundsPaddingMeters = 250f;

    [Header("Wind / Spectrum")]
    [Tooltip("World-space XZ wind direction. X = world X, Y = world Z.")]
    [SerializeField] private Vector2 windDirectionXZ = new Vector2(1f, 0.15f);
    [Tooltip("Wind speed in metres per second. Higher values move spectral energy toward longer, stronger waves.")]
    [SerializeField, Min(0.1f)] private float windSpeed = 26f;
    [Tooltip("Live wind intensity from 0 (light air) to 1 (strong wind). This scales wave displacement, choppiness, foam and wind-particle motion without rebuilding the FFT textures.")]
    [InspectorName("Wind Strength"), SerializeField, Range(0f, 1f)] private float windStrength = 0.65f;
    [Tooltip("Phillips spectrum energy multiplier. Increase this for a more violent sea.")]
    [SerializeField, Min(0f)] private float phillipsAmplitude = 0.0008f;
    [Tooltip("Suppresses very short waves to keep the spectrum well behaved.")]
    [SerializeField, Min(0f)] private float smallWaveDamping = 0.3f;
    [SerializeField, Range(0f, 1f)] private float backwindDamping = 0.07f;
    [SerializeField] private int randomSeed = 12345;

    [Header("Wind Visual Effects")]
    [Tooltip("Master switch for the short-lived, world-space wind particles around the player.")]
    [InspectorName("Show Wind Particles"), SerializeField] private bool showWindStreaks = true;
    [Tooltip("Controls how many short-lived world-space wind wisps are emitted. Use about 0.2-0.4 on land and 1.8-2.4 while sailing.")]
    [InspectorName("Wind Particle Density"), SerializeField, Range(0f, 3f)] private float windStreakFrequency = 1.2f;
    [Tooltip("Multiplier for particle travel speed in the wind direction.")]
    [InspectorName("Particle Flow Speed"), SerializeField, Range(0.1f, 3f)] private float windStreakSpeed = 1.0f;
    [Tooltip("Controls the stretched length of each wind wisp.")]
    [InspectorName("Particle Length"), SerializeField, Range(0.2f, 0.95f)] private float windStreakLength = 0.68f;
    [Tooltip("Approximate particle width in world metres.")]
    [InspectorName("Particle Width (m)"), SerializeField, Range(0.01f, 0.25f)] private float windStreakWidthMeters = 0.055f;
    [Tooltip("Maximum particle opacity. Wind strength also affects the spawn rate and speed.")]
    [InspectorName("Particle Opacity"), SerializeField, Range(0f, 1f)] private float windStreakOpacity = 0.72f;
    [Tooltip("Wind wisps are white by default; this can be used for a subtle warm or cool tint.")]
    [InspectorName("Particle Tint"), SerializeField] private Color windStreakColor = Color.white;

    [Header("Surface Shape")]
    [Tooltip("Horizontal displacement multiplier. Higher values create sharper, more curled crests.")]
    [SerializeField, Range(0f, 3f)] private float choppiness = 1.65f;
    [Tooltip("Foam begins where the horizontal displacement Jacobian drops below this value.")]
    [SerializeField, Range(0.5f, 1.2f)] private float foamBias = 0.98f;
    [SerializeField, Min(0f)] private float foamGain = 3f;

    [Header("Dynamic Sea State")]
    [Tooltip("Starting wave-strength multiplier. 1 = authored default, 2 = roughly twice the displacement amplitude.")]
    [SerializeField, Range(0f, 3f)] private float startSeaState = 1f;
    [Tooltip("Default transition duration used by the public sea-state preset methods.")]
    [SerializeField, Min(0f)] private float defaultSeaStateTransition = 3f;
    [Tooltip("Extra horizontal crest sharpening at high sea states.")]
    [SerializeField, Range(1f, 2f)] private float stormChoppinessMultiplier = 1.3f;
    [Tooltip("Extra whitecap generation at high sea states.")]
    [SerializeField, Range(1f, 3f)] private float stormFoamMultiplier = 1.6f;
    [Tooltip("Optional development shortcuts: F5 calm, F6 normal, F7 rough, F8 storm.")]
    [SerializeField] private bool enableDebugHotkeys = false;

    [Header("Shoreline Distance Field")]
    [InspectorName("Baked Shore Distance Map"), Tooltip("World-aligned distance field. Black = shore/land, white = offshore. Assign the baked distance texture here.")]
    [SerializeField] private Texture2D shoreDistanceTexture;
    [InspectorName("Painted Seabed Depth Modifier"), Tooltip("Optional grayscale map made by the Shore Distance Baker. Black = land/shallow, white = deep. It multiplies the inferred distance-based water depth.")]
    [SerializeField] private Texture2D shoreDepthModifierTexture;
    [InspectorName("Maximum Inferred Water Depth (m)"), Tooltip("Caps the distance-based depth estimate before the painted seabed modifier is applied.")]
    [SerializeField, Min(1f)] private float shoreMaxDepthMeters = 60f;
    [Tooltip("World-space XZ coordinate represented by the bottom-left corner of the shore map.")]
    [SerializeField] private Vector2 shoreMapOriginXZ = Vector2.zero;
    [Tooltip("World-space XZ size covered by the shore map. Must match the baker's World Size XZ.")]
    [SerializeField] private Vector2 shoreMapSizeXZ = new Vector2(8192f, 8192f);
    [Tooltip("Enable if the texture's vertical axis is opposite to world +Z.")]
    [SerializeField] private bool shoreMapFlipV;
    [InspectorName("Baked Distance Range (m)"), Tooltip("Must exactly match the Baker's Coastal Fade Distance. A white pixel represents this distance or farther.")]
    [SerializeField, Min(1f)] private float shoreDistanceRangeMeters = 150f;
    [InspectorName("Wave Recovery Starts (m)"), Tooltip("Distance from the shoreline where wave displacement begins recovering from the still-water edge.")]
    [SerializeField, Min(0f)] private float shoreFadeStartMeters = 0f;
    [InspectorName("Full Wave Height At (m)"), Tooltip("Distance offshore where the full open-ocean wave height is restored. Increase this for a longer, calmer shelf.")]
    [SerializeField, Min(1f)] private float shoreFadeEndMeters = 100f;
    [InspectorName("Residual Shore Wave Strength"), Tooltip("Small residual movement near shore. The final still-water band always eases to exactly zero at land.")]
    [SerializeField, Range(0f, 0.8f)] private float nearShoreWaveStrength = 0.08f;
    [InspectorName("Still-Water Band Width (m)"), Tooltip("Width of the final shoreline band. Wave displacement and normal detail ease to zero, but the water mesh remains visible and stationary.")]
    [SerializeField, Min(0.5f)] private float shoreEdgeFadeMeters = 10f;

    [Header("Shoaling and Breaking")]
    [InspectorName("Breaker Zone Starts (m)"), Tooltip("Offshore distance where the wave begins shoaling toward the breaker zone.")]
    [SerializeField, Min(0f)] private float shoreBreakStartMeters = 2f;
    [InspectorName("Strongest Breaking At (m)"), Tooltip("Distance from shore where crest compression and breaking foam are strongest.")]
    [SerializeField, Min(0.1f)] private float shoreBreakPeakMeters = 22f;
    [InspectorName("Breaker Zone Ends (m)"), Tooltip("Distance offshore where the breaker response returns to ordinary open-ocean motion.")]
    [SerializeField, Min(1f)] private float shoreBreakEndMeters = 90f;
    [InspectorName("Approx. Seabed Slope"), Tooltip("Inferred depth per metre offshore. 0.12 means about 12 m depth at 100 m from land. This is an approximation, not real bathymetry.")]
    [SerializeField, Range(0.01f, 0.5f)] private float shoreDepthPerMeter = 0.12f;
    [Tooltip("Depth-limited breaker index. Around 0.78 is a common shallow-water approximation (maximum wave height / depth).")]
    [SerializeField, Range(0.5f, 1f)] private float breakerHeightToDepth = 0.78f;
    [InspectorName("Maximum Gradual Height Gain"), Tooltip("Maximum height scale as waves shoal. The gain ramps gradually across the breaker zone and is depth-limited. Keep it near 1.1-1.3.")]
    [SerializeField, Range(1f, 1.5f)] private float shoreShoalingScale = 1.18f;
    [Tooltip("Horizontal compression multiplier at the breaker peak. Keep this modest to avoid huge self-intersections.")]
    [SerializeField, Range(1f, 2.5f)] private float shoreChoppiness = 1.55f;
    [Tooltip("Minimum horizontal displacement scale within the still-water approach band. The final shoreline motion still eases to zero.")]
    [SerializeField, Range(0f, 1f)] private float shoreHorizontalFloor = 0.12f;
    [Tooltip("Restores this fraction of horizontal wave travel at the breaker peak while vertical height remains attenuated. Higher values pitch crests forward more strongly.")]
    [SerializeField, Range(0f, 1f)] private float shoreHorizontalDrive = 0.45f;
    [Tooltip("Additional lighting-normal strength in the breaker zone.")]
    [SerializeField, Range(1f, 2f)] private float shoreNormalBoost = 1.25f;

    [Header("Coastal Foam / Wash")]
    [InspectorName("Full-Strength Shore Wash Width (m)"), Tooltip("Distance from land that receives the strongest continuous whitewash. Increase to carry foam farther offshore.")]
    [SerializeField, Min(1f)] private float shoreFoamWidthMeters = 60f;
    [InspectorName("Foam Fade Length (m)"), Tooltip("Approximate distance for foam to fall from full strength to about 6%. Uses a soft asymptotic falloff, not a hard cutoff. Try 70-110 m for beaches.")]
    [SerializeField, Min(0.1f)] private float shoreFoamEdgeFeatherMeters = 85f;
    [InspectorName("Dark-Land Foam Sensitivity"), Tooltip("Normalized distance-map threshold. Higher values include lighter gray pixels farther offshore in the foam expansion. 0.3 = mostly dark pixels; 0.7 = include much lighter grays.")]
    [SerializeField, Range(0f, 1f)] private float shoreFoamBlackThreshold = 0.45f;
    [InspectorName("Dark-Land Transition Softness"), Tooltip("Softness of the sensitivity threshold in the distance map. Higher values make the black-to-gray response more gradual.")]
    [SerializeField, Range(0.001f, 0.5f)] private float shoreFoamBlackSoftness = 0.18f;
    [InspectorName("Foam Boundary Irregularity (m)"), Tooltip("Multi-scale world-space noise displacement of the foam boundary. Increase to break up smooth concentric rings around islands.")]
    [SerializeField, Range(0f, 60f)] private float shoreFoamEdgeNoiseMeters = 14f;
    [Tooltip("Multiplier for the shore wash / breaking foam coverage.")]
    [SerializeField, Range(0f, 8f)] private float shoreFoamGain = 3.5f;
    [Tooltip("Open-ocean spectral whitecap multiplier. Lower values keep the middle of the ocean cleaner.")]
    [SerializeField, Range(0f, 1f)] private float openOceanFoamMultiplier = 0.035f;
    [Tooltip("Jacobian value below which horizontal compression starts contributing to a wave crest.")]
    [SerializeField, Range(0.5f, 1f)] private float shoreCrestJacobianThreshold = 0.94f;
    [Tooltip("Gain applied to crest compression when generating the peak / foam mask.")]
    [SerializeField, Range(1f, 20f)] private float shoreCrestCompressionGain = 8f;
    [Tooltip("How much positive crest height contributes to the peak mask.")]
    [SerializeField, Range(0f, 2f)] private float shoreCrestHeightGain = 0.45f;
    [Tooltip("Travel speed of the animated shore wash front, in metres per second. Positive values move the front toward the shoreline.")]
    [SerializeField, Range(0f, 12f)] private float shoreWashSpeed = 2.2f;
    [Tooltip("Distance between successive animated wash fronts, in metres.")]
    [SerializeField, Range(5f, 200f)] private float shoreWashSpacingMeters = 42f;
    [Tooltip("How strongly foam pulses as each wash front advances. Zero gives steady foam; one gives a stronger moving front.")]
    [SerializeField, Range(0f, 1f)] private float shoreWashPulseStrength = 0.65f;

    [Header("Shore Debug")]
    [SerializeField] private ShoreDebugView shoreDebugView = ShoreDebugView.Final;

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
    private static readonly int ShoreDistanceId = Shader.PropertyToID("_ShoreDistance");
    private static readonly int ShoreDepthModifierId = Shader.PropertyToID("_ShoreDepthModifier");
    private static readonly int ShoreMaxDepthMetersId = Shader.PropertyToID("_ShoreMaxDepthMeters");
    private static readonly int ShoreMapOriginId = Shader.PropertyToID("_ShoreMapOriginXZ");
    private static readonly int ShoreMapSizeId = Shader.PropertyToID("_ShoreMapSizeXZ");
    private static readonly int ShoreMapFlipVId = Shader.PropertyToID("_ShoreMapFlipV");
    private static readonly int ShoreDistanceRangeMetersId = Shader.PropertyToID("_ShoreDistanceRangeMeters");
    private static readonly int ShoreFadeStartMetersId = Shader.PropertyToID("_ShoreFadeStartMeters");
    private static readonly int ShoreFadeEndMetersId = Shader.PropertyToID("_ShoreFadeEndMeters");
    private static readonly int NearShoreWaveStrengthId = Shader.PropertyToID("_NearShoreWaveStrength");
    private static readonly int ShoreEdgeFadeMetersId = Shader.PropertyToID("_ShoreEdgeFadeMeters");
    private static readonly int ShoreBreakStartMetersId = Shader.PropertyToID("_ShoreBreakStartMeters");
    private static readonly int ShoreBreakPeakMetersId = Shader.PropertyToID("_ShoreBreakPeakMeters");
    private static readonly int ShoreBreakEndMetersId = Shader.PropertyToID("_ShoreBreakEndMeters");
    private static readonly int ShoreDepthPerMeterId = Shader.PropertyToID("_ShoreDepthPerMeter");
    private static readonly int BreakerHeightToDepthId = Shader.PropertyToID("_BreakerHeightToDepth");
    private static readonly int ShoreShoalingScaleId = Shader.PropertyToID("_ShoreShoalingScale");
    private static readonly int ShoreHorizontalFloorId = Shader.PropertyToID("_ShoreHorizontalFloor");
    private static readonly int ShoreHorizontalDriveId = Shader.PropertyToID("_ShoreHorizontalDrive");
    private static readonly int ShoreChoppinessId = Shader.PropertyToID("_ShoreChoppiness");
    private static readonly int ShoreNormalBoostId = Shader.PropertyToID("_ShoreNormalBoost");
    private static readonly int ShoreFoamWidthMetersId = Shader.PropertyToID("_ShoreFoamWidthMeters");
    private static readonly int ShoreFoamEdgeFeatherMetersId = Shader.PropertyToID("_ShoreFoamEdgeFeatherMeters");
    private static readonly int ShoreFoamBlackThresholdId = Shader.PropertyToID("_ShoreFoamBlackThreshold");
    private static readonly int ShoreFoamBlackSoftnessId = Shader.PropertyToID("_ShoreFoamBlackSoftness");
    private static readonly int ShoreFoamEdgeNoiseMetersId = Shader.PropertyToID("_ShoreFoamEdgeNoiseMeters");
    private static readonly int OpenOceanFoamMultiplierId = Shader.PropertyToID("_OpenOceanFoamMultiplier");
    private static readonly int ShoreFoamGainId = Shader.PropertyToID("_ShoreFoamGain");
    private static readonly int ShoreCrestJacobianThresholdId = Shader.PropertyToID("_ShoreCrestJacobianThreshold");
    private static readonly int ShoreCrestCompressionGainId = Shader.PropertyToID("_ShoreCrestCompressionGain");
    private static readonly int ShoreCrestHeightGainId = Shader.PropertyToID("_ShoreCrestHeightGain");
    private static readonly int ShoreWashSpeedId = Shader.PropertyToID("_ShoreWashSpeed");
    private static readonly int ShoreWashSpacingMetersId = Shader.PropertyToID("_ShoreWashSpacingMeters");
    private static readonly int ShoreWashPulseStrengthId = Shader.PropertyToID("_ShoreWashPulseStrength");
    private static readonly int ShoreDebugViewId = Shader.PropertyToID("_ShoreDebugView");

    private static readonly int[] PatchLengthIds =
    {
        Shader.PropertyToID("_OceanPatchLength0"),
        Shader.PropertyToID("_OceanPatchLength1"),
        Shader.PropertyToID("_OceanPatchLength2")
    };

    private MeshRenderer meshRenderer;
    private MeshFilter meshFilter;
    private Mesh boundsSourceMesh;
    private MaterialPropertyBlock propertyBlock;
    private Bounds originalRendererLocalBounds;
    private bool hasOriginalRendererLocalBounds;
    private Vector3 lastExpandedBoundsScale;
    private bool hasExpandedBoundsScale;

    private int kernelInit;
    private int kernelEvolve;
    private int kernelFftX;
    private int kernelFftY;
    private int kernelBuildMaps;
    private int kernelSampleSurface;
    private int log2Resolution;

    // Camera immersion samples are prioritized over bulk physics probe batches.
    // This prevents many boats from starving the player's underwater detection.
    private readonly Queue<PendingSurfaceQuery> pendingCameraSurfaceQueries =
        new Queue<PendingSurfaceQuery>();
    private readonly Queue<PendingSurfaceQuery> pendingSurfaceQueries =
        new Queue<PendingSurfaceQuery>();
    private readonly Vector2[] surfaceQueryPositionScratch =
        new Vector2[MaxSurfaceQueriesPerBatch];
    private SurfaceQuerySlot[] surfaceQuerySlots;

    private bool resourcesReady;
    private bool rebuildRequested = true;
    private bool spectrumDirty = true;
    private int spectrumSettingsHash;
    private bool hasSpectrumSettingsHash;
    private int allocatedResolution;
    private ComputeShader allocatedCompute;
    private int allocatedCascadeCount;

    private float currentSeaStateStrength = 1f;
    private float targetSeaStateStrength = 1f;
    private float seaStateVelocity;
    private float seaStateSmoothTime = 3f;

    private Vector2 currentWindDirectionXZ = Vector2.right;
    private Vector2 targetWindDirectionXZ = Vector2.right;
    private float currentWindDirectionAngle;
    private float targetWindDirectionAngle;
    private float windDirectionAngleVelocity;
    private float currentWindStrength = 0.65f;
    private float targetWindStrength = 0.65f;
    private float windStrengthVelocity;
    private float windDirectionSmoothTime = 1.0f;
    private float windStrengthSmoothTime = 1.0f;
    private float currentWindStreakFrequency = 1.2f;
    private float targetWindStreakFrequency = 1.2f;
    private float windStreakFrequencyVelocity;
    private float windStreakFrequencySmoothTime = 0.35f;

    /// <summary>Base water-plane height in world space. The FFT shader adds displacement around this level.</summary>
    public float WaterLevelY => transform.position.y;

    public float CurrentSeaStateStrength => currentSeaStateStrength;
    public float TargetSeaStateStrength => targetSeaStateStrength;
    public Vector2 CurrentWindDirectionXZ => currentWindDirectionXZ;
    public float CurrentWindStrength => currentWindStrength;
    public float TargetWindStrength => targetWindStrength;
    public bool WindStreaksEnabled => showWindStreaks;
    public float WindStreakFrequency => currentWindStreakFrequency;
    public float WindParticleSpeedMultiplier => windStreakSpeed;
    public float WindParticleLength => windStreakLength;
    public float WindParticleWidthMeters => windStreakWidthMeters;
    public float WindParticleOpacity => windStreakOpacity;
    public Color WindParticleTint => windStreakColor;
    public bool CanSampleSurface => resourcesReady &&
        surfaceQuerySlots != null && SystemInfo.supportsAsyncGPUReadback;

    /// <summary>
    /// Queues one or more world-space XZ samples against the current GPU FFT maps.
    /// Results arrive asynchronously to avoid a GPU/CPU synchronization stall.
    /// Call from FixedUpdate for buoyancy or LateUpdate for camera immersion.
    /// </summary>
    public bool RequestSurfaceSamples(
        Vector3[] worldPositions,
        Action<SurfaceSample[]> callback)
    {
        if (!CanQueueSurfaceQuery(worldPositions, callback))
            return false;

        pendingSurfaceQueries.Enqueue(CreatePendingQuery(worldPositions, callback));
        return true;
    }

    public bool RequestSurfaceSample(Vector3 worldPosition, Action<SurfaceSample> callback)
    {
        if (callback == null)
            return false;

        return RequestSurfaceSamples(new[] { worldPosition }, samples =>
        {
            callback(samples != null && samples.Length > 0 ? samples[0] : default);
        });
    }

    /// <summary>
    /// Render-time variant for the output camera. Call from LateUpdate after
    /// Cinemachine has applied the final camera pose; it dispatches the queued
    /// query immediately against the ocean maps generated during Update.
    /// </summary>
    public bool RequestSurfaceSampleForCamera(Vector3 worldPosition, Action<SurfaceSample> callback)
    {
        if (callback == null || !CanSampleSurface ||
            !IsFinite(worldPosition.x) || !IsFinite(worldPosition.y) || !IsFinite(worldPosition.z) ||
            pendingCameraSurfaceQueries.Count >= MaxPendingCameraQueries ||
            pendingCameraSurfaceQueries.Count + pendingSurfaceQueries.Count >= MaxPendingSurfaceQueries)
            return false;

        pendingCameraSurfaceQueries.Enqueue(CreatePendingQuery(
            new[] { worldPosition }, samples =>
            {
                callback(samples != null && samples.Length > 0 ? samples[0] : default);
            }));

        // Dispatch immediately when a slot is available. Camera queries are
        // dequeued before buoyancy batches in ProcessSurfaceSampleRequests.
        ProcessSurfaceSampleRequests();
        return true;
    }

    private bool CanQueueSurfaceQuery(Vector3[] worldPositions, Action<SurfaceSample[]> callback)
    {
        if (worldPositions == null || worldPositions.Length == 0 ||
            worldPositions.Length > MaxSurfaceQueriesPerBatch || callback == null ||
            !CanSampleSurface || pendingSurfaceQueries.Count >= MaxPendingPhysicsQueries ||
            pendingCameraSurfaceQueries.Count + pendingSurfaceQueries.Count >= MaxPendingSurfaceQueries)
            return false;

        for (int i = 0; i < worldPositions.Length; i++)
        {
            Vector3 p = worldPositions[i];
            if (!IsFinite(p.x) || !IsFinite(p.y) || !IsFinite(p.z))
                return false;
        }
        return true;
    }

    private static PendingSurfaceQuery CreatePendingQuery(
        Vector3[] worldPositions, Action<SurfaceSample[]> callback)
    {
        return new PendingSurfaceQuery
        {
            positions = (Vector3[])worldPositions.Clone(),
            callback = callback
        };
    }

    private bool HasPendingSurfaceQueries =>
        pendingCameraSurfaceQueries.Count > 0 || pendingSurfaceQueries.Count > 0;

    private PendingSurfaceQuery PeekNextSurfaceQuery()
    {
        return pendingCameraSurfaceQueries.Count > 0
            ? pendingCameraSurfaceQueries.Peek()
            : pendingSurfaceQueries.Peek();
    }

    private PendingSurfaceQuery DequeueNextSurfaceQuery()
    {
        return pendingCameraSurfaceQueries.Count > 0
            ? pendingCameraSurfaceQueries.Dequeue()
            : pendingSurfaceQueries.Dequeue();
    }

    private void OnEnable()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        meshFilter = GetComponent<MeshFilter>();
        boundsSourceMesh = meshFilter != null ? meshFilter.sharedMesh : null;
        propertyBlock = new MaterialPropertyBlock();
        CaptureAndExpandRendererBounds();
        rebuildRequested = true;
        spectrumDirty = true;
        currentSeaStateStrength = Mathf.Clamp(startSeaState, 0f, 3f);
        targetSeaStateStrength = currentSeaStateStrength;
        seaStateVelocity = 0f;
        seaStateSmoothTime = Mathf.Max(0.01f, defaultSeaStateTransition);

        windDirectionXZ = NormalizeWindDirection(windDirectionXZ);
        currentWindDirectionXZ = windDirectionXZ;
        targetWindDirectionXZ = windDirectionXZ;
        currentWindDirectionAngle = Mathf.Atan2(windDirectionXZ.y, windDirectionXZ.x) * Mathf.Rad2Deg;
        targetWindDirectionAngle = currentWindDirectionAngle;
        windDirectionAngleVelocity = 0f;
        currentWindStrength = Mathf.Clamp01(windStrength);
        targetWindStrength = currentWindStrength;
        windStrengthVelocity = 0f;
        windDirectionSmoothTime = 1.0f;
        windStrengthSmoothTime = 1.0f;
        currentWindStreakFrequency = Mathf.Clamp(windStreakFrequency, 0f, 3f);
        targetWindStreakFrequency = currentWindStreakFrequency;
        windStreakFrequencyVelocity = 0f;
        windStreakFrequencySmoothTime = 0.35f;
        spectrumSettingsHash = ComputeSpectrumSettingsHash();
        hasSpectrumSettingsHash = true;
    }

    private void OnDisable()
    {
        ReleaseResources();
        RestoreRendererBounds();
    }

    private void OnValidate()
    {
        resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Clamp(resolution, 64, 512)), 64, 512);
        rendererBoundsPaddingMeters = Mathf.Max(1f, rendererBoundsPaddingMeters);
        windSpeed = Mathf.Max(0.1f, windSpeed);
        windDirectionXZ = NormalizeWindDirection(windDirectionXZ);
        windStrength = Mathf.Clamp01(windStrength);
        windStreakFrequency = Mathf.Clamp(windStreakFrequency, 0f, 3f);
        if (Application.isPlaying)
        {
            targetWindStreakFrequency = windStreakFrequency;
        }
        else
        {
            currentWindStreakFrequency = windStreakFrequency;
            targetWindStreakFrequency = windStreakFrequency;
        }
        windStreakSpeed = Mathf.Clamp(windStreakSpeed, 0.1f, 3f);
        windStreakLength = Mathf.Clamp(windStreakLength, 0.2f, 0.95f);
        windStreakWidthMeters = Mathf.Clamp(windStreakWidthMeters, 0.01f, 0.25f);
        windStreakOpacity = Mathf.Clamp01(windStreakOpacity);
        if (Application.isPlaying)
        {
            targetWindDirectionXZ = windDirectionXZ;
            targetWindDirectionAngle = Mathf.Atan2(windDirectionXZ.y, windDirectionXZ.x) * Mathf.Rad2Deg;
            targetWindStrength = windStrength;
        }
        phillipsAmplitude = Mathf.Max(0f, phillipsAmplitude);
        smallWaveDamping = Mathf.Max(0f, smallWaveDamping);
        foamGain = Mathf.Max(0f, foamGain);
        startSeaState = Mathf.Clamp(startSeaState, 0f, 3f);
        defaultSeaStateTransition = Mathf.Max(0f, defaultSeaStateTransition);
        shoreMapSizeXZ.x = Mathf.Max(1f, shoreMapSizeXZ.x);
        shoreMapSizeXZ.y = Mathf.Max(1f, shoreMapSizeXZ.y);
        shoreDistanceRangeMeters = Mathf.Max(1f, shoreDistanceRangeMeters);
        shoreMaxDepthMeters = Mathf.Max(1f, shoreMaxDepthMeters);
        shoreFadeStartMeters = Mathf.Clamp(shoreFadeStartMeters, 0f, shoreDistanceRangeMeters - 0.1f);
        shoreFadeEndMeters = Mathf.Clamp(shoreFadeEndMeters, shoreFadeStartMeters + 0.1f, shoreDistanceRangeMeters);
        nearShoreWaveStrength = Mathf.Clamp01(nearShoreWaveStrength);
        shoreEdgeFadeMeters = Mathf.Max(0.5f, shoreEdgeFadeMeters);
        shoreBreakStartMeters = Mathf.Clamp(shoreBreakStartMeters, 0f, Mathf.Max(0f, shoreDistanceRangeMeters - 0.2f));
        shoreBreakPeakMeters = Mathf.Clamp(
            shoreBreakPeakMeters, shoreBreakStartMeters + 0.1f, Mathf.Max(shoreBreakStartMeters + 0.1f, shoreDistanceRangeMeters - 0.1f));
        shoreBreakEndMeters = Mathf.Clamp(
            shoreBreakEndMeters, shoreBreakPeakMeters + 0.1f, shoreDistanceRangeMeters);
        shoreDepthPerMeter = Mathf.Clamp(shoreDepthPerMeter, 0.01f, 0.5f);
        breakerHeightToDepth = Mathf.Clamp(breakerHeightToDepth, 0.5f, 1f);
        shoreShoalingScale = Mathf.Clamp(shoreShoalingScale, 1f, 1.5f);
        shoreChoppiness = Mathf.Clamp(shoreChoppiness, 1f, 2.5f);
        shoreHorizontalFloor = Mathf.Clamp01(shoreHorizontalFloor);
        shoreHorizontalDrive = Mathf.Clamp01(shoreHorizontalDrive);
        shoreNormalBoost = Mathf.Clamp(shoreNormalBoost, 1f, 2f);
        shoreFoamEdgeFeatherMeters = Mathf.Max(0.1f, shoreFoamEdgeFeatherMeters);
        shoreFoamWidthMeters = Mathf.Max(1f, shoreFoamWidthMeters);
        shoreFoamBlackSoftness = Mathf.Clamp(shoreFoamBlackSoftness, 0.001f, 0.5f);
        shoreFoamEdgeNoiseMeters = Mathf.Clamp(shoreFoamEdgeNoiseMeters, 0f, 60f);
        shoreFoamBlackThreshold = Mathf.Clamp(
            shoreFoamBlackThreshold, 0f, 1f - shoreFoamBlackSoftness);
        shoreFoamGain = Mathf.Clamp(shoreFoamGain, 0f, 8f);
        openOceanFoamMultiplier = Mathf.Clamp01(openOceanFoamMultiplier);
        shoreCrestJacobianThreshold = Mathf.Clamp(shoreCrestJacobianThreshold, 0.5f, 1f);
        shoreCrestCompressionGain = Mathf.Clamp(shoreCrestCompressionGain, 1f, 20f);
        shoreCrestHeightGain = Mathf.Clamp(shoreCrestHeightGain, 0f, 2f);
        shoreWashSpeed = Mathf.Clamp(shoreWashSpeed, 0f, 12f);
        shoreWashSpacingMeters = Mathf.Clamp(shoreWashSpacingMeters, 5f, 200f);
        shoreWashPulseStrength = Mathf.Clamp01(shoreWashPulseStrength);
        if (Application.isPlaying && meshRenderer != null && hasOriginalRendererLocalBounds)
            CaptureAndExpandRendererBounds();

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

        // Inspector changes to coastal fade / foam / crest controls are applied
        // by ApplyShoreParameters every frame. Do not release and recreate every
        // FFT RenderTexture just because an artist adjusts a shoreline slider.
        // Only settings that affect allocated GPU resources require a rebuild.
        bool resourceLayoutChanged = !resourcesReady ||
                                     resolution != allocatedResolution ||
                                     oceanCompute != allocatedCompute ||
                                     cascades == null ||
                                     cascades.Length != allocatedCascadeCount;
        if (resourceLayoutChanged)
            rebuildRequested = true;

        // Only H0/spectrum-defining settings regenerate the random spectrum.
        // Shore, wind-streak density, runtime wind direction and wind strength are
        // live uniforms and must not trigger unnecessary spectrum dispatches.
        int currentSpectrumHash = ComputeSpectrumSettingsHash();
        if (!hasSpectrumSettingsHash || currentSpectrumHash != spectrumSettingsHash)
        {
            spectrumSettingsHash = currentSpectrumHash;
            hasSpectrumSettingsHash = true;
            spectrumDirty = true;
        }
    }

    private int ComputeSpectrumSettingsHash()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + windSpeed.GetHashCode();
            hash = hash * 31 + phillipsAmplitude.GetHashCode();
            hash = hash * 31 + smallWaveDamping.GetHashCode();
            hash = hash * 31 + randomSeed.GetHashCode();
            if (cascades != null)
            {
                hash = hash * 31 + cascades.Length;
                for (int i = 0; i < cascades.Length; i++)
                {
                    CascadeSettings c = cascades[i];
                    if (c == null)
                    {
                        hash = hash * 31;
                        continue;
                    }
                    hash = hash * 31 + c.patchLength.GetHashCode();
                    hash = hash * 31 + c.minWavelength.GetHashCode();
                    hash = hash * 31 + c.maxWavelength.GetHashCode();
                    hash = hash * 31 + c.amplitudeScale.GetHashCode();
                }
            }
            return hash;
        }
    }

    private void Update()
    {
        UpdateRendererBoundsIfScaleChanged();
        HandleDebugHotkeys();
        UpdateSeaState(Time.deltaTime);
        UpdateWind(Time.deltaTime);

        if (rebuildRequested || !resourcesReady)
        {
            if (!InitializeResources())
                return;
        }

        SetCommonParameters();
        ApplyShoreParameters();

        if (spectrumDirty)
        {
            for (int i = 0; i < cascades.Length; i++)
                GenerateInitialSpectrum(cascades[i], i);

            spectrumDirty = false;
        }

        for (int i = 0; i < cascades.Length; i++)
            SimulateCascade(cascades[i], i);
    }

    private void LateUpdate()
    {
        ProcessSurfaceSampleRequests();
    }

    private void InitializeSurfaceQuerySlots()
    {
        ReleaseSurfaceQuerySlots();

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            Debug.LogWarning(
                "SpectralOceanFFT: Async GPU readback is unavailable. Underwater wave tracking and buoyancy sampling will use their fallback behavior.",
                this);
            return;
        }

        try
        {
            surfaceQuerySlots = new SurfaceQuerySlot[SurfaceQuerySlotCount];
            for (int i = 0; i < surfaceQuerySlots.Length; i++)
            {
                surfaceQuerySlots[i] = new SurfaceQuerySlot
                {
                    positions = new ComputeBuffer(
                        MaxSurfaceQueriesPerBatch, sizeof(float) * 2, ComputeBufferType.Structured),
                    results = new ComputeBuffer(
                        MaxSurfaceQueriesPerBatch, Marshal.SizeOf<GpuSurfaceQueryResult>(), ComputeBufferType.Structured)
                };
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "SpectralOceanFFT: Could not allocate asynchronous surface-query buffers. Sampling is disabled. " + exception.Message,
                this);
            ReleaseSurfaceQuerySlots();
        }
    }

    private void ProcessSurfaceSampleRequests()
    {
        if (!resourcesReady || surfaceQuerySlots == null || !HasPendingSurfaceQueries)
            return;

        SurfaceQuerySlot slot = null;
        for (int i = 0; i < surfaceQuerySlots.Length; i++)
        {
            SurfaceQuerySlot candidate = surfaceQuerySlots[i];
            if (candidate != null && !candidate.inFlight &&
                candidate.positions != null && candidate.results != null)
            {
                slot = candidate;
                break;
            }
        }

        if (slot == null)
            return;

        List<SurfaceQuerySlice> slices = new List<SurfaceQuerySlice>(8);
        int sampleCount = 0;
        float sampleTime = Time.realtimeSinceStartup;
        float waterLevel = WaterLevelY;

        while (HasPendingSurfaceQueries)
        {
            PendingSurfaceQuery query = PeekNextSurfaceQuery();
            if (query.positions == null || query.positions.Length == 0)
            {
                DequeueNextSurfaceQuery();
                InvokeInvalidSurfaceQuery(query.callback, 0);
                continue;
            }

            if (sampleCount + query.positions.Length > MaxSurfaceQueriesPerBatch)
            {
                if (sampleCount == 0)
                {
                    DequeueNextSurfaceQuery();
                    InvokeInvalidSurfaceQuery(query.callback, query.positions.Length);
                }
                break;
            }

            DequeueNextSurfaceQuery();
            int offset = sampleCount;
            for (int i = 0; i < query.positions.Length; i++)
            {
                Vector3 p = query.positions[i];
                surfaceQueryPositionScratch[sampleCount++] = new Vector2(p.x, p.z);
            }

            slices.Add(new SurfaceQuerySlice
            {
                query = query,
                offset = offset,
                sampleTime = sampleTime,
                waterLevelY = waterLevel
            });
        }

        if (sampleCount == 0)
            return;

        slot.slices = slices;
        slot.resultCount = sampleCount;
        slot.inFlight = true;

        try
        {
            // Keep buffer upload, binding and dispatch in the same guarded block.
            // Previously an exception from SetData/SetTexture happened before the
            // try/catch and left camera/physics callbacks waiting forever.
            slot.positions.SetData(surfaceQueryPositionScratch, 0, 0, sampleCount);
            BindSurfaceQueryParameters(slot, sampleCount);
            oceanCompute.Dispatch(kernelSampleSurface,
                Mathf.CeilToInt(sampleCount / 64f), 1, 1);
            AsyncGPUReadback.Request(slot.results, request => CompleteSurfaceSampleReadback(slot, request));
        }
        catch (Exception exception)
        {
            slot.inFlight = false;
            slot.slices = null;
            slot.resultCount = 0;
            Debug.LogWarning("SpectralOceanFFT: Surface query dispatch failed: " + exception.Message, this);
            for (int i = 0; i < slices.Count; i++)
                InvokeInvalidSurfaceQuery(slices[i].query.callback, slices[i].query.positions.Length);
        }
    }

    private void BindSurfaceQueryParameters(SurfaceQuerySlot slot, int sampleCount)
    {
        oceanCompute.SetInt("_SampleResolution", resolution);
        oceanCompute.SetInt("_SampleCount", sampleCount);
        oceanCompute.SetFloat("_SamplePatchLength0", cascades[0].patchLength);
        oceanCompute.SetFloat("_SamplePatchLength1", cascades[1].patchLength);
        oceanCompute.SetFloat("_SamplePatchLength2", cascades[2].patchLength);
        oceanCompute.SetVector("_SampleShoreMapOriginXZ",
            new Vector4(shoreMapOriginXZ.x, shoreMapOriginXZ.y, 0f, 0f));
        oceanCompute.SetVector("_SampleShoreMapSizeXZ",
            new Vector4(Mathf.Max(1f, shoreMapSizeXZ.x), Mathf.Max(1f, shoreMapSizeXZ.y), 0f, 0f));
        oceanCompute.SetFloat("_SampleShoreMapFlipV", shoreMapFlipV ? 1f : 0f);
        oceanCompute.SetFloat("_SampleShoreDistanceRangeMeters", shoreDistanceRangeMeters);
        oceanCompute.SetFloat("_SampleShoreMaxDepthMeters", shoreMaxDepthMeters);
        oceanCompute.SetFloat("_SampleShoreFadeStartMeters", shoreFadeStartMeters);
        oceanCompute.SetFloat("_SampleShoreFadeEndMeters", shoreFadeEndMeters);
        oceanCompute.SetFloat("_SampleNearShoreWaveStrength", nearShoreWaveStrength);
        oceanCompute.SetFloat("_SampleShoreEdgeFadeMeters", shoreEdgeFadeMeters);
        oceanCompute.SetFloat("_SampleShoreBreakStartMeters", shoreBreakStartMeters);
        oceanCompute.SetFloat("_SampleShoreBreakPeakMeters", shoreBreakPeakMeters);
        oceanCompute.SetFloat("_SampleShoreBreakEndMeters", shoreBreakEndMeters);
        oceanCompute.SetFloat("_SampleShoreDepthPerMeter", shoreDepthPerMeter);
        oceanCompute.SetFloat("_SampleBreakerHeightToDepth", breakerHeightToDepth);
        oceanCompute.SetFloat("_SampleShoreShoalingScale", shoreShoalingScale);
        oceanCompute.SetFloat("_SampleShoreHorizontalFloor", shoreHorizontalFloor);
        oceanCompute.SetFloat("_SampleShoreHorizontalDrive", shoreHorizontalDrive);
        oceanCompute.SetFloat("_SampleShoreChoppiness", shoreChoppiness);
        oceanCompute.SetFloat("_SampleShoreNormalBoost", shoreNormalBoost);

        oceanCompute.SetTexture(kernelSampleSurface, "_SampleDisplacement0", cascades[0].displacement);
        oceanCompute.SetTexture(kernelSampleSurface, "_SampleDisplacement1", cascades[1].displacement);
        oceanCompute.SetTexture(kernelSampleSurface, "_SampleDisplacement2", cascades[2].displacement);
        oceanCompute.SetTexture(kernelSampleSurface, "_SampleNormal0", cascades[0].normal);
        oceanCompute.SetTexture(kernelSampleSurface, "_SampleNormal1", cascades[1].normal);
        oceanCompute.SetTexture(kernelSampleSurface, "_SampleNormal2", cascades[2].normal);
        oceanCompute.SetTexture(kernelSampleSurface, "_SampleShoreDistance",
            shoreDistanceTexture != null ? shoreDistanceTexture : Texture2D.whiteTexture);
        oceanCompute.SetTexture(kernelSampleSurface, "_SampleShoreDepth",
            shoreDepthModifierTexture != null ? shoreDepthModifierTexture : Texture2D.whiteTexture);
        oceanCompute.SetBuffer(kernelSampleSurface, "_SurfaceQueryPositions", slot.positions);
        oceanCompute.SetBuffer(kernelSampleSurface, "_SurfaceQueryResults", slot.results);
    }

    private void CompleteSurfaceSampleReadback(
        SurfaceQuerySlot slot, AsyncGPUReadbackRequest request)
    {
        List<SurfaceQuerySlice> slices = slot.slices;
        bool success = !request.hasError && slices != null;
        GpuSurfaceQueryResult[] results = null;

        if (success)
        {
            try
            {
                var data = request.GetData<GpuSurfaceQueryResult>();
                int expectedCount = Mathf.Clamp(slot.resultCount, 0, MaxSurfaceQueriesPerBatch);
                success = expectedCount > 0 && data.Length >= expectedCount;
                if (success)
                {
                    results = new GpuSurfaceQueryResult[expectedCount];
                    for (int i = 0; i < expectedCount; i++)
                        results[i] = data[i];
                }
            }
            catch (Exception exception)
            {
                success = false;
                Debug.LogWarning("SpectralOceanFFT: Could not read surface-query results: " + exception.Message, this);
            }
        }

        if (slices != null)
        {
            for (int s = 0; s < slices.Count; s++)
            {
                SurfaceQuerySlice slice = slices[s];
                SurfaceSample[] samples = new SurfaceSample[slice.query.positions.Length];
                for (int i = 0; i < samples.Length; i++)
                {
                    int resultIndex = slice.offset + i;
                    if (!success || results == null || resultIndex >= results.Length)
                    {
                        samples[i] = MakeInvalidSurfaceSample(slice.query.positions[i], slice.sampleTime, slice.waterLevelY);
                        continue;
                    }

                    GpuSurfaceQueryResult gpu = results[resultIndex];
                    bool valid = gpu.displacement.w > 0.5f &&
                        IsFinite(gpu.displacement) && IsFinite(gpu.normal);
                    Vector3 basePosition = slice.query.positions[i];
                    Vector3 displacement = valid
                        ? new Vector3(gpu.displacement.x, gpu.displacement.y, gpu.displacement.z)
                        : Vector3.zero;
                    Vector3 normal = valid
                        ? new Vector3(gpu.normal.x, gpu.normal.y, gpu.normal.z).normalized
                        : Vector3.up;

                    samples[i] = new SurfaceSample
                    {
                        BasePosition = basePosition,
                        Position = new Vector3(
                            basePosition.x + displacement.x,
                            slice.waterLevelY + displacement.y,
                            basePosition.z + displacement.z),
                        Normal = normal,
                        IsValid = valid,
                        SampleTime = slice.sampleTime
                    };
                }

                try
                {
                    slice.query.callback?.Invoke(samples);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }

        slot.inFlight = false;
        slot.slices = null;
        slot.resultCount = 0;
        if (slot.disposeWhenComplete)
            slot.Dispose();
    }

    private static bool IsFinite(Vector4 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) &&
               IsFinite(value.z) && IsFinite(value.w);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static SurfaceSample MakeInvalidSurfaceSample(
        Vector3 basePosition, float sampleTime, float waterLevel)
    {
        return new SurfaceSample
        {
            BasePosition = basePosition,
            Position = new Vector3(basePosition.x, waterLevel, basePosition.z),
            Normal = Vector3.up,
            IsValid = false,
            SampleTime = sampleTime
        };
    }

    private static void InvokeInvalidSurfaceQuery(Action<SurfaceSample[]> callback, int count)
    {
        if (callback == null)
            return;
        callback(new SurfaceSample[Mathf.Max(0, count)]);
    }

    private void ReleaseSurfaceQuerySlots()
    {
        while (pendingCameraSurfaceQueries.Count > 0)
        {
            PendingSurfaceQuery query = pendingCameraSurfaceQueries.Dequeue();
            InvokeInvalidSurfaceQuery(query.callback, query.positions != null ? query.positions.Length : 0);
        }
        while (pendingSurfaceQueries.Count > 0)
        {
            PendingSurfaceQuery query = pendingSurfaceQueries.Dequeue();
            InvokeInvalidSurfaceQuery(query.callback, query.positions != null ? query.positions.Length : 0);
        }

        if (surfaceQuerySlots == null)
            return;

        for (int i = 0; i < surfaceQuerySlots.Length; i++)
        {
            SurfaceQuerySlot slot = surfaceQuerySlots[i];
            if (slot == null)
                continue;

            if (slot.inFlight)
                slot.disposeWhenComplete = true;
            else
                slot.Dispose();
        }
        surfaceQuerySlots = null;
    }

    private void CaptureAndExpandRendererBounds()
    {
        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();
        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();
        if (meshRenderer == null)
            return;

        if (!hasOriginalRendererLocalBounds)
        {
            originalRendererLocalBounds = meshRenderer.localBounds;
            hasOriginalRendererLocalBounds = true;
        }

        Bounds expanded = originalRendererLocalBounds;
        Vector3 scale = transform.lossyScale;
        lastExpandedBoundsScale = scale;
        hasExpandedBoundsScale = true;
        float padX = rendererBoundsPaddingMeters / Mathf.Max(Mathf.Abs(scale.x), 0.0001f);
        float padY = rendererBoundsPaddingMeters / Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
        float padZ = rendererBoundsPaddingMeters / Mathf.Max(Mathf.Abs(scale.z), 0.0001f);
        expanded.Expand(new Vector3(padX * 2f, padY * 2f, padZ * 2f));
        meshRenderer.localBounds = expanded;
    }

    private void RestoreRendererBounds()
    {
        if (meshRenderer != null && hasOriginalRendererLocalBounds)
            meshRenderer.localBounds = originalRendererLocalBounds;
        hasOriginalRendererLocalBounds = false;
        hasExpandedBoundsScale = false;
        boundsSourceMesh = null;
    }

    private void UpdateRendererBoundsIfScaleChanged()
    {
        if (meshRenderer == null)
            return;
        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();

        Mesh currentMesh = meshFilter != null ? meshFilter.sharedMesh : null;
        if (currentMesh != boundsSourceMesh)
        {
            // A runtime mesh replacement changes the source bounds. Capture the
            // new mesh bounds before applying the GPU displacement padding.
            boundsSourceMesh = currentMesh;
            originalRendererLocalBounds = currentMesh != null
                ? currentMesh.bounds
                : meshRenderer.localBounds;
            meshRenderer.localBounds = originalRendererLocalBounds;
            hasOriginalRendererLocalBounds = true;
            hasExpandedBoundsScale = false;
        }

        if (!hasOriginalRendererLocalBounds)
        {
            originalRendererLocalBounds = meshRenderer.localBounds;
            hasOriginalRendererLocalBounds = true;
            hasExpandedBoundsScale = false;
        }

        Vector3 currentScale = transform.lossyScale;
        if (!hasExpandedBoundsScale ||
            (currentScale - lastExpandedBoundsScale).sqrMagnitude > 0.000001f)
            CaptureAndExpandRendererBounds();
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
        if (!hasOriginalRendererLocalBounds)
            CaptureAndExpandRendererBounds();

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Clamp(resolution, 64, 512)), 64, 512);
        log2Resolution = Mathf.RoundToInt(Mathf.Log(resolution, 2f));

        kernelInit = oceanCompute.FindKernel("InitSpectrum");
        kernelEvolve = oceanCompute.FindKernel("EvolveSpectrum");
        kernelFftX = oceanCompute.FindKernel("FFT_X");
        kernelFftY = oceanCompute.FindKernel("FFT_Y");
        kernelBuildMaps = oceanCompute.FindKernel("BuildMaps");
        kernelSampleSurface = oceanCompute.FindKernel("SampleSurface");

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

        InitializeSurfaceQuerySlots();

        resourcesReady = true;
        rebuildRequested = false;
        spectrumDirty = true;
        allocatedResolution = resolution;
        allocatedCompute = oceanCompute;
        allocatedCascadeCount = cascades.Length;
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
        Vector2 wind = currentWindDirectionXZ.sqrMagnitude > 0.000001f
            ? currentWindDirectionXZ.normalized
            : Vector2.right;

        // Direction is evaluated in EvolveSpectrum every frame, so runtime wind
        // shifts rotate the spectrum without regenerating random H0 textures.
        // Strength is applied after the FFT and remains inexpensive to animate.
        float normalizedWind = Mathf.Clamp01(currentWindStrength);
        float windWaveScale = Mathf.Lerp(0.12f, 1.75f,
            Mathf.Pow(normalizedWind, 1.25f));
        float windChopScale = Mathf.Lerp(0.65f, 1.35f, normalizedWind);
        float windFoamScale = Mathf.Lerp(0.55f, 1.5f, normalizedWind);

        oceanCompute.SetInt("_Resolution", resolution);
        oceanCompute.SetInt("_Log2Resolution", log2Resolution);
        oceanCompute.SetInt("_Seed", randomSeed);
        oceanCompute.SetVector("_WindDirection", new Vector4(wind.x, wind.y, 0f, 0f));
        oceanCompute.SetFloat("_WindSpeed", windSpeed);
        oceanCompute.SetFloat("_WindStrength", normalizedWind);
        oceanCompute.SetFloat("_WindWaveStrength", windWaveScale);
        oceanCompute.SetFloat("_PhillipsAmplitude", phillipsAmplitude);
        oceanCompute.SetFloat("_SmallWaveDamping", smallWaveDamping);
        oceanCompute.SetFloat("_BackwindDamping", backwindDamping);
        oceanCompute.SetFloat("_TimeSeconds", Time.time);
        oceanCompute.SetFloat("_FoamBias", foamBias);
        oceanCompute.SetFloat("_FoamGain", foamGain * GetStormFoamMultiplier() * windFoamScale);
        oceanCompute.SetFloat("_SeaStateStrength", currentSeaStateStrength);
        oceanCompute.SetFloat("_Choppiness", choppiness * GetStormChoppinessMultiplier() * windChopScale);
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
        ApplyShoreParameters();
    }

    private void ApplyShoreParameters()
    {
        if (meshRenderer == null)
            return;

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        meshRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetTexture(ShoreDistanceId,
            shoreDistanceTexture != null ? shoreDistanceTexture : Texture2D.whiteTexture);
        propertyBlock.SetTexture(ShoreDepthModifierId,
            shoreDepthModifierTexture != null ? shoreDepthModifierTexture : Texture2D.whiteTexture);
        propertyBlock.SetFloat(ShoreMaxDepthMetersId, shoreMaxDepthMeters);
        propertyBlock.SetVector(ShoreMapOriginId,
            new Vector4(shoreMapOriginXZ.x, shoreMapOriginXZ.y, 0f, 0f));
        propertyBlock.SetVector(ShoreMapSizeId,
            new Vector4(Mathf.Max(1f, shoreMapSizeXZ.x), Mathf.Max(1f, shoreMapSizeXZ.y), 0f, 0f));
        propertyBlock.SetFloat(ShoreMapFlipVId, shoreMapFlipV ? 1f : 0f);

        // The baker stores distance / Coastal Fade Distance in the red channel.
        // Convert it back to world metres here so all fade/break/foam controls
        // have predictable units and no longer depend on arbitrary 0..1 sliders.
        propertyBlock.SetFloat(ShoreDistanceRangeMetersId, Mathf.Max(1f, shoreDistanceRangeMeters));
        propertyBlock.SetFloat(ShoreFadeStartMetersId, shoreFadeStartMeters);
        propertyBlock.SetFloat(ShoreFadeEndMetersId, Mathf.Max(shoreFadeStartMeters + 0.1f, shoreFadeEndMeters));
        propertyBlock.SetFloat(NearShoreWaveStrengthId, nearShoreWaveStrength);
        propertyBlock.SetFloat(ShoreEdgeFadeMetersId, shoreEdgeFadeMeters);

        propertyBlock.SetFloat(ShoreBreakStartMetersId, shoreBreakStartMeters);
        propertyBlock.SetFloat(ShoreBreakPeakMetersId, Mathf.Max(shoreBreakStartMeters + 0.1f, shoreBreakPeakMeters));
        propertyBlock.SetFloat(ShoreBreakEndMetersId, Mathf.Max(shoreBreakPeakMeters + 0.1f, shoreBreakEndMeters));
        propertyBlock.SetFloat(ShoreDepthPerMeterId, shoreDepthPerMeter);
        propertyBlock.SetFloat(BreakerHeightToDepthId, breakerHeightToDepth);
        propertyBlock.SetFloat(ShoreShoalingScaleId, shoreShoalingScale);
        propertyBlock.SetFloat(ShoreHorizontalFloorId, shoreHorizontalFloor);
        propertyBlock.SetFloat(ShoreHorizontalDriveId, shoreHorizontalDrive);
        propertyBlock.SetFloat(ShoreChoppinessId, shoreChoppiness);
        propertyBlock.SetFloat(ShoreNormalBoostId, shoreNormalBoost);

        propertyBlock.SetFloat(ShoreFoamWidthMetersId, shoreFoamWidthMeters);
        propertyBlock.SetFloat(ShoreFoamEdgeFeatherMetersId, shoreFoamEdgeFeatherMeters);
        propertyBlock.SetFloat(ShoreFoamBlackThresholdId, shoreFoamBlackThreshold);
        propertyBlock.SetFloat(ShoreFoamBlackSoftnessId, shoreFoamBlackSoftness);
        propertyBlock.SetFloat(ShoreFoamEdgeNoiseMetersId, shoreFoamEdgeNoiseMeters);
        propertyBlock.SetFloat(OpenOceanFoamMultiplierId, openOceanFoamMultiplier);
        propertyBlock.SetFloat(ShoreFoamGainId, shoreFoamGain);
        propertyBlock.SetFloat(ShoreCrestJacobianThresholdId, shoreCrestJacobianThreshold);
        propertyBlock.SetFloat(ShoreCrestCompressionGainId, shoreCrestCompressionGain);
        propertyBlock.SetFloat(ShoreCrestHeightGainId, shoreCrestHeightGain);
        propertyBlock.SetFloat(ShoreWashSpeedId, shoreWashSpeed);
        propertyBlock.SetFloat(ShoreWashSpacingMetersId, shoreWashSpacingMeters);
        propertyBlock.SetFloat(ShoreWashPulseStrengthId, shoreWashPulseStrength);
        propertyBlock.SetFloat(ShoreDebugViewId, (float)shoreDebugView);

        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    private static Vector2 NormalizeWindDirection(Vector2 direction)
    {
        return direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector2.right;
    }

    private void UpdateWind(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        // Smooth the heading as an angle so a 180-degree wind reversal never
        // passes through a zero-length direction vector.
        currentWindDirectionAngle = Mathf.SmoothDampAngle(
            currentWindDirectionAngle,
            targetWindDirectionAngle,
            ref windDirectionAngleVelocity,
            Mathf.Max(0.01f, windDirectionSmoothTime),
            Mathf.Infinity,
            deltaTime);
        float angleRadians = currentWindDirectionAngle * Mathf.Deg2Rad;
        currentWindDirectionXZ = new Vector2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians));

        currentWindStrength = Mathf.SmoothDamp(
            currentWindStrength,
            targetWindStrength,
            ref windStrengthVelocity,
            Mathf.Max(0.01f, windStrengthSmoothTime),
            Mathf.Infinity,
            deltaTime);
        currentWindStrength = Mathf.Clamp01(currentWindStrength);

        currentWindStreakFrequency = Mathf.SmoothDamp(
            currentWindStreakFrequency,
            targetWindStreakFrequency,
            ref windStreakFrequencyVelocity,
            Mathf.Max(0.01f, windStreakFrequencySmoothTime),
            Mathf.Infinity,
            deltaTime);
        currentWindStreakFrequency = Mathf.Clamp(currentWindStreakFrequency, 0f, 3f);
    }

    /// <summary>
    /// Changes the direction the wind blows toward and its normalized strength.
    /// Strength ranges from 0 (light air) to 1 (strong wind). These controls affect
    /// FFT wave amplitude, choppiness, foam and animated surface streaks.
    /// </summary>
    public void SetWind(Vector2 directionXZ, float strength, float transitionSeconds = 1f)
    {
        SetWindDirection(directionXZ, transitionSeconds);
        SetWindStrength(strength, transitionSeconds);
    }

    public void SetWindDirection(Vector2 directionXZ, float transitionSeconds = 1f)
    {
        if (directionXZ.sqrMagnitude <= 0.000001f)
            return;

        windDirectionXZ = directionXZ.normalized;
        targetWindDirectionXZ = windDirectionXZ;
        targetWindDirectionAngle = Mathf.Atan2(
            targetWindDirectionXZ.y, targetWindDirectionXZ.x) * Mathf.Rad2Deg;
        windDirectionSmoothTime = Mathf.Max(0.01f, transitionSeconds);
        if (transitionSeconds <= 0f)
        {
            currentWindDirectionAngle = targetWindDirectionAngle;
            windDirectionAngleVelocity = 0f;
            currentWindDirectionXZ = targetWindDirectionXZ;
        }
    }

    public void SetWindStrength(float strength, float transitionSeconds = 1f)
    {
        windStrength = Mathf.Clamp01(strength);
        targetWindStrength = windStrength;
        windStrengthSmoothTime = Mathf.Max(0.01f, transitionSeconds);
        if (transitionSeconds <= 0f)
        {
            currentWindStrength = targetWindStrength;
            windStrengthVelocity = 0f;
        }
    }

    /// <summary>Master on/off toggle for world-space wind particles.</summary>
    public void SetWindStreaksEnabled(bool enabled)
    {
        showWindStreaks = enabled;
        ApplyShoreParameters();
    }

    /// <summary>Controls world wind-particle density and spawn frequency (0..3).</summary>
    public void SetWindStreakFrequency(float frequency, float transitionSeconds = 0.35f)
    {
        windStreakFrequency = Mathf.Clamp(frequency, 0f, 3f);
        targetWindStreakFrequency = windStreakFrequency;
        windStreakFrequencySmoothTime = Mathf.Max(0.01f, transitionSeconds);
        if (transitionSeconds <= 0f)
        {
            currentWindStreakFrequency = targetWindStreakFrequency;
            windStreakFrequencyVelocity = 0f;
        }
        ApplyShoreParameters();
    }

    /// <summary>
    /// Convenience hook for player-state logic: keep a sparse wind cue on land and
    /// increase particle density while sailing. Wind strength also controls particle motion.
    /// </summary>
    public void SetSailingWindEffects(bool isSailing)
    {
        // Keep the effect faintly visible on land rather than disabling it entirely;
        // wind strength still modulates how much of that low-density signal is visible.
        SetWindStreakFrequency(isSailing ? 2.2f : 0.28f, 0.55f);
        if (isSailing)
            SetWindStreaksEnabled(true);
    }

    /// <summary>
    /// Sets the target sea-state strength. 0 = calm, 1 = authored baseline,
    /// 2 = severe storm, 3 = extreme. Transition is smoothed over time.
    /// </summary>
    public void SetSeaState(float strength)
    {
        SetSeaState(strength, defaultSeaStateTransition);
    }

    public void SetSeaState(float strength, float transitionSeconds)
    {
        targetSeaStateStrength = Mathf.Clamp(strength, 0f, 3f);
        seaStateSmoothTime = Mathf.Max(0.01f, transitionSeconds);

        if (transitionSeconds <= 0f)
        {
            currentSeaStateStrength = targetSeaStateStrength;
            seaStateVelocity = 0f;
        }
    }

    // Parameterless methods can be connected directly to Unity UI Buttons.
    public void SetCalmSea() => SetSeaState(0.35f);
    public void SetNormalSea() => SetSeaState(1.0f);
    public void SetRoughSea() => SetSeaState(1.6f, 5f);
    public void SetStormSea() => SetSeaState(2.2f, 8f);
    public void SetExtremeStorm() => SetSeaState(2.8f, 10f);

    private void UpdateSeaState(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        currentSeaStateStrength = Mathf.SmoothDamp(
            currentSeaStateStrength,
            targetSeaStateStrength,
            ref seaStateVelocity,
            seaStateSmoothTime,
            Mathf.Infinity,
            deltaTime);
    }

    private float GetStormBlend()
    {
        return Mathf.InverseLerp(1f, 2.8f, currentSeaStateStrength);
    }

    private float GetStormChoppinessMultiplier()
    {
        return Mathf.Lerp(1f, stormChoppinessMultiplier, GetStormBlend());
    }

    private float GetStormFoamMultiplier()
    {
        return Mathf.Lerp(1f, stormFoamMultiplier, GetStormBlend());
    }

    private void HandleDebugHotkeys()
    {
        if (!enableDebugHotkeys)
            return;

        // Uses Unity's legacy Input Manager. Public preset methods are preferred
        // when driving sea state from the Input System or gameplay code.
        if (Input.GetKeyDown(KeyCode.F5)) SetCalmSea();
        if (Input.GetKeyDown(KeyCode.F6)) SetNormalSea();
        if (Input.GetKeyDown(KeyCode.F7)) SetRoughSea();
        if (Input.GetKeyDown(KeyCode.F8)) SetStormSea();
    }

    private void ReleaseResources()
    {
        resourcesReady = false;
        ReleaseSurfaceQuerySlots();

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
