using Assets._Project.Features.Boat;
using Assets._Project.Features.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Routes player input to the currently active IMovement implementation.
/// Supports grounded movement, swimming and controlling a boarded boat.
/// </summary>
public sealed class PlayerController : MonoBehaviour
{
    [Header("Movement Types")]
    public GroundedMovement GroundedMovement;
    public SwimmingMovement SwimmingMovement;
    public BoatMovement BoatMovement;

    [Header("Input Actions")]
    public InputActionReference MovementAction;
    public InputActionReference JumpAction;
    [Tooltip("Optional: hold to swim downward. JumpAction swims upward while held.")]
    public InputActionReference DiveAction;

    [Header("Boat Boarding")]
    [Tooltip("Optional CharacterController reference. If empty, one is searched on this object.")]
    public CharacterController CharacterController;

    private IMovement activeMovement;
    private Vector2 latestDirection;
    private Transform previousParent;
    private bool characterControllerWasEnabled;
    private bool isAboardBoat;

    public IMovement ActiveMovement => activeMovement;
    public bool IsAboardBoat => isAboardBoat;

    private void Awake()
    {
        if (CharacterController == null)
            CharacterController = GetComponent<CharacterController>();

        if (GroundedMovement != null)
            activeMovement = GroundedMovement;
        else if (SwimmingMovement != null)
            activeMovement = SwimmingMovement;
        else
            activeMovement = BoatMovement;
    }

    private void OnEnable()
    {
        Subscribe(MovementAction, OnMoved);
        Subscribe(JumpAction, OnJumped);
        Subscribe(DiveAction, OnDiveChanged);
        EnableAction(MovementAction);
        EnableAction(JumpAction);
        EnableAction(DiveAction);
    }

    private void FixedUpdate()
    {
        if (activeMovement == null)
            return;

        activeMovement.Move(latestDirection);
        activeMovement.ApplyMovement();
    }

    private void OnDisable()
    {
        Unsubscribe(MovementAction, OnMoved);
        Unsubscribe(JumpAction, OnJumped);
        Unsubscribe(DiveAction, OnDiveChanged);
        DisableAction(MovementAction);
        DisableAction(JumpAction);
        DisableAction(DiveAction);

        // Stop the boat safely but retain the assigned boat in case this controller
        // is temporarily disabled and later re-enabled while still aboard.
        if (BoatMovement != null)
            BoatMovement.StopDriving();
    }

    /// <summary>Switches to a configured movement implementation.</summary>
    public bool SetActiveMovement(IMovement movement)
    {
        if (movement == null)
            return false;

        if (activeMovement == BoatMovement && movement != BoatMovement && BoatMovement != null)
            BoatMovement.ClearBoat();

        activeMovement = movement;
        latestDirection = Vector2.zero;
        return true;
    }

    public bool EnterSwimming()
    {
        if (SwimmingMovement == null)
        {
            Debug.LogWarning("PlayerController cannot enter swimming: SwimmingMovement is not assigned.", this);
            return false;
        }

        if (CharacterController != null && !CharacterController.enabled)
            CharacterController.enabled = true;

        return SetActiveMovement(SwimmingMovement);
    }

    public bool ExitSwimming()
    {
        if (GroundedMovement == null)
        {
            Debug.LogWarning("PlayerController cannot exit swimming: GroundedMovement is not assigned.", this);
            return false;
        }

        return SetActiveMovement(GroundedMovement);
    }

    /// <summary>
    /// Boards a boat and optionally parents the player to a seat transform. The
    /// CharacterController is disabled while aboard to avoid fighting the boat Rigidbody.
    /// </summary>
    public bool BoardBoat(BoatController boat, Transform seat = null)
    {
        if (boat == null || BoatMovement == null)
        {
            Debug.LogWarning("BoardBoat requires a boat and an assigned BoatMovement component.", this);
            return false;
        }

        if (isAboardBoat)
            LeaveBoat(null);

        previousParent = transform.parent;
        if (CharacterController != null)
        {
            characterControllerWasEnabled = CharacterController.enabled;
            CharacterController.enabled = false;
        }

        if (seat != null)
        {
            transform.SetParent(seat, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }
        else
        {
            transform.SetParent(boat.transform, true);
        }

        BoatMovement.AssignBoat(boat);
        isAboardBoat = true;
        return SetActiveMovement(BoatMovement);
    }

    /// <summary>Leaves the currently boarded boat at the optional exit transform.</summary>
    public void LeaveBoat(Transform exitPoint)
    {
        if (!isAboardBoat)
            return;

        transform.SetParent(previousParent, true);
        if (exitPoint != null)
        {
            transform.SetPositionAndRotation(exitPoint.position, exitPoint.rotation);
        }

        if (CharacterController != null)
        {
            CharacterController.enabled = false;
            Physics.SyncTransforms();
            CharacterController.enabled = characterControllerWasEnabled;
        }

        isAboardBoat = false;
        previousParent = null;
        if (BoatMovement != null)
            BoatMovement.ClearBoat();

        if (GroundedMovement != null)
            SetActiveMovement(GroundedMovement);
    }

    private void OnMoved(InputAction.CallbackContext context)
    {
        latestDirection = context.ReadValue<Vector2>();
    }

    private void OnJumped(InputAction.CallbackContext context)
    {
        activeMovement?.Jump(context.canceled);
    }

    private void OnDiveChanged(InputAction.CallbackContext context)
    {
        if (SwimmingMovement != null)
            SwimmingMovement.SetDiveInput(!context.canceled);
    }

    private static void Subscribe(InputActionReference reference, System.Action<InputAction.CallbackContext> callback)
    {
        if (reference != null && reference.action != null)
        {
            reference.action.performed += callback;
            reference.action.canceled += callback;
        }
    }

    private static void Unsubscribe(InputActionReference reference, System.Action<InputAction.CallbackContext> callback)
    {
        if (reference != null && reference.action != null)
        {
            reference.action.performed -= callback;
            reference.action.canceled -= callback;
        }
    }

    private static void EnableAction(InputActionReference reference)
    {
        if (reference != null && reference.action != null)
            reference.action.Enable();
    }

    private static void DisableAction(InputActionReference reference)
    {
        if (reference != null && reference.action != null)
            reference.action.Disable();
    }
}
