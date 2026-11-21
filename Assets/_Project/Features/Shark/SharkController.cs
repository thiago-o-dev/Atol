using Assets._Project.Features.Audio;
using Assets._Project.Features.Boat;
using Assets._Project.Features.Combat;
using Assets._Project.Features.StatusViewModels;
using Assets._Project.Framework.Logging;
using System.Collections.Generic;
using UnityEngine;

namespace Assets._Project.Features.Shark
{
    // IA do tubarão: patrulha em volta do ponto inicial. Com o player nadando, ronda a presa, dá o bote
    // com a hitbox de mordida, se afasta e volta. Com o player no barco, circula o barco e salta nele
    // ou investe contra o casco. Se errar o salto, se prende no casco e vai enchendo o barco de água até virar.
    // O movimento é todo feito pelo SwimmingMovement.
    [RequireComponent(typeof(SwimmingMovement))]
    [RequireComponent(typeof(HealthComponent))]
    public class SharkController : MonoBehaviour
    {
        public enum State { Patrol, Stalk, Chase, Bite, Retreat, Circle, Leap, Ram, Latch, Dead }

        [Header("Target")]
        public PlayerController Target;
        public float DetectionRadius = 35f;
        public float GiveUpRadius = 60f;

        [Header("Patrol")]
        public float PatrolRadius = 20f;
        public float PatrolSpeed = 2.5f;
        public float PatrolPointTime = 10f;

        [Header("Stalk")]
        [Tooltip("Antes do bote ele ronda quem está nadando, com a barbatana de fora.")]
        public float StalkRadius = 7f;
        public float StalkSpeed = 4f;
        public Vector2 StalkTime = new Vector2(1.5f, 3f);
        [Tooltip("Depois de morder ele se afasta por esse tempo e volta a rondar.")]
        public float RetreatTime = 1.4f;
        [Tooltip("Quando a presa sai da água ele fica rondando o lugar por esse tempo.")]
        public float LurkTime = 10f;

        [Header("Chase")]
        public float ChaseSpeed = 6f;
        [Tooltip("Desiste do bote depois desse tempo sem alcançar e volta a rondar.")]
        public float ChaseMaxTime = 6f;
        [Tooltip("Presa com menos que essa fração de vida deixa ele em frenesi: ronda menos e nada mais rápido.")]
        [Range(0f, 1f)] public float FrenzyHealth = 0.5f;
        public float FrenzySpeed = 1.25f;

        [Header("Bite")]
        public DamageOnTouch Bite;
        public Vector3 BiteOffset = new Vector3(0f, 0f, 1.5f);
        public float BiteRadius = 0.8f;
        public float BiteDamage = 25f;
        public float BiteRange = 3f;
        public float BiteLungeSpeed = 9f;
        public float BiteDuration = 0.35f;
        public float BiteCooldown = 2f;
        public float BiteStaminaDrain = 20f;

        [Header("Boat Circle")]
        public float CircleRadius = 7f;
        public float CircleSpeed = 4f;
        [Tooltip("Velocidade pra alcançar o barco antes de começar a circular. Barco com bom vento escapa.")]
        public float BoatChaseSpeed = 7f;
        public Vector2 CircleTime = new Vector2(3f, 6f);

        [Header("Boat Leap")]
        public float LeapVelocity = 7f;
        public float LeapWindup = 0.6f;
        public float LeapWindupDip = 0.8f;
        public float LeapMaxSpeed = 10f;
        public float LeapHitRadius = 1.3f;
        public float LeapDamage = 20f;
        public float KnockOffSpeed = 5f;
        public float LeapCooldown = 4f;

        [Header("Boat Ram")]
        [Tooltip("Chance de investir contra o casco em vez de saltar.")]
        [Range(0f, 1f)] public float RamChance = 0.5f;
        public float RamSpeed = 9f;
        public float RamHitDistance = 1f;
        [Tooltip("Empurrão no barco, em m/s.")]
        public float RamPush = 2.5f;
        [Tooltip("Tranco de inclinação no casco, em rad/s.")]
        public float RamRoll = 0.7f;
        public float RamFlood = 15f;

        [Header("Boat Latch")]
        [Tooltip("Se errar o salto e cair até essa distância do barco, se prende no casco.")]
        public float LatchRange = 5f;
        public float LatchPull = 1.5f;
        public float LatchFloodRate = 10f;
        public float LatchMaxTime = 20f;

