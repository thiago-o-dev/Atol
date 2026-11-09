using Assets._Project.Features.Movement;
using UnityEngine;

[DisallowMultipleComponent]
public class GroundedMovement : MonoBehaviour, IMovement, IPlanarVelocityMovement
{
    public Transform ReferenceTransform;
    public CharacterController Controller;

    [Min(0f)] public float Speed = 6f;
    [Min(0f)] public float JumpHeight = 2f;
    [Min(0f)] public float GravityMultiplier = 1f;
    [Min(0.01f)] public float TurnSmoothTime = 0.1f;
    [Min(0f)] public float Acceleration = 20f;
    [Min(0f)] public float Deceleration = 15f;
    public bool UseCoyoteTiming = true;
    [Min(0f)] public float CoyoteTime = 0.1f;

    private float _turnSmoothVelocity;
    private float _timeSinceLastGrounded;
    private Vector3 _direction;
    private Vector3 _velocity;

    public Vector3 PlanarVelocity => new Vector3(_velocity.x, 0f, _velocity.z);

    public void Move(Vector2 inputDirection)
    {
        _direction = new Vector3(inputDirection.x, 0f, inputDirection.y);
        if (_direction.sqrMagnitude > 1f)
            _direction.Normalize();
    }

    public void SetPlanarVelocity(Vector3 velocity)
    {
        velocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        _velocity.x = velocity.x;
        _velocity.z = velocity.z;
    }

    /// <summary>
    /// Clears accumulated fall speed when returning from swimming. Ground gravity
    /// starts naturally from rest instead of resuming a stale, compounded value.
    /// </summary>
    public void ResetVerticalVelocity()
    {
        _velocity.y = 0f;
        _timeSinceLastGrounded = 0f;
    }

    /// <summary>Launches the character with an initial planar velocity and a jump of the given height.</summary>
    public void LaunchJump(Vector3 planarVelocity, float height)
    {
        SetPlanarVelocity(planarVelocity);
        _velocity.y = Mathf.Sqrt(Mathf.Max(0f, height) * -2f * Physics.gravity.y);
        _timeSinceLastGrounded = 0f;
    }

    public void Jump(bool released)
    {
        if (released || Controller == null || !Controller.enabled)
            return;

        if (!Controller.isGrounded)
        {
            if (!UseCoyoteTiming || _timeSinceLastGrounded <= 0f)
                return;

            _timeSinceLastGrounded = 0f;
        }

        _velocity.y = Mathf.Sqrt(JumpHeight * -2f * Physics.gravity.y);
    }

    public void ApplyMovement()
    {
        if (Controller == null || !Controller.enabled)
            return;

        if (UseCoyoteTiming)
        {
            _timeSinceLastGrounded = Controller.isGrounded
                ? CoyoteTime
                : Mathf.MoveTowards(_timeSinceLastGrounded, 0f, Time.fixedDeltaTime);
        }

        Vector3 targetHorizontalVelocity = Vector3.zero;
        if (_direction.sqrMagnitude > 0f)
        {
            float targetAngle;
            if (ReferenceTransform != null)
            {
                targetAngle = Mathf.Atan2(_direction.x, _direction.z) * Mathf.Rad2Deg
                              + ReferenceTransform.eulerAngles.y;
            }
            else
            {
                targetAngle = Mathf.Atan2(_direction.x, _direction.z) * Mathf.Rad2Deg;
            }

            SmoothApplyRotation(targetAngle);
            Vector3 worldDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            targetHorizontalVelocity = worldDirection * (Speed * Mathf.Clamp01(_direction.magnitude));
        }

        Vector3 currentHorizontalVelocity = PlanarVelocity;
        float rate = targetHorizontalVelocity.sqrMagnitude > 0.0001f ? Acceleration : Deceleration;
        currentHorizontalVelocity = Vector3.MoveTowards(
            currentHorizontalVelocity, targetHorizontalVelocity, rate * Time.fixedDeltaTime);

        _velocity.x = currentHorizontalVelocity.x;
        _velocity.z = currentHorizontalVelocity.z;

        ApplyGravity();
        Controller.Move(_velocity * Time.fixedDeltaTime);
    }

    private void ApplyGravity()
    {
        if (Controller.isGrounded && _velocity.y < 0f)
            _velocity.y = -1f;
        else
            _velocity.y += Physics.gravity.y * GravityMultiplier * Time.fixedDeltaTime;
    }

    private void SmoothApplyRotation(float targetAngle)
    {
        float angle = Mathf.SmoothDampAngle(
            transform.eulerAngles.y, targetAngle, ref _turnSmoothVelocity, TurnSmoothTime);
        transform.rotation = Quaternion.Euler(0f, angle, 0f);
    }
}
