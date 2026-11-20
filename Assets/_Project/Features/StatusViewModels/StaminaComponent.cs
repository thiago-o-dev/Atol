using System;
using UnityEngine;

namespace Assets._Project.Features.StatusViewModels
{
    /// <summary>
    /// Observable stamina pool (a status "view model": gameplay writes to it, UI and other
    /// systems read and subscribe). Spent in chunks (e.g. a swimming hop) and regenerates
    /// slowly after a short pause. Required by SwimmingMovement and PlayerController.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StaminaComponent : MonoBehaviour
    {
        [Header("Pool")]
        [SerializeField, Min(1f)] private float maximum = 100f;
        [Tooltip("Starting fraction of the pool (0..1).")]
        [SerializeField, Range(0f, 1f)] private float startingFraction = 1f;

        [Header("Regeneration")]
        [Tooltip("Stamina regained per second once regeneration has resumed. Kept low on purpose: stamina is a limited resource.")]
        [SerializeField, Min(0f)] private float regenPerSecond = 6f;
        [Tooltip("Seconds after spending before regeneration resumes.")]
        [SerializeField, Min(0f)] private float regenDelay = 1.5f;
        [Tooltip("Regeneration speed multiplier while the pool is empty (exhausted).")]
        [SerializeField, Range(0f, 1f)] private float exhaustedRegenMultiplier = 0.6f;

        [SerializeField] private float current;
        private float lastSpendTime = -1000f;
        private bool initialised;
        private bool wasEmpty;

        /// <summary>Raised when the value changes: (current, maximum).</summary>
        public event Action<float, float> Changed;
        /// <summary>Raised once when the pool reaches zero.</summary>
        public event Action Depleted;
        /// <summary>Raised when a TryConsume fails: (requested, available).</summary>
        public event Action<float, float> ConsumeFailed;

        public float Current => EnsureInitialised();
        public float Maximum => maximum;
        public float Normalized => maximum > 0f ? Mathf.Clamp01(Current / maximum) : 0f;
        public bool IsEmpty => Current <= 0.001f;
        public bool IsRegenerating => Time.time - lastSpendTime >= regenDelay && Current < maximum;

        /// <summary>Finds a StaminaComponent on this object or a parent, adding one to <paramref name="owner"/> if none exists.</summary>
        public static StaminaComponent Resolve(GameObject owner)
        {
            if (owner == null)
                return null;
            StaminaComponent found = owner.GetComponentInParent<StaminaComponent>();
            return found != null ? found : owner.AddComponent<StaminaComponent>();
        }

        private void Awake() => EnsureInitialised();

        private void OnValidate()
        {
            maximum = Mathf.Max(1f, maximum);
            regenPerSecond = Mathf.Max(0f, regenPerSecond);
            regenDelay = Mathf.Max(0f, regenDelay);
            current = Mathf.Clamp(current, 0f, maximum);
        }

        private float EnsureInitialised()
        {
            if (!initialised)
            {
                initialised = true;
                current = Mathf.Clamp(maximum * startingFraction, 0f, maximum);
                wasEmpty = current <= 0.001f;
            }
            return current;
        }

        private void Update()
        {
            EnsureInitialised();
            if (current >= maximum || Time.time - lastSpendTime < regenDelay)
                return;

            float rate = regenPerSecond * (current <= 0.001f ? exhaustedRegenMultiplier : 1f);
            if (rate > 0f)
                SetCurrent(current + rate * Time.deltaTime);
        }

        public bool CanAfford(float amount) => Current + 0.0001f >= Mathf.Max(0f, amount);

        /// <summary>Spends stamina if the full amount is available. Returns false (and spends nothing) otherwise.</summary>
        public bool TryConsume(float amount)
        {
            amount = Mathf.Max(0f, amount);
            if (!CanAfford(amount))
            {
                ConsumeFailed?.Invoke(amount, current);
                return false;
            }
            lastSpendTime = Time.time;
            SetCurrent(current - amount);
            return true;
        }

        /// <summary>Drains up to <paramref name="amount"/> (e.g. a shark attack or drowning effect).</summary>
        public void Drain(float amount)
        {
            lastSpendTime = Time.time;
            SetCurrent(Current - Mathf.Max(0f, amount));
        }

        public void Restore(float amount) => SetCurrent(Current + Mathf.Max(0f, amount));
        public void Fill() => SetCurrent(maximum);

        private void SetCurrent(float value)
        {
            value = Mathf.Clamp(value, 0f, maximum);
            if (Mathf.Approximately(value, current))
                return;

            current = value;
            try { Changed?.Invoke(current, maximum); }
            catch (Exception e) { Debug.LogException(e, this); }

            bool empty = current <= 0.001f;
            if (empty && !wasEmpty)
            {
                try { Depleted?.Invoke(); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
            wasEmpty = empty;
        }
    }
}
