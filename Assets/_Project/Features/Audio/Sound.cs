
using UnityEngine;

namespace Assets._Project.Features.Audio
{
    /// <summary>
    /// Plays one-shot sounds in the world.
    /// Empty clips are safely ignored.
    /// </summary>
    public static class Sound
    {
        /// <summary>
        /// Plays a sound using Unity's default 3D audio settings.
        /// Preserves compatibility with existing callers.
        /// </summary>
        public static void Play(AudioClip clip, Vector3 position, float volume = 1f)
        {
            if (clip)
                AudioSource.PlayClipAtPoint(clip, position, volume);
        }

        /// <summary>
        /// Plays a sound with configurable 3D distance attenuation.
        /// </summary>
        public static void Play(
            AudioClip clip,
            Vector3 position,
            float volume,
            float minDistance,
            float maxDistance)
        {
            if (!clip)
                return;

            minDistance = Mathf.Max(0.01f, minDistance);
            maxDistance = Mathf.Max(minDistance + 0.01f, maxDistance);

            GameObject soundObject = new GameObject($"OneShotAudio_{clip.name}");
            soundObject.transform.position = position;

            AudioSource source = soundObject.AddComponent<AudioSource>();

            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);

            // Fully 3D audio.
            source.spatialBlend = 1f;

            // Gradually fades from Min Distance to Max Distance.
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;

            // Avoid unwanted pitch modulation.
            source.dopplerLevel = 0f;
            source.playOnAwake = false;

            source.Play();

            Object.Destroy(
                soundObject,
                clip.length / Mathf.Max(Mathf.Abs(source.pitch), 0.01f)
            );
        }
    }
}
