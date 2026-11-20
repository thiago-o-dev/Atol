using System;
using UnityEngine;

namespace Assets._Project.Features.Boat
{
    /// <summary>
    /// Weights and the three rotation axes. Axes are named like an aircraft's:
    ///   Yaw   - heading, about the vertical (up) axis       (rudder, weather helm, broaching)
    ///   Pitch - bow up / bow down, about the beam (right)   (waves along the hull, thrust bow-lift)
    ///   Roll  - heel, about the length (forward) axis       (sail load, waves across the hull)
    /// Every axis has the same family of controls:
    ///   Inertia Scale   rotational weight (how slowly external torques swing the hull)
    ///   Damping         linear damping, 1/s, scaled by how deep the hull sits in the water
    ///   Quadratic Damping  strong only for violent rotation
    ///   Max Rate        soft speed limit, degrees/second
    ///   Stability       restoring (righting) acceleration toward the target "up" (pitch/roll)
    ///   Wave Follow     0 = stay level on the horizon, 1 = lie on the wave surface (pitch/roll)
    /// Torques from the rudder, sail, waves and gusts are divided by the axis' Inertia Scale,
    /// so a heavier axis reacts more slowly. Damping and stability are rates, so they keep their feel.
    /// </summary>
    public sealed partial class BoatController
    {
        [Serializable]
        public sealed class WeightSettings
        {
            [Tooltip("Empty hull mass, kg.")]
            [Min(1f)] public float hullMass = 450f;
            [Tooltip("Extra mass added while a player is aboard, acting at the boarding anchor. Makes the manned boat heavier and more top-heavy.")]
            [Min(0f)] public float crewMass = 80f;
            [Tooltip("Hull centre of mass in local space. Negative Y = ballast / keel weight: lower = more stable, higher = tippier.")]
            public Vector3 centerOfMass = new Vector3(0f, -0.25f, 0f);
            [Tooltip("Hull box size (beam, height, length) in metres for the inertia estimate. Zero = estimate from the buoyancy probes.")]
            public Vector3 hullSize = Vector3.zero;
            [Range(0.3f, 3f)] public float hullHeight = 1f;
        }

        [Serializable]
        public sealed class YawSettings
        {
            [Tooltip("Rotational weight about the vertical axis. Higher = the bow swings and stops more slowly.")]
            [Range(0.25f, 6f)] public float inertiaScale = 1.5f;
            [Range(0f, 8f)] public float damping = 0.9f;
            [Range(0f, 4f)] public float quadraticDamping = 0.5f;
            [Tooltip("Soft limit on turn rate, deg/s.")]
            [Range(20f, 360f)] public float maxRate = 90f;
            [Tooltip("Weathervane torque that keeps the bow along the direction of travel (rad/s^2 at top speed). Stops waves and gusts slewing the boat.")]
            [Range(0f, 4f)] public float directionalStability = 0.9f;
            [Tooltip("A heeled hull rounds up toward the wind (rad/s^2 per unit of heel at top speed).")]
            [Range(0f, 3f)] public float weatherHelm = 0.9f;
            [Tooltip("Running down a wave face at an angle swings the stern around. Counter with the rudder.")]
            [Range(0f, 80f)] public float broaching = 24f;
        }

        [Serializable]
        public sealed class PitchSettings
        {
            [Range(0.25f, 6f)] public float inertiaScale = 1.5f;
            [Range(0f, 8f)] public float damping = 1.6f;
            [Range(0f, 4f)] public float quadraticDamping = 0.8f;
            [Range(20f, 360f)] public float maxRate = 80f;
            [Tooltip("Righting acceleration (rad/s^2 per sin of the angle to the target) that returns the bow to its target trim.")]
            [Range(0f, 14f)] public float stability = 5f;
            [Tooltip("0 = hull stays level fore-aft while waves pass under it. 1 = lies on the wave slope along its length.")]
            [Range(0f, 1f)] public float waveFollow = 0.8f;
            [Tooltip("Bow rises under sail drive (rad/s^2 at full drive), like a hull lifting onto its wave.")]
            [Range(0f, 3f)] public float bowLift = 0.6f;
        }

