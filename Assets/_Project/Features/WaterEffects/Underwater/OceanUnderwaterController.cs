using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Drives the underwater fullscreen effect from the real output camera.
/// Attach beside CinemachineBrain on the Main Camera, never on a virtual camera.
/// The animated water height is sampled asynchronously from the same FFT maps
/// used by Wave.shader, then spatially/temporally predicted and debounced.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(10000)]
public sealed class OceanUnderwaterController : MonoBehaviour
{
    [SerializeField, HideInInspector] private int settingsVersion = 2;

    [Header("Camera / Animated Water Surface")]
    [SerializeField] private Camera outputCamera;
    [Tooltip("Optional flat/static water-height override. Leave EMPTY when using SpectralOceanFFT, otherwise animated waves are intentionally bypassed.")]
    [SerializeField] private Transform waterSurfaceReference;
    [SerializeField] private SpectralOceanFFT ocean;
    [Tooltip("Used only as a fallback mean water level when no valid FFT sample is available.")]
    [SerializeField] private float fallbackWaterLevelY;
    [Tooltip("Camera must remain this far below the animated surface before entering underwater mode.")]
    [SerializeField, Min(0f)] private float enterDepthMeters = 0.16f;
    [Tooltip("After entering, camera must remain this far above the surface before exiting.")]
    [SerializeField, Min(0f)] private float exitAboveSurfaceMeters = 0.08f;
    [Tooltip("How long the camera must remain below the entry threshold. Suppresses single-wave false positives.")]
    [SerializeField, Range(0f, 0.5f)] private float enterConfirmationSeconds = 0.14f;
    [Tooltip("How long the camera must remain above the exit threshold before the underwater state turns off.")]
    [SerializeField, Range(0f, 0.5f)] private float exitConfirmationSeconds = 0.10f;
    [Tooltip("Last FFT sample remains fully trusted for this duration after GPU dispatch.")]
    [SerializeField, Range(0.05f, 1.5f)] private float maximumSurfaceSampleAge = 0.28f;
    [Tooltip("After the trusted age, blend the last predicted wave surface toward mean water level over this duration.")]
    [SerializeField, Range(0.05f, 1.5f)] private float staleSurfaceFallbackFade = 0.20f;
    [Tooltip("How far into the future the last measured wave-height velocity may be extrapolated.")]
    [SerializeField, Range(0f, 0.35f)] private float surfacePredictionSeconds = 0.22f;
    [Tooltip("Maximum extrapolated vertical wave speed, in metres per second.")]
    [SerializeField, Range(1f, 60f)] private float maximumPredictedWaveSpeed = 32f;
    [Tooltip("Maximum vertical wave acceleration used for short readback-latency prediction. Strong storms can move the surface much faster than calm-sea defaults.")]
    [SerializeField, Range(0f, 160f)] private float maximumPredictedWaveAcceleration = 100f;
    [Tooltip("Maximum height correction estimated from camera movement across a steep, wind-driven wave slope.")]
    [SerializeField, Range(0.1f, 12f)] private float maximumSpatialHeightCorrection = 4f;
    [Tooltip("Maximum height correction from temporal wave prediction. A larger bound is needed for high-wind crests while the prediction horizon remains short.")]
    [SerializeField, Range(0.05f, 4f)] private float maximumTemporalHeightCorrection = 1.5f;
    [Tooltip("Reject a readback if the camera teleported farther than this since the query was issued.")]
    [SerializeField, Min(1f)] private float maximumSamplePositionDrift = 30f;
    [Tooltip("Camera point queries in flight. The ocean shares its GPU readback ring with buoyancy probes.")]
    [SerializeField, Range(1, 4)] private int maximumConcurrentCameraQueries = 4;
    [Tooltip("Small final filter on the predicted wave height. Increase slightly if the waterline still sparkles.")]
    [SerializeField, Range(0f, 0.25f)] private float surfaceHeightSmoothing = 0.025f;
    [Tooltip("Depth below the animated surface at which the underwater look reaches full strength.")]
    [SerializeField, Min(0.1f)] private float fullEffectDepthMeters = 1.5f;
    [SerializeField, Min(0f)] private float transitionSeconds = 0.14f;

