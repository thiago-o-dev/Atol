using UnityEngine;

/// <summary>
/// Shared tangent-plane correction for asynchronous ocean samples. Keep this
/// calculation identical for rigidbody buoyancy and CharacterController swimmers.
/// </summary>
public static class WaterSurfaceSampleUtility
{
    public const float DefaultMaximumSlope = 8f;
    public const float DefaultMaximumHeightCorrection = 5f;
    private const float MinimumNormalY = 0.12f;

    /// <summary>
    /// Predicts the sampled water height at a current world-space position using
    /// the sample's tangent plane. The sample's Position.xz is the exact world XZ
    /// queried by the FFT; horizontal wave-particle displacement is deliberately
    /// not added to that coordinate.
    /// </summary>
    public static bool TryGetHeightAtWorldPosition(
        SpectralOceanFFT.SurfaceSample sample,
        Vector3 worldPosition,
        float maximumSampleSlope,
        float maximumHeightCorrection,
        out float waterHeight)
    {
        waterHeight = sample.Height;
        if (!sample.IsValid ||
            !IsFinite(sample.Height) ||
            !IsFinite(sample.Position.x) || !IsFinite(sample.Position.z) ||
            !IsFinite(sample.Normal.x) || !IsFinite(sample.Normal.y) || !IsFinite(sample.Normal.z) ||
            !IsFinite(worldPosition.x) || !IsFinite(worldPosition.z))
        {
            return false;
        }

        Vector3 normal = sample.Normal.sqrMagnitude > 0.001f
            ? sample.Normal.normalized
            : Vector3.up;
        if (normal.y < 0f)
            normal = -normal;

        // This is the same normal floor, gradient, slope clamp and correction
        // limit used by OceanBuoyancy. Do not reject steep but valid wave faces.
        float normalY = Mathf.Max(normal.y, MinimumNormalY);
        Vector2 delta = new Vector2(
            worldPosition.x - sample.Position.x,
            worldPosition.z - sample.Position.z);
        Vector2 slope = new Vector2(-normal.x / normalY, -normal.z / normalY);
        slope = Vector2.ClampMagnitude(slope, Mathf.Max(0.01f, maximumSampleSlope));

        float correctionLimit = Mathf.Max(0f, maximumHeightCorrection);
        float correction = Mathf.Clamp(
            Vector2.Dot(slope, delta), -correctionLimit, correctionLimit);
        waterHeight = sample.Height + correction;
        return IsFinite(waterHeight);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
