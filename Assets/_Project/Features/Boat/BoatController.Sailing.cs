using UnityEngine;

namespace Assets._Project.Features.Boat
{
    /// <summary>
    /// Sail drive, rudder and hull drag. The rudder follows the usual game-boat approach
    /// (Boat Attack's Engine.Turn applies a yaw torque, gated by immersion): a yaw-rate target
    /// whose bite grows with water flow (speed squared), so a drifting canoe cannot pivot on the spot.
    /// </summary>
    public sealed partial class BoatController
    {
        [Header("Sail")]
        [Tooltip("Forward acceleration at full wind, full sail, ideal angle (m/s^2).")]
        [SerializeField, Min(0f)] private float sailPower = 7f;
        [SerializeField, Min(0f)] private float sailTrimRate = 0.85f;
        [SerializeField, Range(0f, 1f)] private float startingSailTrim = 0.8f;
        [Tooltip("Half-angle of the zone facing the wind where the sail gives no drive, degrees.")]
        [SerializeField, Range(0f, 89f)] private float noGoAngle = 42f;
        [Tooltip("Sideways (leeway) acceleration from a beam wind at full sail (m/s^2).")]
        [SerializeField, Min(0f)] private float sailSideForce = 0.55f;
        [Tooltip("Degrees per second the sail swings with Q/E.")]
        [SerializeField, Min(1f)] private float sailRotationSpeed = 72f;
        [SerializeField, Range(0f, 85f)] private float maximumSailRotation = 78f;
        [SerializeField, Range(-85f, 85f)] private float startingSailRotation;
        [Tooltip("Gusts vary the heeling load by this fraction (more in storms).")]
        [SerializeField, Range(0f, 1f)] private float gustStrength = 0.35f;
        [SerializeField, Range(0.05f, 1f)] private float gustFrequency = 0.18f;

        [Header("Rudder (Yaw control)")]
        [Tooltip("Turn rate the rudder asks for at full lock and full speed, deg/s.")]
        [SerializeField, Range(5f, 180f)] private float rudderMaxYawRate = 38f;
        [Tooltip("How quickly the hull reaches the requested turn rate (1/s). Divided by the yaw Inertia Scale.")]
        [SerializeField, Range(0.5f, 12f)] private float rudderResponse = 2.6f;
        [Tooltip("Largest rudder torque, rad/s^2 (divided by the yaw Inertia Scale).")]
        [SerializeField, Range(0.5f, 30f)] private float rudderTorque = 6f;
        [Tooltip("Speed at which the rudder reaches full bite, m/s.")]
        [SerializeField, Min(0.1f)] private float rudderFullSpeed = 3f;
        [Tooltip("Rudder bite when almost stopped (bite rises with speed squared).")]
        [SerializeField, Range(0f, 0.5f)] private float rudderMinAuthority = 0.08f;

        [Header("Hull Drag (Surge / Sway)")]
        [Tooltip("Soft top speed, m/s. Quadratic drag makes speed level off before this.")]
        [SerializeField, Min(1f)] private float maximumSpeed = 14f;
        [Tooltip("Forward water drag, linear (1/s).")]
        [SerializeField, Range(0f, 2f)] private float surgeDrag = 0.05f;
        [Tooltip("Forward water drag, quadratic (1/m).")]
        [SerializeField, Range(0f, 0.2f)] private float surgeQuadraticDrag = 0.04f;
        [Tooltip("Sideways (keel) drag, linear (1/s). Stops the hull skating sideways.")]
        [SerializeField, Range(0f, 8f)] private float swayDrag = 1.65f;
        [Tooltip("Sideways (keel) drag, quadratic (1/m).")]
        [SerializeField, Range(0f, 1f)] private float swayQuadraticDrag = 0.25f;

        private float sailHeelLoad;
        private float sailDriveNormalized;
        private float gustSeedValue;

        private float GustSeed()
        {
            if (gustSeedValue == 0f)
                gustSeedValue = Random.Range(1f, 500f);
            return gustSeedValue;
        }

        private void OnValidateSailing()
        {
            maximumSailRotation = Mathf.Clamp(maximumSailRotation, 0f, 85f);
        }

