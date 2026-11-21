using UnityEngine;
using UnityEngine.Rendering;

// O mundo perdendo a cor conforme decai: o Volume ganha peso e a névoa escurece junto com o Decay.
// O visual de cada extremo fica no perfil do Volume, aqui só a mistura.
public sealed class DecayAtmosphere : MonoBehaviour
{
    public Volume Volume;
    public Color DecayedFogColor = new Color(0.22f, 0.27f, 0.32f, 1f);
    [Tooltip("Multiplicador da densidade da névoa com o Decay no máximo.")]
    public float DecayedFogDensity = 2.2f;
    [Tooltip("Quanto do caminho até o alvo anda por segundo.")]
    public float BlendSpeed = 0.4f;

    private Color _fogColor;
    private float _fogDensity;
    private float _blend;

    private void Awake()
    {
        _fogColor = RenderSettings.fogColor;
        _fogDensity = RenderSettings.fogDensity;
    }

    private void OnDestroy()
    {
        RenderSettings.fogColor = _fogColor;
        RenderSettings.fogDensity = _fogDensity;
    }

    private void Update()
    {
        WorldStateController world = WorldStateController.Instance;
        float target = world ? world.Decay / (float)WorldStateController.DecayMaximum : 0f;

        _blend = Mathf.MoveTowards(_blend, target, BlendSpeed * Time.deltaTime);

        Volume.weight = _blend;
        RenderSettings.fogColor = Color.Lerp(_fogColor, DecayedFogColor, _blend);
        RenderSettings.fogDensity = _fogDensity * Mathf.Lerp(1f, DecayedFogDensity, _blend);
    }
}
