using Assets._Project.Features.Buoy;
using Assets._Project.Features.ProximityPrompt;
using System.Collections.Generic;
using UnityEngine;

namespace Assets._Project.Features.Boat
{
    /// <summary>
    /// Rigidbody-driven sailing and hull physics. Visual sail animation and wake
    /// particles are deliberately handled by separate components.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(Buoyancy), typeof(BoatSailAnimator))]
    public sealed class BoatController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Rigidbody body;
        [SerializeField] private Buoyancy buoyancy;
        [SerializeField] private SpectralOceanFFT ocean;

        [Header("Interaction")]
        [Tooltip("Where the player is attached while controlling the boat.")]
        [SerializeField] private Transform boardingAnchor;
        [Tooltip("Preferred camera-side exit. If empty, searched by child name ExitAnchorLeft.")]
        [SerializeField] private Transform exitAnchorLeft;
        [Tooltip("Other side exit. If empty, searched by child name ExitAnchorRight.")]
        [SerializeField] private Transform exitAnchorRight;
        [Tooltip("Legacy single exit anchor, retained for older prefabs.")]
        [SerializeField] private Transform exitAnchor;
        [SerializeField, Min(0.5f)] private float interactionRange = 3.25f;
        [SerializeField] private ProximityPromptComponent proximityPrompt;

        private static readonly List<BoatController> ActiveBoats = new List<BoatController>(16);

        public Transform BoardingAnchor => boardingAnchor != null ? boardingAnchor : transform;
        public Transform ExitAnchor => exitAnchor != null ? exitAnchor :
            (exitAnchorLeft != null ? exitAnchorLeft : exitAnchorRight);
        public Transform ExitAnchorLeft => exitAnchorLeft;
        public Transform ExitAnchorRight => exitAnchorRight;
        public ProximityPromptComponent Prompt => proximityPrompt;
        public float InteractionRange => interactionRange;

        [Header("Sailing")]
        [Tooltip("Base acceleration at full wind strength and full sail deployment.")]
        [SerializeField, Min(0f)] private float maximumSailAcceleration = 4.5f;
        [Tooltip("Multiplier applied to the legacy sail force, making older prefabs faster without losing their serialized tuning.")]
        [SerializeField, Min(0.1f)] private float sailPowerMultiplier = 1.7f;
        [SerializeField, Min(0f)] private float sailTrimRate = 0.85f;
        [SerializeField, Range(0f, 1f)] private float startingSailTrim = 0.8f;
        [SerializeField, Range(0f, 89f)] private float noGoZoneHalfAngle = 42f;
        [SerializeField, Min(0f)] private float sailLeewayAcceleration = 0.55f;

        [Header("Sail Rotation Input")]
        [Tooltip("How quickly E/Q rotate the sail relative to the hull, in degrees per second.")]
        [SerializeField, Min(1f)] private float sailRotationSpeed = 72f;
        [SerializeField, Range(0f, 85f)] private float maximumSailRotation = 78f;
        [SerializeField, Range(-85f, 85f)] private float startingSailRotation;

        [Header("Steering")]
        [SerializeField, Min(0f)] private float rudderAcceleration = 2.8f;
        [SerializeField, Min(0.1f)] private float rudderFullEffectSpeed = 2.5f;
        [SerializeField, Range(0f, 1f)] private float lowSpeedRudderFactor = 0.5f;
        [SerializeField, Min(0f)] private float yawDamping = 0.75f;
        [SerializeField, Min(1f)] private float maximumYawRate = 42f;
        [SerializeField, Min(0.1f)] private float maximumYawRateMultiplier = 1.25f;
        [SerializeField, Min(0.1f)] private float rudderRateResponse = 4f;
        [SerializeField, Min(0.1f)] private float rudderResponseMultiplier = 1.35f;
        [SerializeField, Range(0f, 1f)] private float lowSpeedRudderFactorFloor = 0.45f;

        [Header("Hull Handling")]
        [SerializeField, Min(0f)] private float lateralWaterDamping = 1.65f;
        [SerializeField, Min(0f)] private float speedLimitDamping = 1.5f;
        [SerializeField, Min(0.1f)] private float maximumForwardSpeed = 15f;
        [SerializeField, Min(0.1f)] private float speedLimitMultiplier = 1.5f;
        [SerializeField, Min(0f)] private float waterCurrentInfluence = 0.15f;

