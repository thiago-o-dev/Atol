# Bear setup

Inimigo terrestre inspirado no tubarão. A IA fica no `BearController`; a animação, no `BearAnimatableComponent`
(mesma ideia do `PlayerAnimationController`: lê a velocidade real e os eventos da IA, não move nada).

## Componentes no prefab

`CharacterController` + `GroundedMovement` + `HealthComponent` + `BearController` + `BearAnimatableComponent`
(o Animator pode estar em um filho). `BearController` exige os três primeiros e liga o `Controller` do
`GroundedMovement` sozinho. O pivô do urso deve ficar nos pés, como o do player.

Ajuste no `GroundedMovement`: `TurnSmoothTime` ~0.2 (urso pesado) e `Acceleration` ~12. Ele não usa NavMesh.

## Estados

`Patrol` (anda entre pontos secos e fuça o chão) -> `Alert` (ruge virado pro player) -> `Chase`, que escolhe:

| Ataque | Quando | O que faz |
|---|---|---|
| Claw | player a menos de `ClawRange` | patada em cone; `ClawVariants` alterna as animações |
| Jump | entre `JumpMinRange` e `JumpMaxRange`, com pouso seco | agacha, mira (com `JumpLead`), salta; acerta no ar ou na pancada do pouso. Pular por cima da pancada escapa |
| Ram | entre `RamMinRange` e `RamMaxRange`, com pista seca e livre | raspa o chão, corre em linha quase reta; parede = atordoado, água/barranco = derrapa e para |

`Recover` só entra se ele acabar na água (empurrado, por exemplo) e o leva de volta ao último ponto seco.

## Água

Cada passo testa o chão à frente com raios: o chão tem que estar `MinGroundAboveWater` acima do nível do mar
(vem do `SpectralOceanFFT` da cena, `FallbackSeaLevel` se não houver), sem queda maior que `MaxStepDrop` e
sem parede (`SphereCast`). Caminho ruim = ele tenta 30, 60, 90 e 135 graus; sem saída, para e encara.
Player nadando ou no barco não é alvo. Se não consegue chegar (player na beira), desiste por `IgnoreTime`.

Layers: `GroundMask` e `ObstacleMask` valem tudo por padrão; tire água, triggers e o próprio urso se der conflito.

## Parâmetros do Animator

| Parâmetro | Tipo | Uso |
|---|---|---|
| `Speed` | Float | 0 parado, ~0.3 andando, 1 correndo (normalizado por `ChaseSpeed`) |
| `VerticalSpeed` | Float | velocidade vertical em m/s |
| `Grounded`, `Jumping`, `Falling` | Bool | mesmo critério do player: queda só conta após `fallDetectionDelay` |
| `Aggro` | Bool | Alert, Chase ou qualquer ataque |
| `Charging` | Bool | fase de corrida da investida (galope) |
| `Stunned` | Bool | bateu na parede na investida |
| `Dead` | Bool | morto |
| `ClawVariant` | Int | 0..ClawVariants-1 |
| `Roar` | Trigger | ao notar o player |
| `Claw` | Trigger | início da patada |
| `JumpWindup` | Trigger | agachar antes do salto |
| `RamWindup` | Trigger | raspar o chão antes da investida |
| `Jump` | Trigger | saída do chão (vem do `GroundedMovement.JumpStarted`) |
| `Land` | Trigger | pouso depois de `minimumAirTimeForLanding` |
| `Hit` | Trigger | levou golpe (só em Patrol, Alert e Chase) |

Parâmetros ausentes são ignorados. Os nomes são campos serializados.

Estados sugeridos: `Locomotion` (blend por `Speed`), `Roar`, `Claw L/R`, `Jump Windup` -> `Jump Up` -> `Air Falling`
-> `Land`, `Ram Windup` -> `Gallop` (while `Charging`) -> `Stunned`, `Hit`, `Death`.
Os tempos de `ClawWindup`, `JumpWindup` e `RamWindup` no `BearController` devem bater com a duração dessas animações.

## Eventos do BearController

`Alerted`, `AttackStarted(BearAttack)`, `AttackStruck(BearAttack)`, `Staggered`, `Stunned`. Tudo é calculado por
distância e cone, sem hitbox: não precisa configurar colliders de ataque.

Não foi compilado nem testado no Unity: confira no Play Mode, principalmente os raios de água na costa da cena.
