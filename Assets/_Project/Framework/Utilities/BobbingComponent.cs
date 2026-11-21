
using UnityEngine;

[DisallowMultipleComponent]
public class BobbingComponent : MonoBehaviour
{
    // =========================================================
    // MOTION MODE
    // =========================================================

    [Header("Motion Mode")]
    [Tooltip("OFF = predictable sine-wave movement. ON = independent random hovering.")]
    [SerializeField] private bool useRandomizedMotion = false;

    // =========================================================
    // BOBBING AXES
    // =========================================================

    [Header("Bobbing Axes")]
    [SerializeField] private bool bobEnabled = true;

    [SerializeField] private bool bobX = false;
    [SerializeField] private bool bobY = true;
    [SerializeField] private bool bobZ = false;

    [Tooltip("Maximum distance from the original position on each local axis.")]
    [SerializeField] private Vector3 bobRange = new Vector3(0.2f, 0.5f, 0.2f);

    // =========================================================
    // PERIODIC BOBBING
    // =========================================================

    [Header("Periodic Bobbing")]
    [Tooltip("Overall speed of the periodic bobbing.")]
    [SerializeField, Min(0f)] private float bobSpeed = 1f;

    [Tooltip("Independent frequency for each axis.")]
    [SerializeField] private Vector3 axisFrequency = Vector3.one;

    [Tooltip("Independent phase offset for each axis, measured in cycles.")]
    [SerializeField] private Vector3 axisPhase = Vector3.zero;

    [Tooltip("Global phase offset applied to all axes, measured in cycles.")]
    [SerializeField] private float phaseOffset = 0f;

    [Tooltip("Optional smoothing of the periodic sine-wave curve.")]
    [SerializeField, Range(0f, 1f)] private float periodicSmoothness = 0f;

    // =========================================================
    // RANDOMIZED BOBBING
    // =========================================================

    [Header("Randomized Bobbing")]
    [Tooltip("Random time taken to move between positions.")]
    [SerializeField] private Vector2 bobDuration = new Vector2(1f, 3f);

    [Tooltip("Random pause between movements.")]
    [SerializeField] private Vector2 pauseDuration = new Vector2(0f, 0.5f);

    [Tooltip("Randomize the initial destination of each axis.")]
    [SerializeField] private bool randomizeOnStart = true;

    [Tooltip("0 = linear movement, 1 = smooth ease-in/ease-out.")]
    [SerializeField, Range(0f, 1f)] private float movementSmoothness = 1f;

    // =========================================================
    // ROTATION
    // =========================================================

    [Header("Floating Rotation")]
    [Tooltip("Enables additional floating rotation.")]
    [SerializeField] private bool rotateWithBob = false;

    [Tooltip("When enabled, rotation follows the actual bobbing of the object.")]
    [SerializeField] private bool rotationFollowsBob = false;

    [Tooltip("Maximum local rotation in degrees.")]
    [SerializeField] private Vector3 rotationRange = new Vector3(2f, 0f, 2f);

    [Tooltip("How quickly rotation follows its target.")]
    [SerializeField, Min(0f)] private float rotationFollowSpeed = 3f;

    [Header("Independent Rotation")]
    [Tooltip("Frequency of independent rotation on each axis.")]
    [SerializeField] private Vector3 rotationFrequency = Vector3.one;

    [Tooltip("Independent rotation phase offsets, measured in cycles.")]
    [SerializeField] private Vector3 rotationPhase = Vector3.zero;

    // =========================================================
    // ACTIVATION
    // =========================================================

    [Header("Activation")]
    [SerializeField] private bool activateOnStart = true;

    [Tooltip("How quickly the effect fades in and out.")]
    [SerializeField, Min(0f)] private float activationSpeed = 2f;

    [Tooltip("Return to the original transform after deactivation.")]
    [SerializeField] private bool restoreOnDeactivate = true;

