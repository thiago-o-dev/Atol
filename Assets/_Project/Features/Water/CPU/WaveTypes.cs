using UnityEngine;

public class WaveFunctions
{
    public static Vector3 GerstnerWave(Vector3 position, Vector2 direciton, float steepness, float wavelength, float speed, float timeSinceStart)
    {
        float k = 2 * Mathf.PI / wavelength;

        Vector2 normalizedDirection = direciton.normalized;

        float f = k * Vector2.Dot(normalizedDirection, new Vector2(position.x, position.z)) - (speed * timeSinceStart);
        float a = steepness / k;

        return new Vector3(normalizedDirection.x * a * Mathf.Cos(f), a * Mathf.Sin(f), normalizedDirection.y * a * Mathf.Cos(f));
    }
}

[System.Serializable]
public class GerstnerData
{
    [Header("Gerstner Data")]
    public float WaveLength = 0.1f;
    public float Speed = 0.1f;
    [Range(0.0f, 1.0f)] public float Steepness = 0.5f;
    public Vector2 Direction = new(1, 0);

    public GerstnerData(float WaveLength, float Speed, float Steepness, Vector2 Direction)
    {
        this.WaveLength = WaveLength;
        this.Speed = Speed;
        this.Steepness = Steepness;
        this.Direction = Direction;
    }
}