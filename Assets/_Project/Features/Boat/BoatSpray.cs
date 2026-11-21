using UnityEngine;

namespace Assets._Project.Features.Boat
{
    // Respingo na proa: quanto mais rápido o barco corta a água, mais ela levanta.
    public sealed class BoatSpray : MonoBehaviour
    {
        public BoatController Boat;
        public ParticleSystem Particles;
        [Tooltip("Abaixo dessa velocidade não levanta nada.")]
        public float MinSpeed = 2f;
        public float FullSpeed = 7f;
        public float MaxRate = 45f;

        private void Update()
        {
            ParticleSystem.EmissionModule emission = Particles.emission;
            emission.rateOverTime = MaxRate * Mathf.InverseLerp(MinSpeed, FullSpeed, Boat.PlanarSpeed);
        }
    }
}
