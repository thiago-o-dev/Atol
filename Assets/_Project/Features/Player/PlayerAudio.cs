using Assets._Project.Features.Audio;
using Assets._Project.Features.Movement;
using UnityEngine;

// Sons do corpo do player: passos em terra, braçadas na água e a água sendo tirada do barco.
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
public sealed class PlayerAudio : MonoBehaviour
{
    public AudioClip[] Footsteps;
    [Tooltip("Metros entre um passo e outro.")]
    public float StepDistance = 1.9f;
    public AudioClip[] SwimStrokes;
    public float StrokeDistance = 2.6f;
    public AudioClip BailSound;
    public float BailInterval = 0.7f;
    [Range(0f, 1f)] public float Volume = 0.5f;

    private PlayerController _player;
    private Vector3 _lastPosition;
    private float _travelled;
    private float _nextBailTime;

    private void Awake()
    {
        _player = GetComponent<PlayerController>();
        _lastPosition = transform.position;
    }

    private void Update()
    {
        Vector3 step = transform.position - _lastPosition;
        step.y = 0f;
        _lastPosition = transform.position;

        if (_player.IsBailing && Time.time >= _nextBailTime)
        {
            _nextBailTime = Time.time + BailInterval;
            Sound.Play(BailSound, transform.position, Volume);
        }

        bool swimming = _player.ActiveMovement is SwimmingMovement;
        bool walking = _player.ActiveMovement is GroundedMovement && _player.CharacterController.isGrounded;

        // Um teleporte (renascer, embarcar) não conta como caminhada.
        if (_player.IsAboardBoat || !(swimming || walking) || step.magnitude > 1f)
        {
            _travelled = 0f;
            return;
        }

        _travelled += step.magnitude;

        if (_travelled < (swimming ? StrokeDistance : StepDistance))
            return;

        _travelled = 0f;

        AudioClip[] clips = swimming ? SwimStrokes : Footsteps;

        if (clips.Length > 0)
            Sound.Play(clips[Random.Range(0, clips.Length)], transform.position, Volume);
    }
}
