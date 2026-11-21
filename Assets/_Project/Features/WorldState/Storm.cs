using Assets._Project.Features.Audio;
using UnityEngine;

// Tempestade do mundo decaído: a partir de um nível de Decay chove, relampeja e troveja.
public sealed class Storm : MonoBehaviour
{
    [Tooltip("Nível de Decay em que a tempestade começa.")]
    public int FromDecay = 2;
    public ParticleSystem Rain;
    public float RainRate = 900f;
    public AudioSource RainLoop;
    [Range(0f, 1f)] public float RainVolume = 0.5f;
    public Light Lightning;
    public float LightningIntensity = 3.5f;
    public Vector2 LightningInterval = new Vector2(7f, 18f);
    public AudioClip Thunder;

    private float _strength;
    private float _nextLightningTime;

    private void Start()
    {
        Lightning.intensity = 0f;
        RainLoop.volume = 0f;
    }

    private void Update()
    {
        WorldStateController world = WorldStateController.Instance;
        bool storming = world && world.Decay >= FromDecay && !CutsceneComponent.AnyPlaying;

        _strength = Mathf.MoveTowards(_strength, storming ? 1f : 0f, 0.25f * Time.deltaTime);

        ParticleSystem.EmissionModule emission = Rain.emission;
        emission.rateOverTime = RainRate * _strength;
        RainLoop.volume = RainVolume * _strength;

        Lightning.intensity = Mathf.Lerp(Lightning.intensity, 0f, 7f * Time.deltaTime);

        if (_strength >= 1f && Time.time >= _nextLightningTime)
        {
            _nextLightningTime = Time.time + Random.Range(LightningInterval.x, LightningInterval.y);

            Lightning.intensity = LightningIntensity;
            Sound.Play(Thunder, transform.position);
        }
    }

    // A chuva cai em volta da câmera, onde quer que ela esteja.
    private void LateUpdate()
    {
        if (Camera.main)
            transform.position = Camera.main.transform.position;
    }
}
