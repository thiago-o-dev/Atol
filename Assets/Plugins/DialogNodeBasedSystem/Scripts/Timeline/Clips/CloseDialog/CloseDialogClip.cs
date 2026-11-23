#if UNITY_TIMELINE
using System;
using UnityEngine;
using UnityEngine.Playables;

namespace cherrydev
{
    /// <summary>
    /// Executes CloseDialog once when its Timeline clip becomes active.
    /// The DialogBehaviour is supplied by the track binding.
    /// </summary>
    [Serializable]
    public class CloseDialogClip : PlayableBehaviour
    {
        private bool _closeRequested;

        public override void OnGraphStart(Playable playable)
        {
            _closeRequested = false;
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (!Application.isPlaying || _closeRequested)
                return;

            DialogBehaviour dialogBehaviour = playerData as DialogBehaviour;
            if (dialogBehaviour == null)
                return;

            _closeRequested = true;
            dialogBehaviour.CloseDialog();
        }
    }
}
#endif
