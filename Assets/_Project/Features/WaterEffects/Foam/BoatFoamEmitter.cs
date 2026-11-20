using Assets._Project.Features.Boat;
using Assets._Project.Features.Buoy;
using UnityEngine;

namespace Assets._Project.Features.WaterEffects
{
    /// <summary>
    /// Makes a boat leave foam and ripples on the water surface.
    ///
    /// Foam follows the HULL OUTLINE: the hull footprint is measured once from the boat's
    /// meshes (falling back to colliders, then to the buoyancy probe spread), and foam is
    /// stamped along that outline at the real water height. Leading edges foam most while
    /// moving; a stationary hull keeps a thin waterline rim.
    ///
    /// Movement wakes are emitted as direction-aware Kelvin V packets, with a low-energy
    /// heightfield impulse retained as supplemental displacement. Added automatically by
    /// WaterWakeField for live BoatController roots.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(WaterContactFoam))]
    public sealed class BoatFoamEmitter : MonoBehaviour
    {
        [Header("References (auto-resolved)")]
        [SerializeField] private BoatController boat;
        [SerializeField] private OceanBuoyancy probes;
        [SerializeField] private WaterContactFoam foam;
        [SerializeField] private SpectralOceanFFT ocean;

        [Header("Hull Outline")]
        [Tooltip("Hull mesh object names measured for the outline (e.g. \"Boat\"). Empty = every non-ignored mesh under the boat.")]
        [SerializeField] private string[] hullNames = { "Boat" };
        [Tooltip("Words that exclude a mesh when no hull name matches (sail, rope...).")]
        [SerializeField] private string[] ignoreWords = { "sail", "mast", "rope", "sprit", "block", "tiller", "foot" };
        [Tooltip("Optional manual hull size (x width, y height, z length) in metres. Zero = measure from meshes.")]
        [SerializeField] private Vector3 hullSizeOverride;
        [Tooltip("Where the waterline sits on the hull height (0 = keel, 1 = rim). Foam is stamped around that cross-section.")]
        [SerializeField, Range(0.05f, 0.95f)] private float waterlineFraction = 0.4f;
        [Tooltip("Foam stamps placed around the hull waterline.")]
        [SerializeField, Range(6, 48)] private int outlinePoints = 20;

        [Header("Wake Foam")]
        [Tooltip("Planar speed (m/s) where hull foam starts to build.")]
        [SerializeField, Min(0f)] private float minSpeed = 0.25f;
        [Tooltip("Planar speed (m/s) where hull foam is at full intensity.")]
        [SerializeField, Min(0.1f)] private float fullSpeed = 7f;
        [Tooltip("Foam intensity while stationary, so a floating hull still rims the waterline.")]
        [SerializeField, Range(0f, 1f)] private float idleIntensity = 0.25f;
        [Tooltip("How much more the leading side of the hull foams than the trailing side.")]
        [SerializeField, Range(0f, 1f)] private float leadBias = 0.6f;
        [Tooltip("How far (m) the water may be below the hull bottom / above the rail before a point stops foaming.")]
        [SerializeField, Min(0f)] private float contactSlack = 0.15f;

        [Header("Kelvin Wake Simulation")]
        [Tooltip("Seconds between source events. Distance between circular impulses grows with boat speed to build a continuous moving wake.")]
        [SerializeField, Range(0.2f, 1.2f)] private float wakeImpulseInterval = 0.38f;
        [Tooltip("Base radius (metres) of each circular height impulse.")]
        [SerializeField, Range(0.2f, 1.5f)] private float wakeImpulseRadius = 0.8f;
        [Tooltip("Vertical velocity injected into the water heightfield at each source point.")]
        [SerializeField, Range(0f, 0.9f)] private float wakeImpulseStrength = 0.42f;
        [Tooltip("Maximum trailing distance over which the two Kelvin wake arms spread.")]
        [SerializeField, Min(2f)] private float wakeMaxLength = 22f;
        [Tooltip("Lifts the size estimate so even a small boat makes clearly visible rings: size = floor + (1 - floor) * estimate.")]
        [SerializeField, Range(0f, 0.9f)] private float rippleSizeFloor = 0.35f;
        [Tooltip("Seconds between small ripples while drifting or idle.")]
        [SerializeField, Min(0.2f)] private float idleRippleInterval = 2.4f;
        [Tooltip("Mass (kg) mapped to size 0 and size 1 for the mass part of the size estimate.")]
        [SerializeField] private Vector2 massRange = new Vector2(80f, 2500f);
        [Tooltip("Hull footprint sqrt(length*width) (m) mapped to size 0 and size 1.")]
        [SerializeField] private Vector2 footprintRange = new Vector2(1.2f, 7f);

