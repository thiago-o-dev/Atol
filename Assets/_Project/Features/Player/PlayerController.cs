using Assets._Project.Features.Boat;
using Assets._Project.Features.Movement;
using Assets._Project.Features.StatusViewModels;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Routes player input to grounded, swimming, or boat movement. Movement changes
/// preserve the current input vector and planar velocity where supported.
/// </summary>
[RequireComponent(typeof(StaminaComponent))]
public sealed class PlayerController : MonoBehaviour
{
    [Header("Movement Types")]
    public GroundedMovement GroundedMovement;
    public SwimmingMovement SwimmingMovement;
    public BoatMovement BoatMovement;

    [Header("Input Actions")]
    public InputActionReference MovementAction;
    public InputActionReference JumpAction;
    [Tooltip("Optional: hold to swim downward. While swimming, JumpAction performs a stamina-costing hop out of the water (SwimmingMovement.EnableJumpHop).")]
    public InputActionReference DiveAction;

    [Header("Boat Boarding")]
    [Tooltip("Optional CharacterController reference. If empty, one is searched on this object.")]
    public CharacterController CharacterController;
    [Tooltip("Optional Input System action. If unassigned, E is used as the interaction key.")]
    public InputActionReference InteractAction;
    [Tooltip("Fallback distance to move the player clear of the hull when no ExitAnchor is assigned.")]
    [Min(0.25f)] public float DefaultBoatExitDistance = 1.75f;
    [Tooltip("Fraction of the boat's planar velocity inherited when stepping off.")]
    [Range(0f, 1.5f)] public float DisembarkVelocityInheritance = 1f;
    [Header("Leaving The Boat (Jump key)")]
    [Tooltip("Upward launch height (metres) when jumping off the boat.")]
    [Min(0f)] public float BoatExitJumpHeight = 1.1f;
    [Tooltip("Extra planar speed (m/s) added when jumping off, along input direction (or away from the hull when idle).")]
    [Min(0f)] public float BoatExitJumpSpeed = 3f;
    [Tooltip("Seconds the player does not collide with the boat after jumping off.")]
    [Min(0f)] public float BoatExitIgnoreCollisionTime = 0.2f;
    [Tooltip("Layers checked when selecting a safe side of the boat to disembark.")]
    public LayerMask BoatExitObstructionMask = ~0;
    [Tooltip("Extra outward offsets tested when an ExitAnchor is close to terrain or another obstacle.")]
    [Min(0f)] public float BoatExitClearanceStep = 0.45f;

    [Header("Automatic Swimming")]
    public bool AutoEnterSwimming = true;
    [Tooltip("FFT ocean used to detect the animated water surface. Auto-resolved if empty.")]
    public SpectralOceanFFT Ocean;
    [Min(0.05f)] public float WaterCheckInterval = 0.15f;
    [Tooltip("After entering swimming, the water must fall this far below the body midpoint before returning to grounded movement.")]
    [Min(0f)] public float SwimmingExitHysteresis = 0.25f;

    private IMovement activeMovement;
    private Vector2 latestDirection;
    private Transform previousParent;
    private bool characterControllerWasEnabled;
    private bool isAboardBoat;
    private BoatController currentBoat;
    private BoatController nearbyBoat;
    private float nextWaterCheckTime;
    private bool waterSamplePending;
    private int waterSampleGeneration;
    private BoatControlHintUI boatControlHint;
    private bool interactionPressPending;
    private readonly System.Collections.Generic.List<Collider> ignoredBoatColliders = new System.Collections.Generic.List<Collider>(16);
    private readonly System.Collections.Generic.List<Collider> ignoredPlayerColliders = new System.Collections.Generic.List<Collider>(4);
    private float ignoreBoatCollisionUntil;
    private bool boatCollisionIgnored;
    private bool sailInputActive;
    private float nextPromptPollTime;
    private const float PromptPollInterval = 0.1f;
    private int pendingSampleGeneration;
    private System.Action<SpectralOceanFFT.SurfaceSample> sampleCallback;
    private readonly Collider[] exitOverlapBuffer = new Collider[32];

