using Assets._Project.Framework.Architecture;
using System;
using UnityEngine;

public class WorldStateController : Singleton<WorldStateController>
{
    public const int DecayMaximum = 3;
    [Range(0, DecayMaximum)] public int Decay = 0;
    public SpectralOceanFFT SpectralOcean;
    
    public void UpdateDecay(int value)
    {
        Decay = Mathf.Clamp(value, 0, DecayMaximum);

        // Sempre tem um pouco de vento, senão o barco não sai do lugar com Decay 0.
        SpectralOcean.SetWindStrength(Mathf.Lerp(0.35f, 1f, Decay / (float)DecayMaximum));
    }

    private void OnValidate()
    {
        UpdateDecay(Decay);
    }

    // Update is called once per frame
    void Start()
    {
        UpdateDecay(Decay);
    }
}