        [Header("Hull Weight / Wave Response")]
        [Tooltip("The minimum effective Rigidbody mass. A heavier hull is difficult for the player to shove, but still receives acceleration-based sailing and buoyancy forces.")]
        [SerializeField, Min(1f)] private float minimumEffectiveMass = 450f;
        [Tooltip("Fraction of vertical wave velocity that the hull follows. Lower values make the boat feel heavier and reduce bobbing.")]
        [SerializeField, Range(0f, 1f)] private float verticalWaveFollow = 0.18f;
        [Tooltip("Damping that pulls hull vertical velocity toward the reduced wave-follow target.")]
        [SerializeField, Range(0f, 4.5f)] private float verticalBobDamping = 3.5f;
        [SerializeField, Range(0f, 12f)] private float maximumVerticalStabilityAcceleration = 7f;
        [Tooltip("Extra vertical tracking acceleration available as wind-driven wave speed rises.")]
        [SerializeField, Range(0f, 6f)] private float highWindStabilityAccelerationBonus = 3f;
        [Tooltip("Damps pitch and roll only while buoyancy probes confirm the hull is supported by water. It fades to zero when airborne so the boat keeps natural angular motion.")]
        [SerializeField, Min(0f)] private float pitchRollDamping = 3.5f;

        [Header("Airborne Gravity")]
        [SerializeField, Range(0f, 1f)] private float airborneBlend = 0f;
        [Tooltip("Total gravity multiplier when every hull probe is out of the water. Unity gravity is already enabled; this adds only the extra portion.")]
        [SerializeField, Min(1f)] private float airborneGravityMultiplier = 2.0f;
        [Tooltip("Additional gravity multiplier at maximum wind strength, helping the hull return to the rapidly moving water surface.")]
        [SerializeField, Min(0f)] private float highWindAirborneGravityBonus = 0.65f;
        [Tooltip("Probe immersion at which water support is treated as fully established.")]
        [SerializeField, Range(0.01f, 0.5f)] private float fullWaterSupportImmersion = 0.22f;

        [Header("Unoccupied Anchor")]
        [SerializeField] private bool anchorWhenUnoccupied = true;
        [SerializeField, Min(0f)] private float anchorPositionSpring = 12f;
        [SerializeField, Min(0f)] private float anchorPositionDamping = 7f;
        [SerializeField, Min(0f)] private float anchorYawSpring = 5f;
        [SerializeField, Min(0f)] private float anchorYawDamping = 3f;

        [Header("Capsize Recovery")]
        [SerializeField, Range(-1f, 0f)] private float upsideDownDotThreshold = -0.65f;
        [SerializeField, Min(0f)] private float upsideDownConfirmationTime = 0.35f;
        [SerializeField, Range(0f, 1f)] private float rightingCompleteDotThreshold = 0.82f;
        [SerializeField, Min(0f)] private float rightingTorqueStrength = 18f;
        [SerializeField, Min(0f)] private float rightingTorqueDamping = 6f;
        [SerializeField, Min(0.1f)] private float maximumRightingAngularAcceleration = 30f;

        [Header("Runtime State")]
        [SerializeField, Range(0f, 1f)] private float sailTrim;
        [SerializeField, Range(-85f, 85f)] private float sailRotationDegrees;
        [SerializeField] private bool isAnchored = true;

        private Vector2 movementInput;
        private float sailRotationInput;
        private Vector3 anchorWorldPosition;
        private float anchorWorldYaw;
        private PlayerController aboardPlayer;
        private float upsideDownTimer;
        private bool isRecoveringFromCapsize;

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

                // A capsized boat is temporarily not interactable, but it remains
                // registered so it becomes discoverable again when righting ends.
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

        private void Awake()
        {
            ResolveReferences();
            ResolveExitAnchors();

            if (proximityPrompt == null)
                proximityPrompt = GetComponent<ProximityPromptComponent>();
            if (proximityPrompt == null)
                proximityPrompt = gameObject.AddComponent<ProximityPromptComponent>();
            proximityPrompt.SetVisible(false);

            sailTrim = startingSailTrim;
            sailRotationDegrees = startingSailRotation;

            // Keep sail visuals in their own component while ensuring a boat prefab
            // receives the animator automatically when upgraded from an older setup.
            if (GetComponent<BoatSailAnimator>() == null)
                gameObject.AddComponent<BoatSailAnimator>();

            if (body != null)
            {
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.maxAngularVelocity = Mathf.Max(body.maxAngularVelocity, 8f);
                if (body.useGravity == false)
                    body.useGravity = true;
                if (body.mass < minimumEffectiveMass)
                {
                    body.mass = minimumEffectiveMass;
                    body.ResetInertiaTensor();
                }
            }

            if (anchorWhenUnoccupied)
                Anchor();
        }