    /// <summary>Stamina pool shared with SwimmingMovement (auto-added if missing on older prefabs).</summary>
    public StaminaComponent Stamina { get; private set; }

    public IMovement ActiveMovement => activeMovement;
    public bool IsAboardBoat => isAboardBoat;
    public BoatController CurrentBoat => currentBoat;

    private void Awake()
    {
        if (CharacterController == null)
            CharacterController = GetComponent<CharacterController>();

        if (Ocean == null)
            Ocean = Object.FindAnyObjectByType<SpectralOceanFFT>();

        Stamina = StaminaComponent.Resolve(gameObject);
        if (SwimmingMovement != null && SwimmingMovement.Stamina == null)
            SwimmingMovement.Stamina = Stamina;

        boatControlHint = GetComponent<BoatControlHintUI>();
        if (boatControlHint == null)
            boatControlHint = gameObject.AddComponent<BoatControlHintUI>();
        boatControlHint.SetVisible(false);

        if (GroundedMovement != null && GroundedMovement.Controller == null)
            GroundedMovement.Controller = CharacterController;
        if (SwimmingMovement != null)
        {
            if (SwimmingMovement.Controller == null)
                SwimmingMovement.Controller = CharacterController;
            if (SwimmingMovement.Ocean == null)
                SwimmingMovement.Ocean = Ocean;
            if (SwimmingMovement.ReferenceTransform == null)
            {
                if (GroundedMovement != null && GroundedMovement.ReferenceTransform != null)
                    SwimmingMovement.ReferenceTransform = GroundedMovement.ReferenceTransform;
                else if (Camera.main != null)
                    SwimmingMovement.ReferenceTransform = Camera.main.transform;
            }
        }

        if (GroundedMovement != null)
            activeMovement = GroundedMovement;
        else if (SwimmingMovement != null)
            activeMovement = SwimmingMovement;
        else
            activeMovement = BoatMovement;

        activeMovement?.Move(latestDirection);
    }

    private void OnEnable()
    {
        Subscribe(MovementAction, OnMoved);
        Subscribe(JumpAction, OnJumped);
        Subscribe(DiveAction, OnDiveChanged);
        Subscribe(InteractAction, OnInteract);
        EnableAction(MovementAction);
        EnableAction(JumpAction);
        EnableAction(DiveAction);
        EnableAction(InteractAction);
        waterSampleGeneration++;
        waterSamplePending = false;
        activeMovement?.Move(latestDirection);
    }

    private void Update()
    {
        // Only use direct keyboard interaction when no Input System action is assigned.
        if (InteractAction == null)
            UpdateKeyboardFallbackInteraction();

        UpdateBoatCollisionIgnore();
        UpdateSailRotationInput();
        UpdateNearbyBoatPrompt();
        UpdateAutomaticSwimming();
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
        Unsubscribe(InteractAction, OnInteract);
        DisableAction(MovementAction);
        DisableAction(JumpAction);
        DisableAction(DiveAction);
        DisableAction(InteractAction);
        CancelPendingWaterSample();
        EndBoatCollisionIgnore();

        if (nearbyBoat != null && nearbyBoat.Prompt != null)
            nearbyBoat.Prompt.SetVisible(false);
        if (currentBoat != null && currentBoat.Prompt != null)
            currentBoat.Prompt.SetVisible(false);
        nearbyBoat = null;
        boatControlHint?.SetVisible(false);
        interactionPressPending = false;

        if (BoatMovement != null)
            BoatMovement.StopDriving();
    }

