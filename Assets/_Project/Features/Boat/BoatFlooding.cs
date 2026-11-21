using Assets._Project.Features.StatusViewModels;
using System;
using UnityEngine;

namespace Assets._Project.Features.Boat
{
    // Água acumulada no barco (ex: tubarão preso no casco). Deixa o barco lento e, quando enche, vira ele
    // pro lado de onde a água está entrando. Depois o capsize do BoatController cuida de desvirar.
    // A barra aparece no HUD do player (PlayerHud.Flood).
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoatController))]
    public class BoatFlooding : MonoBehaviour, IStatusPool
    {
        public float Capacity = 100f;
        public float DrainRate = 3f;
        [Tooltip("Quanto sai por segundo com o player tirando água na mão.")]
        public float BailRate = 14f;
        [Tooltip("Freio aplicado no barco quando ele está cheio.")]
        public float MaxSlowdown = 1.5f;
        public float CapsizeTorque = 16f;
        public float CapsizeTimeout = 4f;

        public float Water { get; private set; }
        public float Normalized => Mathf.Clamp01(Water / Capacity);
        public bool IsCapsizing { get; private set; }

        public event Action Flooded;

        private BoatController _boat;
        private float _floodSide = 1f;
        private float _capsizeStartTime;
        private float _lastFillTime;

        private void Awake()
        {
            _boat = GetComponent<BoatController>();
        }

        public void AddWater(float amount, Vector3 fromPoint)
        {
            if (IsCapsizing || amount <= 0f)
                return;

            _floodSide = Mathf.Sign(transform.InverseTransformPoint(fromPoint).x);
            _lastFillTime = Time.time;

            Water = Mathf.Min(Water + amount, Capacity);

            if (Water >= Capacity)
            {
                IsCapsizing = true;
                _capsizeStartTime = Time.time;
                Flooded?.Invoke();
            }
        }

        public void Bail(float deltaTime)
        {
            if (!IsCapsizing)
                Water = Mathf.Max(0f, Water - BailRate * deltaTime);
        }

        private void FixedUpdate()
        {
            Rigidbody body = _boat.Body;

            if (body == null)
                return;

            if (IsCapsizing)
                UpdateCapsize(body);
            else if (Water > 0f && Time.time - _lastFillTime > 1f)
                Water = Mathf.Max(0f, Water - DrainRate * Time.fixedDeltaTime);

            if (Water > 0f)
            {
                Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
                body.AddForce(-planarVelocity * MaxSlowdown * Normalized, ForceMode.Acceleration);
            }
        }

        private void UpdateCapsize(Rigidbody body)
        {
            if (_boat.IsRecoveringFromCapsize || Time.time - _capsizeStartTime > CapsizeTimeout)
            {
                IsCapsizing = false;
                Water = 0f;
                return;
            }

            // Mesmo torque que um peso puxando a lateral pra baixo daria.
            Vector3 side = transform.right * _floodSide;
            body.AddTorque(Vector3.Cross(side, Vector3.down) * CapsizeTorque, ForceMode.Acceleration);
        }
    }
}
