using UnityEngine;

// Névoa baixa sobre o mar. Acompanha a câmera na altura da água, toma a cor da névoa da cena
// e engrossa quando escurece.
public sealed class SeaMist : MonoBehaviour
{
    public ParticleSystem Particles;
    [Range(0f, 1f)] public float Opacity = 0.16f;
    [Tooltip("Fração da névoa que sobra com o sol alto.")]
    [Range(0f, 1f)] public float DayAmount = 0.45f;

    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    private ParticleSystemRenderer _renderer;
    private MaterialPropertyBlock _properties;
    private float _height;

    private void Awake()
    {
        _renderer = Particles.GetComponent<ParticleSystemRenderer>();
        _properties = new MaterialPropertyBlock();
        _height = transform.position.y;
    }

    private void Update()
    {
        Color color = RenderSettings.fogColor;
        color.a = Opacity * Mathf.Lerp(1f, DayAmount, DayNightCycle.Daylight);

        _properties.SetColor(BaseColor, color);
        _renderer.SetPropertyBlock(_properties);
    }

    private void LateUpdate()
    {
        if (Camera.main == null)
            return;

        Vector3 position = Camera.main.transform.position;
        position.y = _height;
        transform.position = position;
    }
}
