using Assets._Project.Features.Boat;
using Assets._Project.Features.Combat;
using UnityEngine;

namespace Assets._Project.Features.WalkingDragon
{
    /// <summary>
    /// Runtime behavior for a WalkingDragon fireball.
    /// Direct player hits deal full damage. If the player is aboard a boat,
    /// a direct hull/player contact damages and launches the boat instead because
    /// the player character collider/controller is disabled while aboard.
    /// The projectile owns all explosion and boat-impact tuning.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SphereCollider))]
    public sealed class WalkingDragonFireballProjectile : MonoBehaviour
    {
        [Header("Travel")]
        [Min(0f)] public float Damage = 40f;
        [Min(0.1f)] public float Speed = 36f;
        [Min(0.1f)] public float Lifetime = 10f;

        [Header("Impact Explosion")]
        [Min(0f)] public float ExplosionRadius = 7f;
        [Tooltip("Area damage is only applied if the target was not directly hit by this fireball or protected by the directly-hit boat.")]
        [Min(0f)] public float ExplosionDamage = 20f;
        public GameObject ExplosionEffectPrefab;
        public LayerMask ExplosionMask = ~0;

        [Header("Direct Boat Impact")]
        [Tooltip("Flooding added when the fireball directly hits the player's boat. Increase this for a more devastating fireball. The value uses the project's BoatFlooding.AddWater amount units.")]
        [Min(0f)] public float BoatImpactFlood = 50f;
        [Tooltip("Minimum world-Y velocity reached by a dynamic boat on direct fireball impact. Only vertical velocity is changed; horizontal velocity and rotation are preserved.")]
        [Min(0f)] public float BoatImpactVerticalVelocity = 34f;
        [Tooltip("World-Y displacement applied instead when the boat Rigidbody is kinematic.")]
        [Min(0f)] public float BoatImpactKinematicDisplacement = 14f;

        private WalkingDragonController _owner;
        private PlayerController _target;
        private Rigidbody _body;
        private SphereCollider _sphere;
        private Vector3 _direction;
        private float _age;
        private bool _paused;
        private bool _initialized;
        private bool _hasHit;
        private bool _hasDamagedBoat;
        private readonly Collider[] _explosionHits = new Collider[64];

