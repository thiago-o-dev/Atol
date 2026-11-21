using UnityEngine;

// Vagalumes: só saem quando escurece.
public sealed class Fireflies : MonoBehaviour
{
    public ParticleSystem Particles;

    private float _rate;

    private void Awake()
    {
        _rate = Particles.emission.rateOverTime.constant;
    }

    private void Update()
    {
        ParticleSystem.EmissionModule emission = Particles.emission;
        emission.rateOverTime = _rate * (1f - DayNightCycle.Daylight);
    }
}
