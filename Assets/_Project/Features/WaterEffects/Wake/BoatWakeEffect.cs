using UnityEngine;
using Assets._Project.Features.Boat;

namespace Assets._Project.Features.WaterEffects
{
    /// <summary>
    /// Optional visual-only boat wake. Reads motion and rudder/sail input from
    /// BoatController; the boat physics component has no ParticleSystem dependency.
    /// Add this beside BoatController and optionally assign a custom wake system.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoatWakeEffect : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BoatController boat;
        [SerializeField] private Transform stern;
        [SerializeField] private ParticleSystem particles;
        [SerializeField] private Material particleMaterial;
        [SerializeField] private bool createDefaultParticles = true;

        [Header("Speed Response")]
        [SerializeField, Min(0f)] private float startSpeed = 1.1f;
        [SerializeField, Min(0.1f)] private float fullWakeSpeed = 7.5f;
        [SerializeField, Min(0f)] private float maximumEmission = 34f;
        [SerializeField, Min(0f)] private float minimumEmission = 0f;
        [SerializeField, Min(0f)] private float steeringWakeBoost = 0.35f;
        [SerializeField, Min(0f)] private float sailWakeBoost = 0.15f;
        [SerializeField, Min(0.01f)] private float emissionResponse = 3.5f;

        [Header("Spawn Placement")]
        [SerializeField] private Vector3 defaultLocalSternOffset = new Vector3(0f, -0.12f, -1.8f);
        [SerializeField] private Vector3 defaultLocalEmissionEuler = new Vector3(0f, 180f, 0f);

        private float currentEmission;
        private Material runtimeMaterial;

        public BoatController Boat => boat;
        public ParticleSystem Particles => particles;
        public float CurrentEmission => currentEmission;

        private void Reset()
        {
            boat = GetComponent<BoatController>();
        }

        private void Awake()
        {
            ResolveReferences();
            if (particles == null && createDefaultParticles)
                CreateDefaultParticles();
            ConfigureParticles();
        }

        private void OnValidate()
        {
            startSpeed = Mathf.Max(0f, startSpeed);
            fullWakeSpeed = Mathf.Max(startSpeed + 0.1f, fullWakeSpeed);
            maximumEmission = Mathf.Max(0f, maximumEmission);
            minimumEmission = Mathf.Max(0f, minimumEmission);
            steeringWakeBoost = Mathf.Max(0f, steeringWakeBoost);
            sailWakeBoost = Mathf.Max(0f, sailWakeBoost);
            emissionResponse = Mathf.Max(0.01f, emissionResponse);
        }

        private void Update()
        {
            if (boat == null || particles == null)
                return;

            float targetEmission = 0f;
            if (!boat.IsAnchored)
            {
                float speed01 = Mathf.InverseLerp(startSpeed, fullWakeSpeed, boat.PlanarSpeed);
                if (speed01 > 0f)
                {
                    float controlBoost = 1f
                        + Mathf.Abs(boat.SteeringInput) * steeringWakeBoost
                        + boat.SailTrim * sailWakeBoost;
                    targetEmission = Mathf.Lerp(minimumEmission, maximumEmission, speed01)
                                     * controlBoost;
                    targetEmission = Mathf.Min(targetEmission, maximumEmission * (1f + steeringWakeBoost + sailWakeBoost));
                }
            }

            currentEmission = Mathf.MoveTowards(
                currentEmission, targetEmission, emissionResponse * maximumEmission * Time.deltaTime);

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = currentEmission;
            if (currentEmission > 0.01f && !particles.isPlaying)
                particles.Play();
        }

        private void OnDisable()
        {
            currentEmission = 0f;
            if (particles != null)
            {
                ParticleSystem.EmissionModule emission = particles.emission;
                emission.rateOverTime = 0f;
                particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(runtimeMaterial);
                else
                    DestroyImmediate(runtimeMaterial);
            }
        }

        private void ResolveReferences()
        {
            if (boat == null)
                boat = GetComponent<BoatController>();
        }

        private void ConfigureParticles()
        {
            if (particles == null)
                return;

            ParticleSystem.MainModule main = particles.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.Automatic;
            main.maxParticles = Mathf.Max(main.maxParticles, 256);

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            ParticleSystem.InheritVelocityModule inherit = particles.inheritVelocity;
            inherit.enabled = true;
            inherit.mode = ParticleSystemInheritVelocityMode.Initial;
            inherit.curve = new ParticleSystem.MinMaxCurve(1f);

            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
                return;

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            if (particleMaterial != null)
                renderer.sharedMaterial = particleMaterial;
            else if (renderer.sharedMaterial == null)
                TryCreateDefaultMaterial(renderer);
        }

        private void CreateDefaultParticles()
        {
            GameObject particleObject = new GameObject("Boat Wake Particles");
            Transform parent = stern != null ? stern : transform;
            particleObject.transform.SetParent(parent, false);
            particleObject.transform.localPosition = stern != null ? Vector3.zero : defaultLocalSternOffset;
            particleObject.transform.localRotation = Quaternion.Euler(defaultLocalEmissionEuler);

            particles = particleObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.duration = 5f;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.65f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
            main.startColor = new Color(0.9f, 0.97f, 1f, 0.5f);
            main.gravityModifier = 0f;
            main.maxParticles = 256;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 0.22f;
            shape.length = 0.35f;

            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.82f, 0.93f, 1f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.42f, 0.12f),
                    new GradientAlphaKey(0.24f, 0.7f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.45f),
                new Keyframe(0.3f, 1f),
                new Keyframe(1f, 1.5f));
            size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.alignment = ParticleSystemRenderSpace.View;
        }

        private void TryCreateDefaultMaterial(ParticleSystemRenderer renderer)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                return;

            runtimeMaterial = new Material(shader)
            {
                name = "Runtime Boat Wake Material"
            };
            if (runtimeMaterial.HasProperty("_BaseColor"))
                runtimeMaterial.SetColor("_BaseColor", Color.white);
            renderer.sharedMaterial = runtimeMaterial;
        }
    }
}