    // =========================================================
    // GENERAL SETTINGS
    // =========================================================

    [Header("General Settings")]
    [Tooltip("Use local position and rotation instead of world space.")]
    [SerializeField] private bool useLocalSpace = true;

    [Tooltip("Continue movement when Time.timeScale is zero.")]
    [SerializeField] private bool useUnscaledTime = false;

    // =========================================================
    // INTERNAL DATA
    // =========================================================

    private Vector3 originalPosition;
    private Quaternion originalRotation;

    private float elapsedTime;
    private float activationWeight;

    private bool isActive;
    private bool initialized;

    private AxisMotion xMotion;
    private AxisMotion yMotion;
    private AxisMotion zMotion;

    public bool IsActive => isActive;
    public float ActivationWeight => activationWeight;
    public bool IsRandomizedMotion => useRandomizedMotion;

    private class AxisMotion
    {
        public float current;
        public float start;
        public float target;

        public float elapsed;
        public float duration;
        public float pauseRemaining;

        public bool waiting;
    }

    // =========================================================
    // INITIALIZATION
    // =========================================================

    private void Awake()
    {
        Initialize();
    }

    private void Start()
    {
        if (activateOnStart)
            ActivateBob();
        else
            DeactivateBob();
    }

    private void Initialize()
    {
        if (initialized)
            return;

        originalPosition = useLocalSpace
            ? transform.localPosition
            : transform.position;

        originalRotation = useLocalSpace
            ? transform.localRotation
            : transform.rotation;

        xMotion = new AxisMotion();
        yMotion = new AxisMotion();
        zMotion = new AxisMotion();

        InitializeAxis(xMotion);
        InitializeAxis(yMotion);
        InitializeAxis(zMotion);

        initialized = true;
    }

    private void InitializeAxis(AxisMotion axis)
    {
        axis.current = 0f;
        axis.start = 0f;

        axis.target = randomizeOnStart
            ? Random.Range(-1f, 1f)
            : 0f;

        axis.elapsed = 0f;
        axis.duration = GetRandomDuration();
        axis.pauseRemaining = 0f;
        axis.waiting = false;
    }

    // =========================================================
    // UPDATE
    // =========================================================

    private void Update()
    {
        float dt = useUnscaledTime
            ? Time.unscaledDeltaTime
            : Time.deltaTime;

        if (isActive)
        {
            activationWeight = activationSpeed <= 0f
                ? 1f
                : Mathf.MoveTowards(
                    activationWeight,
                    1f,
                    activationSpeed * dt
                );

            // Shared time for periodic bobbing and rotation.
            elapsedTime += dt * bobSpeed;

            if (useRandomizedMotion && bobEnabled)
            {
                UpdateAxis(xMotion, dt);
                UpdateAxis(yMotion, dt);
                UpdateAxis(zMotion, dt);
            }
        }
        else if (restoreOnDeactivate)
        {
            activationWeight = activationSpeed <= 0f
                ? 0f
                : Mathf.MoveTowards(
                    activationWeight,
                    0f,
                    activationSpeed * dt
                );
        }

        if (isActive || (restoreOnDeactivate && activationWeight > 0f))
        {
            ApplyBobbing(dt);
        }
        else if (restoreOnDeactivate && !isActive)
        {
            SnapToOriginalTransform();
        }
    }

    // =========================================================
    // PERIODIC BOBBING
    // =========================================================

    private float GetPeriodicValue(float cycles)
    {
        float value = Mathf.Sin(cycles * Mathf.PI * 2f);

        if (periodicSmoothness > 0f)
        {
            float normalized = (value + 1f) * 0.5f;

            float smoothed = Mathf.SmoothStep(
                0f,
                1f,
                normalized
            );

            value = Mathf.Lerp(
                value,
                smoothed * 2f - 1f,
                periodicSmoothness
            );
        }

        return value;
    }

    // =========================================================
    // RANDOMIZED BOBBING
    // =========================================================

