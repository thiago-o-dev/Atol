using Assets._Project.Framework.Logging;
using System;
using System.Collections;
using Unity.Cinemachine;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;

public class CutsceneComponent : MonoBehaviour
{
    public CinemachineCamera Camera;
    public PlayableDirector Timeline;
    public AnimationClip Clip;

    public bool PlayOnAwake = false;
    [Tooltip("When this cutscene is entered as a quest step, play it directly to its final frame and complete it.")]
    public bool SkipCutscene = false;

    [RenamedFrom("OnAnimationStarted")]
    public UnityEvent OnCutsceneStarted = new UnityEvent();
    [RenamedFrom("OnTimelineCutsceneCompleted")]
    public UnityEvent OnCutsceneEnded = new UnityEvent();

    private FrameworkLogger _logInstance;
    private FrameworkLogger _log => _logInstance ??= new(this);
    private Coroutine _clipRoutine;
    private bool _isPlaying;

    /// <summary>Raised when this cutscene reaches its completed state.</summary>
    public event Action Completed;

    public bool IsPlaying => _isPlaying;

    // Preserve the existing inspector field while exposing the common quest skip contract.
    public bool SkipOnBegin => SkipCutscene;

    private void Awake()
    {
        if (PlayOnAwake)
            StartCutscene();
    }

    /// <summary>Quest-step entry point. Also usable directly from a UnityEvent.</summary>
    public void Begin()
    {
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        StartCutscene();
    }

    public void StartCutscene()
    {
        if (_isPlaying)
            return;

        _isPlaying = true;
        _log.Log("Cutscene started.");

        if (Camera != null)
            Camera.gameObject.SetActive(true);

        OnCutsceneStarted?.Invoke();

        // A UnityEvent listener may have ended or cancelled the cutscene.
        if (!_isPlaying)
            return;

        if (SkipCutscene)
        {
            Skip();
            return;
        }

        if (Timeline != null)
        {
            Timeline.stopped -= OnTimelineStopped;
            Timeline.stopped += OnTimelineStopped;
            Timeline.Play();
            return;
        }

        if (Clip != null && TryGetComponent<Animator>(out Animator animator))
        {
            animator.CrossFade(Clip.name, 0.1f);
            _clipRoutine = StartCoroutine(WaitForClipEnd(Clip.length));
            return;
        }

        if (Clip != null)
            _log.Error("An AnimationClip is assigned, but no Animator component was found.");

        // A cutscene without a Timeline or playable AnimationClip has no
        // asynchronous work to wait for, so finish instead of stalling a quest.
        EndCutscene();
    }

    /// <summary>
    /// Fast-forwards the active cutscene to its final evaluated frame and completes it.
    /// This is also called automatically by QuestComponent when SkipCutscene is enabled.
    /// </summary>
    public void Skip()
    {
        if (!_isPlaying)
            return;

        if (Timeline != null)
        {
            Timeline.stopped -= OnTimelineStopped;
            Timeline.stopped += OnTimelineStopped;

            if (Timeline.duration > 0.1)
            {
                Timeline.time = Timeline.duration - 0.1;
            }
            Timeline.Evaluate();
            
            Timeline.Resume();

            return;
        }
        else if (Clip != null)
        {
            if (TryGetComponent<Animator>(out Animator animator))
            {
                // CrossFade uses the clip name as the state name in the existing
                // implementation; Play at normalized time 1 evaluates its end pose.
                animator.Play(Clip.name, 0, 1f);
                animator.Update(0f);
            }
            else
            {
                _log.Error("Cannot skip the AnimationClip because no Animator component was found.");
            }
        }

        EndCutscene();
    }

    private IEnumerator WaitForClipEnd(float duration)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, duration));
        _clipRoutine = null;
        EndCutscene();
    }

    private void OnTimelineStopped(PlayableDirector director)
    {
        EndCutscene();
    }

    /// <summary>Ends the cutscene and notifies both UnityEvents and quest listeners.</summary>
    public void EndCutscene()
    {
        if (!_isPlaying)
            return;

        _isPlaying = false;

        if (Timeline != null)
        {
            Timeline.stopped -= OnTimelineStopped;
            Timeline.Stop();
        }

        if (_clipRoutine != null)
        {
            StopCoroutine(_clipRoutine);
            _clipRoutine = null;
        }

        _log.Log("Cutscene ended.");

        // Camera visibility remains controlled by OnCutsceneEnded, as in the
        // original feature, so existing scene setups keep their behavior.
        OnCutsceneEnded?.Invoke();
        Completed?.Invoke();
    }

    /// <summary>Stops playback without marking the quest step as completed.</summary>
    public void Cancel()
    {
        if (!_isPlaying)
            return;

        _isPlaying = false;

        if (Timeline != null)
        {
            Timeline.stopped -= OnTimelineStopped;
            Timeline.Stop();
        }

        if (_clipRoutine != null)
        {
            StopCoroutine(_clipRoutine);
            _clipRoutine = null;
        }
    }

    private void OnDisable()
    {
        if (_isPlaying)
            Cancel();
    }

    private void OnDestroy()
    {
        if (Timeline != null)
            Timeline.stopped -= OnTimelineStopped;
    }
}
