using Assets._Project.Features.StatusViewModels;
using System.Collections.Generic;
using UnityEngine;

namespace Assets._Project.Features.Combat
{
    // Tira vida de quem tiver HealthComponent e encostar no trigger.
    // Serve pra perigos do mapa e como hitbox de ataque (ex: mordida do tubarão).
    [RequireComponent(typeof(Collider))]
    public class DamageOnTouch : MonoBehaviour
    {
        public float Damage = 10f;
        [Tooltip("Tempo entre acertos enquanto o alvo continua dentro. 0 = só ao entrar.")]
        public float RepeatInterval = 1f;
        public LayerMask TargetLayers = ~0;

        private readonly Dictionary<HealthComponent, float> _nextHitTime = new();

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnDisable()
        {
            _nextHitTime.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            TryDamage(other);
        }

        private void OnTriggerStay(Collider other)
        {
            if (RepeatInterval > 0f)
                TryDamage(other);
        }

        private void OnTriggerExit(Collider other)
        {
            HealthComponent health = other.GetComponentInParent<HealthComponent>();

            if (health)
                _nextHitTime.Remove(health);
        }

        private void TryDamage(Collider other)
        {
            if ((TargetLayers.value & (1 << other.gameObject.layer)) == 0)
                return;

            HealthComponent health = other.GetComponentInParent<HealthComponent>();

            if (health == null || health.IsDead)
                return;

            // Não acerta o próprio dono da hitbox.
            if (transform.IsChildOf(health.transform))
                return;

            if (_nextHitTime.TryGetValue(health, out float nextHit) && Time.time < nextHit)
                return;

            health.TakeDamage(Damage);

            _nextHitTime[health] = RepeatInterval > 0f ? Time.time + RepeatInterval : float.PositiveInfinity;
        }
    }
}
