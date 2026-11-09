using System;
using UnityEngine;

/// <summary>
/// First-stage FFT buoyancy for floating objects.
/// Attach to a non-kinematic Rigidbody and place sample probes around the hull.
/// A surface query is queued from every FixedUpdate (up to the configured number
/// of outstanding snapshots). GPU readback is asynchronous, so physics applies
/// the newest valid water snapshot instead of synchronously stalling the CPU.
/// The same samples are exposed for future boat navigation and wave-following.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class OceanBuoyancy : MonoBehaviour
{
    [Serializable]
    public struct BuoyancyPoint
    {
        [Tooltip("Point position relative to this object's Rigidbody transform.")]
        public Vector3 localPosition;
        [Tooltip("Relative contribution of this point to total buoyancy.")]
        [Range(0f, 3f)] public float liftWeight;
    }

    [Header("Ocean / Hull Sampling")]
    [SerializeField] private SpectralOceanFFT ocean;
    [SerializeField] private BuoyancyPoint[] samplePoints =
    {
        new BuoyancyPoint { localPosition = new Vector3(-1.2f, 0f, -1.8f), liftWeight = 1f },
        new BuoyancyPoint { localPosition = new Vector3( 1.2f, 0f, -1.8f), liftWeight = 1f },
        new BuoyancyPoint { localPosition = new Vector3(-1.2f, 0f,  1.8f), liftWeight = 1f },
        new BuoyancyPoint { localPosition = new Vector3( 1.2f, 0f,  1.8f), liftWeight = 1f }
    };
    [Tooltip("Maximum physics snapshots waiting for GPU readback. More snapshots improve temporal coverage but use more readback slots.")]
    [SerializeField, Range(1, 4)] private int maxOutstandingSnapshots = 3;

    [Header("Buoyancy")]
    [Tooltip("Disable force application to use this component as a FixedUpdate water-surface sampler for boat navigation, suspension or custom physics.")]
    [SerializeField] private bool applyBuoyancyForces = true;
    [Tooltip("At full immersion, total upward acceleration is gravity multiplied by this value.")]
    [SerializeField, Min(0f)] private float buoyancyStrength = 1.12f;
    [Tooltip("Vertical distance over which a probe reaches full immersion.")]
    [SerializeField, Min(0.05f)] private float buoyancyDepth = 1.25f;
    [Tooltip("Velocity damping at submerged points. Helps prevent endless bobbing.")]
    [SerializeField, Min(0f)] private float waterDrag = 1.8f;
    [Tooltip("Maximum drag acceleration at each buoyancy point.")]
    [SerializeField, Min(0f)] private float maxDragAcceleration = 12f;
    [Tooltip("Blend buoyancy direction toward the sampled wave normal. 0 = world up.")]
    [SerializeField, Range(0f, 1f)] private float normalInfluence = 0.12f;

    [Header("Sampling")]
    [Tooltip("Samples older than this are ignored. Async GPU readback usually adds one or two frames of latency.")]
    [SerializeField, Min(0.05f)] private float maximumSampleAge = 0.30f;
    [Tooltip("If enabled, only the most recently requested physics snapshot is used. Disable to accept every completed snapshot in sequence.")]
    [SerializeField] private bool preferNewestSnapshot = true;
    [Tooltip("Maximum tangent-plane height correction when a hull point has moved since its asynchronous FFT sample.")]
    [SerializeField, Range(0.5f, 12f)] private float maximumSlopeHeightCorrection = 5f;
    [Tooltip("Maximum slope magnitude used to predict water height at a moving hull point.")]
    [SerializeField, Range(1f, 12f)] private float maximumSampleSlope = 8f;
    [Tooltip("Maximum magnitude allowed for the estimated water-particle velocity.")]
    [SerializeField, Range(5f, 80f)] private float maximumEstimatedWaveVelocity = 55f;

    /// <summary>Runtime multiplier on water drag (heave damping). 1 = serialized behaviour.</summary>
    public float RuntimeDragScale { get; set; } = 1f;

    /// <summary>
    /// Runtime wave-slope follow per hull axis (x = roll, across the beam; y = pitch, along the
    /// length). 1 = the probes ride the true wave surface (default, unchanged behaviour).
    /// 0 = the probes only follow the mean wave height, so the hull heaves but stays level.
    /// Implemented at the source of buoyancy, so it works whether or not the boat is manned.
    /// </summary>
    public Vector2 RuntimeSlopeFollow { get; set; } = Vector2.one;

    private float[] slopeHeights;
    private bool[] slopeValid;

    /// <summary>Bounds of the sample points in local space (hull size estimate).</summary>
    public bool TryGetProbeLocalBounds(out Bounds bounds)
    {
        bounds = default;
        if (samplePoints == null || samplePoints.Length == 0)
            return false;
        bounds = new Bounds(samplePoints[0].localPosition, Vector3.zero);
        for (int i = 1; i < samplePoints.Length; i++)
            bounds.Encapsulate(samplePoints[i].localPosition);
        return true;
    }

    private Rigidbody body;
    private Vector3[] queryPositions;
    private SpectralOceanFFT.SurfaceSample[] latestSamples;
    private int outstandingSnapshots;
    private int nextSnapshotId;
    private int latestAcceptedSnapshotId = -1;
    private int componentGeneration;
    private float latestSampleTime = -1000f;
    private bool hasAnyValidSamples;
    private float averageWaterHeight;
    private Vector3 averageWaterPosition;
    private Vector3 averageWaterNormal = Vector3.up;
    private Vector3 averageWaterVelocity;
    private Vector2 averageLatticeDisplacement;
    private Vector3 previousAverageWaveDisplacement;
    private float previousAverageWaveSampleTime = -1000f;

    /// <summary>Latest accepted samples in the same order as Sample Points.</summary>
    public SpectralOceanFFT.SurfaceSample[] LatestSamples => latestSamples;
    public int SamplePointCount => samplePoints != null ? samplePoints.Length : 0;
    public float AverageWaterHeight => averageWaterHeight;
    public Vector3 AverageWaterPosition => averageWaterPosition;
    public Vector3 AverageWaterNormal => averageWaterNormal;
    /// <summary>Estimated world-space water-particle velocity from consecutive FFT samples.</summary>
    public Vector3 AverageWaterVelocity => averageWaterVelocity;
    /// <summary>Timestamp of the latest accepted FFT surface snapshot.</summary>
    public float LatestSampleTime => latestSampleTime;
    /// <summary>Age measured from GPU dispatch time, including readback latency.</summary>
    public float SampleAge => Time.realtimeSinceStartup - latestSampleTime;
    public bool HasAnyValidSamples => hasAnyValidSamples;
    public bool HasFreshSamples => hasAnyValidSamples && latestSamples != null &&
        SampleAge >= 0f && SampleAge <= maximumSampleAge;

    /// <summary>
    /// Highest current probe immersion (0 = no hull contact, 1 = at least one
    /// probe is fully immersed). Used by the boat to distinguish floating from
    /// airborne physics without damping natural rotation in mid-air.
    /// </summary>
    public float CurrentWaterImmersion
    {
        get
        {
            // Cached per physics step / frame: callers (boat, effects) read this several times.
            int frame = Time.frameCount;
            float fixedNow = Time.fixedTime;
            if (frame != immersionCacheFrame || fixedNow != immersionCacheFixedTime)
            {
                immersionCache = CalculateCurrentWaterImmersion();
                immersionCacheFrame = frame;
                immersionCacheFixedTime = fixedNow;
            }
            return immersionCache;
        }
    }

    private float immersionCache;
    private int immersionCacheFrame = -1;
    private float immersionCacheFixedTime = -1f;

    /// <summary>Raised whenever a newer physics snapshot has been accepted.</summary>
    public event Action<SpectralOceanFFT.SurfaceSample[]> SamplesUpdated;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        RebuildSampleArrays();
    }

    private void OnEnable()
    {
        if (body == null)
            body = GetComponent<Rigidbody>();
        if (ocean == null)
            ocean = UnityEngine.Object.FindAnyObjectByType<SpectralOceanFFT>();
        RebuildSampleArrays();
    }

    private void OnDisable()
    {
        componentGeneration++;
        outstandingSnapshots = 0;
    }

    private void OnValidate()
    {
        maxOutstandingSnapshots = Mathf.Clamp(maxOutstandingSnapshots, 1, 4);
        buoyancyStrength = Mathf.Max(0f, buoyancyStrength);
        buoyancyDepth = Mathf.Max(0.05f, buoyancyDepth);
        waterDrag = Mathf.Max(0f, waterDrag);
        maxDragAcceleration = Mathf.Max(0f, maxDragAcceleration);
        maximumSampleAge = Mathf.Max(0.05f, maximumSampleAge);
        normalInfluence = Mathf.Clamp01(normalInfluence);
        maximumSlopeHeightCorrection = Mathf.Clamp(maximumSlopeHeightCorrection, 0.5f, 12f);
        maximumSampleSlope = Mathf.Clamp(maximumSampleSlope, 1f, 12f);
        maximumEstimatedWaveVelocity = Mathf.Clamp(maximumEstimatedWaveVelocity, 5f, 80f);
        if (samplePoints == null || samplePoints.Length == 0)
        {
            samplePoints = new[]
            {
                new BuoyancyPoint { localPosition = Vector3.zero, liftWeight = 1f }
            };
        }
        if (Application.isPlaying && queryPositions != null &&
            queryPositions.Length != samplePoints.Length)
            RebuildSampleArrays();
    }

    private void FixedUpdate()
    {
        if (body == null || body.isKinematic || ocean == null ||
            !ocean.CanSampleSurface || samplePoints == null || samplePoints.Length == 0)
            return;

        // Take a new world-space hull snapshot during every physics step. The
        // ocean clones the input array before queueing it, so the scratch array
        // can safely be reused on the next FixedUpdate.
        if (outstandingSnapshots < maxOutstandingSnapshots)
            RequestPhysicsSnapshot();

        if (applyBuoyancyForces)
            ApplyBuoyancyForces();
    }

    private void RequestPhysicsSnapshot()
    {
        for (int i = 0; i < samplePoints.Length; i++)
        {
            queryPositions[i] = body.transform.TransformPoint(samplePoints[i].localPosition);
        }

        int snapshotId = ++nextSnapshotId;
        int generation = componentGeneration;
        outstandingSnapshots++;
        SnapshotRequest request = snapshotPool.Count > 0 ? snapshotPool.Pop() : new SnapshotRequest(this);
        request.generation = generation;
        request.snapshotId = snapshotId;
        bool queued = ocean.RequestSurfaceSamples(queryPositions, request.callback);
        if (!queued)
        {
            outstandingSnapshots = Mathf.Max(0, outstandingSnapshots - 1);
            snapshotPool.Push(request);
        }
    }

    // Pooled callback object: avoids allocating a closure every FixedUpdate.
    private sealed class SnapshotRequest
    {
        public int generation;
        public int snapshotId;
        public readonly System.Action<SpectralOceanFFT.SurfaceSample[]> callback;
        private readonly OceanBuoyancy owner;

        public SnapshotRequest(OceanBuoyancy owner)
        {
            this.owner = owner;
            callback = Invoke;
        }

        private void Invoke(SpectralOceanFFT.SurfaceSample[] samples)
        {
            owner.OnSurfaceSamplesReceived(generation, snapshotId, samples);
            owner.snapshotPool.Push(this);
        }
    }

    private readonly System.Collections.Generic.Stack<SnapshotRequest> snapshotPool =
        new System.Collections.Generic.Stack<SnapshotRequest>(4);

    private void ApplyBuoyancyForces()
    {
        if (!HasFreshSamples || latestSamples == null || latestSamples.Length == 0)
            return;

        float totalWeight = 0f;
        for (int i = 0; i < latestSamples.Length; i++)
        {
            if (!latestSamples[i].IsValid)
                continue;
            totalWeight += Mathf.Max(0f, samplePoints[i].liftWeight);
        }

        if (totalWeight <= 0f)
            return;

        bool slopeActive = PrepareSlopeFollowHeights();
        float gravity = Physics.gravity.magnitude;
        for (int i = 0; i < latestSamples.Length; i++)
        {
            SpectralOceanFFT.SurfaceSample sample = latestSamples[i];
            float weight = Mathf.Max(0f, samplePoints[i].liftWeight);
            if (!sample.IsValid || weight <= 0f)
                continue;

            Vector3 probeWorld = body.transform.TransformPoint(samplePoints[i].localPosition);

            // The GPU sample belongs to the probe position captured in an earlier
            // FixedUpdate. Correct height at the body's current probe XZ using the
            // local tangent plane, avoiding force spikes when a fast hull moves
            // slightly while AsyncGPUReadback is in flight.
            float waterHeightAtCurrentProbe;
            if (slopeActive)
            {
                if (!slopeValid[i])
                    continue;
                waterHeightAtCurrentProbe = slopeHeights[i];
            }
            else if (!TryGetWaterHeightAtProbe(sample, probeWorld, out waterHeightAtCurrentProbe))
            {
                continue;
            }
            float submergedDepth = waterHeightAtCurrentProbe - probeWorld.y;
            if (submergedDepth <= 0f)
                continue;

            float immersion = Mathf.Clamp01(submergedDepth / buoyancyDepth);
            float normalizedWeight = weight / totalWeight;
            Vector3 sampledNormal = sample.Normal.sqrMagnitude > 0.001f
                ? sample.Normal.normalized
                : Vector3.up;
            Vector3 forceDirection = Vector3.Slerp(
                Vector3.up, sampledNormal, normalInfluence).normalized;

            float liftAcceleration = gravity * buoyancyStrength * immersion * normalizedWeight;
            body.AddForceAtPosition(
                forceDirection * liftAcceleration,
                probeWorld,
                ForceMode.Acceleration);

            Vector3 pointVelocity = body.GetPointVelocity(probeWorld);
            Vector3 dragAcceleration = -pointVelocity
                * (waterDrag * RuntimeDragScale * immersion * normalizedWeight);
            dragAcceleration = Vector3.ClampMagnitude(dragAcceleration, maxDragAcceleration * RuntimeDragScale);
            body.AddForceAtPosition(dragAcceleration, probeWorld, ForceMode.Acceleration);
        }
    }

    /// <summary>
    /// Fits a plane to the probe water heights (hull-local x/z) and rebuilds each height as
    /// mean + follow.x * roll slope + follow.y * pitch slope + residual. The mean height is
    /// always preserved, so heave is untouched. Returns false when follow is (1,1).
    /// </summary>
    private bool PrepareSlopeFollowHeights()
    {
        Vector2 follow = RuntimeSlopeFollow;
        follow.x = Mathf.Clamp01(follow.x);
        follow.y = Mathf.Clamp01(follow.y);
        if (follow.x > 0.999f && follow.y > 0.999f)
            return false;

        int count = Mathf.Min(latestSamples.Length, samplePoints.Length);
        if (slopeHeights == null || slopeHeights.Length < count)
        {
            slopeHeights = new float[count];
            slopeValid = new bool[count];
        }

        float weightSum = 0f, meanHeight = 0f, centerX = 0f, centerZ = 0f;
        for (int i = 0; i < count; i++)
        {
            slopeValid[i] = false;
            float weight = Mathf.Max(0f, samplePoints[i].liftWeight);
            if (weight <= 0f || !latestSamples[i].IsValid)
                continue;
            Vector3 probeWorld = body.transform.TransformPoint(samplePoints[i].localPosition);
            if (!TryGetWaterHeightAtProbe(latestSamples[i], probeWorld, out float height))
                continue;
            slopeHeights[i] = height;
            slopeValid[i] = true;
            weightSum += weight;
            meanHeight += weight * height;
            centerX += weight * samplePoints[i].localPosition.x;
            centerZ += weight * samplePoints[i].localPosition.z;
        }
        if (weightSum <= 0f)
            return false;

        meanHeight /= weightSum;
        centerX /= weightSum;
        centerZ /= weightSum;

        float sxx = 0f, szz = 0f, sxh = 0f, szh = 0f;
        for (int i = 0; i < count; i++)
        {
            if (!slopeValid[i]) continue;
            float weight = Mathf.Max(0f, samplePoints[i].liftWeight);
            float dx = samplePoints[i].localPosition.x - centerX;
            float dz = samplePoints[i].localPosition.z - centerZ;
            float dh = slopeHeights[i] - meanHeight;
            sxx += weight * dx * dx; szz += weight * dz * dz;
            sxh += weight * dx * dh; szh += weight * dz * dh;
        }
        float rollSlope = sxx > 0.0001f ? sxh / sxx : 0f;
        float pitchSlope = szz > 0.0001f ? szh / szz : 0f;
        float residualFollow = 0.5f * (follow.x + follow.y);

        for (int i = 0; i < count; i++)
        {
            if (!slopeValid[i]) continue;
            float dx = samplePoints[i].localPosition.x - centerX;
            float dz = samplePoints[i].localPosition.z - centerZ;
            float dh = slopeHeights[i] - meanHeight;
            float plane = rollSlope * dx + pitchSlope * dz;
            slopeHeights[i] = meanHeight + follow.x * rollSlope * dx
                              + follow.y * pitchSlope * dz + residualFollow * (dh - plane);
        }
        return true;
    }

    private bool TryGetWaterHeightAtProbe(
        SpectralOceanFFT.SurfaceSample sample, Vector3 probeWorld, out float waterHeight)
    {
        waterHeight = sample.Height;
        if (!sample.IsValid || !IsFinite(sample.Height) || !IsFinite(sample.Normal.x) ||
            !IsFinite(sample.Normal.y) || !IsFinite(sample.Normal.z))
            return false;

        Vector3 normal = sample.Normal.sqrMagnitude > 0.001f
            ? sample.Normal.normalized
            : Vector3.up;
        if (normal.y < 0f)
            normal = -normal;

        float normalY = Mathf.Max(normal.y, 0.12f);
        Vector2 delta = new Vector2(
            probeWorld.x - sample.Position.x,
            probeWorld.z - sample.Position.z);
        Vector2 slope = new Vector2(-normal.x / normalY, -normal.z / normalY);
        slope = Vector2.ClampMagnitude(slope, maximumSampleSlope);

        float correction = Mathf.Clamp(
            Vector2.Dot(slope, delta),
            -maximumSlopeHeightCorrection,
            maximumSlopeHeightCorrection);
        waterHeight = sample.Height + correction;
        return IsFinite(waterHeight);
    }

    /// <summary>Number of buoyancy probes.</summary>
    public int ProbeCount => samplePoints != null ? samplePoints.Length : 0;

    /// <summary>
    /// World position of probe <paramref name="index"/> and its signed immersion
    /// (water height minus probe height) / buoyancyDepth: 0 = at the surface,
    /// positive = under water, negative = above it. Used by effects (foam, spray).
    /// </summary>
    public bool TryGetProbeContact(int index, out Vector3 probeWorld, out float immersion)
    {
        probeWorld = default;
        immersion = 0f;
        if (body == null || latestSamples == null || samplePoints == null || !HasFreshSamples ||
            index < 0 || index >= latestSamples.Length || index >= samplePoints.Length)
            return false;

        SpectralOceanFFT.SurfaceSample sample = latestSamples[index];
        if (!sample.IsValid)
            return false;

        probeWorld = body.transform.TransformPoint(samplePoints[index].localPosition);
        if (!TryGetWaterHeightAtProbe(sample, probeWorld, out float waterHeight))
            return false;

        immersion = Mathf.Clamp((waterHeight - probeWorld.y) / buoyancyDepth, -1f, 1f);
        return true;
    }

    private float CalculateCurrentWaterImmersion()
    {
        if (!HasFreshSamples || body == null || latestSamples == null || samplePoints == null)
            return 0f;

        float maximumImmersion = 0f;
        int count = Mathf.Min(latestSamples.Length, samplePoints.Length);
        for (int i = 0; i < count; i++)
        {
            SpectralOceanFFT.SurfaceSample sample = latestSamples[i];
            if (!sample.IsValid || samplePoints[i].liftWeight <= 0f)
                continue;

            Vector3 probeWorld = body.transform.TransformPoint(samplePoints[i].localPosition);
            if (!TryGetWaterHeightAtProbe(sample, probeWorld, out float waterHeight))
                continue;

            float submergedDepth = waterHeight - probeWorld.y;
            float immersion = Mathf.Clamp01(submergedDepth / buoyancyDepth);
            maximumImmersion = Mathf.Max(maximumImmersion, immersion);
        }

        return maximumImmersion;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void OnSurfaceSamplesReceived(
        int generation, int snapshotId, SpectralOceanFFT.SurfaceSample[] samples)
    {
        if (generation != componentGeneration)
            return;

        outstandingSnapshots = Mathf.Max(0, outstandingSnapshots - 1);
        immersionCacheFrame = -1;
        if (!isActiveAndEnabled || samples == null || samples.Length != SamplePointCount)
            return;
        if (preferNewestSnapshot && snapshotId < latestAcceptedSnapshotId)
            return;

        latestAcceptedSnapshotId = Mathf.Max(latestAcceptedSnapshotId, snapshotId);
        latestSamples = samples;
        latestSampleTime = -1000f;
        hasAnyValidSamples = false;

        Vector3 positionSum = Vector3.zero;
        Vector3 normalSum = Vector3.zero;
        Vector3 waveDisplacementSum = Vector3.zero;
        Vector2 latticeSum = Vector2.zero;
        float heightSum = 0f;
        int validCount = 0;
        float meanWaterLevel = ocean != null ? ocean.WaterLevelY : 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            if (!samples[i].IsValid)
                continue;
            positionSum += samples[i].Position;
            normalSum += samples[i].Normal;
            heightSum += samples[i].Height;
            latticeSum += samples[i].LatticeDisplacementXZ;
            waveDisplacementSum += new Vector3(
                samples[i].Position.x - samples[i].BasePosition.x,
                samples[i].Position.y - meanWaterLevel,
                samples[i].Position.z - samples[i].BasePosition.z);
            latestSampleTime = Mathf.Max(latestSampleTime, samples[i].SampleTime);
            validCount++;
        }

        if (validCount > 0)
        {
            hasAnyValidSamples = true;
            float inverseCount = 1f / validCount;
            averageLatticeDisplacement = latticeSum * inverseCount;
            averageWaterPosition = positionSum * inverseCount;
            averageWaterHeight = heightSum * inverseCount;
            averageWaterNormal = normalSum.sqrMagnitude > 0.001f
                ? normalSum.normalized
                : Vector3.up;

            Vector3 averageWaveDisplacement = waveDisplacementSum * inverseCount;
            float sampleDelta = latestSampleTime - previousAverageWaveSampleTime;
            if (previousAverageWaveSampleTime > 0f && sampleDelta > 0.005f && sampleDelta < 0.5f)
            {
                Vector3 rawVelocity = (averageWaveDisplacement - previousAverageWaveDisplacement)
                    / sampleDelta;
                rawVelocity = Vector3.ClampMagnitude(rawVelocity, maximumEstimatedWaveVelocity);
                float velocityBlend = 1f - Mathf.Exp(-sampleDelta * 6f);
                averageWaterVelocity = Vector3.Lerp(
                    averageWaterVelocity, rawVelocity, velocityBlend);

                previousAverageWaveDisplacement = averageWaveDisplacement;
                previousAverageWaveSampleTime = latestSampleTime;
            }
            else if (previousAverageWaveSampleTime <= 0f || sampleDelta >= 0.5f || sampleDelta < -0.005f)
            {
                // The FFT can be intentionally rate-limited (e.g. 30 Hz) while
                // physics requests samples at 50 Hz. Repeated readbacks of the same
                // map have an identical SampleTime; keep the last measured velocity
                // rather than resetting it to zero on every duplicate snapshot.
                averageWaterVelocity = Vector3.zero;
                previousAverageWaveDisplacement = averageWaveDisplacement;
                previousAverageWaveSampleTime = latestSampleTime;
            }

            try
            {
                SamplesUpdated?.Invoke(latestSamples);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
    }

    private void RebuildSampleArrays()
    {
        componentGeneration++;
        int count = samplePoints != null ? samplePoints.Length : 0;
        queryPositions = new Vector3[count];
        latestSamples = new SpectralOceanFFT.SurfaceSample[count];
        outstandingSnapshots = 0;
        latestAcceptedSnapshotId = -1;
        latestSampleTime = -1000f;
        hasAnyValidSamples = false;
        averageWaterHeight = 0f;
        averageWaterPosition = Vector3.zero;
        averageWaterNormal = Vector3.up;
        averageWaterVelocity = Vector3.zero;
        averageLatticeDisplacement = Vector2.zero;
        previousAverageWaveDisplacement = Vector3.zero;
        previousAverageWaveSampleTime = -1000f;
    }

    /// <summary>
    /// Average horizontal displacement of the water particles under the hull probes
    /// (what a Gerstner wave does to a floating body). Zero-mean over a wave cycle.
    /// </summary>
    public bool TryGetLatticeDisplacement(out Vector2 displacement, out float sampleAge)
    {
        sampleAge = SampleAge;
        displacement = averageLatticeDisplacement;
        return HasFreshSamples;
    }

    /// <summary>Returns the latest accepted sample for a specific hull probe.</summary>
    public bool TryGetLatestSample(int index, out SpectralOceanFFT.SurfaceSample sample)
    {
        if (HasFreshSamples && latestSamples != null && index >= 0 && index < latestSamples.Length &&
            latestSamples[index].IsValid)
        {
            sample = latestSamples[index];
            return true;
        }

        sample = default;
        return false;
    }

    /// <summary>Convenience API for future navigation / water-following systems.</summary>
    public bool TryGetWaterPosition(int index, out Vector3 waterPosition)
    {
        if (TryGetLatestSample(index, out SpectralOceanFFT.SurfaceSample sample))
        {
            waterPosition = sample.Position;
            return true;
        }

        waterPosition = default;
        return false;
    }

    /// <summary>
    /// Navigation-friendly aggregate. Position and normal are averaged from the
    /// latest valid probe sample snapshot; sampleAge includes GPU readback time.
    /// </summary>
    public bool TryGetAverageWaterSurface(
        out Vector3 position, out Vector3 normal, out float sampleAge)
    {
        return TryGetAverageWaterSurface(
            out position, out normal, out _, out sampleAge);
    }

    /// <summary>
    /// Navigation-friendly aggregate including estimated water-particle velocity.
    /// The velocity is derived from consecutive wave displacements at the probes,
    /// with the object's own probe translation removed.
    /// </summary>
    public bool TryGetAverageWaterSurface(
        out Vector3 position, out Vector3 normal, out Vector3 waterVelocity, out float sampleAge)
    {
        sampleAge = SampleAge;
        if (!HasFreshSamples)
        {
            position = default;
            normal = Vector3.up;
            waterVelocity = Vector3.zero;
            return false;
        }

        position = averageWaterPosition;
        normal = averageWaterNormal;
        waterVelocity = averageWaterVelocity;
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (samplePoints == null)
            return;

        for (int i = 0; i < samplePoints.Length; i++)
        {
            Vector3 point = transform.TransformPoint(samplePoints[i].localPosition);
            Gizmos.color = latestSamples != null && i < latestSamples.Length && latestSamples[i].IsValid
                ? Color.cyan
                : Color.yellow;
            Gizmos.DrawWireSphere(point, 0.12f);
            Gizmos.DrawLine(point, point + Vector3.up * Mathf.Max(0.2f, buoyancyDepth));
        }
    }
}
