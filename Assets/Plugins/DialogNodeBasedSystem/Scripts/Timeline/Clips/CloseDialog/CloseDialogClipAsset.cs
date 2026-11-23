#if UNITY_TIMELINE
using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace cherrydev
{
    /// <summary>
    /// Timeline clip that closes the bound DialogBehaviour when the clip is reached.
    /// </summary>
    [Serializable]
    public class CloseDialogClipAsset : PlayableAsset, ITimelineClipAsset
    {
        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<CloseDialogClip>.Create(graph);
        }
    }
}
#endif