        [Serializable]
        public sealed class RollSettings
        {
            [Range(0.25f, 6f)] public float inertiaScale = 1.5f;
            [Range(0f, 8f)] public float damping = 2.2f;
            [Range(0f, 4f)] public float quadraticDamping = 1f;
            [Range(20f, 360f)] public float maxRate = 100f;
            [Tooltip("Righting acceleration (rad/s^2 per sin of the heel error). Higher = stiffer, resists heeling more.")]
            [Range(0f, 14f)] public float stability = 6f;
            [Tooltip("0 = hull stays level across the beam while waves pass under it. 1 = lies on the wave slope across its width.")]
            [Range(0f, 1f)] public float waveFollow = 0.7f;
            [Tooltip("Heel angle beyond which righting fades out (angle of vanishing stability), degrees.")]
            [Range(60f, 170f)] public float vanishingAngle = 110f;
            [Tooltip("Heeling acceleration at full sail load (rad/s^2). Ease the sheet or feather the sail to flatten the boat.")]
            [Range(0f, 8f)] public float sailHeel = 1.6f;
            [Tooltip("Lean outward in turns (rad/s^2 per m/s of speed per rad/s of yaw rate).")]
            [Range(0f, 1f)] public float turnLean = 0.12f;
            [Tooltip("Heel below this angle costs nothing, degrees.")]
            [Range(5f, 60f)] public float comfortableHeel = 22f;
            [Tooltip("Heel at which drive and rudder are fully reduced, degrees.")]
            [Range(20f, 85f)] public float maximumHeel = 55f;
            [Range(0f, 0.9f)] public float heelDriveLoss = 0.5f;
            [Range(0f, 0.9f)] public float heelRudderLoss = 0.3f;
        }

        [Header("Weight")]
        [SerializeField] private WeightSettings weight = new WeightSettings();

        [Header("Yaw (heading)")]
        [SerializeField] private YawSettings yaw = new YawSettings();

        [Header("Pitch (bow up / down)")]
        [SerializeField] private PitchSettings pitch = new PitchSettings();

        [Header("Roll (heel)")]
        [SerializeField] private RollSettings roll = new RollSettings();

        /// <summary>One consistent snapshot of the hull, captured at the start of each physics step.</summary>
        private struct HullState
        {
            public Quaternion rotation;
            public Vector3 up, right, forward, forwardFlat, rightFlat;
            public Vector3 velocity;
            public float planarSpeed, forwardSpeed, lateralSpeed;
            public Vector3 localAngularVelocity;
            public float yawRate;
            public float immersion, support;
            public bool hasSamples, hasSurface;
            public Vector3 waterNormal, waterVelocity;
            public float heelSin, heelDegrees, trimDegrees;
            public float heelOver, driveFactor, rudderFactor;
            public float stormBlend, windStrength;
        }

        private HullState state;
        private float appliedMass;
        private Vector3 appliedCenterOfMass;
        private Vector3 appliedInertia;

        /// <summary>Heel in degrees. Positive = mast tilts to starboard.</summary>
        public float HeelAngleDegrees => state.heelDegrees;
        /// <summary>Trim in degrees. Positive = bow up.</summary>
        public float TrimAngleDegrees => state.trimDegrees;
        /// <summary>0..1: how firmly the hull is carried by the water.</summary>
        public float WaterSupport => state.support;
        /// <summary>0 (calm) to 1 (extreme storm).</summary>
        public float StormBlend => state.stormBlend;

        private void OnValidateHull()
        {
            roll.maximumHeel = Mathf.Max(roll.comfortableHeel + 1f, roll.maximumHeel);
        }

        // ------------------------------------------------------------------ weights

        private float CurrentMass => weight.hullMass + (aboardPlayer != null ? weight.crewMass : 0f);

