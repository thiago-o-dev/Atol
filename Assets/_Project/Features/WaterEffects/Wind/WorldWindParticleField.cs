using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Lightweight, world-space wind wisps around a moving focus (normally the player).
/// Particles are simulated in world space and depth-tested by the particle shader,
/// so opaque scene geometry naturally occludes them.
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldWindParticleField : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SpectralOceanFFT ocean;
    [Tooltip("Usually the player root. If left empty, this component's transform is used.")]
    [SerializeField] private Transform followTarget;
    [Tooltip("Optional. When empty, the component creates a material from the wind particle shader.")]
    [SerializeField] private Material particleMaterial;
    [SerializeField] private Shader particleShader;

    [Header("Spawn Area")]
    [Tooltip("Width, height and depth of the invisible particle spawn volume, in world metres.")]
    [SerializeField] private Vector3 spawnVolume = new Vector3(16f, 3.2f, 16f);
    [Tooltip("Spawn volume height above the follow target.")]
    [SerializeField] private float heightAboveTarget = 2.5f;
    [Tooltip("How quickly the spawn volume follows the target. Existing particles remain in world space.")]
    [SerializeField, Min(0.1f)] private float followSharpness = 14f;

    [Header("Motion")]
    [SerializeField, Min(0.1f)] private float lightWindSpeed = 3.5f;
    [SerializeField, Min(0.1f)] private float strongWindSpeed = 10f;
    [SerializeField, Min(0.1f)] private float minimumLifetime = 0.65f;
    [SerializeField, Min(0.1f)] private float maximumLifetime = 1.15f;
    [Tooltip("Small low-frequency turbulence. Keep this subtle so the wind still travels downwind.")]
    [SerializeField, Range(0f, 1f)] private float turbulence = 0.18f;

    [Header("Appearance")]
    [Tooltip("Particles emitted per second at density 1 and strong wind. Density and wind strength scale this value.")]
    [SerializeField, Min(0f)] private float baseEmissionRate = 11f;

    [Header("Performance")]
    [SerializeField, Range(8, 256)] private int maximumParticles = 96;

    private ParticleSystem windParticles;
    private ParticleSystemRenderer particleRenderer;
    private Material generatedMaterial;
    private Vector3 smoothedEmitterPosition;
    private bool hasEmitterPosition;
    private bool warnedMissingShader;

    private const string ParticleShaderName = "CoastalBreakers/Particles/Soft Wind Wisps";

    private void Reset()
    {
        if (particleShader == null)
            particleShader = Shader.Find(ParticleShaderName);
        if (followTarget == null)
            followTarget = transform;
    }

    private void OnValidate()
    {
        spawnVolume.x = Mathf.Max(1f, spawnVolume.x);
        spawnVolume.y = Mathf.Max(1f, spawnVolume.y);
        spawnVolume.z = Mathf.Max(1f, spawnVolume.z);
        heightAboveTarget = Mathf.Max(-5f, heightAboveTarget);
        followSharpness = Mathf.Max(0.1f, followSharpness);
        lightWindSpeed = Mathf.Max(0.1f, lightWindSpeed);
        strongWindSpeed = Mathf.Max(lightWindSpeed, strongWindSpeed);
        minimumLifetime = Mathf.Max(0.1f, minimumLifetime);
        maximumLifetime = Mathf.Max(minimumLifetime, maximumLifetime);
        baseEmissionRate = Mathf.Max(0f, baseEmissionRate);
        if (particleShader == null)
            particleShader = Shader.Find(ParticleShaderName);
    }

    private void Awake()
    {
        if (followTarget == null)
            followTarget = transform;
        if (ocean == null)
            ocean = UnityEngine.Object.FindAnyObjectByType<SpectralOceanFFT>();

        CreateParticleSystem();
    }

    private void LateUpdate()
    {
        if (windParticles == null || ocean == null)
            return;

        Vector3 targetPosition = (followTarget != null ? followTarget.position : transform.position)
            + Vector3.up * heightAboveTarget;

        if (!hasEmitterPosition)
        {
            smoothedEmitterPosition = targetPosition;
            hasEmitterPosition = true;
        }
        else
        {
            float blend = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            smoothedEmitterPosition = Vector3.Lerp(smoothedEmitterPosition, targetPosition, blend);
        }

        // Keep the spawn box world-axis aligned; the particles themselves are world simulated.
        Transform emitter = windParticles.transform;
        emitter.position = smoothedEmitterPosition;
        emitter.rotation = Quaternion.identity;
        emitter.localScale = Vector3.one;

        Vector2 direction = ocean.CurrentWindDirectionXZ;
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector2.right;
        direction.Normalize();

        float strength = Mathf.Clamp01(ocean.CurrentWindStrength);
        float density = ocean.WindStreaksEnabled ? Mathf.Clamp(ocean.WindStreakFrequency, 0f, 3f) : 0f;
        float windPresence = Mathf.Lerp(0.08f, 1f, strength);
        float emissionRate = Mathf.Clamp(baseEmissionRate * density * windPresence, 0f, 40f);

        ParticleSystem.EmissionModule emission = windParticles.emission;
        emission.rateOverTime = emissionRate;

        float speed = Mathf.Lerp(lightWindSpeed, strongWindSpeed, strength) * ocean.WindParticleSpeedMultiplier;
        Vector3 windVelocity = new Vector3(direction.x, 0f, direction.y) * speed;
        ParticleSystem.VelocityOverLifetimeModule velocity = windParticles.velocityOverLifetime;
        velocity.x = new ParticleSystem.MinMaxCurve(windVelocity.x);
        velocity.y = new ParticleSystem.MinMaxCurve(windVelocity.y);
        velocity.z = new ParticleSystem.MinMaxCurve(windVelocity.z);

        ParticleSystem.MainModule main = windParticles.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minimumLifetime, maximumLifetime);
        Color liveTint = ocean.WindParticleTint;
        main.startColor = new Color(liveTint.r, liveTint.g, liveTint.b, 0.34f * ocean.WindParticleOpacity);
        float liveWidth = ocean.WindParticleWidthMeters;
        main.startSize = new ParticleSystem.MinMaxCurve(liveWidth * 0.75f, liveWidth * 1.25f);
        particleRenderer.lengthScale = Mathf.Lerp(2.5f, 5.2f, ocean.WindParticleLength);

        if (!ocean.WindStreaksEnabled || density <= 0.001f)
            emission.rateOverTime = 0f;
    }

    private void CreateParticleSystem()
    {
        GameObject particleObject = new GameObject("World Wind Wisps");
        particleObject.transform.SetParent(transform, false);
        particleObject.transform.localPosition = Vector3.zero;
        particleObject.transform.localRotation = Quaternion.identity;
        particleObject.transform.localScale = Vector3.one;

        windParticles = particleObject.AddComponent<ParticleSystem>();
        particleRenderer = particleObject.GetComponent<ParticleSystemRenderer>();
        ConfigureMaterial();
        ConfigureParticleSystem();
    }

    private void ConfigureParticleSystem()
    {
        // Stop before changing modules. This also makes reconfiguration safe if
        // this method is called again from an editor/runtime setup path.
        windParticles.Stop();

        ParticleSystem.MainModule main = windParticles.main;
        main.loop = true;
        main.duration = 5f;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startDelay = 0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minimumLifetime, maximumLifetime);
        main.startSpeed = 0f;
        float initialWidth = ocean != null ? ocean.WindParticleWidthMeters : 0.055f;
        Color initialTint = ocean != null ? ocean.WindParticleTint : Color.white;
        main.startSize = new ParticleSystem.MinMaxCurve(initialWidth * 0.75f, initialWidth * 1.25f);
        main.startColor = new Color(initialTint.r, initialTint.g, initialTint.b, 0.34f);
        main.gravityModifier = 0f;
        main.maxParticles = maximumParticles;
        main.cullingMode = ParticleSystemCullingMode.Automatic;

        ParticleSystem.EmissionModule emission = windParticles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0f;

        ParticleSystem.ShapeModule shape = windParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = spawnVolume;
        shape.boxThickness = Vector3.one;

        ParticleSystem.VelocityOverLifetimeModule velocity = windParticles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f);

        ParticleSystem.NoiseModule noise = windParticles.noise;
        noise.enabled = turbulence > 0.001f;
        noise.strength = new ParticleSystem.MinMaxCurve(turbulence);
        noise.frequency = 0.28f;
        noise.octaveCount = 1;
        noise.damping = true;
        noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.12f);
        noise.quality = ParticleSystemNoiseQuality.Low;

        ParticleSystem.ColorOverLifetimeModule color = windParticles.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.08f),
                new GradientAlphaKey(0.72f, 0.48f),
                new GradientAlphaKey(0.28f, 0.82f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = new ParticleSystem.MinMaxGradient(gradient);

        ParticleSystem.SizeOverLifetimeModule size = windParticles.sizeOverLifetime;
        size.enabled = true;
        AnimationCurve taper = new AnimationCurve(
            new Keyframe(0f, 0.7f),
            new Keyframe(0.10f, 1f),
            new Keyframe(0.78f, 0.78f),
            new Keyframe(1f, 0.05f));
        size.size = new ParticleSystem.MinMaxCurve(1f, taper);

        // Stretch is driven by the real world-space particle velocity set in LateUpdate.
        // A larger velocityScale gives each wisp a visible travel-oriented silhouette.
        particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        particleRenderer.alignment = ParticleSystemRenderSpace.View;
        particleRenderer.freeformStretching = true;
        particleRenderer.rotateWithStretchDirection = true;
        particleRenderer.lengthScale = ocean != null ? Mathf.Lerp(2.5f, 5.2f, ocean.WindParticleLength) : 4.0f;
        particleRenderer.velocityScale = 0.18f;
        particleRenderer.cameraVelocityScale = 0f;
        particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;
        particleRenderer.lightProbeUsage = LightProbeUsage.Off;
        particleRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        particleRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        particleRenderer.sortingFudge = 0f;

        // Explicitly restart only after every module and renderer property is configured.
        windParticles.Play();
    }

    private void ConfigureMaterial()
    {
        if (particleMaterial != null)
        {
            particleRenderer.sharedMaterial = particleMaterial;
            return;
        }

        if (particleShader == null)
            particleShader = Shader.Find(ParticleShaderName);

        if (particleShader == null)
        {
            particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!warnedMissingShader)
            {
                Debug.LogWarning(
                    "WorldWindParticleField could not find its soft-wisp shader. " +
                    "Assign a material using 'CoastalBreakers/Particles/Soft Wind Wisps' for the intended look.", this);
                warnedMissingShader = true;
            }
        }

        if (particleShader != null)
        {
            generatedMaterial = new Material(particleShader)
            {
                name = "Runtime World Wind Wisps Material",
                hideFlags = HideFlags.DontSave
            };
            particleRenderer.sharedMaterial = generatedMaterial;
        }
    }

    private void OnDestroy()
    {
        if (generatedMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(generatedMaterial);
            else
                DestroyImmediate(generatedMaterial);
        }
    }
}