        [Header("Slam splash")]
        [Tooltip("Impact speed (m/s) that produces a full-size splash and ripple.")]
        [SerializeField, Min(0.5f)] private float fullSlamSpeed = 8f;

        private HullOutline hull;
        private Vector3 hullCenter;      // boat local
        private Vector3 hullHalfSize;    // boat local half extents
        private bool hullMeasured;
        private float sizeEstimate01 = 0.5f;
        private float rippleSize01 = 0.5f;
        private Vector2[] outlineLocal;
        private Vector2[] outlineNormal;

        private Vector3[] previousPoints;
        private bool[] hadPrevious;
        private float wakeDistanceAccumulator;
        private float wakeTrailLength;
        private float wakeIdleTime;
        private Vector3 lastWakeDirection;
        private float nextIdleRippleTime;
        private bool warnedNoSamples;
        private Vector3 previousBoatPosition;
        private bool hasPreviousBoatPosition;
        private Vector3 previousPhysicsPosition;
        private bool hasPreviousPhysicsPosition;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            Resolve();
        }

        private void OnEnable()
        {
            Resolve();
            if (boat != null)
                boat.HullSlammed += OnHullSlammed;
            hullMeasured = false;
            wakeDistanceAccumulator = 0f;
            wakeTrailLength = 0f;
            wakeIdleTime = 0f;
            lastWakeDirection = Vector3.zero;
            nextIdleRippleTime = Time.time + 0.5f;
            previousBoatPosition = transform.position;
            hasPreviousBoatPosition = true;
            previousPhysicsPosition = boat != null && boat.Body != null
                ? boat.Body.position : transform.position;
            hasPreviousPhysicsPosition = true;
        }

        private void OnDisable()
        {
            if (boat != null)
                boat.HullSlammed -= OnHullSlammed;
        }

        [ContextMenu("Re-measure hull")]
        private void RemeasureHull()
        {
            hullMeasured = false;
        }

        [ContextMenu("Log hull measurement")]
        private void LogHull()
        {
            if (!hullMeasured) MeasureHull();
            Debug.Log("BoatFoamEmitter hull: meshes=" + hull.MeshCount + " fromVertices=" + hull.FromVertices +
                      " size=" + (hullHalfSize * 2f) + " waterlineY=" + hull.WaterlineY +
                      (hull.FromVertices ? "" : "  (enable Read/Write on the hull model for an exact outline)"), this);
        }

        private void Resolve()
        {
            if (boat == null) boat = GetComponent<BoatController>();
            if (probes == null) probes = GetComponent<OceanBuoyancy>();
            if (foam == null) foam = GetComponent<WaterContactFoam>();
            if (ocean == null) ocean = Object.FindAnyObjectByType<SpectralOceanFFT>();
        }

        // ------------------------------------------------------------------ hull measurement

        private void MeasureHull()
        {
            hullMeasured = true;
            hull = HullOutline.Measure(transform, hullNames, ignoreWords, hullSizeOverride, waterlineFraction);
            hullCenter = hull.Center;
            hullHalfSize = hull.HalfSize;

            outlineLocal = new Vector2[outlinePoints];
            outlineNormal = new Vector2[outlinePoints];
            HullOutline.Resample(hull.Waterline, outlinePoints, outlineLocal, outlineNormal);

            // Size estimate 0..1 from mass + footprint (+ probe spread when it is larger).
            float footprint = Mathf.Sqrt(hullHalfSize.x * hullHalfSize.z * 4f);
            footprint = Mathf.Max(footprint, EstimateProbeFootprint());
            float sizeTerm = Mathf.InverseLerp(footprintRange.x, footprintRange.y, footprint);
            float mass = boat != null && boat.Body != null ? boat.Body.mass : 0f;
            float massTerm = mass > 0f ? Mathf.InverseLerp(massRange.x, massRange.y, mass) : sizeTerm;
            sizeEstimate01 = Mathf.Clamp01(0.6f * sizeTerm + 0.4f * massTerm);
            rippleSize01 = Mathf.Lerp(rippleSizeFloor, 1f, sizeEstimate01);
        }

