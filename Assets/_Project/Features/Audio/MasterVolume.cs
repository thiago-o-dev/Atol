using UnityEngine;

namespace Assets._Project.Features.Audio
{
    // Volume geral do jogo, guardado entre sessões.
    public static class MasterVolume
    {
        private const string Key = "MasterVolume";

        public static float Value
        {
            get => PlayerPrefs.GetFloat(Key, 1f);
            set
            {
                PlayerPrefs.SetFloat(Key, Mathf.Clamp01(value));
                Apply();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Apply()
        {
            AudioListener.volume = Value;
        }
    }
}
