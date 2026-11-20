using UnityEngine;

namespace Assets._Project.Features.WaterEffects
{
    /// <summary>
    /// Water-contact effects for non-controllable objects moved by Timeline or animation.
    /// The component samples the FFT water surface near a SphereCollider, emits a subtle
    /// foam trail while the sphere intersects the surface, and deposits distance-spaced
    /// Kelvin wake packets whenever the object's world position changes while in contact.
    ///
    /// No Rigidbody, movement controller, or trigger callbacks are required.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(120)]
    [RequireComponent(typeof(SphereCollider))]
    public sealed class InanimateFoamEmitter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SphereCollider sphereCollider;
        [SerializeField] private SpectralOceanFFT ocean;

        [Header("Water Surface Sampling")]
        [Tooltip("Seconds between asynchronous FFT height queries.")]
        [SerializeField, Min(0.03f)] private float sampleInterval = 0.1f;
        [Tooltip("A returned FFT surface sample older than this is ignored.")]
        [SerializeField, Min(0.05f)] private float maximumSampleAge = 0.4f;
        [Tooltip("Retry a query if its asynchronous callback does not arrive in time.")]
        [SerializeField, Min(0.1f)] private float sampleTimeout = 0.5f;
        [Tooltip("Reject a readback if the fish has moved farther than this from the queried XZ.")]
        [SerializeField, Min(0.1f)] private float maximumReadbackDrift = 1.25f;
        [Tooltip("Reject samples whose inverse surface mapping residual exceeds this value.")]
        [SerializeField, Range(0.01f, 0.5f)] private float maximumQueryError = 0.25f;
        [Tooltip("Use the ocean's mean water plane briefly when FFT sampling is not available yet.")]
        [SerializeField] private bool allowMeanWaterLevelFallback = true;
        [Tooltip("How far the sphere's bottom may be above the surface and still count as touching.")]
        [SerializeField, Min(0f)] private float aboveSurfaceTolerance = 0.12f;
        [Tooltip("Stop emitting when the water surface is deeper than this fraction of the sphere radius below its centre.")]
        [SerializeField, Range(0.1f, 0.95f)] private float deepSubmergeFraction = 0.72f;
        [Tooltip("Soft fade distance at the upper and lower contact limits.")]
        [SerializeField, Min(0.01f)] private float contactFadeDistance = 0.12f;

        [Header("Kelvin Movement Wake")]
        [Tooltip("Planar speed below which no movement wake is emitted.")]
        [SerializeField, Min(0f)] private float minimumWakeSpeed = 0.3f;
        [Tooltip("Distance travelled between Kelvin wake source packets.")]
        [SerializeField, Min(0.1f)] private float wakeSpacing = 0.7f;
        [Tooltip("Maximum length of each visible Kelvin V packet.")]
        [SerializeField, Min(1f)] private float wakeLength = 6f;
        [Tooltip("Source radius passed to the shared Kelvin wake renderer.")]
        [SerializeField, Min(0.05f)] private float wakeRadius = 0.22f;
        [Tooltip("Base strength of the Kelvin wake, multiplied by surface-contact amount.")]
        [SerializeField, Range(0f, 1f)] private float wakeStrength = 0.22f;
        [Tooltip("Place the wake source behind the sphere centre by this multiple of its world radius.")]
        [SerializeField, Range(0f, 2f)] private float wakeOriginOffset = 0.8f;
        [Tooltip("Half-angle of each Kelvin wake arm. Deep-water Kelvin wakes are about 19.47 degrees.")]
        [SerializeField, Range(5f, 35f)] private float wakeHalfAngle = 19.47f;
        [Tooltip("Ignore unusually large one-frame jumps (Timeline seeks/teleports) rather than drawing a wake across the map.")]
        [SerializeField, Min(0.5f)] private float maximumMovementStep = 3f;
        [SerializeField, Range(1, 6)] private int maximumWakePacketsPerFrame = 3;

        [Header("Subtle Surface Foam")]
        [SerializeField] private bool emitFoam = true;
        [Tooltip("World-space foam stamp radius. Keep this small for a fish.")]
        [SerializeField, Min(0.02f)] private float foamRadius = 0.13f;
        [Tooltip("Foam field stamp strength per emission. Small values create a faint trace rather than a boat-like trail.")]
        [SerializeField, Range(0f, 0.2f)] private float foamStrengthPerStamp = 0.025f;
        [Tooltip("Seconds between small foam stamps.")]
        [SerializeField, Min(0.03f)] private float foamInterval = 0.1f;