        private void Awake()
        {
            _sphere = GetComponent<SphereCollider>();
            _sphere.isTrigger = true;

            _body = GetComponent<Rigidbody>();
            if (_body == null)
                _body = gameObject.AddComponent<Rigidbody>();

            _body.useGravity = false;
            _body.isKinematic = false;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        public void Initialize(
            WalkingDragonController owner,
            PlayerController target,
            Vector3 direction,
            float speed,
            float damage,
            float lifetime)
        {
            _owner = owner;
            _target = target;
            _direction = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
            Speed = Mathf.Max(0.1f, speed);
            Damage = Mathf.Max(0f, damage);
            Lifetime = Mathf.Max(0.1f, lifetime);
            _age = 0f;
            _hasHit = false;
            _hasDamagedBoat = false;
            _initialized = true;

            if (_owner != null)
            {
                Collider[] ownerColliders = _owner.GetComponentsInChildren<Collider>();
                foreach (Collider ownerCollider in ownerColliders)
                {
                    if (ownerCollider != null)
                        Physics.IgnoreCollision(_sphere, ownerCollider, true);
                }
            }

            _body.linearVelocity = _paused ? Vector3.zero : _direction * Speed;
        }

        private void FixedUpdate()
        {
            if (!_initialized || _paused || _hasHit)
                return;

            _age += Time.fixedDeltaTime;
            if (_age >= Lifetime)
            {
                Destroy(gameObject);
                return;
            }

            _body.linearVelocity = _direction * Speed;
            if (_direction.sqrMagnitude > 0.001f)
                _body.MoveRotation(Quaternion.LookRotation(_direction, Vector3.up));
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (_body == null)
                return;

            if (paused)
            {
                _body.linearVelocity = Vector3.zero;
                _body.isKinematic = true;
                if (_sphere != null)
                    _sphere.enabled = false;
            }
            else
            {
                _body.isKinematic = false;
                if (_sphere != null)
                    _sphere.enabled = true;
                _body.linearVelocity = _initialized ? _direction * Speed : Vector3.zero;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_paused || _hasHit || other == null || IsOwnerOrWater(other))
                return;

            // A boat hull is the damage receiver when the player is aboard.
            // Check the collider hierarchy first, then the boat Rigidbody because
            // some boat prefabs place hull colliders below a separate body object.
            BoatController hitBoat = other.GetComponentInParent<BoatController>();
            if (hitBoat == null && _target != null && _target.IsAboardBoat)
            {
                BoatController targetBoat = GetPlayerBoat(_target);
                Rigidbody targetBoatBody = GetBoatBody(targetBoat);
                if (targetBoat != null && targetBoatBody != null && other.attachedRigidbody == targetBoatBody)
                    hitBoat = targetBoat;
            }

            if (hitBoat != null)
            {
                Vector3 boatImpactPoint = other.ClosestPoint(transform.position);
                ApplyDirectBoatImpact(hitBoat, boatImpactPoint);
                Explode(transform.position, null, hitBoat);
                return;
            }

            PlayerController hitPlayer = other.GetComponentInParent<PlayerController>();
            if (hitPlayer != null)
            {
                // Some player prefabs keep a collider enabled while aboard even
                // though their active character controller/Health receiver is
                // disabled. Route that hit to the boat rather than the character.
                if (hitPlayer == _target && hitPlayer.IsAboardBoat)
                {
                    BoatController playerBoat = GetPlayerBoat(hitPlayer);
                    if (playerBoat != null)
                    {
                        ApplyDirectBoatImpact(playerBoat, other.ClosestPoint(transform.position));
                        Explode(transform.position, null, playerBoat);
                        return;
                    }
                }

                bool directHitCanDamage = hitPlayer == _target &&
                    hitPlayer.Health != null &&
                    !hitPlayer.Health.IsDead;

                if (directHitCanDamage)
                    hitPlayer.Health.TakeDamage(Damage);

                // A player physically hit by the projectile is never also hit by
                // this projectile's explosion, avoiding duplicate direct + area damage.
                Explode(transform.position, hitPlayer, null);
                return;
            }

            // Ignore unrelated trigger volumes (water volumes, interaction zones,
            // detection triggers). Solid geometry triggers the impact explosion.
            if (!other.isTrigger)
                Explode(transform.position, null, null);
        }

        private bool IsOwnerOrWater(Collider other)
        {
            if (_owner != null && (other.transform == _owner.transform || other.transform.IsChildOf(_owner.transform)))
                return true;

            return other.GetComponentInParent<SpectralOceanFFT>() != null;
        }

        private static BoatController GetPlayerBoat(PlayerController player)
        {
            if (player == null)
                return null;

            if (player.CurrentBoat != null)
                return player.CurrentBoat;

            return player.GetComponentInParent<BoatController>();
        }

        private static Rigidbody GetBoatBody(BoatController boat)
        {
            if (boat == null)
                return null;

            if (boat.Body != null)
                return boat.Body;

            Rigidbody rootBody = boat.GetComponent<Rigidbody>();
            return rootBody != null ? rootBody : boat.GetComponentInChildren<Rigidbody>();
        }

        private void ApplyDirectBoatImpact(BoatController boat, Vector3 impactPoint)
        {
            if (_hasDamagedBoat || boat == null)
                return;

            _hasDamagedBoat = true;

            // BoatFlooding is the project's boat damage system. Create it on the
            // boat root if a prefab does not already have one, matching the ram path.
            if (BoatImpactFlood > 0f)
            {
                if (!boat.TryGetComponent(out BoatFlooding flooding))
                    flooding = boat.gameObject.AddComponent<BoatFlooding>();

                flooding.AddWater(BoatImpactFlood, impactPoint);
            }

            // Match the Leviathan ram response: world Y only. Do not disturb the
            // boat's horizontal steering/velocity or apply a torque impulse.
            Rigidbody boatBody = GetBoatBody(boat);
            if (boatBody != null)
            {
                boatBody.WakeUp();
                if (boatBody.isKinematic)
                {
                    Vector3 position = boatBody.position;
                    position.y += BoatImpactKinematicDisplacement;
                    boatBody.position = position;
                }
                else
                {
                    float upwardDelta = Mathf.Max(0f, BoatImpactVerticalVelocity - boatBody.linearVelocity.y);
                    if (upwardDelta > 0f)
                        boatBody.AddForce(Vector3.up * upwardDelta, ForceMode.VelocityChange);
                }
            }

            if (_owner != null && _owner.ShowLogs)
                Debug.Log($"WalkingDragon fireball heavily hit boat '{boat.name}'. Flood added: {BoatImpactFlood:0.0}; vertical velocity: {(boatBody != null && !boatBody.isKinematic ? boatBody.linearVelocity.y : 0f):0.0} m/s.", boat);
        }

        private void Explode(Vector3 position, PlayerController directlyHitPlayer, BoatController directlyHitBoat)
        {
            if (_hasHit)
                return;

            _hasHit = true;
            if (_sphere != null)
                _sphere.enabled = false;
            if (_body != null)
            {
                _body.linearVelocity = Vector3.zero;
                _body.isKinematic = true;
            }

            if (ExplosionEffectPrefab != null)
                Instantiate(ExplosionEffectPrefab, position, Quaternion.identity);

            TryApplyExplosionDamage(position, directlyHitPlayer, directlyHitBoat);
            Destroy(gameObject);
        }

        private void TryApplyExplosionDamage(
            Vector3 position,
            PlayerController directlyHitPlayer,
            BoatController directlyHitBoat)
        {
            if (_target == null || _target == directlyHitPlayer || ExplosionRadius <= 0f || ExplosionDamage <= 0f)
                return;

            // If the blast hit the hull the player is riding, boat damage is the
            // intended receiver. Do not also attempt character damage for this hit.
            if (directlyHitBoat != null && _target.IsAboardBoat && GetPlayerBoat(_target) == directlyHitBoat)
                return;

            if (_target.Health == null || _target.Health.IsDead)
                return;

            int hitCount = Physics.OverlapSphereNonAlloc(
                position,
                ExplosionRadius,
                _explosionHits,
                ExplosionMask,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < hitCount; i++)
            {
                Collider candidate = _explosionHits[i];
                if (candidate == null)
                    continue;

                PlayerController affectedPlayer = candidate.GetComponentInParent<PlayerController>();
                if (affectedPlayer != _target)
                    continue;

                // Apply once even if several player colliders overlap the blast.
                _target.Health.TakeDamage(ExplosionDamage);
                return;
            }
        }
    }
}
