using UnityEngine;

namespace Assets._Project.Features.Boat
{
    /// <summary>
    /// How the sea acts on the hull besides tilting it:
    ///  * Heave (bob) attenuation, in the probes (heaveProbeDamping) and on the hull (linear + quadratic).
    ///  * Wave drift: like a Gerstner wave, the hull circles with the water particle under it and is
    ///    back where it started after every wave cycle (no net travel).
    ///  * Airborne gravity, landing levelling, water-entry cushion and a launch limiter so a strong wave
    ///    cannot throw the boat into the sky.
    /// Wave-slope follow (roll / pitch) is configured on the axes and applied inside the buoyancy probes.
    /// </summary>
    public sealed partial class BoatController
    {
        [Header("Heave (bob attenuation)")]
        [Tooltip("Multiplier on the buoyancy probes' water drag. Higher = less bobbing and ringing.")]
        [SerializeField, Range(1f, 4f)] private float heaveProbeDamping = 2f;
        [Tooltip("Fraction of the water's vertical speed the hull follows. Lower = heavier, calmer hull.")]
        [SerializeField, Range(0f, 1f)] private float heaveWaveFollow = 0.5f;
        [Tooltip("Pulls the hull's vertical speed toward the followed wave speed (1/s).")]
        [SerializeField, Range(0f, 4f)] private float heaveDamping = 1.5f;
        [Tooltip("Extra damping that is strong only for big vertical swings (1/m).")]
        [SerializeField, Range(0f, 1f)] private float heaveQuadraticDamping = 0.12f;
        [Tooltip("Fraction of water damping that remains while the hull is clear of the water.")]
        [SerializeField, Range(0f, 1f)] private float airborneDampingFraction = 0.1f;

        [Header("Wave Drift (side-to-side wave motion)")]
        [SerializeField] private bool enableWaveDrift = true;
        [Tooltip("Fraction of the water particle's horizontal motion the hull follows.")]
        [SerializeField, Range(0f, 1.5f)] private float waveDriftFollow = 0.6f;
        [Tooltip("Response frequency, rad/s. Waves are 0.4-1.6: lower = a heavier, lagging hull.")]
        [SerializeField, Range(0.5f, 8f)] private float waveDriftResponse = 2.6f;
        [Tooltip("Depth below the centre of mass where the push acts. Beam waves roll the hull more with a greater depth.")]
        [SerializeField, Range(0f, 1.5f)] private float waveDriftDepth = 0.35f;
        [Tooltip("A moored boat circles its anchor with the waves.")]
        [SerializeField] private bool driftAffectsAnchoredBoat = true;

        [Header("Airborne & Landing")]
        [Tooltip("Total gravity multiplier once the hull has been clear of the water for a while.")]
        [SerializeField, Range(1f, 4f)] private float airborneGravity = 2f;
        [Tooltip("Seconds clear of the water before extra gravity starts. Short hops stay floaty.")]
        [SerializeField, Range(0f, 1f)] private float airborneGraceTime = 0.18f;
        [SerializeField, Range(0.05f, 2f)] private float airborneRampTime = 0.35f;
        [SerializeField, Range(5f, 60f)] private float maximumFallSpeed = 32f;
        [Tooltip("Soft torque (rad/s^2) levelling the hull toward the wave it will land on. Low enough that a bad launch can still pitch-pole.")]
        [SerializeField, Range(0f, 3f)] private float landingLevelling = 1.4f;
        [Tooltip("Downward speed (m/s) above which re-entering the water is cushioned.")]
        [SerializeField, Range(1f, 20f)] private float slamStartSpeed = 4f;
        [SerializeField, Range(0f, 3f)] private float slamCushion = 0.9f;
        [Tooltip("Impact speed (m/s) that raises HullSlammed (audio, camera shake, spray).")]
        [SerializeField, Range(2f, 30f)] private float slamEventSpeed = 6f;

        [Header("Launch Limiter")]
        [Tooltip("Most upward speed the hull can gain per second from any source, m/s^2 (gravity excluded). Ordinary waves are far below it.")]
        [SerializeField, Range(3f, 40f)] private float maxRiseAcceleration = 16f;
        [Tooltip("Soft ceiling on upward speed, m/s.")]
        [SerializeField, Range(2f, 20f)] private float maxRiseSpeed = 8f;