    [Header("Exit Transition")]
    [Tooltip("Soft screen blur fades completely after leaving water. Default is half a second.")]
    [SerializeField, Range(0.05f, 1.5f)] private float exitBlurFadeSeconds = 0.5f;
    [Tooltip("Maximum blur radius in screen pixels immediately after exiting the water.")]
    [SerializeField, Range(0f, 5f)] private float exitBlurRadiusPixels = 2.5f;

    [Header("Underwater Look")]
    [SerializeField] private Color waterTint = new Color(0.12f, 0.48f, 0.53f, 1f);
    [SerializeField, Range(0f, 2f)] private float tintStrength = 0.72f;
    [SerializeField, Range(0f, 0.03f)] private float refractionStrength = 0.0035f;
    [SerializeField, Range(0f, 2f)] private float causticsStrength = 0.35f;
    [SerializeField, Range(0f, 1f)] private float desaturation = 0.18f;
    [SerializeField, Range(0f, 2f)] private float fogDensity = 0.16f;
    [SerializeField] private Color fogColor = new Color(0.025f, 0.19f, 0.23f, 1f);
    [SerializeField, Range(0f, 1f)] private float vignetteStrength = 0.22f;
    [SerializeField, Range(0f, 5f)] private float causticsSpeed = 0.8f;

    private static readonly int IntensityId = Shader.PropertyToID("_OceanUnderwaterIntensity");
    private static readonly int DepthId = Shader.PropertyToID("_OceanUnderwaterCameraDepth");
    private static readonly int TintId = Shader.PropertyToID("_OceanUnderwaterTint");
    private static readonly int TintStrengthId = Shader.PropertyToID("_OceanUnderwaterTintStrength");
    private static readonly int RefractionId = Shader.PropertyToID("_OceanUnderwaterRefraction");
    private static readonly int CausticsId = Shader.PropertyToID("_OceanUnderwaterCaustics");
    private static readonly int DesaturationId = Shader.PropertyToID("_OceanUnderwaterDesaturation");
    private static readonly int FogDensityId = Shader.PropertyToID("_OceanUnderwaterFogDensity");
    private static readonly int FogColorId = Shader.PropertyToID("_OceanUnderwaterFogColor");
    private static readonly int VignetteId = Shader.PropertyToID("_OceanUnderwaterVignette");
    private static readonly int CausticsSpeedId = Shader.PropertyToID("_OceanUnderwaterCausticsSpeed");
    private static readonly int ExitBlurId = Shader.PropertyToID("_OceanUnderwaterExitBlur");
    private static readonly int ExitBlurRadiusId = Shader.PropertyToID("_OceanUnderwaterExitBlurRadiusPixels");

    private int trackingGeneration;
    private int nextRequestId;
    private int lastAcceptedRequestId = -1;
    private int cameraQueriesInFlight;
    private bool hasSurfaceSample;
    private bool hasInitializedFromReliableSurface;
    private SpectralOceanFFT.SurfaceSample latestSurfaceSample;
    private float estimatedVerticalWaveVelocity;
    private float estimatedVerticalWaveAcceleration;
    private float filteredSurfaceHeight;
    private float surfaceHeightFilterVelocity;
    private bool surfaceHeightFilterInitialized;

    private float enterCandidateTime;
    private float exitCandidateTime;
    private float currentIntensity;
    private float intensityVelocity;
    private float exitBlurRemaining;
    private bool hasEnteredWater;

    public bool IsUnderwater => hasEnteredWater;
    public float CurrentIntensity => currentIntensity;
    public float CurrentSignedDepthMeters => filteredSurfaceHeight -
        (outputCamera != null ? outputCamera.transform.position.y : 0f);
    public float LastSurfaceSampleAge => hasSurfaceSample
        ? Mathf.Max(0f, Time.realtimeSinceStartup - latestSurfaceSample.SampleTime)
        : float.PositiveInfinity;
    public float CurrentSurfaceHeight
    {
        get
        {
            TryGetCurrentSurfaceHeight(out float height);
            return height;
        }
    }