        [Header("Shake Off")]
        [Tooltip("Golpes do player (PlayerStrike) pra ele soltar o casco.")]
        public int StrikesToRelease = 3;
        [Tooltip("Velejando acima dessa velocidade por ShakeOffTime segundos, ele se solta.")]
        public float ShakeOffSpeed = 5f;
        public float ShakeOffTime = 2f;

        [Header("Depth")]
        [Tooltip("Centro do corpo abaixo da superfície da onda, em metros.")]
        public float SwimDepth = 0.5f;
        [Tooltip("Mais raso enquanto circula o barco, pra barbatana aparecer.")]
        public float CircleDepth = 0.2f;
        [Tooltip("0 desliga. Cola o tubarão na altura da onda; só ligar se ele ficar saindo da água.")]
        public float DepthFollow = 0f;
        [Tooltip("Limite de subida/descida pra ele não ser catapultado por uma leitura ruim da onda.")]
        public float MaxVerticalSpeed = 4f;

        [Header("Aggression")]
        [Tooltip("Multiplicadores com o Decay do mundo no máximo. Com Decay 0 vale o valor normal.")]
        public float MaxDecayDetection = 1.5f;
        public float MaxDecaySpeed = 1.4f;
        public float MaxDecayCooldown = 0.5f;

        [Header("Body")]
        [Tooltip("Modelo que inclina nas curvas e no salto. Se vazio, usa o primeiro MeshRenderer filho.")]
        public Transform Model;
        public float BankAngle = 25f;
        [Tooltip("Graus de inclinação por grau/s de curva.")]
        public float BankPerTurn = 0.4f;
        public float LeapPitch = 45f;
        [Tooltip("Força da batida de cauda (parado, a toda velocidade).")]
        public Vector2 TailPower = new Vector2(0.2f, 0.6f);
        [Tooltip("Frequência da batida de cauda (calmo, atacando).")]
        public Vector2 TailSpeed = new Vector2(3f, 6f);

        [Header("Feedback")]
        [Tooltip("Tremor de câmera quando ele parte pro ataque perto do player.")]
        public float TelegraphShake = 0.06f;
        public float RamShake = 0.35f;
        [Tooltip("Graus que o corpo torce ao levar um golpe.")]
        public float FlinchRoll = 50f;
        [Tooltip("Câmera lenta quando ele salta sobre o barco: escala do tempo e duração em segundos reais.")]
        public Vector2 LeapSlowMotion = new Vector2(0.35f, 0.9f);
        public AudioClip BiteSound;
        public AudioClip HurtSound;
        public AudioClip RamSound;
        [Tooltip("Saída e queda do salto.")]
        public AudioClip SplashSound;
        [Tooltip("Partículas de água na saída e na queda do salto e na batida no casco.")]
        public GameObject SplashEffect;

        [Header("Stuck")]
        [Tooltip("Parado por esse tempo tentando nadar, ele desiste e procura outro ponto.")]
        public float StuckTime = 2f;

        public bool ShowLogs = false;

        public State CurrentState { get; private set; }

        // 0 tranquilo, 0.5 rondando a presa, 1 atacando. A trilha de tensão acompanha isso.
        public float Threat => CurrentState switch
        {
            State.Stalk or State.Circle or State.Retreat => 0.5f,
            State.Chase or State.Bite or State.Leap or State.Ram or State.Latch => 1f,
            _ => 0f,
        };

        private SwimmingMovement _swimming;
        private HealthComponent _health;
        private Rigidbody _rigidbody;
        private FrameworkLogger _log;

        private Vector3 _home;
        private Vector3 _patrolPoint;
        private float _stateStartTime;
        private float _nextBiteTime;

        private float _depth;
        private float _targetDepth;
        private float _stuckTimer;
        private float _circleDuration;
        private float _orbitDirection = 1f;
        private float _orbitTime;
        private float _stalkDuration;
        private Vector3 _retreatDirection;
        private float _preyHealth;
        private bool _biteLanded;
        private Vector3 _lurkCenter;
        private float _lurkUntil;
        private float _nextLeapTime;
        private BoatController _leapBoat;
        private bool _leapLaunched;
        private bool _leapHit;
        private readonly List<Collider> _sharkColliders = new();
        private readonly List<Collider> _boatColliders = new();

        private BoatFlooding _flooding;
        private Vector3 _latchLocalPosition;
        private Quaternion _latchLocalRotation;
        private int _strikeCount;
        private float _fastSailTime;

        private static readonly int TailPowerId = Shader.PropertyToID("_SwimmingPower");
        private static readonly int TailSpeedId = Shader.PropertyToID("_SwimmingSpeed");

