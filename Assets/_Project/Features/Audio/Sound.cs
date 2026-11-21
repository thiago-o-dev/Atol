using UnityEngine;

namespace Assets._Project.Features.Audio
{
    // Toca um som solto no mundo. Clip vazio não faz nada, então os campos de som são opcionais.
    public static class Sound
    {
        public static void Play(AudioClip clip, Vector3 position, float volume = 1f)
        {
            if (clip)
                AudioSource.PlayClipAtPoint(clip, position, volume);
        }
    }
}