        /// <summary>Raised when the hull lands after real airtime. Argument = impact speed (m/s).</summary>
        public event System.Action<float> HullSlammed;

        private float airTime;
        private float airBlend;
        private float lastAirborneVerticalSpeed;
        private float lastVerticalSpeed;
        private bool hasLastVerticalSpeed;

        private Vector2 driftTarget, driftOffset, driftVelocity, driftDrive, driftAnchorReference;
        private bool driftTargetValid, driftInitialised;
        private float driftEngage;

        public bool IsAirborne => airTime > airborneGraceTime;
        public float AirTime => airTime;
        public float LastSlamSpeed { get; private set; }
        public Vector3 WaveDriftOffset => new Vector3(driftOffset.x, 0f, driftOffset.y);

        private void OnValidateWaves()
        {
            maxRiseSpeed = Mathf.Max(2f, maxRiseSpeed);
        }

        private void ResetWaveState()
        {
            airTime = airBlend = lastAirborneVerticalSpeed = 0f;
            hasLastVerticalSpeed = false;
            driftTarget = driftOffset = driftVelocity = driftDrive = driftAnchorReference = Vector2.zero;
            driftTargetValid = driftInitialised = false;
            driftEngage = 0f;
        }

        // ------------------------------------------------------------------ heave + gravity

        private void ApplyHeaveAndGravity()
        {
            HullState s = state;
            float dt = Time.fixedDeltaTime;
            Vector3 velocity = body.linearVelocity;

            // Airborne bookkeeping. Without fresh samples the hull is never claimed to be airborne.
            bool airborneNow = s.hasSamples && s.support < 0.02f;
            if (airborneNow)
            {
                airTime += dt;
                lastAirborneVerticalSpeed = velocity.y;
            }
            else
            {
                if (airTime > airborneGraceTime && s.support > 0.05f)
                {
                    float impact = -Mathf.Min(0f, lastAirborneVerticalSpeed);
                    if (impact >= slamEventSpeed)
                    {
                        LastSlamSpeed = impact;
                        try { HullSlammed?.Invoke(impact); }
                        catch (System.Exception exception) { Debug.LogException(exception, this); }
                    }
                }
                airTime = 0f;
            }
            float ramp = Mathf.Clamp01((airTime - airborneGraceTime) / Mathf.Max(0.05f, airborneRampTime));
            airBlend = ramp * ramp * (3f - 2f * ramp);

            // Heave attenuation while supported.
            if (s.support > 0.001f)
            {
                float target = s.waterVelocity.y * heaveWaveFollow;
                float accel = (target - velocity.y) * heaveDamping;
                accel -= Mathf.Sign(velocity.y) * heaveQuadraticDamping * velocity.y * velocity.y;
                body.AddForce(Vector3.up * Mathf.Clamp(accel * s.support, -6f, 6f), ForceMode.Acceleration);
            }

            // Extra gravity after real airtime (long launches fall harder, storms slightly more).
            if (airBlend > 0.001f)
            {
                float multiplier = Mathf.Min(airborneGravity * (1f + 0.2f * s.stormBlend), 4f);
                body.AddForce(Physics.gravity * (Mathf.Max(0f, multiplier - 1f) * airBlend), ForceMode.Acceleration);
            }

            bool airborne = airTime > airborneGraceTime;
            if (airborne)
            {
                if (velocity.y < -maximumFallSpeed)
                {
                    velocity.y = -maximumFallSpeed;
                    body.linearVelocity = velocity;
                }
                if (landingLevelling > 0f && s.hasSurface)
                {
                    Vector3 targetUp = Vector3.Slerp(Vector3.up, s.waterNormal, 0.6f).normalized;
                    float fade = Mathf.Clamp01((airTime - airborneGraceTime) / 0.3f);
                    body.AddTorque(Vector3.Cross(s.up, targetUp) * (landingLevelling * fade), ForceMode.Acceleration);
                }
            }

            // Cushion the water entry without cancelling the impact.
            if (s.support > 0.02f && velocity.y < -slamStartSpeed && slamCushion > 0f)
            {
                float excess = -velocity.y - slamStartSpeed;
                body.AddForce(Vector3.up * Mathf.Min(14f, excess * slamCushion * s.support), ForceMode.Acceleration);
            }
        }

