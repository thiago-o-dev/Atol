using Assets._Project.Framework.Architecture;
using System;
using UnityEngine;

public class WorldStateController : Singleton<WorldStateController>
{
    public const int DecayMaximum = 3;
    [Range(0, DecayMaximum)] public int Decay = 0;
    public SpectralOceanFFT SpectralOcean;

    private int latestDecay = 0;
    
    public void UpdateDecay(int value)
    {
        Decay = Mathf.Clamp(value, 0, DecayMaximum);
        latestDecay = Decay;

        // É O OBJETIVO O BARCO NÃO SAIR DO LUGAR COM 0 DECAYYYY
        SpectralOcean.SetWindStrength(Mathf.Lerp(0f, 1f, Decay / (float)DecayMaximum));
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
