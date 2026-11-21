using Assets._Project.Features.Movement;
using Assets._Project.Features.StatusViewModels;
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
    [Tooltip("Allows jumping for a short time after leaving a ledge.")]
    public bool UseCoyoteTiming = true;
    [Min(0f)] public float CoyoteTime = 0.2f;

    [Header("Stamina")]
    public StaminaComponent Stamina;
    [Min(0f)] public float JumpStaminaCost = 10f;

    [Header("Dash")]
    [Min(0f)] public float DashSpeed = 16f;
    [Min(0.01f)] public float DashDuration = 0.18f;
    [Min(0f)] public float DashCooldown = 0.4f;
    [Min(0f)] public float DashStaminaCost = 20f;

    private float _turnSmoothVelocity;
    private bool _isDashing;
    private float _dashEndTime;
    private float _nextDashTime;
    private Vector3 _dashVelocity;
    private float _lastGroundedTime = -1000f;
    private float _fallingDuration;
    private bool _jumpInProgress;
    private Vector3 _direction;
    private Vector3 _velocity;

    public Vector3 PlanarVelocity => new Vector3(_velocity.x, 0f, _velocity.z);
    public Vector3 Velocity => _velocity;
    public float VerticalVelocity => _velocity.y;
    public float FallingDuration => _fallingDuration;
    public bool IsJumpInProgress => _jumpInProgress;
    public bool IsDashing => _isDashing;
    public bool IsGrounded => Controller != null && Controller.enabled && Controller.isGrounded;
    public bool CanCoyoteJump => UseCoyoteTiming && _lastGroundedTime > -999f &&
                                 Time.time - _lastGroundedTime >= 0f &&
                                 Time.time - _lastGroundedTime <= Mathf.Max(0f, CoyoteTime);

    /// <summary>Raised only after a normal or coyote-time jump has actually been accepted.</summary>
    public event System.Action JumpStarted;
    public event System.Action DashStarted;

    private void OnEnable()
    {
        RememberGroundContact();
    }

    public void Move(Vector2 inputDirection)
    {
        _direction = new Vector3(inputDirection.x, 0f, inputDirection.y);
        if (_direction.sqrMagnitude > 1f)
            _direction.Normalize();
    }

    public void SetPlanarVelocity(Vector3 velocity)
    {
        velocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        _isDashing = false;
        _velocity.x = velocity.x;
        _velocity.z = velocity.z;
    }

    /// <summary>
    /// Clears accumulated fall speed when changing movement modes. The caller can
    /// preserve the short coyote window when automatic water detection switches
    /// modes near a ledge, so an accepted grace-window jump is not lost.
    /// </summary>
    public void ResetVerticalVelocity(bool clearCoyoteWindow = true)
    {
        // A mode handoff with clearCoyoteWindow=false is allowed to happen while a
        // jump/fall is still airborne. Preserve that actual vertical motion; otherwise
        // a delayed ocean sample can zero the accepted coyote jump at the handoff.
        // When no accepted jump is in progress, stale grounded gravity is still reset.
        bool preserveAcceptedJump = !clearCoyoteWindow && _jumpInProgress;
        if (!preserveAcceptedJump)
        {
            _velocity.y = 0f;
            _fallingDuration = 0f;
        }

        if (clearCoyoteWindow)
        {
            _lastGroundedTime = -1000f;
            _jumpInProgress = false;
        }
    }

    /// <summary>Launches the character with an initial planar velocity and a jump of the given height.</summary>
    public void LaunchJump(Vector3 planarVelocity, float height)
    {
        SetPlanarVelocity(planarVelocity);
        _velocity.y = Mathf.Sqrt(Mathf.Max(0f, height) * -2f * Physics.gravity.y);
        _lastGroundedTime = -1000f;
        _fallingDuration = 0f;
        _jumpInProgress = true;
        JumpStarted?.Invoke();
    }

    public void Jump(bool released)
    {
        if (released || Controller == null || !Controller.enabled)
            return;

        // Check the controller's last collision result before updating its timer.
        // The timestamp remains valid for the full configured coyote window even
        // when the jump input arrives between FixedUpdate ticks.
        bool grounded = Controller.isGrounded;
        bool insideCoyoteWindow = CanCoyoteJump;
        if (!grounded && !insideCoyoteWindow)
            return;

        if (Stamina != null && !Stamina.TryConsume(JumpStaminaCost))
            return;

        _velocity.y = Mathf.Sqrt(Mathf.Max(0f, JumpHeight) * -2f * Physics.gravity.y);
        _lastGroundedTime = -1000f;
        _fallingDuration = 0f;
        _jumpInProgress = true;
        JumpStarted?.Invoke();
    }

    public bool Dash()
    {
        if (Controller == null || !Controller.enabled || _isDashing || Time.time < _nextDashTime)
            return false;

        if (!Controller.isGrounded && !CanCoyoteJump)
            return false;

        if (Stamina != null && !Stamina.TryConsume(DashStaminaCost))
            return false;

        Vector3 direction = _direction.sqrMagnitude > 0f
            ? Quaternion.Euler(0f, GetTargetAngle(), 0f) * Vector3.forward
            : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        _dashVelocity = direction * DashSpeed;
        _isDashing = true;
        _dashEndTime = Time.time + DashDuration;
        _nextDashTime = _dashEndTime + DashCooldown;
        DashStarted?.Invoke();
        return true;
    }

    public void ApplyMovement()
    {
        if (Controller == null || !Controller.enabled)
            return;

        // Remember support before Move so a ledge departure during this physics step
        // still starts the coyote clock. RememberGroundContact ignores an accepted
        // jump until it has actually landed, preventing stale isGrounded from re-arming
        // the grace window on the first fixed tick after jump input.
        RememberGroundContact();

        Vector3 targetHorizontalVelocity = Vector3.zero;
        if (_direction.sqrMagnitude > 0f)
        {
            float targetAngle = GetTargetAngle();

            SmoothApplyRotation(targetAngle);
            Vector3 worldDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            targetHorizontalVelocity = worldDirection * (Speed * Mathf.Clamp01(_direction.magnitude));
        }

        Vector3 currentHorizontalVelocity = PlanarVelocity;
        if (_isDashing && Time.time >= _dashEndTime)
        {
            _isDashing = false;
            currentHorizontalVelocity = Vector3.ClampMagnitude(currentHorizontalVelocity, Speed);
        }

        if (_isDashing)
        {
            currentHorizontalVelocity = _dashVelocity;
        }
        else
        {
            float rate = targetHorizontalVelocity.sqrMagnitude > 0.0001f ? Acceleration : Deceleration;
            currentHorizontalVelocity = Vector3.MoveTowards(
                currentHorizontalVelocity, targetHorizontalVelocity, rate * Time.fixedDeltaTime);
        }

        _velocity.x = currentHorizontalVelocity.x;
        _velocity.z = currentHorizontalVelocity.z;

        float dt = Time.fixedDeltaTime;
        ApplyGravity();
        Controller.Move(_velocity * dt);

        // A jump remains in progress until Move confirms a real landing. Only then
        // clear the state and refresh the coyote timestamp for the next jump.
        if (Controller.isGrounded)
        {
            _fallingDuration = 0f;
            _jumpInProgress = false;
        }
        else if (_velocity.y < -0.05f)
        {
            _fallingDuration += dt;
        }
        else if (_velocity.y > 0.05f)
        {
            _fallingDuration = 0f;
        }

        // Must run after the landing-state update above. A cached grounded result at
        // the start of the jump must not refresh the timestamp while _jumpInProgress.
        RememberGroundContact();
    }

    private void RememberGroundContact()
    {
        if (_jumpInProgress)
            return;

        if (UseCoyoteTiming && Controller != null && Controller.enabled && Controller.isGrounded)
            _lastGroundedTime = Time.time;
    }

    /// <summary>
    /// Checks whether terrain is still close beneath the capsule. CharacterController
    /// grounding can flicker for a frame while following a downhill slope or stair.
    /// The centre ray, or two supporting edge rays, suppress false fall animations.
    /// The mask should exclude the player and non-solid water layers.
    /// </summary>
    public bool HasNearbyGroundSupport(float extraDistance, LayerMask groundLayers)
    {
        if (Controller == null || !Controller.enabled)
            return false;

        float scaleY = Mathf.Abs(Controller.transform.lossyScale.y);
        float scaleXZ = Mathf.Max(
            Mathf.Abs(Controller.transform.lossyScale.x),
            Mathf.Abs(Controller.transform.lossyScale.z));
        float halfHeight = Controller.height * scaleY * 0.5f;
        float radius = Controller.radius * scaleXZ * 0.65f;
        float maxDistance = halfHeight + Controller.skinWidth * scaleY
                            + Mathf.Max(0f, extraDistance);
        Vector3 origin = Controller.bounds.center;
        Vector3 right = Vector3.ProjectOnPlane(Controller.transform.right, Vector3.up).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(Controller.transform.forward, Vector3.up).normalized;
        int edgeSupportCount = 0;

        for (int i = 0; i < 5; i++)
        {
            Vector3 offset = Vector3.zero;
            if (i == 1) offset = right * radius;
            else if (i == 2) offset = -right * radius;
            else if (i == 3) offset = forward * radius;
            else if (i == 4) offset = -forward * radius;

            if (!Physics.Raycast(origin + offset, Vector3.down, out RaycastHit hit,
                    maxDistance, groundLayers, QueryTriggerInteraction.Ignore))
                continue;

            if (hit.collider == Controller || hit.collider.transform.IsChildOf(Controller.transform))
                continue;

            if (i == 0)
                return true;

            edgeSupportCount++;
        }

        return edgeSupportCount >= 2;
    }

    private void ApplyGravity()
    {
        if (Controller.isGrounded && _velocity.y < 0f)
            _velocity.y = -1f;
        else
            _velocity.y += Physics.gravity.y * GravityMultiplier * Time.fixedDeltaTime;
    }

    private float GetTargetAngle()
    {
        float angle = Mathf.Atan2(_direction.x, _direction.z) * Mathf.Rad2Deg;
        return ReferenceTransform != null ? angle + ReferenceTransform.eulerAngles.y : angle;
    }

    private void SmoothApplyRotation(float targetAngle)
    {
        float angle = Mathf.SmoothDampAngle(
            transform.eulerAngles.y, targetAngle, ref _turnSmoothVelocity, TurnSmoothTime);
        transform.rotation = Quaternion.Euler(0f, angle, 0f);
    }
}
