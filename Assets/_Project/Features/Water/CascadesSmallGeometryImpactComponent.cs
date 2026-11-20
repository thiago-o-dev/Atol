using System.Collections;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(SpectralOceanFFT))]
public class CascadesSmallGeometryImpactComponent : MonoBehaviour
{
    private float[] originalCascadeScales;
    public float[] WeakCascadeScales = { 0.005f, 0.5f, 2f };

    private void Awake()
    {
        originalCascadeScales = GetComponent<SpectralOceanFFT>().GetCascadeAmplitudeScales();
    }

    public void WeakenCascades()
    {
        StartCoroutine(WeakenCascadesRoutine());
    }
    private IEnumerator WeakenCascadesRoutine()
    {
        yield return null;

        if (!originalCascadeScales.Any())
        {
            originalCascadeScales = GetComponent<SpectralOceanFFT>().GetCascadeAmplitudeScales();
        }

        GetComponent<SpectralOceanFFT>().SetCascadeAmplitudeScales(WeakCascadeScales);
    }

    public void ReturnCascades()
    {
        GetComponent<SpectralOceanFFT>().SetCascadeAmplitudeScales(originalCascadeScales);
    }
}