        private void OnValidate()
        {
            maximumSailAcceleration = Mathf.Max(0f, maximumSailAcceleration);
            sailPowerMultiplier = Mathf.Max(0.1f, sailPowerMultiplier);
            sailTrimRate = Mathf.Max(0f, sailTrimRate);
            sailRotationSpeed = Mathf.Max(1f, sailRotationSpeed);
            maximumSailRotation = Mathf.Clamp(maximumSailRotation, 0f, 85f);
            rudderAcceleration = Mathf.Max(0f, rudderAcceleration);
            rudderFullEffectSpeed = Mathf.Max(0.1f, rudderFullEffectSpeed);
            lowSpeedRudderFactor = Mathf.Clamp01(lowSpeedRudderFactor);
            yawDamping = Mathf.Max(0f, yawDamping);
            maximumYawRate = Mathf.Max(1f, maximumYawRate);
            maximumYawRateMultiplier = Mathf.Max(0.1f, maximumYawRateMultiplier);
            rudderRateResponse = Mathf.Max(0.1f, rudderRateResponse);
            rudderResponseMultiplier = Mathf.Max(0.1f, rudderResponseMultiplier);
            lowSpeedRudderFactorFloor = Mathf.Clamp01(lowSpeedRudderFactorFloor);
            lateralWaterDamping = Mathf.Max(0f, lateralWaterDamping);
            speedLimitDamping = Mathf.Max(0f, speedLimitDamping);
            maximumForwardSpeed = Mathf.Max(0.1f, maximumForwardSpeed);
            speedLimitMultiplier = Mathf.Max(0.1f, speedLimitMultiplier);
            waterCurrentInfluence = Mathf.Max(0f, waterCurrentInfluence);
            minimumEffectiveMass = Mathf.Max(1f, minimumEffectiveMass);
            verticalBobDamping = Mathf.Max(0f, verticalBobDamping);
            maximumVerticalStabilityAcceleration = Mathf.Max(0f, maximumVerticalStabilityAcceleration);
            highWindStabilityAccelerationBonus = Mathf.Max(0f, highWindStabilityAccelerationBonus);
            pitchRollDamping = Mathf.Max(0f, pitchRollDamping);
            airborneGravityMultiplier = Mathf.Max(1f, airborneGravityMultiplier);
            highWindAirborneGravityBonus = Mathf.Max(0f, highWindAirborneGravityBonus);
            fullWaterSupportImmersion = Mathf.Clamp(fullWaterSupportImmersion, 0.01f, 0.5f);
            anchorPositionSpring = Mathf.Max(0f, anchorPositionSpring);
            anchorPositionDamping = Mathf.Max(0f, anchorPositionDamping);
            anchorYawSpring = Mathf.Max(0f, anchorYawSpring);
            anchorYawDamping = Mathf.Max(0f, anchorYawDamping);
            upsideDownConfirmationTime = Mathf.Max(0f, upsideDownConfirmationTime);
            rightingTorqueStrength = Mathf.Max(0f, rightingTorqueStrength);
            rightingTorqueDamping = Mathf.Max(0f, rightingTorqueDamping);
            maximumRightingAngularAcceleration = Mathf.Max(0.1f, maximumRightingAngularAcceleration);
            sailTrim = Mathf.Clamp01(sailTrim);
            sailRotationDegrees = Mathf.Clamp(sailRotationDegrees, -maximumSailRotation, maximumSailRotation);
        }

