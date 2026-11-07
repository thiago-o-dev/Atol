using Assets._Project.Features.Movement;
using UnityEngine;

/// <summary>
/// Reusable actor-agnostic swimming motor. Player and NPC controllers can drive it
/// through IMovement while AI can additionally control buoyancy/drowning through
/// ISwimmingBuoyancyControl. Supports CharacterController and Rigidbody actors.
/// </summary>
[DisallowMultipleComponent]
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
    }

    public void Move(Vector2 direction)
    {
        inputDirection = Vector2.ClampMagnitude(direction, 1f);
    }

    /// <summary>Jump/up swims upward while held (PlayerController-compatible).</summary>
    public void Jump(bool released)
    {
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
        float heightError = targetCenterY - GetBodyCenterY(actor);

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

        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
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
