# Hulda animation setup

`PlayerAnimationController` drives `HuldaAnimatorController` from the active movement motor. It reads planar movement in player-local space and supplies the Animator with smoothed `Speed` and `Direction` values. The movement components remain responsible for physics and displacement.

## Ground fall qualification

Grounded falling is intentionally delayed until the player has been **continuously descending for 0.2 seconds**. The timer starts only while the CharacterController reports airborne and its vertical velocity is below the configured negative-speed threshold. Upward jump motion and one-frame grounding flicker do not qualify as a fall.

When the player touches ground:

- A fall shorter than the 0.2-second qualification window returns directly to grounded locomotion. Short jumps can leave `Jumping Up` shortly after take-off, without playing a landing animation.
- A qualified fall sets the `Land` trigger and writes the measured descending duration to `FallDuration` before the Animator evaluates the transition.
- `FallDuration` is measured in seconds and capped at 1.5 seconds by default.

The `Falling To Landing` state's 1D blend tree maps `FallDuration` from 0.2 to 1.5 seconds. With the clips supplied in this archive, it blends from the `Air Falling` pose at the low end toward the `Falling To Landing` clip at the high end. This creates a weak-to-strong landing pose range without inventing extra animation assets. When a dedicated soft-landing clip becomes available, replace the low-end `Air Falling` child with that clip for a more natural result.

## Wave-driven swimming falls

`SwimmingMovement` exposes `IsWaveAirborne` separately from a deliberate swim hop. A wave ejection is recognized when the swimmer's body centre stays more than 0.2 metres above the sampled water surface for 0.08 seconds. A lower re-entry threshold (0.05 metres above the surface) provides hysteresis so the state does not flicker as the character skims a wave crest.

While wave-ejected:

- `PlayerController` keeps `SwimmingMovement` active, even if the sampled surface is below the body midpoint.
- The Animator's `Falling` flag transitions from `Swimming Locomotion` into the state named `Swim Falling`.
- The character returns to grounded movement only when the water is below the configured exit hysteresis **and** the CharacterController confirms solid-ground contact.
- Re-entry into the water uses `SwimLand` to return to swimming locomotion.

A deliberate swimming hop continues to use `Swimming Hop` followed by `Swim Falling` and `SwimLand` on water re-entry.

## Relevant Animator parameters

| Parameter | Type | Purpose |
|---|---|---|
| `Speed` | Float | Normalized planar speed for grounded/swimming blend trees |
| `Direction` | Float | Local sideways direction for grounded locomotion |
| `MovementMode` | Int | `0` grounded, `1` swimming, `2` boat |
| `VerticalSpeed` | Float | Vertical velocity in metres per second |
| `Grounded` | Bool | CharacterController grounding |
| `Jumping` | Bool | Rising during a grounded jump or swim hop |
| `Falling` | Bool | Qualified grounded descent or wave/hop airborne state in swimming |
| `FallDuration` | Float | Descending duration in seconds, capped at 1.5; drives the landing blend tree |
| `Swimming` | Bool | Swimming movement is active |
| `InBoat` | Bool | Boat movement is active |
| `Backward` | Bool | Selects the reverse-walk clip |
| `Jump`, `Land`, `SwimHop`, `SwimLand`, `BoatDodge` | Trigger | One-shot animation events |

## States and blend trees

- `Grounded Locomotion`: existing idle and directional walking blend.
- `Unarmed Walk Back`: reverse-walk clip; can transition to `Air Falling` after the same 0.2-second qualification.
- `Jumping Up`: transitions to `Air Falling` only after a qualified descent. Short jumps recover directly to grounded locomotion.
- `Air Falling`: entered from grounded locomotion or jump once the descent is qualified.
- `Falling To Landing`: uses the `Landing Strength Blend (FallDuration)` 1D blend tree.
- `Swimming Locomotion`: transitions to `Swim Falling` when the wave-ejection detector reports that the body has cleared the waterline.
- `Swimming Hop`: deliberate out-of-water hop.
- `Swim Falling`: shared swim-hop descent and wave-ejection state. It returns to swim locomotion on `SwimLand`, or leaves swimming when the PlayerController has confirmed dry-ground contact.
- `Boat Sitting Idle` and `Boat Fast Dodge`: unchanged from the supplied baseline.

The Animator's Base Layer has IK Pass enabled. Keep `PlayerFootPlacementIK` on the Humanoid Animator object; its foot corrections should remain enabled only for grounded walking/landing and blended out for swimming and boat sitting.

## Unity validation

After importing in Unity 6000.5.6f, test these cases in Play Mode:

1. Walk off a low step: no falling/landing animation if the downward phase lasts less than 0.2 seconds.
2. Walk off a larger ledge: transition to `Air Falling` after approximately 0.2 seconds of continuous descent and land with `FallDuration` reflecting the measured descent, capped at 1.5.
3. Make a normal jump: `Jumping Up` plays during ascent; a sufficiently long descent enters `Air Falling`; landing uses the blend tree. A small hop returns to locomotion without a landing animation.
4. Swim through ordinary waves: no unwanted movement-mode switch or swim-falling flicker.
5. Get lifted clear of the water by a strong wave: stay on `SwimmingMovement` and enter `Swim Falling`; do not return to grounded until `CharacterController.isGrounded` is true and the surface is below the exit hysteresis.
6. Re-enter the water after wave ejection: return to swimming locomotion via `SwimLand`.
7. Land on dry ground after wave ejection: confirm the movement mode changes to grounded only after solid ground contact.

This archive was edited statically; Unity compilation, FBX import, and Play Mode validation still need to be completed in the target project.


### Coyote-time jumps

`GroundedMovement.CoyoteTime` defaults to `0.2` seconds. It is measured from the last
confirmed `CharacterController.isGrounded` contact and can be tuned on the Player prefab.
An accepted normal or coyote jump raises `JumpStarted`; `PlayerAnimationController` clears
stale fall/landing state and sets the `Jump` trigger. `HuldaAnimatorController` has direct
interrupt transitions from `Air Falling` and `Falling To Landing` to `Jumping Up`, so a
late-but-valid coyote jump can immediately take control of the animation.
