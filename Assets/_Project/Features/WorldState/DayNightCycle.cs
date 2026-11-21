using UnityEngine;
using UnityEngine.Rendering;

// Ciclo de dia e noite. O sol gira a partir de onde a cena o deixou; conforme ele desce, a luz esfria,
// o Volume da noite ganha peso e as estrelas aparecem. Fica parado durante as cutscenes.
public sealed class DayNightCycle : MonoBehaviour
{
    [Tooltip("Se vazio, usa o sol das Lighting Settings.")]
    public Light Sun;
    [Tooltip("Minutos de jogo para um dia inteiro.")]
    public float DayLengthMinutes = 12f;
    public Volume NightVolume;
    [Tooltip("Céu de estrelas. Acompanha a câmera e só aparece à noite.")]
    public ParticleSystemRenderer Stars;
    public Color MoonColor = new Color(0.55f, 0.66f, 0.95f, 1f);
    [Tooltip("Fração da luz do sol que sobra como luar.")]
    [Range(0f, 1f)] public float MoonLight = 0.18f;

    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    private Color _sunColor;
    private float _sunIntensity;
    private MaterialPropertyBlock _starProperties;

    // 0 no meio da noite, 1 com o sol alto.
    public static float Daylight { get; private set; } = 1f;

    private void Awake()
    {
        if (Sun == null)
            Sun = RenderSettings.sun;

        _sunColor = Sun.color;
        _sunIntensity = Sun.intensity;
        _starProperties = new MaterialPropertyBlock();
    }

    private void OnDestroy()
    {
        Daylight = 1f;
        Sun.color = _sunColor;
        Sun.intensity = _sunIntensity;
    }

    private void Update()
    {
        if (!CutsceneComponent.AnyPlaying)
            Sun.transform.Rotate(Vector3.right, 360f / (DayLengthMinutes * 60f) * Time.deltaTime, Space.World);

        // Altura do sol sobre o horizonte: -1 a pino embaixo, 1 a pino em cima.
        float height = -Sun.transform.forward.y;

        Daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.22f, height));

        Sun.color = Color.Lerp(MoonColor, _sunColor, Daylight);
        Sun.intensity = _sunIntensity * Mathf.Lerp(MoonLight, 1f, Daylight);
        NightVolume.weight = 1f - Daylight;

        _starProperties.SetColor(BaseColor, new Color(1f, 1f, 1f, 1f - Daylight));
        Stars.SetPropertyBlock(_starProperties);
    }

    private void LateUpdate()
    {
        if (Camera.main)
            Stars.transform.position = Camera.main.transform.position;
    }
}