        private float EstimateProbeFootprint()
        {
            if (probes == null || probes.LatestSamples == null)
                return 0f;
            SpectralOceanFFT.SurfaceSample[] samples = probes.LatestSamples;
            bool any = false;
            Vector2 min = Vector2.zero, max = Vector2.zero;
            for (int i = 0; i < samples.Length; i++)
            {
                if (!samples[i].IsValid)
                    continue;
                Vector3 l = transform.InverseTransformPoint(samples[i].BasePosition);
                Vector2 p = new Vector2(l.x, l.z);
                if (!any) { min = max = p; any = true; }
                else { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            }
            return any ? Mathf.Sqrt(Mathf.Max(0.01f, (max.x - min.x) * (max.y - min.y))) : 0f;
        }

        // ------------------------------------------------------------------ water height

        /// <summary>Inverse-distance blend of the probe water heights around a world XZ point.</summary>
        private bool TryGetWaterHeight(Vector3 world, out float height)
        {
            height = 0f;
            SpectralOceanFFT.SurfaceSample[] samples = probes != null && probes.HasFreshSamples ? probes.LatestSamples : null;
            if (samples == null || samples.Length == 0)
            {
                if (ocean == null) ocean = Object.FindAnyObjectByType<SpectralOceanFFT>();
                if (ocean == null) return false;
                height = ocean.WaterLevelY;
                return true;
            }

            float sum = 0f, weightSum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                if (!samples[i].IsValid)
                    continue;
                float dx = samples[i].Position.x - world.x;
                float dz = samples[i].Position.z - world.z;
                float w = 1f / (dx * dx + dz * dz + 0.25f);
                sum += samples[i].Position.y * w;
                weightSum += w;
            }

            if (weightSum <= 0f)
            {
                if (ocean == null) ocean = Object.FindAnyObjectByType<SpectralOceanFFT>();
                if (ocean == null) return false;
                height = ocean.WaterLevelY;
                return true;
            }
            height = sum / weightSum;
            return true;
        }

        // ------------------------------------------------------------------ fixed-step movement wake

        private void FixedUpdate()
        {
            if (boat == null)
                Resolve();
            if (boat == null)
                return;

            Rigidbody body = boat.Body;
            Vector3 currentPosition = body != null ? body.position : transform.position;
            if (!hasPreviousPhysicsPosition)
            {
                previousPhysicsPosition = currentPosition;
                hasPreviousPhysicsPosition = true;
                return;
            }

            float dt = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            Vector3 displacement = Vector3.ProjectOnPlane(
                currentPosition - previousPhysicsPosition, Vector3.up);
            previousPhysicsPosition = currentPosition;

            Vector3 measuredVelocity = displacement / dt;
            Vector3 rigidbodyVelocity = body != null
                ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up)
                : Vector3.zero;

            // Body velocity is reliable for dynamic boats; measured displacement also
            // covers kinematic/moved-by-script boats and catches a velocity that is
            // stale for one physics step. Prefer the measured direction when present.
            Vector3 planarVelocity = measuredVelocity.sqrMagnitude > 0.0225f
                ? measuredVelocity
                : rigidbodyVelocity;
            if (measuredVelocity.sqrMagnitude > 0.0225f && rigidbodyVelocity.sqrMagnitude > 0.0225f &&
                Vector3.Dot(measuredVelocity, rigidbodyVelocity) > 0f)
            {
                planarVelocity = Vector3.Lerp(measuredVelocity, rigidbodyVelocity, 0.25f);
            }

            float speed = planarVelocity.magnitude;
            Vector3 travelDirection = speed > 0.15f
                ? planarVelocity / speed
                : Vector3.zero;

            // Fallback for a boat driven by a controller that rotates/moves the root
            // without exposing a useful Rigidbody velocity.
            if (travelDirection.sqrMagnitude < 0.001f &&
                Mathf.Abs(boat.ForwardSpeed) > 0.15f)
            {
                travelDirection = Vector3.ProjectOnPlane(
                    transform.forward * Mathf.Sign(boat.ForwardSpeed), Vector3.up).normalized;
                speed = Mathf.Max(speed, Mathf.Abs(boat.ForwardSpeed));
            }

