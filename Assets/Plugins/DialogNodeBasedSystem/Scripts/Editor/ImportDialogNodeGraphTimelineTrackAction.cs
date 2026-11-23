#if UNITY_EDITOR && UNITY_TIMELINE
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Timeline.Actions;
using UnityEngine.Timeline;

namespace cherrydev
{
    /// <summary>
    /// Adds an Import Node Graph command to the actual Unity Timeline track right-click menu.
    /// The TimelineAction context carries the track under the context menu, unlike an Inspector
    /// MenuItem which only appears when right-clicking an object in the Inspector/project view.
    /// </summary>
    [MenuEntry("Import Dialog Node Graph...")]
    public sealed class ImportDialogNodeGraphTimelineTrackAction : TimelineAction
    {
        public override ActionValidity Validate(ActionContext context)
        {
            if (context.tracks == null)
                return ActionValidity.NotApplicable;

            List<TrackAsset> contextTracks = context.tracks.ToList();
            if (contextTracks.Count != 1)
                return ActionValidity.NotApplicable;

            DialogBehaviourTrack dialogTrack = contextTracks[0] as DialogBehaviourTrack;
            if (dialogTrack == null)
                return ActionValidity.NotApplicable;

            if (dialogTrack.isSubTrack || dialogTrack.lockedInHierarchy)
                return ActionValidity.Invalid;

            return ActionValidity.Valid;
        }

        public override bool Execute(ActionContext context)
        {
            if (context.tracks == null)
                return false;

            DialogBehaviourTrack dialogTrack = context.tracks.OfType<DialogBehaviourTrack>().FirstOrDefault();
            if (dialogTrack == null || dialogTrack.isSubTrack || dialogTrack.lockedInHierarchy)
                return false;

            DialogNodeGraphTimelineImporter.OpenForTimelineTrack(dialogTrack);
            return true;
        }
    }
}
#endif
