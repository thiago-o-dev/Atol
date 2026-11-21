using Assets._Project.Framework.Logging;
using UnityEngine;

namespace Assets._Project.Features.StatusViewModels
{
    // Barra acima da cabeça (ex: vida dos animais), sempre virada pra câmera. O visual é o WorldStatusBar.prefab.
    [DisallowMultipleComponent]
    public sealed class WorldStatusBarUI : MonoBehaviour
    {
        [Tooltip("Componente que implementa IStatusPool. Se vazio, usa o HealthComponent do objeto.")]
        public MonoBehaviour Source;
        public StatusBarUI BarPrefab;
        public Vector3 Offset = new Vector3(0f, 2f, 0f);
        [Tooltip("Aparece com a câmera mais perto que isso. Com a barra incompleta, até três vezes mais longe.")]
        public float ShowDistance = 12f;

        private IStatusPool _pool;
        private StatusBarUI _bar;

        private void Awake()
        {
            if (Source == null)
                Source = GetComponent<HealthComponent>();

            _pool = Source as IStatusPool;

            if (_pool == null || BarPrefab == null)
            {
                new FrameworkLogger(this).Error("Needs an IStatusPool source and a BarPrefab");
                enabled = false;
                return;
            }

            _bar = Instantiate(BarPrefab);
            _bar.Bind(_pool);
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;

            if (camera == null)
                return;

            float value = _pool.Normalized;
            float distance = Vector3.Distance(camera.transform.position, transform.position);
            float range = value < 0.999f ? ShowDistance * 3f : ShowDistance;

            _bar.gameObject.SetActive(value > 0f && distance < range);
            _bar.transform.SetPositionAndRotation(transform.position + Offset, camera.transform.rotation);
        }

        private void OnDisable()
        {
            if (_bar)
                _bar.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_bar)
                Destroy(_bar.gameObject);
        }
    }
}