            if (speed <= minSpeed || travelDirection.sqrMagnitude < 0.001f)
            {
                wakeDistanceAccumulator = 0f;
                wakeIdleTime += dt;
                if (wakeIdleTime > 0.75f)
                {
                    wakeTrailLength = 0f;
                    lastWakeDirection = Vector3.zero;
                }
                return;
            }

            if (lastWakeDirection.sqrMagnitude > 0.01f &&
                Vector3.Dot(lastWakeDirection, travelDirection) < 0.72f)
            {
                // Start a new packet stream after a real heading change. Existing
                // packets retain their old direction in the shader.
                wakeDistanceAccumulator = 0f;
                wakeTrailLength = 0f;
            }

            lastWakeDirection = travelDirection;
            wakeIdleTime = 0f;

            float movementDistance = Mathf.Max(displacement.magnitude, speed * dt * 0.75f);
            wakeTrailLength = Mathf.Min(wakeMaxLength, wakeTrailLength + movementDistance);
            wakeDistanceAccumulator += movementDistance;

            // A shorter minimum distance is important for slow sailboats. At speed,
            // events remain spaced in world distance so the wake does not become a
            // frame-rate-dependent particle stream.
            float spacing = Mathf.Clamp(speed * wakeImpulseInterval, 0.75f, 2.8f);
            float speed01 = Mathf.InverseLerp(minSpeed, Mathf.Max(fullSpeed, 1f), speed);
            float radius = wakeImpulseRadius * Mathf.Lerp(1f, 1.35f, sizeEstimate01);
            float trailBuild = Mathf.Lerp(0.62f, 1f,
                Mathf.Clamp01(wakeTrailLength / Mathf.Max(2f, wakeMaxLength)));
            float strength = wakeImpulseStrength * Mathf.Lerp(0.52f, 1f, speed01) * trailBuild;
            float sternOffset = Mathf.Max(0.8f, hullHalfSize.z * 0.82f);

            int emitted = 0;
            while (wakeDistanceAccumulator >= spacing && emitted < 4)
            {
                float overshoot = wakeDistanceAccumulator - spacing;
                Vector3 pathPosition = currentPosition - travelDirection * overshoot;
                Vector3 stern = pathPosition - travelDirection * sternOffset;
                WaterWakeField.EmitKelvinWake(
                    stern, travelDirection, radius, strength, wakeMaxLength, 19.47f, speed);
                wakeDistanceAccumulator -= spacing;
                emitted++;
            }
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (boat == null)
                return;

            bool hasFreshSamples = probes != null && probes.HasFreshSamples && probes.LatestSamples != null;
            if (!hullMeasured)
                MeasureHull();

            int count = outlineLocal.Length;
            if (previousPoints == null || previousPoints.Length != count)
            {
                previousPoints = new Vector3[count];
                hadPrevious = new bool[count];
            }

