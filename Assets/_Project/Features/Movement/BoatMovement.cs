using Assets._Project.Features.Boat;
using Assets._Project.Features.Movement;
using UnityEngine;

/// <summary>
/// IMovement adapter used by PlayerController while aboard a boat.
/// X steers the rudder. Positive Y deploys the sail; negative Y furls it.
/// E/Q sail rotation is supplied separately so the movement vector remains
/// compatible with existing Input Actions.
/// </summary>
[DisallowMultipleComponent]
public sealed class BoatMovement : MonoBehaviour, IMovement
{
    [SerializeField] private BoatController boat;

    private Vector2 inputDirection;
    private float sailRotationInput;

    public BoatController Boat => boat;
    /// <summary>World-space planar velocity used by the animation bridge.</summary>
    public Vector3 AnimationPlanarVelocity => boat != null && boat.Body != null
        ? Vector3.ProjectOnPlane(boat.Body.linearVelocity, Vector3.up)
        : Vector3.zero;

    public void AssignBoat(BoatController targetBoat)
    {
        if (boat != null && boat != targetBoat)
            boat.ReleaseControls();

        boat = targetBoat;
        inputDirection = Vector2.zero;
        sailRotationInput = 0f;
    }

    public void SetSailRotationInput(float input)
    {
        sailRotationInput = Mathf.Clamp(input, -1f, 1f);
    }

    public void StopDriving()
    {
        inputDirection = Vector2.zero;
        sailRotationInput = 0f;
        if (boat != null)
            boat.ReleaseControls();
    }

    public void ClearBoat()
    {
        if (boat != null)
            boat.ReleaseControls();
        boat = null;
        inputDirection = Vector2.zero;
        sailRotationInput = 0f;
    }

    public void Move(Vector2 direction)
    {
        inputDirection = Vector2.ClampMagnitude(direction, 1f);
    }

    public void Jump(bool released) { } // Leaving the boat is handled by PlayerController.LeaveBoatWithJump.

    public void ApplyMovement()
    {
        if (boat == null)
            return;

        boat.SetMovementInput(inputDirection);
        boat.SetSailRotationInput(sailRotationInput);
    }

    private void OnDisable()
    {
        StopDriving();
    }
}