        /// <summary>Caps upward speed gained from any source so a strong wave or collision cannot launch the hull.</summary>
        private void LimitVerticalLaunch(float dt)
        {
            Vector3 velocity = body.linearVelocity;
            float vy = velocity.y;
            if (hasLastVerticalSpeed)
                vy = Mathf.Min(vy, lastVerticalSpeed + maxRiseAcceleration * dt);
            if (vy > maxRiseSpeed)
                vy -= (vy - maxRiseSpeed) * Mathf.Min(1f, 6f * dt);

            if (!Mathf.Approximately(vy, velocity.y))
            {
                velocity.y = vy;
                body.linearVelocity = velocity;
            }
            lastVerticalSpeed = vy;
            hasLastVerticalSpeed = true;
        }

        // ------------------------------------------------------------------ wave drift

        /// <summary>
        /// A virtual damped oscillator chases follow * (horizontal displacement of the water particle under
        /// the hull). It is a linear filter of a zero-mean signal, so it cannot accumulate drift; the hull is
        /// driven by its acceleration plus drag compensation (feed-forward only).
        /// </summary>
        private void UpdateWaveDrift(float dt)
        {
            driftDrive = Vector2.zero;
            if (!enableWaveDrift || buoyancy == null)
            {
                driftEngage = 0f;
                return;
            }

            if (buoyancy.TryGetLatticeDisplacement(out Vector2 lattice, out _) &&
                !float.IsNaN(lattice.x) && !float.IsNaN(lattice.y) &&
                !float.IsInfinity(lattice.x) && !float.IsInfinity(lattice.y))
            {
                driftTarget = lattice;
                driftTargetValid = true;
            }
            else
            {
                driftTarget = Vector2.Lerp(driftTarget, Vector2.zero, 1f - Mathf.Exp(-1.5f * dt));
            }

            if (!driftTargetValid)
            {
                driftEngage = 0f;
                return;
            }

            Vector2 target = Vector2.ClampMagnitude(driftTarget * waveDriftFollow, 6f);
            if (!driftInitialised)
            {
                driftOffset = target;
                driftVelocity = Vector2.zero;
                driftAnchorReference = target;
                driftInitialised = true;
            }

            float omega = waveDriftResponse;
            Vector2 acceleration = (target - driftOffset) * (omega * omega) - driftVelocity * (2f * 0.85f * omega);
            driftVelocity += acceleration * dt;
            driftOffset += driftVelocity * dt;

            driftEngage = Mathf.MoveTowards(driftEngage, state.support > 0.05f ? 1f : 0f, 2f * dt);

            Vector2 forward2 = new Vector2(state.forwardFlat.x, state.forwardFlat.z);
            Vector2 right2 = new Vector2(state.rightFlat.x, state.rightFlat.z);
            Vector2 compensation = forward2 * (Vector2.Dot(driftVelocity, forward2) * surgeDrag)
                                   + right2 * (Vector2.Dot(driftVelocity, right2) * swayDrag);
            driftDrive = Vector2.ClampMagnitude(acceleration + compensation, 12f);
        }

        private void ApplyWaveDriftForce()
        {
            if (!driftInitialised || driftEngage <= 0.001f || driftDrive.sqrMagnitude < 1e-6f)
                return;

            Vector3 acceleration = new Vector3(driftDrive.x, 0f, driftDrive.y) * driftEngage;
            if (waveDriftDepth > 0.001f)
                body.AddForceAtPosition(acceleration, body.worldCenterOfMass - state.up * waveDriftDepth,
                    ForceMode.Acceleration);
            else
                body.AddForce(acceleration, ForceMode.Acceleration);
        }

        private void CaptureWaveDriftAnchorReference()
        {
            driftAnchorReference = driftInitialised ? driftOffset : Vector2.zero;
        }

        private bool TryGetAnchoredDrift(out Vector3 offset, out Vector3 velocity)
        {
            if (!enableWaveDrift || !driftAffectsAnchoredBoat || !driftInitialised || driftEngage <= 0.001f)
            {
                offset = velocity = Vector3.zero;
                return false;
            }

            Vector2 relative = (driftOffset - driftAnchorReference) * driftEngage;
            offset = new Vector3(relative.x, 0f, relative.y);
            velocity = new Vector3(driftVelocity.x, 0f, driftVelocity.y) * driftEngage;
            return true;
        }
    }
}