    private float GetRandomDuration()
    {
        float min = Mathf.Min(bobDuration.x, bobDuration.y);
        float max = Mathf.Max(bobDuration.x, bobDuration.y);

        return Mathf.Max(0.01f, Random.Range(min, max));
    }

    private float GetRandomPause()
    {
        float min = Mathf.Max(0f, Mathf.Min(
            pauseDuration.x,
            pauseDuration.y
        ));

        float max = Mathf.Max(
            pauseDuration.x,
            pauseDuration.y
        );

        return Random.Range(min, max);
    }

    private void UpdateAxis(AxisMotion axis, float dt)
    {
        float speed = bobSpeed;

        if (axis.waiting)
        {
            axis.pauseRemaining -= dt * speed;

            if (axis.pauseRemaining <= 0f)
                ChooseNewTarget(axis);

            return;
        }

        axis.elapsed += dt * speed;

        float t = axis.duration <= 0f
            ? 1f
            : Mathf.Clamp01(axis.elapsed / axis.duration);

        // Blend between linear and smooth movement.
        float smoothT = Mathf.Lerp(
            t,
            Mathf.SmoothStep(0f, 1f, t),
            movementSmoothness
        );

        axis.current = Mathf.Lerp(
            axis.start,
            axis.target,
            smoothT
        );

        if (t >= 1f)
        {
            axis.current = axis.target;
            axis.waiting = true;
            axis.pauseRemaining = GetRandomPause();
        }
    }

    private void ChooseNewTarget(AxisMotion axis)
    {
        axis.start = axis.current;
        axis.target = Random.Range(-1f, 1f);

        axis.elapsed = 0f;
        axis.duration = GetRandomDuration();

        axis.pauseRemaining = 0f;
        axis.waiting = false;
    }

    // =========================================================
    // GET BOB VALUES
    // =========================================================

    private Vector3 GetAxisValues()
    {
        if (!bobEnabled)
            return Vector3.zero;

        Vector3 values = Vector3.zero;

        if (bobX)
        {
            values.x = useRandomizedMotion
                ? xMotion.current
                : GetPeriodicValue(
                    elapsedTime * axisFrequency.x +
                    axisPhase.x +
                    phaseOffset
                );
        }

        if (bobY)
        {
            values.y = useRandomizedMotion
                ? yMotion.current
                : GetPeriodicValue(
                    elapsedTime * axisFrequency.y +
                    axisPhase.y +
                    phaseOffset
                );
        }

        if (bobZ)
        {
            values.z = useRandomizedMotion
                ? zMotion.current
                : GetPeriodicValue(
                    elapsedTime * axisFrequency.z +
                    axisPhase.z +
                    phaseOffset
                );
        }

        return values;
    }

    // =========================================================
    // APPLY BOBBING
    // =========================================================

    private void ApplyBobbing(float dt)
    {
        Vector3 axisValues = GetAxisValues();

        Vector3 offset = new Vector3(
            axisValues.x * bobRange.x,
            axisValues.y * bobRange.y,
            axisValues.z * bobRange.z
        );

        offset *= activationWeight;

        Vector3 targetPosition = originalPosition + offset;

        if (useLocalSpace)
            transform.localPosition = targetPosition;
        else
            transform.position = targetPosition;

        ApplyRotation(axisValues, dt);
    }

    // =========================================================
    // APPLY ROTATION
    // =========================================================