    /// <summary>
    /// Changes the active movement without dropping input or horizontal momentum.
    /// Vertical grounded velocity is deliberately reset on water transitions so
    /// stale gravity cannot accumulate while the grounded motor is inactive.
    /// </summary>
    public bool SetActiveMovement(IMovement movement)
    {
        if (movement == null)
            return false;

        if (object.ReferenceEquals(activeMovement, movement))
        {
            activeMovement.Move(latestDirection);
            return true;
        }

        IMovement previousMovement = activeMovement;
        if (previousMovement is IPlanarVelocityMovement sourceState &&
            movement is IPlanarVelocityMovement targetState)
        {
            targetState.SetPlanarVelocity(sourceState.PlanarVelocity);
        }

        if (object.ReferenceEquals(movement, SwimmingMovement))
        {
            SwimmingMovement.ResetVerticalVelocity();
            if (previousMovement is GroundedMovement previousGrounded)
                previousGrounded.ResetVerticalVelocity();
        }
        else if (object.ReferenceEquals(movement, GroundedMovement) &&
                 previousMovement is SwimmingMovement)
        {
            GroundedMovement.ResetVerticalVelocity();
        }

        if (object.ReferenceEquals(previousMovement, BoatMovement) &&
            !object.ReferenceEquals(movement, BoatMovement) && BoatMovement != null)
        {
            BoatMovement.ClearBoat();
        }

        activeMovement = movement;
        // Input actions do not send another performed event just because a movement
        // implementation changed. Push the currently held vector into the new mode.
        activeMovement.Move(latestDirection);
        return true;
    }

