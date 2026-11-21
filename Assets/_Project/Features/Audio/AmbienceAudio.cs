using Assets._Project.Features.Shark;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Assets._Project.Features.Audio
{
    /// <summary>A named music clip configured on the AmbienceAudio component.</summary>
    [Serializable]
    public sealed class MusicTrack
    {
        [Tooltip("Identifier passed to AmbienceAudio.PlayTrack. If using an enum, this must match its member name.")]
        public string Name;
        public AudioClip Clip;
    }

    // Game ambience: sea and music loops, a threat layer that rises as sharks approach,
    // and low-pass filtering while the camera is underwater.
    public sealed class AmbienceAudio : MonoBehaviour
    {
        [Header("Ambience")]
        public AudioSource Sea;
        [Tooltip("Primary music source. Additional sources are pooled at runtime to crossfade tracks.")]
        public AudioSource Music;
        public AudioSource Tension;
        [Tooltip("Filters for the ambience sources. The cutoff drops when the camera submerges.")]
        public AudioLowPassFilter[] Filters;

        [Header("Music tracks")]
        [Tooltip("Add one entry per song. Call AmbienceAudio.PlayTrack with its Name, or an enum member with the same name.")]
        public MusicTrack[] Tracks = Array.Empty<MusicTrack>();
        [Min(0f)]
        [Tooltip("Duration of each music crossfade, in seconds.")]
        public float TrackFadeDuration = 2f;

        [Header("Mix")]
        [Range(0f, 1f)] public float MusicVolume = 0.3f;
        [Tooltip("How much music yields to the threat layer at maximum threat.")]
        [Range(0f, 1f)] public float MusicDucking = 0.6f;
        [Range(0f, 1f)] public float TensionVolume = 0.55f;
        public float FadeSpeed = 0.5f;
        public float UnderwaterCutoff = 600f;

        private const float OpenCutoff = 22000f;
        private const float WeightEpsilon = 0.0001f;

        private sealed class MusicLayer
        {
            public AudioSource Source;
            public float Weight;
        }

        private sealed class FadingLayer
        {
            public MusicLayer Layer;
            public float StartingWeight;
        }

        public static AmbienceAudio Instance { get; private set; }
        public string CurrentTrackName { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => Instance = null;

        private SharkController[] _sharks;
        private OceanUnderwaterController _underwater;
        private readonly List<MusicLayer> _musicLayers = new List<MusicLayer>();
        private Coroutine _musicFadeRoutine;
        private float _threat;
        private float _cutoff = OpenCutoff;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("More than one AmbienceAudio exists. The first instance remains the global music system.", this);
                enabled = false;
                return;
            }

            Instance = this;

            if (Music != null)
            {
                Music.loop = true;
                _musicLayers.Add(new MusicLayer
                {
                    Source = Music,
                    Weight = (Music.clip != null || Music.playOnAwake) ? 1f : 0f
                });
            }
        }

        private void Start()
        {
            _sharks = FindObjectsByType<SharkController>(FindObjectsInactive.Include);
            _underwater = FindAnyObjectByType<OceanUnderwaterController>();

            if (Tension != null)
                Tension.volume = 0f;

            // Keep the original prefab's Play On Awake behavior reliable if a track is assigned.
            if (Music != null && Music.playOnAwake && !Music.isPlaying)
                Music.Play();
        }

        /// <summary>
        /// Crossfades to a track whose Name matches the supplied identifier (case-insensitive).
        /// Example: AmbienceAudio.PlayTrack("Exploration").
        /// </summary>
        public static void PlayTrack(string trackName)
        {
            if (Instance == null)
            {
                Debug.LogWarning("Cannot play a music track because no active AmbienceAudio exists.");
                return;
            }

            Instance.SwitchTrack(trackName);
        }

        /// <summary>
        /// Enum convenience overload. The enum member name must match a track Name in the inspector.
        /// Example: AmbienceAudio.PlayTrack(MusicId.Exploration).
        /// </summary>
        public static void PlayTrack(Enum track)
        {
            if (track == null)
            {
                Debug.LogWarning("Cannot play a music track from a null enum value.");
                return;
            }

            PlayTrack(track.ToString());
        }

        private void SwitchTrack(string trackName)
        {
            if (string.IsNullOrWhiteSpace(trackName))
            {
                Debug.LogWarning("PlayTrack requires a non-empty track name.", this);
                return;
            }

            MusicTrack track = null;
            if (Tracks != null)
            {
                foreach (MusicTrack candidate in Tracks)
                {
                    if (candidate != null && string.Equals(candidate.Name, trackName, StringComparison.OrdinalIgnoreCase))
                    {
                        track = candidate;
                        break;
                    }
                }
            }

            if (track == null || track.Clip == null)
            {
                Debug.LogWarning($"Music track '{trackName}' was not found or has no AudioClip in the AmbienceAudio track list.", this);
                return;
            }

            if (string.Equals(CurrentTrackName, track.Name, StringComparison.OrdinalIgnoreCase))
                return;

            // The prefab may already be playing the initial clip before the first explicit request.
            // Adopt its configured name instead of briefly crossfading a clip into itself.
            if (_musicFadeRoutine == null)
            {
                foreach (MusicLayer layer in _musicLayers)
                {
                    if (layer.Source != null && layer.Source.isPlaying &&
                        layer.Source.clip == track.Clip && layer.Weight >= 0.999f)
                    {
                        CurrentTrackName = track.Name;
                        return;
                    }
                }
            }

            if (Music == null)
            {
                Debug.LogWarning("Cannot play a music track because the primary Music AudioSource is not assigned.", this);
                return;
            }

            if (_musicFadeRoutine != null)
            {
                StopCoroutine(_musicFadeRoutine);
                _musicFadeRoutine = null;
            }

            MusicLayer incoming = GetAvailableMusicLayer();
            incoming.Source.Stop();
            incoming.Source.clip = track.Clip;
            incoming.Source.loop = true;
            incoming.Source.volume = 0f;
            incoming.Weight = 0f;
            incoming.Source.Play();

            List<FadingLayer> outgoing = new List<FadingLayer>();
            foreach (MusicLayer layer in _musicLayers)
            {
                if (layer == incoming)
                    continue;

                if (layer.Source != null && layer.Source.isPlaying && layer.Weight > WeightEpsilon)
                {
                    outgoing.Add(new FadingLayer
                    {
                        Layer = layer,
                        StartingWeight = layer.Weight
                    });
                }
                else
                {
                    layer.Weight = 0f;
                    if (layer.Source != null)
                        layer.Source.Stop();
                }
            }

            CurrentTrackName = track.Name;

            float duration = Mathf.Max(0f, TrackFadeDuration);
            if (duration <= 0f)
            {
                FinishCrossfade(outgoing, incoming);
                return;
            }

            _musicFadeRoutine = StartCoroutine(CrossfadeMusic(outgoing, incoming, duration));
        }

        private MusicLayer GetAvailableMusicLayer()
        {
            foreach (MusicLayer layer in _musicLayers)
            {
                if (layer.Weight <= WeightEpsilon && layer.Source != null && !layer.Source.isPlaying)
                    return layer;
            }

            // All current layers are audible (for example, another transition was interrupted).
            // Add a source beside Music so it shares the same AudioLowPassFilter and spatial setup.
            AudioSource source = Music.gameObject.AddComponent<AudioSource>();
            CopyAudioSourceSettings(Music, source);
            source.clip = null;
            source.volume = 0f;
            source.playOnAwake = false;

            MusicLayer created = new MusicLayer { Source = source, Weight = 0f };
            _musicLayers.Add(created);
            return created;
        }

        private static void CopyAudioSourceSettings(AudioSource from, AudioSource to)
        {
            to.outputAudioMixerGroup = from.outputAudioMixerGroup;
            to.bypassEffects = from.bypassEffects;
            to.bypassListenerEffects = from.bypassListenerEffects;
            to.bypassReverbZones = from.bypassReverbZones;
            to.dopplerLevel = from.dopplerLevel;
            to.ignoreListenerPause = from.ignoreListenerPause;
            to.ignoreListenerVolume = from.ignoreListenerVolume;
            to.loop = true;
            to.maxDistance = from.maxDistance;
            to.minDistance = from.minDistance;
            to.mute = from.mute;
            to.panStereo = from.panStereo;
            to.pitch = from.pitch;
            to.priority = from.priority;
            to.reverbZoneMix = from.reverbZoneMix;
            to.rolloffMode = from.rolloffMode;
            to.spatialBlend = from.spatialBlend;
            to.spatialize = from.spatialize;
            to.spatializePostEffects = from.spatializePostEffects;
            to.spread = from.spread;
            to.velocityUpdateMode = from.velocityUpdateMode;
            to.playOnAwake = false;
        }

        private IEnumerator CrossfadeMusic(List<FadingLayer> outgoing, MusicLayer incoming, float duration)
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0f, 1f, progress);
                float fadeOut = Mathf.Cos(eased * Mathf.PI * 0.5f);
                float fadeIn = Mathf.Sin(eased * Mathf.PI * 0.5f);

                foreach (FadingLayer layer in outgoing)
                    layer.Layer.Weight = layer.StartingWeight * fadeOut;

                incoming.Weight = fadeIn;
                yield return null;
            }

            FinishCrossfade(outgoing, incoming);
            _musicFadeRoutine = null;
        }

        private static void FinishCrossfade(List<FadingLayer> outgoing, MusicLayer incoming)
        {
            foreach (FadingLayer fading in outgoing)
            {
                fading.Layer.Weight = 0f;
                fading.Layer.Source.Stop();
                fading.Layer.Source.clip = null;
                fading.Layer.Source.volume = 0f;
            }

            incoming.Weight = 1f;
        }

        private void Update()
        {
            if (_sharks == null)
                return;

            float threat = 0f;
            foreach (SharkController shark in _sharks)
            {
                if (shark && shark.isActiveAndEnabled)
                    threat = Mathf.Max(threat, shark.Threat);
            }

            _threat = Mathf.MoveTowards(_threat, threat, FadeSpeed * Time.unscaledDeltaTime);

            if (Tension != null)
                Tension.volume = TensionVolume * _threat;

            float musicDuck = 1f - MusicDucking * _threat;
            foreach (MusicLayer layer in _musicLayers)
            {
                if (layer.Source != null)
                    layer.Source.volume = MusicVolume * musicDuck * layer.Weight;
            }

            bool submerged = _underwater && _underwater.IsUnderwater;
            _cutoff = Mathf.Lerp(_cutoff, submerged ? UnderwaterCutoff : OpenCutoff, 6f * Time.unscaledDeltaTime);

            if (Filters != null)
            {
                foreach (AudioLowPassFilter filter in Filters)
                {
                    if (filter != null)
                        filter.cutoffFrequency = _cutoff;
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
