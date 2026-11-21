using UnityEngine;

// Balança os filhos com o vento do oceano, cada um num compasso. Feito pra capim, flores e arbustos.
public sealed class WindSway : MonoBehaviour
{
    [Tooltip("Inclinação máxima com o vento no máximo, em graus.")]
    public float MaxAngle = 7f;
    public float Speed = 1.7f;

    private SpectralOceanFFT _ocean;
    private Transform[] _plants;
    private Quaternion[] _rest;

    private void Start()
    {
        _ocean = FindAnyObjectByType<SpectralOceanFFT>();
        _plants = new Transform[transform.childCount];
        _rest = new Quaternion[_plants.Length];

        for (int i = 0; i < _plants.Length; i++)
        {
            _plants[i] = transform.GetChild(i);
            _rest[i] = _plants[i].rotation;
        }
    }

    private void Update()
    {
        if (_ocean == null)
            return;

        Vector2 wind = _ocean.CurrentWindDirectionXZ;
        Vector3 axis = Vector3.Cross(Vector3.up, new Vector3(wind.x, 0f, wind.y));
        float strength = MaxAngle * _ocean.CurrentWindStrength;

        for (int i = 0; i < _plants.Length; i++)
        {
            // Inclina a favor do vento e oscila em torno disso.
            float angle = strength * (0.5f + 0.5f * Mathf.Sin(Time.time * Speed + i * 1.37f));
            _plants[i].rotation = Quaternion.AngleAxis(angle, axis) * _rest[i];
        }
    }
}