        private Renderer _modelRenderer;
        private MaterialPropertyBlock _modelProperties;
        private Vector3 _modelPosition;
        private Quaternion _modelRotation;
        private float _lastYaw;
        private float _yawRate;
        private float _roll;
        private float _pitch;

        private bool Lurking => Time.time < _lurkUntil;
        private bool InFrenzy => Target && Target.Health && Target.Health.Normalized < FrenzyHealth;

        private void Awake()
        {
            _log = new(this, showLogs: ShowLogs, prefixColor: Color.red);

            _home = transform.position;
            GetComponentsInChildren(_sharkColliders);

            _swimming = GetComponent<SwimmingMovement>();
            _health = GetComponent<HealthComponent>();
            _health.Died += OnDied;
            _health.Damaged += OnDamaged;
            _rigidbody = GetComponent<Rigidbody>();

            _swimming.HopStarted += OnLeapLaunched;
            _swimming.HopLanded += OnLeapLanded;
            _depth = SwimDepth;
            _targetDepth = SwimDepth;

            // O salto usa o pulo do SwimmingMovement, com os valores do tubarão.
            _swimming.EnableJumpHop = true;
            _swimming.JumpVelocity = LeapVelocity;
            _swimming.JumpDelay = LeapWindup;
            _swimming.JumpWindupDip = LeapWindupDip;
            _swimming.JumpStaminaCost = 0f;
            _swimming.JumpCooldown = 0f;
            _swimming.JumpMaxDepthError = 5f;

            if (Model == null)
            {
                MeshRenderer meshRenderer = GetComponentInChildren<MeshRenderer>();

                if (meshRenderer && meshRenderer.transform != transform)
                    Model = meshRenderer.transform;
            }

            if (Model)
            {
                _modelRenderer = Model.GetComponent<Renderer>();
                _modelProperties = new MaterialPropertyBlock();
                _modelPosition = Model.localPosition;
                _modelRotation = Model.localRotation;
            }

            _lastYaw = transform.eulerAngles.y;

            if (Bite == null)
                CreateBite();

            Bite.Damage = BiteDamage;
            Bite.RepeatInterval = 0f;
            Bite.gameObject.SetActive(false);
        }

        private void Start()
        {
            if (Target == null)
                Target = FindAnyObjectByType<PlayerController>();

            if (Target == null)
                _log.Warning("No PlayerController found, shark will only patrol");

            SetState(State.Patrol);
        }

        private void OnDestroy()
        {
            if (_health)
            {
                _health.Died -= OnDied;
                _health.Damaged -= OnDamaged;
            }

            if (_swimming)
            {
                _swimming.HopStarted -= OnLeapLaunched;
                _swimming.HopLanded -= OnLeapLanded;
            }
        }

        private void Update()
        {
            UpdateBody();
        }

        private void FixedUpdate()
        {
            float yaw = transform.eulerAngles.y;
            _yawRate = Mathf.DeltaAngle(_lastYaw, yaw) / Time.fixedDeltaTime;
            _lastYaw = yaw;

            UpdateStuck();

            switch (CurrentState)
            {
                case State.Patrol:
                    UpdatePatrol();
                    break;
                case State.Stalk:
                    UpdateStalk();
                    break;
                case State.Chase:
                    UpdateChase();
                    break;
                case State.Bite:
                    UpdateBite();
                    break;
                case State.Retreat:
                    UpdateRetreat();
                    break;
                case State.Circle:
                    UpdateCircle();
                    break;
                case State.Leap:
                    UpdateLeap();
                    break;
                case State.Ram:
                    UpdateRam();
                    break;
                case State.Latch:
                    UpdateLatch();
                    return;
                case State.Dead:
                    _swimming.Move(Vector2.zero);
                    break;
            }

            _swimming.ApplyMovement();

            if (CurrentState is not (State.Leap or State.Dead) && !_swimming.IsAirborne)
                FollowSurface();
        }

        // O SwimmingMovement limita a submersão e a velocidade vertical, então o tubarão ficava pra fora
        // quando a onda passava. Aqui ele é colado na altura da onda que o próprio motor já mede.
        private void FollowSurface()
        {
            if (DepthFollow <= 0f || _swimming.SurfaceSampleAge > 1f)
                return;

            _depth = Mathf.MoveTowards(_depth, _targetDepth, 0.5f * Time.fixedDeltaTime);

            float targetY = _swimming.TrackedSurfaceHeight - _depth;

            float correction = (targetY - _rigidbody.position.y) * DepthFollow;
            float waveVelocity = Mathf.Clamp(_swimming.TrackedSurfaceVelocity, -MaxVerticalSpeed, MaxVerticalSpeed);

            Vector3 velocity = _rigidbody.linearVelocity;
            velocity.y = Mathf.Clamp(correction + waveVelocity, -MaxVerticalSpeed, MaxVerticalSpeed);
            _rigidbody.linearVelocity = velocity;
        }

