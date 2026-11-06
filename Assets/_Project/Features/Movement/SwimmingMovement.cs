using Assets._Project.Features.Movement;
using UnityEngine;

/// <summary>
/// CharacterController-based swimming movement. Horizontal input is camera-relative.
/// Jump input swims upward while held; SetDiveInput can be wired to a separate action.
/// The character is gently held at a configurable depth beneath the FFT ocean surface.
/// </summary>
[DisallowMultipleComponent]
public sealed class SwimmingMovement : MonoBehaviour, IMovement
{
    [Header("References")]
    public Transform ReferenceTransform;
    public CharacterController Controller;
    public SpectralOceanFFT Ocean;

    [Header("Horizontal Swimming")]
    [Min(0f)] public float Speed = 3.8f;
    [Min(0f)] public float Acceleration = 8f;
    [Min(0f)] public float Deceleration = 5f;
    [Min(0.01f)] public float TurnSmoothTime = 0.12f;

    [Header("Depth / Buoyancy")]
    [Tooltip("Target distance between the character pivot and the water surface.")]
    [Min(0f)] public float DepthBelowSurface = 0.65f;
    [Min(0f)] public float SurfaceSpring = 5f;
    [Min(0f)] public float VerticalDamping = 3.5f;
    [Min(0f)] public float VerticalSwimAcceleration = 8f;
    [Min(0.1f)] public float MaximumVerticalSpeed = 2.5f;
    [Min(0.02f)] public float SurfaceSampleInterval = 0.12f;
    [Min(0.05f)] public float MaximumSurfaceSampleAge = 0.5f;