        private void FixedUpdate()
        {
            if (body == null)
                return;

            float dt = Time.fixedDeltaTime;
            UpdateCapsizeState(dt);
            if (isRecoveringFromCapsize)
            {
                movementInput = Vector2.zero;
                sailRotationInput = 0f;
                isAnchored = false;
                ApplyRightingTorque();
                return;
            }

            ApplyWaveStability();

            if (isAnchored)
            {
                movementInput = Vector2.zero;
                sailRotationInput = 0f;
                ApplyAnchorHold();
                return;
            }

            float steering = Mathf.Clamp(movementInput.x, -1f, 1f);
            float trimInput = Mathf.Clamp(movementInput.y, -1f, 1f);
            sailTrim = Mathf.Clamp01(sailTrim + trimInput * sailTrimRate * dt);
            sailRotationDegrees = Mathf.Clamp(
                sailRotationDegrees + sailRotationInput * sailRotationSpeed * dt,
                -maximumSailRotation, maximumSailRotation);

            ApplySailingForce();
            ApplyRudder(steering);
            ApplyHullResistance();
        }

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
                    body.WakeUp();
            }
            else
            {
                aboardPlayer = null;
                ReleaseControls();
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
            isAnchored = true;
            ReleaseControls();
        }

        public void Unanchor()
        {
            isAnchored = false;
            if (body != null)
                body.WakeUp();
        }

        private void UpdateCapsizeState(float dt)
        {
            float upAlignment = Vector3.Dot(body.rotation * Vector3.up, Vector3.up);

            if (!isRecoveringFromCapsize)
            {
                if (upAlignment <= upsideDownDotThreshold)
                    upsideDownTimer += dt;
                else
                    upsideDownTimer = 0f;

                if (upsideDownTimer >= upsideDownConfirmationTime)
                    BeginCapsizeRecovery();
            }
            else if (upAlignment >= rightingCompleteDotThreshold)
            {
                CompleteCapsizeRecovery();
            }
        }

        private void BeginCapsizeRecovery()
        {
            isRecoveringFromCapsize = true;
            isAnchored = false;
            ReleaseControls();
            proximityPrompt?.SetVisible(false);

            PlayerController player = aboardPlayer;
            if (player != null)
                player.ForceUnboardFromBoat(this, null);

            aboardPlayer = null;
            isAnchored = false;
            if (body != null)
                body.WakeUp();
        }

        private void CompleteCapsizeRecovery()
        {
            // Normalize pitch and roll once the damped righting torque has nearly
            // succeeded. This makes the boat consistently boardable instead of
            // lingering just below the recovery threshold due to wave forces.
            Vector3 flatForward = Vector3.ProjectOnPlane(body.rotation * Vector3.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = Vector3.ProjectOnPlane(body.rotation * Vector3.right, Vector3.up);
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = Vector3.forward;

            Quaternion uprightRotation = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
            body.MoveRotation(uprightRotation);
            body.angularVelocity = Vector3.Project(body.angularVelocity, Vector3.up);
            body.WakeUp();

            isRecoveringFromCapsize = false;
            upsideDownTimer = 0f;
            proximityPrompt?.SetVisible(false);
            if (anchorWhenUnoccupied && aboardPlayer == null)
            {
                // Cache the yaw of the target upright rotation (not the old tilted
                // body rotation that MoveRotation is about to replace).
                anchorWorldPosition = body.position;
                anchorWorldYaw = uprightRotation.eulerAngles.y;
                isAnchored = true;
                ReleaseControls();
            }
            else
            {
                isAnchored = false;
            }
        }

        private void ApplyRightingTorque()
        {
            Vector3 currentUp = (body.rotation * Vector3.up).normalized;
            Quaternion correction = Quaternion.FromToRotation(currentUp, Vector3.up);
            correction.ToAngleAxis(out float angleDegrees, out Vector3 axis);

            if (angleDegrees > 180f)
                angleDegrees -= 360f;
            if (axis.sqrMagnitude < 0.0001f)
            {
                axis = Vector3.ProjectOnPlane(body.rotation * Vector3.right, Vector3.up);
                if (axis.sqrMagnitude < 0.0001f)
                    axis = Vector3.right;
            }
            axis.Normalize();

            Vector3 pitchRollAngularVelocity = Vector3.ProjectOnPlane(body.angularVelocity, Vector3.up);
            Vector3 angularAcceleration = axis * (angleDegrees * Mathf.Deg2Rad * rightingTorqueStrength)
                                          - pitchRollAngularVelocity * rightingTorqueDamping;
            angularAcceleration = Vector3.ClampMagnitude(
                angularAcceleration, maximumRightingAngularAcceleration);
            body.AddTorque(angularAcceleration, ForceMode.Acceleration);
        }

        private void ApplyWaveStability()
        {
            float windStrength = ocean != null ? Mathf.Clamp01(ocean.CurrentWindStrength) : 0f;
            float waterImmersion = buoyancy != null ? buoyancy.CurrentWaterImmersion : 0f;
            float waterSupport = Mathf.SmoothStep(
                0f, fullWaterSupportImmersion,
                Mathf.Clamp(waterImmersion, 0f, fullWaterSupportImmersion));
            if (waterSupport > 0.001f && buoyancy != null && buoyancy.TryGetWaterSurface(
                    out _, out _, out Vector3 waterVelocity, out _))
            {
                // Follow wave velocity only while the hull is actually supported.
                // At high wind, both the readback velocity limit and available
                // stability acceleration rise so the boat does not lag behind a
                // fast-moving crest and then appear to hang above the water.
                // High wind increases the target only slightly. Large negative
                // wave velocities must not become an artificial force that drives
                // the boat under the water faster than buoyancy can recover it.
                float windFollow = Mathf.Lerp(0.9f, 1.0f, windStrength);
                float targetVerticalVelocity = waterVelocity.y * verticalWaveFollow * windFollow;
                float verticalAcceleration = (targetVerticalVelocity - body.linearVelocity.y)
                    * verticalBobDamping * waterSupport;
                float maxAcceleration = maximumVerticalStabilityAcceleration
                    + highWindStabilityAccelerationBonus * windStrength;
                verticalAcceleration = Mathf.Clamp(
                    verticalAcceleration, -Mathf.Min(3.5f, maxAcceleration), maxAcceleration);
                body.AddForce(Vector3.up * verticalAcceleration, ForceMode.Acceleration);
            }

            // Native Rigidbody gravity remains active. Add a controlled extra
            // component while unsupported so a gust cannot leave the hull hanging
            // in the air after the wave has moved away. This fades out as soon as
            // the buoyancy probes touch water again.
            if (airborneBlend > 0.001f && body.useGravity)
            {
                float gravityMultiplier = airborneGravityMultiplier
                    + highWindAirborneGravityBonus * windStrength;
                float extraGravity = Mathf.Max(0f, gravityMultiplier - 1f) * airborneBlend;
                body.AddForce(Physics.gravity * extraGravity, ForceMode.Acceleration);
            }

            // Crucially, do not damp pitch/roll when airborne. The previous always-on
            // damping made the hull look frozen in mid-air, with only player rudder
            // torque appearing to rotate it. Water-supported angular damping fades
            // continuously with the actual probe immersion.
            Vector3 pitchRollVelocity = Vector3.ProjectOnPlane(body.angularVelocity, Vector3.up);
            float effectivePitchRollDamping = pitchRollDamping * waterSupport;
            if (effectivePitchRollDamping > 0.001f && pitchRollVelocity.sqrMagnitude > 0.0001f)
                body.AddTorque(-pitchRollVelocity * effectivePitchRollDamping, ForceMode.Acceleration);
        }

        private void ApplyAnchorHold()
        {
            Vector3 positionError = anchorWorldPosition - body.position;
            positionError.y = 0f;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            Vector3 anchorAcceleration = positionError * anchorPositionSpring
                                         - planarVelocity * anchorPositionDamping;
            body.AddForce(anchorAcceleration, ForceMode.Acceleration);

            float yawError = Mathf.DeltaAngle(body.rotation.eulerAngles.y, anchorWorldYaw) * Mathf.Deg2Rad;
            float yawRate = Vector3.Dot(body.angularVelocity, Vector3.up);
            float yawAcceleration = yawError * anchorYawSpring - yawRate * anchorYawDamping;
            body.AddTorque(Vector3.up * yawAcceleration, ForceMode.Acceleration);
        }

        private void ResolveReferences()
        {
            if (body == null)
                body = GetComponent<Rigidbody>();
            if (buoyancy == null)
                buoyancy = GetComponent<Buoyancy>();
            if (ocean == null)
                ocean = Object.FindAnyObjectByType<SpectralOceanFFT>();
        }

        private void ResolveExitAnchors()
        {
            if (exitAnchorLeft == null)
                exitAnchorLeft = transform.Find("ExitAnchorLeft");
            if (exitAnchorRight == null)
                exitAnchorRight = transform.Find("ExitAnchorRight");

            // Older boat prefabs serialized only one exit transform. Keep it as a
            // fallback; the two named side anchors take precedence when available.
            if (exitAnchorLeft == null && exitAnchorRight == null && exitAnchor != null)
                exitAnchorRight = exitAnchor;
        }

        private void ApplySailingForce()
        {
            if (ocean == null || sailTrim <= 0.001f || maximumSailAcceleration <= 0f)
                return;

            Vector2 windXZ = ocean.CurrentWindDirectionXZ;
            if (windXZ.sqrMagnitude < 0.0001f)
                return;

            float windStrength = Mathf.Clamp01(ocean.CurrentWindStrength);
            if (windStrength <= 0.001f)
                return;

            Vector3 windDirection = new Vector3(windXZ.x, 0f, windXZ.y).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            float intoWindAlignment = Vector3.Dot(forward, -windDirection);
            float noGoStart = Mathf.Cos(noGoZoneHalfAngle * Mathf.Deg2Rad);
            float noGoEnd = Mathf.Clamp01(noGoStart - 0.28f);
            float noGoFactor = 1f - Mathf.SmoothStep(noGoEnd, noGoStart, intoWindAlignment);

            Vector3 sailHeading = Quaternion.AngleAxis(sailRotationDegrees, Vector3.up) * forward;
            Vector3 sailNormal = Vector3.Cross(Vector3.up, sailHeading).normalized;
            float sailWindCapture = Mathf.Abs(Vector3.Dot(windDirection, sailNormal));
            float sailAngleEfficiency = Mathf.Lerp(0.48f, 1f, sailWindCapture);
            float windAngleFactor = Mathf.Lerp(0.72f, 1f,
                Mathf.Abs(Vector3.Dot(forward, windDirection)));

            float driveAcceleration = maximumSailAcceleration
                * sailPowerMultiplier
                * windStrength
                * sailTrim
                * noGoFactor
                * windAngleFactor
                * sailAngleEfficiency;

            body.AddForce(forward * driveAcceleration, ForceMode.Acceleration);

            float windSide = Vector3.Dot(windDirection, transform.right);
            float sailSideFactor = Mathf.Lerp(0.55f, 1f, sailWindCapture);
            body.AddForce(
                transform.right * (windSide * sailLeewayAcceleration * windStrength * sailTrim
                                   * noGoFactor * sailSideFactor),
                ForceMode.Acceleration);
        }

        private void ApplyRudder(float steering)
        {
            Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            float speedFactor = Mathf.Clamp01(planarVelocity.magnitude / rudderFullEffectSpeed);
            float lowSpeedAuthority = Mathf.Max(lowSpeedRudderFactor, lowSpeedRudderFactorFloor);
            float rudderAuthority = Mathf.Lerp(lowSpeedAuthority, 1f, speedFactor);
            float desiredYawRate = steering * maximumYawRate * maximumYawRateMultiplier
                                   * Mathf.Deg2Rad * rudderAuthority;
            float currentYawRate = Vector3.Dot(body.angularVelocity, Vector3.up);
            float yawAcceleration = (desiredYawRate - currentYawRate)
                                    * rudderRateResponse * rudderResponseMultiplier;
            float accelerationLimit = Mathf.Max(1f, rudderAcceleration * 3f);
            yawAcceleration = Mathf.Clamp(yawAcceleration, -accelerationLimit, accelerationLimit);
            body.AddTorque(Vector3.up * yawAcceleration, ForceMode.Acceleration);
            body.AddTorque(Vector3.up * (-currentYawRate * yawDamping * 0.25f), ForceMode.Acceleration);
        }

        private void ApplyHullResistance()
        {
            Vector3 relativeVelocity = body.linearVelocity;
            if (buoyancy != null && buoyancy.TryGetWaterSurface(
                    out _, out _, out Vector3 waterVelocity, out _))
                relativeVelocity -= waterVelocity * waterCurrentInfluence;

            float lateralSpeed = Vector3.Dot(relativeVelocity, transform.right);
            body.AddForce(-transform.right * (lateralSpeed * lateralWaterDamping), ForceMode.Acceleration);

            float forwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);
            float effectiveMaximumSpeed = maximumForwardSpeed * speedLimitMultiplier;
            if (forwardSpeed > effectiveMaximumSpeed)
            {
                float excessSpeed = forwardSpeed - effectiveMaximumSpeed;
                body.AddForce(-transform.forward * (excessSpeed * speedLimitDamping), ForceMode.Acceleration);
            }
        }
    }
}
