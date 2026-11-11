using Assets._Project.Features.Buoy;
using Assets._Project.Features.ProximityPrompt;
using System.Collections.Generic;
using UnityEngine;

namespace Assets._Project.Features.Boat
{
    /// <summary>
    /// Rigidbody sailboat (a canoe with a sail). Physics is split by concern:
    ///   BoatController.cs          references, interaction, input, pipeline, anchor, capsize
    ///   BoatController.Hull.cs     weights (mass / centre of mass / inertia) and the three rotation axes
    ///   BoatController.Sailing.cs  sail, rudder, hull drag
    ///   BoatController.Waves.cs    wave slope follow, wave drift, heave, airborne and landing
    ///
    /// Design notes (see Plan Part F):
    ///  * Mass properties are owned here. Parenting the player to the hull adds colliders, and
    ///    Unity would silently recompute the automatic centre of mass and inertia. The same lesson
    ///    as Boat Attack's BuoyantObject, which sets rb.centerOfMass explicitly.
    ///  * Damping scales with immersion (Boat Attack scales drag / angular drag with percent submerged).
    ///  * Steering is a speed-dependent rudder (yaw rate target) that also leans the hull into the turn.
    ///  * Axes are named like aircraft rotations: Yaw (up), Pitch (right), Roll (forward).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(Buoyancy), typeof(BoatSailAnimator))]
    public sealed partial class BoatController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Rigidbody body;
        [SerializeField] private Buoyancy buoyancy;
        [SerializeField] private SpectralOceanFFT ocean;

        [Header("Interaction")]
        [Tooltip("Where the player is attached while controlling the boat. Also where crew weight acts.")]
        [SerializeField] private Transform boardingAnchor;
        [SerializeField] private Transform exitAnchorLeft;
        [SerializeField] private Transform exitAnchorRight;
        [SerializeField, Min(0.5f)] private float interactionRange = 3.25f;
        [SerializeField] private ProximityPromptComponent proximityPrompt;

        [Header("Anchor (while unoccupied)")]
        [SerializeField] private bool anchorWhenUnoccupied = true;
        [Tooltip("Spring pulling a moored boat to its anchor point (1/s^2).")]
        [SerializeField, Min(0f)] private float anchorSpring = 12f;
        [SerializeField, Min(0f)] private float anchorDamping = 7f;

        [Header("Effects")]
        [Tooltip("Adds BoatFoamEmitter at runtime so the hull leaves foam on the water surface.")]
        [SerializeField] private bool spawnContactFoam = true;

        [Header("Capsize Recovery")]
        [Tooltip("Tilt from upright (degrees) beyond which the hull counts as capsized.")]
        [SerializeField, Range(100f, 175f)] private float capsizeAngle = 130f;
        [Tooltip("Seconds the hull must stay capsized before recovery starts.")]
        [SerializeField, Min(0f)] private float capsizeConfirmTime = 0.35f;
        [Tooltip("How long (seconds) the boat takes to turn back upright once recovery starts. Higher = slower, more visible righting.")]
        [SerializeField, Range(0.5f, 30f)] private float capsizeRecoveryTime = 6f;
        [Tooltip("Righting acceleration used while capsized (rad/s^2 per radian of tilt).")]
        [SerializeField, Min(0f)] private float capsizeRightingTorque = 18f;
        [SerializeField, Min(0f)] private float capsizeRightingDamping = 6f;

        [Header("Runtime State")]
        [SerializeField, Range(0f, 1f)] private float sailTrim;
        [SerializeField, Range(-85f, 85f)] private float sailRotationDegrees;
        [SerializeField] private bool isAnchored = true;

        private const float CapsizeCompleteDot = 0.82f;
        private const float FullSupportImmersion = 0.22f;

        private static readonly List<BoatController> ActiveBoats = new List<BoatController>(16);

        private Vector2 movementInput;
        private float sailRotationInput;
        private Vector3 anchorWorldPosition;
        private float anchorWorldYaw;
        private PlayerController aboardPlayer;
        private float upsideDownTimer;
        private bool isRecoveringFromCapsize;
        private float recoveryElapsed;
        private float recoveryStartAngle;