    public bool EnterSwimming()
    {
        if (SwimmingMovement == null || isAboardBoat)
        {
            if (SwimmingMovement == null)
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

    /// <summary>Boards a boat and attaches the player to its BoardingAnchor.</summary>
    public bool BoardBoat(BoatController boat, Transform seat = null)
    {
        if (boat == null || boat.IsRecoveringFromCapsize || BoatMovement == null)
        {
            Debug.LogWarning("BoardBoat requires a boat and an assigned BoatMovement component.", this);
            return false;
        }

        if (isAboardBoat)
            LeaveBoat(null);

        CancelPendingWaterSample();
        previousParent = transform.parent;
        currentBoat = boat;

        if (CharacterController != null)
        {
            characterControllerWasEnabled = CharacterController.enabled;
            if (characterControllerWasEnabled)
                CharacterController.enabled = false;
        }

        if (seat == null)
            seat = boat.BoardingAnchor;

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
        boat.SetPlayerAboard(true, this);
        isAboardBoat = true;
        // The boat's world prompt/outline is only for approaching players.
        // Once aboard, use the compact bottom-left controls hint instead.
        boat.Prompt?.SetVisible(false);
        boatControlHint?.SetVisible(true);
        interactionPressPending = false;

        return SetActiveMovement(BoatMovement);
    }

    /// <summary>Leaves the boat at an exit point (used for forced exits). The Jump key uses LeaveBoatWithJump.</summary>
    public void LeaveBoat(Transform exitPoint)
    {
        if (!isAboardBoat)
            return;

        BoatController leavingBoat = currentBoat;
        if (exitPoint == null && leavingBoat != null &&
            TrySelectSafeBoatExit(leavingBoat, out _, out Vector3 safePosition, out Quaternion safeRotation))
        {
            LeaveBoatAtSafePose(safePosition, safeRotation);
            return;
        }

        Vector3 position = exitPoint != null ? exitPoint.position :
            (leavingBoat != null
                ? leavingBoat.transform.position - leavingBoat.transform.forward * DefaultBoatExitDistance
                : transform.position);
        Quaternion rotation = exitPoint != null ? exitPoint.rotation :
            (leavingBoat != null
                ? Quaternion.LookRotation(-leavingBoat.transform.forward, Vector3.up)
                : transform.rotation);
        if (exitPoint == null && leavingBoat != null)
            position.y = Mathf.Max(transform.position.y, leavingBoat.transform.position.y + 0.25f);
        LeaveBoatAtSafePose(position, rotation);
    }

    private void LeaveBoatAtSafePose(Vector3 exitPosition, Quaternion exitRotation, bool jump = false, Vector3 jumpPlanarVelocity = default)
    {
        if (!isAboardBoat)
            return;

        CancelPendingWaterSample();
        BoatController leavingBoat = currentBoat;
        Vector3 boatPlanarVelocity = leavingBoat != null && leavingBoat.Body != null
            ? Vector3.ProjectOnPlane(leavingBoat.Body.linearVelocity, Vector3.up) * DisembarkVelocityInheritance
            : Vector3.zero;

        transform.SetParent(previousParent, true);
        transform.SetPositionAndRotation(exitPosition, exitRotation);

        if (CharacterController != null && characterControllerWasEnabled && !CharacterController.enabled)
            CharacterController.enabled = true;

        if (BoatMovement != null)
            BoatMovement.ClearBoat();

        if (leavingBoat != null)
        {
            leavingBoat.Prompt?.SetPrompt("[E] Board");
            leavingBoat.Prompt?.SetVisible(false);
            leavingBoat.SetPlayerAboard(false);
        }

        boatControlHint?.SetVisible(false);
        isAboardBoat = false;
        previousParent = null;
        currentBoat = null;
        nearbyBoat = null;
        interactionPressPending = false;
        nextWaterCheckTime = Time.time + Mathf.Max(0.05f, WaterCheckInterval);

        if (GroundedMovement != null)
        {
            SetActiveMovement(GroundedMovement);
            if (jump)
            {
                GroundedMovement.LaunchJump(boatPlanarVelocity + jumpPlanarVelocity, BoatExitJumpHeight);
                BeginBoatCollisionIgnore(leavingBoat);
            }
            else
            {
                GroundedMovement.SetPlanarVelocity(boatPlanarVelocity);
                GroundedMovement.ResetVerticalVelocity();
            }
        }
    }

    /// <summary>
    /// Leaves the boat with a hop from the current seat position (no exit anchor).
    /// The player ignores the boat's colliders for BoatExitIgnoreCollisionTime.
    /// </summary>
    public void LeaveBoatWithJump()
    {
        if (!isAboardBoat || currentBoat == null)
            return;

        Vector3 outward = Vector3.ProjectOnPlane(transform.position - currentBoat.transform.position, Vector3.up);
        if (outward.sqrMagnitude < 0.0001f)
            outward = transform.forward;
        outward.Normalize();

        Vector3 direction = outward;
        if (latestDirection.sqrMagnitude > 0.04f)
        {
            float yaw = GroundedMovement != null && GroundedMovement.ReferenceTransform != null
                ? GroundedMovement.ReferenceTransform.eulerAngles.y : 0f;
            direction = Quaternion.Euler(0f, yaw, 0f) * new Vector3(latestDirection.x, 0f, latestDirection.y);
            direction.Normalize();
        }

        LeaveBoatAtSafePose(
            transform.position + Vector3.up * 0.1f,
            Quaternion.Euler(0f, transform.eulerAngles.y, 0f),
            true,
            direction * BoatExitJumpSpeed);
    }

    private void BeginBoatCollisionIgnore(BoatController boat)
    {
        EndBoatCollisionIgnore();
        if (boat == null || BoatExitIgnoreCollisionTime <= 0f)
            return;

        ignoredBoatColliders.Clear();
        ignoredPlayerColliders.Clear();
        boat.GetComponentsInChildren(true, ignoredBoatColliders);
        GetComponentsInChildren(true, ignoredPlayerColliders);

        for (int p = 0; p < ignoredPlayerColliders.Count; p++)
            for (int b = 0; b < ignoredBoatColliders.Count; b++)
                if (ignoredPlayerColliders[p] != null && ignoredBoatColliders[b] != null &&
                    !ignoredBoatColliders[b].transform.IsChildOf(transform))
                    Physics.IgnoreCollision(ignoredPlayerColliders[p], ignoredBoatColliders[b], true);

        boatCollisionIgnored = true;
        ignoreBoatCollisionUntil = Time.time + BoatExitIgnoreCollisionTime;
    }

    private void UpdateBoatCollisionIgnore()
    {
        if (boatCollisionIgnored && Time.time >= ignoreBoatCollisionUntil)
            EndBoatCollisionIgnore();
    }

    private void EndBoatCollisionIgnore()
    {
        if (!boatCollisionIgnored)
            return;

        for (int p = 0; p < ignoredPlayerColliders.Count; p++)
            for (int b = 0; b < ignoredBoatColliders.Count; b++)
                if (ignoredPlayerColliders[p] != null && ignoredBoatColliders[b] != null)
                    Physics.IgnoreCollision(ignoredPlayerColliders[p], ignoredBoatColliders[b], false);

        ignoredPlayerColliders.Clear();
        ignoredBoatColliders.Clear();
        boatCollisionIgnored = false;
    }


    /// <summary>
    /// Called by BoatController when its capsize recovery begins. Only the player
    /// currently attached to that boat is detached.
    /// </summary>
    public void ForceUnboardFromBoat(BoatController boat, Transform preferredExitPoint = null)
    {
        if (!isAboardBoat || boat == null || !object.ReferenceEquals(currentBoat, boat))
            return;

        // Always prefer the camera-facing safe anchor pair. Retain the legacy
        // preferred anchor as a fallback if neither side anchor has clear space.
        if (TrySelectSafeBoatExit(boat, out _, out Vector3 safePosition, out Quaternion safeRotation))
        {
            LeaveBoatAtSafePose(safePosition, safeRotation);
        }
        else
        {
            LeaveBoat(preferredExitPoint);
        }
    }

    private void HandleInteractPressed()
    {
        if (!isAboardBoat && nearbyBoat != null)
            BoardBoat(nearbyBoat, nearbyBoat.BoardingAnchor);
    }

    private void BeginInteractionPress()
    {
        if (!isAboardBoat || interactionPressPending)
            return;

        interactionPressPending = true;
    }

    private void CompleteInteractionPress()
    {
        interactionPressPending = false;
    }

    private void UpdateKeyboardFallbackInteraction()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (isAboardBoat)
                BeginInteractionPress();
            else
                HandleInteractPressed();
        }

        if (Keyboard.current.eKey.wasReleasedThisFrame)
            CompleteInteractionPress();
    }