        [Header("Water Entry / Jump Impact Ripples")]
        [Tooltip("Emit a circular impact ripple pair when the object descends from above and enters the water.")]
        [SerializeField] private bool emitWaterEntryImpactRipples = true;
        [Tooltip("The sphere bottom must be this far above the sampled surface to arm an entry impact.")]
        [SerializeField, Min(0f)] private float impactArmingClearance = 0.12f;
        [Tooltip("Minimum continuous descending time above the water before an impact ripple can fire.")]
        [SerializeField, Min(0f)] private float minimumImpactFallDuration = 0.1f;
        [Tooltip("Minimum downward speed required for a water-entry impact.")]
        [SerializeField, Min(0f)] private float minimumImpactDownwardSpeed = 0.55f;
        [Tooltip("Downward speed at which the impact reaches full speed intensity.")]
        [SerializeField, Min(0.1f)] private float fullImpactDownwardSpeed = 4f;
        [Tooltip("Fall duration at which the impact reaches maximum size.")]
        [SerializeField, Min(0.2f)] private float maximumImpactFallDuration = 2.2f;
        [Tooltip("Overall impact-ring strength. Fish should generally use a restrained value.")]
        [SerializeField, Range(0f, 1f)] private float impactRippleStrength = 0.55f;
        [Tooltip("Scales both impact ring radii for smaller objects. 0.4 means 40 percent of the player-sized impact rings.")]
        [SerializeField, Range(0.1f, 1f)] private float impactRippleSizeScale = 0.4f;
        [Tooltip("Foam fleck amount on the strong first impact crest.")]
        [SerializeField, Range(0f, 0.3f)] private float impactRippleFoam = 0.035f;
        [Tooltip("A brief quiet period prevents multiple impact pairs from one bumpy surface crossing.")]
        [SerializeField, Min(0f)] private float impactRippleCooldown = 0.6f;

        [Header("Jump Take-Off Circle Ripple")]
        [Tooltip("Emit a small expanding full-circle ripple when the fish leaves the surface while moving upward.")]
        [SerializeField] private bool emitJumpTakeoffRipple = true;
        [SerializeField, Min(0f)] private float minimumJumpTakeoffSpeed = 0.45f;
        [SerializeField, Min(0.2f)] private float jumpTakeoffRippleRadius = 2.8f;
        [SerializeField, Range(0f, 1f)] private float jumpTakeoffRippleStrength = 0.32f;
        [SerializeField, Min(0.3f)] private float jumpTakeoffRippleLifetime = 2.8f;
        [SerializeField, Min(0f)] private float jumpTakeoffRippleCooldown = 0.45f;

        [Header("Debug")]
        [SerializeField] private bool drawWaterContactGizmo;