    private void OnEnable()
    {
        MigrateLegacySettings();
        trackingGeneration++;
        if (outputCamera == null)
            outputCamera = GetComponent<Camera>();
        if (ocean == null)
            ocean = UnityEngine.Object.FindAnyObjectByType<SpectralOceanFFT>();

        hasSurfaceSample = false;
        hasInitializedFromReliableSurface = false;
        cameraQueriesInFlight = 0;
        lastAcceptedRequestId = -1;
        estimatedVerticalWaveVelocity = 0f;
        estimatedVerticalWaveAcceleration = 0f;
        filteredSurfaceHeight = GetMeanWaterLevel();
        surfaceHeightFilterInitialized = false;
        enterCandidateTime = 0f;
        exitCandidateTime = 0f;
        currentIntensity = 0f;
        intensityVelocity = 0f;
        exitBlurRemaining = 0f;
        hasEnteredWater = false;

        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        SetNeutralGlobals();
    }

    private void OnDisable()
    {
        trackingGeneration++;
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        currentIntensity = 0f;
        intensityVelocity = 0f;
        exitBlurRemaining = 0f;
        hasEnteredWater = false;
        cameraQueriesInFlight = 0;
        hasSurfaceSample = false;
        estimatedVerticalWaveVelocity = 0f;
        estimatedVerticalWaveAcceleration = 0f;
        surfaceHeightFilterInitialized = false;
        SetNeutralGlobals();
    }

    private void OnValidate()
    {
        MigrateLegacySettings();
        enterDepthMeters = Mathf.Max(0f, enterDepthMeters);
        // Entry and exit thresholds are on opposite sides of the surface; their
        // magnitudes do not need to be ordered. Keeping this independent is what
        // allows a small above-water exit threshold (e.g. 0.08 m).
        exitAboveSurfaceMeters = Mathf.Max(0f, exitAboveSurfaceMeters);
        enterConfirmationSeconds = Mathf.Clamp(enterConfirmationSeconds, 0f, 0.5f);
        exitConfirmationSeconds = Mathf.Clamp(exitConfirmationSeconds, 0f, 0.5f);
        maximumSurfaceSampleAge = Mathf.Clamp(maximumSurfaceSampleAge, 0.05f, 1.5f);
        staleSurfaceFallbackFade = Mathf.Clamp(staleSurfaceFallbackFade, 0.05f, 1.5f);
        surfacePredictionSeconds = Mathf.Clamp(surfacePredictionSeconds, 0f, 0.35f);
        maximumPredictedWaveSpeed = Mathf.Clamp(maximumPredictedWaveSpeed, 1f, 60f);
        maximumPredictedWaveAcceleration = Mathf.Clamp(maximumPredictedWaveAcceleration, 0f, 160f);
        maximumSpatialHeightCorrection = Mathf.Clamp(maximumSpatialHeightCorrection, 0.1f, 12f);
        maximumTemporalHeightCorrection = Mathf.Clamp(maximumTemporalHeightCorrection, 0.05f, 4f);
        maximumSamplePositionDrift = Mathf.Max(1f, maximumSamplePositionDrift);
        maximumConcurrentCameraQueries = Mathf.Clamp(maximumConcurrentCameraQueries, 1, 4);
        surfaceHeightSmoothing = Mathf.Clamp(surfaceHeightSmoothing, 0f, 0.25f);
        fullEffectDepthMeters = Mathf.Max(0.1f, fullEffectDepthMeters);
        transitionSeconds = Mathf.Max(0f, transitionSeconds);
        exitBlurFadeSeconds = Mathf.Clamp(exitBlurFadeSeconds, 0.05f, 1.5f);
        exitBlurRadiusPixels = Mathf.Clamp(exitBlurRadiusPixels, 0f, 5f);
        tintStrength = Mathf.Clamp(tintStrength, 0f, 2f);
        refractionStrength = Mathf.Clamp(refractionStrength, 0f, 0.03f);
        causticsStrength = Mathf.Clamp(causticsStrength, 0f, 2f);
        desaturation = Mathf.Clamp01(desaturation);
        fogDensity = Mathf.Clamp(fogDensity, 0f, 2f);
        vignetteStrength = Mathf.Clamp01(vignetteStrength);
        causticsSpeed = Mathf.Clamp(causticsSpeed, 0f, 5f);
    }

