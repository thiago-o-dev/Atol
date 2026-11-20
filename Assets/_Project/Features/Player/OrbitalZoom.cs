using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class OrbitalZoom : MonoBehaviour
{
    public CinemachineOrbitalFollow OrbitalFollow;
    public float zoomSpeed = 10f;
    public float minRadius = 3f;
    public float maxRadius = 8f;
    public float minHeight = 3.5f*0.75f;
    public float maxHeight = 7f;


    void Update()
    {
        float scrollInput = Mouse.current.scroll.ReadValue().y * Time.unscaledDeltaTime;


        if (scrollInput != 0 && OrbitalFollow != null)
        {
            OrbitalFollow.Orbits.Top.Height = Mathf.Clamp(OrbitalFollow.Orbits.Top.Height - scrollInput * (zoomSpeed / (maxHeight-minHeight)), minHeight, maxHeight);
            OrbitalFollow.Orbits.Center.Radius = Mathf.Clamp(OrbitalFollow.Orbits.Center.Radius - scrollInput * (zoomSpeed / (maxRadius - minRadius)), minRadius, maxRadius);
        }
    }
}