        private void ApplySail()
        {
            sailHeelLoad = 0f;
            sailDriveNormalized = 0f;
            if (ocean == null || sailTrim <= 0.001f || sailPower <= 0f)
                return;

            Vector2 windXZ = ocean.CurrentWindDirectionXZ;
            float windStrength = state.windStrength;
            if (windXZ.sqrMagnitude < 0.0001f || windStrength <= 0.001f)
                return;

            Vector3 windDirection = new Vector3(windXZ.x, 0f, windXZ.y).normalized;
            Vector3 forward = state.forwardFlat;

            float intoWind = Vector3.Dot(forward, -windDirection);
            float noGoStart = Mathf.Cos(noGoAngle * Mathf.Deg2Rad);
            float noGoEnd = Mathf.Clamp01(noGoStart - 0.28f);
            float noGo = 1f - Mathf.SmoothStep(noGoEnd, noGoStart, intoWind);

            Vector3 sailHeading = Quaternion.AngleAxis(sailRotationDegrees, Vector3.up) * forward;
            Vector3 sailNormal = Vector3.Cross(Vector3.up, sailHeading).normalized;
            float capture = Mathf.Abs(Vector3.Dot(windDirection, sailNormal));
            float sailEfficiency = Mathf.Lerp(0.48f, 1f, capture);
            float windAngle = Mathf.Lerp(0.72f, 1f, Mathf.Abs(Vector3.Dot(forward, windDirection)));

            float drive = sailPower * windStrength * sailTrim * noGo * windAngle * sailEfficiency
                          * state.driveFactor;
            sailDriveNormalized = Mathf.Clamp01(drive / Mathf.Max(0.01f, sailPower));
            body.AddForce(forward * drive, ForceMode.Acceleration);

            float windSide = Vector3.Dot(windDirection, state.rightFlat);
            body.AddForce(state.rightFlat * (windSide * sailSideForce * windStrength * sailTrim * noGo
                                              * Mathf.Lerp(0.55f, 1f, capture)),
                ForceMode.Acceleration);

            // Heeling load shrinks with heel (projected sail area): sailing alone cannot flip the hull.
            float cosHeel = Mathf.Sqrt(Mathf.Max(0f, 1f - state.heelSin * state.heelSin));
            sailHeelLoad = windSide * windStrength * sailTrim * noGo * capture * cosHeel;
        }

        private void ApplyRudder(float steering)
        {
            float flow = Mathf.Clamp01(state.planarSpeed / rudderFullSpeed);
            float authority = Mathf.Max(rudderMinAuthority, flow * flow) * state.rudderFactor;
            if (state.hasSamples)
                authority *= Mathf.Lerp(0.2f, 1f, state.support);   // little bite with the hull in the air

            float desiredRate = steering * rudderMaxYawRate * Mathf.Deg2Rad * authority;
            float accel = (desiredRate - state.yawRate) * rudderResponse / yaw.inertiaScale;
            float limit = rudderTorque / yaw.inertiaScale;
            body.AddTorque(Vector3.up * Mathf.Clamp(accel, -limit, limit), ForceMode.Acceleration);
        }

        private void ApplyHullDrag()
        {
            Vector3 relative = state.velocity;
            relative -= state.waterVelocity * 0.15f;     // the water's own motion carries the hull a little
            float lateral = Vector3.Dot(relative, state.rightFlat);
            float forward = state.forwardSpeed;
            float wet = Mathf.Lerp(airborneDampingFraction, 1f, state.support);
            float dt = Time.fixedDeltaTime;

            float sway = (swayDrag * Mathf.Abs(lateral) + swayQuadraticDrag * lateral * lateral) * wet;
            sway = Mathf.Min(sway, Mathf.Abs(lateral) / dt);
            body.AddForce(-state.rightFlat * (Mathf.Sign(lateral) * sway), ForceMode.Acceleration);

            float surge = (surgeDrag * Mathf.Abs(forward) + surgeQuadraticDrag * forward * forward) * wet;
            if (forward > maximumSpeed)
                surge += (forward - maximumSpeed) * 1.5f;
            surge = Mathf.Min(surge, Mathf.Abs(forward) / dt);
            body.AddForce(-state.forwardFlat * (Mathf.Sign(forward) * surge), ForceMode.Acceleration);
        }
    }
}
