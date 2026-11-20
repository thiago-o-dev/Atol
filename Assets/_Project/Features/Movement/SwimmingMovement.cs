using Assets._Project.Features.Movement;
using Assets._Project.Features.StatusViewModels;
using Assets._Project.Features.WaterEffects;
using UnityEngine;

/// <summary>
/// Reusable actor-agnostic swimming motor. Player and NPC controllers can drive it
/// through IMovement while AI can additionally control buoyancy/drowning through
/// ISwimmingBuoyancyControl. Supports CharacterController and Rigidbody actors.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(StaminaComponent))]
public sealed class SwimmingMovement : MonoBehaviour, IMovement, IPlanarVelocityMovement, ISwimmingBuoyancyControl
{
    [Header("Actor")]
    [Tooltip("Optional root moved by this motor. Defaults to the CharacterController, Rigidbody, or this transform.")]
    public Transform MovementRoot;
    [Tooltip("Preferred for player characters. Leave empty for Rigidbody-driven NPCs.")]
    public CharacterController Controller;
    [Tooltip("Optional alternative for NPCs that use a Rigidbody instead of a CharacterController.")]
    public Rigidbody Body;
    [Tooltip("Optional collider used to measure body height and center for the buoyancy target.")]
    public Collider BodyCollider;

    [Header("Input Reference")]
    [Tooltip("Camera for player swimming. Leave empty for NPCs to move relative to their own facing direction.")]
    public Transform ReferenceTransform;
    [Min(0f)] public float Speed = 3.8f;
    [Min(0f)] public float Acceleration = 8f;
    [Min(0f)] public float Deceleration = 5f;
    [Min(0.01f)] public float TurnSmoothTime = 0.12f;

    [Header("Surface Buoyancy")]
    public SpectralOceanFFT Ocean;
    [Tooltip("Fraction of the actor's measured body height below the waterline. 0.55 keeps the actor near the surface with the upper body above water.")]
    [Range(0.1f, 0.9f)] public float TargetSubmersion = 0.55f;
    [Tooltip("Fallback body height when no CharacterController or collider is available.")]
    [Min(0.1f)] public float AssumedBodyHeight = 1.8f;
    [Tooltip("Fallback offset from actor root to body center.")]
    [Min(0f)] public float AssumedBodyCenterOffset = 0.9f;
    [Tooltip("Strength of the spring pulling the body toward its surface-floating height.")]
    [Min(0f)] public float SurfaceSpring = 6.5f;
    [Tooltip("Damps vertical motion without making the actor feel glued to the water.")]
    [Min(0f)] public float VerticalDamping = 8f;
    [Tooltip("Scales the measured vertical speed of the water. The swimmer follows both rising and falling waves.")]
    [Min(0f)] public float WaveLiftMultiplier = 0.3f;
    [Tooltip("How strongly the swimmer matches the water surface vertical velocity in both directions. 0 ignores wave motion; 1 fully follows it before WaveLiftMultiplier is applied.")]
    [Range(0f, 2f)] public float WaveVelocityFollow = 0.2f;
    [Tooltip("Limits noisy surface-height changes when estimating wave vertical velocity.")]
    [Min(0.1f)] public float MaximumWaveVerticalSpeed = 1.0f;
    [Tooltip("Extra upward acceleration available to wave buoyancy input and jump/swim-up input.")]
    [Min(0f)] public float ExtraBuoyancyAcceleration = 8f;
    [Tooltip("Downward acceleration available to drowning/submerge input.")]
    [Min(0f)] public float DrowningPullAcceleration = 16f;
    [Tooltip("Remaining surface-spring strength while fully drowned. Lower values let abilities pull actors deeper underwater.")]
    [Range(0f, 1f)] public float DrowningBuoyancyRetention = 0.18f;
    [Tooltip("Fraction of wave-follow force retained while drowning pull is active.")]
    [Range(0f, 1f)] public float DrowningWaveResponse = 0.3f;
    [Min(0f)] public float MaximumUpwardAcceleration = 8f;
    [Min(0f)] public float MaximumDownwardAcceleration = 8f;
    [Min(0.1f)] public float MaximumUpwardSpeed = 1.35f;
    [Min(0.1f)] public float MaximumDownwardSpeed = 1.35f;

    [Header("Wave Ejection Detection")]
    [Tooltip("The body centre must rise this far above the water surface before a wave ejection is recognized.")]
    [Min(0f)] public float WaveEjectionHeight = 0.2f;
    [Tooltip("Once wave-ejected, clear the state when the body centre returns this close to the water surface.")]
    [Min(0f)] public float WaveReentryMargin = 0.05f;
    [Tooltip("Brief confirmation time filters out one-frame surface-sampling noise.")]
    [Min(0f)] public float WaveEjectionConfirmationTime = 0.08f;

    [Header("Swimming Vertical Input")]
    [Tooltip("Acceleration applied while the jump/up input is held.")]
    [Min(0f)] public float VerticalSwimAcceleration = 5f;

    [Header("Wave Drift (side-to-side wave motion)")]
    [Tooltip("The swimmer is carried by the horizontal (orbital) motion of the waves while not fully submerged. It is zero-mean: after a full wave cycle the swimmer is back where it started.")]
    public bool EnableWaveDrift = true;
    [Tooltip("Fraction of the water particle's horizontal displacement the swimmer follows.")]
    [Range(0f, 1.5f)] public float WaveDriftFollow = 0.8f;
    [Tooltip("Response frequency (rad/s) of the drift filter. Waves are 0.4-1.6 rad/s.")]
    [Range(0.5f, 8f)] public float WaveDriftResponse = 3.2f;
    [Range(0.3f, 2f)] public float WaveDriftDamping = 0.9f;
    [Tooltip("Largest horizontal excursion from waves, metres.")]
    [Min(0.1f)] public float WaveDriftMaxOffset = 3f;
    [Tooltip("Fraction of body height under the surface at which wave drift starts fading (full drift below it).")]
    [Range(0f, 1f)] public float DriftFadeStart = 0.7f;
    [Tooltip("Fraction under the surface at which drift is zero (fully submerged, e.g. pulled down by a shark).")]
    [Range(0f, 1f)] public float DriftFadeEnd = 1f;

    [Header("Foam")]
    [Tooltip("Leave soft foam on the water surface while swimming and on hop splashes.")]
    public bool EmitFoam = true;
    [Tooltip("Planar speed (m/s) at which swim foam is at full intensity.")]
    [Min(0.1f)] public float FoamFullSpeed = 2.8f;
    [Tooltip("Swim foam stamp radius (m) at low / full speed. Small: a swimmer pushes little water.")]
    public Vector2 FoamRadius = new Vector2(0.26f, 0.62f);
    [Tooltip("Foam added per second at full speed.")]
    [Min(0f)] public float FoamRate = 22f;
    [Tooltip("Foam stamps per second (fixed rate, independent of physics steps).")]
    [Range(5f, 60f)] public float FoamEmitHz = 18f;
    [Tooltip("Metres ahead of the body where the chest pushes the water; foam is stamped there.")]
    [Min(0f)] public float FoamLeadOffset = 0.35f;
    [Tooltip("When driven by PlayerController, continuous player foam is emitted by PlayerWaterContactFoam so shallow-water walking and swimming share one reliable trail owner. NPC swimmers leave this disabled.")]
    [HideInInspector] public bool UseExternalPlayerFoam;
    [Tooltip("Seconds between visible circular surface ripples for NPC swimmers. Player uses PlayerWaterContactFoam instead.")]
    [Min(0.25f)] public float RippleInterval = 2.1f;

