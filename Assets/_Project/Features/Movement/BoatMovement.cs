using Assets._Project.Features.Boat;
using Assets._Project.Features.Movement;
using UnityEngine;

/// <summary>
/// IMovement adapter used by PlayerController while the player is aboard a boat.
/// X input steers. Positive Y deploys the sail; negative Y furls it.
/// </summary>
[DisallowMultipleComponent]
public sealed class BoatMovement : MonoBehaviour, IMovement
{
    [SerializeField] private BoatController boat;

    private Vector2 inputDirection;

    public BoatController Boat => boat;

    public void AssignBoat(BoatController targetBoat)
    {
        if (boat != null && boat != targetBoat)
            boat.ReleaseControls();

        boat = targetBoat;
        inputDirection = Vector2.zero;
    }

    public void StopDriving()
    {
        inputDirection = Vector2.zero;
        if (boat != null)
            boat.ReleaseControls();
    }

    public void ClearBoat()
    {
        if (boat != null)
            boat.ReleaseControls();
        boat = null;
        inputDirection = Vector2.zero;
    }

    public void Move(Vector2 direction)
    {
        inputDirection = Vector2.ClampMagnitude(direction, 1f);
    }

    // Jump has no boat action by default; keep this method to satisfy IMovement.
    public void Jump(bool released)
    {
    }

    public void ApplyMovement()
    {
        if (boat != null)
            boat.SetMovementInput(inputDirection);
    }

    private void OnDisable()
    {
        StopDriving();
    }
}
