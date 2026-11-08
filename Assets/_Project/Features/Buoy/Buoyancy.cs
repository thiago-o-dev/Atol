using UnityEngine;

namespace Assets._Project.Features.Buoy
{
    /// <summary>
    /// Public buoyancy feature facade for floating gameplay objects.
    /// The actual FFT sampling and force solver remain in the existing OceanBuoyancy
    /// component from the Coastal Breakers water feature. This facade gives boats
    /// and other systems a stable, feature-level API without duplicating that solver.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(global::OceanBuoyancy))]
    public sealed class Buoyancy : MonoBehaviour
    {
        [SerializeField] private global::OceanBuoyancy oceanBuoyancy;

        public global::OceanBuoyancy OceanBuoyancy => oceanBuoyancy;
        public bool HasFreshSamples => oceanBuoyancy != null && oceanBuoyancy.HasFreshSamples;
        public float AverageWaterHeight => oceanBuoyancy != null ? oceanBuoyancy.AverageWaterHeight : transform.position.y;
        public Vector3 AverageWaterPosition => oceanBuoyancy != null ? oceanBuoyancy.AverageWaterPosition : transform.position;
        public Vector3 AverageWaterNormal => oceanBuoyancy != null ? oceanBuoyancy.AverageWaterNormal : Vector3.up;
        public Vector3 AverageWaterVelocity => oceanBuoyancy != null ? oceanBuoyancy.AverageWaterVelocity : Vector3.zero;
        public float SampleAge => oceanBuoyancy != null ? oceanBuoyancy.SampleAge : float.PositiveInfinity;
        public float CurrentWaterImmersion => oceanBuoyancy != null ? oceanBuoyancy.CurrentWaterImmersion : 0f;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnValidate()
        {
            if (oceanBuoyancy == null)
                oceanBuoyancy = GetComponent<global::OceanBuoyancy>();
        }

        private void ResolveReferences()
        {
            if (oceanBuoyancy == null)
                oceanBuoyancy = GetComponent<global::OceanBuoyancy>();
        }

        public bool TryGetWaterSurface(out Vector3 position, out Vector3 normal, out Vector3 waterVelocity, out float sampleAge)
        {
            if (oceanBuoyancy != null)
            {
                return oceanBuoyancy.TryGetAverageWaterSurface(
                    out position, out normal, out waterVelocity, out sampleAge);
            }

            position = default;
            normal = Vector3.up;
            waterVelocity = Vector3.zero;
            sampleAge = float.PositiveInfinity;
            return false;
        }
    }
}