    private void UpdateSailRotationInput()
    {
        if (!isAboardBoat || BoatMovement == null)
        {
            if (sailInputActive)
            {
                BoatMovement?.SetSailRotationInput(0f);
                sailInputActive = false;
            }
            return;
        }

        // Holding E rotates the sail immediately; Q rotates it the other way.
        float rotationInput = 0f;
        if (Keyboard.current != null && Keyboard.current.qKey.isPressed)
            rotationInput -= 1f;
        if (interactionPressPending)
            rotationInput += 1f;

        BoatMovement.SetSailRotationInput(Mathf.Clamp(rotationInput, -1f, 1f));
        sailInputActive = true;
    }

    private void UpdateNearbyBoatPrompt()
    {
        if (isAboardBoat)
        {
            currentBoat?.Prompt?.SetVisible(false);
            if (nearbyBoat != null && nearbyBoat != currentBoat)
                nearbyBoat.Prompt?.SetVisible(false);
            nearbyBoat = null;
            boatControlHint?.SetVisible(true);
            return;
        }

        // Boarding range does not need per-frame polling; 10 Hz is indistinguishable.
        if (Time.unscaledTime < nextPromptPollTime)
            return;
        nextPromptPollTime = Time.unscaledTime + PromptPollInterval;

        BoatController candidate = BoatController.FindNearest(transform.position, out _);
        if (candidate == nearbyBoat)
        {
            if (candidate != null)
                candidate.Prompt?.SetVisible(true);
            return;
        }

        if (nearbyBoat != null && nearbyBoat.Prompt != null)
        {
            nearbyBoat.Prompt.SetPrompt("[E] Board");
            nearbyBoat.Prompt.SetVisible(false);
        }

        nearbyBoat = candidate;
        if (nearbyBoat != null && nearbyBoat.Prompt != null)
        {
            nearbyBoat.Prompt.SetPrompt("[E] Board");
            nearbyBoat.Prompt.SetVisible(true);
        }
    }

