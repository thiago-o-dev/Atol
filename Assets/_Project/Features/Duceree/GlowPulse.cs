using UnityEngine;

namespace Assets._Project.Features.Duceree
{
    // Luz que respira: a intensidade sobe e desce devagar, cada instância num compasso um pouco diferente.
    // Pode enfraquecer de dia, pra só aparecer de verdade quando escurece.
    public sealed class GlowPulse : MonoBehaviour
    {
        public Light Light;
        public float MinIntensity = 1.2f;
        public float MaxIntensity = 3.5f;
        public float Speed = 1.2f;
        [Tooltip("Fração do brilho que sobra com o sol alto.")]
        [Range(0f, 1f)] public float DayAmount = 1f;

        private float _offset;

        private void Awake()
        {
            _offset = Random.value * 10f;
        }

        private void Update()
        {
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * Speed + _offset);
            Light.intensity = Mathf.Lerp(MinIntensity, MaxIntensity, wave) * Mathf.Lerp(1f, DayAmount, DayNightCycle.Daylight);
        }
    }
}