        // Encalhou na ilha ou travou em algo: muda de ideia em vez de ficar parado empurrando.
        private void UpdateStuck()
        {
            bool swimmingState = CurrentState is State.Patrol or State.Stalk or State.Chase or State.Circle;

            if (!swimmingState || _rigidbody == null)
            {
                _stuckTimer = 0f;
                return;
            }

            Vector3 goal = _patrolPoint;

            if (CurrentState is State.Chase or State.Stalk && Target)
                goal = Target.transform.position;
            else if (CurrentState == State.Circle && Target && Target.CurrentBoat)
                goal = Target.CurrentBoat.transform.position;

            if (FlatDistance(transform.position, goal) < BiteRange * 1.5f)
            {
                _stuckTimer = 0f;
                return;
            }

            Vector3 planarVelocity = Vector3.ProjectOnPlane(_rigidbody.linearVelocity, Vector3.up);
            _stuckTimer = planarVelocity.magnitude < 0.3f ? _stuckTimer + Time.fixedDeltaTime : 0f;

            if (_stuckTimer < StuckTime)
                return;

            _stuckTimer = 0f;
            _log.Log($"Stuck while {CurrentState}, picking another point");

            SetState(State.Patrol);
        }

        private void UpdatePatrol()
        {
            if (CanSeeTarget(DetectionRadius))
            {
                SetState(State.Stalk);
                return;
            }

            if (CanSeeBoat(DetectionRadius))
            {
                SetState(State.Circle);
                return;
            }

            bool reachedPoint = FlatDistance(transform.position, _patrolPoint) < 3f;

            if (reachedPoint || Time.time - _stateStartTime > PatrolPointTime)
                PickPatrolPoint();

            SwimTowards(_patrolPoint);
        }

        private void UpdateStalk()
        {
            if (!CanSeeTarget(GiveUpRadius))
            {
                LoseTarget();
                return;
            }

            Vector3 prey = Target.transform.position;
            bool closeEnough = FlatDistance(transform.position, prey) < StalkRadius * 1.5f;

            _swimming.Speed = (closeEnough ? StalkSpeed : ChaseSpeed) * HuntSpeed();
            SwimTowards(closeEnough ? OrbitPoint(prey, StalkRadius) : prey);

            // O tempo até chegar perto não conta, senão ele ataca assim que encosta.
            if (closeEnough)
                _orbitTime += Time.fixedDeltaTime;

            if (_orbitTime > _stalkDuration)
                SetState(State.Chase);
        }

        private void UpdateChase()
        {
            if (!CanSeeTarget(GiveUpRadius))
            {
                LoseTarget();
                return;
            }

            if (Time.time - _stateStartTime > ChaseMaxTime)
            {
                SetState(State.Stalk);
                return;
            }

            Vector3 targetPosition = Target.transform.position;

            if (FlatDistance(transform.position, targetPosition) < BiteRange && Time.time >= _nextBiteTime)
            {
                SetState(State.Bite);
                return;
            }

            SwimTowards(targetPosition);
        }

        private void UpdateBite()
        {
            if (Target)
                SwimTowards(Target.transform.position);

            if (!_biteLanded && Target && Target.Health && Target.Health.Current < _preyHealth)
            {
                _biteLanded = true;
                Sound.Play(BiteSound, transform.position);

                if (Target.Stamina)
                    Target.Stamina.Drain(BiteStaminaDrain);
            }

            if (Time.time - _stateStartTime < BiteDuration)
                return;

            _nextBiteTime = Time.time + BiteCooldown * Aggression(MaxDecayCooldown);
            StartRetreat(transform.forward);
        }

        private void UpdateRetreat()
        {
            SwimTowards(transform.position + _retreatDirection * 10f);

            if (Time.time - _stateStartTime < RetreatTime * Aggression(MaxDecayCooldown))
                return;

            if (CanSeeTarget(GiveUpRadius))
                SetState(State.Stalk);
            else if (CanSeeBoat(GiveUpRadius))
                SetState(State.Circle);
            else
                SetState(State.Patrol);
        }

        private void StartRetreat(Vector3 direction)
        {
            direction.y = 0f;

            float turn = Random.Range(25f, 50f) * (Random.value < 0.5f ? -1f : 1f);
            _retreatDirection = Quaternion.Euler(0f, turn, 0f) * direction.normalized;

            SetState(State.Retreat);
        }