        // ---- public API (kept for PlayerController, BoatMovement, sail animator, wake, hint UI) ----
        public Transform BoardingAnchor => boardingAnchor != null ? boardingAnchor : transform;
        public Transform ExitAnchor => exitAnchorLeft != null ? exitAnchorLeft : exitAnchorRight;
        public Transform ExitAnchorLeft => exitAnchorLeft;
        public Transform ExitAnchorRight => exitAnchorRight;
        public ProximityPromptComponent Prompt => proximityPrompt;
        public float InteractionRange => interactionRange;
        public Rigidbody Body => body;
        public Buoyancy Buoyancy => buoyancy;
        public float SailTrim => sailTrim;
        public float SailRotationDegrees => sailRotationDegrees;
        public Vector2 MovementInput => movementInput;
        public float SteeringInput => movementInput.x;
        public float SailTrimInput => movementInput.y;
        public float ForwardSpeed => body != null ? Vector3.Dot(body.linearVelocity, transform.forward) : 0f;
        public Vector3 Velocity => body != null ? body.linearVelocity : Vector3.zero;
        public float PlanarSpeed => Vector3.ProjectOnPlane(Velocity, Vector3.up).magnitude;
        public bool HasOcean => ocean != null;
        public bool IsAnchored => isAnchored;
        public bool IsRecoveringFromCapsize => isRecoveringFromCapsize;
        public bool HasPlayerAboard => aboardPlayer != null;

        private void Reset()
        {
            body = GetComponent<Rigidbody>();
            buoyancy = GetComponent<Buoyancy>();
            proximityPrompt = GetComponent<ProximityPromptComponent>();
            ResolveExitAnchors();
        }

        private void Awake()
        {
            ResolveReferences();
            ResolveExitAnchors();

            if (proximityPrompt == null)
                proximityPrompt = GetComponent<ProximityPromptComponent>();
            if (proximityPrompt == null)
                proximityPrompt = gameObject.AddComponent<ProximityPromptComponent>();
            proximityPrompt.SetVisible(false);

            if (GetComponent<BoatSailAnimator>() == null)
                gameObject.AddComponent<BoatSailAnimator>();

            sailTrim = startingSailTrim;
            sailRotationDegrees = startingSailRotation;
            ResetWaveState();

            if (body != null)
            {
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.useGravity = true;
                ApplyMassProperties(true);
            }

            if (anchorWhenUnoccupied)
                Anchor();
        }

        private void OnEnable()
        {
            if (!ActiveBoats.Contains(this))
                ActiveBoats.Add(this);
        }

        private void OnDisable()
        {
            ActiveBoats.Remove(this);
            movementInput = Vector2.zero;
            sailRotationInput = 0f;
            proximityPrompt?.SetVisible(false);
        }

        private void OnValidate()
        {
            sailTrim = Mathf.Clamp01(sailTrim);
            sailRotationDegrees = Mathf.Clamp(sailRotationDegrees, -maximumSailRotation, maximumSailRotation);
            OnValidateHull();
            OnValidateSailing();
            OnValidateWaves();
        }

        /// <summary>Adding/removing child colliders (boarding) must never change the hull's mass distribution.</summary>
        private void OnTransformChildrenChanged()
        {
            if (body != null)
                ApplyMassProperties(true);
        }

        public static BoatController FindNearest(Vector3 position, out float distance)
        {
            BoatController nearest = null;
            float bestSqr = float.PositiveInfinity;
            for (int i = ActiveBoats.Count - 1; i >= 0; i--)
            {
                BoatController candidate = ActiveBoats[i];
                if (candidate == null || !candidate.isActiveAndEnabled)
                {
                    ActiveBoats.RemoveAt(i);
                    continue;
                }

                // A capsized boat is temporarily not interactable but stays registered.
                if (candidate.isRecoveringFromCapsize)
                    continue;

                Vector3 interactionPosition = candidate.boardingAnchor != null
                    ? candidate.boardingAnchor.position
                    : candidate.transform.position;
                float sqr = (interactionPosition - position).sqrMagnitude;
                float range = candidate.interactionRange;
                if (sqr <= range * range && sqr < bestSqr)
                {
                    bestSqr = sqr;
                    nearest = candidate;
                }
            }

            distance = nearest != null ? Mathf.Sqrt(bestSqr) : float.PositiveInfinity;
            return nearest;
        }

        // ------------------------------------------------------------------ physics pipeline

