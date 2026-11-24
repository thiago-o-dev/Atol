using Assets._Project.Features.Audio;
using Assets._Project.Features.Boat;
using Assets._Project.Features.Combat;
using Assets._Project.Features.StatusViewModels;
using Assets._Project.Framework.Logging;
using UnityEngine;

namespace Assets._Project.Features.Bear
{
    public enum BearAttack { Claw, Jump, Ram }

    // IA do urso: patrulha em volta do ponto inicial, parando de vez em quando. Ao notar o player (vista ou
    // barulho) ele ruge e corre atrás. De perto dá a patada; a média distância salta em cima; a longa distância
    // faz uma investida em linha reta. O urso nunca entra na água: cada passo é testado na frente e, se o
    // caminho vai pra água, borda ou parede, ele contorna ou para. Player nadando ou no barco é ignorado.
    // O movimento é todo feito pelo GroundedMovement; a animação fica no BearAnimatableComponent.
    [RequireComponent(typeof(GroundedMovement))]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(HealthComponent))]
    public class BearController : MonoBehaviour
    {
        public enum State { Patrol, Alert, Chase, Claw, Jump, Ram, Recover, Dead }

        // Todo ataque tem as mesmas três fases: preparação (telegrafa), ação e recuperação.
        public enum AttackPhase { Windup, Active, Recover }

        [Header("Target")]
        public PlayerController Target;
        [Tooltip("Distância em que ele nota o player dentro do campo de visão.")]
        public float DetectionRadius = 22f;
        [Tooltip("Ele ouve o player a essa distância mesmo de costas.")]
        public float HearingRadius = 8f;
        [Tooltip("Campo de visão total, em graus.")]
        [Range(10f, 360f)] public float ViewAngle = 140f;
        [Tooltip("Depois de ver o player ele só desiste além dessa distância.")]
        public float GiveUpRadius = 40f;
        [Tooltip("Paredes e pedras entre os dois escondem o player.")]
        public bool RequireLineOfSight = true;
        public LayerMask SightMask = ~0;
        public float EyeHeight = 1.4f;
        [Tooltip("Sem ver o player por esse tempo, ele vai até onde o viu por último e desiste.")]
        public float LoseSightTime = 3f;

        [Header("Patrol")]
        public float PatrolRadius = 15f;
        public float PatrolSpeed = 1.8f;
        public Vector2 IdleTime = new Vector2(2f, 5f);
        public float PatrolPointTime = 12f;
        public float ArriveDistance = 1.2f;

        [Header("Alert")]
        [Tooltip("Tempo rugindo virado pro player antes de correr atrás.")]
        public float AlertTime = 1f;
        public float TurnSpeed = 240f;

        [Header("Chase")]
        public float ChaseSpeed = 6.5f;
        [Tooltip("Parado ou sem conseguir chegar por esse tempo, ele desiste e ignora o player por IgnoreTime.")]
        public float StuckTime = 3f;
        public float IgnoreTime = 5f;

        [Header("Claw (ataque de perto)")]
        public float ClawRange = 2.4f;
        [Tooltip("Abertura total da patada, em graus.")]
        [Range(10f, 360f)] public float ClawArc = 100f;
        public float ClawDamage = 18f;
        [Tooltip("Do início da animação até a garra acertar.")]
        public float ClawWindup = 0.45f;
        public float ClawRecover = 0.6f;
        public float ClawCooldown = 1.2f;
        [Tooltip("Passo pra frente no golpe, em m/s.")]
        public float ClawLunge = 3f;
        [Tooltip("Quantas animações diferentes de patada (0, 1, ...). Vai alternando.")]
        [Min(1)] public int ClawVariants = 2;
        public float ClawShake = 0.12f;

        [Header("Jump (salto em cima do player)")]
        public float JumpMinRange = 5f;
        public float JumpMaxRange = 11f;
        [Tooltip("Agacha e mira por esse tempo antes de saltar.")]
        public float JumpWindup = 0.55f;
        public float JumpHeight = 1.6f;
        public float JumpMaxSpeed = 12f;
        [Tooltip("Quanto da velocidade do player entra na mira (0 mira onde ele está, 1 onde estará).")]
        [Range(0f, 1.5f)] public float JumpLead = 0.6f;
        [Tooltip("Acerto direto no ar.")]
        public float JumpHitRadius = 1.6f;
        [Tooltip("Pancada ao aterrissar. O player pulando por cima escapa (veja VerticalReach).")]
        public float JumpSlamRadius = 2.6f;
        public float JumpDamage = 30f;
        public float JumpRecover = 0.9f;
        public float JumpCooldown = 6f;
        public float JumpLandShake = 0.3f;

        [Header("Ram (investida)")]
        public float RamMinRange = 7f;
        public float RamMaxRange = 20f;
        [Tooltip("Raspa o chão e mira por esse tempo antes de disparar.")]
        public float RamWindup = 0.8f;
        public float RamSpeed = 11f;
        [Tooltip("Aceleração do GroundedMovement durante a investida.")]
        public float RamAcceleration = 30f;
        [Tooltip("Graus por segundo que ele ainda corrige a direção. Baixo pra dar pra desviar.")]
        public float RamSteer = 25f;
        public float RamMaxDistance = 22f;
        public float RamMaxTime = 2.5f;
        public float RamHitRadius = 1.5f;
        public float RamDamage = 35f;
        [Tooltip("Empurrão no player, em m/s.")]
        public float RamKnockback = 8f;
        [Tooltip("Bater numa parede deixa ele atordoado por esse tempo.")]
        public float RamStunTime = 1.6f;
        public float RamMissRecover = 1f;
        public float RamCooldown = 8f;
        public float RamShake = 0.35f;
        [Tooltip("Chance da investida quando salto e investida estão disponíveis ao mesmo tempo.")]
        [Range(0f, 1f)] public float RamChance = 0.5f;
        [Tooltip("Pausa mínima entre dois ataques especiais (salto ou investida).")]
        public float SpecialGap = 1f;

        [Header("Combat")]
        [Tooltip("Diferença de altura máxima pra um ataque acertar. Pular a pancada do salto escapa dela.")]
        public float VerticalReach = 1.6f;
        public float JumpKnockback = 5f;
        public float ClawKnockback = 3f;

        [Header("Water & Obstacles")]
        [Tooltip("O Y do mar vem do oceano da cena. Só usado se ele não for encontrado.")]
        public float FallbackSeaLevel = 0f;
        [Tooltip("Chão só conta como seco acima do nível do mar mais essa margem, por causa das ondas.")]
        public float MinGroundAboveWater = 0.6f;
        [Tooltip("Altura de onde o raio pro chão parte. Evita tetos baixos.")]
        public float GroundProbeHeight = 2f;
        public float GroundProbeDepth = 8f;
        public LayerMask GroundMask = ~0;
        [Tooltip("Queda máxima entre dois pontos testados. Maior que isso é barranco.")]
        public float MaxStepDrop = 1.2f;
        [Tooltip("Distância mínima testada à frente. Cresce com a velocidade.")]
        public float LookAhead = 2f;
        [Tooltip("Segundos de antecedência da velocidade na distância testada.")]
        public float LookAheadTime = 0.35f;
        public float PathStep = 0.75f;
        public float ObstacleHeight = 0.8f;
        public float ObstacleRadius = 0.4f;
        public LayerMask ObstacleMask = ~0;
        [Tooltip("De quanto em quanto tempo ele reavalia o desvio.")]
        public float SteerInterval = 0.1f;
        [Tooltip("Se acabar na água (empurrado, por exemplo), volta ao último ponto seco depois desse tempo.")]
        public float WaterEscapeTime = 0.75f;

        [Header("Aggression")]
        [Tooltip("Multiplicadores com o Decay do mundo no máximo. Com Decay 0 vale o valor normal.")]
        public float MaxDecayDetection = 1.4f;
        public float MaxDecaySpeed = 1.25f;
        public float MaxDecayCooldown = 0.6f;

        [Header("Feedback")]
        public AudioClip RoarSound;
        public AudioClip ClawSound;
        public AudioClip JumpSound;
        public AudioClip SlamSound;
        public AudioClip RamSound;
        public AudioClip RamImpactSound;
        public AudioClip HurtSound;
        public AudioClip DeathSound;

        public bool ShowLogs = false;

        public State CurrentState { get; private set; }
        public AttackPhase Phase { get; private set; }

        public bool IsDead => CurrentState == State.Dead;
        public bool IsAggro => CurrentState is State.Alert or State.Chase or State.Claw or State.Jump or State.Ram;
        public bool IsCharging => CurrentState == State.Ram && Phase == AttackPhase.Active;
        public bool IsStunned { get; private set; }
        public int ClawVariant { get; private set; }
        public GroundedMovement Movement => _movement;
        public HealthComponent Health => _health;

        // Velocidade de corrida: referência pra o animator saber o que é andar e o que é correr.
        public float ReferenceSpeed => ChaseSpeed;

        // 0 tranquilo, 0.5 alerta/perseguindo, 1 atacando. Pode alimentar a trilha de tensão como a do tubarão.
        public float Threat => CurrentState switch
        {
            State.Alert or State.Chase => 0.5f,
            State.Claw or State.Jump or State.Ram => 1f,
            _ => 0f,
        };

        public event System.Action Alerted;
        // Início da preparação do ataque: o animator dispara a animação certa.
        public event System.Action<BearAttack> AttackStarted;
        // Instante do golpe (garra acertando, saída do salto, início da investida).
        public event System.Action<BearAttack> AttackStruck;
        public event System.Action Staggered;
        public event System.Action Stunned;

        private GroundedMovement _movement;
        private CharacterController _controller;
        private HealthComponent _health;
        private SpectralOceanFFT _ocean;
        private FrameworkLogger _log;

        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private static readonly float[] SteerAngles = { 30f, 60f, 90f, 135f };

        private Vector3 _home;
        private Vector3 _patrolPoint;
        private float _patrolStart;
        private float _idleUntil;
        private float _stateStart;
        private float _phaseStart;

        private float _baseSpeed;
        private float _baseAcceleration;

        private float _nextClawTime;
        private float _nextJumpTime;
        private float _nextRamTime;
        private float _nextSpecialTime;
        private float _nextSpecialCheck;
        private float _ignoreTargetUntil;

        private Vector3 _lastKnownPosition;
        private float _lostSightTime;
        private float _blockedTime;

        private float _steerOffset;
        private float _steerSign = 1f;
        private float _nextSteerTime;
        private bool _steerBlocked;

        private bool _attackHit;
        private bool _struck;
        private Vector3 _jumpDirection;
        private float _jumpSpeed;
        private float _jumpLaunchTime;
        private Vector3 _ramDirection;
        private Vector3 _ramOrigin;
        private float _ramChargeStart;
        private float _stunTime;

        private Vector3 _lastSafePosition;
        private float _nextSafeCheck;
        private float _notDryTime;
        private float _dryTime;

        private float SeaLevel => _ocean ? _ocean.WaterLevelY : FallbackSeaLevel;

        private void Awake()
        {
            _log = new(this, showLogs: ShowLogs, prefixColor: new Color(0.6f, 0.35f, 0.1f));

            _movement = GetComponent<GroundedMovement>();
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<HealthComponent>();

            if (_movement.Controller == null)
                _movement.Controller = _controller;

            _baseSpeed = _movement.Speed;
            _baseAcceleration = _movement.Acceleration;

            _home = transform.position;
            _lastSafePosition = _home;

            _health.Died += OnDied;
            _health.Damaged += OnDamaged;
        }

        private void Start()
        {
            _ocean = FindAnyObjectByType<SpectralOceanFFT>();

            if (Target == null)
                Target = FindAnyObjectByType<PlayerController>();

            if (Target == null)
                _log.Warning("No PlayerController found, bear will only patrol");

            SetState(State.Patrol);
        }

        private void OnDestroy()
        {
            if (_health)
            {
                _health.Died -= OnDied;
                _health.Damaged -= OnDamaged;
            }
        }

        private void FixedUpdate()
        {
            UpdateWaterSafety();

            switch (CurrentState)
            {
                case State.Patrol:
                    UpdatePatrol();
                    break;
                case State.Alert:
                    UpdateAlert();
                    break;
                case State.Chase:
                    UpdateChase();
                    break;
                case State.Claw:
                    UpdateClaw();
                    break;
                case State.Jump:
                    UpdateJump();
                    break;
                case State.Ram:
                    UpdateRam();
                    break;
                case State.Recover:
                    UpdateRecover();
                    break;
                case State.Dead:
                    _movement.Move(Vector2.zero);
                    break;
            }

            _movement.ApplyMovement();
        }

        // ---------------------------------------------------------------- Patrol

        private void UpdatePatrol()
        {
            if (CanNoticeTarget())
            {
                SetState(State.Alert);
                return;
            }

            if (Time.time < _idleUntil)
            {
                _movement.Move(Vector2.zero);
                return;
            }

            bool reached = FlatDistance(transform.position, _patrolPoint) < ArriveDistance;
            bool timedOut = Time.time - _patrolStart > PatrolPointTime;

            if (reached || timedOut || _blockedTime > 1f)
            {
                // Chegou (ou não deu pra chegar): fuça o chão um pouco e escolhe outro ponto.
                _idleUntil = Time.time + Random.Range(IdleTime.x, IdleTime.y);
                PickPatrolPoint();
                _blockedTime = 0f;
                return;
            }

            MoveTowards(_patrolPoint, PatrolSpeed, FlatDistance(transform.position, _patrolPoint));
        }

        private void PickPatrolPoint()
        {
            // Tenta alguns pontos até achar um em terra seca. Se não achar, fica no lugar.
            _patrolPoint = transform.position;

            for (int i = 0; i < 10; i++)
            {
                Vector2 offset = Random.insideUnitCircle * PatrolRadius;
                Vector3 point = _home + new Vector3(offset.x, 0f, offset.y);

                if (TryGetGround(point, out float groundY) && groundY >= SeaLevel + MinGroundAboveWater)
                {
                    _patrolPoint = new Vector3(point.x, groundY, point.z);
                    break;
                }
            }

            _patrolStart = Time.time;
        }

        // ---------------------------------------------------------------- Alert & Chase

        private void UpdateAlert()
        {
            _movement.Move(Vector2.zero);

            if (!CanSeeTarget(GiveUpRadius))
            {
                SetState(State.Patrol);
                return;
            }

            FaceDirection(FlatDirectionTo(Target.transform.position), TurnSpeed);

            if (Time.time - _stateStart >= AlertTime * Aggression(MaxDecayCooldown))
                SetState(State.Chase);
        }

        private void UpdateChase()
        {
            if (!CanSeeTarget(GiveUpRadius))
            {
                SetState(State.Patrol);
                return;
            }

            Vector3 prey = Target.transform.position;
            float distance = FlatDistance(transform.position, prey);

            // Sem ver o player (parede, árvore) ele corre até onde o viu por último e desiste.
            if (RequireLineOfSight && !HasLineOfSight())
            {
                _lostSightTime += Time.fixedDeltaTime;

                if (_lostSightTime > LoseSightTime || FlatDistance(transform.position, _lastKnownPosition) < ArriveDistance)
                {
                    SetState(State.Patrol);
                    return;
                }

                prey = _lastKnownPosition;
                distance = FlatDistance(transform.position, prey);
            }
            else
            {
                _lostSightTime = 0f;
                _lastKnownPosition = prey;
            }

            if (distance <= ClawRange * 0.9f && Time.time >= _nextClawTime && _lostSightTime <= 0f)
            {
                SetState(State.Claw);
                return;
            }

            if (_lostSightTime <= 0f && Time.time >= _nextSpecialTime && TryStartSpecial(distance))
                return;

            float speed = ChaseSpeed * Aggression(MaxDecaySpeed);

            // Perto demais da presa ele só encara; empurrar o player não é o ataque.
            if (distance > ClawRange * 0.7f)
            {
                MoveTowards(prey, speed, Mathf.Max(distance - ClawRange * 0.5f, 0.75f));
            }
            else
            {
                _movement.Move(Vector2.zero);
                FaceDirection(FlatDirectionTo(prey), TurnSpeed);
                _blockedTime = 0f;
            }

            // Não consegue chegar (presa na beira da água, do outro lado de um barranco): desiste por um tempo.
            if (_blockedTime >= StuckTime)
            {
                _log.Log("Can't reach the target, giving up for a while");
                _ignoreTargetUntil = Time.time + IgnoreTime;
                SetState(State.Patrol);
            }
        }

        private bool TryStartSpecial(float distance)
        {
            // Testar o caminho custa vários raios: reavalia poucas vezes por segundo.
            if (Time.time < _nextSpecialCheck)
                return false;

            _nextSpecialCheck = Time.time + 0.2f;

            Vector3 prey = Target.transform.position;

            bool jumpReady = distance >= JumpMinRange && distance <= JumpMaxRange && Time.time >= _nextJumpTime
                             && CanLandAt(prey);

            bool ramReady = distance >= RamMinRange && distance <= RamMaxRange && Time.time >= _nextRamTime
                            && IsRunwayClear(prey);

            if (!jumpReady && !ramReady)
                return false;

            bool ram = ramReady && (!jumpReady || Random.value < RamChance);

            SetState(ram ? State.Ram : State.Jump);
            return true;
        }

        // ---------------------------------------------------------------- Claw

        private void UpdateClaw()
        {
            float elapsed = Time.time - _phaseStart;

            switch (Phase)
            {
                case AttackPhase.Windup:
                    _movement.Move(Vector2.zero);

                    if (Target)
                        FaceDirection(FlatDirectionTo(Target.transform.position), TurnSpeed);

                    if (elapsed >= ClawWindup)
                    {
                        SetPhase(AttackPhase.Recover);
                        ClawStrike();
                    }
                    break;

                case AttackPhase.Recover:
                    _movement.Move(Vector2.zero);

                    if (elapsed >= ClawRecover)
                    {
                        _nextClawTime = Time.time + ClawCooldown * Aggression(MaxDecayCooldown);
                        EndAttack();
                    }
                    break;
            }
        }

        private void ClawStrike()
        {
            _movement.SetPlanarVelocity(transform.forward * ClawLunge);

            AttackStruck?.Invoke(BearAttack.Claw);
            Sound.Play(ClawSound, transform.position);

            if (!CanHitTarget(ClawRange, ClawArc))
                return;

            DamageTarget(ClawDamage, FlatDirectionTo(Target.transform.position), ClawKnockback);
            CameraFeel.Shake(ClawShake);
        }

        // ---------------------------------------------------------------- Jump

        private void UpdateJump()
        {
            float elapsed = Time.time - _phaseStart;

            switch (Phase)
            {
                case AttackPhase.Windup:
                    // Agacha acompanhando o player. A mira trava na hora de saltar.
                    _movement.Move(Vector2.zero);

                    if (Target)
                        FaceDirection(FlatDirectionTo(Target.transform.position), TurnSpeed);

                    if (elapsed >= JumpWindup)
                    {
                        LaunchJump();
                        SetPhase(AttackPhase.Active);
                    }
                    break;

                case AttackPhase.Active:
                    // Mantém a velocidade horizontal: o GroundedMovement desacelera sem input.
                    _movement.Speed = _jumpSpeed;
                    _movement.Move(new Vector2(_jumpDirection.x, _jumpDirection.z));

                    if (!_attackHit && Target && FlatDistance(transform.position, Target.transform.position) < JumpHitRadius
                        && Mathf.Abs(Target.transform.position.y - transform.position.y) < VerticalReach + 1f)
                    {
                        _attackHit = true;
                        DamageTarget(JumpDamage, _jumpDirection, JumpKnockback);
                    }

                    bool landed = !_movement.IsJumpInProgress && Time.time - _jumpLaunchTime > 0.15f;

                    if (landed || Time.time - _jumpLaunchTime > 4f)
                    {
                        JumpSlam();
                        SetPhase(AttackPhase.Recover);
                    }
                    break;

                case AttackPhase.Recover:
                    _movement.Move(Vector2.zero);

                    if (elapsed >= JumpRecover)
                    {
                        _nextJumpTime = Time.time + JumpCooldown * Aggression(MaxDecayCooldown);
                        EndAttack();
                    }
                    break;
            }
        }

        private void LaunchJump()
        {

            // Tempo no ar de um pulo que sobe e volta à mesma altura.
            float gravity = Mathf.Abs(Physics.gravity.y);
            float airTime = 2f * Mathf.Sqrt(2f * JumpHeight * gravity) / Mathf.Max(gravity * _movement.GravityMultiplier, 0.1f);

            Vector3 aim = Target ? Target.transform.position : transform.position + transform.forward * JumpMinRange;

            if (Target && Target.GroundedMovement)
                aim += Target.GroundedMovement.PlanarVelocity * (airTime * JumpLead);

            Vector3 direction = FlatDirectionTo(aim);

            if (direction == Vector3.zero)
                direction = transform.forward;

            float distance = FlatDistance(transform.position, aim);
            float speed = Mathf.Min(distance / airTime, JumpMaxSpeed);

            // O pouso tem que ser em terra seca. Encurta o salto até caber; se nem assim, pula quase no lugar.
            for (int i = 0; i < 4; i++)
            {
                Vector3 landing = transform.position + direction * (speed * airTime);

                if (IsDryAt(landing))
                    break;

                speed *= 0.7f;
            }

            _jumpDirection = direction;
            _jumpSpeed = Mathf.Max(speed, 1f);
            _jumpLaunchTime = Time.time;
            _attackHit = false;

            _movement.Speed = _jumpSpeed;
            _movement.LaunchJump(direction * _jumpSpeed, JumpHeight);

            AttackStruck?.Invoke(BearAttack.Jump);
            Sound.Play(JumpSound, transform.position);
        }

        private void JumpSlam()
        {
            _movement.Speed = _baseSpeed;
            _movement.SetPlanarVelocity(Vector3.zero);

            AttackStruck?.Invoke(BearAttack.Jump);
            Sound.Play(SlamSound, transform.position);
            CameraFeel.Shake(JumpLandShake);

            if (_attackHit || Target == null)
                return;

            // Pulando por cima da pancada o player escapa.
            if (FlatDistance(transform.position, Target.transform.position) <= JumpSlamRadius
                && Mathf.Abs(Target.transform.position.y - transform.position.y) <= VerticalReach)
            {
                _attackHit = true;
                DamageTarget(JumpDamage, FlatDirectionTo(Target.transform.position), JumpKnockback);
            }
        }

        // ---------------------------------------------------------------- Ram

        private void UpdateRam()
        {
            float elapsed = Time.time - _phaseStart;

            switch (Phase)
            {
                case AttackPhase.Windup:
                    _movement.Move(Vector2.zero);

                    if (Target)
                        FaceDirection(FlatDirectionTo(Target.transform.position), TurnSpeed);

                    if (elapsed >= RamWindup)
                        StartCharge();
                    break;

                case AttackPhase.Active:
                    UpdateCharge();
                    break;

                case AttackPhase.Recover:
                    _movement.Move(Vector2.zero);

                    float recover = IsStunned ? _stunTime : RamMissRecover;

                    if (elapsed >= recover)
                    {
                        _nextRamTime = Time.time + RamCooldown * Aggression(MaxDecayCooldown);
                        EndAttack();
                    }
                    break;
            }
        }

        private void StartCharge()
        {
            _ramDirection = Target ? FlatDirectionTo(Target.transform.position) : transform.forward;

            if (_ramDirection == Vector3.zero)
                _ramDirection = transform.forward;

            _ramOrigin = transform.position;
            _ramChargeStart = Time.time;
            _attackHit = false;

            _movement.Speed = RamSpeed * Aggression(MaxDecaySpeed);
            _movement.Acceleration = RamAcceleration;

            SetPhase(AttackPhase.Active);

            AttackStruck?.Invoke(BearAttack.Ram);
            Sound.Play(RamSound, transform.position);
        }

        private void UpdateCharge()
        {
            // Corrige a direção só um pouco: dá pra sair da frente.
            if (Target)
            {
                Vector3 toTarget = FlatDirectionTo(Target.transform.position);

                if (toTarget != Vector3.zero)
                {
                    float step = RamSteer * Mathf.Deg2Rad * Time.fixedDeltaTime;
                    _ramDirection = Vector3.RotateTowards(_ramDirection, toTarget, step, 0f);
                    _ramDirection.y = 0f;
                    _ramDirection.Normalize();
                }
            }

            float lookAhead = Mathf.Max(LookAhead, _movement.PlanarVelocity.magnitude * LookAheadTime);
            PathResult path = CheckPath(_ramDirection, lookAhead);

            if (path != PathResult.Clear)
            {
                // Parede: bate e fica atordoado. Água ou barranco: derrapa e para antes.
                EndCharge(path == PathResult.Obstacle);
                return;
            }

            _movement.Move(new Vector2(_ramDirection.x, _ramDirection.z));

            if (!_attackHit && Target && FlatDistance(transform.position, Target.transform.position) < RamHitRadius
                && Mathf.Abs(Target.transform.position.y - transform.position.y) < VerticalReach)
            {
                _attackHit = true;
                DamageTarget(RamDamage, _ramDirection, RamKnockback);
                CameraFeel.Shake(RamShake);
                Sound.Play(RamImpactSound, transform.position);

                // Acertou o player: segue só mais um pouco e desacelera.
                EndCharge(false);
                return;
            }

            bool tooFar = FlatDistance(transform.position, _ramOrigin) >= RamMaxDistance;
            bool tooLong = Time.time - _ramChargeStart > RamMaxTime;

            if (tooFar || tooLong)
                EndCharge(false);
        }

        private void EndCharge(bool hitObstacle)
        {
            _movement.Acceleration = _baseAcceleration;
            _movement.Speed = _baseSpeed;

            IsStunned = hitObstacle;
            _stunTime = RamStunTime;

            if (hitObstacle)
            {
                CameraFeel.Shake(RamShake);
                Sound.Play(RamImpactSound, transform.position);
                Stunned?.Invoke();
                _log.Log("Rammed a wall, stunned");
            }

            SetPhase(AttackPhase.Recover);
        }

        // ---------------------------------------------------------------- Recover (saiu do seco)

        // Acabou na água (empurrado, caiu de um barranco): volta ao último ponto seco sem desviar de nada.
        private void UpdateRecover()
        {
            Vector3 direction = FlatDirectionTo(_lastSafePosition);

            _movement.Speed = ChaseSpeed * 0.6f;
            _movement.Move(new Vector2(direction.x, direction.z));

            _dryTime = IsDryAt(transform.position) ? _dryTime + Time.fixedDeltaTime : 0f;

            bool safe = _dryTime > 0.5f;

            // Nunca achou terra seca (nasceu na beira): desiste de voltar e deixa de checar por um tempo.
            bool gaveUp = Time.time - _stateStart > 6f;

            if (safe || gaveUp)
            {
                _nextSafeCheck = Time.time + (gaveUp ? 5f : 0.25f);
                _notDryTime = 0f;
                SetState(State.Patrol);
            }
        }

        private void UpdateWaterSafety()
        {
            if (CurrentState is State.Dead or State.Recover || !_controller.enabled || Time.time < _nextSafeCheck)
                return;

            _nextSafeCheck = Time.time + 0.25f;

            if (!_movement.IsGrounded)
                return;

            if (IsDryAt(transform.position))
            {
                _lastSafePosition = transform.position;
                _notDryTime = 0f;
                return;
            }

            _notDryTime += 0.25f;

            if (_notDryTime >= WaterEscapeTime)
            {
                _log.Log("In the water, going back to dry land");
                _dryTime = 0f;
                SetState(State.Recover);
            }
        }

        // ---------------------------------------------------------------- Steering

        private enum PathResult { Clear, Water, Obstacle }

        // Anda em direção a um ponto sem pisar na água. distance limita o quanto à frente ele testa.
        private void MoveTowards(Vector3 point, float speed, float distance)
        {
            Vector3 desired = FlatDirectionTo(point);

            _movement.Speed = speed;

            if (desired == Vector3.zero)
            {
                _movement.Move(Vector2.zero);
                _steerBlocked = false;
                return;
            }

            float lookAhead = Mathf.Min(Mathf.Max(LookAhead, speed * LookAheadTime), Mathf.Max(distance, 0.5f));

            if (Time.time >= _nextSteerTime)
                ReevaluateSteer(desired, lookAhead);

            if (_steerBlocked)
            {
                // Sem caminho seguro: para e encara o ponto.
                _movement.Move(Vector2.zero);
                FaceDirection(desired, TurnSpeed);
                _blockedTime += Time.fixedDeltaTime;
                return;
            }

            _blockedTime = 0f;

            Vector3 direction = Quaternion.AngleAxis(_steerOffset, Vector3.up) * desired;
            _movement.Move(new Vector2(direction.x, direction.z));
        }

        private void ReevaluateSteer(Vector3 desired, float lookAhead)
        {
            _nextSteerTime = Time.time + SteerInterval;
            _steerBlocked = false;

            if (CheckPath(desired, lookAhead) == PathResult.Clear)
            {
                _steerOffset = 0f;
                return;
            }

            // Tenta ângulos cada vez maiores, preferindo o lado que já estava contornando.
            foreach (float angle in SteerAngles)
            {
                float preferred = angle * _steerSign;

                if (CheckPath(Quaternion.AngleAxis(preferred, Vector3.up) * desired, lookAhead) == PathResult.Clear)
                {
                    _steerOffset = preferred;
                    return;
                }

                if (CheckPath(Quaternion.AngleAxis(-preferred, Vector3.up) * desired, lookAhead) == PathResult.Clear)
                {
                    _steerOffset = -preferred;
                    _steerSign = -_steerSign;
                    return;
                }
            }

            _steerBlocked = true;
        }

        // Testa o chão em passos ao longo da direção (seco? sem barranco?) e depois procura paredes.
        private PathResult CheckPath(Vector3 direction, float distance)
        {
            Vector3 origin = transform.position;
            float previousY = origin.y;
            float step = Mathf.Max(0.25f, PathStep);

            for (float walked = step; ; walked += step)
            {
                float d = Mathf.Min(walked, distance);
                Vector3 point = origin + direction * d;

                if (!TryGetGround(point, out float groundY) || groundY < SeaLevel + MinGroundAboveWater)
                    return PathResult.Water;

                if (previousY - groundY > MaxStepDrop)
                    return PathResult.Water;

                previousY = groundY;

                if (d >= distance)
                    break;
            }

            int count = Physics.SphereCastNonAlloc(origin + Vector3.up * ObstacleHeight, ObstacleRadius, direction,
                _hits, distance, ObstacleMask, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];

                if (IsIgnored(hit.collider) || hit.normal.y >= 0.7f || hit.distance <= 0f)
                    continue;

                nearest = Mathf.Min(nearest, hit.distance);
            }

            return nearest < float.MaxValue ? PathResult.Obstacle : PathResult.Clear;
        }

        // Ele é você mesmo, o player e o oceano não bloqueiam o caminho nem contam como chão.
        private bool IsIgnored(Collider other)
        {
            if (other.transform.IsChildOf(transform))
                return true;

            if (Target && other.transform.IsChildOf(Target.transform))
                return true;

            return other.GetComponentInParent<SpectralOceanFFT>() != null;
        }

        private bool TryGetGround(Vector3 point, out float groundY)
        {
            Vector3 origin = point + Vector3.up * GroundProbeHeight;

            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, GroundProbeHeight + GroundProbeDepth,
                GroundMask, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            groundY = 0f;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];

                if (hit.distance >= nearest || IsIgnored(hit.collider))
                    continue;

                nearest = hit.distance;
                groundY = hit.point.y;
            }

            return nearest < float.MaxValue;
        }

        private bool IsDryAt(Vector3 point)
        {
            return TryGetGround(point, out float groundY) && groundY >= SeaLevel + MinGroundAboveWater;
        }

        // O salto precisa de terra seca onde o player está (ou perto dele).
        private bool CanLandAt(Vector3 point)
        {
            return IsDryAt(point) && CheckPath(FlatDirectionTo(point), Mathf.Min(FlatDistance(transform.position, point), JumpMaxRange)) == PathResult.Clear;
        }

        // A investida precisa de pista inteira seca e livre até o alvo.
        private bool IsRunwayClear(Vector3 point)
        {
            float distance = Mathf.Min(FlatDistance(transform.position, point), RamMaxDistance);

            return CheckPath(FlatDirectionTo(point), distance) == PathResult.Clear;
        }

        // ---------------------------------------------------------------- Perception

        private bool CanSeeTarget(float radius)
        {
            if (Target == null || CutsceneComponent.AnyPlaying || Time.time < _ignoreTargetUntil)
                return false;

            if (Target.Health && Target.Health.IsDead)
                return false;

            // Nadando ou no barco o player está fora do alcance: o urso não vai pra água.
            if (Target.IsAboardBoat || Target.ActiveMovement is not GroundedMovement)
                return false;

            return FlatDistance(transform.position, Target.transform.position) < radius * Aggression(MaxDecayDetection);
        }

        // Nota o player pela visão (cone + linha de visão) ou por perto demais (ouvido).
        private bool CanNoticeTarget()
        {
            if (!CanSeeTarget(DetectionRadius))
                return false;

            Vector3 toTarget = FlatDirectionTo(Target.transform.position);
            float distance = FlatDistance(transform.position, Target.transform.position);

            bool heard = distance < HearingRadius;
            bool inCone = toTarget != Vector3.zero && Vector3.Angle(transform.forward, toTarget) <= ViewAngle * 0.5f;

            if (!heard && !inCone)
                return false;

            if (RequireLineOfSight && !HasLineOfSight())
                return false;

            return true;
        }

        private bool HasLineOfSight()
        {
            Vector3 eye = transform.position + Vector3.up * EyeHeight;
            Vector3 goal = Target.transform.position + Vector3.up;
            Vector3 delta = goal - eye;
            float distance = delta.magnitude;

            if (distance < 0.1f)
                return true;

            int count = Physics.RaycastNonAlloc(eye, delta / distance, _hits, distance, SightMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (!IsIgnored(_hits[i].collider))
                    return false;
            }

            return true;
        }

        // Quanto mais o mundo decai, mais longe ele enxerga, mais rápido persegue e menos espera entre ataques.
        private float Aggression(float maxDecayMultiplier)
        {
            WorldStateController world = WorldStateController.Instance;

            if (world == null)
                return 1f;

            return Mathf.Lerp(1f, maxDecayMultiplier, world.Decay / (float)WorldStateController.DecayMaximum);
        }

        // ---------------------------------------------------------------- Combat helpers

        private bool CanHitTarget(float range, float arc)
        {
            if (Target == null || Target.Health == null || Target.Health.IsDead)
                return false;

            Vector3 position = Target.transform.position;

            if (FlatDistance(transform.position, position) > range || Mathf.Abs(position.y - transform.position.y) > VerticalReach)
                return false;

            Vector3 direction = FlatDirectionTo(position);

            return direction == Vector3.zero || Vector3.Angle(transform.forward, direction) <= arc * 0.5f;
        }

        private void DamageTarget(float damage, Vector3 direction, float knockback)
        {
            if (Target == null || Target.Health == null || Target.Health.IsDead)
                return;

            Target.Health.TakeDamage(damage);

            if (knockback > 0f && Target.ActiveMovement is GroundedMovement grounded)
                grounded.SetPlanarVelocity(direction * knockback);

            _log.Log($"Hit the player for {damage}");
        }

        private void OnDamaged(float amount)
        {
            if (IsDead)
                return;

            Sound.Play(HurtSound, transform.position);
            Staggered?.Invoke();

            // Levou um golpe sem ter notado o player: vai direto pra cima dele.
            if (CurrentState == State.Patrol && Target && CanSeeTarget(GiveUpRadius))
                SetState(State.Alert);
        }

        private void OnDied()
        {
            Sound.Play(DeathSound, transform.position);
            SetState(State.Dead);
        }

        // ---------------------------------------------------------------- State

        private void SetState(State state)
        {
            // Qualquer ataque cancelado devolve os valores que o ataque tinha mexido no movimento.
            if (CurrentState is State.Ram or State.Jump)
            {
                _movement.Acceleration = _baseAcceleration;
                _movement.Speed = _baseSpeed;
            }

            CurrentState = state;
            Phase = AttackPhase.Windup;
            IsStunned = false;
            _stateStart = Time.time;
            _phaseStart = Time.time;
            _steerOffset = 0f;
            _nextSteerTime = 0f;
            _steerBlocked = false;

            switch (state)
            {
                case State.Patrol:
                    _movement.Speed = PatrolSpeed;
                    _lostSightTime = 0f;
                    _blockedTime = 0f;
                    _idleUntil = Time.time + Random.Range(IdleTime.x, IdleTime.y) * 0.5f;
                    PickPatrolPoint();
                    break;

                case State.Alert:
                    _movement.Speed = _baseSpeed;
                    _lostSightTime = 0f;
                    _blockedTime = 0f;
                    _lastKnownPosition = Target ? Target.transform.position : transform.position;
                    Sound.Play(RoarSound, transform.position);
                    Alerted?.Invoke();
                    break;

                case State.Chase:
                    _movement.Speed = ChaseSpeed;
                    break;

                case State.Claw:
                    ClawVariant = (ClawVariant + 1) % Mathf.Max(1, ClawVariants);
                    AttackStarted?.Invoke(BearAttack.Claw);
                    break;

                case State.Jump:
                    _nextSpecialTime = Time.time + SpecialGap;
                    AttackStarted?.Invoke(BearAttack.Jump);
                    break;

                case State.Ram:
                    _nextSpecialTime = Time.time + SpecialGap;
                    AttackStarted?.Invoke(BearAttack.Ram);
                    break;

                case State.Recover:
                    _movement.Speed = ChaseSpeed * 0.6f;
                    break;
            }

            _log.Log($"State: {state}");
        }

        private void SetPhase(AttackPhase phase)
        {
            Phase = phase;
            _phaseStart = Time.time;
        }

        // Termina o ataque: volta a perseguir se o player ainda está ao alcance, senão patrulha.
        private void EndAttack()
        {
            _movement.Speed = ChaseSpeed;
            _movement.Acceleration = _baseAcceleration;

            SetState(CanSeeTarget(GiveUpRadius) ? State.Chase : State.Patrol);
        }

        // ---------------------------------------------------------------- Utility

        private void FaceDirection(Vector3 direction, float degreesPerSecond)
        {
            if (direction == Vector3.zero)
                return;

            Quaternion goal = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, goal, degreesPerSecond * Time.fixedDeltaTime);
        }

        private Vector3 FlatDirectionTo(Vector3 point)
        {
            Vector3 direction = point - transform.position;
            direction.y = 0f;

            return direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.zero;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;

            return Vector3.Distance(a, b);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 home = Application.isPlaying ? _home : transform.position;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(home, PatrolRadius);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, DetectionRadius);

            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(transform.position, HearingRadius);

            Vector3 left = Quaternion.AngleAxis(-ViewAngle * 0.5f, Vector3.up) * transform.forward;
            Vector3 right = Quaternion.AngleAxis(ViewAngle * 0.5f, Vector3.up) * transform.forward;
            Gizmos.DrawLine(transform.position, transform.position + left * DetectionRadius);
            Gizmos.DrawLine(transform.position, transform.position + right * DetectionRadius);

            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, ClawRange);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, JumpMinRange);
            Gizmos.DrawWireSphere(transform.position, JumpMaxRange);

            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.position, RamMinRange);
            Gizmos.DrawWireSphere(transform.position, RamMaxRange);
        }
    }
}
