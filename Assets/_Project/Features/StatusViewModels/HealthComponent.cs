using System;
using UnityEngine;

namespace Assets._Project.Features.StatusViewModels
{
    [DisallowMultipleComponent]
    public sealed class HealthComponent : MonoBehaviour, IStatusPool
    {
        public float Maximum = 100f;

        public float Current { get; private set; }
        public float Normalized => Mathf.Clamp01(Current / Maximum);
        public bool IsDead => Current <= 0f;

        public event Action<float, float> Changed;
        public event Action<float> Damaged;
        public event Action Died;

        private void Awake()
        {
            Current = Maximum;
        }

        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f)
                return;

            SetCurrent(Current - amount);
            Damaged?.Invoke(amount);

            if (IsDead)
                Died?.Invoke();
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f)
                return;

            SetCurrent(Current + amount);
        }

        public void Fill()
        {
            SetCurrent(Maximum);
        }

        private void SetCurrent(float value)
        {
            Current = Mathf.Clamp(value, 0f, Maximum);
            Changed?.Invoke(Current, Maximum);
        }
    }
}