        private void FixedUpdate()
        {
            if (body == null)
                return;

            float dt = Time.fixedDeltaTime;
            if (dt <= 0f)
                return;

            UpdateCapsizeState(dt);
            if (isRecoveringFromCapsize)
            {
                movementInput = Vector2.zero;
                sailRotationInput = 0f;
                isAnchored = false;
                ApplyCapsizeRighting();
                return;
            }

            MaintainMassProperties();
            CaptureHullState(dt);       // one consistent snapshot for every force below
            SyncBuoyancyModel();        // slope follow + heave damping live in the buoyancy probes
            LimitVerticalLaunch(dt);
            UpdateWaveDrift(dt);
            ApplyHeaveAndGravity();

            if (isAnchored)
            {
                movementInput = Vector2.zero;
                sailRotationInput = 0f;
                sailHeelLoad = 0f;
                sailDriveNormalized = 0f;
                ApplyAnchorHold();
                ApplyAttitude(0f, dt);
                return;
            }

            float steering = Mathf.Clamp(movementInput.x, -1f, 1f);
            float trimInput = Mathf.Clamp(movementInput.y, -1f, 1f);
            sailTrim = Mathf.Clamp01(sailTrim + trimInput * sailTrimRate * dt);
            sailRotationDegrees = Mathf.Clamp(
                sailRotationDegrees + sailRotationInput * sailRotationSpeed * dt,
                -maximumSailRotation, maximumSailRotation);

            ApplySail();
            ApplyRudder(steering);
            ApplyHullDrag();
            ApplyWaveDriftForce();
            ApplyAttitude(steering, dt);
        }

        // ------------------------------------------------------------------ input

        public void SetMovementInput(Vector2 input)
        {
            movementInput = Vector2.ClampMagnitude(input, 1f);
        }

        public void SetSailRotationInput(float input)
        {
            sailRotationInput = isRecoveringFromCapsize ? 0f : Mathf.Clamp(input, -1f, 1f);
        }

        public void SetSailTrim(float value) => sailTrim = Mathf.Clamp01(value);
        public void AddSailTrim(float amount) => sailTrim = Mathf.Clamp01(sailTrim + amount);

        public void ReleaseControls()
        {
            movementInput = Vector2.zero;
            sailRotationInput = 0f;
        }

        public void SetPlayerAboard(bool aboard) => SetPlayerAboard(aboard, null);

        public void SetPlayerAboard(bool aboard, PlayerController player)
        {
            if (aboard)
            {
                aboardPlayer = player;
                isAnchored = false;
                movementInput = Vector2.zero;
                sailRotationInput = 0f;
                proximityPrompt?.SetVisible(false);
                if (body != null)
                {
                    ApplyMassProperties(true);   // crew weight in, collider side effects out
                    body.WakeUp();
                }
            }
            else
            {
                aboardPlayer = null;
                ReleaseControls();
                if (body != null)
                    ApplyMassProperties(true);
                if (anchorWhenUnoccupied && !isRecoveringFromCapsize)
                    Anchor();
                else
                    isAnchored = false;
            }
        }

        public void Anchor()
        {
            if (body == null)
                ResolveReferences();
            if (body == null)
                return;

            anchorWorldPosition = body.position;
            anchorWorldYaw = body.rotation.eulerAngles.y;
            CaptureWaveDriftAnchorReference();
            isAnchored = true;
            ReleaseControls();
        }

        public void Unanchor()
        {
            isAnchored = false;
            if (body != null)
                body.WakeUp();
        }

        // ------------------------------------------------------------------ anchor

        private void ApplyAnchorHold()
        {
            TryGetAnchoredDrift(out Vector3 driftOffset, out Vector3 driftVelocity);
            Vector3 positionError = anchorWorldPosition + driftOffset - body.position;
            positionError.y = 0f;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up) - driftVelocity;
            body.AddForce(positionError * anchorSpring - planarVelocity * anchorDamping,
                ForceMode.Acceleration);

