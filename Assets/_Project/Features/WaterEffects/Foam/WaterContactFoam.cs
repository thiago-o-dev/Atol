using UnityEngine;

namespace Assets._Project.Features.WaterEffects
{
    /// <summary>
    /// Per-actor foam tuning + emit helpers. Boats and swimmers own one of these and
    /// describe WHAT touched the water (a moving contact point, or a splash); this
    /// component decides how big and strong the foam stamp is and hands it to
    /// <see cref="WaterFoamField"/>, which makes it appear on the water surface.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterContactFoam : MonoBehaviour
    {
        [Header("Trail (continuous contact)")]
        [Tooltip("Foam stamp radius (m) at low contact intensity.")]
        [SerializeField, Min(0.05f)] private float trailRadiusMin = 0.5f;
        [Tooltip("Foam stamp radius (m) at full contact intensity.")]
        [SerializeField, Min(0.05f)] private float trailRadiusMax = 1.1f;
        [Tooltip("Foam added per second at full intensity (screen-blended, so it never overshoots). 30 = about half strength per frame at 60 fps.")]
        [SerializeField, Range(0f, 120f)] private float trailFoamRate = 30f;

        [Header("Burst (splash / slam)")]
        [Tooltip("Foam radius (m) at full-intensity burst.")]
        [SerializeField, Min(0.1f)] private float burstRadius = 1.6f;
        [Tooltip("Peak strength (0..1) of a full-intensity burst.")]
        [SerializeField, Range(0f, 1f)] private float burstStrength = 0.85f;

        [Header("Ripples (expanding rings)")]
        [Tooltip("Ring radius (m) reached by the smallest ripple (before the per-call range scale).")]
        [SerializeField, Min(0.2f)] private float rippleRadiusSmall = 4f;
        [Tooltip("Ring radius (m) reached by the largest (size 1) ripple.")]
        [SerializeField, Min(0.5f)] private float rippleRadiusLarge = 30f;
        [Tooltip("Seconds a small ripple lasts.")]
        [SerializeField, Min(0.3f)] private float rippleLifeShort = 5f;
        [Tooltip("Seconds a large ripple lasts.")]
        [SerializeField, Min(0.3f)] private float rippleLifeLong = 10f;
        [Tooltip("Normal tilt / brightness of the ring at strength 1.")]
        [SerializeField, Range(0f, 2f)] private float rippleStrength = 1f;
        [Tooltip("Small foam flecks shed by the ring crest (0 = pure water ripple). Scales with ripple size.")]
        [SerializeField, Range(0f, 1f)] private float rippleFoam = 0.15f;

        [Header("Global")]
        [Tooltip("Master switch for this actor.")]
        public bool EmitFoam = true;

        /// <summary>
        /// Continuous foam along from -> to. intensity 0..1 scales radius and strength.
        /// Call once per frame (or physics step) per contact point; dt is the elapsed time of that step.
        /// </summary>
        public void EmitTrail(Vector3 from, Vector3 to, float intensity, float dt)
        {
            if (!EmitFoam || intensity <= 0.001f)
                return;

            EmitTrail(from, to, intensity, dt, trailRadiusMin, trailRadiusMax, trailFoamRate);
        }

        /// <summary>
        /// Trail with explicit size/rate, for actors that need their own tuning (e.g. the swimmer)
        /// without a second component. Radii in metres, rate = foam per second at full intensity.
        /// </summary>
        public void EmitTrail(Vector3 from, Vector3 to, float intensity, float dt,
            float radiusMin, float radiusMax, float ratePerSecond)
        {
            if (!EmitFoam || intensity <= 0.001f)
                return;

            intensity = Mathf.Clamp01(intensity);
            float radius = Mathf.Lerp(radiusMin, radiusMax, intensity);
            float strength = intensity * ratePerSecond * Mathf.Min(dt, 0.1f);
            WaterFoamField.Emit(from, to, radius, strength);
        }

        /// <summary>
        /// Outward ripple ring. size01 (0..1) is the inferred size of the disturbance (heavier /
        /// bigger = larger, longer-lived ring with more foam bits). strength01 scales how visible it is.
        /// </summary>
        public void EmitRipple(Vector3 center, float startRadius, float size01, float strength01)
        {
            EmitRipple(center, startRadius, size01, strength01, Vector3.zero, 0f);
        }

        /// <summary>
        /// Ripple with an arc. A non-zero <paramref name="facing"/> (world direction, Y ignored) with
        /// arc 1 gives a half-circle that spreads away on that side (moving objects); zero facing = full circle.
        /// </summary>
        public void EmitRipple(Vector3 center, float startRadius, float size01, float strength01,
            Vector3 facing, float arc, float foamScale = 1f, float rangeScale = 1f)
        {
            if (!EmitFoam || strength01 <= 0.01f)
                return;

            size01 = Mathf.Clamp01(size01);
            rangeScale = Mathf.Clamp01(rangeScale);
            WaterFoamField.EmitRipple(
                center,
                startRadius,
                Mathf.Max(startRadius + 0.5f, RippleMaxRadius(size01) * rangeScale),
                Mathf.Clamp01(strength01) * rippleStrength,
                rippleFoam * foamScale * size01 * size01,
                Mathf.Lerp(rippleLifeShort, rippleLifeLong, size01) * Mathf.Lerp(0.4f, 1f, rangeScale),
                new Vector2(facing.x, facing.z),
                arc);
        }

        /// <summary>Ring radius (m) a ripple of the given size reaches (range scale 1).</summary>
        public float RippleMaxRadius(float size01)
        {
            return Mathf.Lerp(rippleRadiusSmall, rippleRadiusLarge, Mathf.Clamp01(size01));
        }

        /// <summary>One soft splash blob at a point (hull slam, jump take-off or landing).</summary>
        public void EmitBurst(Vector3 point, float intensity, float sizeScale = 1f)
        {
            if (!EmitFoam || intensity <= 0.001f)
                return;

            intensity = Mathf.Clamp01(intensity);
            WaterFoamField.Emit(point, point, burstRadius * sizeScale * Mathf.Lerp(0.35f, 1f, intensity), burstStrength * intensity);
        }
    }
}
