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
    /// Ripples are expanding rings whose SIZE is inferred from the boat's mass, its measured
    /// hull footprint and the spread of the buoyancy probes. Added automatically by BoatController.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaterContactFoam))]
    public sealed class BoatFoamEmitter : MonoBehaviour
    {
        public enum ArcSide { Behind, Ahead }

        [Header("References (auto-resolved)")]
        [SerializeField] private BoatController boat;
        [SerializeField] private OceanBuoyancy probes;
        [SerializeField] private WaterContactFoam foam;

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
        [SerializeField, Min(0f)] private float minSpeed = 0.4f;
        [Tooltip("Planar speed (m/s) where hull foam is at full intensity.")]
        [SerializeField, Min(0.1f)] private float fullSpeed = 7f;
        [Tooltip("Foam intensity while stationary, so a floating hull still rims the waterline.")]
        [SerializeField, Range(0f, 1f)] private float idleIntensity = 0.25f;
        [Tooltip("How much more the leading side of the hull foams than the trailing side.")]
        [SerializeField, Range(0f, 1f)] private float leadBias = 0.6f;
        [Tooltip("How far (m) the water may be below the hull bottom / above the rail before a point stops foaming.")]
        [SerializeField, Min(0f)] private float contactSlack = 0.15f;

        [Header("Ripples")]
        [Tooltip("Metres travelled between ripples at size 0.5 (scaled by size).")]
        [SerializeField, Min(0.5f)] private float rippleSpacing = 3.5f;
        [Tooltip("Which way moving ripples (half circles) spread: behind the boat like a trail, or ahead like a bow wave.")]
        [SerializeField] private ArcSide arcSide = ArcSide.Behind;
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
        private Vector2[] outlineLocal;
        private Vector2[] outlineNormal;

        private Vector3[] previousPoints;
        private bool[] hadPrevious;
        private float travelled;
        private float nextIdleRippleTime;
        private bool warnedNoSamples;

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
            nextIdleRippleTime = Time.time + 0.5f;
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
            SpectralOceanFFT.SurfaceSample[] samples = probes != null ? probes.LatestSamples : null;
            if (samples == null || samples.Length == 0)
                return false;

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
                return false;
            height = sum / weightSum;
            return true;
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (probes == null || boat == null || foam == null || !foam.EmitFoam)
                return;

            if (!probes.HasFreshSamples || probes.LatestSamples == null)
            {
                if (!warnedNoSamples && Time.time > 3f)
                {
                    warnedNoSamples = true;
                    Debug.LogWarning("BoatFoamEmitter: no fresh water samples from OceanBuoyancy, so no hull foam yet.", this);
                }
                return;
            }

            if (!hullMeasured)
                MeasureHull();

            int count = outlineLocal.Length;
            if (previousPoints == null || previousPoints.Length != count)
            {
                previousPoints = new Vector3[count];
                hadPrevious = new bool[count];
            }

            Rigidbody body = boat.Body;
            float dt = Time.deltaTime;
            float speed = boat.PlanarSpeed;
            float speed01 = Mathf.InverseLerp(minSpeed, fullSpeed, speed);
            Vector3 travelDir = body != null
                ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).normalized
                : Vector3.zero;

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

            UpdateRipples(dt, speed, speed01, travelDir);
        }

        private void UpdateRipples(float dt, float speed, float speed01, Vector3 travelDir)
        {
            Vector3 centerWorld = transform.TransformPoint(new Vector3(hullCenter.x, hull.WaterlineY, hullCenter.z));
            if (!TryGetWaterHeight(centerWorld, out float waterY))
                return;
            centerWorld.y = waterY;

            float startRadius = Mathf.Max(0.3f, Mathf.Min(hullHalfSize.x, hullHalfSize.z) * 0.9f);

            // Moving: a half-circle ring every few metres, spreading away behind (or ahead of) the hull.
            travelled += speed * dt;
            float spacing = rippleSpacing * Mathf.Lerp(1.3f, 0.7f, sizeEstimate01);
            if (speed01 > 0.05f && travelled >= spacing && travelDir.sqrMagnitude > 0.01f)
            {
                travelled = 0f;
                Vector3 facing = arcSide == ArcSide.Behind ? -travelDir : travelDir;
                foam.EmitRipple(centerWorld, startRadius, sizeEstimate01 * Mathf.Lerp(0.6f, 1f, speed01),
                    Mathf.Lerp(0.5f, 1f, speed01), facing, 1f);
            }

            // Idle / drifting: faint full circles from the bobbing hull.
            if (Time.time >= nextIdleRippleTime)
            {
                nextIdleRippleTime = Time.time + idleRippleInterval * Mathf.Lerp(1.3f, 0.8f, sizeEstimate01);
                foam.EmitRipple(centerWorld, startRadius, sizeEstimate01 * 0.45f, 0.4f);
            }
        }

        private void OnHullSlammed(float impactSpeed)
        {
            if (foam == null || probes == null || !hullMeasured)
                return;

            float intensity = Mathf.Clamp01(impactSpeed / fullSlamSpeed);
            Vector3 centerWorld = transform.TransformPoint(new Vector3(hullCenter.x, hull.WaterlineY, hullCenter.z));
            if (!TryGetWaterHeight(centerWorld, out float waterY))
                return;
            centerWorld.y = waterY;

            // Splash blobs around the outline + one big FULL-CIRCLE ripple.
            for (int i = 0; i < outlineLocal.Length; i += 2)
            {
                Vector3 world = transform.TransformPoint(new Vector3(outlineLocal[i].x, hull.WaterlineY, outlineLocal[i].y));
                foam.EmitBurst(new Vector3(world.x, waterY, world.z), intensity);
            }

            float startRadius = Mathf.Max(0.3f, Mathf.Min(hullHalfSize.x, hullHalfSize.z));
            foam.EmitRipple(centerWorld, startRadius, Mathf.Clamp01(sizeEstimate01 * 0.5f + intensity * 0.6f), 0.6f + 0.4f * intensity);
        }
    }
}
