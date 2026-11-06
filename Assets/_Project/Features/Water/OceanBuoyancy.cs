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
        bool queued = ocean.RequestSurfaceSamples(
            queryPositions,
            samples => OnSurfaceSamplesReceived(generation, snapshotId, samples));
        if (!queued)
            outstandingSnapshots = Mathf.Max(0, outstandingSnapshots - 1);
    }

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
            Vector3 sampledNormal = sample.Normal.sqrMagnitude > 0.001f
                ? sample.Normal.normalized
                : Vector3.up;
            float normalY = Mathf.Max(sampledNormal.y, 0.2f);
            Vector2 probeDelta = new Vector2(
                probeWorld.x - sample.Position.x,
                probeWorld.z - sample.Position.z);
            Vector2 surfaceSlope = new Vector2(
                -sampledNormal.x / normalY,
                -sampledNormal.z / normalY);
            float heightCorrection = Mathf.Clamp(
                Vector2.Dot(surfaceSlope, probeDelta), -2f, 2f);
            float waterHeightAtCurrentProbe = sample.Height + heightCorrection;
            float submergedDepth = waterHeightAtCurrentProbe - probeWorld.y;
            if (submergedDepth <= 0f)
                continue;

            float immersion = Mathf.Clamp01(submergedDepth / buoyancyDepth);
            float normalizedWeight = weight / totalWeight;
            Vector3 forceDirection = Vector3.Slerp(
                Vector3.up, sampledNormal, normalInfluence).normalized;

            float liftAcceleration = gravity * buoyancyStrength * immersion * normalizedWeight;
            body.AddForceAtPosition(
                forceDirection * liftAcceleration,
                probeWorld,
                ForceMode.Acceleration);

            Vector3 pointVelocity = body.GetPointVelocity(probeWorld);
            Vector3 dragAcceleration = -pointVelocity
                * (waterDrag * immersion * normalizedWeight);
            dragAcceleration = Vector3.ClampMagnitude(dragAcceleration, maxDragAcceleration);
            body.AddForceAtPosition(dragAcceleration, probeWorld, ForceMode.Acceleration);
        }
    }

    private void OnSurfaceSamplesReceived(
        int generation, int snapshotId, SpectralOceanFFT.SurfaceSample[] samples)
    {
        if (generation != componentGeneration)
            return;

        outstandingSnapshots = Mathf.Max(0, outstandingSnapshots - 1);
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
                rawVelocity = Vector3.ClampMagnitude(rawVelocity, 30f);
                float velocityBlend = 1f - Mathf.Exp(-sampleDelta * 6f);
                averageWaterVelocity = Vector3.Lerp(
                    averageWaterVelocity, rawVelocity, velocityBlend);
            }
            else
            {
                averageWaterVelocity = Vector3.zero;
            }

            previousAverageWaveDisplacement = averageWaveDisplacement;
            previousAverageWaveSampleTime = latestSampleTime;

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
        previousAverageWaveDisplacement = Vector3.zero;
        previousAverageWaveSampleTime = -1000f;
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