        // A presa saiu da água mas continua por perto: ele não vai embora, fica rondando o lugar.
        private void LoseTarget()
        {
            bool preyOnLand = Target && !Target.IsAboardBoat && !(Target.Health && Target.Health.IsDead)
                && FlatDistance(transform.position, Target.transform.position) < GiveUpRadius;

            if (preyOnLand)
            {
                _lurkCenter = Target.transform.position;
                _lurkUntil = Time.time + LurkTime;
            }

            SetState(State.Patrol);
        }

        private void UpdateCircle()
        {
            if (!CanSeeBoat(GiveUpRadius))
            {
                SetState(State.Patrol);
                return;
            }

            Vector3 boatPosition = Target.CurrentBoat.transform.position;

            Vector3 circlePoint = OrbitPoint(boatPosition, CircleRadius);

            bool closeEnough = FlatDistance(transform.position, boatPosition) < CircleRadius * 1.5f;

            _swimming.Speed = closeEnough ? CircleSpeed : BoatChaseSpeed * Aggression(MaxDecaySpeed);
            SwimTowards(closeEnough ? circlePoint : boatPosition);

            if (closeEnough)
                _orbitTime += Time.fixedDeltaTime;

            if (_orbitTime > _circleDuration && Time.time >= _nextLeapTime)
                SetState(Random.value < RamChance ? State.Ram : State.Leap);
        }

        // Mira um ponto um pouco à frente no círculo, assim ele gira em volta do alvo.
        private Vector3 OrbitPoint(Vector3 center, float radius)
        {
            Vector3 fromCenter = transform.position - center;
            float angle = Mathf.Atan2(fromCenter.z, fromCenter.x) + 0.6f * _orbitDirection;

            return center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        }

        private void UpdateRam()
        {
            if (_leapBoat == null || !CanSeeBoat(GiveUpRadius) || Time.time - _stateStartTime > 4f)
            {
                SetState(State.Patrol);
                return;
            }

            SwimTowards(_leapBoat.transform.position);

            Vector3 nose = transform.TransformPoint(BiteOffset);
            Vector3 hullPoint = _leapBoat.Body.ClosestPointOnBounds(nose);

            if (FlatDistance(nose, hullPoint) < RamHitDistance)
                RamBoat();
        }

        private void RamBoat()
        {
            Vector3 push = FlatDirectionTo(_leapBoat.transform.position);
            Rigidbody hull = _leapBoat.Body;

            // A batida é abaixo da linha da água: o casco é empurrado e aderna pro lado do tubarão.
            hull.AddForce(push * RamPush, ForceMode.VelocityChange);
            hull.AddTorque(Vector3.Cross(push, Vector3.up) * RamRoll, ForceMode.VelocityChange);

            if (!_leapBoat.TryGetComponent(out BoatFlooding flooding))
                flooding = _leapBoat.gameObject.AddComponent<BoatFlooding>();

            flooding.AddWater(RamFlood, transform.position);

            _nextLeapTime = Time.time + LeapCooldown * Aggression(MaxDecayCooldown);
            _log.Log("Rammed the boat");

            CameraFeel.Shake(RamShake);
            Sound.Play(RamSound, transform.position);
            Splash();

            StartRetreat(-push);
        }

        private void UpdateLeap()
        {
            if (Target)
                SwimTowards(Target.transform.position);

            if (!_leapLaunched)
            {
                // Se o pulo não sair (preso ou sem água embaixo), desiste e volta a circular.
                if (Time.time - _stateStartTime > LeapWindup + 1f)
                    EndLeap();

                return;
            }

            if (!_leapHit && Target && Target.IsAboardBoat)
            {
                Vector3 bitePoint = transform.TransformPoint(BiteOffset);
                Vector3 playerCenter = Target.transform.position + Vector3.up * 0.9f;

                if (Vector3.Distance(bitePoint, playerCenter) < LeapHitRadius)
                    HitPlayerOnBoat();
            }

            if (Time.time - _stateStartTime > 5f)
                EndLeap();
        }

        private void HitPlayerOnBoat()
        {
            _leapHit = true;

            if (Target.Health)
                Target.Health.TakeDamage(LeapDamage);

            Target.KnockOffBoat(FlatDirectionTo(Target.transform.position), KnockOffSpeed);

            _log.Log("Leap hit, player knocked off the boat");
        }

