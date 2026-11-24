using Assets._Project.Framework.Architecture;
using System;
using UnityEngine;
using UnityEngine.Events;

public class WorldStateController : Singleton<WorldStateController>
{
    public const int DecayMaximum = 3;
    [Range(0, DecayMaximum)] public int Decay = 0;
    public SpectralOceanFFT SpectralOcean;

    private int latestDecay = 0;

    public UnityEvent OnGameOver;
    
    public void UpdateDecay(int value)
    {
        Decay = Mathf.Clamp(value, 0, DecayMaximum);
        latestDecay = Decay;

        // É O OBJETIVO O BARCO NÃO SAIR DO LUGAR COM 0 DECAYYYY
        SpectralOcean.SetWindStrength(Mathf.Lerp(0f, 1f, Decay / (float)DecayMaximum));

        if (latestDecay != 0 && value == 0)
        {
            OnGameOver.Invoke();
        }
    }
    public void AddDecay(int value)
    {
        UpdateDecay(Decay + value);
    }

    private void OnValidate()
    {
        UpdateDecay(Decay);
    }

    // Update is called once per frame
    void Start()
    {
        UpdateDecay(Decay);

        InvokeRepeating(nameof(CheckDecay), 5, 1.0f);
    }

    private void CheckDecay()
    {
        if (latestDecay !=  Decay)
        {
            UpdateDecay(Decay);
        }
    }
}