            Rigidbody body = boat.Body;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 planarVelocity = body != null
                ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up)
                : Vector3.zero;
            if (planarVelocity.sqrMagnitude < 0.04f && hasPreviousBoatPosition)
            {
                Vector3 transformVelocity = Vector3.ProjectOnPlane(
                    (transform.position - previousBoatPosition) / dt, Vector3.up);
                if (transformVelocity.sqrMagnitude > planarVelocity.sqrMagnitude)
                    planarVelocity = transformVelocity;
            }
            previousBoatPosition = transform.position;
            hasPreviousBoatPosition = true;

            float speed = Mathf.Max(boat.PlanarSpeed, planarVelocity.magnitude);
            float speed01 = Mathf.InverseLerp(minSpeed, fullSpeed, speed);
            Vector3 travelDir = planarVelocity.sqrMagnitude > 0.04f
                ? planarVelocity.normalized
                : (Mathf.Abs(boat.ForwardSpeed) > minSpeed
                    ? Vector3.ProjectOnPlane(transform.forward * Mathf.Sign(boat.ForwardSpeed), Vector3.up).normalized
                    : Vector3.zero);

            // Hull foam still needs real surface samples, but the movement wake does not.
            // The shader evaluates only world XZ, so its Y can safely use the hull waterline
            // estimate when probes/ocean sampling is temporarily unavailable.
            if (hasFreshSamples && foam != null)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector3 local = new Vector3(outlineLocal[i].x, hull.WaterlineY, outlineLocal[i].y);
                    Vector3 world = transform.TransformPoint(local);
                    if (!TryGetWaterHeight(world, out float waterY))
                    {
                        hadPrevious[i] = false;
                        continue;
                    }
    
                    // Only foam where the water actually meets the hull side.
                    float bottomY = transform.TransformPoint(new Vector3(local.x, hull.MinY, local.z)).y;
                    float topY = transform.TransformPoint(new Vector3(local.x, hull.MaxY, local.z)).y;
                    if (waterY < bottomY - contactSlack || waterY > topY + contactSlack)
                    {
                        hadPrevious[i] = false;
                        continue;
                    }
    
                    Vector3 point = new Vector3(world.x, waterY, world.z);
                    Vector3 from = hadPrevious[i] ? previousPoints[i] : point;
                    previousPoints[i] = point;
                    hadPrevious[i] = true;
    
                    // The side of the hull pushing into the water foams most.
                    float lead = 0.5f;
                    if (travelDir.sqrMagnitude > 0.01f)
                    {
                        Vector3 outwardWorld = transform.TransformDirection(new Vector3(outlineNormal[i].x, 0f, outlineNormal[i].y));
                        lead = Mathf.Clamp01(0.5f + 0.5f * Vector3.Dot(outwardWorld, travelDir));
                    }
                    float movingPart = speed01 * Mathf.Lerp(1f - leadBias, 1f, lead);
                    float intensity = Mathf.Max(idleIntensity, movingPart);
                    foam.EmitTrail(from, point, intensity, dt);
                }
            }

            UpdateIdleRipples(dt, speed, travelDir);
        }

        private void UpdateIdleRipples(float dt, float speed, Vector3 travelDir)
        {
            Vector3 centerWorld = transform.TransformPoint(
                new Vector3(hullCenter.x, hull.WaterlineY, hullCenter.z));
            if (TryGetWaterHeight(centerWorld, out float waterY))
                centerWorld.y = waterY;

            // Movement wakes are emitted in FixedUpdate from actual Rigidbody travel.
            // Keep only the soft circular idle pulse here.
            if (speed > minSpeed && travelDir.sqrMagnitude > 0.01f)
            {
                wakeIdleTime = 0f;
                return;
            }

            wakeIdleTime += dt;
            if (wakeIdleTime > 0.75f)
            {
                wakeTrailLength = 0f;
                lastWakeDirection = Vector3.zero;
                wakeDistanceAccumulator = 0f;
            }

            if (Time.time >= nextIdleRippleTime)
            {
                nextIdleRippleTime = Time.time + idleRippleInterval * Mathf.Lerp(1.3f, 0.8f, sizeEstimate01);
                WaterFoamField.EmitRipple(centerWorld, 0.08f, wakeImpulseRadius * 3.5f,
                    Mathf.Clamp01(0.12f * rippleSize01), 0f, 3.2f);
            }
        }

        private void OnHullSlammed(float impactSpeed)
        {
            if (foam == null || probes == null || !hullMeasured)
                return;

            float intensity = Mathf.Clamp01(impactSpeed / fullSlamSpeed);
            Vector3 centerWorld = transform.TransformPoint(new Vector3(hullCenter.x, hull.WaterlineY, hullCenter.z));
            if (TryGetWaterHeight(centerWorld, out float waterY))
                centerWorld.y = waterY;
            else
                centerWorld.y = transform.TransformPoint(new Vector3(hullCenter.x, hull.WaterlineY, hullCenter.z)).y;

            // Splash blobs around the outline + one big FULL-CIRCLE ripple.
            for (int i = 0; i < outlineLocal.Length; i += 2)
            {
                Vector3 world = transform.TransformPoint(new Vector3(outlineLocal[i].x, hull.WaterlineY, outlineLocal[i].y));
                foam.EmitBurst(new Vector3(world.x, waterY, world.z), intensity);
            }

            float startRadius = Mathf.Max(0.3f, Mathf.Max(hullHalfSize.x, 0.6f * hullHalfSize.z));
            foam.EmitRipple(centerWorld, startRadius, Mathf.Clamp01(rippleSize01 * 0.6f + intensity * 0.5f), 0.65f + 0.35f * intensity);
        }
    }
}