        private void OnLeapLaunched()
        {
            if (CurrentState != State.Leap || Target == null)
                return;

            _leapLaunched = true;
            Splash();
            CameraFeel.SlowMotion(LeapSlowMotion.x, LeapSlowMotion.y);

            // Velocidade horizontal pra cair em cima do player, considerando o tempo no ar.
            float airTime = 2f * _swimming.JumpVelocity / _swimming.JumpGravity;
            float distance = FlatDistance(transform.position, Target.transform.position);
            float speed = Mathf.Min(distance / airTime, LeapMaxSpeed);

            _swimming.Speed = speed;
            _swimming.SetPlanarVelocity(FlatDirectionTo(Target.transform.position) * speed);

            SetBoatCollision(false);
        }

        private void OnLeapLanded(float impact)
        {
            if (CurrentState != State.Leap || !_leapLaunched)
                return;

            Splash();

            bool nearBoat = _leapBoat && FlatDistance(transform.position, _leapBoat.transform.position) < LatchRange;

            if (!_leapHit && nearBoat && _rigidbody)
                SetState(State.Latch);
            else
                EndLeap();
        }

        private void StartLatch()
        {
            _flooding = _leapBoat.GetComponent<BoatFlooding>();

            if (_flooding == null)
                _flooding = _leapBoat.gameObject.AddComponent<BoatFlooding>();

            // Gruda na lateral do casco mais próxima, virado pro barco.
            Vector3 hullPoint = _leapBoat.Body.ClosestPointOnBounds(transform.position);
            Vector3 outward = FlatDirectionFrom(_leapBoat.transform.position, hullPoint);
            Vector3 position = hullPoint + outward * 0.3f;
            position.y = Mathf.Min(transform.position.y, hullPoint.y);

            _latchLocalPosition = _leapBoat.transform.InverseTransformPoint(position);
            _latchLocalRotation = Quaternion.Inverse(_leapBoat.transform.rotation) * Quaternion.LookRotation(-outward);

            _rigidbody.isKinematic = true;

            _strikeCount = 0;
            _fastSailTime = 0f;
        }

        private void UpdateLatch()
        {
            bool playerLeft = Target == null || !Target.IsAboardBoat;

            if (_leapBoat == null || playerLeft || _flooding.IsCapsizing || Time.time - _stateStartTime > LatchMaxTime)
            {
                ReleaseLatch();
                return;
            }

            _fastSailTime = _leapBoat.PlanarSpeed > ShakeOffSpeed ? _fastSailTime + Time.fixedDeltaTime : 0f;

            if (_fastSailTime >= ShakeOffTime)
            {
                _log.Log("Shaken off by sailing fast");
                ReleaseLatch();
                return;
            }

            Transform boat = _leapBoat.transform;

            _rigidbody.MovePosition(boat.TransformPoint(_latchLocalPosition));
            _rigidbody.MoveRotation(boat.rotation * _latchLocalRotation);

            _leapBoat.Body.AddForceAtPosition(Vector3.down * LatchPull, transform.position, ForceMode.Acceleration);
            _flooding.AddWater(LatchFloodRate * Time.fixedDeltaTime, transform.position);
        }

        // Levou um golpe: torce o corpo e, se não estiver no meio do salto, recua. Preso no casco, conta pra soltar.
        private void OnDamaged(float amount)
        {
            if (_health.IsDead)
                return;

            _roll = Random.value < 0.5f ? -FlinchRoll : FlinchRoll;
            Sound.Play(HurtSound, transform.position);

            if (CurrentState == State.Latch)
            {
                if (++_strikeCount >= StrikesToRelease)
                    ReleaseLatch();
            }
            else if (CurrentState != State.Leap && Target)
            {
                StartRetreat(-FlatDirectionTo(Target.transform.position));
            }
        }

        private void ReleaseLatch()
        {
            _rigidbody.isKinematic = false;
            _rigidbody.linearVelocity = Vector3.zero;
            _swimming.SetPlanarVelocity(Vector3.zero);
            _swimming.ResetVerticalVelocity();

            if (_leapBoat)
                transform.position += FlatDirectionFrom(_leapBoat.transform.position, transform.position) * 1.5f;

            EndLeap();
        }

        private void EndLeap()
        {
            SetBoatCollision(true);

            // Se caiu em terra firme o pulo nunca "aterrissa" na água; religar o motor zera esse estado.
            if (_swimming.IsAirborne)
            {
                _swimming.enabled = false;
                _swimming.enabled = true;
            }

            _nextLeapTime = Time.time + LeapCooldown * Aggression(MaxDecayCooldown);
            SetState(State.Patrol);
        }