    private void ApplyRotation(Vector3 axisValues, float dt)
    {
        Quaternion targetRotation = originalRotation;

        if (rotateWithBob)
        {
            Vector3 rotationOffset;

            if (rotationFollowsBob)
            {
                // Rotation follows actual bob movement.
                // X movement tilts around Z.
                // Z movement tilts around X.
                // Y movement creates subtle yaw.

                rotationOffset = new Vector3(
                    axisValues.z * rotationRange.x,
                    axisValues.y * rotationRange.y,
                    -axisValues.x * rotationRange.z
                );
            }
            else
            {
                // Independent periodic floating rotation.
                // Preserves separate frequency and phase controls.

                rotationOffset = new Vector3(
                    GetPeriodicValue(
                        elapsedTime * rotationFrequency.x +
                        rotationPhase.x
                    ) * rotationRange.x,

                    GetPeriodicValue(
                        elapsedTime * rotationFrequency.y +
                        rotationPhase.y
                    ) * rotationRange.y,

                    GetPeriodicValue(
                        elapsedTime * rotationFrequency.z +
                        rotationPhase.z
                    ) * rotationRange.z
                );
            }

            rotationOffset *= activationWeight;

            targetRotation =
                originalRotation * Quaternion.Euler(rotationOffset);
        }

        Quaternion currentRotation = useLocalSpace
            ? transform.localRotation
            : transform.rotation;

        float blend = rotationFollowSpeed <= 0f
            ? 1f
            : 1f - Mathf.Exp(-rotationFollowSpeed * dt);

        Quaternion result = Quaternion.Slerp(
            currentRotation,
            targetRotation,
            blend
        );

        SetRotation(result);
    }

    private void SetRotation(Quaternion rotation)
    {
        if (useLocalSpace)
            transform.localRotation = rotation;
        else
            transform.rotation = rotation;
    }

    // =========================================================
    // TRANSFORM RESTORATION
    // =========================================================

    private void SnapToOriginalTransform()
    {
        if (useLocalSpace)
        {
            transform.localPosition = originalPosition;
            transform.localRotation = originalRotation;
        }
        else
        {
            transform.position = originalPosition;
            transform.rotation = originalRotation;
        }
    }

    // =========================================================
    // PUBLIC FUNCTIONS
    // =========================================================

    public void ActivateBob()
    {
        Initialize();
        isActive = true;
    }

    public void DeactivateBob()
    {
        isActive = false;
    }

    public void SetBobEnabled(bool enabled)
    {
        bobEnabled = enabled;
    }

    public void SetBobAxes(bool x, bool y, bool z)
    {
        bobX = x;
        bobY = y;
        bobZ = z;
    }

    public void SetRotationEnabled(bool enabled)
    {
        rotateWithBob = enabled;
    }

    public void SetRotationFollowsBob(bool enabled)
    {
        rotationFollowsBob = enabled;
    }

    public void SetRandomizedMotion(bool enabled)
    {
        if (useRandomizedMotion == enabled)
            return;

        useRandomizedMotion = enabled;

        if (enabled)
            RandomizeBob();
    }

    public void SetBobSpeed(float speed)
    {
        bobSpeed = Mathf.Max(0f, speed);
    }

    public void SetBobRange(Vector3 range)
    {
        bobRange = range;
    }

    public void SetPhaseOffset(float phase)
    {
        phaseOffset = phase;
    }

    public void SetAxisPhase(Vector3 phase)
    {
        axisPhase = phase;
    }

    public void SetAxisFrequency(Vector3 frequency)
    {
        axisFrequency = frequency;
    }

    public void SetRotationPhase(Vector3 phase)
    {
        rotationPhase = phase;
    }

    public void SetRotationFrequency(Vector3 frequency)
    {
        rotationFrequency = frequency;
    }

    public void SetOriginalTransform()
    {
        originalPosition = useLocalSpace
            ? transform.localPosition
            : transform.position;

        originalRotation = useLocalSpace
            ? transform.localRotation
            : transform.rotation;
    }

    public void RandomizeBob()
    {
        Initialize();

        ChooseNewTarget(xMotion);
        ChooseNewTarget(yMotion);
        ChooseNewTarget(zMotion);
    }

    public void ResetBobTime()
    {
        elapsedTime = 0f;

        if (useRandomizedMotion)
            RandomizeBob();
    }
}