    private Vector2 inputDirection;
    private float verticalInput;
    private bool jumpHeld;
    private bool diveHeld;
    private float verticalVelocity;
    private float turnSmoothVelocity;
    private float sampledSurfaceHeight;
    private float sampledSurfaceTime = -1000f;
    private float nextSampleRequestTime;
    private bool hasSurfaceSample;
    private bool sampleRequestPending;
    private int sampleGeneration;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        sampleGeneration++;
        sampleRequestPending = false;
    }

    private void OnDisable()
    {
        sampleGeneration++;
        sampleRequestPending = false;
        inputDirection = Vector2.zero;
        jumpHeld = false;
        diveHeld = false;
        verticalInput = 0f;
        _horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
    }

    private void OnValidate()
    {
        Speed = Mathf.Max(0f, Speed);
        Acceleration = Mathf.Max(0f, Acceleration);
        Deceleration = Mathf.Max(0f, Deceleration);
        TurnSmoothTime = Mathf.Max(0.01f, TurnSmoothTime);
        SurfaceSpring = Mathf.Max(0f, SurfaceSpring);
        VerticalDamping = Mathf.Max(0f, VerticalDamping);
        VerticalSwimAcceleration = Mathf.Max(0f, VerticalSwimAcceleration);
        MaximumVerticalSpeed = Mathf.Max(0.1f, MaximumVerticalSpeed);
        SurfaceSampleInterval = Mathf.Max(0.02f, SurfaceSampleInterval);
        MaximumSurfaceSampleAge = Mathf.Max(0.05f, MaximumSurfaceSampleAge);
    }

    public void Move(Vector2 direction)
    {
        inputDirection = Vector2.ClampMagnitude(direction, 1f);
    }

    /// <summary>Hold the configured Jump action to swim upward.</summary>
    public void Jump(bool released)
    {
        jumpHeld = !released;
        RefreshVerticalInput();
    }

    /// <summary>Wire this to a separate dive/crouch action if downward swimming is desired.</summary>
    public void SetDiveInput(bool held)
    {
        diveHeld = held;
        RefreshVerticalInput();
    }

    public void SetVerticalInput(float value)
    {
        jumpHeld = value > 0f;
        diveHeld = value < 0f;
        RefreshVerticalInput();
    }

    private void RefreshVerticalInput()
    {
        verticalInput = diveHeld ? -1f : (jumpHeld ? 1f : 0f);
    }

    public void ApplyMovement()
    {
        ResolveReferences();
        if (Controller == null || !Controller.enabled)
            return;

        RequestSurfaceSampleIfNeeded();

        float dt = Time.fixedDeltaTime;
        Vector3 desiredDirection = GetCameraRelativeDirection(inputDirection);
        Vector3 targetHorizontalVelocity = desiredDirection * Speed;
        Vector3 currentHorizontalVelocity = Vector3.ProjectOnPlane(_horizontalVelocity, Vector3.up);
        float rate = desiredDirection.sqrMagnitude > 0.0001f ? Acceleration : Deceleration;
        currentHorizontalVelocity = Vector3.MoveTowards(
            currentHorizontalVelocity, targetHorizontalVelocity, rate * dt);
        _horizontalVelocity = currentHorizontalVelocity;

        if (desiredDirection.sqrMagnitude > 0.0001f)
        {
            float targetAngle = Mathf.Atan2(desiredDirection.x, desiredDirection.z) * Mathf.Rad2Deg;
            float angle = Mathf.SmoothDampAngle(
                transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, TurnSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);
        }

        float surfaceHeight = GetCurrentSurfaceHeight();
        float targetY = surfaceHeight - DepthBelowSurface;
        float springAcceleration = (targetY - transform.position.y) * SurfaceSpring;
        float dampingAcceleration = -verticalVelocity * VerticalDamping;
        float swimAcceleration = verticalInput * VerticalSwimAcceleration;
        verticalVelocity += (springAcceleration + dampingAcceleration + swimAcceleration) * dt;
        verticalVelocity = Mathf.Clamp(verticalVelocity, -MaximumVerticalSpeed, MaximumVerticalSpeed);

        Vector3 velocity = _horizontalVelocity + Vector3.up * verticalVelocity;
        Controller.Move(velocity * dt);
    }

    private Vector3 _horizontalVelocity;

    private Vector3 GetCameraRelativeDirection(Vector2 input)
    {
        Vector3 forward;
        Vector3 right;

        if (ReferenceTransform != null)
        {
            forward = Vector3.ProjectOnPlane(ReferenceTransform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(ReferenceTransform.right, Vector3.up).normalized;
        }
        else
        {
            forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        }

        Vector3 direction = right * input.x + forward * input.y;
        return direction.sqrMagnitude > 1f ? direction.normalized : direction;
    }

    private float GetCurrentSurfaceHeight()
    {
        if (Ocean == null)
            return transform.position.y + DepthBelowSurface;

        bool sampleFresh = hasSurfaceSample &&
            Time.realtimeSinceStartup - sampledSurfaceTime <= MaximumSurfaceSampleAge;
        return sampleFresh ? sampledSurfaceHeight : Ocean.WaterLevelY;
    }

    private void RequestSurfaceSampleIfNeeded()
    {
        if (Ocean == null || !Ocean.CanSampleSurface || sampleRequestPending ||
            Time.time < nextSampleRequestTime)
            return;

        nextSampleRequestTime = Time.time + SurfaceSampleInterval;
        sampleRequestPending = true;
        int generation = sampleGeneration;
        Vector3 samplePosition = transform.position;

        bool queued = Ocean.RequestSurfaceSample(samplePosition, sample =>
        {
            if (generation != sampleGeneration)
                return;

            sampleRequestPending = false;
            if (!isActiveAndEnabled || !sample.IsValid)
                return;

            sampledSurfaceHeight = sample.Height;
            sampledSurfaceTime = Time.realtimeSinceStartup;
            hasSurfaceSample = true;
        });

        if (!queued)
            sampleRequestPending = false;
    }

    private void ResolveReferences()
    {
        if (Controller == null)
            Controller = GetComponent<CharacterController>();
        if (ReferenceTransform == null && Camera.main != null)
            ReferenceTransform = Camera.main.transform;
        if (Ocean == null)
            Ocean = Object.FindAnyObjectByType<SpectralOceanFFT>();
    }
}