        // O tubarão atravessa o barco durante o salto e enquanto está preso no casco.
        private void SetBoatCollision(bool enabled)
        {
            if (!enabled)
            {
                _boatColliders.Clear();

                if (_leapBoat)
                    _leapBoat.GetComponentsInChildren(_boatColliders);
            }

            foreach (Collider sharkCollider in _sharkColliders)
            {
                foreach (Collider boatCollider in _boatColliders)
                {
                    if (sharkCollider && boatCollider)
                        Physics.IgnoreCollision(sharkCollider, boatCollider, !enabled);
                }
            }

            if (enabled)
                _boatColliders.Clear();
        }

        private void SetState(State state)
        {
            CurrentState = state;
            _stateStartTime = Time.time;

            Bite.gameObject.SetActive(state == State.Bite);
            _targetDepth = state is State.Circle or State.Stalk ? CircleDepth : SwimDepth;

            switch (state)
            {
                case State.Patrol:
                    _swimming.Speed = Lurking ? StalkSpeed : PatrolSpeed;
                    PickPatrolPoint();
                    break;
                case State.Stalk:
                    _orbitTime = 0f;
                    _orbitDirection = Random.value < 0.5f ? -1f : 1f;
                    _stalkDuration = Random.Range(StalkTime.x, StalkTime.y) * Aggression(MaxDecayCooldown);

                    if (InFrenzy)
                        _stalkDuration *= 0.5f;
                    break;
                case State.Chase:
                    _swimming.Speed = ChaseSpeed * HuntSpeed();
                    break;
                case State.Retreat:
                    _swimming.Speed = ChaseSpeed;
                    break;
                case State.Bite:
                    _preyHealth = Target.Health ? Target.Health.Current : 0f;
                    _biteLanded = false;
                    _swimming.Speed = BiteLungeSpeed;
                    _swimming.SetPlanarVelocity(FlatDirectionTo(Target.transform.position) * BiteLungeSpeed);
                    break;
                case State.Circle:
                    _orbitTime = 0f;
                    _orbitDirection = Random.value < 0.5f ? -1f : 1f;
                    _swimming.Speed = CircleSpeed;
                    _circleDuration = Random.Range(CircleTime.x, CircleTime.y) * Aggression(MaxDecayCooldown);
                    break;
                case State.Leap:
                    _leapBoat = Target.CurrentBoat;
                    _leapLaunched = false;
                    _leapHit = false;
                    _swimming.RequestJumpHop();
                    break;
                case State.Ram:
                    _leapBoat = Target.CurrentBoat;
                    _swimming.Speed = RamSpeed * Aggression(MaxDecaySpeed);
                    break;
                case State.Latch:
                    StartLatch();
                    break;
            }

            // O bote, o salto e a investida avisam com um tremor leve.
            if (state is State.Chase or State.Leap or State.Ram && FlatDistance(transform.position, Target.transform.position) < DetectionRadius)
                CameraFeel.Shake(TelegraphShake, 0.5f);

            _log.Log($"State: {state}");
        }

        private bool CanSeeTarget(float radius)
        {
            if (Target == null || Target.IsAboardBoat || CutsceneComponent.AnyPlaying)
                return false;

            if (Target.Health && Target.Health.IsDead)
                return false;

            if (Target.ActiveMovement is not SwimmingMovement)
                return false;

            return FlatDistance(transform.position, Target.transform.position) < radius * Aggression(MaxDecayDetection);
        }

        private bool CanSeeBoat(float radius)
        {
            if (Target == null || !Target.IsAboardBoat || Target.CurrentBoat == null || CutsceneComponent.AnyPlaying)
                return false;

            if (Target.Health && Target.Health.IsDead)
                return false;

            return FlatDistance(transform.position, Target.CurrentBoat.transform.position) < radius * Aggression(MaxDecayDetection);
        }

        // Quanto mais o mundo decai, mais longe ele enxerga, mais rápido persegue e menos espera entre ataques.
        private float Aggression(float maxDecayMultiplier)
        {
            WorldStateController world = WorldStateController.Instance;

            if (world == null)
                return 1f;

            return Mathf.Lerp(1f, maxDecayMultiplier, world.Decay / (float)WorldStateController.DecayMaximum);
        }

        private void Splash()
        {
            Vector3 surface = transform.position;
            surface.y = _swimming.TrackedSurfaceHeight;

            Sound.Play(SplashSound, surface);

            if (SplashEffect)
                Instantiate(SplashEffect, surface, Quaternion.identity);
        }

        private float HuntSpeed()
        {
            return Aggression(MaxDecaySpeed) * (InFrenzy ? FrenzySpeed : 1f);
        }