    [Header("Jump Hop (out of the water)")]
    [Tooltip("Jump becomes a small hop out of the water that costs stamina. Off = legacy hold-to-swim-up.")]
    public bool EnableJumpHop = true;
    public StaminaComponent Stamina;
    [Min(0f)] public float JumpStaminaCost = 25f;
    [Tooltip("Wind-up between pressing jump and leaving the water (the swimmer kicks down first).")]
    [Min(0f)] public float JumpDelay = 0.18f;
    [Tooltip("Seconds after a hop before another can start.")]
    [Min(0f)] public float JumpCooldown = 1.2f;
    [Tooltip("Upward speed at take-off, m/s. 3.2 m/s is roughly a 0.5 m hop.")]
    [Min(0.1f)] public float JumpVelocity = 3.4f;
    [Tooltip("Extra planar speed added in the travel direction at take-off.")]
    [Min(0f)] public float JumpSpeedBoost = 1.6f;
    [Tooltip("Fraction of horizontal control available in the air (keeps the speed boost).")]
    [Range(0f, 1f)] public float JumpAirControl = 0.25f;
    [Min(0.1f)] public float JumpGravity = 9.81f;
    [Tooltip("How far the body sinks during the wind-up, metres.")]
    [Min(0f)] public float JumpWindupDip = 0.2f;
    [Tooltip("A jump is only allowed while the body centre is within this height error of its floating height (not when pulled down by a shark).")]
    [Min(0.05f)] public float JumpMaxDepthError = 0.45f;
    [Tooltip("Seconds after landing during which the player is not switched back to grounded movement.")]
    [Min(0f)] public float JumpLandingGrace = 0.5f;

    [Header("Surface Sampling")]
    [Tooltip("How often the swimmer requests the exact water height beneath its body centre.")]
    [Min(0.02f)] public float SurfaceSampleInterval = 0.05f;
    [Min(0.05f)] public float MaximumSurfaceSampleAge = 0.35f;
    [Range(0f, 1f)] public float WaveVelocitySmoothing = 0.3f;
    [Tooltip("Reject inverse-mapped samples whose horizontal solver residual exceeds this distance.")]
    [Min(0.01f)] public float MaximumSurfaceQueryError = 1.0f;
    [Tooltip("Discard delayed FFT samples when the player has moved this far from the exact queried world position.")]
    [Min(0.05f)] public float MaximumSurfaceQueryDrift = 0.35f;

    [Header("Wave Riding (surface tracker)")]
    [Tooltip("The swimmer rides the water in the water's own moving frame: a time-stamped tracker interpolates/extrapolates the async FFT samples, and the buoyancy spring acts on motion RELATIVE to the surface. Off = legacy low-pass follow (cannot keep up with real waves).")]
    public bool RideWaveSurface = true;
    [Tooltip("Fraction of the tracked surface vertical velocity the swimmer matches. 1 = rides the wave exactly.")]
    [Range(0f, 1.5f)] public float RideVelocityFollow = 1f;
    [Tooltip("Spring (1/s^2) pulling the body toward its floating height relative to the moving surface.")]
    [Min(0f)] public float RideStiffness = 22f;
    [Tooltip("Damping (1/s) of motion relative to the moving surface. About 2*sqrt(RideStiffness) is critically damped.")]
    [Min(0f)] public float RideDamping = 9.4f;
    [Tooltip("Time constant (s) for smoothing the surface velocity estimated from consecutive samples.")]
    [Range(0.02f, 0.6f)] public float SurfaceVelocitySmoothTime = 0.12f;
    [Tooltip("Upper bound (m/s) for the estimated vertical speed of the water surface; rejects outlier samples.")]
    [Min(1f)] public float MaximumTrackedSurfaceSpeed = 14f;
    [Tooltip("When a sample arrives, how much of the error between the tracker and the (latency-compensated) sample is corrected at once.")]
    [Range(0.1f, 1f)] public float SurfaceTrackerCorrection = 0.65f;
    [Tooltip("Tangent-plane slope limit used to move a delayed sample to the swimmer's current XZ.")]
    [Range(0.5f, 8f)] public float SurfaceTrackSlopeLimit = 2f;
    [Tooltip("Largest tangent-plane height correction (m) applied to a delayed sample.")]
    [Range(0.05f, 3f)] public float SurfaceTrackCorrectionLimit = 0.6f;
    [Tooltip("If the tracker and a fresh sample disagree by more than this (m), the tracker snaps to the sample.")]
    [Min(0.2f)] public float SurfaceTrackerSnapDistance = 2f;
    [Tooltip("Seconds over which the surface target eases back to mean sea level after samples have been lost for longer than the hold time.")]
    [Min(0.5f)] public float SurfaceLostReturnTime = 4f;
    [Tooltip("Show a small on-screen readout of surface sampling health (accepted/rejected samples, age, tracked height/velocity).")]
    public bool ShowSurfaceDebug;

    [Header("Stable Surface Following")]
    [Tooltip("Smooths the local water height before it becomes the swimmer's buoyancy target.")]
    [Range(0.04f, 0.5f)] public float SurfaceHeightSmoothTime = 0.16f;
    [Tooltip("Maximum rate (m/s) at which the swimmer's surface target may move. Prevents a single noisy sample from launching the character.")]
    [Min(0.1f)] public float MaximumSurfaceFollowSpeed = 2.0f;
    [Tooltip("Hold the last valid wave sample briefly if a GPU readback is delayed; then blend gently toward mean sea level.")]
    [Min(0.1f)] public float SurfaceSampleHoldTime = 0.9f;
    // Legacy serialized values retained for prefab compatibility. Swimmer height
    // now comes from one exact body-centre query; surrounding samples are not averaged.
    [HideInInspector, Range(0.2f, 1.2f)] public float SurfaceProbeSpread = 0.7f;
    [HideInInspector, Min(0.05f)] public float MaximumProbeHeightDifference = 0.28f;

    private Vector2 inputDirection;
    private float verticalInput;
    private bool jumpHeld;
    private bool diveHeld;
    private float buoyancyInput;
    private float verticalVelocity;
    private float turnSmoothVelocity;
    private Vector3 horizontalVelocity;

    private float sampledSurfaceHeight;
    private float sampledSurfaceTime = -1000f;
    private float smoothedSurfaceHeight;
    private float surfaceHeightSmoothVelocity;
    private float filteredSurfaceVerticalVelocity;
    private bool hasSmoothedSurfaceHeight;
    private float nextSampleRequestTime;
    private bool hasSurfaceSample;
    private bool sampleRequestPending;
    private int sampleGeneration;

    // Surface tracker state (RideWaveSurface).
    private float trackedHeight;
    private float trackedVelocity;
    private bool hasTracker;
    private bool hasPrevMeasurement;
    private Vector2 trackedSlope;          // d(height)/d(x,z) from the newest sample normal
    private Vector2 lastTrackXZ;
    private bool hasTrackXZ;
    private SpectralOceanFFT.SurfaceSample prevMeasurement;
    private float lastIngestRealtime = -1000f;
    private int acceptedSampleCount;
    private int rejectedInvalidCount;
    private int rejectedErrorCount;
    private int rejectedDriftCount;
    private int rejectedAgeCount;