        /// <summary>
        /// Sets mass, centre of mass and inertia explicitly. Automatic values would change whenever the
        /// player's collider joins the hull, which silently altered stability while manned.
        /// </summary>
        private void ApplyMassProperties(bool force)
        {
            if (body == null)
                return;

            float crew = aboardPlayer != null ? weight.crewMass : 0f;
            float total = weight.hullMass + crew;
            Vector3 com = weight.centerOfMass;
            if (crew > 0f)
            {
                Vector3 crewLocal = transform.InverseTransformPoint(BoardingAnchor.position);
                com = (weight.centerOfMass * weight.hullMass + crewLocal * crew) / total;
            }

            Vector3 size = weight.hullSize;
            if (size.sqrMagnitude < 0.0001f)
            {
                size = new Vector3(2.4f, weight.hullHeight, 4f);
                if (buoyancy != null && buoyancy.OceanBuoyancy != null &&
                    buoyancy.OceanBuoyancy.TryGetProbeLocalBounds(out Bounds probes))
                    size = new Vector3(probes.size.x + 0.4f, weight.hullHeight, probes.size.z + 0.8f);
            }

            float k = total / 12f;
            Vector3 inertia = new Vector3(
                k * (size.y * size.y + size.z * size.z) * pitch.inertiaScale,   // about X: pitch
                k * (size.x * size.x + size.z * size.z) * yaw.inertiaScale,     // about Y: yaw
                k * (size.x * size.x + size.y * size.y) * roll.inertiaScale);   // about Z: roll
            inertia = Vector3.Max(inertia, Vector3.one * 0.01f);

            body.mass = total;
            body.centerOfMass = com;
            body.inertiaTensorRotation = Quaternion.identity;
            body.inertiaTensor = inertia;
            body.maxAngularVelocity = Mathf.Max(1f,
                Mathf.Max(yaw.maxRate, Mathf.Max(pitch.maxRate, roll.maxRate)) * 1.5f * Mathf.Deg2Rad);

            appliedMass = total;
            appliedCenterOfMass = com;
            appliedInertia = inertia;
        }

        /// <summary>Cheap per-step guard: re-assert if anything (a new collider, an inspector edit) changed them.</summary>
        private void MaintainMassProperties()
        {
            if (!Mathf.Approximately(body.mass, appliedMass) ||
                (body.centerOfMass - appliedCenterOfMass).sqrMagnitude > 1e-6f ||
                (body.inertiaTensor - appliedInertia).sqrMagnitude > 1e-4f)
                ApplyMassProperties(true);
        }

        // ------------------------------------------------------------------ snapshot

        private void CaptureHullState(float dt)
        {
            ref HullState s = ref state;
            s.rotation = body.rotation;
            s.up = s.rotation * Vector3.up;
            s.right = s.rotation * Vector3.right;
            s.forward = s.rotation * Vector3.forward;

            s.forwardFlat = Vector3.ProjectOnPlane(s.forward, Vector3.up);
            s.forwardFlat = s.forwardFlat.sqrMagnitude > 0.0004f ? s.forwardFlat.normalized : Vector3.forward;
            s.rightFlat = new Vector3(s.forwardFlat.z, 0f, -s.forwardFlat.x);

            s.velocity = body.linearVelocity;
            Vector3 planar = Vector3.ProjectOnPlane(s.velocity, Vector3.up);
            s.planarSpeed = planar.magnitude;
            s.forwardSpeed = Vector3.Dot(planar, s.forwardFlat);
            s.lateralSpeed = Vector3.Dot(planar, s.rightFlat);

            Vector3 angular = body.angularVelocity;
            s.localAngularVelocity = Quaternion.Inverse(s.rotation) * angular;
            s.yawRate = Vector3.Dot(angular, Vector3.up);

            s.immersion = buoyancy != null ? buoyancy.CurrentWaterImmersion : 0f;
            s.hasSamples = buoyancy != null && buoyancy.HasFreshSamples;
            s.support = s.hasSamples
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s.immersion / FullSupportImmersion))
                : 0f;

            s.hasSurface = buoyancy != null && buoyancy.TryGetWaterSurface(
                out _, out s.waterNormal, out s.waterVelocity, out _);
            if (s.hasSurface)
            {
                if (s.waterNormal.y < 0f)
                    s.waterNormal = -s.waterNormal;
                s.waterNormal = s.waterNormal.sqrMagnitude > 0.001f ? s.waterNormal.normalized : Vector3.up;
            }
            else
            {
                s.waterNormal = Vector3.up;
                s.waterVelocity = Vector3.zero;
            }

