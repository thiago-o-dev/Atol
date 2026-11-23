#if UNITY_TIMELINE
using System;
using UnityEngine;
using UnityEngine.Playables;

namespace cherrydev
{
    [Serializable]
    public class SentencePerformingClipAsset : PlayableAsset
    {
        public ExposedReference<SentenceNode> SentenceNode;

        // Used by the editor importer to safely replace only clips generated from this graph.
        [HideInInspector] public DialogNodeGraph SourceGraph;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<SentencePerformingClip> playable = ScriptPlayable<SentencePerformingClip>.Create(graph);
            SentencePerformingClip clip = playable.GetBehaviour();
            clip.SentenceNode = SentenceNode.Resolve(graph.GetResolver());
            clip.DialogBehaviour = owner.GetComponent<DialogBehaviour>();
            return playable;
        }
    }
}
#endif