    /// <summary>Tracked (latency-compensated, dead-reckoned) water height under the swimmer.</summary>
    public float TrackedSurfaceHeight => trackedHeight;
    /// <summary>Tracked vertical speed of the water surface under the swimmer, m/s.</summary>
    public float TrackedSurfaceVelocity => trackedVelocity;
    /// <summary>Seconds since the newest accepted surface sample was simulated.</summary>
    public float SurfaceSampleAge => hasSurfaceSample ? Time.realtimeSinceStartup - sampledSurfaceTime : float.PositiveInfinity;

    private Vector2 sampledLattice;
    private Vector3 sampledSurfaceQueryPosition;
    private float sampledSurfaceQueryError = float.PositiveInfinity;
    private Vector2 driftOffset;
    private Vector2 driftVelocity;
    private float driftExposure = 1f;
    private float jumpWindupTimer = -1f;
    private float nextJumpTime;
    private bool hopAirborne;
    private float hopDescendingDuration;
    private bool waveAirborne;
    private float waveEjectionTimer;
    private float hopLandedTime = -1000f;
    private WaterContactFoam foam;
    private Vector3 lastFoamPoint;
    private bool hasFoamPoint;
    private float rippleTimer;
    private float foamAccumulator;
    private float wakeDistanceAccumulator;

    private bool bodyGravityWasEnabled;
    private bool bodyGravityCached;

    private Transform ActorTransform
    {
        get
        {
            if (MovementRoot != null) return MovementRoot;
            if (Controller != null) return Controller.transform;
            if (Body != null) return Body.transform;
            return transform;
        }
    }

    public Vector3 PlanarVelocity => Vector3.ProjectOnPlane(horizontalVelocity, Vector3.up);
    public float VerticalVelocity => verticalVelocity;
    public float CurrentBuoyancyInput => buoyancyInput;
    public float LastSurfaceQueryErrorMeters => sampledSurfaceQueryError;
    public Vector3 LastSurfaceQueryPosition => sampledSurfaceQueryPosition;
    /// <summary>True during the jump wind-up, the hop itself and a short landing grace.</summary>
    public bool IsHopping => jumpWindupTimer >= 0f || hopAirborne ||
                             Time.time - hopLandedTime < JumpLandingGrace;
    /// <summary>True only while the jump wind-up or airborne hop is active; excludes post-landing grace.</summary>
    public bool IsHopInProgress => jumpWindupTimer >= 0f || hopAirborne;
    /// <summary>True while performing the deliberate swimming hop.</summary>
    public bool IsAirborne => hopAirborne;
    /// <summary>True when a strong wave has lifted the body clear of the waterline.</summary>
    public bool IsWaveAirborne => waveAirborne && !hopAirborne;
    /// <summary>0 = fully submerged (no wave push), 1 = fully exposed.</summary>
    public float WaveDriftExposure => driftExposure;
    public Vector3 WaveDriftOffset => new Vector3(driftOffset.x, 0f, driftOffset.y);
    /// <summary>Raised when the hop starts leaving the water.</summary>
    public event System.Action HopStarted;
    /// <summary>Raised when the swimmer re-enters the water after a hop. Argument = impact speed.</summary>
    public event System.Action<float> HopLanded;
    /// <summary>Raised when a jump is refused (cooldown, stamina, too deep). Argument = reason.</summary>
    public event System.Action<string> HopRefused;

    private void Awake()
    {
        ResolveReferences();
        CacheRigidbodyGravity();
    }

    private void OnEnable()
    {
        sampleGeneration++;
        sampleRequestPending = false;
        if (!hasSurfaceSample)
            hasSmoothedSurfaceHeight = false;
        SetRigidbodyGravityControlled(true);
    }

    private void OnDisable()
    {
        sampleGeneration++;
        sampleRequestPending = false;
        inputDirection = Vector2.zero;
        verticalInput = 0f;
        jumpHeld = false;
        diveHeld = false;
        buoyancyInput = 0f;
        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
        driftOffset = Vector2.zero;
        driftVelocity = Vector2.zero;
        jumpWindupTimer = -1f;
        hopAirborne = false;
        hopDescendingDuration = 0f;
        waveAirborne = false;
        waveEjectionTimer = 0f;
        hasSmoothedSurfaceHeight = false;
        hasSurfaceSample = false;
        hasTracker = false;
        hasPrevMeasurement = false;
        hasTrackXZ = false;
        trackedVelocity = 0f;
        lastIngestRealtime = -1000f;
        sampledSurfaceTime = -1000f;
        sampledLattice = Vector2.zero;
        sampledSurfaceQueryPosition = Vector3.zero;
        sampledSurfaceQueryError = float.PositiveInfinity;
        surfaceHeightSmoothVelocity = 0f;
        filteredSurfaceVerticalVelocity = 0f;
        SetRigidbodyGravityControlled(false);
    }

    private void OnValidate()
    {
        Speed = Mathf.Max(0f, Speed);
        Acceleration = Mathf.Max(0f, Acceleration);
        Deceleration = Mathf.Max(0f, Deceleration);
        TurnSmoothTime = Mathf.Max(0.01f, TurnSmoothTime);
        TargetSubmersion = Mathf.Clamp(TargetSubmersion, 0.1f, 0.9f);
        WaveVelocityFollow = Mathf.Clamp(WaveVelocityFollow, 0f, 2f);
        WaveVelocitySmoothing = Mathf.Clamp01(WaveVelocitySmoothing);
        DrowningBuoyancyRetention = Mathf.Clamp01(DrowningBuoyancyRetention);
        DrowningWaveResponse = Mathf.Clamp01(DrowningWaveResponse);
        AssumedBodyHeight = Mathf.Max(0.1f, AssumedBodyHeight);
        AssumedBodyCenterOffset = Mathf.Max(0f, AssumedBodyCenterOffset);
        SurfaceSpring = Mathf.Max(0f, SurfaceSpring);
        VerticalDamping = Mathf.Max(0f, VerticalDamping);
        WaveLiftMultiplier = Mathf.Max(0f, WaveLiftMultiplier);
        MaximumWaveVerticalSpeed = Mathf.Max(0.1f, MaximumWaveVerticalSpeed);
        ExtraBuoyancyAcceleration = Mathf.Max(0f, ExtraBuoyancyAcceleration);
        DrowningPullAcceleration = Mathf.Max(0f, DrowningPullAcceleration);
        MaximumUpwardAcceleration = Mathf.Max(0f, MaximumUpwardAcceleration);
        MaximumDownwardAcceleration = Mathf.Max(0f, MaximumDownwardAcceleration);
        MaximumUpwardSpeed = Mathf.Max(0.1f, MaximumUpwardSpeed);
        MaximumDownwardSpeed = Mathf.Max(0.1f, MaximumDownwardSpeed);
        VerticalSwimAcceleration = Mathf.Max(0f, VerticalSwimAcceleration);
        SurfaceSampleInterval = Mathf.Max(0.02f, SurfaceSampleInterval);
        MaximumSurfaceSampleAge = Mathf.Max(0.05f, MaximumSurfaceSampleAge);
        MaximumSurfaceQueryError = Mathf.Max(0.01f, MaximumSurfaceQueryError);
        MaximumSurfaceQueryDrift = Mathf.Max(0.05f, MaximumSurfaceQueryDrift);
        RideStiffness = Mathf.Max(0f, RideStiffness);
        RideDamping = Mathf.Max(0f, RideDamping);
        SurfaceVelocitySmoothTime = Mathf.Clamp(SurfaceVelocitySmoothTime, 0.02f, 0.6f);
        MaximumTrackedSurfaceSpeed = Mathf.Max(1f, MaximumTrackedSurfaceSpeed);
        SurfaceTrackerCorrection = Mathf.Clamp(SurfaceTrackerCorrection, 0.1f, 1f);
        SurfaceTrackerSnapDistance = Mathf.Max(0.2f, SurfaceTrackerSnapDistance);
        SurfaceLostReturnTime = Mathf.Max(0.5f, SurfaceLostReturnTime);
        SurfaceHeightSmoothTime = Mathf.Clamp(SurfaceHeightSmoothTime, 0.04f, 0.5f);
        MaximumSurfaceFollowSpeed = Mathf.Max(0.1f, MaximumSurfaceFollowSpeed);
        SurfaceSampleHoldTime = Mathf.Max(MaximumSurfaceSampleAge, SurfaceSampleHoldTime);
        SurfaceProbeSpread = Mathf.Clamp(SurfaceProbeSpread, 0.2f, 1.2f);
        MaximumProbeHeightDifference = Mathf.Max(0.05f, MaximumProbeHeightDifference);
        DriftFadeEnd = Mathf.Max(DriftFadeStart + 0.01f, DriftFadeEnd);
        JumpStaminaCost = Mathf.Max(0f, JumpStaminaCost);
        WaveEjectionHeight = Mathf.Max(0f, WaveEjectionHeight);
        WaveReentryMargin = Mathf.Max(0f, WaveReentryMargin);
        WaveEjectionConfirmationTime = Mathf.Max(0f, WaveEjectionConfirmationTime);
    }