        // Só visual: o modelo deita nas curvas, empina no salto e bate a cauda conforme a velocidade.
        private void UpdateBody()
        {
            if (Model == null)
                return;

            Vector3 velocity = _rigidbody ? _rigidbody.linearVelocity : Vector3.zero;
            float planarSpeed = new Vector2(velocity.x, velocity.z).magnitude;

            float targetRoll = Mathf.Clamp(-_yawRate * BankPerTurn, -BankAngle, BankAngle);
            float targetPitch = 0f;

            if (_swimming.IsAirborne)
                targetPitch = Mathf.Clamp(-Mathf.Atan2(velocity.y, Mathf.Max(planarSpeed, 0.5f)) * Mathf.Rad2Deg, -LeapPitch, LeapPitch);

            if (CurrentState is State.Latch or State.Dead)
                targetRoll = 0f;

            _roll = Mathf.Lerp(_roll, targetRoll, 5f * Time.deltaTime);
            _pitch = Mathf.Lerp(_pitch, targetPitch, 8f * Time.deltaTime);

            // Gira em volta da raiz, não do pivô do modelo, que fica deslocado.
            Quaternion tilt = Quaternion.Euler(_pitch, 0f, _roll);
            Model.SetLocalPositionAndRotation(tilt * _modelPosition, tilt * _modelRotation);

            if (_modelRenderer == null)
                return;

            bool attacking = CurrentState is State.Chase or State.Bite or State.Leap or State.Ram;
            float effort = Mathf.Clamp01(planarSpeed / Mathf.Max(ChaseSpeed, 0.1f));

            _modelRenderer.GetPropertyBlock(_modelProperties);
            _modelProperties.SetFloat(TailPowerId, CurrentState == State.Dead ? 0f : Mathf.Lerp(TailPower.x, TailPower.y, effort));
            _modelProperties.SetFloat(TailSpeedId, attacking ? TailSpeed.y : TailSpeed.x);
            _modelRenderer.SetPropertyBlock(_modelProperties);
        }

        private void PickPatrolPoint()
        {
            // Tenta algumas vezes achar um ponto que não seja em cima da ilha. Se não achar, vai assim mesmo.
            Vector3 center = Lurking ? _lurkCenter : _home;
            float radius = Lurking ? StalkRadius : PatrolRadius;

            for (int i = 0; i < 8; i++)
            {
                Vector2 offset = Random.insideUnitCircle * radius;
                _patrolPoint = center + new Vector3(offset.x, 0f, offset.y);

                if (IsOverWater(_patrolPoint))
                    break;
            }

            _stateStartTime = Time.time;
        }

        private bool IsOverWater(Vector3 point)
        {
            Vector3 origin = point + Vector3.up * 15f;

            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore))
                return true;

            if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<SpectralOceanFFT>())
                return true;

            // Chão perto do nível do mar = praia ou ilha.
            return hit.point.y < _home.y - 2f;
        }

        // O SwimmingMovement sem ReferenceTransform recebe o input relativo à direção do próprio tubarão.
        private void SwimTowards(Vector3 point)
        {
            Vector3 direction = FlatDirectionTo(point);

            if (direction == Vector3.zero)
            {
                _swimming.Move(Vector2.zero);
                return;
            }

            Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, transform.eulerAngles.y, 0f)) * direction;

            _swimming.Move(new Vector2(local.x, local.z));
        }

        private Vector3 FlatDirectionTo(Vector3 point)
        {
            Vector3 direction = point - transform.position;
            direction.y = 0f;

            return direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.zero;
        }

        private static Vector3 FlatDirectionFrom(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            direction.y = 0f;

            return direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.forward;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;

            return Vector3.Distance(a, b);
        }

        private void CreateBite()
        {
            GameObject biteObject = new GameObject("Bite Hitbox", typeof(SphereCollider));
            biteObject.transform.SetParent(transform, false);
            biteObject.transform.localPosition = BiteOffset;

            SphereCollider biteCollider = biteObject.GetComponent<SphereCollider>();
            biteCollider.isTrigger = true;
            biteCollider.radius = BiteRadius;

            Bite = biteObject.AddComponent<DamageOnTouch>();
        }

        private void OnDied()
        {
            if (CurrentState == State.Latch)
                ReleaseLatch();
            else if (CurrentState == State.Leap)
                SetBoatCollision(true);

            SetState(State.Dead);

            _swimming.SetDrowning(true);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 home = Application.isPlaying ? _home : transform.position;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(home, PatrolRadius);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, DetectionRadius);

            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.TransformPoint(BiteOffset), BiteRadius);
        }
    }
}
