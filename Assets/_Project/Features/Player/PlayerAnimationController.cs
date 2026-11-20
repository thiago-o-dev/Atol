using System.Collections.Generic;
using Assets._Project.Features.Movement;
using UnityEngine;

/// <summary>
/// Drives HuldaAnimatorController from actual movement velocity. The controller
/// owns animation only; movement state and physics remain in PlayerController.
/// Ground falls are qualified after a short continuous descent so tiny steps
/// and grounding flicker do not trigger the fall/landing animation.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerAnimationController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController player;
    [SerializeField] private Animator animator;

    [Header("HuldaAnimatorController Parameters")]
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string directionParameter = "Direction";
    [SerializeField] private string movementModeParameter = "MovementMode";
    [SerializeField] private string verticalSpeedParameter = "VerticalSpeed";
    [SerializeField] private string groundedParameter = "Grounded";
    [SerializeField] private string jumpingParameter = "Jumping";
    [SerializeField] private string fallingParameter = "Falling";
    [SerializeField] private string fallDurationParameter = "FallDuration";
    [SerializeField] private string swimmingParameter = "Swimming";
    [SerializeField] private string boatParameter = "InBoat";
    [SerializeField] private string backwardParameter = "Backward";
    [SerializeField] private string jumpTriggerParameter = "Jump";
    [SerializeField] private string landTriggerParameter = "Land";
    [SerializeField] private string swimHopTriggerParameter = "SwimHop";
    [SerializeField] private string swimLandTriggerParameter = "SwimLand";
    [SerializeField] private string boatDodgeTriggerParameter = "BoatDodge";

    [Header("Smoothing")]
    [Min(0f)] [SerializeField] private float speedDampTime = 0.12f;
    [Min(0f)] [SerializeField] private float directionDampTime = 0.10f;
    [Min(0f)] [SerializeField] private float verticalSpeedDampTime = 0.08f;

    [Header("Ground Fall Detection")]
    [Tooltip("A grounded fall must be continuously descending for this long before the fall animation starts.")]
    [Min(0.01f)] [SerializeField] private float fallDetectionDelay = 0.2f;
    [Tooltip("Ignore tiny negative CharacterController velocity caused by grounding noise.")]
    [Min(0f)] [SerializeField] private float minimumFallingSpeed = 0.08f;
    [Tooltip("Extra distance below the CharacterController foot used to recognize downhill/stair support and ignore grounding flicker.")]
    [Min(0f)] [SerializeField] private float groundSupportIgnoreDistance = 0.35f;
    [Tooltip("Layers that count as solid ground for the downhill support probe. Exclude the player and water surfaces.")]
    [SerializeField] private LayerMask groundSupportLayers = ~0;
    [Tooltip("FallDuration is measured in seconds and clamped to this value for the landing blend tree.")]
    [Min(0.21f)] [SerializeField] private float maximumFallDurationForBlend = 1.5f;

    [Header("Boat Animation")]
    [Min(0.1f)] [SerializeField] private float boatMaxAnimationSpeed = 12f;
    [Tooltip("Trigger the sitting dodge once when the boat crosses this planar speed.")]
    [Min(0.1f)] [SerializeField] private float boatDodgeSpeedThreshold = 8f;
    [Tooltip("The dodge can trigger again after speed falls below this fraction of its threshold.")]
    [Range(0.1f, 0.95f)] [SerializeField] private float boatDodgeRearmFraction = 0.55f;

    [Header("State Detection")]
    [Min(0f)] [SerializeField] private float airborneThreshold = 0.1f;
    [Range(0f, 1f)] [SerializeField] private float movementDeadZone = 0.04f;
    [Range(0f, 1f)] [SerializeField] private float backwardThreshold = 0.12f;

    private readonly HashSet<int> availableParameters = new HashSet<int>();
    private Animator cachedAnimator;
    private GroundedMovement subscribedGroundedMovement;
    private bool pendingGroundJump;
    private bool jumpTriggerSentByGroundEvent;
    private bool wasJumping;
    private bool wasSwimmingAirborne;
    private bool wasGrounded;
    private bool boatDodgeArmed = true;
    private float groundedFallDuration;
    private bool groundedFallQualified;

    private const int GroundedMode = 0;
    private const int SwimmingMode = 1;
    private const int BoatMode = 2;

    private void Awake()
    {
        ResolveReferences();
        CacheAnimatorParameters();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheAnimatorParameters();
        BindGroundJumpEvent();
        ResetFallTracking();
        pendingGroundJump = false;
        jumpTriggerSentByGroundEvent = false;
        wasJumping = false;
        wasSwimmingAirborne = false;
        wasGrounded = false;
        boatDodgeArmed = true;
    }

    private void OnDisable()
    {
        UnbindGroundJumpEvent();
    }

    private void OnValidate()
    {
        fallDetectionDelay = Mathf.Max(0.01f, fallDetectionDelay);
        minimumFallingSpeed = Mathf.Max(0f, minimumFallingSpeed);
        maximumFallDurationForBlend = Mathf.Max(fallDetectionDelay + 0.01f, maximumFallDurationForBlend);
    }

    private void Update()
    {
        ResolveReferences();
        if (animator == null || player == null)
            return;

        if (cachedAnimator != animator)
            CacheAnimatorParameters();

        UpdateAnimatorState();
    }

    private void ResolveReferences()
    {
        if (player == null)
            player = GetComponent<PlayerController>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        BindGroundJumpEvent();
    }

    private void BindGroundJumpEvent()
    {
        GroundedMovement target = player != null ? player.GroundedMovement : null;
        if (subscribedGroundedMovement == target)
            return;

        UnbindGroundJumpEvent();
        subscribedGroundedMovement = target;
        if (subscribedGroundedMovement != null)
            subscribedGroundedMovement.JumpStarted += OnGroundJumpStarted;
    }

    private void UnbindGroundJumpEvent()
    {
        if (subscribedGroundedMovement != null)
            subscribedGroundedMovement.JumpStarted -= OnGroundJumpStarted;
        subscribedGroundedMovement = null;
    }

    private void OnGroundJumpStarted()
    {
        // A coyote jump may be accepted while the animator is already in its
        // landing/falling state. Clear the old fall qualification immediately
        // and queue the jump transition for the next animation update.
        ResetFallTracking();
        pendingGroundJump = true;
        wasGrounded = false;
        wasJumping = false;

        if (animator != null)
        {
            if (HasParameter(landTriggerParameter))
                animator.ResetTrigger(Animator.StringToHash(landTriggerParameter));

            if (HasParameter(jumpTriggerParameter))
            {
                int jumpHash = Animator.StringToHash(jumpTriggerParameter);
                animator.ResetTrigger(jumpHash);
                animator.SetTrigger(jumpHash);
                jumpTriggerSentByGroundEvent = true;
            }
        }
    }

    private static bool IsSameMovement(IMovement current, UnityEngine.Object expected)
    {
        return (current as UnityEngine.Object) == expected;
    }

    private void CacheAnimatorParameters()
    {
        availableParameters.Clear();
        cachedAnimator = animator;
        if (animator == null)
            return;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
            availableParameters.Add(parameters[i].nameHash);
    }

    private void UpdateAnimatorState()
    {
        IMovement activeMovement = player.ActiveMovement;
        bool inBoat = player.IsAboardBoat ||
                      (player.BoatMovement != null && IsSameMovement(activeMovement, player.BoatMovement));
        bool swimming = !inBoat && player.SwimmingMovement != null &&
                        IsSameMovement(activeMovement, player.SwimmingMovement);
        bool groundedMode = !inBoat && !swimming && player.GroundedMovement != null &&
                            IsSameMovement(activeMovement, player.GroundedMovement);

        Vector3 worldVelocity = Vector3.zero;
        float verticalSpeed = 0f;
        float maximumSpeed = 1f;
        bool grounded = false;
        bool jumping = false;
        bool falling = false;
        bool swimmingAirborne = false;
        int movementMode = GroundedMode;

        if (groundedMode)
        {
            GroundedMovement movement = player.GroundedMovement;
            worldVelocity = movement.PlanarVelocity;
            maximumSpeed = Mathf.Max(0.1f, movement.Speed);
            CharacterController controller = movement.Controller != null
                ? movement.Controller
                : player.CharacterController;

            if (controller != null && controller.enabled)
            {
                bool actualGrounded = controller.isGrounded;
                verticalSpeed = movement.VerticalVelocity;
                if (Mathf.Abs(verticalSpeed) < 0.01f)
                    verticalSpeed = controller.velocity.y;

                bool nearGroundSupport = !actualGrounded &&
                    verticalSpeed <= -minimumFallingSpeed &&
                    movement.HasNearbyGroundSupport(groundSupportIgnoreDistance, groundSupportLayers);

                if (pendingGroundJump)
                {
                    // An accepted coyote jump must take precedence over a qualified
                    // fall/landing from the previous frame.
                    ResetFallTracking();
                    wasGrounded = false;
                    grounded = false;
                    jumping = true;
                    pendingGroundJump = false;
                }
                else
                {
                    UpdateGroundFallTracking(actualGrounded, nearGroundSupport, verticalSpeed);
                    grounded = actualGrounded || (nearGroundSupport && !groundedFallQualified);
                    jumping = !grounded && !groundedFallQualified &&
                              verticalSpeed > airborneThreshold;
                    falling = !grounded && groundedFallQualified;
                }
            }
            else
            {
                ResetFallTracking();
                grounded = false;
                jumping = false;
                falling = false;
                pendingGroundJump = false;
            }
        }
        else
        {
            // A fall that began in water is owned by SwimmingMovement until the
            // PlayerController confirms solid-ground contact and changes mode.
            ResetFallTracking();
            pendingGroundJump = false;
            wasGrounded = false;

            if (swimming)
            {
                SwimmingMovement movement = player.SwimmingMovement;
                worldVelocity = movement.PlanarVelocity;
                verticalSpeed = movement.VerticalVelocity;
                maximumSpeed = Mathf.Max(0.1f, movement.Speed);

                bool waveAirborne = movement.IsWaveAirborne;
                swimmingAirborne = movement.IsAirborne || waveAirborne;
                jumping = movement.IsAirborne && verticalSpeed > airborneThreshold;
                // A wave ejection enters the dedicated swimming-fall pose even if
                // the wave is still lifting the character for a few frames.
                falling = waveAirborne || (movement.IsAirborne && verticalSpeed <= airborneThreshold);
                movementMode = SwimmingMode;
            }
            else if (inBoat)
            {
                if (player.CurrentBoat != null && player.CurrentBoat.Body != null)
                    worldVelocity = Vector3.ProjectOnPlane(player.CurrentBoat.Body.linearVelocity, Vector3.up);
                maximumSpeed = boatMaxAnimationSpeed;
                movementMode = BoatMode;
            }
        }

        Vector3 localVelocity = transform.InverseTransformDirection(
            Vector3.ProjectOnPlane(worldVelocity, Vector3.up));
        float planarSpeed = localVelocity.magnitude;
        float normalizedSpeed = Mathf.Clamp01(planarSpeed / maximumSpeed);
        float normalizedDirection = planarSpeed > 0.001f
            ? Mathf.Clamp(localVelocity.x / planarSpeed, -1f, 1f)
            : 0f;
        bool movingBackwards = groundedMode && normalizedSpeed > movementDeadZone &&
                               localVelocity.z / maximumSpeed < -backwardThreshold;

        SetInteger(movementModeParameter, movementMode);
        SetFloat(speedParameter, normalizedSpeed, speedDampTime);
        SetFloat(directionParameter, normalizedDirection, directionDampTime);
        SetFloat(verticalSpeedParameter, verticalSpeed, verticalSpeedDampTime);
        SetBoolean(groundedParameter, grounded);
        SetBoolean(jumpingParameter, jumping);
        SetBoolean(fallingParameter, falling);
        SetBoolean(swimmingParameter, swimming);
        SetBoolean(boatParameter, inBoat);
        SetBoolean(backwardParameter, movingBackwards);

        if (jumping && !wasJumping)
        {
            if (swimming)
                SetTrigger(swimHopTriggerParameter);
            else if (groundedMode)
            {
                if (!jumpTriggerSentByGroundEvent)
                    SetTrigger(jumpTriggerParameter);
                jumpTriggerSentByGroundEvent = false;
            }
        }

        // SwimLand is used only when a hop/wave ejection returns to the water.
        // If the swimmer has reached dry ground, the swim state's "Leave swimming"
        // transition takes over instead.
        if (swimming && wasSwimmingAirborne && !swimmingAirborne)
            SetTrigger(swimLandTriggerParameter);

        UpdateBoatDodge(inBoat, planarSpeed);
        wasJumping = jumping;
        wasSwimmingAirborne = swimmingAirborne;
    }

    private void UpdateGroundFallTracking(bool actualGrounded, bool nearGroundSupport, float verticalSpeed)
    {
        if (actualGrounded)
        {
            // Only a fall that already qualified may play the landing animation.
            // A short hop or one-frame grounding flicker returns to locomotion.
            bool landedFromQualifiedFall = !wasGrounded && groundedFallQualified;
            if (landedFromQualifiedFall)
            {
                float blendDuration = Mathf.Clamp(
                    groundedFallDuration, fallDetectionDelay, maximumFallDurationForBlend);
                SetFloatImmediate(fallDurationParameter, blendDuration);
                SetTrigger(landTriggerParameter);
            }

            groundedFallDuration = 0f;
            groundedFallQualified = false;
            wasGrounded = true;
            return;
        }

        // If a downhill/stair step momentarily clears CharacterController.isGrounded,
        // terrain still close beneath the capsule means this is support following,
        // not a genuine airborne fall. Once a fall has qualified, do not use this
        // shortcut: it must continue into its real landing.
        if (nearGroundSupport && !groundedFallQualified)
        {
            groundedFallDuration = 0f;
            wasGrounded = true;
            return;
        }

        if (verticalSpeed < -minimumFallingSpeed)
        {
            groundedFallDuration += Time.deltaTime;
            if (groundedFallDuration >= fallDetectionDelay)
                groundedFallQualified = true;
        }
        else if (verticalSpeed > airborneThreshold && !groundedFallQualified)
        {
            // Rising velocity (including a normal/coyote jump) starts a fresh arc.
            groundedFallDuration = 0f;
        }

        wasGrounded = false;
    }

    private void ResetFallTracking()
    {
        groundedFallDuration = 0f;
        groundedFallQualified = false;
    }

    private void UpdateBoatDodge(bool inBoat, float planarSpeed)
    {
        if (!inBoat)
        {
            boatDodgeArmed = true;
            return;
        }

        if (planarSpeed <= boatDodgeSpeedThreshold * boatDodgeRearmFraction)
            boatDodgeArmed = true;

        if (boatDodgeArmed && planarSpeed >= boatDodgeSpeedThreshold)
        {
            SetTrigger(boatDodgeTriggerParameter);
            boatDodgeArmed = false;
        }
    }

    private bool HasParameter(string parameterName)
    {
        return !string.IsNullOrWhiteSpace(parameterName) &&
               availableParameters.Contains(Animator.StringToHash(parameterName));
    }

    private void SetFloat(string parameterName, float value, float dampTime)
    {
        if (HasParameter(parameterName))
            animator.SetFloat(Animator.StringToHash(parameterName), value, dampTime, Time.deltaTime);
    }

    private void SetFloatImmediate(string parameterName, float value)
    {
        if (HasParameter(parameterName))
            animator.SetFloat(Animator.StringToHash(parameterName), value);
    }

    private void SetBoolean(string parameterName, bool value)
    {
        if (HasParameter(parameterName))
            animator.SetBool(Animator.StringToHash(parameterName), value);
    }

    private void SetInteger(string parameterName, int value)
    {
        if (HasParameter(parameterName))
            animator.SetInteger(Animator.StringToHash(parameterName), value);
    }

    private void SetTrigger(string parameterName)
    {
        if (HasParameter(parameterName))
            animator.SetTrigger(Animator.StringToHash(parameterName));
    }
}
