# Walking Dragon / Leviathan — Unity 6000.5.6f1

This revision adds a Bear-style animation/audio adapter for the colossal Walking Dragon. Gameplay remains in `WalkingDragonController`; animation parameters, animation triggers, footsteps, and long-range 3D one-shot sounds live in `WalkingDragonAnimatableComponent`.

## Package contents

- `WalkingDragon/WalkingDragonController.cs` — boss AI, orbiting, fireballs, roar/jump/charge boat ram, seafloor grounding, and gameplay animation events.
- `WalkingDragon/WalkingDragonFireballProjectile.cs` and `WalkingDragonFireball.prefab` — fireball projectile, direct boat impact, explosion and damage behavior.
- `WalkingDragon/WalkingDragonAnimatableComponent.cs` — Bear-style event subscription, Animator parameter updates, timed footsteps, and the four configurable distant sound cues.
- `Editor/WalkingDragonAnimatorBuilder.cs` — Unity Editor tool that generates a complete Animator Controller from the included FBX clips.
- `Animations/DragonSoulEater/` — the 17 supplied DragonSoulEater FBX animation clips and their `.meta` importer settings.

The package does not contain audio clips. Assign your own footstep, roar, ram-hit, and fireball-throw AudioClips in the animatable component's Inspector.

## Installation

Copy the folders into these project locations, keeping the `.meta` files included in the package:

| Package folder | Destination |
|---|---|
| `WalkingDragon/` contents | `Assets/_Project/Features/Leviathan/` |
| `Animations/` | `Assets/_Project/Features/Leviathan/Animations/` |
| `Editor/` | `Assets/_Project/Features/Leviathan/Editor/` |

When replacing `WalkingDragonController.cs` in an existing project, keep the existing `WalkingDragonController.cs.meta` file so the script GUID used by your scene/prefabs does not change. Keep the included `.meta` files for the new animatable component, Editor builder, and FBX assets.

The project must already contain the `Assets._Project.Features.Audio.Sound` class matching the `Sound.cs` overload supplied with the task:

```csharp
Sound.Play(AudioClip clip, Vector3 position, float volume, float minDistance, float maxDistance)
```

`WalkingDragonAnimatableComponent` uses that exact overload for 3D linear distance attenuation. It does not create or replace `Sound.cs`.

## Generate the Animator Controller

The animation FBXs are Generic-rig clips. The controller asset is generated inside the Unity Editor after the FBXs have imported, so its motions reference the actual imported clip sub-assets rather than guessed asset IDs.

1. Let Unity finish importing `Animations/DragonSoulEater/`.
2. From the Unity menu, select **Tools → Walking Dragon → Create-Refresh Animator Controller**.
3. Confirm replacement if `WalkingDragonAnimator.controller` already exists.
4. The generated controller is saved at `Assets/_Project/Features/Leviathan/WalkingDragonAnimator.controller`.
5. Select the dragon model's `Animator` component and assign this controller. Assign the matching Generic Avatar from the dragon model if the model importer/Animator exposes one.
6. Add `WalkingDragonAnimatableComponent` to the same root GameObject as `WalkingDragonController` (or assign the references manually). Assign the model's child `Animator` if automatic child lookup does not find the correct one.

The component disables Animator root motion by default because `SwimmingMovement` and `WalkingDragonController` own all world movement. Keep **Disable Animator Root Motion** enabled unless you deliberately change the boss movement architecture.

### Generated states and clip mapping

| Animator state | Included FBX clip | Purpose |
|---|---|---|
| Ground Locomotion blend tree | Idle / Walk / Run | Normal movement using `Speed` in metres per second |
| Roar | Scream | Ram telegraph roar |
| Fireball | Fireball Shoot | Grounded fireball throw |
| Fly Fireball | Fly Fireball Shoot | Fireball throw during the ram leap |
| Ram Take Off | Take Off | Leap start |
| Ram Flight | Fly Forward | Forward flight during the jump |
| Ram Charge | Run | Ground-level charge loop |
| Ram Impact | Basic Attack | Hull impact / bite-like attack pose |
| Land | Land | Landing after an interrupted or completed leap |
| Defend | Defend | Optional manually triggered pose |
| Tail Attack | Tail Attack | Optional manually triggered attack |
| Get Hit | Get Hit | Optional manually triggered reaction |
| Die | Die | Optional manual state; the boss AI itself remains unkillable |
| Sleeping | Sleeping | Optional loop, exited with `Wake` |
| Fly Float / Fly Glide | Fly Float / Fly Glide | Optional manually triggered flight poses |

`Walk`, `Run`, `Idle`, and the sustained flight/sleep clips remain looping. One-shot attack, roar, jump, landing, hit, and death clips are configured not to loop in their FBX importer metadata.