    private void OnInteract(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            if (isAboardBoat)
                BeginInteractionPress();
            else
                HandleInteractPressed();
        }
        else if (context.canceled)
        {
            CompleteInteractionPress();
        }
    }


    private bool TrySelectSafeBoatExit(
        BoatController boat,
        out Transform selectedAnchor,
        out Vector3 selectedPosition,
        out Quaternion selectedRotation)
    {
        selectedAnchor = null;
        selectedPosition = default;
        selectedRotation = Quaternion.identity;
        if (boat == null)
            return false;

        Camera camera = Camera.main;
        Vector3 cameraSide = camera != null
            ? Vector3.ProjectOnPlane(camera.transform.position - boat.transform.position, Vector3.up)
            : Vector3.zero;
        if (cameraSide.sqrMagnitude < 0.001f && camera != null)
            cameraSide = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
        if (cameraSide.sqrMagnitude < 0.001f)
            cameraSide = boat.transform.right;
        cameraSide.Normalize();

        Transform left = boat.ExitAnchorLeft;
        Transform right = boat.ExitAnchorRight;
        Transform legacy = boat.ExitAnchor;

        float leftScore = GetExitCameraScore(left, boat.transform.position, cameraSide);
        float rightScore = GetExitCameraScore(right, boat.transform.position, cameraSide);
        bool leftFirst = left != null && (right == null || leftScore >= rightScore);

        if (leftFirst)
        {
            if (TryAnchorAndClearance(left, boat, out selectedPosition, out selectedRotation))
            {
                selectedAnchor = left;
                return true;
            }
            if (TryAnchorAndClearance(right, boat, out selectedPosition, out selectedRotation))
            {
                selectedAnchor = right;
                return true;
            }
        }
        else
        {
            if (TryAnchorAndClearance(right, boat, out selectedPosition, out selectedRotation))
            {
                selectedAnchor = right;
                return true;
            }
            if (TryAnchorAndClearance(left, boat, out selectedPosition, out selectedRotation))
            {
                selectedAnchor = left;
                return true;
            }
        }

        if (legacy != null && legacy != left && legacy != right &&
            TryAnchorAndClearance(legacy, boat, out selectedPosition, out selectedRotation))
        {
            selectedAnchor = legacy;
            return true;
        }

        // Both authored side anchors may be blocked (for example, beside a cliff).
        // Search a few wider offsets around the camera-facing side before falling
        // back to the old rear-of-boat placement.
        Vector3 preferredDirection = cameraSide;
        for (int i = 0; i < 6; i++)
        {
            float distance = DefaultBoatExitDistance + i * Mathf.Max(0.1f, BoatExitClearanceStep);
            Vector3 candidatePosition = boat.transform.position + preferredDirection * distance;
            candidatePosition.y = Mathf.Max(transform.position.y, boat.transform.position.y + 0.2f);
            Quaternion candidateRotation = Quaternion.LookRotation(preferredDirection, Vector3.up);
            if (IsExitPoseClear(candidatePosition, candidateRotation, boat))
            {
                selectedPosition = candidatePosition;
                selectedRotation = candidateRotation;
                return true;
            }
        }

        return false;
    }

    private static float GetExitCameraScore(Transform anchor, Vector3 boatPosition, Vector3 cameraSide)
    {
        if (anchor == null)
            return float.NegativeInfinity;
        Vector3 side = Vector3.ProjectOnPlane(anchor.position - boatPosition, Vector3.up);
        return side.sqrMagnitude > 0.001f ? Vector3.Dot(side.normalized, cameraSide) : -1f;
    }

    private bool TryAnchorAndClearance(
        Transform anchor,
        BoatController boat,
        out Vector3 safePosition,
        out Quaternion safeRotation)
    {
        safePosition = default;
        safeRotation = Quaternion.identity;
        if (anchor == null)
            return false;

        Vector3 outward = Vector3.ProjectOnPlane(anchor.position - boat.transform.position, Vector3.up);
        if (outward.sqrMagnitude < 0.001f)
            outward = anchor.right;
        outward.Normalize();

        float clearanceStep = Mathf.Max(0.1f, BoatExitClearanceStep);
        for (int i = 0; i < 4; i++)
        {
            Vector3 candidate = anchor.position + outward * (clearanceStep * i);
            Quaternion rotation = anchor.rotation;
            if (IsExitPoseClear(candidate, rotation, boat))
            {
                safePosition = candidate;
                safeRotation = rotation;
                return true;
            }
        }

        return false;
    }

    private bool IsExitPoseClear(Vector3 rootPosition, Quaternion rootRotation, BoatController boat)
    {
        float scaleX = Mathf.Abs(transform.lossyScale.x);
        float scaleY = Mathf.Abs(transform.lossyScale.y);
        float scaleZ = Mathf.Abs(transform.lossyScale.z);
        float radius = CharacterController != null
            ? CharacterController.radius * Mathf.Max(scaleX, scaleZ)
            : 0.34f;
        float height = CharacterController != null
            ? CharacterController.height * scaleY
            : 1.8f;
        radius = Mathf.Max(0.1f, radius);
        height = Mathf.Max(radius * 2f, height);

        Vector3 localCenter = CharacterController != null
            ? Vector3.Scale(CharacterController.center, new Vector3(scaleX, scaleY, scaleZ))
            : Vector3.up * (height * 0.5f);
        Vector3 capsuleCenter = rootPosition + rootRotation * localCenter;
        Vector3 up = rootRotation * Vector3.up;
        float halfLine = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 pointA = capsuleCenter - up * halfLine;
        Vector3 pointB = capsuleCenter + up * halfLine;

        int count = Physics.OverlapCapsuleNonAlloc(
            pointA, pointB, radius, exitOverlapBuffer, BoatExitObstructionMask,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = exitOverlapBuffer[i];
            if (hit == null || hit.isTrigger)
                continue;
            Transform hitTransform = hit.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                continue;
            if (boat != null && (hitTransform == boat.transform || hitTransform.IsChildOf(boat.transform)))
                continue;
            return false;
        }

        return true;
    }

    private void CancelPendingWaterSample()
    {
        // GPU readbacks cannot be cancelled, so invalidate their callbacks. This
        // prevents a boarding transition from being followed by a stale mode switch.
        waterSampleGeneration++;
        waterSamplePending = false;
    }

    private void UpdateAutomaticSwimming()
    {
        if (!AutoEnterSwimming || SwimmingMovement == null || isAboardBoat ||
            Ocean == null || !Ocean.CanSampleSurface || Time.time < nextWaterCheckTime ||
            waterSamplePending)
            return;

        nextWaterCheckTime = Time.time + Mathf.Max(0.05f, WaterCheckInterval);
        waterSamplePending = true;
        int generation = waterSampleGeneration;
        Vector3 samplePosition = CharacterController != null
            ? CharacterController.bounds.center
            : transform.position + Vector3.up * 0.9f;

        pendingSampleGeneration = generation;
        sampleCallback ??= OnWaterSampleReady;
        bool queued = Ocean.RequestSurfaceSample(samplePosition, sampleCallback);

        if (!queued)
            waterSamplePending = false;
    }

    private void OnWaterSampleReady(SpectralOceanFFT.SurfaceSample sample)
    {
        if (pendingSampleGeneration != waterSampleGeneration)
            return;

        waterSamplePending = false;
        if (!isActiveAndEnabled || !sample.IsValid)
            return;

        EvaluateSwimmingThreshold(sample.Height);
    }

    private void EvaluateSwimmingThreshold(float surfaceHeight)
    {
        if (isAboardBoat)
            return;

        float bodyMidpoint = CharacterController != null
            ? CharacterController.bounds.center.y
            : transform.position.y + 0.9f;

        if (!object.ReferenceEquals(activeMovement, SwimmingMovement) && surfaceHeight >= bodyMidpoint)
            EnterSwimming();
        else if (object.ReferenceEquals(activeMovement, SwimmingMovement) &&
                 !SwimmingMovement.IsHopping &&
                 surfaceHeight < bodyMidpoint - Mathf.Max(0f, SwimmingExitHysteresis))
            ExitSwimming();
    }

    private void OnMoved(InputAction.CallbackContext context)
    {
        latestDirection = Vector2.ClampMagnitude(context.ReadValue<Vector2>(), 1f);
    }

    private void OnJumped(InputAction.CallbackContext context)
    {
        if (isAboardBoat)
        {
            if (context.performed)
                LeaveBoatWithJump();
            return;
        }

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
