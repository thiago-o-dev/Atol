using Unity.Cinemachine;
using UnityEngine;

// Vida da câmera aplicada depois do Cinemachine, sem mexer nas câmeras da cena:
// tremor, um tranco de zoom nos impactos e o campo de visão abrindo com a velocidade.
public static class CameraFeel
{
    // Velocidade de quem a câmera segue, em m/s. O PlayerController atualiza todo frame.
    public static float Speed;

    private const float FullSpeed = 8f;
    private const float SpeedFov = 9f;

    private static float _shakeStrength;
    private static float _shakeDuration;
    private static float _shakeUntil;
    private static float _punch;
    private static float _speedFov;
    private static float _slowUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        Speed = _shakeUntil = _punch = _speedFov = _slowUntil = 0f;

        CinemachineCore.CameraUpdatedEvent.RemoveListener(Apply);
        CinemachineCore.CameraUpdatedEvent.AddListener(Apply);
    }

    // Strength é o deslocamento máximo em metros. Um tremor mais fraco não interrompe um mais forte.
    public static void Shake(float strength, float duration = 0.3f)
    {
        if (Time.unscaledTime < _shakeUntil && strength < _shakeStrength)
            return;

        _shakeStrength = strength;
        _shakeDuration = duration;
        _shakeUntil = Time.unscaledTime + duration;
    }

    // Tranco no campo de visão, em graus. Negativo aproxima.
    public static void Punch(float degrees)
    {
        _punch = degrees;
    }

    // Câmera lenta por alguns instantes (duração em tempo real). Não mexe no tempo se o jogo estiver pausado.
    public static void SlowMotion(float timeScale, float duration)
    {
        if (Time.timeScale <= 0f)
            return;

        Time.timeScale = timeScale;
        _slowUntil = Time.unscaledTime + duration;
    }

    private static void Apply(CinemachineBrain brain)
    {
        if (_slowUntil > 0f && Time.unscaledTime >= _slowUntil)
        {
            _slowUntil = 0f;

            if (Time.timeScale > 0f)
                Time.timeScale = 1f;
        }

        Camera camera = brain.OutputCamera;

        if (camera == null || Time.timeScale <= 0f)
            return;

        float target = Mathf.Clamp01(Speed / FullSpeed) * SpeedFov;

        _speedFov = Mathf.Lerp(_speedFov, target, 1.5f * Time.deltaTime);
        _punch = Mathf.Lerp(_punch, 0f, 5f * Time.deltaTime);

        camera.fieldOfView += _speedFov + _punch;

        float left = _shakeUntil - Time.unscaledTime;

        if (left <= 0f)
            return;

        Vector2 offset = Random.insideUnitCircle * (_shakeStrength * left / _shakeDuration);
        camera.transform.position += camera.transform.rotation * offset;
    }
}