    private void MigrateLegacySettings()
    {
        if (settingsVersion < 1)
        {
            // v3's OnValidate forced Exit Above Surface to be greater than Enter Depth.
            // Existing scenes therefore kept values such as 0.32 m even after the
            // corrected script default became 0.08 m. Migrate that old coupled value
            // exactly once so replacing the component script actually fixes existing
            // scenes instead of only fixing newly added components.
            if (exitAboveSurfaceMeters > 0.20f)
                exitAboveSurfaceMeters = 0.08f;
            if (maximumSurfaceSampleAge > 0.45f)
                maximumSurfaceSampleAge = 0.28f;
            if (staleSurfaceFallbackFade > 0.30f)
                staleSurfaceFallbackFade = 0.20f;
            if (enterConfirmationSeconds < 0.10f)
                enterConfirmationSeconds = 0.14f;
            settingsVersion = 1;
        }

        if (settingsVersion < 2)
        {
            // High-wind FFT crests can travel several metres during async GPU
            // readback. Older defaults clamped both spatial and temporal prediction
            // too aggressively, making the underwater threshold follow mean sea
            // level instead of the actual crest/trough.
            maximumPredictedWaveSpeed = Mathf.Max(maximumPredictedWaveSpeed, 32f);
            maximumPredictedWaveAcceleration = Mathf.Max(maximumPredictedWaveAcceleration, 100f);
            maximumSpatialHeightCorrection = Mathf.Max(maximumSpatialHeightCorrection, 4f);
            maximumTemporalHeightCorrection = Mathf.Max(maximumTemporalHeightCorrection, 1.5f);
            surfaceHeightSmoothing = Mathf.Min(surfaceHeightSmoothing, 0.025f);
            settingsVersion = 2;
        }
    }

    private void LateUpdate()
    {
        if (outputCamera == null)
            outputCamera = GetComponent<Camera>();

        if (waterSurfaceReference != null || ocean == null ||
            !ocean.CanSampleSurface || outputCamera == null)
            return;

        // Keep several rolling queries in flight. Camera requests are put in the
        // ocean's priority queue so a boat's hull probes cannot starve immersion.
        if (cameraQueriesInFlight >= maximumConcurrentCameraQueries)
            return;

        Vector3 requestPosition = outputCamera.transform.position;
        int requestId = ++nextRequestId;
        int generation = trackingGeneration;
        cameraQueriesInFlight++;

        bool queued = ocean.RequestSurfaceSampleForCamera(
            requestPosition,
            sample => OnSurfaceSampleReceived(sample, requestPosition, requestId, generation));

        if (!queued)
            cameraQueriesInFlight = Mathf.Max(0, cameraQueriesInFlight - 1);
    }