            float yawError = Mathf.DeltaAngle(body.rotation.eulerAngles.y, anchorWorldYaw) * Mathf.Deg2Rad;
            float yawRate = Vector3.Dot(body.angularVelocity, Vector3.up);
            body.AddTorque(Vector3.up * (yawError * anchorSpring * 0.42f - yawRate * anchorDamping * 0.43f),
                ForceMode.Acceleration);
        }

        // ------------------------------------------------------------------ capsize

        private void UpdateCapsizeState(float dt)
        {
            float upAlignment = Vector3.Dot(body.rotation * Vector3.up, Vector3.up);

            if (!isRecoveringFromCapsize)
            {
                float capsizeDot = Mathf.Cos(capsizeAngle * Mathf.Deg2Rad);
                upsideDownTimer = upAlignment <= capsizeDot ? upsideDownTimer + dt : 0f;
                if (upsideDownTimer >= capsizeConfirmTime)
                    BeginCapsizeRecovery();
            }
            else
            {
                recoveryElapsed += dt;
                bool timeDone = recoveryElapsed >= capsizeRecoveryTime;
                if ((timeDone && upAlignment >= CapsizeCompleteDot) || recoveryElapsed >= capsizeRecoveryTime + 4f)
                    CompleteCapsizeRecovery();
            }
        }

        private void BeginCapsizeRecovery()
        {
            isRecoveringFromCapsize = true;
            recoveryElapsed = 0f;
            recoveryStartAngle = Vector3.Angle(body.rotation * Vector3.up, Vector3.up);
            isAnchored = false;
            ReleaseControls();
            proximityPrompt?.SetVisible(false);

            PlayerController player = aboardPlayer;
            if (player != null)
                player.ForceUnboardFromBoat(this, null);

            aboardPlayer = null;
            ApplyMassProperties(true);
            body.WakeUp();
        }

        private void CompleteCapsizeRecovery()
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(body.rotation * Vector3.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = Vector3.ProjectOnPlane(body.rotation * Vector3.right, Vector3.up);
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = Vector3.forward;

            Quaternion upright = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
            body.MoveRotation(upright);
            body.angularVelocity = Vector3.Project(body.angularVelocity, Vector3.up);
            body.WakeUp();

            isRecoveringFromCapsize = false;
            upsideDownTimer = 0f;
            proximityPrompt?.SetVisible(false);
            if (anchorWhenUnoccupied && aboardPlayer == null)
            {
                anchorWorldPosition = body.position;
                anchorWorldYaw = upright.eulerAngles.y;
                CaptureWaveDriftAnchorReference();
                isAnchored = true;
                ReleaseControls();
            }
            else
            {
                isAnchored = false;
            }
        }

        private void ApplyCapsizeRighting()
        {
            Vector3 currentUp = (body.rotation * Vector3.up).normalized;
            Quaternion.FromToRotation(currentUp, Vector3.up).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
                angle -= 360f;
            if (axis.sqrMagnitude < 0.0001f)
            {
                axis = Vector3.ProjectOnPlane(body.rotation * Vector3.right, Vector3.up);
                if (axis.sqrMagnitude < 0.0001f)
                    axis = Vector3.right;
            }
            axis.Normalize();

            Vector3 pitchRoll = Vector3.ProjectOnPlane(body.angularVelocity, Vector3.up);
            // Follow a time-based tilt schedule so recovery lasts capsizeRecoveryTime instead of snapping.
            float t = Mathf.Clamp01(recoveryElapsed / Mathf.Max(0.01f, capsizeRecoveryTime));
            float desiredAngle = recoveryStartAngle * (1f - t * t * (3f - 2f * t));
            float error = Mathf.Max(0f, angle - desiredAngle);
            Vector3 accel = axis * (error * Mathf.Deg2Rad * capsizeRightingTorque) - pitchRoll * capsizeRightingDamping;
            body.AddTorque(Vector3.ClampMagnitude(accel, 30f), ForceMode.Acceleration);
        }

        // ------------------------------------------------------------------ helpers

        private void ResolveReferences()
        {
            if (body == null)
                body = GetComponent<Rigidbody>();
            if (buoyancy == null)
                buoyancy = GetComponent<Buoyancy>();
            if (ocean == null)
                ocean = Object.FindAnyObjectByType<SpectralOceanFFT>();
            // Soft foam where the hull touches the water (see Features/WaterEffects).
            if (Application.isPlaying && spawnContactFoam && GetComponent<WaterEffects.BoatFoamEmitter>() == null)
                gameObject.AddComponent<WaterEffects.BoatFoamEmitter>();
        }

        private void ResolveExitAnchors()
        {
            if (exitAnchorLeft == null)
                exitAnchorLeft = transform.Find("ExitAnchorLeft");
            if (exitAnchorRight == null)
                exitAnchorRight = transform.Find("ExitAnchorRight");
        }
    }
}
