using Assets._Project.Features.StatusViewModels;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Assets._Project.Features.Combat
{
    // Golpe do player: o clique esquerdo acerta quem tiver vida ao alcance, do lado pra onde a mira aponta.
    // Gasta stamina só quando acerta.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerStrike : MonoBehaviour
    {
        public float Damage = 15f;
        public float StaminaCost = 15f;
        public float Range = 3.5f;
        [Range(0f, 360f)] public float Arc = 140f;
        public float Cooldown = 0.5f;
        public float HitShake = 0.12f;

        // Quem o golpe acertaria agora. A mira do HUD acende com isso.
        public HealthComponent Target { get; private set; }

        public event System.Action Struck;

        private readonly Collider[] _hits = new Collider[16];
        private PlayerController _player;
        private float _nextStrikeTime;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
        }

        private void Update()
        {
            Target = _player.InputBlocked || Mouse.current == null ? null : FindTarget();

            if (Target == null || !Mouse.current.leftButton.wasPressedThisFrame || Time.time < _nextStrikeTime)
                return;

            if (!_player.Stamina.TryConsume(StaminaCost))
                return;

            _nextStrikeTime = Time.time + Cooldown;
            Target.TakeDamage(Damage);
            Struck?.Invoke();

            CameraFeel.Shake(HitShake, 0.15f);
        }

        private HealthComponent FindTarget()
        {
            Vector3 origin = transform.position + Vector3.up;
            Vector3 aim = AimDirection(origin);

            HealthComponent best = null;
            float bestDistance = float.PositiveInfinity;
            int count = Physics.OverlapSphereNonAlloc(origin, Range, _hits, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                HealthComponent health = _hits[i].GetComponentInParent<HealthComponent>();

                if (health == null || health.IsDead || health == _player.Health)
                    continue;

                Vector3 toTarget = _hits[i].ClosestPoint(origin) - origin;
                float distance = toTarget.magnitude;
                toTarget.y = 0f;

                if (distance >= bestDistance || Vector3.Angle(aim, toTarget) > Arc * 0.5f)
                    continue;

                best = health;
                bestDistance = distance;
            }

            return best;
        }

        // Direção do player até o ponto do chão embaixo do ponteiro.
        private Vector3 AimDirection(Vector3 origin)
        {
            Camera camera = Camera.main;

            if (camera == null)
                return transform.forward;

            Ray ray = camera.ScreenPointToRay(Mouse.current.position.ReadValue());
            Vector3 direction = new Plane(Vector3.up, origin).Raycast(ray, out float distance)
                ? ray.GetPoint(distance) - origin
                : camera.transform.forward;

            direction.y = 0f;
            return direction;
        }
    }
}
