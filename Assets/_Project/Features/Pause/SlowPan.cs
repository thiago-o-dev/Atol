using UnityEngine;

namespace Assets._Project.Features.Pause
{
    // Gira devagar em torno do eixo vertical. Dá vida à câmera parada do menu inicial.
    public sealed class SlowPan : MonoBehaviour
    {
        public float DegreesPerSecond = 2.5f;

        private void Update()
        {
            transform.Rotate(Vector3.up, DegreesPerSecond * Time.unscaledDeltaTime, Space.World);
        }
    }
}