    private void OnSurfaceSampleReceived(
        SpectralOceanFFT.SurfaceSample sample,
        Vector3 requestPosition,
        int requestId,
        int generation)
    {
        // A readback from a disabled/re-enabled instance must not mutate the new session.
        if (generation != trackingGeneration)
            return;

        cameraQueriesInFlight = Mathf.Max(0, cameraQueriesInFlight - 1);
        if (!isActiveAndEnabled || !sample.IsValid || outputCamera == null ||
            requestId <= lastAcceptedRequestId || !IsFinite(sample.Height) ||
            !IsFinite(sample.SampleTime) || sample.Normal.sqrMagnitude < 0.0001f ||
            !IsFinite(sample.Normal.x) || !IsFinite(sample.Normal.y) || !IsFinite(sample.Normal.z) ||
            !IsFinite(sample.Position.x) || !IsFinite(sample.Position.y) ||
            !IsFinite(sample.Position.z))
            return;

        float sampleAgeAtReceipt = Time.realtimeSinceStartup - sample.SampleTime;
        if (sampleAgeAtReceipt < -0.05f ||
            sampleAgeAtReceipt > maximumSurfaceSampleAge + staleSurfaceFallbackFade)
            return;

        Vector3 currentPosition = outputCamera.transform.position;
        Vector2 drift = new Vector2(
            currentPosition.x - requestPosition.x,
            currentPosition.z - requestPosition.z);
        if (drift.sqrMagnitude > maximumSamplePositionDrift * maximumSamplePositionDrift)
            return;

        if (hasSurfaceSample)
        {
            float sampleDelta = sample.SampleTime - latestSurfaceSample.SampleTime;
            if (sampleDelta > 0.003f && sampleDelta < 0.5f)
            {
                Vector3 previousNormal = latestSurfaceSample.Normal.sqrMagnitude > 0.001f
                    ? latestSurfaceSample.Normal.normalized
                    : Vector3.up;
                float normalY = Mathf.Max(previousNormal.y, 0.2f);
                Vector2 previousSlope = new Vector2(
                    -previousNormal.x / normalY,
                    -previousNormal.z / normalY);
                Vector2 sampleDeltaXZ = new Vector2(
                    sample.Position.x - latestSurfaceSample.Position.x,
                    sample.Position.z - latestSurfaceSample.Position.z);
                float expectedHeightAtNewXZ = latestSurfaceSample.Height
                    + Vector2.Dot(previousSlope, sampleDeltaXZ);
                float waveVelocity = (sample.Height - expectedHeightAtNewXZ) / sampleDelta;
                waveVelocity = Mathf.Clamp(
                    waveVelocity, -maximumPredictedWaveSpeed, maximumPredictedWaveSpeed);

                // A short acceleration term improves prediction over the one-to-three
                // frame GPU readback latency when a crest is curving through the camera.
                // Both derivatives are bounded and exponentially filtered to avoid
                // amplifying one noisy sample into a false waterline crossing.
                float rawAcceleration = (waveVelocity - estimatedVerticalWaveVelocity)
                    / Mathf.Max(sampleDelta, 0.01f);
                rawAcceleration = Mathf.Clamp(
                    rawAcceleration,
                    -maximumPredictedWaveAcceleration,
                    maximumPredictedWaveAcceleration);
                float velocityBlend = 1f - Mathf.Exp(-sampleDelta * 10f);
                float accelerationBlend = 1f - Mathf.Exp(-sampleDelta * 5f);
                estimatedVerticalWaveAcceleration = Mathf.Lerp(
                    estimatedVerticalWaveAcceleration, rawAcceleration, accelerationBlend);
                estimatedVerticalWaveVelocity = Mathf.Lerp(
                    estimatedVerticalWaveVelocity, waveVelocity, velocityBlend);
            }
        }
        else
        {
            estimatedVerticalWaveVelocity = 0f;
            estimatedVerticalWaveAcceleration = 0f;
        }

        // Surface queries should return an upward-facing normal. A folded shore
        // patch can produce a flipped normal at a locally ambiguous point; orient
        // it upward before using it for camera-height slope prediction.
        Vector3 acceptedNormal = sample.Normal.normalized;
        if (acceptedNormal.y < 0f)
            acceptedNormal = -acceptedNormal;
        sample.Normal = acceptedNormal;

        latestSurfaceSample = sample;
        hasSurfaceSample = true;
        lastAcceptedRequestId = requestId;
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
    {
        if (outputCamera == null)
            outputCamera = GetComponent<Camera>();

        if (renderingCamera != outputCamera)
        {
            SetNeutralGlobals();
            return;
        }

        bool hasReliableSurface = TryGetCurrentSurfaceHeight(out float targetSurfaceHeight);

        float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        bool firstReliableSample = hasReliableSurface && !hasInitializedFromReliableSurface;
        if (!surfaceHeightFilterInitialized || firstReliableSample)
        {
            filteredSurfaceHeight = targetSurfaceHeight;
            surfaceHeightFilterVelocity = 0f;
            surfaceHeightFilterInitialized = true;
        }
        else if (surfaceHeightSmoothing <= 0f)
        {
            filteredSurfaceHeight = targetSurfaceHeight;
            surfaceHeightFilterVelocity = 0f;
        }
        else
        {
            filteredSurfaceHeight = Mathf.SmoothDamp(
                filteredSurfaceHeight,
                targetSurfaceHeight,
                ref surfaceHeightFilterVelocity,
                surfaceHeightSmoothing,
                Mathf.Infinity,
                deltaTime);
        }

        if (hasReliableSurface)
            hasInitializedFromReliableSurface = true;
        float signedDepth = filteredSurfaceHeight - renderingCamera.transform.position.y;
        bool wasUnderwater = hasEnteredWater;

        if (!hasEnteredWater)
        {
            if (hasReliableSurface && signedDepth >= enterDepthMeters)
                enterCandidateTime += deltaTime;
            else
                enterCandidateTime = 0f;

            if (enterCandidateTime >= enterConfirmationSeconds)
            {
                hasEnteredWater = true;
                enterCandidateTime = 0f;
                exitCandidateTime = 0f;
                exitBlurRemaining = 0f;
            }
        }
        else
        {
            // If FFT sampling becomes unavailable, exit only after a short grace
            // period. The surface has already blended toward mean sea level.
            bool aboveExit = !hasReliableSurface || signedDepth <= -exitAboveSurfaceMeters;
            if (aboveExit)
            {
                exitCandidateTime += deltaTime;
                // If the camera is clearly outside the water (for example after a
                // jump, boat bounce or Cinemachine camera cut), don't wait for the
                // normal debounce to expire before beginning the exit transition.
                float clearAirGap = Mathf.Max(0.45f, exitAboveSurfaceMeters * 3f);
                if (hasReliableSurface && signedDepth <= -clearAirGap)
                    exitCandidateTime = Mathf.Max(exitCandidateTime, exitConfirmationSeconds);
            }
            else
            {
                exitCandidateTime = 0f;
            }

            if (exitCandidateTime >= exitConfirmationSeconds)
            {
                hasEnteredWater = false;
                exitCandidateTime = 0f;
                enterCandidateTime = 0f;
            }
        }

        if (hasEnteredWater && !wasUnderwater)
            exitBlurRemaining = 0f;
        else if (!hasEnteredWater && wasUnderwater)
            exitBlurRemaining = exitBlurFadeSeconds;

        float depthBelowSurface = hasEnteredWater
            ? Mathf.Max(0f, signedDepth)
            : 0f;
        float depthBlend = hasEnteredWater
            ? Mathf.SmoothStep(0f, 1f, depthBelowSurface / fullEffectDepthMeters)
            : 0f;

        if (transitionSeconds <= 0f)
        {
            currentIntensity = depthBlend;
            intensityVelocity = 0f;
        }
        else
        {
            currentIntensity = Mathf.SmoothDamp(
                currentIntensity,
                depthBlend,
                ref intensityVelocity,
                transitionSeconds,
                Mathf.Infinity,
                deltaTime);
        }

        if (!hasEnteredWater && exitBlurRemaining > 0f)
            exitBlurRemaining = Mathf.Max(0f, exitBlurRemaining - deltaTime);

        float exitBlur = exitBlurRemaining > 0f
            ? Mathf.SmoothStep(0f, 1f,
                exitBlurRemaining / Mathf.Max(exitBlurFadeSeconds, 0.05f))
            : 0f;

        ApplyGlobals(currentIntensity, depthBelowSurface, exitBlur);
    }

    private bool TryGetCurrentSurfaceHeight(out float height)
    {
        height = GetMeanWaterLevel();

        if (waterSurfaceReference != null)
        {
            height = waterSurfaceReference.position.y;
            return IsFinite(height);
        }

        if (!hasSurfaceSample)
            return false; // Never activate from the flat mean plane before the first real FFT result.

        float age = Mathf.Max(0f, Time.realtimeSinceStartup - latestSurfaceSample.SampleTime);
        Vector3 normal = latestSurfaceSample.Normal.sqrMagnitude > 0.001f
            ? latestSurfaceSample.Normal.normalized
            : Vector3.up;
        float normalY = Mathf.Max(normal.y, 0.2f);
        Vector2 slope = new Vector2(-normal.x / normalY, -normal.z / normalY);
        slope = Vector2.ClampMagnitude(slope, 8.0f);

        Vector3 cameraPosition = outputCamera != null
            ? outputCamera.transform.position
            : latestSurfaceSample.BasePosition;
        Vector2 cameraDeltaXZ = new Vector2(
            cameraPosition.x - latestSurfaceSample.Position.x,
            cameraPosition.z - latestSurfaceSample.Position.z);
        float spatialCorrection = Mathf.Clamp(
            Vector2.Dot(slope, cameraDeltaXZ),
            -maximumSpatialHeightCorrection,
            maximumSpatialHeightCorrection);
        float predictionTime = Mathf.Min(age, surfacePredictionSeconds);
        float temporalCorrection = estimatedVerticalWaveVelocity * predictionTime
            + 0.5f * estimatedVerticalWaveAcceleration * predictionTime * predictionTime;
        temporalCorrection = Mathf.Clamp(
            temporalCorrection,
            -maximumTemporalHeightCorrection,
            maximumTemporalHeightCorrection);
        float predictedHeight = latestSurfaceSample.Height
            + spatialCorrection + temporalCorrection;

        if (!IsFinite(predictedHeight))
        {
            hasSurfaceSample = false;
            return false;
        }

        if (age <= maximumSurfaceSampleAge)
        {
            height = predictedHeight;
            return true;
        }

        float fallbackT = Mathf.SmoothStep(
            0f, 1f,
            (age - maximumSurfaceSampleAge) / Mathf.Max(staleSurfaceFallbackFade, 0.05f));
        height = Mathf.Lerp(predictedHeight, GetMeanWaterLevel(), fallbackT);
        return age <= maximumSurfaceSampleAge + staleSurfaceFallbackFade && IsFinite(height);
    }

    private float GetMeanWaterLevel()
    {
        if (waterSurfaceReference != null)
            return waterSurfaceReference.position.y;
        if (ocean != null)
            return ocean.WaterLevelY;
        return fallbackWaterLevelY;
    }

    private void ApplyGlobals(float intensity, float depth, float exitBlur)
    {
        Shader.SetGlobalFloat(IntensityId, Mathf.Clamp01(intensity));
        Shader.SetGlobalFloat(DepthId, depth);
        Shader.SetGlobalColor(TintId, waterTint);
        Shader.SetGlobalFloat(TintStrengthId, tintStrength);
        Shader.SetGlobalFloat(RefractionId, refractionStrength);
        Shader.SetGlobalFloat(CausticsId, causticsStrength);
        Shader.SetGlobalFloat(DesaturationId, desaturation);
        Shader.SetGlobalFloat(FogDensityId, fogDensity);
        Shader.SetGlobalColor(FogColorId, fogColor);
        Shader.SetGlobalFloat(VignetteId, vignetteStrength);
        Shader.SetGlobalFloat(CausticsSpeedId, causticsSpeed);
        Shader.SetGlobalFloat(ExitBlurId, Mathf.Clamp01(exitBlur));
        Shader.SetGlobalFloat(ExitBlurRadiusId, exitBlurRadiusPixels);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static void SetNeutralGlobals()
    {
        Shader.SetGlobalFloat(IntensityId, 0f);
        Shader.SetGlobalFloat(DepthId, 0f);
        Shader.SetGlobalFloat(ExitBlurId, 0f);
        Shader.SetGlobalFloat(ExitBlurRadiusId, 0f);
    }
}
