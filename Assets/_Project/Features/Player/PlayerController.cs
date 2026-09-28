using Assets._Project.Features.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("MovementTypes")]
    public GroundedMovement GroundedMovement;

    [Header("Keybinds")]
    public InputActionReference MovementAction;
    public InputActionReference JumpAction;

    private IMovement _activeIMovement;
    private Vector2 _latestDirection = Vector2.zero;

    public void FixedUpdate()
    {
        _activeIMovement.Move(_latestDirection);

        _activeIMovement.ApplyMovement();
    }

    private void Awake()
    {
        _activeIMovement = GroundedMovement;

        MovementAction.action.performed += OnMoved;
        MovementAction.action.canceled += OnMoved;

        JumpAction.action.performed += OnJumped;
        JumpAction.action.canceled += OnJumped;

        MovementAction.action.Enable();
        JumpAction.action.Enable();
    }

    private void OnDestroy()
    {
        MovementAction.action.performed -= OnMoved;
        MovementAction.action.canceled -= OnMoved;

        JumpAction.action.performed -= OnJumped;
        JumpAction.action.canceled -= OnJumped;
    }

    private void OnMoved(InputAction.CallbackContext context)
    {
        _latestDirection = context.ReadValue<Vector2>();
    }

    private void OnJumped(InputAction.CallbackContext context)
    {
        _activeIMovement.Jump(context.canceled);
    }
}