            s.windStrength = ocean != null ? Mathf.Clamp01(ocean.CurrentWindStrength) : 0f;
            s.stormBlend = ocean != null
                ? Mathf.Clamp01(Mathf.InverseLerp(1f, 2.8f, ocean.CurrentSeaStateStrength))
                : 0f;

            // Heel (roll against the horizon) and trim (pitch against the horizon).
            float rightLength = Vector3.ProjectOnPlane(s.right, Vector3.up).magnitude;
            if (rightLength > 0.2f)
                s.heelSin = Mathf.Clamp(Vector3.Dot(s.up, Vector3.ProjectOnPlane(s.right, Vector3.up) / rightLength), -1f, 1f);
            else
                s.heelSin = s.heelSin >= 0f ? 1f : -1f;
            s.heelDegrees = Mathf.Asin(s.heelSin) * Mathf.Rad2Deg;
            s.trimDegrees = Mathf.Asin(Mathf.Clamp(s.forward.y, -1f, 1f)) * Mathf.Rad2Deg;

            s.heelOver = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                roll.comfortableHeel, roll.maximumHeel, Mathf.Abs(s.heelDegrees)));
            s.driveFactor = Mathf.Lerp(1f, 1f - roll.heelDriveLoss, s.heelOver);
            s.rudderFactor = 1f - roll.heelRudderLoss * s.heelOver;
        }

        /// <summary>Pushes the wave-slope follow and heave damping into the buoyancy probes.</summary>
        private void SyncBuoyancyModel()
        {
            if (buoyancy == null)
                return;
            buoyancy.SlopeFollow = new Vector2(roll.waveFollow, pitch.waveFollow);
            buoyancy.DragScale = heaveProbeDamping;
        }

        // ------------------------------------------------------------------ attitude

        /// <summary>
        /// Roll, pitch and yaw torques for this step: righting toward the wave-aware target up, per-axis
        /// damping, heel loads, bow lift, directional stability, weather helm and broaching.
        /// All are angular accelerations (rad/s^2), applied in one call.
        /// </summary>
        private void ApplyAttitude(float steering, float dt)
        {
            HullState s = state;
            float support = s.support;
            float immersionScale = Mathf.Lerp(airborneDampingFraction, 1f, support);
            Quaternion inverse = Quaternion.Inverse(s.rotation);

            Vector3 local = Vector3.zero;    // x pitch, y (unused), z roll in hull axes
            Vector3 world = Vector3.zero;    // yaw and heel loads in world axes

            // 1. Righting toward the wave-aware target. Roll and pitch each follow the surface by
            //    their own Wave Follow, matching what the buoyancy probes are asked to do.
            if (support > 0.001f)
            {
                Vector3 surfaceNormal = s.hasSurface ? s.waterNormal : Vector3.up;

                Vector3 targetUpRoll = Vector3.Slerp(Vector3.up, surfaceNormal, roll.waveFollow).normalized;
                Vector3 rollAxis = Vector3.Cross(s.up, targetUpRoll);
                float rollError = Mathf.Atan2(rollAxis.magnitude, Vector3.Dot(s.up, targetUpRoll));
                local.z += (inverse * rollAxis).z * roll.stability
                           * Lobe(rollError, roll.vanishingAngle) * support;

                Vector3 targetUpPitch = Vector3.Slerp(Vector3.up, surfaceNormal, pitch.waveFollow).normalized;
                Vector3 pitchAxis = Vector3.Cross(s.up, targetUpPitch);
                float pitchError = Mathf.Atan2(pitchAxis.magnitude, Vector3.Dot(s.up, targetUpPitch));
                local.x += (inverse * pitchAxis).x * pitch.stability
                           * Lobe(pitchError, roll.vanishingAngle) * support;
            }

            // 2. Per-axis damping (linear + quadratic + soft rate limit), scaled by immersion.
            float yawDampFade = 1f - 0.5f * Mathf.Clamp01(Mathf.Abs(steering));
            local.x += AxisDamping(s.localAngularVelocity.x, pitch.damping, pitch.quadraticDamping,
                pitch.maxRate * Mathf.Deg2Rad, dt) * immersionScale;
            local.z += AxisDamping(s.localAngularVelocity.z, roll.damping, roll.quadraticDamping,
                roll.maxRate * Mathf.Deg2Rad, dt) * immersionScale;
            world += Vector3.up * (AxisDamping(s.yawRate, yaw.damping * yawDampFade, yaw.quadraticDamping,
                yaw.maxRate * Mathf.Deg2Rad, dt) * immersionScale);

            if (support > 0.001f)
            {
                float speedNorm = Mathf.Clamp01(s.planarSpeed / Mathf.Max(0.1f, maximumSpeed));

                // 3. Roll loads: sail heel with gusts, outward lean in turns (external: divided by inertia).
                if (roll.sailHeel > 0f && Mathf.Abs(sailHeelLoad) > 0.0001f)
                {
                    float noise = Mathf.PerlinNoise(Time.time * gustFrequency, GustSeed()) * 2f - 1f;
                    float gust = 1f + gustStrength * noise * Mathf.Lerp(0.35f, 1f, s.stormBlend);
                    world += Vector3.Cross(s.up, s.rightFlat)
                             * (roll.sailHeel * sailHeelLoad * gust * support / roll.inertiaScale);
                }
                if (roll.turnLean > 0f)
                    world += Vector3.Cross(s.up, -s.rightFlat)
                             * (roll.turnLean * s.planarSpeed * s.yawRate * support / roll.inertiaScale);

                // 4. Pitch load: bow lifts under drive (negative X torque = bow up).
                local.x -= pitch.bowLift * sailDriveNormalized * support / pitch.inertiaScale;

                // 5. Yaw: directional stability (restoring), weather helm and broaching (external).
                float yawTorque = 0f;
                if (yaw.directionalStability > 0f && s.planarSpeed > 0.5f)
                {
                    float slip = s.lateralSpeed / s.planarSpeed;
                    float fade = 1f - Mathf.Clamp01(Mathf.Abs(steering) * 2f);
                    yawTorque += yaw.directionalStability * slip * speedNorm * fade;
                }
                float external = -yaw.weatherHelm * s.heelSin * speedNorm;
                if (yaw.broaching > 0f && s.hasSurface)
                {
                    Vector3 downhill = new Vector3(s.waterNormal.x, 0f, s.waterNormal.z)
                                       / Mathf.Max(s.waterNormal.y, 0.2f);
                    float along = Vector3.Dot(downhill, s.forwardFlat);
                    float across = Vector3.Dot(downhill, s.rightFlat);
                    external -= yaw.broaching * Mathf.Max(0f, along) * across * speedNorm
                                * Mathf.Lerp(0.5f, 1f, s.stormBlend);
                }
                yawTorque += external / yaw.inertiaScale;
                world += Vector3.up * (yawTorque * support);
            }

            Vector3 total = s.rotation * local + world;
            total = Vector3.ClampMagnitude(total, MaximumAngularAcceleration);
            if (total.sqrMagnitude > 1e-6f)
                body.AddTorque(total, ForceMode.Acceleration);
        }

        private const float MaximumAngularAcceleration = 30f;

        /// <summary>1 inside the stable range, fading to 0 at the angle of vanishing stability.</summary>
        private static float Lobe(float errorRadians, float vanishingDegrees)
        {
            float x = errorRadians / (Mathf.Max(1f, vanishingDegrees) * Mathf.Deg2Rad);
            return x < 1f ? 1f - x * x : 0f;
        }

        /// <summary>Damping torque for one axis, never strong enough to reverse the rate in one step.</summary>
        private static float AxisDamping(float rate, float linear, float quadratic, float softMax, float dt)
        {
            float magnitude = Mathf.Abs(rate);
            if (magnitude < 0.0001f)
                return 0f;
            float excess = Mathf.Max(0f, magnitude - softMax);
            float torque = linear * magnitude + quadratic * magnitude * magnitude + 8f * excess;
            torque = Mathf.Min(torque, magnitude / Mathf.Max(dt, 0.0001f));
            return -Mathf.Sign(rate) * torque;
        }
    }
}