        private Vector3 previousCenter;
        private bool hasPreviousCenter;
        private float nextSampleAt;
        private bool samplePending;
        private float sampleRequestStartedAt;
        private int sampleRequestVersion;
        private float sampledSurfaceHeight;
        private float sampledAtRealtime = -1000f;
        private bool hasValidSurfaceSample;
        private bool lastContact;
        private float contactAmount;
        private float wakeDistanceAccumulator;
        private float foamTimer;
        private float impactFallDuration;
        private float maximumImpactDownwardSpeed;
        private float fallTrackingIdleTime;
        private bool waterEntryImpactArmed;
        private float nextImpactRippleTime;
        private float nextTakeoffRippleTime;
        private Vector3 previousFoamPoint;
        private bool hasPreviousFoamPoint;
        private float nextOceanSearchAt;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            sampleRequestVersion++;
            samplePending = false;
            hasValidSurfaceSample = false;
            sampledAtRealtime = -1000f;
            hasPreviousCenter = TryGetSphereCenter(out previousCenter);
            nextSampleAt = 0f;
            wakeDistanceAccumulator = 0f;
            foamTimer = 0f;
            ResetWaterEntryTracking();
            nextImpactRippleTime = 0f;
            nextTakeoffRippleTime = 0f;
            hasPreviousFoamPoint = false;
            lastContact = false;
        }

        private void OnDisable()
        {
            sampleRequestVersion++;
            samplePending = false;
            hasValidSurfaceSample = false;
            hasPreviousCenter = false;
            hasPreviousFoamPoint = false;
            wakeDistanceAccumulator = 0f;
            foamTimer = 0f;
            ResetWaterEntryTracking();
            contactAmount = 0f;
        }

        private void OnValidate()
        {
            sampleInterval = Mathf.Max(0.03f, sampleInterval);
            maximumSampleAge = Mathf.Max(0.05f, maximumSampleAge);
            sampleTimeout = Mathf.Max(0.1f, sampleTimeout);
            maximumReadbackDrift = Mathf.Max(0.1f, maximumReadbackDrift);
            maximumMovementStep = Mathf.Max(0.5f, maximumMovementStep);
            wakeSpacing = Mathf.Max(0.1f, wakeSpacing);
            wakeLength = Mathf.Max(1f, wakeLength);
            foamRadius = Mathf.Max(0.02f, foamRadius);
            foamInterval = Mathf.Max(0.03f, foamInterval);
            minimumImpactFallDuration = Mathf.Max(0f, minimumImpactFallDuration);
            maximumImpactFallDuration = Mathf.Max(minimumImpactFallDuration + 0.05f,
                maximumImpactFallDuration);
            fullImpactDownwardSpeed = Mathf.Max(minimumImpactDownwardSpeed + 0.01f,
                fullImpactDownwardSpeed);
        }

        private void Update()
        {
            ResolveReferences();
            if (sphereCollider == null || !sphereCollider.enabled || !sphereCollider.gameObject.activeInHierarchy)
            {
                ResetContactState();
                return;
            }

            if (!TryGetSphereCenter(out Vector3 currentCenter))
            {
                ResetContactState();
                return;
            }

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            if (!hasPreviousCenter)
            {
                previousCenter = currentCenter;
                hasPreviousCenter = true;
            }

            Vector3 fullDisplacement = currentCenter - previousCenter;
            Vector3 displacement = Vector3.ProjectOnPlane(fullDisplacement, Vector3.up);
            previousCenter = currentCenter;
            float travelled = displacement.magnitude;
            float verticalSpeed = fullDisplacement.y / dt;
            bool teleported = fullDisplacement.magnitude > maximumMovementStep;

            if (samplePending && Time.realtimeSinceStartup - sampleRequestStartedAt > sampleTimeout)
            {
                samplePending = false;
                sampleRequestVersion++;
            }

            if (ocean == null && Time.time >= nextOceanSearchAt)
            {
                nextOceanSearchAt = Time.time + 1f;
                ocean = Object.FindAnyObjectByType<SpectralOceanFFT>();
            }

            if (!samplePending && ocean != null && Time.realtimeSinceStartup >= nextSampleAt)
                RequestSurfaceSample(currentCenter);

            if (!TryGetCurrentSurfaceHeight(out float surfaceHeight))
            {
                ResetContactState();
                ResetWaterEntryTracking();
                return;
            }

            float worldRadius = GetWorldSphereRadius();
            contactAmount = EvaluateSurfaceContact(currentCenter.y, surfaceHeight, worldRadius);
            bool touchingSurface = contactAmount > 0.01f;
            if (!touchingSurface)
            {
                // Leaving the contact band while moving upward is the fish's jump
                // take-off. Deposit a small full-circle ring at the point it left.
                if (lastContact && !teleported)
                    TryEmitJumpTakeoffRipple(
                        new Vector3(currentCenter.x, surfaceHeight, currentCenter.z), verticalSpeed);

                if (!teleported)
                    TrackDescendingAboveWater(currentCenter, surfaceHeight, worldRadius, verticalSpeed, dt);
                else
                    ResetWaterEntryTracking();

                ResetContactState();
                return;
            }

            bool wasInContact = lastContact;
            Vector3 waterlinePoint = new Vector3(currentCenter.x, surfaceHeight, currentCenter.z);

            // A fish animated by Timeline may jump clear of the surface and fall back
            // through it. Emit the same two-layer circular impact used by swimmer
            // water-entry landings: a sharp strong crest followed by a broad soft ring.
            // Merely spawning on the surface or bobbing with a wave does not arm this.
            if (!wasInContact && !teleported)
                TryEmitWaterEntryImpact(waterlinePoint, verticalSpeed);
            else if (teleported)
                ResetWaterEntryTracking();

            if (!wasInContact || teleported)
            {
                wakeDistanceAccumulator = 0f;
                foamTimer = 0f;
                previousFoamPoint = waterlinePoint;
                hasPreviousFoamPoint = true;
            }

            // Do not create a wake from movement that occurred before the sphere
            // entered the surface-contact band.
            if (wasInContact && !teleported && travelled > 0.0001f)
                EmitMovementWake(currentCenter, displacement, travelled, dt, surfaceHeight);

            if (emitFoam)
                UpdateSurfaceFoam(waterlinePoint, dt, contactAmount);

            lastContact = true;
        }

        private void ResolveReferences()
        {
            if (sphereCollider == null)
                sphereCollider = GetComponent<SphereCollider>();
            if (ocean == null && Time.time >= nextOceanSearchAt)
            {
                nextOceanSearchAt = Time.time + 1f;
                ocean = Object.FindAnyObjectByType<SpectralOceanFFT>();
            }
        }

        private bool TryGetSphereCenter(out Vector3 center)
        {
            center = Vector3.zero;
            if (sphereCollider == null)
                return false;
            center = sphereCollider.bounds.center;
            return IsFinite(center.x) && IsFinite(center.y) && IsFinite(center.z);
        }

        private float GetWorldSphereRadius()
        {
            if (sphereCollider == null)
                return 0.25f;
            Vector3 scale = sphereCollider.transform.lossyScale;
            float maximumScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            return Mathf.Max(0.01f, sphereCollider.radius * maximumScale);
        }

        private void RequestSurfaceSample(Vector3 queryPosition)
        {
            if (ocean == null || !ocean.CanSampleSurface)
            {
                nextSampleAt = Time.realtimeSinceStartup + sampleInterval;
                return;
            }

            int version = ++sampleRequestVersion;
            samplePending = true;
            sampleRequestStartedAt = Time.realtimeSinceStartup;
            nextSampleAt = sampleRequestStartedAt + sampleInterval;
            bool queued = ocean.RequestSurfaceSample(queryPosition, sample =>
            {
                if (this == null || !isActiveAndEnabled || version != sampleRequestVersion)
                    return;

                samplePending = false;
                if (!sample.IsValid || sample.HorizontalQueryErrorMeters > maximumQueryError ||
                    !IsFinite(sample.Height) || sample.SampleTime <= 0f)
                    return;

                float sampleAge = Time.realtimeSinceStartup - sample.SampleTime;
                if (sampleAge < -0.05f || sampleAge > maximumSampleAge)
                    return;

                if (!TryGetSphereCenter(out Vector3 nowCenter))
                    return;

                Vector2 drift = new Vector2(nowCenter.x - queryPosition.x, nowCenter.z - queryPosition.z);
                if (drift.sqrMagnitude > maximumReadbackDrift * maximumReadbackDrift)
                    return;

                sampledSurfaceHeight = sample.Height;
                // Move the sampled height along its local tangent plane for the small
                // XZ drift between dispatch and callback. This avoids applying an old
                // crest/trough height at a noticeably different Timeline position.
                if (sample.Normal.y > 0.15f)
                {
                    float correction = -(sample.Normal.x * drift.x + sample.Normal.z * drift.y) / sample.Normal.y;
                    sampledSurfaceHeight += Mathf.Clamp(correction, -0.35f, 0.35f);
                }

                sampledAtRealtime = Time.realtimeSinceStartup;
                hasValidSurfaceSample = true;
            });

            if (!queued)
                samplePending = false;
        }

        private bool TryGetCurrentSurfaceHeight(out float height)
        {
            float now = Time.realtimeSinceStartup;
            if (hasValidSurfaceSample)
            {
                // Once a real animated-surface sample has been received, never
                // replace it with the mean plane when it goes stale. Stop emitting
                // until a fresh FFT sample arrives instead of following the wrong
                // crest/trough height.
                if (now - sampledAtRealtime <= maximumSampleAge)
                {
                    height = sampledSurfaceHeight;
                    return true;
                }
                height = 0f;
                return false;
            }

            if (allowMeanWaterLevelFallback && ocean != null)
            {
                height = ocean.WaterLevelY;
                return true;
            }

            height = 0f;
            return false;
        }

        private float EvaluateSurfaceContact(float sphereCenterY, float surfaceY, float radius)
        {
            // Positive offset means the sphere centre is above the water plane.
            float centreAboveWater = sphereCenterY - surfaceY;
            float lowerLimit = -radius * deepSubmergeFraction;
            float upperLimit = radius + aboveSurfaceTolerance;

            float lowerFade = Smooth01(Mathf.InverseLerp(
                lowerLimit - contactFadeDistance, lowerLimit, centreAboveWater));
            float upperFade = 1f - Smooth01(Mathf.InverseLerp(
                upperLimit - contactFadeDistance, upperLimit, centreAboveWater));
            return Mathf.Clamp01(lowerFade * upperFade);
        }

        private void TryEmitJumpTakeoffRipple(Vector3 waterlinePoint, float verticalSpeed)
        {
            if (!emitJumpTakeoffRipple || verticalSpeed < minimumJumpTakeoffSpeed ||
                jumpTakeoffRippleStrength <= 0.001f || Time.time < nextTakeoffRippleTime)
                return;

            // A single circular ripple marks where the fish leaves the surface.
            // Directional Kelvin packets remain reserved for horizontal movement.
            WaterFoamField.EmitRipple(
                waterlinePoint,
                0.12f,
                Mathf.Max(0.3f, jumpTakeoffRippleRadius),
                Mathf.Clamp01(jumpTakeoffRippleStrength),
                0.025f * Mathf.Clamp01(jumpTakeoffRippleStrength),
                Mathf.Max(0.3f, jumpTakeoffRippleLifetime),
                Vector2.zero,
                0f,
                1f);

            nextTakeoffRippleTime = Time.time + jumpTakeoffRippleCooldown;
        }

        private void TrackDescendingAboveWater(Vector3 center, float surfaceY,
            float sphereRadius, float verticalSpeed, float dt)
        {
            if (!emitWaterEntryImpactRipples)
            {
                ResetWaterEntryTracking();
                return;
            }

            float bottomClearance = center.y - sphereRadius - surfaceY;
            if (bottomClearance <= impactArmingClearance)
            {
                fallTrackingIdleTime += dt;
                if (fallTrackingIdleTime > 0.3f)
                    ResetWaterEntryTracking();
                return;
            }

            // Only a real downward trajectory arms the impact. Upward movement means
            // the fish is jumping out of the water, so discard any earlier fall.
            if (verticalSpeed > 0.2f)
            {
                ResetWaterEntryTracking();
                return;
            }

            if (verticalSpeed < -0.12f)
            {
                waterEntryImpactArmed = true;
                impactFallDuration = Mathf.Min(maximumImpactFallDuration,
                    impactFallDuration + dt);
                maximumImpactDownwardSpeed = Mathf.Max(maximumImpactDownwardSpeed,
                    -verticalSpeed);
                fallTrackingIdleTime = 0f;
            }
            else if (waterEntryImpactArmed)
            {
                // Preserve the falling interval briefly around the apex/noisy Timeline
                // samples, but do not let a very old descent trigger a later impact.
                fallTrackingIdleTime += dt;
                if (fallTrackingIdleTime > 0.3f)
                    ResetWaterEntryTracking();
            }
        }

        private void TryEmitWaterEntryImpact(Vector3 waterlinePoint, float currentVerticalSpeed)
        {
            if (!emitWaterEntryImpactRipples || !waterEntryImpactArmed ||
                Time.time < nextImpactRippleTime)
            {
                ResetWaterEntryTracking();
                return;
            }

            float impactSpeed = Mathf.Max(maximumImpactDownwardSpeed,
                Mathf.Max(0f, -currentVerticalSpeed));
            if (impactFallDuration < minimumImpactFallDuration ||
                impactSpeed < minimumImpactDownwardSpeed || impactRippleStrength <= 0.001f)
            {
                ResetWaterEntryTracking();
                return;
            }

            float duration01 = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(minimumImpactFallDuration, maximumImpactFallDuration,
                    impactFallDuration));
            float speed01 = Mathf.InverseLerp(minimumImpactDownwardSpeed,
                Mathf.Max(minimumImpactDownwardSpeed + 0.01f, fullImpactDownwardSpeed), impactSpeed);
            float impact = Mathf.Clamp01(impactRippleStrength * Mathf.Lerp(0.45f, 1f, speed01));
            float sizeScale = impactRippleSizeScale * Mathf.Lerp(0.75f, 1f, duration01);

            // First ring: compact and crisp. Second ring: wider, much softer, delayed.
            // Both are full circles (zero facing / zero arc), unlike the movement wake.
            float sharpRadius = Mathf.Lerp(6f, 12f, duration01) * sizeScale;
            WaterFoamField.EmitRipple(
                waterlinePoint,
                Mathf.Max(0.08f, 0.18f * sizeScale),
                Mathf.Max(0.8f, sharpRadius),
                Mathf.Clamp01(0.92f * impact),
                Mathf.Clamp01(impactRippleFoam * impact),
                Mathf.Lerp(2.8f, 3.8f, duration01),
                Vector2.zero, 0f, 0.82f);

            float softRadius = Mathf.Lerp(18f, 36f, duration01) * sizeScale;
            WaterFoamField.EmitRipple(
                waterlinePoint,
                Mathf.Max(0.15f, 0.55f * sizeScale),
                Mathf.Max(2f, softRadius),
                Mathf.Clamp01(0.23f * impact),
                0f,
                Mathf.Lerp(6.5f, 8.5f, duration01),
                Vector2.zero, 0f, 4.2f, 0.22f);

            nextImpactRippleTime = Time.time + impactRippleCooldown;
            ResetWaterEntryTracking();
        }

        private void ResetWaterEntryTracking()
        {
            impactFallDuration = 0f;
            maximumImpactDownwardSpeed = 0f;
            fallTrackingIdleTime = 0f;
            waterEntryImpactArmed = false;
        }

        private void EmitMovementWake(Vector3 currentCenter,
            Vector3 displacement, float travelled, float dt, float surfaceY)
        {
            float speed = travelled / dt;
            if (speed < minimumWakeSpeed || wakeStrength <= 0.001f)
            {
                wakeDistanceAccumulator = 0f;
                return;
            }

            Vector3 direction = displacement.normalized;
            float distanceAlreadyAccumulated = wakeDistanceAccumulator;
            float distanceConsumedThisFrame = 0f;
            int emitted = 0;
            Vector3 frameStart = currentCenter - displacement;
            float radius = GetWorldSphereRadius();

            while (distanceAlreadyAccumulated + (travelled - distanceConsumedThisFrame) >= wakeSpacing &&
                   emitted < maximumWakePacketsPerFrame)
            {
                float needed = wakeSpacing - distanceAlreadyAccumulated;
                distanceConsumedThisFrame += needed;
                float t = Mathf.Clamp01(distanceConsumedThisFrame / Mathf.Max(travelled, 0.0001f));
                Vector3 source = Vector3.Lerp(frameStart, currentCenter, t);
                source.y = surfaceY;
                Vector3 stern = source - direction * (radius * wakeOriginOffset);
                stern.y = surfaceY;

                float speed01 = Mathf.Clamp01(speed / 4f);
                float strength = wakeStrength * Mathf.Lerp(0.35f, 1f, speed01) * contactAmount;
                WaterWakeField.EmitKelvinWake(
                    stern,
                    direction,
                    wakeRadius,
                    strength,
                    wakeLength,
                    wakeHalfAngle,
                    speed);

                distanceAlreadyAccumulated = 0f;
                emitted++;
            }

            float remaining = Mathf.Max(0f, travelled - distanceConsumedThisFrame);
            wakeDistanceAccumulator = distanceAlreadyAccumulated + remaining;
            if (wakeDistanceAccumulator >= wakeSpacing)
                wakeDistanceAccumulator %= wakeSpacing;
        }

        private void UpdateSurfaceFoam(Vector3 waterlinePoint, float dt, float contact)
        {
            foamTimer += dt;
            if (foamTimer < foamInterval)
                return;

            foamTimer %= foamInterval;
            Vector3 from = hasPreviousFoamPoint ? previousFoamPoint : waterlinePoint;
            float segmentLength = Vector2.Distance(
                new Vector2(from.x, from.z), new Vector2(waterlinePoint.x, waterlinePoint.z));
            if (segmentLength > maximumMovementStep)
                from = waterlinePoint;

            WaterFoamField.Emit(from, waterlinePoint, foamRadius, foamStrengthPerStamp * contact);
            previousFoamPoint = waterlinePoint;
            hasPreviousFoamPoint = true;
        }

        private void ResetContactState()
        {
            lastContact = false;
            contactAmount = 0f;
            wakeDistanceAccumulator = 0f;
            foamTimer = 0f;
            hasPreviousFoamPoint = false;
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawWaterContactGizmo)
                return;
            SphereCollider sphere = sphereCollider != null ? sphereCollider : GetComponent<SphereCollider>();
            if (sphere == null)
                return;
            Vector3 scale = sphere.transform.lossyScale;
            float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Gizmos.color = contactAmount > 0.01f ? Color.cyan : Color.gray;
            Gizmos.DrawWireSphere(sphere.bounds.center, radius);
        }
    }
}
