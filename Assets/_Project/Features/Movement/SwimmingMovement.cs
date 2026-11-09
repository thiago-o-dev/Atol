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
    [Min(0f)] public float SurfaceSpring = 10f;
    [Tooltip("Damps vertical motion without making the actor feel glued to the water.")]
    [Min(0f)] public float VerticalDamping = 4.8f;
    [Tooltip("Scales the measured vertical speed of the water. The swimmer follows both rising and falling waves.")]
    [Min(0f)] public float WaveLiftMultiplier = 1.0f;
    [Tooltip("How strongly the swimmer matches the water surface vertical velocity in both directions. 0 ignores wave motion; 1 fully follows it before WaveLiftMultiplier is applied.")]
    [Range(0f, 2f)] public float WaveVelocityFollow = 0.9f;
    [Tooltip("Limits noisy surface-height changes when estimating wave vertical velocity.")]
    [Min(0.1f)] public float MaximumWaveVerticalSpeed = 3.5f;
    [Tooltip("Extra upward acceleration available to wave buoyancy input and jump/swim-up input.")]
    [Min(0f)] public float ExtraBuoyancyAcceleration = 8f;
    [Tooltip("Downward acceleration available to drowning/submerge input.")]
    [Min(0f)] public float DrowningPullAcceleration = 16f;
    [Tooltip("Remaining surface-spring strength while fully drowned. Lower values let abilities pull actors deeper underwater.")]
    [Range(0f, 1f)] public float DrowningBuoyancyRetention = 0.18f;
    [Tooltip("Fraction of wave-follow force retained while drowning pull is active.")]
    [Range(0f, 1f)] public float DrowningWaveResponse = 0.3f;
    [Min(0f)] public float MaximumUpwardAcceleration = 24f;
    [Min(0f)] public float MaximumDownwardAcceleration = 22f;
    [Min(0.1f)] public float MaximumUpwardSpeed = 2.8f;
    [Min(0.1f)] public float MaximumDownwardSpeed = 3f;

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
    [Min(0.1f)] public float FoamFullSpeed = 3f;

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
    [Min(0.02f)] public float SurfaceSampleInterval = 0.12f;
    [Min(0.05f)] public float MaximumSurfaceSampleAge = 0.5f;
    [Range(0f, 1f)] public float WaveVelocitySmoothing = 0.4f;

    private Vector2 inputDirection;
    private float verticalInput;
    private bool jumpHeld;
    private bool diveHeld;
    private float buoyancyInput;
    private float verticalVelocity;
    private float turnSmoothVelocity;
    private Vector3 horizontalVelocity;

    private float sampledSurfaceHeight;
    private float sampledSurfaceVerticalVelocity;
    private float sampledSurfaceTime = -1000f;
    private float nextSampleRequestTime;
    private bool hasSurfaceSample;
    private bool sampleRequestPending;
    private int sampleGeneration;

    private Vector2 sampledLattice;
    private Vector2 driftOffset;
    private Vector2 driftVelocity;
    private float driftExposure = 1f;
    private float jumpWindupTimer = -1f;
    private float nextJumpTime;
    private bool hopAirborne;
    private float hopLandedTime = -1000f;
    private WaterContactFoam foam;
    private Vector3 lastFoamPoint;
    private bool hasFoamPoint;
    private float rippleTimer;

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
    /// <summary>True during the jump wind-up, the hop itself and a short landing grace.</summary>
    public bool IsHopping => jumpWindupTimer >= 0f || hopAirborne ||
                             Time.time - hopLandedTime < JumpLandingGrace;
    public bool IsAirborne => hopAirborne;
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
        DriftFadeEnd = Mathf.Max(DriftFadeStart + 0.01f, DriftFadeEnd);
        JumpStaminaCost = Mathf.Max(0f, JumpStaminaCost);
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
            if (verticalVelocity <= 0f && heightError >= 0f)
            {
                hopAirborne = false;
                hopLandedTime = Time.time;
                float impact = -verticalVelocity;
                verticalVelocity = Mathf.Max(verticalVelocity, -MaximumDownwardSpeed);
                EmitHopSplash(Mathf.Clamp01(0.35f + impact / 8f));
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
        float targetWaveVelocity = freshWaveVelocity * WaveVelocityFollow
                                   * WaveLiftMultiplier * waveResponse;
        float dampingAcceleration = (targetWaveVelocity - verticalVelocity) * VerticalDamping;
        float swimAcceleration = verticalInput * VerticalSwimAcceleration;
        float buoyancyControlAcceleration = buoyancyInput >= 0f
            ? buoyancyInput * ExtraBuoyancyAcceleration
            : buoyancyInput * DrowningPullAcceleration;

        float verticalAcceleration = springAcceleration + dampingAcceleration
                                     + swimAcceleration + buoyancyControlAcceleration;
        verticalAcceleration = Mathf.Clamp(
            verticalAcceleration, -MaximumDownwardAcceleration, MaximumUpwardAcceleration);
        verticalVelocity = Mathf.Clamp(
            verticalVelocity + verticalAcceleration * dt,
            -MaximumDownwardSpeed,
            MaximumUpwardSpeed);
        }

        UpdateContactFoam(dt, actor, surfaceHeight);

        Vector2 driftDelta = UpdateWaveDrift(dt, surfaceHeight, bodyHeight, actor);
        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity
                           + new Vector3(driftDelta.x, 0f, driftDelta.y) / dt;
        if (Controller != null)
        {
            Controller.Move(velocity * dt);
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
        float speed01 = hopAirborne ? 0f : Mathf.Clamp01(horizontalVelocity.magnitude / FoamFullSpeed);
        // Idle swimmers still disturb the water a little.
        float intensity = hopAirborne ? 0f : Mathf.Max(speed01, 0.35f);
        foam.EmitTrail(hasFoamPoint ? lastFoamPoint : point, point, intensity, dt);

        // Small rings: faster while swimming, rare while treading water.
        rippleTimer += dt;
        float rippleInterval = speed01 > 0.15f ? 0.5f : 1.6f;
        if (!hopAirborne && rippleTimer >= rippleInterval)
        {
            rippleTimer = 0f;
            Vector3 behind = speed01 > 0.15f ? -horizontalVelocity : Vector3.zero;
            foam.EmitRipple(point, 0.25f, 0.12f + 0.1f * speed01, 0.45f + 0.35f * speed01, behind, 1f);
        }
        lastFoamPoint = point;
        hasFoamPoint = !hopAirborne;
    }

    private void EmitHopSplash(float intensity)
    {
        if (!EmitFoam || foam == null)
            return;
        Vector3 point = ActorTransform != null ? ActorTransform.position : transform.position;
        point.y = GetCurrentSurfaceHeight(ActorTransform != null ? ActorTransform : transform);
        foam.EmitBurst(point, intensity);
        foam.EmitRipple(point, 0.3f, 0.2f + 0.3f * intensity, 0.6f + 0.4f * intensity);
    }

    private void LaunchHop(Vector3 desiredDirection, Transform actor)
    {
        jumpWindupTimer = -1f;
        hopAirborne = true;
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

    private float GetCurrentSurfaceHeight(Transform actor)
    {
        if (Ocean == null)
            return GetBodyCenterY(actor) - (0.5f - TargetSubmersion) * GetBodyHeight();

        return HasFreshSurfaceSample() ? sampledSurfaceHeight : Ocean.WaterLevelY;
    }

    private float GetCurrentSurfaceVerticalVelocity()
    {
        return HasFreshSurfaceSample() ? sampledSurfaceVerticalVelocity : 0f;
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

    private void RequestSurfaceSampleIfNeeded()
    {
        if (Ocean == null || !Ocean.CanSampleSurface || sampleRequestPending ||
            Time.time < nextSampleRequestTime)
            return;

        nextSampleRequestTime = Time.time + SurfaceSampleInterval;
        sampleRequestPending = true;
        int generation = sampleGeneration;
        Vector3 samplePosition = ActorTransform.position;

        bool queued = Ocean.RequestSurfaceSample(samplePosition, sample =>
        {
            if (generation != sampleGeneration)
                return;

            sampleRequestPending = false;
            if (!isActiveAndEnabled || !sample.IsValid)
                return;

            if (hasSurfaceSample)
            {
                float sampleDt = sample.SampleTime - sampledSurfaceTime;
                if (sampleDt > 0.015f)
                {
                    float rawVerticalSpeed = (sample.Height - sampledSurfaceHeight) / sampleDt;
                    rawVerticalSpeed = Mathf.Clamp(
                        rawVerticalSpeed, -MaximumWaveVerticalSpeed, MaximumWaveVerticalSpeed);
                    sampledSurfaceVerticalVelocity = Mathf.Lerp(
                        sampledSurfaceVerticalVelocity, rawVerticalSpeed, WaveVelocitySmoothing);
                }
            }
            else
            {
                sampledSurfaceVerticalVelocity = 0f;
                hasSurfaceSample = true;
            }

            sampledLattice = sample.LatticeDisplacementXZ;
            sampledSurfaceHeight = sample.Height;
            sampledSurfaceTime = sample.SampleTime > 0f
                ? sample.SampleTime
                : Time.realtimeSinceStartup;
        });

        if (!queued)
            sampleRequestPending = false;
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