    public void Move(Vector2 direction)
    {
        inputDirection = Vector2.ClampMagnitude(direction, 1f);
    }

    /// <summary>Jump/up swims upward while held (PlayerController-compatible).</summary>
    public void Jump(bool released)
    {
        if (EnableJumpHop)
        {
            if (!released)
                RequestJumpHop();
            return;
        }

        jumpHeld = !released;
        RefreshVerticalInput();
    }

    /// <summary>Set -1 to pull down, 0 for neutral, or +1 to swim upward.</summary>
    public void SetVerticalInput(float value)
    {
        jumpHeld = value > 0f;
        diveHeld = value < 0f;
        RefreshVerticalInput();
    }

    /// <summary>Optional downward control. Positive held input pulls the swimmer down.</summary>
    public void SetDiveInput(bool held)
    {
        diveHeld = held;
        RefreshVerticalInput();
    }

    /// <summary>Starts a hop if off cooldown, near the surface, not drowning and stamina allows.</summary>
    public bool RequestJumpHop()
    {
        ResolveReferences();
        if (!isActiveAndEnabled || jumpWindupTimer >= 0f || hopAirborne)
            return false;
        if (Time.time < nextJumpTime) { HopRefused?.Invoke("cooldown"); return false; }
        if (buoyancyInput < -0.01f) { HopRefused?.Invoke("drowning"); return false; }

        Transform actor = ActorTransform;
        float targetCenterY = GetCurrentSurfaceHeight(actor) + (0.5f - TargetSubmersion) * GetBodyHeight();
        if (GetBodyCenterY(actor) < targetCenterY - JumpMaxDepthError)
        {
            HopRefused?.Invoke("too deep");
            return false;
        }
        if (Stamina != null && !Stamina.TryConsume(JumpStaminaCost))
        {
            HopRefused?.Invoke("stamina");
            return false;
        }

        jumpWindupTimer = 0f;
        nextJumpTime = Time.time + JumpDelay + JumpCooldown;
        return true;
    }

    private void RefreshVerticalInput()
    {
        verticalInput = diveHeld ? -1f : jumpHeld ? 1f : 0f;
    }

    /// <summary>
    /// Sets buoyancy independently of swimming input: -1 actively pulls down,
    /// 0 uses natural buoyancy, and +1 adds lift. Designed for NPC/ability control.
    /// </summary>
    public void SetBuoyancyInput(float value)
    {
        buoyancyInput = Mathf.Clamp(value, -1f, 1f);
    }

    /// <summary>Convenience API for drowning/grab effects that pull an actor underwater.</summary>
    public void SetDrowning(bool drowning)
    {
        buoyancyInput = drowning ? -1f : 0f;
    }

    public void SetTargetSubmersion(float fraction)
    {
        TargetSubmersion = Mathf.Clamp(fraction, 0.1f, 0.9f);
    }

