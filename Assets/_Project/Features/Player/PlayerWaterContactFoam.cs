using Assets._Project.Features.Movement;
using Assets._Project.Features.WaterEffects;
using UnityEngine;

/// <summary>
/// Emits the player's continuous water-contact trail in BOTH grounded wading and swimming.
/// Like BoatFoamEmitter, it tracks multiple contact points around the actor's footprint and
/// emits from their previous to current waterline positions. It also handles high-fall
/// water-entry impacts. SwimmingMovement delegates its continuous trail to this component.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(WaterContactFoam))]
public sealed class PlayerWaterContactFoam : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController player;
    [SerializeField] private SpectralOceanFFT ocean;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private WaterContactFoam foam;

    [Header("Surface Contact")]
    [Tooltip("How frequently the animated ocean height is sampled at the player's capsule centre.")]
    [SerializeField, Min(0.03f)] private float sampleInterval = 0.08f;
    [Tooltip("Old surface samples are ignored after this many seconds.")]
    [SerializeField, Min(0.08f)] private float maxSampleAge = 0.75f;
    [Tooltip("A stalled async surface request is retried after this many seconds.")]
    [SerializeField, Min(0.1f)] private float sampleRequestTimeout = 0.4f;
    [Tooltip("Foam begins when at least this fraction of capsule height is submerged.")]
    [SerializeField, Range(0f, 0.3f)] private float contactEnterFraction = 0.002f;
    [Tooltip("Foam fades out once this much of the body is submerged, to avoid underwater foam.")]
    [SerializeField, Range(0.55f, 1f)] private float deepSubmersionStart = 0.78f;
    [SerializeField, Range(0.65f, 1f)] private float deepSubmersionEnd = 0.94f;

    [Header("Player Waterline Trail (wading + swimming)")]
    [SerializeField, Min(0.05f)] private float fullFoamSpeed = 2.6f;
    [SerializeField, Range(0f, 12f)] private float foamRate = 4f;
    [Tooltip("Player-only final strength multiplier. This scales every emitted foam stamp.")]
    [SerializeField, Range(0f, 1f)] private float playerFoamStrength = 0.22f;
    [SerializeField] private Vector2 foamRadius = new Vector2(0.22f, 0.36f);
    [SerializeField, Min(0f)] private float leadOffset = 0.06f;
    [SerializeField, Range(1f, 12f)] private float emitRateHz = 4f;
    [Tooltip("Seconds between quiet circular ripples while the player is touching the surface but nearly stationary.")]
    [SerializeField, Min(0.25f)] private float idleRippleInterval = 2.1f;
    [SerializeField, Min(0.25f)] private float idleRippleRadius = 2.4f;
    [SerializeField, Range(0f, 1f)] private float idleRippleStrength = 0.32f;

    [Header("Kelvin Wake")]
    [Tooltip("Distance-spaced source interval expressed as seconds at the current speed.")]
    [SerializeField, Range(0.35f, 1.2f)] private float wakeImpulseInterval = 0.65f;
    [Tooltip("Maximum distance behind the player over which the two wake arms diverge.")]
    [SerializeField, Min(1f)] private float wakeMaxLength = 8f;
    [SerializeField, Range(0.2f, 1.2f)] private float wakeImpulseRadius = 0.48f;
    [SerializeField, Range(0f, 0.5f)] private float wakeImpulseStrength = 0.24f;

    [Header("Fall Into Water")]
    [Tooltip("Minimum continuous downward time above the water before an entry creates a large impact ripple.")]
    [SerializeField, Min(0.05f)] private float minimumFallDuration = 0.2f;
    [Tooltip("Caps the fall duration used to scale the large secondary ripple.")]
    [SerializeField, Min(0.3f)] private float maximumImpactFallDuration = 2.2f;
    [Tooltip("Minimum downward speed required to distinguish a real dive from tiny downhill grounding gaps.")]
    [SerializeField, Min(0.1f)] private float minimumImpactDownSpeed = 2.5f;

    private float sampledSurfaceHeight;
    private float sampledAt = -1000f;
    private float nextSampleAt;
    private bool samplePending;
    private int sampleGeneration;
    private int sampleRequestId;
    private float sampleRequestStartedAt;
    private const int TrailPointCount = 8;
    private readonly Vector3[] previousTrailPoints = new Vector3[TrailPointCount];
    private readonly bool[] hasPreviousTrailPoint = new bool[TrailPointCount];
    private float emitAccumulator;
    private float nextIdleRippleTime;
    private float wakeDistanceAccumulator;
    private float wakeTrailLength;
    private float wakeIdleTime;
    private Vector3 lastWakeDirection;
    private float fallingDuration;
    private float peakDownwardSpeed;
    private bool fallSequenceArmed;
    private float previousSubmergedFraction = -1f;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        sampleGeneration++;
        sampleRequestId++;
        samplePending = false;
        nextIdleRippleTime = Time.time + 1.25f;
        RequestSample();
    }

    private void OnDisable()
    {
        sampleGeneration++;
        sampleRequestId++;
        samplePending = false;
        ResetTrail();
        fallingDuration = 0f;
        peakDownwardSpeed = 0f;
        fallSequenceArmed = false;
        previousSubmergedFraction = -1f;
        sampledAt = -1000f;
    }

    /// <summary>Called by PlayerController after its own auto-resolution has run.</summary>
    public void Initialize(PlayerController owner, SpectralOceanFFT water, CharacterController controller)
    {
        player = owner;
        ocean = water;
        characterController = controller;
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (player == null) player = GetComponent<PlayerController>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
        if (foam == null) foam = GetComponent<WaterContactFoam>();
        if (foam == null) foam = gameObject.AddComponent<WaterContactFoam>();
        if (ocean == null) ocean = Object.FindAnyObjectByType<SpectralOceanFFT>();
    }

    private void Update()
    {
        ResolveReferences();
        if (player == null || ocean == null || characterController == null ||
            !characterController.enabled || player.IsAboardBoat || foam == null || !foam.EmitFoam)
        {
            ResetTrail();
            previousSubmergedFraction = -1f;
            return;
        }

        if (samplePending && Time.realtimeSinceStartup - sampleRequestStartedAt > sampleRequestTimeout)
        {
            // Do not allow a missed GPU callback to permanently stop the player's trail.
            samplePending = false;
            sampleRequestId++;
        }

        if (Time.time >= nextSampleAt && !samplePending)
            RequestSample();

        // Track descent independently from the asynchronous surface sample. The player
        // can fall for several frames before a GPU water-height result is returned.
        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        float verticalVelocity = GetVerticalVelocity();
        bool groundedOnTerrain = player.GroundedMovement != null &&
                                 IsSameMovement(player.ActiveMovement, player.GroundedMovement) &&
                                 player.GroundedMovement.IsGrounded;
        bool descending = verticalVelocity < -0.2f && !groundedOnTerrain;
        if (descending)
        {
            fallingDuration = Mathf.Min(maximumImpactFallDuration, fallingDuration + dt);
            peakDownwardSpeed = Mathf.Max(peakDownwardSpeed, -verticalVelocity);
            fallSequenceArmed = true;
        }
        else if (groundedOnTerrain || verticalVelocity > 0.1f)
        {
            fallingDuration = 0f;
            peakDownwardSpeed = 0f;
            fallSequenceArmed = false;
        }

        if (sampledAt < 0f || Time.realtimeSinceStartup - sampledAt > maxSampleAge)
        {
            // Keep a functional contact trail on platforms where asynchronous GPU
            // readback is unavailable or temporarily stalls. This uses the ocean's
            // base plane as a fallback; animated FFT height is used whenever available.
            sampledSurfaceHeight = ocean.WaterLevelY;
            sampledAt = Time.realtimeSinceStartup;
            previousSubmergedFraction = -1f;
            ResetTrail();
        }

        Bounds bounds = characterController.bounds;
        float bodyHeight = Mathf.Max(0.1f, bounds.size.y);
        float submergedFraction = Mathf.Clamp01((sampledSurfaceHeight - bounds.min.y) / bodyHeight);

        bool hasPreviousWaterlineSample = previousSubmergedFraction >= 0f;
        bool enteredWater = hasPreviousWaterlineSample &&
                            previousSubmergedFraction <= 0.02f && submergedFraction > 0.02f;
        bool swimmingHopAlreadyEmittedImpact = player.SwimmingMovement != null &&
                                               IsSameMovement(player.ActiveMovement, player.SwimmingMovement) &&
                                               player.SwimmingMovement.IsHopping;
        if (enteredWater && fallSequenceArmed && fallingDuration >= minimumFallDuration &&
            peakDownwardSpeed >= minimumImpactDownSpeed && !swimmingHopAlreadyEmittedImpact)
        {
            Vector3 impactPoint = bounds.center;
            impactPoint.y = sampledSurfaceHeight;
            float duration01 = Mathf.InverseLerp(minimumFallDuration, maximumImpactFallDuration, fallingDuration);
            float impactSpeed01 = Mathf.InverseLerp(minimumImpactDownSpeed, 12f, peakDownwardSpeed);
            float impact01 = Mathf.Max(duration01, impactSpeed01);
            foam.EmitBurst(impactPoint, Mathf.Lerp(0.65f, 1f, impact01), 1f);
            foam.EmitImpactRipplePair(impactPoint, fallingDuration, Mathf.Lerp(0.7f, 1f, impact01));
            fallingDuration = 0f;
            peakDownwardSpeed = 0f;
            fallSequenceArmed = false;
            ResetTrail();
        }

        previousSubmergedFraction = submergedFraction;
        if (submergedFraction > 0.35f && !enteredWater && fallingDuration < minimumFallDuration)
        {
            // If a coarse async sample skipped the first few centimetres of entry,
            // do not carry a short, unrelated drop into later contacts.
            fallingDuration = 0f;
            peakDownwardSpeed = 0f;
            fallSequenceArmed = false;
        }

        bool isSwimming = player.SwimmingMovement != null &&
                          IsSameMovement(player.ActiveMovement, player.SwimmingMovement);
        if (isSwimming && !player.SwimmingMovement.EmitFoam)
        {
            ResetTrail();
            return;
        }

        // A small amount of water on the lower capsule is enough to create the same
        // continuous hull-style rim as the boat. The narrower entry ramp prevents a
        // shallow shoreline contact from being almost invisible, while the deep fade
        // still removes foam once the player is fully underwater.
        float contactRamp = Smooth01Between(contactEnterFraction, 0.055f, submergedFraction);
        float deepSubmersionFade = 1f - Smooth01Between(deepSubmersionStart, deepSubmersionEnd, submergedFraction);
        float contact = contactRamp * deepSubmersionFade;
        if (contact <= 0.001f)
        {
            ResetTrail();
            return;
        }

        float effectiveFullSpeed = fullFoamSpeed;
        float effectiveRate = foamRate;
        float effectiveEmitRate = emitRateHz;
        float effectiveRadiusMin = foamRadius.x;
        float effectiveRadiusMax = foamRadius.y;
        float effectiveLeadOffset = leadOffset;
        if (isSwimming)
        {
            // SwimmingMovement exposes its own authoring controls. Normalize them
            // against the prefab's original defaults so changing any swim slider
            // directly changes this shared player trail too.
            SwimmingMovement swim = player.SwimmingMovement;
            effectiveFullSpeed = Mathf.Max(0.1f, swim.FoamFullSpeed);
            effectiveRate *= Mathf.Clamp(swim.FoamRate / 22f, 0f, 1.5f);
            effectiveEmitRate *= Mathf.Clamp(swim.FoamEmitHz / 18f, 0f, 1.5f);
            effectiveRadiusMin *= Mathf.Clamp(swim.FoamRadius.x / 0.26f, 0.25f, 2f);
            effectiveRadiusMax *= Mathf.Clamp(swim.FoamRadius.y / 0.62f, 0.25f, 2f);
            effectiveLeadOffset *= Mathf.Clamp(swim.FoamLeadOffset / 0.35f, 0f, 2f);
        }

        IPlanarVelocityMovement planar = player.ActiveMovement as IPlanarVelocityMovement;
        Vector3 velocity = planar != null
            ? Vector3.ProjectOnPlane(planar.PlanarVelocity, Vector3.up)
            : Vector3.zero;
        if (velocity.sqrMagnitude < 0.0025f)
            velocity = Vector3.ProjectOnPlane(characterController.velocity, Vector3.up);
        float speed = velocity.magnitude;
        float speed01 = Mathf.Clamp01(speed / Mathf.Max(0.05f, effectiveFullSpeed));
        // Keep a visible low-speed waterline rim, then build toward the same strength
        // used by BoatFoamEmitter as the player accelerates.
        float speedWeight = Mathf.Lerp(0.4f, 1f, Smooth01Between(0.03f, 0.82f, speed01));
        float intensity = contact * speedWeight * playerFoamStrength;
        Vector3 direction = speed > 0.05f ? velocity / speed : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.001f)
            direction = Vector3.forward;

        // Match the boat's multi-point hull trail with an eight-point elliptical
        // perimeter around the player's capsule. Each point retains its own previous
        // world position so the emitted marks follow both the actor and its turning arc.
        Vector3 centre = bounds.center;
        centre.y = sampledSurfaceHeight;
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        if (right.sqrMagnitude < 0.001f)
            right = transform.right;
        float scaleX = Mathf.Abs(characterController.transform.lossyScale.x);
        float scaleZ = Mathf.Abs(characterController.transform.lossyScale.z);
        float radiusX = Mathf.Max(0.18f, characterController.radius * scaleX * 0.82f);
        float radiusZ = Mathf.Max(0.18f, characterController.radius * scaleZ * 0.82f);
        Vector3 lead = direction * effectiveLeadOffset;

        if (effectiveRate > 0f && effectiveEmitRate > 0f && playerFoamStrength > 0f)
        {
            emitAccumulator += dt;
            float interval = 1f / Mathf.Clamp(effectiveEmitRate, 1f, 12f);
            while (emitAccumulator >= interval)
            {
                float step = Mathf.Min(0.1f, interval);
                emitAccumulator -= interval;
                for (int i = 0; i < TrailPointCount; i++)
                {
                    float angle = (i / (float)TrailPointCount) * Mathf.PI * 2f;
                    Vector3 offset = right * (Mathf.Cos(angle) * radiusX)
                                   + direction * (Mathf.Sin(angle) * radiusZ);
                    Vector3 current = centre + lead + offset;
                    current.y = sampledSurfaceHeight;
                    Vector3 previous = hasPreviousTrailPoint[i] ? previousTrailPoints[i] : current;
                    // Use the same WaterContactFoam -> WaterFoamField path as the boat.
                    // Independent perimeter points leave an unbroken trail when the player
                    // turns, walks through shallows, or changes between grounded/swim motors.
                    foam.EmitTrail(previous, current, intensity, step, effectiveRadiusMin, effectiveRadiusMax, effectiveRate);
                    previousTrailPoints[i] = current;
                    hasPreviousTrailPoint[i] = true;
                }
            }
        }
        else
        {
            emitAccumulator = 0f;
            for (int i = 0; i < TrailPointCount; i++)
                hasPreviousTrailPoint[i] = false;
        }

        // The previous swimmer idle ripples were removed when the wake-heightfield
        // replaced the analytic ring path. Keep a small independent analytic ripple
        // cadence for a player who is floating in place.
        if (speed < 0.4f)
        {
            if (Time.time >= nextIdleRippleTime)
            {
                nextIdleRippleTime = Time.time + idleRippleInterval;
                WaterFoamField.EmitRipple(
                    centre,
                    0.08f,
                    idleRippleRadius,
                    idleRippleStrength * contact,
                    0f,
                    2.8f,
                    Vector2.zero,
                    0f,
                    1.45f);
            }
        }
        else
        {
            nextIdleRippleTime = Time.time + idleRippleInterval;
        }

        UpdatePlayerWake(dt, centre, direction, speed, speed01, contact);
    }

    private static float Smooth01Between(float edge0, float edge1, float value)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, value));
    }

    private void UpdatePlayerWake(float dt, Vector3 centre, Vector3 direction,
        float speed, float speed01, float contact)
    {
        if (speed < 0.4f || contact < 0.05f)
        {
            wakeDistanceAccumulator = 0f;
            wakeIdleTime += dt;
            if (wakeIdleTime > 0.65f)
            {
                wakeTrailLength = 0f;
                lastWakeDirection = Vector3.zero;
            }
            return;
        }

        if (lastWakeDirection.sqrMagnitude > 0.01f && Vector3.Dot(lastWakeDirection, direction) < 0.82f)
        {
            // A sharp turn starts a new wake fan instead of dragging the old V around
            // the corner and leaving a diagonal sheet across the water.
            wakeTrailLength = 0f;
            wakeDistanceAccumulator = 0f;
        }
        lastWakeDirection = direction;
        wakeIdleTime = 0f;

        float travelled = speed * dt;
        wakeDistanceAccumulator += travelled;
        wakeTrailLength = Mathf.Min(wakeMaxLength, wakeTrailLength + travelled);
        // Time spacing is fixed, therefore source distance grows with speed. Every
        // source remains at the position the actor actually crossed instead of being
        // re-offset into a hand-drawn V. Expanding circular wavefronts overlap into the
        // transverse crests and diverging Kelvin envelope.
        float spacing = Mathf.Clamp(speed * wakeImpulseInterval, 1.6f, 3.8f);
        float radius = wakeImpulseRadius * Mathf.Lerp(0.9f, 1.2f, speed01);
        float wakeBuild = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(wakeTrailLength / Mathf.Max(1f, wakeMaxLength)));
        float strength = wakeImpulseStrength * Mathf.Lerp(0.35f, 1f, speed01) * contact * wakeBuild;
        Vector3 stern = centre - direction * (radiusZForWake + 0.15f);
        if (wakeDistanceAccumulator >= spacing)
        {
            float overflow = wakeDistanceAccumulator % spacing;
            wakeDistanceAccumulator = overflow;
            Vector3 sourcePoint = stern - direction * overflow;
            WaterWakeField.EmitKelvinWake(sourcePoint, direction, radius, strength, wakeMaxLength, 19.47f, speed);
        }
    }

    private float radiusZForWake => characterController != null
        ? Mathf.Max(0.18f, characterController.radius * Mathf.Abs(characterController.transform.lossyScale.z) * 0.82f)
        : 0.25f;

    private float radiusXForWake => characterController != null
        ? Mathf.Max(0.18f, characterController.radius * Mathf.Abs(characterController.transform.lossyScale.x) * 0.82f)
        : 0.25f;

    private void ResetTrail()
    {
        emitAccumulator = 0f;
        wakeDistanceAccumulator = 0f;
        wakeTrailLength = 0f;
        wakeIdleTime = 0f;
        lastWakeDirection = Vector3.zero;
        for (int i = 0; i < TrailPointCount; i++)
            hasPreviousTrailPoint[i] = false;
    }

    private float GetVerticalVelocity()
    {
        if (player != null && player.GroundedMovement != null &&
            IsSameMovement(player.ActiveMovement, player.GroundedMovement))
            return player.GroundedMovement.VerticalVelocity;
        if (player != null && player.SwimmingMovement != null &&
            IsSameMovement(player.ActiveMovement, player.SwimmingMovement))
            return player.SwimmingMovement.VerticalVelocity;
        return 0f;
    }

    private static bool IsSameMovement(IMovement current, UnityEngine.Object expected)
    {
        // Cast through UnityEngine.Object so Unity's destroyed-object-aware equality
        // operator is used instead of an interface/reference comparison.
        return (current as UnityEngine.Object) == expected;
    }

    private void RequestSample()
    {
        if (ocean == null || characterController == null)
            return;

        nextSampleAt = Time.time + Mathf.Max(0.03f, sampleInterval);
        if (!ocean.CanSampleSurface)
        {
            sampledSurfaceHeight = ocean.WaterLevelY;
            sampledAt = Time.realtimeSinceStartup;
            samplePending = false;
            return;
        }

        samplePending = true;
        sampleRequestStartedAt = Time.realtimeSinceStartup;
        int requestGeneration = sampleGeneration;
        int requestId = ++sampleRequestId;
        Vector3 samplePosition = characterController.bounds.center;
        bool queued = ocean.RequestSurfaceSample(samplePosition, sample =>
            OnSurfaceSampleReady(sample, requestGeneration, requestId));
        if (!queued)
        {
            samplePending = false;
            sampledSurfaceHeight = ocean.WaterLevelY;
            sampledAt = Time.realtimeSinceStartup;
        }
    }

    private void OnSurfaceSampleReady(SpectralOceanFFT.SurfaceSample sample,
        int requestGeneration, int requestId)
    {
        // Ignore stale results from a timed-out request or a disabled/re-enabled component.
        if (requestGeneration != sampleGeneration || requestId != sampleRequestId)
            return;

        samplePending = false;
        sampledSurfaceHeight = sample.IsValid ? sample.Height : ocean != null ? ocean.WaterLevelY : 0f;
        sampledAt = Time.realtimeSinceStartup;
    }

    private void OnValidate()
    {
        sampleInterval = Mathf.Max(0.03f, sampleInterval);
        maxSampleAge = Mathf.Max(0.08f, maxSampleAge);
        sampleRequestTimeout = Mathf.Max(0.1f, sampleRequestTimeout);
        deepSubmersionEnd = Mathf.Max(deepSubmersionStart + 0.02f, deepSubmersionEnd);
        fullFoamSpeed = Mathf.Max(0.05f, fullFoamSpeed);
        wakeImpulseInterval = Mathf.Clamp(wakeImpulseInterval, 0.35f, 1.2f);
        wakeMaxLength = Mathf.Max(1f, wakeMaxLength);
        wakeImpulseRadius = Mathf.Max(0.1f, wakeImpulseRadius);
        wakeImpulseStrength = Mathf.Clamp(wakeImpulseStrength, 0f, 0.5f);
        foamRate = Mathf.Clamp(foamRate, 0f, 12f);
        foamRadius.x = Mathf.Max(0.05f, foamRadius.x);
        foamRadius.y = Mathf.Max(foamRadius.x, foamRadius.y);
        emitRateHz = Mathf.Clamp(emitRateHz, 1f, 12f);
        playerFoamStrength = Mathf.Clamp01(playerFoamStrength);
        idleRippleInterval = Mathf.Max(0.25f, idleRippleInterval);
        idleRippleRadius = Mathf.Max(0.25f, idleRippleRadius);
        maximumImpactFallDuration = Mathf.Max(minimumFallDuration, maximumImpactFallDuration);
        minimumImpactDownSpeed = Mathf.Max(0.1f, minimumImpactDownSpeed);
    }
}
