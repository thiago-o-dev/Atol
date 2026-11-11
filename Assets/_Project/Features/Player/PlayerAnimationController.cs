using System.Collections.Generic;
using Assets._Project.Features.Movement;
using UnityEngine;

/// <summary>
/// Bridges the active movement mode to an Animator. It deliberately owns no
/// movement logic: locomotion, jump/fall/landing, swimming and boat states are
/// derived from the movement components and exposed as Animator parameters.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerAnimationController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController player;
    [SerializeField] private Animator animator;

    [Header("Animator Parameters")]
    [Tooltip("Optional integer: 0 = grounded, 1 = swimming, 2 = boat.")]
    [SerializeField] private string movementModeParameter = "MovementMode";
    [SerializeField] private string moveSpeedParameter = "MoveSpeed";
    [SerializeField] private string moveXParameter = "MoveX";
    [SerializeField] private string moveYParameter = "MoveY";
    [SerializeField] private string verticalSpeedParameter = "VerticalSpeed";
    [SerializeField] private string groundedParameter = "Grounded";
    [SerializeField] private string jumpingParameter = "Jumping";
    [SerializeField] private string fallingParameter = "Falling";
    [SerializeField] private string swimmingParameter = "Swimming";
    [SerializeField] private string boatParameter = "InBoat";
    [SerializeField] private string jumpTriggerParameter = "Jump";
    [SerializeField] private string landTriggerParameter = "Land";
    [SerializeField] private string swimHopTriggerParameter = "SwimHop";
    [SerializeField] private string swimLandTriggerParameter = "SwimLand";

    [Header("Smoothing")]
    [Min(0f)] [SerializeField] private float speedDampTime = 0.12f;
    [Min(0f)] [SerializeField] private float directionDampTime = 0.1f;
    [Min(0f)] [SerializeField] private float verticalSpeedDampTime = 0.08f;
    [Min(0.1f)] [SerializeField] private float boatMaxAnimationSpeed = 8f;
    [Min(0f)] [SerializeField] private float airborneThreshold = 0.1f;

    private readonly HashSet<int> availableParameters = new HashSet<int>();
    private Animator cachedAnimator;
    private bool wasJumping;
    private bool wasAirborne;

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
        wasJumping = false;
        wasAirborne = false;
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

        // Allows the visual rig/Animator to be added under the player after this
        // component has been configured, without requiring a second reference edit.
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);
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
                      (player.BoatMovement != null && activeMovement == player.BoatMovement);
        bool swimming = !inBoat && player.SwimmingMovement != null &&
                        activeMovement == player.SwimmingMovement;
        bool groundedMode = !inBoat && !swimming && player.GroundedMovement != null &&
                            activeMovement == player.GroundedMovement;

        Vector3 worldVelocity = Vector3.zero;
        float verticalSpeed = 0f;
        float maximumSpeed = 1f;
        bool grounded = false;
        bool jumping = false;
        bool falling = false;
        int movementMode = GroundedMode;

        if (groundedMode && player.GroundedMovement != null)
        {
            GroundedMovement movement = player.GroundedMovement;
            worldVelocity = movement.Velocity;
            verticalSpeed = movement.VerticalVelocity;
            grounded = movement.IsGrounded;
            jumping = !grounded && verticalSpeed > airborneThreshold;
            falling = !grounded && !jumping;
            maximumSpeed = Mathf.Max(0.1f, movement.Speed);
            movementMode = GroundedMode;
        }
        else if (swimming && player.SwimmingMovement != null)
        {
            SwimmingMovement movement = player.SwimmingMovement;
            worldVelocity = movement.PlanarVelocity;
            verticalSpeed = movement.VerticalVelocity;
            jumping = movement.IsAirborne && verticalSpeed > airborneThreshold;
            falling = movement.IsAirborne && verticalSpeed <= airborneThreshold;
            maximumSpeed = Mathf.Max(0.1f, movement.Speed);
            movementMode = SwimmingMode;
        }
        else if (inBoat && player.BoatMovement != null)
        {
            worldVelocity = player.BoatMovement.AnimationPlanarVelocity;
            maximumSpeed = boatMaxAnimationSpeed;
            movementMode = BoatMode;
        }

        Vector3 localVelocity = transform.InverseTransformDirection(
            Vector3.ProjectOnPlane(worldVelocity, Vector3.up));
        float normalizedSpeed = Mathf.Clamp01(localVelocity.magnitude / maximumSpeed);
        float normalizedMoveX = Mathf.Clamp(localVelocity.x / maximumSpeed, -1f, 1f);
        float normalizedMoveY = Mathf.Clamp(localVelocity.z / maximumSpeed, -1f, 1f);
        bool airborne = jumping || falling;

        SetInteger(movementModeParameter, movementMode);
        SetFloat(moveSpeedParameter, normalizedSpeed, speedDampTime);
        SetFloat(moveXParameter, normalizedMoveX, directionDampTime);
        SetFloat(moveYParameter, normalizedMoveY, directionDampTime);
        SetFloat(verticalSpeedParameter, verticalSpeed, verticalSpeedDampTime);
        SetBoolean(groundedParameter, grounded);
        SetBoolean(jumpingParameter, jumping);
        SetBoolean(fallingParameter, falling);
        SetBoolean(swimmingParameter, swimming);
        SetBoolean(boatParameter, inBoat);

        if (jumping && !wasJumping)
        {
            SetTrigger(jumpTriggerParameter);
            if (swimming)
                SetTrigger(swimHopTriggerParameter);
        }

        if (wasAirborne && !airborne)
        {
            SetTrigger(landTriggerParameter);
            if (swimming)
                SetTrigger(swimLandTriggerParameter);
        }

        wasJumping = jumping;
        wasAirborne = airborne;
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

    private void SetTrigger(string parameterName)
    {
        if (HasParameter(parameterName))
            animator.SetTrigger(Animator.StringToHash(parameterName));
    }
}
