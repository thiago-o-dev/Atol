using Assets._Project.Framework.Architecture;
using Assets._Project.Framework.Logging;
using UnityEditor.Overlays;
using UnityEngine;

public class WaterController : Singleton<WaterController>
{
    public bool ShowLogs = true;
    [Header("Gerstner Waves Variables")]

    [SerializeField] private GerstnerData[] _waveData;

    [SerializeField] private Material _material;

    public bool IsGamePaused = false;


    private FrameworkLogger _log;

    protected override void SingletonAwake()
    {
        _log = new(this, showLogs: ShowLogs, prefixColor: Color.darkGreen);
        _waveData = GetDataFromMaterial();

        _log.Log("Initialized water controller");
    }

    public GerstnerData[] GetDataFromMaterial()
    {
        if (_material != null)
        {
            GerstnerData[] data = {
                new(_material.GetFloat("Wavelength1"), _material.GetFloat("Speed1"), _material.GetFloat("Steepness1"),  _material.GetVector("Direction1")),
                new(_material.GetFloat("Wavelength2"), _material.GetFloat("Speed2"), _material.GetFloat("Steepness2"),  _material.GetVector("Direction2")),
                new(_material.GetFloat("Wavelength3"), _material.GetFloat("Speed3"), _material.GetFloat("Steepness3"),  _material.GetVector("Direction3")),
            };
            return data;
        }

        return _waveData;
    }

    public void SetData(GerstnerData data1, GerstnerData data2, GerstnerData data3)
    {
        if (_material != null)
        {
            GerstnerData[] data = { data1, data2, data3 };
            _waveData = data;

            _material.SetFloat("Wavelength1", data1.WaveLength);
            _material.SetFloat("Speed1", data1.Speed);
            _material.SetFloat("Steepness1", data1.Steepness);
            _material.SetVector("Direction1", data1.Direction);

            _material.SetFloat("Wavelength2", data2.WaveLength);
            _material.SetFloat("Speed2", data2.Speed);
            _material.SetFloat("Steepness2", data2.Steepness);
            _material.SetVector("Direction2", data2.Direction);

            _material.SetFloat("Wavelength3", data3.WaveLength);
            _material.SetFloat("Speed3", data3.Speed);
            _material.SetFloat("Steepness3", data3.Steepness);
            _material.SetVector("Direction3", data3.Direction);
        }
        else
        {
            _log.Error("SetDataInMaterial(): Material is NULL!");
        }
    }
    public float getHeightAtPosition(Vector3 position)
    {
        float time = Time.timeSinceLevelLoad;
        Vector3 currentPosition = GetWaveAddition(position, time);

        for (int i = 0; i < 3; i++)
        {
            Vector3 diff = new Vector3(position.x - currentPosition.x, 0, position.z - currentPosition.z);
            currentPosition = GetWaveAddition(diff, time);
        }

        return currentPosition.y;
    }
    public Vector3 GetWaveAddition(Vector3 position, float timeSinceStart)
    {
        Vector3 result = new Vector3();

        foreach (GerstnerData data in _waveData)
        {
            result += WaveFunctions.GerstnerWave(position, data.Direction, data.Steepness, data.WaveLength, data.Speed, timeSinceStart);
        }

        return result;
    }
}


