using Assets._Project.Features.Shark;
using UnityEngine;

namespace Assets._Project.Features.Audio
{
    // Trilha do jogo (Ambience.prefab): mar e música em loop, uma camada de tensão que sobe conforme
    // os tubarões ameaçam o player e tudo abafado enquanto a câmera está debaixo d'água.
    public sealed class AmbienceAudio : MonoBehaviour
    {
        public AudioSource Sea;
        public AudioSource Music;
        public AudioSource Tension;
        [Tooltip("Filtros das três fontes. O corte desce quando a câmera submerge.")]
        public AudioLowPassFilter[] Filters;

        [Range(0f, 1f)] public float MusicVolume = 0.3f;
        [Tooltip("Quanto a música cede espaço com a tensão no máximo.")]
        [Range(0f, 1f)] public float MusicDucking = 0.6f;
        [Range(0f, 1f)] public float TensionVolume = 0.55f;
        public float FadeSpeed = 0.5f;
        public float UnderwaterCutoff = 600f;

        private const float OpenCutoff = 22000f;

        private SharkController[] _sharks;
        private OceanUnderwaterController _underwater;
        private float _threat;
        private float _cutoff = OpenCutoff;

        private void Start()
        {
            _sharks = FindObjectsByType<SharkController>(FindObjectsInactive.Include);
            _underwater = FindAnyObjectByType<OceanUnderwaterController>();

            Tension.volume = 0f;
        }

        private void Update()
        {
            float threat = 0f;

            foreach (SharkController shark in _sharks)
            {
                if (shark && shark.isActiveAndEnabled)
                    threat = Mathf.Max(threat, shark.Threat);
            }

            _threat = Mathf.MoveTowards(_threat, threat, FadeSpeed * Time.unscaledDeltaTime);

            Tension.volume = TensionVolume * _threat;
            Music.volume = MusicVolume * (1f - MusicDucking * _threat);

            bool submerged = _underwater && _underwater.IsUnderwater;
            _cutoff = Mathf.Lerp(_cutoff, submerged ? UnderwaterCutoff : OpenCutoff, 6f * Time.unscaledDeltaTime);

            foreach (AudioLowPassFilter filter in Filters)
                filter.cutoffFrequency = _cutoff;
        }
    }
}
