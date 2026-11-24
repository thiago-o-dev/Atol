using Assets._Project.Features.Boat;
using Assets._Project.Features.Combat;
using Assets._Project.Features.Shark;
using System.Collections.Generic;
using UnityEngine;

namespace Assets._Project.Features.WalkingDragon
{
    /// <summary>
    /// Colossal, unkillable, water-bound boss. It persistently tracks and circles
    /// the player, fires projectiles regardless of whether the player is on land,
    /// swimming, or aboard a boat, and performs a roar-jump-charge ram against boats.
    /// The boat ram changes only the boat Rigidbody's
    /// vertical velocity; its horizontal velocity and rotation are left untouched.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SwimmingMovement))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class WalkingDragonController : MonoBehaviour
    {
        public enum State
        {
            Patrol,
            OrbitLandTarget,
            OrbitBoat,
            OrbitSwimmingTarget,
            RamRoar,
            RamJump,
            RamBoat
        }

        [Header("Player Tracking")]
        [Tooltip("Assign the player, or leave empty to auto-find a PlayerController. The boss does not use a detection or give-up radius.")]
        public PlayerController Target;
        [Tooltip("How often to retry finding the player if Target is not assigned.")]
        [Min(0.1f)] public float TargetSearchInterval = 1f;

        [Header("Colossal Boss Scale")]
        [Tooltip("Optional model-only transform. If empty, the dragon root is scaled. Set this to the model child if your root has separate movement/collider objects.")]
        public Transform VisualRoot;
        [Tooltip("Applied once at runtime to make this a boss-sized creature. Reduce this if your imported model is already large.")]
        [Min(0.01f)] public float BossVisualScaleMultiplier = 8f;
        [Tooltip("World-space radius used for obstacle checks. Keep this close to the dragon's actual half-width.")]
        [Min(1f)] public float ObstacleRadius = 16f;
        [Tooltip("World-space radius of the water-depth safety footprint around the dragon. The whole footprint must be over deep water.")]
        [Min(1f)] public float WaterSafetyRadius = 15f;
        [Range(4, 16)] public int WaterFootprintProbeCount = 8;
        [Tooltip("Vertical offset of the obstacle sweep from the dragon root.")]
        public float ObstacleCastHeight = 5f;

        [Header("Colossal Turning")]
        [Tooltip("SmoothDamp time used to turn the massive body toward its target. Higher values feel heavier and less reactive.")]
        [Min(0.1f)] public float TurnSmoothTime = 3.2f;
        [Tooltip("Hard maximum yaw turn rate in degrees per second. Keep this low for a colossal creature.")]
        [Min(1f)] public float TurnMaxDegreesPerSecond = 9f;

        [Header("Orbit / Presence")]
        [Tooltip("The dragon keeps this approximate distance from the player or their boat while circling.")]
        [Min(5f)] public float OrbitRadius = 75f;
        [Tooltip("Approximate tangential movement speed while circling.")]
        [Min(0f)] public float OrbitSpeed = 10f;
        [Tooltip("Movement speed used to regain the orbit if the player travels away from the current orbit point.")]
        [Min(0f)] public float OrbitApproachSpeed = 17f;
        [Tooltip("How quickly the orbit point revolves around the player, in degrees per second.")]
        [Min(0.1f)] public float OrbitAngularSpeed = 8f;
        [Tooltip("Recalculate a safe orbit point this often. A cached point avoids excessive terrain raycasts.")]
        [Min(0.05f)] public float OrbitRepathInterval = 0.35f;
        [Tooltip("If the dragon is farther than this from its orbit point, it uses OrbitApproachSpeed.")]
        [Min(1f)] public float OrbitRepositionDistance = 22f;
        [Tooltip("Additional radius tried when the preferred orbit ring is too close to an island or shallow water.")]
        [Min(1f)] public float OrbitRadiusStep = 18f;
        [Range(1, 5)] public int OrbitRadiusAttempts = 3;
        [Range(8, 48)] public int OrbitAngleSamples = 24;
        [Tooltip("Speed used when it has no player target to follow.")]
        [Min(0f)] public float PatrolSpeed = 8f;
        [Min(1f)] public float PatrolRadius = 180f;
        [Min(1f)] public float PatrolPointTime = 24f;
        [Min(1f)] public float ArriveDistance = 8f;

        [Header("Fireball Attack - All Player States")]
        [Tooltip("The projectile prefab must have a SphereCollider. A Rigidbody and projectile behaviour are added/configured at runtime if absent.")]
        public GameObject FireballPrefab;
        [Tooltip("Optional mouth transform. If empty, the projectile spawns well in front of and above the dragon root.")]
        public Transform FireballMuzzle;
        [Min(0f)] public float FireballRange = 125f;
        [Min(0f)] public float FireballCooldown = 5f;
        [Min(0f)] public float FireballWindup = 1.5f;
        [Min(0f)] public float FireballSpeed = 36f;
        [Min(0f)] public float FireballDamage = 40f;
        [Min(0f)] public float FireballAimHeight = 1.1f;
        [Min(0.1f)] public float FireballLifetime = 10f;
        [Tooltip("When enabled, a blocked shot waits for a clear line. Disabled by default so the boss keeps pressuring players even when terrain or a boat briefly obstructs the ray.")]
        public bool RequireLineOfSight;
        [Tooltip("Maximum horizontal yaw difference accepted for firing. This is intentionally generous because the colossal boss turns slowly. The wind-up remains active while it turns into this firing arc.")]
        [Range(1f, 180f)] public float FireballFacingTolerance = 75f;
        public LayerMask SightMask = ~0;
        [Tooltip("Fallback muzzle offset in world metres when no FireballMuzzle is assigned.")]
        public Vector3 FallbackMuzzleOffset = new Vector3(0f, 8f, 12f);
        [Tooltip("When searching for a safe orbit point, prefer nearby positions with a clear shot. The dragon still moves and circles when no shot is available.")]
        public bool PreferClearShotOrbitPositions = true;
        [Range(4, 16)] public int OrbitShotAngleSamples = 8;
        [Tooltip("How often multi-point line of sight is refreshed. Lower values react faster but use more physics queries.")]
        [Min(0.05f)] public float SightCheckInterval = 0.2f;
        [Tooltip("How far each visibility ray extends past its sampled point, in metres.")]
        [Min(0f)] public float SightProbePadding = 0.05f;


        [Header("Boat Attack - Colossal Ram")]
        [Tooltip("Time spent circling before the first ram, or between re-entering a boat encounter and its first ram.")]
        [Min(0f)] public float OrbitBeforeRam = 8f;
        [Min(0f)] public float RamCooldown = 20f;
        [Tooltip("How long the boss roars before leaping into its boat ram.")]
        [Min(0f)] public float RamWindup = 2f;
        [Tooltip("Duration of the forward leap. During this phase the boss bypasses normal island steering so it can clear an island between itself and the boat.")]
        [Min(0.1f)] public float RamJumpDuration = 3.5f;
        [Tooltip("Peak vertical height of the ram leap above the seafloor. The boss returns to the seabed target after the leap.")]
        [Min(0f)] public float RamJumpHeight = 24f;
        [Min(0f)] public float RamSpeed = 25f;
        [Tooltip("Maximum time spent on the final charge after the roar-and-jump sequence has completed.")]
        [Min(0.1f)] public float RamTimeout = 12f;
        [Tooltip("Unscaled local-direction offset in metres from the dragon root to the snout contact point.")]
        public Vector3 RamLocalOffset = new Vector3(0f, 8f, 24f);
        [Min(0.1f)] public float RamHitDistance = 16f;
        [Tooltip("Horizontal speed applied to the player when they are knocked off. The existing boat-exit jump supplies the vertical hop.")]
        [Min(0f)] public float PlayerKnockbackSpeed = 13f;
        [Tooltip("Minimum upward velocity reached by a dynamic boat on impact. A pure Y velocity-change is applied; X/Z velocity and rotation are preserved.")]
        [Min(0f)] public float BoatLaunchVerticalVelocity = 28f;
        [Tooltip("If a boat Rigidbody is kinematic, move it upward by this distance on impact. Only world Y is changed.")]
        [Min(0f)] public float KinematicBoatLaunchDisplacement = 12f;
        [Tooltip("Extra approach/contact margin based on the dragon's large body, in addition to RamHitDistance.")]
        [Min(0f)] public float RamBodyContactBonus = 6f;
        [Tooltip("Optional flooding added on impact. Zero disables flooding so the ram does not sink the boat as a side effect.")]
        [Min(0f)] public float BoatRamFlood = 0f;

        [Header("Successful Ram - Shark Reinforcement")]
        [Tooltip("Assign the project's existing Shark prefab. A shark is spawned only when the ram reaches the boat while the target player is still aboard it.")]
        public SharkController SharkPrefab;
        [Tooltip("Enable spawning one shark after each successful player hit from a boat ram.")]
        public bool SpawnSharkAfterSuccessfulRam = true;
        [Tooltip("Spawn offset in the rammed boat's local space. Negative Z places the shark behind the boat; negative Y starts it below the surface.")]
        public Vector3 SharkSpawnLocalOffset = new Vector3(0f, -2f, -14f);

        [Header("Seafloor Contact / Island Avoidance")]
        [Tooltip("Optional static helper transform placed at the bottom of the dragon's feet. The controller keeps this point on the seabed. Do not assign an animated foot bone. If empty, the lowest enabled non-trigger collider bound is used.")]
        public Transform GroundContactReference;
        [Tooltip("Small offset above the seabed. Set to 0 for feet that should touch the ground exactly; a tiny positive value avoids collider jitter.")]
        [Min(0f)] public float GroundContactClearance = 0.05f;
        [Tooltip("Strength of the vertical position controller that keeps the dragon's feet aligned to the seabed.")]
        [Min(0.1f)] public float GroundFollowStrength = 8f;
        [Tooltip("Maximum vertical correction speed while following an uneven seabed or settling onto the ground.")]
        [Min(0.1f)] public float MaxGroundFollowSpeed = 20f;
        [Tooltip("Fixed seabed Y used when a ground ray finds no terrain/seabed collider. Set this to your deep-water ground map Y (usually negative).")]
        public float GroundMapY = -60f;
        [Tooltip("Mean water level is used ONLY to validate water depth for navigation. It is never used to move the dragon vertically.")]
        public float FallbackWaterLevel = 0f;
        [Tooltip("Water must be at least this deep above the nearest terrain/ground hit for the point to be navigable.")]
        [Min(0f)] public float MinimumWaterDepth = 14f;
        [Min(1f)] public float GroundProbeHeight = 300f;
        [Min(1f)] public float GroundProbeDepth = 800f;
        public LayerMask GroundMask = ~0;
        public LayerMask ObstacleMask = ~0;
        [Min(0.25f)] public float PathStep = 5f;
        [Min(0.1f)] public float LookAhead = 45f;
        [Min(0f)] public float SteerInterval = 0.2f;

        [Header("Attack Controls / Cutscenes")]
        [Tooltip("When enabled, the dragon continues to track and circle the player, but cannot start or complete attacks. Active fireballs are paused too.")]
        public bool AttacksFrozen;
        [Tooltip("Also freeze attacks while the project's CutsceneComponent reports an active cutscene.")]
        public bool FreezeAttacksDuringCutscenes;

        // Animation and audio are deliberately handled by WalkingDragonAnimatableComponent.
        // The controller only announces gameplay events, following the BearController pattern.

        [Header("Debug")]
        public bool ShowLogs;
        public bool DrawGizmos = true;

        public State CurrentState { get; private set; } = State.Patrol;
        public bool AreAttacksFrozen => _attacksFrozenNow;
        public Vector3 HomePosition => _home;
        public bool IsRammingBoat => _ramInProgress;
        public bool IsRamJumping => CurrentState == State.RamJump;
        public bool IsRamCharging => CurrentState == State.RamBoat;
        public bool IsFireballWindingUp => _fireballWindingUp;

        /// <summary>Raised when the boss begins its roar telegraph.</summary>
        public event System.Action RoarStarted;
        /// <summary>Raised when the boss begins the leap portion of its boat ram.</summary>
        public event System.Action RamJumpStarted;
        /// <summary>Raised when the leap ends and the ground-level charge begins.</summary>
        public event System.Action RamChargeStarted;
        /// <summary>Raised when a fireball projectile is actually released.</summary>
        public event System.Action<Vector3> FireballLaunched;
        /// <summary>Raised when a fireball wind-up begins, so the correct throw animation can play.</summary>
        public event System.Action FireballWindupStarted;
        /// <summary>Raised only after the ram physically reaches the boat.</summary>
        public event System.Action<Vector3> RamImpact;

        private SwimmingMovement _swimming;
        private Rigidbody _body;
        private SpectralOceanFFT _ocean;
        private Vector3 _home;
        private Vector3 _patrolPoint;
        private float _nextPatrolPointTime;
        private float _nextTargetSearchTime;
        private float _nextFireballTime;
        private float _fireballWindupEnd;
        private bool _fireballWindingUp;
        private bool _animationDrivenFireballReleaseAvailable;
        private float _nextSteerTime;
        private float _steerAngle;
        private float _steerSign = 1f;
        private bool _attacksFrozenNow;
        private float _orbitAngle;
        private float _orbitElapsed;
        private bool _orbitInitialized;
        private Vector3 _lastOrbitCenter;
        private float _nextOrbitPointTime;
        private Vector3 _cachedOrbitPoint;
        private bool _hasCachedOrbitPoint;
        private float _nextBoatRamTime;
        private bool _ramInProgress;
        private BoatController _ramBoat;
        private enum RamPhase { None, Roar, Jump, Charge }
        private RamPhase _ramPhase;
        private float _ramPhaseStartTime;
        private float _ramChargeStartTime;
        private float _ramJumpBaseBodyY;
        private float _ramJumpOffset;
        private float _nextBoatReferenceWarningTime;
        private BoatController _collisionIgnoredBoat;
        private readonly List<CollisionIgnorePair> _ignoredBoatCollisionPairs = new List<CollisionIgnorePair>(32);
        private Vector3 _initialVisualScale;
        private Transform _scaleTarget;
        private readonly RaycastHit[] _groundHits = new RaycastHit[64];
        private readonly RaycastHit[] _obstacleHits = new RaycastHit[64];
        private readonly List<WalkingDragonFireballProjectile> _projectiles = new List<WalkingDragonFireballProjectile>(12);
        private PlayerController _sightTarget;
        private Collider[] _targetSightColliders = new Collider[0];
        private readonly RaycastHit[] _sightHits = new RaycastHit[64];
        private readonly Vector3[] _sightSamplePoints = new Vector3[8];
        private float _nextSightCheckTime;
        private bool _cachedSightResult;
        private bool _hasCachedSightResult;
        private float _smoothedYaw;
        private float _yawVelocity;
        private bool _turnInitialized;
        // Cached vertical offset from the Rigidbody centre to the foot/ground reference.
        // This keeps the feet aligned with the seabed without following surface waves.
        private float _groundContactOffsetFromBody;
        private bool _hasGroundContactOffset;

        private static readonly float[] SteerAngles = { 15f, 30f, 45f, 60f, 80f, 105f, 130f, 155f };

        private struct CollisionIgnorePair
        {
            public Collider DragonCollider;
            public Collider BoatCollider;
        }

        // Mean sea level is used only for deep-water navigation checks. The dragon's
        // vertical movement is grounded to the seabed and never follows wave samples.
        private float WaterLevel => _ocean != null ? _ocean.WaterLevelY : FallbackWaterLevel;

        private bool AttacksFrozenByGameState => AttacksFrozen ||
            (FreezeAttacksDuringCutscenes && CutsceneComponent.AnyPlaying);

        private void Awake()
        {
            _swimming = GetComponent<SwimmingMovement>();
            _body = GetComponent<Rigidbody>();
            _home = transform.position;
            _patrolPoint = _home;
            _smoothedYaw = _body.rotation.eulerAngles.y;
            _turnInitialized = true;
            RefreshTargetSightCache();

            _scaleTarget = VisualRoot != null ? VisualRoot : transform;
            _initialVisualScale = _scaleTarget.localScale;
            _scaleTarget.localScale = _initialVisualScale * Mathf.Max(0.01f, BossVisualScaleMultiplier);

            if (_swimming.Body == null)
                _swimming.Body = _body;
            if (_swimming.MovementRoot == null)
                _swimming.MovementRoot = transform;

            // SwimmingMovement normally turns every swimmer toward its travel
            // direction. The boss must face its target while moving sideways around
            // the orbit, so use the same slow damping there and apply the final
            // target-facing rotation after ApplyMovement() each physics tick.
            _swimming.TurnSmoothTime = Mathf.Max(_swimming.TurnSmoothTime, TurnSmoothTime);

            // A creature this large walks on the sea floor; the generic swimmer's
            // buoyancy and wave-follow systems must not lift, bob, or drift it.
            DisableSwimmingWaveResponse();

            _body.useGravity = false;
            _body.isKinematic = false;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            CacheGroundContactOffset();
        }

        private void Start()
        {
            if (_ocean == null)
                _ocean = FindAnyObjectByType<SpectralOceanFFT>();

            // Deliberately keep SwimmingMovement.Ocean null. The cached _ocean is
            // consulted only by IsDeepWaterAt() to validate map depth.
            FindTargetIfNeeded();
            _nextPatrolPointTime = Time.time;
            _nextTargetSearchTime = Time.time + TargetSearchInterval;
            _nextBoatRamTime = Time.time;

            if (!IsDeepWater(transform.position))
            {
                Debug.LogWarning("WalkingDragon starts outside its deep-water footprint. Move its spawn point offshore or adjust GroundMapY, GroundMask, WaterSafetyRadius, and MinimumWaterDepth.", this);
            }
        }

        private void OnDisable()
        {
            SetActiveProjectilesPaused(true);
            RestoreBoatCollisionFiltering();
            if (_swimming != null)
                _swimming.Move(Vector2.zero);
        }

        private void OnEnable()
        {
            SetActiveProjectilesPaused(AttacksFrozenByGameState);
        }

        private void FixedUpdate()
        {
            if (_swimming == null || _body == null)
                return;

            // Reassert these settings every physics tick so another component cannot
            // accidentally reconnect ocean sampling or re-enable the swimmer's lift.
            DisableSwimmingWaveResponse();
            UpdateAttackFreezeState();
            RemoveDestroyedProjectiles();
            FindTargetIfNeeded();
            UpdateBoatCollisionFiltering();
            UpdateAI();
            _swimming.ApplyMovement();
            FaceCurrentTarget();
            FollowSeafloor();
        }

        private void DisableSwimmingWaveResponse()
        {
            if (_swimming == null)
                return;

            // Retain a scene-ocean reference only for the horizontal deep-water
            // navigation check. Never expose it to SwimmingMovement, which would
            // otherwise resume sampling and applying wave buoyancy/drift.
            if (_ocean == null && _swimming.Ocean != null)
                _ocean = _swimming.Ocean;

            _swimming.Ocean = null;
            _swimming.RideWaveSurface = false;
            _swimming.EnableWaveDrift = false;
            _swimming.WaveLiftMultiplier = 0f;
            _swimming.WaveVelocityFollow = 0f;
            _swimming.SurfaceSpring = 0f;
            _swimming.VerticalDamping = 0f;
            _swimming.ExtraBuoyancyAcceleration = 0f;
            _swimming.VerticalSwimAcceleration = 0f;
            _swimming.DrowningPullAcceleration = 0f;
            _swimming.SetBuoyancyInput(0f);
        }

        private void FindTargetIfNeeded()
        {
            if (_sightTarget != Target)
                RefreshTargetSightCache();

            if (Target != null || Time.time < _nextTargetSearchTime)
                return;

            Target = FindAnyObjectByType<PlayerController>();
            _nextTargetSearchTime = Time.time + Mathf.Max(0.1f, TargetSearchInterval);
            RefreshTargetSightCache();
            if (Target != null)
            {
                _orbitInitialized = false;
                _hasCachedOrbitPoint = false;
            }
        }

        private void UpdateAI()
        {
            if (Target == null)
            {
                CancelAttackSequences();
                UpdatePatrol();
                return;
            }

            bool targetCanBeAttacked = Target.Health == null || !Target.Health.IsDead;

            if (Target.IsAboardBoat)
            {
                UpdateBoatTarget(targetCanBeAttacked);
            }
            else if (Target.ActiveMovement is GroundedMovement)
            {
                UpdateLandTarget();
            }
            else
            {
                // A swimming player is still a valid fireball target. The boss
                // keeps circling and may also use fireballs while the player swims.
                CancelBoatRam();
                CurrentState = State.OrbitSwimmingTarget;
                UpdateOrbit(Target.transform.position, null);
            }

            // Fireballs are a universal pressure attack, not a land-only state.
            // Boat rams remain an additional attack when the player is aboard.
            if (!targetCanBeAttacked || _attacksFrozenNow || FireballPrefab == null)
                CancelFireballWindup();
            else
                TryFireball();
        }

        private void UpdatePatrol()
        {
            CurrentState = State.Patrol;
            _swimming.Speed = PatrolSpeed;

            if (Time.time >= _nextPatrolPointTime || FlatDistance(transform.position, _patrolPoint) <= ArriveDistance)
                PickPatrolPoint();

            MoveTowards(_patrolPoint, PatrolSpeed, null);
        }

        private void UpdateLandTarget()
        {
            CancelBoatRam();
            CurrentState = State.OrbitLandTarget;
            UpdateOrbit(Target.transform.position, null);
        }

        private void UpdateBoatTarget(bool targetCanBeAttacked)
        {
            BoatController boat = GetTargetBoat();
            if (boat == null || GetBoatBody(boat) == null)
            {
                CancelBoatRam();
                CurrentState = State.OrbitSwimmingTarget;
                UpdateOrbit(Target.transform.position, null);

                if (ShowLogs && Time.time >= _nextBoatReferenceWarningTime)
                {
                    Debug.LogWarning("WalkingDragon sees PlayerController.IsAboardBoat, but could not resolve a BoatController with a Rigidbody. Check PlayerController.CurrentBoat / player parenting and the boat Rigidbody reference.", this);
                    _nextBoatReferenceWarningTime = Time.time + 3f;
                }
                return;
            }

            if (_ramInProgress)
            {
                if (_ramBoat != boat || !targetCanBeAttacked || _attacksFrozenNow)
                {
                    CancelBoatRam();
                }
                else
                {
                    UpdateBoatRam(boat);
                    return;
                }
            }

            CurrentState = State.OrbitBoat;
            UpdateOrbit(boat.transform.position, boat);
            _orbitElapsed += Time.fixedDeltaTime;

            if (targetCanBeAttacked && !_attacksFrozenNow && Time.time >= _nextBoatRamTime && _orbitElapsed >= OrbitBeforeRam)
                BeginBoatRam(boat);
        }

        private void UpdateOrbit(Vector3 center, BoatController ignoredBoat)
        {
            if (!_orbitInitialized)
            {
                Vector3 offset = transform.position - center;
                _orbitAngle = Mathf.Atan2(offset.z, offset.x) * Mathf.Rad2Deg;
                if (offset.sqrMagnitude < 1f)
                    _orbitAngle = Random.Range(0f, 360f);
                _orbitInitialized = true;
                _orbitElapsed = 0f;
                _hasCachedOrbitPoint = false;
            }
            else if (FlatDistance(center, _lastOrbitCenter) > 0.5f)
            {
                // Follow a moving player/boat without resetting the time spent
                // circling; just ask for a new point around the updated centre.
                _hasCachedOrbitPoint = false;
            }

            _lastOrbitCenter = center;
            _orbitAngle = Mathf.Repeat(_orbitAngle + OrbitAngularSpeed * Time.fixedDeltaTime, 360f);

            if (!_hasCachedOrbitPoint || Time.time >= _nextOrbitPointTime)
            {
                _hasCachedOrbitPoint = TryFindSafeOrbitPoint(center, out _cachedOrbitPoint);
                _nextOrbitPointTime = Time.time + Mathf.Max(0.05f, OrbitRepathInterval);
            }

            if (!_hasCachedOrbitPoint)
            {
                // No safe route exists from the current location. Stop instead of
                // allowing momentum to carry this enormous boss onto an island.
                _swimming.Move(Vector2.zero);
                _swimming.SetPlanarVelocity(Vector3.zero);
                return;
            }

            float distanceToOrbitPoint = FlatDistance(transform.position, _cachedOrbitPoint);
            float speed = distanceToOrbitPoint > OrbitRepositionDistance ? OrbitApproachSpeed : OrbitSpeed;
            _swimming.Speed = speed;
            MoveTowards(_cachedOrbitPoint, speed, ignoredBoat);
        }

        private bool TryFindSafeOrbitPoint(Vector3 center, out Vector3 result)
        {
            int angleSamples = Mathf.Max(8, OrbitAngleSamples);
            int radiusAttempts = Mathf.Max(1, OrbitRadiusAttempts);
            float baseRadius = Mathf.Max(5f, OrbitRadius);
            bool shouldSeekShot = PreferClearShotOrbitPositions && RequireLineOfSight &&
                Target != null && Target.ActiveMovement is GroundedMovement &&
                !Target.IsAboardBoat;
            int shotSamples = Mathf.Clamp(OrbitShotAngleSamples, 4, 16);
            bool foundFallback = false;
            Vector3 fallback = transform.position;

            // Search the current orbit angle and nearby angles for a safe point.
            // When fighting a land target, prefer a point with a clear firing lane,
            // but retain a safe fallback so LOS never freezes the orbit movement.
            for (int radiusIndex = 0; radiusIndex < radiusAttempts; radiusIndex++)
            {
                float radius = baseRadius + radiusIndex * OrbitRadiusStep;
                for (int sample = 0; sample < angleSamples; sample++)
                {
                    int signedStep;
                    if (sample == 0)
                        signedStep = 0;
                    else
                    {
                        int magnitude = (sample + 1) / 2;
                        signedStep = sample % 2 == 1 ? magnitude : -magnitude;
                    }

                    float angle = (_orbitAngle + signedStep * (360f / angleSamples)) * Mathf.Deg2Rad;
                    Vector3 candidate = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    if (!IsDeepWater(candidate))
                        continue;

                    if (!foundFallback)
                    {
                        fallback = candidate;
                        foundFallback = true;
                    }

                    if (shouldSeekShot && sample < shotSamples)
                    {
                        Vector3 toTarget = center - candidate;
                        toTarget.y = 0f;
                        Vector3 forward = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : transform.forward;
                        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                        Vector3 muzzleOffset = FireballMuzzle != null
                            ? transform.InverseTransformPoint(FireballMuzzle.position)
                            : FallbackMuzzleOffset;
                        Vector3 virtualMuzzle = candidate + forward * muzzleOffset.z +
                            right * muzzleOffset.x + Vector3.up * muzzleOffset.y;
                        if (CanSeeTargetFromPoint(virtualMuzzle, GetTargetAimPoint()))
                        {
                            result = candidate;
                            return true;
                        }
                    }
                }

                // If we are not trying to find a clear firing lane, the first
                // radius with a safe candidate is sufficient.
                if (!shouldSeekShot && foundFallback)
                {
                    result = fallback;
                    return true;
                }
            }

            if (foundFallback)
            {
                result = fallback;
                return true;
            }

            result = transform.position;
            return false;
        }

        private bool IsFacingTargetForFireball()
        {
            if (Target == null)
                return false;

            Vector3 aimPoint = Target.transform.position + Vector3.up * FireballAimHeight;
            Vector3 aimDirection = Vector3.ProjectOnPlane(aimPoint - transform.position, Vector3.up);
            Vector3 bodyForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

            if (aimDirection.sqrMagnitude < 0.001f || bodyForward.sqrMagnitude < 0.001f)
                return true;

            float angle = Vector3.Angle(bodyForward.normalized, aimDirection.normalized);
            return angle <= Mathf.Clamp(FireballFacingTolerance, 1f, 180f);
        }

        /// <summary>
        /// The animatable component registers whether it can release fireballs from
        /// the actual Animator state's normalized time. Without an active Animator,
        /// the controller retains its configurable timed-windup fallback.
        /// </summary>
        public void SetAnimationDrivenFireballReleaseAvailable(bool available)
        {
            _animationDrivenFireballReleaseAvailable = available;
        }

        private void TryFireball()
        {
            if (Target == null || Time.time < _nextFireballTime)
                return;

            float distance = FlatDistance(transform.position, Target.transform.position);
            if (distance > FireballRange)
            {
                CancelFireballWindup();
                return;
            }

            // Start the throw only once the dragon has turned into its firing arc.
            // Once the animation begins, do not re-check yaw on every frame: the
            // colossal body can continue a slow turn while the head/muzzle animation
            // completes, and the projectile is released at the animation midpoint.
            if (!_fireballWindingUp)
            {
                bool facingForShot = IsFacingTargetForFireball();
                bool hasShotLane = !RequireLineOfSight || CanSeeTargetForFireball();
                if (!facingForShot || !hasShotLane)
                    return;

                _fireballWindingUp = true;
                _fireballWindupEnd = Time.time + FireballWindup;
                FireballWindupStarted?.Invoke();
                return;
            }

            // When an Animator is registered, WalkingDragonAnimatableComponent
            // calls TryReleaseFireballFromAnimation() at the configured normalized
            // time (50% by default). If no usable Animator is present, use the
            // original time-based wind-up so the boss remains fully functional.
            if (_animationDrivenFireballReleaseAvailable)
                return;

            if (Time.time < _fireballWindupEnd)
                return;

            TryReleaseFireballFromAnimation();
        }

        /// <summary>
        /// Releases the pending projectile. Called by WalkingDragonAnimatableComponent
        /// at the midpoint of the Fireball / Fly Fireball Animator state, or by the
        /// controller's timed fallback when no animation driver is available.
        /// </summary>
        public bool TryReleaseFireballFromAnimation()
        {
            if (!_fireballWindingUp || _attacksFrozenNow || Target == null || FireballPrefab == null)
                return false;

            if (FlatDistance(transform.position, Target.transform.position) > FireballRange)
            {
                CancelFireballWindup();
                return false;
            }

            Vector3 spawnPosition = GetFireballSpawnPosition();
            Vector3 aimPosition = Target.transform.position + Vector3.up * FireballAimHeight;
            Vector3 direction = (aimPosition - spawnPosition).normalized;
            if (direction.sqrMagnitude < 0.001f)
                direction = transform.forward;

            GameObject instance = Instantiate(FireballPrefab, spawnPosition,
                Quaternion.LookRotation(direction, Vector3.up));
            WalkingDragonFireballProjectile projectile = instance.GetComponent<WalkingDragonFireballProjectile>();
            if (projectile == null)
                projectile = instance.AddComponent<WalkingDragonFireballProjectile>();

            // Explosion visuals/radius/damage/mask are owned by the projectile
            // component on its prefab. The boss only supplies launch/target data.
            projectile.Initialize(this, Target, direction, FireballSpeed, FireballDamage, FireballLifetime);
            projectile.SetPaused(_attacksFrozenNow);
            FireballLaunched?.Invoke(spawnPosition);
            _projectiles.Add(projectile);

            _nextFireballTime = Time.time + FireballCooldown;
            _fireballWindingUp = false;
            _fireballWindupEnd = 0f;
            return true;
        }

        private void BeginBoatRam(BoatController boat)
        {
            if (boat == null || GetBoatBody(boat) == null || _attacksFrozenNow)
                return;

            _ramInProgress = true;
            _ramBoat = boat;
            _ramPhase = RamPhase.Roar;
            _ramPhaseStartTime = Time.time;
            _ramChargeStartTime = 0f;
            _ramJumpOffset = 0f;
            _orbitElapsed = 0f;
            CurrentState = State.RamRoar;
            _swimming.Move(Vector2.zero);
            _swimming.SetPlanarVelocity(Vector3.zero);
            RoarStarted?.Invoke();

            if (ShowLogs)
                Debug.Log($"WalkingDragon roared before jumping to ram boat '{boat.name}'.", this);
        }

        private void UpdateBoatRam(BoatController boat)
        {
            Rigidbody boatBody = GetBoatBody(boat);
            if (boat == null || boatBody == null || Target == null || !Target.IsAboardBoat || GetTargetBoat() != boat)
            {
                CancelBoatRam();
                return;
            }

            if (_ramPhase == RamPhase.Roar)
            {
                CurrentState = State.RamRoar;
                _swimming.Move(Vector2.zero);
                _swimming.SetPlanarVelocity(Vector3.zero);

                if (Time.time - _ramPhaseStartTime >= Mathf.Max(0f, RamWindup))
                {
                    _ramPhase = RamPhase.Jump;
                    _ramPhaseStartTime = Time.time;
                    _ramJumpBaseBodyY = _body.position.y;
                    _ramJumpOffset = 0f;
                    CurrentState = State.RamJump;
                    RamJumpStarted?.Invoke();
                }
                return;
            }

            if (_ramPhase == RamPhase.Jump)
            {
                CurrentState = State.RamJump;
                float jumpDuration = Mathf.Max(0.1f, RamJumpDuration);
                float progress = Mathf.Clamp01((Time.time - _ramPhaseStartTime) / jumpDuration);
                _ramJumpOffset = Mathf.Sin(progress * Mathf.PI) * Mathf.Max(0f, RamJumpHeight);

                // The jump deliberately bypasses normal ground/obstacle steering so
                // the colossal boss can clear an island between itself and the boat.
                MoveDirectTowards(boatBody.worldCenterOfMass, RamSpeed);

                if (TryCompleteBoatRamFromProximity(boat))
                    return;

                if (progress >= 1f)
                {
                    _ramJumpOffset = 0f;
                    _ramPhase = RamPhase.Charge;
                    _ramPhaseStartTime = Time.time;
                    _ramChargeStartTime = Time.time;
                    CurrentState = State.RamBoat;
                    RamChargeStarted?.Invoke();
                }
                return;
            }

            CurrentState = State.RamBoat;
            _swimming.Speed = RamSpeed;
            MoveTowards(boatBody.worldCenterOfMass, RamSpeed, boat);

            if (TryCompleteBoatRamFromProximity(boat))
                return;

            if (Time.time - _ramChargeStartTime > Mathf.Max(0.1f, RamTimeout))
            {
                _nextBoatRamTime = Time.time + Mathf.Max(1f, RamCooldown * 0.5f);
                if (ShowLogs)
                    Debug.LogWarning("WalkingDragon failed to reach the boat after its roar-and-jump ram; it will circle and try again.", this);
                CancelBoatRam();
            }
        }

        private bool TryCompleteBoatRamFromProximity(BoatController boat)
        {
            Vector3 snout = transform.TransformPoint(RamLocalOffset);
            if (!TryGetClosestBoatPoint(boat, snout, out Vector3 hullPoint))
                return false;

            float snoutDistance = FlatDistance(snout, hullPoint);
            float bodyContactDistance = Mathf.Max(RamHitDistance, ObstacleRadius * 0.5f) + RamBodyContactBonus;
            float bodyDistance = GetFlatDistanceToBoat(boat, transform.position);

            if (snoutDistance > RamHitDistance && bodyDistance > bodyContactDistance)
                return false;

            CompleteBoatRam(boat, hullPoint);
            return true;
        }

        private void MoveDirectTowards(Vector3 goal, float speed)
        {
            Vector3 desired = FlatDirectionTo(goal);
            _swimming.Speed = speed;
            if (desired == Vector3.zero)
            {
                _swimming.Move(Vector2.zero);
                return;
            }

            Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, transform.eulerAngles.y, 0f)) * desired;
            _swimming.Move(new Vector2(local.x, local.z));
        }

        private BoatController GetTargetBoat()
        {
            if (Target == null)
                return null;

            if (Target.CurrentBoat != null)
                return Target.CurrentBoat;

            // PlayerController parents the character under the boat while aboard.
            // This fallback covers a stale/unassigned CurrentBoat reference.
            return Target.GetComponentInParent<BoatController>();
        }

        private Rigidbody GetBoatBody(BoatController boat)
        {
            if (boat == null)
                return null;

            if (boat.Body != null)
                return boat.Body;

            Rigidbody fallback = boat.GetComponent<Rigidbody>();
            return fallback != null ? fallback : boat.GetComponentInChildren<Rigidbody>();
        }

        private bool TryGetClosestBoatPoint(BoatController boat, Vector3 from, out Vector3 closestPoint)
        {
            closestPoint = from;
            if (boat == null)
                return false;

            Collider[] colliders = boat.GetComponentsInChildren<Collider>(true);
            float closestSqrDistance = float.MaxValue;
            bool found = false;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider candidate = colliders[i];
                if (candidate == null || !candidate.enabled || candidate.isTrigger || !candidate.gameObject.activeInHierarchy)
                    continue;

                Vector3 point = candidate.ClosestPoint(from);
                float sqrDistance = (point - from).sqrMagnitude;
                if (sqrDistance >= closestSqrDistance)
                    continue;

                closestSqrDistance = sqrDistance;
                closestPoint = point;
                found = true;
            }

            if (found)
                return true;

            Rigidbody boatBody = GetBoatBody(boat);
            if (boatBody != null)
            {
                closestPoint = boatBody.ClosestPointOnBounds(from);
                return true;
            }

            return false;
        }

        private float GetFlatDistanceToBoat(BoatController boat, Vector3 from)
        {
            if (!TryGetClosestBoatPoint(boat, from, out Vector3 closestPoint))
                return float.MaxValue;

            return FlatDistance(from, closestPoint);
        }

        private void UpdateBoatCollisionFiltering()
        {
            BoatController trackedBoat = Target != null && Target.IsAboardBoat ? GetTargetBoat() : null;
            if (trackedBoat != null)
            {
                if (_collisionIgnoredBoat != trackedBoat)
                {
                    RestoreBoatCollisionFiltering();
                    _collisionIgnoredBoat = trackedBoat;

                    Collider[] dragonColliders = GetComponentsInChildren<Collider>();
                    Collider[] boatColliders = trackedBoat.GetComponentsInChildren<Collider>();
                    foreach (Collider dragonCollider in dragonColliders)
                    {
                        if (dragonCollider == null)
                            continue;
                        foreach (Collider boatCollider in boatColliders)
                        {
                            if (boatCollider == null || dragonCollider == boatCollider)
                                continue;

                            Physics.IgnoreCollision(dragonCollider, boatCollider, true);
                            _ignoredBoatCollisionPairs.Add(new CollisionIgnorePair
                            {
                                DragonCollider = dragonCollider,
                                BoatCollider = boatCollider
                            });
                        }
                    }
                }

                return;
            }

            // Keep the pair ignored after a successful ram until the huge body has
            // cleared the hull. Re-enabling contact while they overlap could apply
            // an unintended horizontal collision impulse to the boat.
            if (_collisionIgnoredBoat != null &&
                (GetBoatBody(_collisionIgnoredBoat) == null ||
                 FlatDistance(transform.position, _collisionIgnoredBoat.transform.position) > ObstacleRadius * 2.5f))
            {
                RestoreBoatCollisionFiltering();
            }
        }

        private void RestoreBoatCollisionFiltering()
        {
            for (int i = 0; i < _ignoredBoatCollisionPairs.Count; i++)
            {
                Collider dragonCollider = _ignoredBoatCollisionPairs[i].DragonCollider;
                Collider boatCollider = _ignoredBoatCollisionPairs[i].BoatCollider;
                if (dragonCollider != null && boatCollider != null)
                    Physics.IgnoreCollision(dragonCollider, boatCollider, false);
            }

            _ignoredBoatCollisionPairs.Clear();
            _collisionIgnoredBoat = null;
        }

        private void CompleteBoatRam(BoatController boat, Vector3 hullPoint)
        {
            Rigidbody boatBody = GetBoatBody(boat);
            if (boat == null || boatBody == null)
            {
                CancelBoatRam();
                return;
            }

            Vector3 ramDirection = FlatDirectionTo(boat.transform.position);
            if (ramDirection == Vector3.zero)
                ramDirection = transform.forward;

            // Launch only on world Y. Applying a VelocityChange impulse makes the
            // impact survive the next physics integration while preserving X/Z
            // velocity, boat heading, and the boat's normal steering system.
            boatBody.WakeUp();
            if (boatBody.isKinematic)
            {
                Vector3 kinematicPosition = boatBody.position;
                kinematicPosition.y += KinematicBoatLaunchDisplacement;
                boatBody.position = kinematicPosition;
            }
            else
            {
                float requiredUpwardDelta = Mathf.Max(0f, BoatLaunchVerticalVelocity - boatBody.linearVelocity.y);
                if (requiredUpwardDelta > 0f)
                    boatBody.AddForce(Vector3.up * requiredUpwardDelta, ForceMode.VelocityChange);
            }

            // A successful player hit means the hull contact happened while the
            // live target was aboard this exact boat. Knocking the player off is
            // the project's existing ram-hit response; only that success spawns
            // the shark reinforcement.
            bool playerWasHit = Target != null &&
                (Target.Health == null || !Target.Health.IsDead) &&
                Target.IsAboardBoat &&
                GetTargetBoat() == boat;

            if (playerWasHit)
                Target.KnockOffBoat(ramDirection, PlayerKnockbackSpeed);

            if (BoatRamFlood > 0f)
            {
                if (!boat.TryGetComponent(out BoatFlooding flooding))
                    flooding = boat.gameObject.AddComponent<BoatFlooding>();
                flooding.AddWater(BoatRamFlood, hullPoint);
            }

            // This event is emitted only after hull contact succeeds. The animatable
            // component uses it for the impact pose and long-range impact sound.
            RamImpact?.Invoke(hullPoint);

            _nextBoatRamTime = Time.time + RamCooldown;
            _orbitElapsed = 0f;
            CancelBoatRam();
            _hasCachedOrbitPoint = false;

            // Spawn only after the ram state has ended and only when the player
            // was actually aboard the boat at the moment of hull contact.
            if (playerWasHit)
                SpawnSharkReinforcement(boat);

            if (ShowLogs)
                Debug.Log($"WalkingDragon rammed boat '{boat.name}' and launched it vertically. Player hit: {playerWasHit}. Boat Y velocity: {boatBody.linearVelocity.y:0.0} m/s.", this);
        }

        private void SpawnSharkReinforcement(BoatController boat)
        {
            if (!SpawnSharkAfterSuccessfulRam || SharkPrefab == null || boat == null)
                return;

            Vector3 spawnPosition = boat.transform.TransformPoint(SharkSpawnLocalOffset);
            Vector3 facingDirection = Target != null
                ? Target.transform.position - spawnPosition
                : boat.transform.position - spawnPosition;
            facingDirection.y = 0f;

            if (facingDirection.sqrMagnitude < 0.001f)
                facingDirection = boat.transform.forward;

            Quaternion spawnRotation = Quaternion.LookRotation(facingDirection.normalized, Vector3.up);
            SharkController spawnedShark = Instantiate(SharkPrefab, spawnPosition, spawnRotation);
            if (spawnedShark != null)
                spawnedShark.Target = Target;

            if (ShowLogs && spawnedShark != null)
                Debug.Log($"WalkingDragon spawned shark reinforcement '{spawnedShark.name}' after hitting the player with a boat ram.", spawnedShark);
        }

        private void CancelBoatRam()
        {
            _ramInProgress = false;
            _ramBoat = null;
            _ramPhase = RamPhase.None;
            _ramJumpOffset = 0f;
        }

        private void CancelAttackSequences()
        {
            CancelFireballWindup();
            CancelBoatRam();
        }

        private void MoveTowards(Vector3 goal, float speed, BoatController ignoredBoat)
        {
            Vector3 desired = FlatDirectionTo(goal);
            _swimming.Speed = speed;

            if (desired == Vector3.zero)
            {
                _swimming.Move(Vector2.zero);
                return;
            }

            float distance = Mathf.Min(LookAhead, FlatDistance(transform.position, goal));
            if (Time.time >= _nextSteerTime)
                ReevaluateSteering(desired, Mathf.Max(PathStep, distance), ignoredBoat);

            if (_steerAngle == float.MaxValue)
            {
                _swimming.Move(Vector2.zero);
                _swimming.SetPlanarVelocity(Vector3.zero);
                return;
            }

            Vector3 direction = Quaternion.AngleAxis(_steerAngle, Vector3.up) * desired;
            Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, transform.eulerAngles.y, 0f)) * direction;
            _swimming.Move(new Vector2(local.x, local.z));
        }

        private void ReevaluateSteering(Vector3 desired, float distance, BoatController ignoredBoat)
        {
            _nextSteerTime = Time.time + Mathf.Max(0.02f, SteerInterval);
            if (CheckWaterPath(desired, distance, ignoredBoat))
            {
                _steerAngle = 0f;
                return;
            }

            foreach (float angle in SteerAngles)
            {
                float candidate = angle * _steerSign;
                if (CheckWaterPath(Quaternion.AngleAxis(candidate, Vector3.up) * desired, distance, ignoredBoat))
                {
                    _steerAngle = candidate;
                    return;
                }

                if (CheckWaterPath(Quaternion.AngleAxis(-candidate, Vector3.up) * desired, distance, ignoredBoat))
                {
                    _steerAngle = -candidate;
                    _steerSign = -_steerSign;
                    return;
                }
            }

            _steerAngle = float.MaxValue;
        }

        private bool CheckWaterPath(Vector3 direction, float distance, BoatController ignoredBoat)
        {
            Vector3 origin = transform.position;
            float step = Mathf.Max(0.25f, PathStep);
            float totalDistance = Mathf.Max(step, distance);

            for (float walked = step; ; walked += step)
            {
                float d = Mathf.Min(walked, totalDistance);
                Vector3 sample = origin + direction * d;
                if (!IsDeepWater(sample))
                    return false;
                if (d >= totalDistance)
                    break;
            }

            Vector3 castOrigin = origin + Vector3.up * ObstacleCastHeight;
            int count = Physics.SphereCastNonAlloc(castOrigin, ObstacleRadius, direction, _obstacleHits,
                totalDistance, ObstacleMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hitCollider = _obstacleHits[i].collider;
                if (hitCollider == null || IsIgnored(hitCollider))
                    continue;
                if (ignoredBoat != null && hitCollider.GetComponentInParent<BoatController>() == ignoredBoat)
                    continue;
                if (_obstacleHits[i].distance > 0f)
                    return false;
            }

            return true;
        }

        private bool IsDeepWater(Vector3 point)
        {
            if (!IsDeepWaterAt(point))
                return false;

            int sampleCount = Mathf.Clamp(WaterFootprintProbeCount, 4, 16);
            float footprint = Mathf.Max(0f, WaterSafetyRadius);
            for (int i = 0; i < sampleCount; i++)
            {
                float angle = (i / (float)sampleCount) * Mathf.PI * 2f;
                Vector3 probe = point + new Vector3(Mathf.Cos(angle) * footprint, 0f, Mathf.Sin(angle) * footprint);
                if (!IsDeepWaterAt(probe))
                    return false;
            }

            return true;
        }

        private bool IsDeepWaterAt(Vector3 point)
        {
            if (!TryGetGround(point, out float groundY))
                groundY = GroundMapY;
            return WaterLevel - groundY >= MinimumWaterDepth;
        }

        private bool TryGetGround(Vector3 point, out float groundY)
        {
            Vector3 origin = point + Vector3.up * GroundProbeHeight;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits,
                GroundProbeHeight + GroundProbeDepth, GroundMask, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            groundY = GroundMapY;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _groundHits[i];
                if (hit.collider == null || hit.distance >= nearest || IsIgnored(hit.collider))
                    continue;

                BoatController targetBoat = GetTargetBoat();
                if (targetBoat != null && hit.collider.GetComponentInParent<BoatController>() == targetBoat)
                    continue;

                nearest = hit.distance;
                groundY = hit.point.y;
                found = true;
            }

            return found;
        }

        private bool IsIgnored(Collider other)
        {
            if (other == null || other.transform.IsChildOf(transform))
                return true;
            if (Target != null && other.transform.IsChildOf(Target.transform))
                return true;
            return other.GetComponentInParent<SpectralOceanFFT>() != null;
        }

        private bool CanSeeTargetForFireball()
        {
            if (Target == null)
                return false;
            if (!RequireLineOfSight)
                return true;

            if (_sightTarget != Target)
                RefreshTargetSightCache();

            if (_hasCachedSightResult && Time.time < _nextSightCheckTime)
                return _cachedSightResult;

            Vector3 origin = GetFireballSpawnPosition();
            _cachedSightResult = HasMultiPointLineOfSight(origin);
            _hasCachedSightResult = true;
            _nextSightCheckTime = Time.time + Mathf.Max(0.05f, SightCheckInterval);
            return _cachedSightResult;
        }

        private void RefreshTargetSightCache()
        {
            _sightTarget = Target;
            _targetSightColliders = Target != null
                ? Target.GetComponentsInChildren<Collider>(true)
                : new Collider[0];
            _hasCachedSightResult = false;
            _nextSightCheckTime = 0f;
        }

        private Vector3 GetFireballSpawnPosition()
        {
            return FireballMuzzle != null
                ? FireballMuzzle.position
                : transform.position + transform.forward * FallbackMuzzleOffset.z +
                    Vector3.up * FallbackMuzzleOffset.y + transform.right * FallbackMuzzleOffset.x;
        }

        private Bounds GetTargetSightBounds()
        {
            bool found = false;
            Bounds bounds = new Bounds(Target != null ? Target.transform.position : transform.position, Vector3.one);

            if (_targetSightColliders != null)
            {
                for (int i = 0; i < _targetSightColliders.Length; i++)
                {
                    Collider candidate = _targetSightColliders[i];
                    if (candidate == null || !candidate.enabled || !candidate.gameObject.activeInHierarchy)
                        continue;

                    if (!found)
                    {
                        bounds = candidate.bounds;
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(candidate.bounds);
                    }
                }
            }

            if (!found && Target != null)
            {
                Vector3 fallbackCenter = Target.transform.position + Vector3.up * 0.9f;
                bounds = new Bounds(fallbackCenter, new Vector3(0.8f, 1.8f, 0.8f));
            }

            return bounds;
        }

        private Vector3 GetTargetAimPoint()
        {
            if (Target == null)
                return transform.position;

            Bounds bounds = GetTargetSightBounds();
            return new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.68f, bounds.center.z);
        }

        private bool HasMultiPointLineOfSight(Vector3 origin)
        {
            if (Target == null)
                return false;

            Bounds bounds = GetTargetSightBounds();
            float height = Mathf.Max(0.1f, bounds.size.y);
            float middleY = bounds.min.y + height * 0.55f;
            float sideX = Mathf.Max(0.05f, bounds.extents.x * 0.55f);
            float sideZ = Mathf.Max(0.05f, bounds.extents.z * 0.55f);

            // Several vertical samples plus lateral samples prevent a low wall,
            // rock, or small overlap from incorrectly hiding the whole player.
            _sightSamplePoints[0] = new Vector3(bounds.center.x, bounds.min.y + height * 0.15f, bounds.center.z);
            _sightSamplePoints[1] = new Vector3(bounds.center.x, bounds.min.y + height * 0.38f, bounds.center.z);
            _sightSamplePoints[2] = new Vector3(bounds.center.x, bounds.min.y + height * 0.62f, bounds.center.z);
            _sightSamplePoints[3] = new Vector3(bounds.center.x, bounds.min.y + height * 0.88f, bounds.center.z);
            _sightSamplePoints[4] = new Vector3(bounds.center.x - sideX, middleY, bounds.center.z);
            _sightSamplePoints[5] = new Vector3(bounds.center.x + sideX, middleY, bounds.center.z);
            _sightSamplePoints[6] = new Vector3(bounds.center.x, middleY, bounds.center.z - sideZ);
            _sightSamplePoints[7] = new Vector3(bounds.center.x, middleY, bounds.center.z + sideZ);

            for (int i = 0; i < _sightSamplePoints.Length; i++)
            {
                if (CanSeeTargetFromPoint(origin, _sightSamplePoints[i]))
                    return true;
            }

            return false;
        }

        private bool CanSeeTargetFromPoint(Vector3 origin, Vector3 destination)
        {
            Vector3 delta = destination - origin;
            float distance = delta.magnitude;
            if (distance <= 0.05f)
                return true;

            Vector3 direction = delta / distance;
            float nearestBlockerDistance = float.MaxValue;
            Collider nearestCollider = null;
            RaycastHit[] hits = Physics.RaycastAll(origin, direction,
                distance + Mathf.Max(0f, SightProbePadding), SightMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hitCollider = hits[i].collider;
                if (hitCollider == null || IsDragonOrWaterCollider(hitCollider))
                    continue;

                if (hits[i].distance < nearestBlockerDistance)
                {
                    nearestBlockerDistance = hits[i].distance;
                    nearestCollider = hitCollider;
                }
            }

            // An unobstructed ray is visible. When the player's collider is the
            // first hit, that sampled point is visible as well.
            if (nearestCollider == null)
                return true;

            return nearestCollider.transform == Target.transform ||
                nearestCollider.transform.IsChildOf(Target.transform);
        }

        private bool IsDragonOrWaterCollider(Collider other)
        {
            if (other == null || other.transform == transform || other.transform.IsChildOf(transform))
                return true;
            return other.GetComponentInParent<SpectralOceanFFT>() != null;
        }

        private void PickPatrolPoint()
        {
            _nextPatrolPointTime = Time.time + Mathf.Max(1f, PatrolPointTime);
            for (int i = 0; i < 32; i++)
            {
                Vector2 random = Random.insideUnitCircle * PatrolRadius;
                Vector3 candidate = _home + new Vector3(random.x, 0f, random.y);
                if (!IsDeepWater(candidate))
                    continue;

                Vector3 direction = FlatDirectionTo(candidate);
                float distance = FlatDistance(transform.position, candidate);
                if (distance < PathStep || CheckWaterPath(direction, Mathf.Min(distance, LookAhead * 2f), null))
                {
                    _patrolPoint = candidate;
                    return;
                }
            }

            _patrolPoint = transform.position;
        }

        private void CacheGroundContactOffset()
        {
            if (GroundContactReference != null)
            {
                _groundContactOffsetFromBody = GroundContactReference.position.y - _body.position.y;
                _hasGroundContactOffset = true;
                return;
            }

            // If no explicit helper is assigned, align the lowest real collider point
            // with the seabed. This is more suitable for a giant walking creature than
            // using its Rigidbody centre or a waterline/pelvis point.
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            float lowestY = float.PositiveInfinity;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c == null || !c.enabled || c.isTrigger)
                    continue;
                lowestY = Mathf.Min(lowestY, c.bounds.min.y);
            }

            if (!float.IsPositiveInfinity(lowestY))
            {
                _groundContactOffsetFromBody = lowestY - _body.position.y;
                _hasGroundContactOffset = true;
            }
            else
            {
                _groundContactOffsetFromBody = 0f;
                _hasGroundContactOffset = false;
                Debug.LogWarning("WalkingDragon has no GroundContactReference and no enabled non-trigger collider. Assign a static helper at the bottom of its feet or add a body collider so it can align to the seabed.", this);
            }
        }

        private void FollowSeafloor()
        {
            // Unlike ordinary swimmers, this boss is physically enormous and stands
            // on the ocean floor. Do not query FFT wave height, follow mean sea level,
            // or apply any buoyancy correction to its Y position.
            Vector3 groundProbe = transform.position;
            float groundY = TryGetGround(groundProbe, out float sampledGroundY)
                ? sampledGroundY
                : GroundMapY;

            float referenceY;
            if (GroundContactReference != null)
            {
                referenceY = GroundContactReference.position.y;
            }
            else if (_hasGroundContactOffset)
            {
                referenceY = _body.position.y + _groundContactOffsetFromBody;
            }
            else
            {
                referenceY = _body.position.y;
            }

            float referenceToBodyOffset = referenceY - _body.position.y;
            float targetBodyY = _ramInProgress && _ramPhase == RamPhase.Jump
                ? _ramJumpBaseBodyY + _ramJumpOffset
                : groundY + GroundContactClearance - referenceToBodyOffset;
            float positionError = targetBodyY - _body.position.y;
            bool isRamJumping = _ramInProgress && _ramPhase == RamPhase.Jump;
            float jumpDuration = Mathf.Max(0.1f, RamJumpDuration);
            float maxJumpSpeed = Mathf.Max(0f, RamJumpHeight) * Mathf.PI / jumpDuration;
            float maxVerticalSpeed = isRamJumping
                ? Mathf.Max(MaxGroundFollowSpeed, maxJumpSpeed)
                : MaxGroundFollowSpeed;

            float desiredVerticalSpeed = positionError * GroundFollowStrength;
            if (isRamJumping)
            {
                float progress = Mathf.Clamp01((Time.time - _ramPhaseStartTime) / jumpDuration);
                float arcVelocity = maxJumpSpeed * Mathf.Cos(progress * Mathf.PI);
                desiredVerticalSpeed = arcVelocity + positionError * GroundFollowStrength;
            }

            float verticalSpeed = Mathf.Clamp(desiredVerticalSpeed, -maxVerticalSpeed, maxVerticalSpeed);

            Vector3 velocity = _body.linearVelocity;
            velocity.y = verticalSpeed;
            _body.linearVelocity = velocity;
            _swimming.SetVerticalVelocity(verticalSpeed);
        }

        private void FaceCurrentTarget()
        {
            if (_ramInProgress && _ramBoat != null)
            {
                FacePoint(_ramBoat.transform.position);
                return;
            }

            if (Target != null)
            {
                BoatController targetBoat = Target.IsAboardBoat ? GetTargetBoat() : null;
                Vector3 targetPosition = targetBoat != null
                    ? targetBoat.transform.position
                    : Target.transform.position;
                FacePoint(targetPosition);
                return;
            }

            FacePoint(_patrolPoint);
        }

        private void FacePoint(Vector3 point)
        {
            Vector3 direction = FlatDirectionTo(point);
            if (direction == Vector3.zero || _body == null)
                return;

            if (!_turnInitialized)
            {
                _smoothedYaw = _body.rotation.eulerAngles.y;
                _turnInitialized = true;
            }

            float targetYaw = Quaternion.LookRotation(direction, Vector3.up).eulerAngles.y;
            _smoothedYaw = Mathf.SmoothDampAngle(
                _smoothedYaw,
                targetYaw,
                ref _yawVelocity,
                Mathf.Max(0.1f, TurnSmoothTime),
                Mathf.Max(1f, TurnMaxDegreesPerSecond),
                Time.fixedDeltaTime);

            _body.MoveRotation(Quaternion.Euler(0f, _smoothedYaw, 0f));
        }

        private void UpdateAttackFreezeState()
        {
            bool frozen = AttacksFrozenByGameState;
            if (frozen == _attacksFrozenNow)
                return;

            _attacksFrozenNow = frozen;
            if (frozen)
            {
                CancelAttackSequences();
                SetActiveProjectilesPaused(true);
            }
            else
            {
                SetActiveProjectilesPaused(false);
            }
        }

        /// <summary>Call from Timeline, a cutscene director, or gameplay code.</summary>
        public void SetAttacksFrozen(bool frozen)
        {
            AttacksFrozen = frozen;
            UpdateAttackFreezeState();
        }

        public void FreezeAttacks() => SetAttacksFrozen(true);
        public void ResumeAttacks() => SetAttacksFrozen(false);

        private void CancelFireballWindup()
        {
            _fireballWindingUp = false;
            _fireballWindupEnd = 0f;
        }

        private void SetActiveProjectilesPaused(bool paused)
        {
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                if (_projectiles[i] == null)
                {
                    _projectiles.RemoveAt(i);
                    continue;
                }
                _projectiles[i].SetPaused(paused);
            }
        }

        private void RemoveDestroyedProjectiles()
        {
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                if (_projectiles[i] == null)
                    _projectiles.RemoveAt(i);
            }
        }

        private Vector3 FlatDirectionTo(Vector3 point)
        {
            Vector3 direction = point - transform.position;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.zero;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private void OnDrawGizmos()
        {
            if (!DrawGizmos)
                return;

            Vector3 root = transform.position;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            BoatController boat = Target != null && Target.IsAboardBoat ? GetTargetBoat() : null;
            Vector3 targetPoint = boat != null
                ? boat.transform.position
                : Target != null ? Target.transform.position : root + forward * OrbitRadius;

            // Horizontal navigation and attack ranges.
            Gizmos.color = new Color(0.1f, 0.75f, 1f, 0.85f);
            DrawHorizontalRing(targetPoint, OrbitRadius);
            Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.55f);
            Gizmos.DrawWireSphere(root, FireballRange);
            Gizmos.color = new Color(1f, 0.9f, 0.1f, 0.85f);
            DrawHorizontalRing(root, ObstacleRadius);
            Gizmos.color = new Color(0.2f, 1f, 0.45f, 0.85f);
            DrawHorizontalRing(root, WaterSafetyRadius);

            // Mouth/muzzle position and fire direction/facing tolerance.
            Vector3 muzzle = FireballMuzzle != null
                ? FireballMuzzle.position
                : root + transform.forward * FallbackMuzzleOffset.z +
                    Vector3.up * FallbackMuzzleOffset.y + transform.right * FallbackMuzzleOffset.x;
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(muzzle, 1.25f);
            Gizmos.DrawLine(muzzle, muzzle + (FireballMuzzle != null ? FireballMuzzle.forward : transform.forward) * 18f);
            float halfAngle = Mathf.Clamp(FireballFacingTolerance, 1f, 180f);
            Vector3 leftCone = Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward;
            Vector3 rightCone = Quaternion.AngleAxis(halfAngle, Vector3.up) * forward;
            Gizmos.color = new Color(1f, 0.2f, 1f, 0.65f);
            Gizmos.DrawLine(root, root + leftCone * 24f);
            Gizmos.DrawLine(root, root + rightCone * 24f);

            // Ram snout/contact reference and path toward the player boat.
            Vector3 ramPoint = transform.TransformPoint(RamLocalOffset);
            Gizmos.color = new Color(1f, 0.25f, 0.1f, 1f);
            Gizmos.DrawSphere(ramPoint, 1.5f);
            Gizmos.DrawLine(root, ramPoint);

            Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.8f);
            Vector3 previousRamArcPoint = root;
            const int ramArcSegments = 24;
            for (int i = 1; i <= ramArcSegments; i++)
            {
                float t = i / (float)ramArcSegments;
                Vector3 arcPoint = Vector3.Lerp(root, targetPoint, t) +
                    Vector3.up * (Mathf.Sin(t * Mathf.PI) * Mathf.Max(0f, RamJumpHeight));
                Gizmos.DrawLine(previousRamArcPoint, arcPoint);
                previousRamArcPoint = arcPoint;
            }
            Gizmos.DrawWireSphere(targetPoint, RamHitDistance);
            if (boat != null)
            {
                Rigidbody boatBody = GetBoatBody(boat);
                if (boatBody != null)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawWireSphere(boatBody.worldCenterOfMass, RamHitDistance * 0.5f);
                }

                if (SharkPrefab != null && SpawnSharkAfterSuccessfulRam)
                {
                    Vector3 sharkSpawnPoint = boat.transform.TransformPoint(SharkSpawnLocalOffset);
                    Gizmos.color = new Color(0.1f, 0.9f, 0.9f, 0.95f);
                    Gizmos.DrawWireSphere(sharkSpawnPoint, 2f);
                    Gizmos.DrawLine(boat.transform.position, sharkSpawnPoint);
                }
            }

            // Seafloor contact reference and fallback ground height.
            Vector3 groundReference = GroundContactReference != null
                ? GroundContactReference.position
                : root + Vector3.up * -2f;
            Gizmos.color = new Color(0.35f, 0.85f, 0.3f, 0.9f);
            Gizmos.DrawSphere(groundReference, 0.8f);
            Gizmos.DrawLine(groundReference, new Vector3(groundReference.x, GroundMapY, groundReference.z));

            // Current tracked player, line-of-sight ray, and target bounds.
            if (Target != null)
            {
                Vector3 aimPoint = Target.transform.position + Vector3.up * FireballAimHeight;
                Gizmos.color = new Color(1f, 1f, 0.2f, 0.85f);
                Gizmos.DrawLine(muzzle, aimPoint);
                Gizmos.DrawWireSphere(aimPoint, 0.75f);
            }
        }

        private static void DrawHorizontalRing(Vector3 center, float radius)
        {
            const int segments = 64;
            float safeRadius = Mathf.Max(0f, radius);
            Vector3 previous = center + new Vector3(safeRadius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 next = center + new Vector3(Mathf.Cos(angle) * safeRadius, 0f, Mathf.Sin(angle) * safeRadius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