    public void ApplyMovement()
    {
        ResolveReferences();
        if (Controller != null && !Controller.enabled)
            return;
        if (Controller == null && Body == null && ActorTransform == null)
            return;

        RequestSurfaceSampleIfNeeded();

        float dt = Time.fixedDeltaTime;
        if (dt <= 0f)
            return;

        UpdateFilteredSurfaceTarget(dt);
        Transform actor = ActorTransform;
        Vector3 desiredDirection = GetCameraRelativeDirection(inputDirection, actor);
        Vector3 targetHorizontalVelocity = desiredDirection * Speed;
        float horizontalRate = desiredDirection.sqrMagnitude > 0.0001f ? Acceleration : Deceleration;
        if (hopAirborne)
            horizontalRate *= JumpAirControl;
        horizontalVelocity = Vector3.MoveTowards(
            Vector3.ProjectOnPlane(horizontalVelocity, Vector3.up),
            targetHorizontalVelocity,
            horizontalRate * dt);

        if (desiredDirection.sqrMagnitude > 0.0001f)
        {
            float targetAngle = Mathf.Atan2(desiredDirection.x, desiredDirection.z) * Mathf.Rad2Deg;
            float angle = Mathf.SmoothDampAngle(
                actor.eulerAngles.y, targetAngle, ref turnSmoothVelocity, TurnSmoothTime);
            actor.rotation = Quaternion.Euler(0f, angle, 0f);
        }

        float surfaceHeight = GetCurrentSurfaceHeight(actor);
        float bodyHeight = GetBodyHeight();
        float targetCenterY = surfaceHeight + (0.5f - TargetSubmersion) * bodyHeight;

        UpdateWaveAirborneState(surfaceHeight, actor, dt);

        if (jumpWindupTimer >= 0f)
        {
            jumpWindupTimer += dt;
            float progress = JumpDelay > 0.0001f ? Mathf.Clamp01(jumpWindupTimer / JumpDelay) : 1f;
            targetCenterY -= JumpWindupDip * Mathf.Sin(progress * Mathf.PI * 0.5f);
            if (progress >= 1f)
                LaunchHop(desiredDirection, actor);
        }

        float heightError = targetCenterY - GetBodyCenterY(actor);

        if (hopAirborne)
        {
            verticalVelocity = Mathf.Max(verticalVelocity - JumpGravity * dt, -MaximumDownwardSpeed * 3f);
            if (verticalVelocity <= 0f)
                hopDescendingDuration += dt;

            // A hop can finish by returning to the water's floating target OR by
            // landing on solid terrain above that target. Treat confirmed downward
            // ground contact as a valid landing so hopAirborne cannot remain stuck
            // true on a high shoreline.
            bool descending = verticalVelocity <= 0f;
            bool touchedSolidGround = descending && Controller != null &&
                                      Controller.enabled && Controller.isGrounded;
            bool returnedToWater = descending && heightError >= 0f;
            if (touchedSolidGround || returnedToWater)
            {
                hopAirborne = false;
                hopLandedTime = Time.time;
                float impact = Mathf.Max(0f, -verticalVelocity);
                float fallDuration = hopDescendingDuration;
                hopDescendingDuration = 0f;
                verticalVelocity = touchedSolidGround ? 0f : Mathf.Max(verticalVelocity, -MaximumDownwardSpeed);

                // Only spawn the splash when the surface intersects the capsule.
                // Landing on dry ground above the waterline is a land impact, not a
                // water-entry event.
                float bodyBottom = GetBodyCenterY(actor) - bodyHeight * 0.5f;
                bool waterTouchesBody = surfaceHeight >= bodyBottom;
                if (waterTouchesBody)
                    EmitHopSplash(Mathf.Clamp01(0.35f + impact / 8f), fallDuration, true);
                HopLanded?.Invoke(impact);
            }
        }

        if (!hopAirborne)
        {
        float drowningAmount = Mathf.Max(0f, -buoyancyInput);
        float springRetention = Mathf.Lerp(1f, DrowningBuoyancyRetention, drowningAmount);
        float springAcceleration = heightError * SurfaceSpring * springRetention;

        // Treat the ocean surface as a moving reference frame. Damping is applied
        // against the sampled wave's vertical velocity, not against world-zero.
        // This makes the swimmer travel DOWN with a receding trough just as readily
        // as it rises with an approaching crest, preventing the body being left
        // suspended above a falling wave.
        float waveResponse = Mathf.Lerp(1f, DrowningWaveResponse, drowningAmount);
        float freshWaveVelocity = GetCurrentSurfaceVerticalVelocity();
        float swimAcceleration = verticalInput * VerticalSwimAcceleration;
        float buoyancyControlAcceleration = buoyancyInput >= 0f
            ? buoyancyInput * ExtraBuoyancyAcceleration
            : buoyancyInput * DrowningPullAcceleration;

        if (RideWaveSurface)
        {
            // Work in the water's moving frame. relativeVelocity is the swimmer's
            // vertical speed minus the surface's vertical speed, so a wave that
            // rises or falls at several m/s carries the swimmer with it and the
            // speed / acceleration limits only constrain motion relative to the water
            // (swim up/down, drowning pull, settling to the floating height).
            float surfaceVelocity = freshWaveVelocity * RideVelocityFollow * waveResponse;
            float relativeVelocity = verticalVelocity - surfaceVelocity;
            // Only player/ability input is acceleration-limited; the tracking spring
            // must be free to follow wave accelerations.
            float inputAcceleration = Mathf.Clamp(
                swimAcceleration + buoyancyControlAcceleration,
                -MaximumDownwardAcceleration, MaximumUpwardAcceleration);
            float rideAcceleration = heightError * RideStiffness * springRetention
                                     - relativeVelocity * RideDamping
                                     + inputAcceleration;
            // Catching up with the water (it dropped away or rose under the swimmer) is
            // allowed to be faster than deliberate swimming, otherwise the body hovers
            // over a falling surface at the 1 m/s relative cap.
            relativeVelocity = Mathf.Clamp(
                relativeVelocity + rideAcceleration * dt,
                -Mathf.Max(MaximumDownwardSpeed * 3f, 3f),
                Mathf.Max(MaximumUpwardSpeed * 2f, 2f));
            verticalVelocity = relativeVelocity + surfaceVelocity;
        }
        else
        {
        float targetWaveVelocity = freshWaveVelocity * WaveVelocityFollow
                                   * WaveLiftMultiplier * waveResponse;
        float dampingAcceleration = (targetWaveVelocity - verticalVelocity) * VerticalDamping;

        float verticalAcceleration = springAcceleration + dampingAcceleration
                                     + swimAcceleration + buoyancyControlAcceleration;
        verticalAcceleration = Mathf.Clamp(
            verticalAcceleration, -MaximumDownwardAcceleration, MaximumUpwardAcceleration);
        verticalVelocity = Mathf.Clamp(
            verticalVelocity + verticalAcceleration * dt,
            -MaximumDownwardSpeed,
            MaximumUpwardSpeed);
        }
        }

        UpdateContactFoam(dt, actor, surfaceHeight);

        Vector2 driftDelta = UpdateWaveDrift(dt, surfaceHeight, bodyHeight, actor);
        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity
                           + new Vector3(driftDelta.x, 0f, driftDelta.y) / dt;
        if (Controller != null)
        {
            Controller.Move(velocity * dt);
            // CharacterController.isGrounded is only valid after Move. Clamp the
            // motor's downward velocity and clear wave-ejection state on real solid
            // contact so the player controller can reliably hand off to grounded mode.
            if (Controller.isGrounded)
            {
                if (verticalVelocity < 0f)
                    verticalVelocity = 0f;
                waveAirborne = false;
                waveEjectionTimer = 0f;
            }
        }
        else if (Body != null)
        {
            if (Body.isKinematic)
                Body.MovePosition(Body.position + velocity * dt);
            else
                Body.linearVelocity = velocity;
        }
        else
        {
            actor.position += velocity * dt;
        }
    }

    private void UpdateContactFoam(float dt, Transform actor, float surfaceHeight)
    {
        if (!EmitFoam || actor == null)
            return;
        if (foam == null)
        {
            foam = GetComponent<WaterContactFoam>();
            if (foam == null)
                foam = gameObject.AddComponent<WaterContactFoam>();
        }

        Vector3 point = actor.position;
        point.y = surfaceHeight;

        // Contact foam belongs to the visible waterline, not merely to the fact that
        // the swimming movement motor is active. Suppress it when the swimmer is
        // completely clear of the surface or deeply submerged.
        float bodyHeight = GetBodyHeight();
        float bodyBottom = GetBodyCenterY(actor) - bodyHeight * 0.5f;
        float submergedFraction = Mathf.Clamp01((surfaceHeight - bodyBottom) / Mathf.Max(0.1f, bodyHeight));
        float waterlineContact = Smooth01Between(0.02f, 0.14f, submergedFraction)
                               * (1f - Smooth01Between(0.78f, 0.94f, submergedFraction));
        if (waterlineContact <= 0.001f)
        {
            hasFoamPoint = false;
            foamAccumulator = 0f;
            rippleTimer = 0f;
            return;
        }

        float speed = horizontalVelocity.magnitude;
        float speed01 = hopAirborne ? 0f : Mathf.Clamp01(speed / Mathf.Max(0.1f, FoamFullSpeed));
        if (!UseExternalPlayerFoam)
        {
            // NPC swimmers own their own trail. PlayerController enables the external
            // player-level emitter so swimming and wading share the same tracked trail.
            foamAccumulator += dt;
            float emitInterval = 1f / Mathf.Max(5f, FoamEmitHz);
            if (foamAccumulator >= emitInterval)
            {
                float step = foamAccumulator;
                foamAccumulator = 0f;
                float curve = hopAirborne ? 0f :
                    Mathf.Lerp(0.18f, 1f, Smooth01Between(0.04f, 0.82f, speed01)) * waterlineContact;
                if (curve > 0.001f)
                {
                    Vector3 dir = horizontalVelocity.sqrMagnitude > 0.01f ? horizontalVelocity.normalized : actor.forward;
                    Vector3 lead = point + dir * FoamLeadOffset;
                    foam.EmitTrail(hasFoamPoint ? lastFoamPoint : lead, lead, curve, step,
                        FoamRadius.x, FoamRadius.y, FoamRate);
                    lastFoamPoint = lead;
                    hasFoamPoint = true;
                }
                else
                {
                    hasFoamPoint = false;
                }
            }
        }
        else
        {
            hasFoamPoint = false;
            foamAccumulator = 0f;
        }

        // NPC swimmers retain a local directional wake here. PlayerController sets
        // UseExternalPlayerFoam so PlayerWaterContactFoam emits the player's unified
        // multi-point foam + Kelvin wake for both swimming and wading.
        if (!UseExternalPlayerFoam && !hopAirborne && speed > 0.35f)
        {
            wakeDistanceAccumulator += speed * dt;
            float spacing = Mathf.Clamp(speed * 0.65f, 1.5f, 3.8f);
            Vector3 direction = horizontalVelocity.normalized;
            Vector3 wakePoint = point - direction * Mathf.Max(0.12f, FoamLeadOffset * 0.55f);
            float wakeRadius = Mathf.Lerp(0.38f, 0.58f, speed01);
            float wakeStrength = Mathf.Lerp(0.18f, 0.34f, speed01);
            if (wakeDistanceAccumulator >= spacing)
            {
                float overflow = wakeDistanceAccumulator % spacing;
                wakeDistanceAccumulator = overflow;
                WaterWakeField.EmitKelvinWake(
                    wakePoint - direction * overflow,
                    direction,
                    wakeRadius,
                    wakeStrength,
                    8f,
                    19.47f,
                    speed);
            }
        }
        else
        {
            wakeDistanceAccumulator = 0f;
        }

        // Restore visible analytic ripple sources for NPC swimmers. Their circular
        // packets overlap along the movement path; the shared heightfield remains an
        // extra vertex-displacement layer, not the only visible wake representation.
        if (!UseExternalPlayerFoam && EmitFoam && waterlineContact > 0.05f && !hopAirborne)
        {
            rippleTimer += dt;
            if (rippleTimer >= Mathf.Max(0.25f, RippleInterval))
            {
                rippleTimer = 0f;
                float rippleStrength = Mathf.Lerp(0.28f, 0.55f, speed01) * waterlineContact;
                WaterFoamField.EmitRipple(
                    point,
                    0.08f,
                    Mathf.Lerp(2.2f, 4.0f, speed01),
                    rippleStrength,
                    0f,
                    Mathf.Lerp(2.4f, 3.2f, speed01),
                    Vector2.zero,
                    0f,
                    1.25f);
            }
        }
        else
        {
            rippleTimer = 0f;
        }

        if (hopAirborne)
            hasFoamPoint = false;
    }