## Animator parameters

The builder creates these parameters, and `WalkingDragonAnimatableComponent` checks that each parameter exists with the correct type before writing to it.

| Parameter | Type | Driven by |
|---|---|---|
| `Speed` | Float | `SwimmingMovement.PlanarVelocity.magnitude` |
| `VerticalSpeed` | Float | Dragon Rigidbody world-Y velocity |
| `Grounded` | Bool | False during the ram jump |
| `Jumping` | Bool | Rising portion of the ram jump |
| `Falling` | Bool | Descending portion of the ram jump |
| `RamAirborne` | Bool | `WalkingDragonController.IsRamJumping` |
| `Charging` | Bool | `WalkingDragonController.IsRamCharging` |
| `Roar` | Trigger | Ram roar begins |
| `Fireball` | Trigger | Grounded fireball wind-up begins |
| `FlyFireball` | Trigger | Fireball wind-up begins during ram jump |
| `RamJump` | Trigger | Ram leap begins |
| `RamCharge` | Trigger | Ram leap ends and charge begins |
| `RamImpact` | Trigger | Ram physically contacts the boat |
| `Land` | Trigger | Ram leap is interrupted before charge |
| `Defend`, `TailAttack`, `Hit`, `Die`, `Sleep`, `Wake`, `FlyFloat`, `FlyGlide` | Trigger | Optional public hooks on the animatable component |

The component exposes `PlayDefend()`, `PlayTailAttack()`, `PlayHitReaction()`, `PlayDeath()`, `PlaySleep()`, `PlayWake()`, `PlayFlyFloat()`, and `PlayFlyGlide()` for cutscene or gameplay scripts.

## Long-range audio setup

All four sounds use the project's `Sound.Play(clip, position, volume, minDistance, maxDistance)` method. Every cue has its own Enabled toggle, AudioClip, Volume, Min Distance, and Max Distance controls. Distances are in Unity world units (normally metres); the supplied `Sound.cs` uses 3D spatial audio with linear rolloff.

| Component Inspector cue | Event | Default volume | Min distance | Max distance |
|---|---|---:|---:|---:|
| Footstep Sound | Timed from the looping locomotion / ram-charge animation | 0.9 | 80 | 1800 |
| Roar Sound | Roar telegraph starts before a boat ram | 1.0 | 120 | 3500 |
| Ram Hit Sound | The boat hull is successfully contacted | 1.0 | 100 | 3200 |
| Fireball Throw Sound | The fireball projectile is actually released | 1.0 | 70 | 2800 |

Assign one clip to each cue and tune its enabled state, range, and volume independently. Empty clips are safely ignored by the supplied `Sound.Play` implementation. These are deliberately large default distances so the boss can be heard across a broad part of the level; adjust them to your world's scale and mix.

Footsteps do not require Animation Events in the FBX files. The component reads the Animator's looping `Ground Locomotion` or `Ram Charge` normalized time and plays the same footstep cue at the two configurable phases (`0.12` and `0.62`). `Footstep Min Movement Speed` prevents footsteps while idle. Assign a static bottom-of-feet `GroundContactReference` on the controller to place footsteps close to the seabed; otherwise the dragon root is used.

The roar sound is placed at the dragon visual/root position, the fireball throw sound at the fireball muzzle, and the ram-hit sound at the actual hull contact point.

## Gameplay event / ownership split

`WalkingDragonController` raises these C# events and does not write Animator parameters directly:

- `RoarStarted`
- `FireballWindupStarted`
- `FireballLaunched(Vector3 spawnPosition)`
- `RamJumpStarted`
- `RamChargeStarted`
- `RamImpact(Vector3 hullContactPosition)`

`WalkingDragonAnimatableComponent` subscribes/unsubscribes to those events like `BearAnimatableComponent`. The controller retains AI, movement and attack ownership; the component owns animation and audio feedback. Explosion settings continue to belong only to `WalkingDragonFireballProjectile`.

## Existing boss setup

Keep the existing installation and tuning for `WalkingDragonController`, the fireball prefab, boat ram, shark reinforcement, deep-water pathing, and seabed grounding. The animation/audio addition does not restore wave correction, change ground contact, or change fireball damage ownership.

## Validation notes

The source was reviewed against the supplied Bear animation pattern, `SwimmingMovement` public properties, the supplied `Sound.cs` signature, and the FBX importer metadata. The package cannot be honestly described as compiled or play-tested inside the full game because the complete Unity project, dragon scene/prefab, and audio clips were not included. After importing, generate the controller with the menu item above, assign the Animator, then test locomotion blending, ram timing, and audio range in the target scene.
