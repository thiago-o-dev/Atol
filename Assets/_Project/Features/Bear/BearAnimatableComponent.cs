using System.Collections.Generic;
using Assets._Project.Features.Bear;
using UnityEngine;

/// <summary>
/// Drives the bear's Animator from its real movement and from BearController events.
/// Same idea as PlayerAnimationController: the animator owns animation only, while
/// movement, AI and damage stay in GroundedMovement and BearController.
/// Missing animator parameters are skipped, so the controller can be built up step by step.
/// </summary>
[DisallowMultipleComponent]
public sealed class BearAnimatableComponent : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BearController bear;
    [SerializeField] private Animator animator;

    [Header("Animator Parameters")]
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string verticalSpeedParameter = "VerticalSpeed";
    [SerializeField] private string groundedParameter = "Grounded";
    [SerializeField] private string jumpingParameter = "Jumping";
    [SerializeField] private string fallingParameter = "Falling";
    [SerializeField] private string aggroParameter = "Aggro";
    [SerializeField] private string chargingParameter = "Charging";
    [SerializeField] private string stunnedParameter = "Stunned";
    [SerializeField] private string deadParameter = "Dead";
    [SerializeField] private string clawVariantParameter = "ClawVariant";
    [SerializeField] private string roarTriggerParameter = "Roar";
    [SerializeField] private string clawTriggerParameter = "Claw";
    [SerializeField] private string jumpWindupTriggerParameter = "JumpWindup";
    [SerializeField] private string ramWindupTriggerParameter = "RamWindup";
    [SerializeField] private string jumpTriggerParameter = "Jump";
    [SerializeField] private string landTriggerParameter = "Land";
    [SerializeField] private string hitTriggerParameter = "Hit";

    [Header("Smoothing")]
    [Min(0f)] [SerializeField] private float speedDampTime = 0.12f;
    [Min(0f)] [SerializeField] private float verticalSpeedDampTime = 0.08f;

    [Header("Air Detection")]
    [Tooltip("Must be airborne and descending for this long before the falling pose starts. Filters out steps and grounding flicker.")]
    [Min(0.01f)] [SerializeField] private float fallDetectionDelay = 0.2f;
    [Tooltip("Ignore tiny negative CharacterController velocity caused by grounding noise.")]
    [Min(0f)] [SerializeField] private float minimumFallingSpeed = 0.08f;
    [Tooltip("Vertical speed above which the bear counts as rising (jumping).")]
    [Min(0f)] [SerializeField] private float airborneThreshold = 0.1f;
    [Tooltip("Shortest time in the air that plays the landing animation.")]
    [Min(0f)] [SerializeField] private float minimumAirTimeForLanding = 0.25f;

    private readonly HashSet<int> availableParameters = new HashSet<int>();
    private Animator cachedAnimator;
    private GroundedMovement subscribedMovement;
    private BearController subscribedBear;

    private bool wasGrounded = true;
    private bool pendingJump;
    private float airTime;
    private float fallingTime;
    private bool fallQualified;

    private void Awake()
    {
        ResolveReferences();
        CacheAnimatorParameters();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheAnimatorParameters();
        BindEvents();

        wasGrounded = true;
        pendingJump = false;
        airTime = 0f;
        ResetFallTracking();
    }

    private void OnDisable()
    {
        UnbindEvents();
    }

    private void OnValidate()
    {
        fallDetectionDelay = Mathf.Max(0.01f, fallDetectionDelay);
        minimumFallingSpeed = Mathf.Max(0f, minimumFallingSpeed);
    }

    private void Update()
    {
        ResolveReferences();

        if (animator == null || bear == null)
            return;

        if (cachedAnimator != animator)
            CacheAnimatorParameters();

        UpdateAnimatorState();
    }

    private void ResolveReferences()
    {
        if (bear == null)
            bear = GetComponent<BearController>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        BindEvents();
    }

    private void BindEvents()
    {
        if (subscribedBear != bear)
        {
            UnbindBearEvents();
            subscribedBear = bear;

            if (subscribedBear != null)
            {
                subscribedBear.Alerted += OnAlerted;
                subscribedBear.AttackStarted += OnAttackStarted;
                subscribedBear.Staggered += OnStaggered;
            }
        }

        GroundedMovement movement = bear != null ? bear.Movement : null;

        if (subscribedMovement != movement)
        {
            UnbindMovementEvents();
            subscribedMovement = movement;

            if (subscribedMovement != null)
                subscribedMovement.JumpStarted += OnJumpStarted;
        }
    }

    private void UnbindEvents()
    {
        UnbindBearEvents();
        UnbindMovementEvents();
    }

    private void UnbindBearEvents()
    {
        if (subscribedBear != null)
        {
            subscribedBear.Alerted -= OnAlerted;
            subscribedBear.AttackStarted -= OnAttackStarted;
            subscribedBear.Staggered -= OnStaggered;
        }

        subscribedBear = null;
    }

    private void UnbindMovementEvents()
    {
        if (subscribedMovement != null)
            subscribedMovement.JumpStarted -= OnJumpStarted;

        subscribedMovement = null;
    }

    private void OnAlerted()
    {
        // Damage while patrolling can raise Hit immediately before the AI enters Alert.
        // The roar is the intended response in that case; do not leave a stale Hit trigger queued.
        ResetTrigger(hitTriggerParameter);
        SetTrigger(roarTriggerParameter);
    }

    private void OnStaggered()
    {
        // A hit reaction must not cut into a wind-up or a charge; the AI keeps its super armor.
        if (bear.CurrentState is BearController.State.Patrol or BearController.State.Alert or BearController.State.Chase)
            SetTrigger(hitTriggerParameter);
    }

    private void OnAttackStarted(BearAttack attack)
    {
        switch (attack)
        {
            case BearAttack.Claw:
                SetInteger(clawVariantParameter, bear.ClawVariant);
                SetTrigger(clawTriggerParameter);
                break;
            case BearAttack.Jump:
                SetTrigger(jumpWindupTriggerParameter);
                break;
            case BearAttack.Ram:
                SetTrigger(ramWindupTriggerParameter);
                break;
        }
    }

    private void OnJumpStarted()
    {
        // Same handling as the player: the take-off is queued here and the air state is
        // read on the next animation update, so a stale fall pose cannot win.
        ResetFallTracking();
        pendingJump = true;
        wasGrounded = false;
        airTime = 0f;

        ResetTrigger(landTriggerParameter);
        SetTrigger(jumpTriggerParameter);
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
        GroundedMovement movement = bear.Movement;

        float verticalSpeed = 0f;
        bool grounded = true;
        bool jumping = false;
        bool falling = false;

        if (movement != null)
        {
            CharacterController controller = movement.Controller;

            if (controller != null && controller.enabled)
            {
                bool actualGrounded = controller.isGrounded;
                verticalSpeed = movement.VerticalVelocity;

                if (Mathf.Abs(verticalSpeed) < 0.01f)
                    verticalSpeed = controller.velocity.y;

                if (pendingJump)
                {
                    ResetFallTracking();
                    grounded = false;
                    jumping = true;
                    pendingJump = false;
                }
                else
                {
                    UpdateAirTracking(actualGrounded, verticalSpeed);
                    grounded = actualGrounded;
                    jumping = !grounded && !fallQualified && verticalSpeed > airborneThreshold;
                    falling = !grounded && fallQualified;
                }
            }
        }

        float referenceSpeed = Mathf.Max(0.1f, bear.ReferenceSpeed);
        float planarSpeed = movement != null ? movement.PlanarVelocity.magnitude : 0f;

        SetFloat(speedParameter, Mathf.Clamp01(planarSpeed / referenceSpeed), speedDampTime);
        SetFloat(verticalSpeedParameter, verticalSpeed, verticalSpeedDampTime);
        SetBoolean(groundedParameter, grounded);
        SetBoolean(jumpingParameter, jumping);
        SetBoolean(fallingParameter, falling);
        SetBoolean(aggroParameter, bear.IsAggro);
        SetBoolean(chargingParameter, bear.IsCharging);
        SetBoolean(stunnedParameter, bear.IsStunned);
        SetBoolean(deadParameter, bear.IsDead);
    }

    private void UpdateAirTracking(bool actualGrounded, float verticalSpeed)
    {
        if (actualGrounded)
        {
            // Only a real time in the air plays the landing. A step or one-frame flicker does not.
            if (!wasGrounded && airTime >= minimumAirTimeForLanding)
                SetTrigger(landTriggerParameter);

            airTime = 0f;
            wasGrounded = true;
            ResetFallTracking();
            return;
        }

        wasGrounded = false;
        airTime += Time.deltaTime;

        if (verticalSpeed < -minimumFallingSpeed)
        {
            fallingTime += Time.deltaTime;

            if (fallingTime >= fallDetectionDelay)
                fallQualified = true;
        }
        else if (verticalSpeed > airborneThreshold && !fallQualified)
        {
            // Match the player controller: a new upward arc resets an unqualified descent,
            // so small hops/vertical corrections cannot accumulate into a false fall.
            fallingTime = 0f;
        }
    }

    private void ResetFallTracking()
    {
        fallingTime = 0f;
        fallQualified = false;
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

    private void ResetTrigger(string parameterName)
    {
        if (animator != null && HasParameter(parameterName))
            animator.ResetTrigger(Animator.StringToHash(parameterName));
    }

    private void SetTrigger(string parameterName)
    {
        if (animator == null || !HasParameter(parameterName))
            return;

        int hash = Animator.StringToHash(parameterName);
        // A previously queued trigger must never fire later after an interruption.
        animator.ResetTrigger(hash);
        animator.SetTrigger(hash);
    }
}