    private static float Smooth01Between(float edge0, float edge1, float value)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, value));
    }

    private void UpdateWaveAirborneState(float surfaceHeight, Transform actor, float dt)
    {
        // A deliberate hop has its own rising/falling animation path.
        if (hopAirborne || jumpWindupTimer >= 0f)
        {
            waveAirborne = false;
            waveEjectionTimer = 0f;
            return;
        }

        float bodyCenterY = GetBodyCenterY(actor);
        if (bodyCenterY > surfaceHeight + WaveEjectionHeight)
        {
            waveEjectionTimer += dt;
            if (waveEjectionTimer >= WaveEjectionConfirmationTime)
                waveAirborne = true;
        }
        else if (bodyCenterY <= surfaceHeight + WaveReentryMargin)
        {
            // Use a separate lower threshold to prevent rapid state toggling as
            // the body skims the waterline during an oscillating wave.
            waveEjectionTimer = 0f;
            waveAirborne = false;
        }
    }

    private void EmitHopSplash(float intensity, float fallDuration = 0f, bool landing = false)
    {
        if (!EmitFoam)
            return;
        if (foam == null)
        {
            foam = GetComponent<WaterContactFoam>();
            if (foam == null)
                foam = gameObject.AddComponent<WaterContactFoam>();
        }
        Vector3 point = ActorTransform != null ? ActorTransform.position : transform.position;
        point.y = GetCurrentSurfaceHeight(ActorTransform != null ? ActorTransform : transform);
        if (landing)
            foam.EmitImpactRipplePair(point, fallDuration, intensity);
        else
            foam.EmitRipple(point, 0.3f, 0.2f + 0.3f * intensity, 0.6f + 0.4f * intensity,
                Vector3.zero, 0f, 0.3f, 0.25f);
        foam.EmitBurst(point, intensity, 0.55f);
    }

    private void LaunchHop(Vector3 desiredDirection, Transform actor)
    {
        jumpWindupTimer = -1f;
        hopAirborne = true;
        hopDescendingDuration = 0f;
        waveAirborne = false;
        waveEjectionTimer = 0f;
        verticalVelocity = JumpVelocity;

        Vector3 direction = desiredDirection;
        if (direction.sqrMagnitude < 0.0001f)
            direction = horizontalVelocity.sqrMagnitude > 0.04f ? horizontalVelocity : Vector3.zero;
        if (direction.sqrMagnitude > 0.0001f)
            horizontalVelocity += direction.normalized * JumpSpeedBoost;
        EmitHopSplash(0.55f);
        HopStarted?.Invoke();
    }

    /// <summary>
    /// Virtual damped oscillator chasing the horizontal displacement of the water particle
    /// under the swimmer. It is a linear filter of a zero-mean signal, so the swimmer is
    /// carried side to side and returns to the same place every wave cycle. Returns this
    /// step's displacement.
    /// </summary>
    private Vector2 UpdateWaveDrift(float dt, float surfaceHeight, float bodyHeight, Transform actor)
    {
        if (!EnableWaveDrift && driftOffset.sqrMagnitude < 0.000001f && driftVelocity.sqrMagnitude < 0.000001f)
            return Vector2.zero;

        float exposure = 0f;
        if (EnableWaveDrift && !hopAirborne && Ocean != null)
        {
            float bottom = GetBodyCenterY(actor) - bodyHeight * 0.5f;
            float submerged = Mathf.Clamp01((surfaceHeight - bottom) / Mathf.Max(0.1f, bodyHeight));
            exposure = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DriftFadeStart, DriftFadeEnd, submerged));
        }
        driftExposure = exposure;

        Vector2 lattice = HasFreshSurfaceSample() ? sampledLattice : Vector2.zero;
        Vector2 target = Vector2.ClampMagnitude(lattice * (WaveDriftFollow * exposure), WaveDriftMaxOffset);

        float omega = WaveDriftResponse;
        Vector2 acceleration = (target - driftOffset) * (omega * omega)
                               - driftVelocity * (2f * WaveDriftDamping * omega);
        driftVelocity += acceleration * dt;
        Vector2 previous = driftOffset;
        driftOffset += driftVelocity * dt;
        return driftOffset - previous;
    }

    public void SetPlanarVelocity(Vector3 velocity)
    {
        horizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
    }

    public void ResetVerticalVelocity()
    {
        verticalVelocity = 0f;
    }

    /// <summary>
    /// Receives vertical motion when an airborne grounded jump/fall enters the water.
    /// The buoyancy solver takes over from this initial velocity on its next physics step.
    /// </summary>
    public void SetVerticalVelocity(float value)
    {
        verticalVelocity = value;
    }

    private Vector3 GetCameraRelativeDirection(Vector2 input, Transform actor)
    {
        Transform reference = ReferenceTransform != null ? ReferenceTransform : actor;
        Vector3 forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(reference.right, Vector3.up).normalized;
        Vector3 direction = right * input.x + forward * input.y;
        return direction.sqrMagnitude > 1f ? direction.normalized : direction;
    }

    private bool HasFreshSurfaceSample()
    {
        return hasSurfaceSample &&
               Time.realtimeSinceStartup - sampledSurfaceTime <= MaximumSurfaceSampleAge;
    }

    /// <summary>
    /// Folds a freshly accepted async sample into the surface tracker. The sample is
    /// time-stamped with the simulation time of the FFT maps it was read from, so it
    /// is first moved to the swimmer's current XZ (tangent plane, clamped) and then
    /// extrapolated by its age using the tracked vertical velocity. Between samples
    /// the tracker dead-reckons every physics step, so the swimmer sees a continuous
    /// water height instead of a stair-stepped, delayed one.
    /// </summary>
    private void IngestSurfaceMeasurement(
        SpectralOceanFFT.SurfaceSample sample, Vector3 currentQueryPosition, float sampleAge)
    {
        float now = Time.realtimeSinceStartup;

        // Height at the swimmer's current XZ (the sample is exact only where it was queried).
        float heightAtCurrent = sample.Height;
        if (WaterSurfaceSampleUtility.TryGetHeightAtWorldPosition(
                sample, currentQueryPosition, SurfaceTrackSlopeLimit,
                SurfaceTrackCorrectionLimit, out float corrected))
            heightAtCurrent = corrected;

        // Vertical speed: compare the previous sample, moved to THIS sample's exact
        // XZ, with this sample. Removing the horizontal-travel term stops swimming
        // across a slope from being mistaken for a rising or falling wave.
        bool gotVelocity = false;
        float measuredVelocity = 0f;
        float sampleInterval = 0f;
        if (hasPrevMeasurement)
        {
            sampleInterval = sample.SampleTime - prevMeasurement.SampleTime;
            if (sampleInterval > 0.015f && sampleInterval < 0.6f &&
                WaterSurfaceSampleUtility.TryGetHeightAtWorldPosition(
                    prevMeasurement, sample.Position, SurfaceTrackSlopeLimit,
                    SurfaceTrackCorrectionLimit, out float previousAtThis))
            {
                measuredVelocity = Mathf.Clamp(
                    (sample.Height - previousAtThis) / sampleInterval,
                    -MaximumTrackedSurfaceSpeed, MaximumTrackedSurfaceSpeed);
                gotVelocity = true;
            }
        }
        if (sampleInterval > 0.015f || !hasPrevMeasurement)
        {
            prevMeasurement = sample;
            hasPrevMeasurement = true;
        }

        bool stale = now - lastIngestRealtime > 1.0f;
        if (!hasTracker || stale)
        {
            trackedHeight = heightAtCurrent;
            trackedVelocity = gotVelocity ? measuredVelocity : 0f;
            hasTracker = true;
        }
        else
        {
            if (gotVelocity)
            {
                float blend = 1f - Mathf.Exp(-sampleInterval / Mathf.Max(0.02f, SurfaceVelocitySmoothTime));
                trackedVelocity = Mathf.Lerp(trackedVelocity, measuredVelocity, blend);
            }

            float latencyCompensated = heightAtCurrent + trackedVelocity * Mathf.Clamp(sampleAge, 0f, 0.6f);
            float error = latencyCompensated - trackedHeight;
            if (Mathf.Abs(error) > SurfaceTrackerSnapDistance)
                trackedHeight = latencyCompensated;
            else
                trackedHeight += error * SurfaceTrackerCorrection;
        }

        // Remember the local wave slope so the tracker can follow the surface while the
        // swimmer travels across it (e.g. sliding off the side of a wave) between samples.
        Vector3 n = sample.Normal.sqrMagnitude > 0.001f ? sample.Normal.normalized : Vector3.up;
        float ny = Mathf.Max(n.y, 0.12f);
        trackedSlope = Vector2.ClampMagnitude(
            new Vector2(-n.x / ny, -n.z / ny), SurfaceTrackSlopeLimit);
        lastTrackXZ = new Vector2(currentQueryPosition.x, currentQueryPosition.z);
        hasTrackXZ = true;

        lastIngestRealtime = now;
        smoothedSurfaceHeight = trackedHeight;
        hasSmoothedSurfaceHeight = true;
    }

    private void UpdateRideSurfaceTarget(float dt)
    {
        if (!hasTracker)
        {
            // No sample yet: sit at mean sea level until the first readback arrives.
            trackedHeight = hasSurfaceSample ? sampledSurfaceHeight : Ocean.WaterLevelY;
            trackedVelocity = 0f;
            smoothedSurfaceHeight = trackedHeight;
            hasSmoothedSurfaceHeight = true;
            return;
        }

        float age = hasSurfaceSample
            ? Time.realtimeSinceStartup - sampledSurfaceTime
            : float.PositiveInfinity;
        float overdue = age - MaximumSurfaceSampleAge;   // > 0 once samples are no longer fresh

        if (overdue > 0f)
        {
            // Samples are late (slow readback, rejected queries). Keep riding on the
            // last measured motion but fade it out so the swimmer cannot run away.
            trackedVelocity *= Mathf.Exp(-dt / 0.6f);
        }

        trackedHeight += trackedVelocity * dt;

        // Spatial term: the surface height under the swimmer also changes because the
        // swimmer moves across the wave slope. Without it the target stays "stuck" at
        // the old crest height until the next sample and the body hovers above the water.
        Transform trackActor = ActorTransform;
        if (trackActor != null && hasTrackXZ)
        {
            Vector3 q = GetSurfaceQueryWorldPosition(trackActor);
            Vector2 xz = new Vector2(q.x, q.z);
            Vector2 moved = xz - lastTrackXZ;
            lastTrackXZ = xz;
            if (moved.sqrMagnitude < 4f)   // ignore teleports
            {
                float spatial = Vector2.Dot(trackedSlope, moved);
                trackedHeight += Mathf.Clamp(spatial, -SurfaceTrackCorrectionLimit, SurfaceTrackCorrectionLimit);
            }
        }

        if (overdue > SurfaceSampleHoldTime)
        {
            // Sampling has genuinely stopped: only now ease toward mean sea level,
            // slowly, instead of snapping the swimmer to a flat ocean.
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.5f, SurfaceLostReturnTime) * 3f);
            trackedHeight = Mathf.Lerp(trackedHeight, Ocean.WaterLevelY, k);
        }

        smoothedSurfaceHeight = trackedHeight;
        filteredSurfaceVerticalVelocity = trackedVelocity;
        hasSmoothedSurfaceHeight = true;
    }

    private void UpdateFilteredSurfaceTarget(float dt)
    {
        if (Ocean == null)
            return;

        if (RideWaveSurface)
        {
            UpdateRideSurfaceTarget(dt);
            return;
        }

        if (!hasSmoothedSurfaceHeight)
        {
            smoothedSurfaceHeight = hasSurfaceSample ? sampledSurfaceHeight : Ocean.WaterLevelY;
            surfaceHeightSmoothVelocity = 0f;
            filteredSurfaceVerticalVelocity = 0f;
            hasSmoothedSurfaceHeight = true;
        }

        float sampleAge = hasSurfaceSample
            ? Time.realtimeSinceStartup - sampledSurfaceTime
            : float.PositiveInfinity;

        // Do not jump from a valid displaced wave sample to flat WaterLevelY just
        // because one asynchronous readback missed its deadline. Hold briefly, then
        // converge slowly toward the mean level if sampling really stops.
        float targetHeight = hasSurfaceSample && sampleAge <= SurfaceSampleHoldTime
            ? sampledSurfaceHeight
            : Ocean.WaterLevelY;

        smoothedSurfaceHeight = Mathf.SmoothDamp(
            smoothedSurfaceHeight, targetHeight, ref surfaceHeightSmoothVelocity,
            SurfaceHeightSmoothTime, MaximumSurfaceFollowSpeed, dt);
        surfaceHeightSmoothVelocity = Mathf.Clamp(
            surfaceHeightSmoothVelocity, -MaximumWaveVerticalSpeed, MaximumWaveVerticalSpeed);
        filteredSurfaceVerticalVelocity = Mathf.Lerp(
            filteredSurfaceVerticalVelocity, surfaceHeightSmoothVelocity,
            Mathf.Clamp01(WaveVelocitySmoothing));
    }

    private float GetCurrentSurfaceHeight(Transform actor)
    {
        if (Ocean == null)
            return GetBodyCenterY(actor) - (0.5f - TargetSubmersion) * GetBodyHeight();

        return hasSmoothedSurfaceHeight ? smoothedSurfaceHeight
            : hasSurfaceSample ? sampledSurfaceHeight : Ocean.WaterLevelY;
    }

    private float GetCurrentSurfaceVerticalVelocity()
    {
        if (RideWaveSurface)
            return hasTracker ? trackedVelocity : 0f;

        if (!hasSmoothedSurfaceHeight ||
            (!HasFreshSurfaceSample() && (!hasSurfaceSample ||
             Time.realtimeSinceStartup - sampledSurfaceTime > SurfaceSampleHoldTime)))
            return 0f;
        return filteredSurfaceVerticalVelocity;
    }

    private float GetBodyHeight()
    {
        if (Controller != null)
            return Mathf.Max(0.1f, Controller.height * Mathf.Abs(Controller.transform.lossyScale.y));
        if (BodyCollider != null)
            return Mathf.Max(0.1f, BodyCollider.bounds.size.y);
        return AssumedBodyHeight;
    }

    private float GetBodyCenterY(Transform actor)
    {
        if (Controller != null)
            return Controller.transform.TransformPoint(Controller.center).y;
        if (BodyCollider != null)
            return BodyCollider.bounds.center.y;
        if (Body != null)
            return Body.worldCenterOfMass.y;
        return actor.position.y + AssumedBodyCenterOffset;
    }

    private Vector3 GetSurfaceQueryWorldPosition(Transform actor)
    {
        Vector3 position = actor != null ? actor.position : transform.position;

        // Sample the actual body/capsule centre in world space. The previous five
        // probes were spread around the player and averaged together; at moderate
        // wind they could cover different crests/troughs and create a false water
        // height that did not exist beneath the player.
        if (Controller != null && Controller.enabled)
            position = Controller.transform.TransformPoint(Controller.center);
        else if (BodyCollider != null)
            position = BodyCollider.bounds.center;
        else if (Body != null)
            position = Body.worldCenterOfMass;

        return position;
    }

    private void RequestSurfaceSampleIfNeeded()
    {
        if (Ocean == null || !Ocean.CanSampleSurface || sampleRequestPending ||
            Time.time < nextSampleRequestTime)
            return;

        Transform actor = ActorTransform;
        if (actor == null)
            return;

        Vector3 requestPosition = GetSurfaceQueryWorldPosition(actor);
        nextSampleRequestTime = Time.time + SurfaceSampleInterval;
        sampleRequestPending = true;
        int generation = sampleGeneration;

        bool queued = Ocean.RequestSurfaceSample(requestPosition, sample =>
        {
            if (generation != sampleGeneration)
                return;

            sampleRequestPending = false;
            if (!isActiveAndEnabled)
                return;
            if (!sample.IsValid ||
                float.IsNaN(sample.Height) || float.IsInfinity(sample.Height))
            {
                rejectedInvalidCount++;
                return;
            }
            if (sample.HorizontalQueryErrorMeters > MaximumSurfaceQueryError)
            {
                rejectedErrorCount++;
                return;
            }

            Transform currentActor = ActorTransform;
            Vector3 currentQueryPosition = GetSurfaceQueryWorldPosition(currentActor);
            Vector2 spatialDelta = new Vector2(
                currentQueryPosition.x - requestPosition.x,
                currentQueryPosition.z - requestPosition.z);

            // SpectralOceanFFT returns the height at the exact world XZ that was
            // queried (sample.Position.xz == requestPosition.xz). The returned
            // normal is useful for orientation, but extrapolating the height along
            // its tangent plane is unsafe for a fast-moving CharacterController:
            // at higher wind, steep normals turn even a small async readback drift
            // into a large artificial vertical correction. Accept only nearby
            // samples and use their exact queried height without extrapolation.
            if (spatialDelta.sqrMagnitude > MaximumSurfaceQueryDrift * MaximumSurfaceQueryDrift)
            {
                rejectedDriftCount++;
                return;
            }

            float currentSampleAge = Time.realtimeSinceStartup - sample.SampleTime;
            if (currentSampleAge < -0.05f || currentSampleAge > MaximumSurfaceSampleAge)
            {
                rejectedAgeCount++;
                return;
            }

            acceptedSampleCount++;
            sampledSurfaceHeight = sample.Height;
            sampledLattice = sample.LatticeDisplacementXZ;
            sampledSurfaceQueryPosition = requestPosition;
            sampledSurfaceQueryError = sample.HorizontalQueryErrorMeters;
            sampledSurfaceTime = sample.SampleTime;

            if (RideWaveSurface)
                IngestSurfaceMeasurement(sample, currentQueryPosition, currentSampleAge);

            if (!hasSmoothedSurfaceHeight)
            {
                smoothedSurfaceHeight = sampledSurfaceHeight;
                surfaceHeightSmoothVelocity = 0f;
                filteredSurfaceVerticalVelocity = 0f;
                hasSmoothedSurfaceHeight = true;
            }
            hasSurfaceSample = true;
        });

        if (!queued)
            sampleRequestPending = false;
    }

    private void OnGUI()
    {
        if (!ShowSurfaceDebug || !isActiveAndEnabled)
            return;

        float centreY = ActorTransform != null ? GetBodyCenterY(ActorTransform) : 0f;
        GUI.Label(new Rect(10f, 10f, 640f, 110f),
            "Swim surface | accepted " + acceptedSampleCount +
            "  rejected: invalid " + rejectedInvalidCount +
            ", error " + rejectedErrorCount +
            ", drift " + rejectedDriftCount +
            ", age " + rejectedAgeCount +
            "\nsample age " + SurfaceSampleAge.ToString("F2") + " s" +
            "   tracked height " + trackedHeight.ToString("F2") +
            "   surface vel " + trackedVelocity.ToString("F2") + " m/s" +
            "\nbody centre Y " + centreY.ToString("F2") +
            "   body-surface " + (centreY - trackedHeight).ToString("F2") +
            "   waveAirborne " + waveAirborne);
    }

    private void ResolveReferences()
    {
        if (Controller == null)
            Controller = GetComponent<CharacterController>();
        if (Body == null)
            Body = GetComponent<Rigidbody>();
        if (BodyCollider == null)
            BodyCollider = GetComponent<Collider>();
        if (Ocean == null)
            Ocean = Object.FindAnyObjectByType<SpectralOceanFFT>();
        if (Stamina == null)
            Stamina = StaminaComponent.Resolve(gameObject);
    }

    private void CacheRigidbodyGravity()
    {
        if (Body == null || Controller != null || bodyGravityCached)
            return;

        bodyGravityWasEnabled = Body.useGravity;
        bodyGravityCached = true;
    }

    private void SetRigidbodyGravityControlled(bool controlled)
    {
        if (Body == null || Controller != null)
            return;

        CacheRigidbodyGravity();
        if (!bodyGravityCached)
            return;

        Body.useGravity = controlled ? false : bodyGravityWasEnabled;
    }
}
