# Player animation setup

`PlayerAnimationController` is the bridge between the movement modes and a Unity Animator. It is included on the Player prefab and automatically finds an Animator on the Player or one of its children. The uploaded prefab does not contain a Humanoid model/Animator yet, so the Animator reference remains optional until the visual rig is added.

## Animator parameters

The defaults below are optional. The bridge checks which parameters exist before writing to them, so you can omit parameters or rename them in the component inspector.

| Parameter | Type | Purpose |
|---|---|---|
| `MovementMode` | Int | `0` grounded, `1` swimming, `2` boat |
| `MoveSpeed` | Float | Normalized planar speed (0–1), suitable for locomotion blend trees |
| `MoveX`, `MoveY` | Float | Local planar movement direction for strafing/blend trees |
| `VerticalSpeed` | Float | Vertical movement speed in metres/second |
| `Grounded` | Bool | CharacterController is grounded in grounded mode |
| `Jumping` | Bool | Moving upward while airborne |
| `Falling` | Bool | Moving downward while airborne |
| `Swimming` | Bool | Swimming movement is active |
| `InBoat` | Bool | Boat movement is active |
| `Jump` | Trigger | Entering the rising part of a jump or swim hop |
| `Land` | Trigger | Airborne movement has ended |
| `SwimHop` | Trigger | Optional swim-hop-specific transition |
| `SwimLand` | Trigger | Optional swim-landing-specific transition |

Suggested Animator organization:

- Grounded locomotion: a 1D/2D blend tree driven by `MoveSpeed` or `MoveX`/`MoveY`.
- Jump/fall/landing: transitions driven by `Jumping`, `Falling`, and `Land`.
- Swimming: a swimming sub-state machine selected by `Swimming`, with surface locomotion and optional `SwimHop`/`SwimLand` transitions.
- Boat: a boat sub-state machine selected by `InBoat`, using `MoveSpeed` for idle/rowing/sailing blends as appropriate.

The script does not create clips, transitions, or an Animator Controller: those remain project-specific assets. Because the movement motors move the CharacterController/boat directly, start with in-place clips; enable root motion only after explicitly integrating it with the movement motors.

## Foot placement IK

1. Use a Humanoid Avatar on the character Animator.
2. Add `PlayerFootPlacementIK` to the **same GameObject as the Animator**.
3. Enable **IK Pass** on the Animator layer that evaluates the character pose.
4. Set `Ground Layers` to terrain and walkable geometry. Exclude the Player layer to avoid self-hits.
5. Tune raycast start height/distance, foot offset, maximum correction, and blend speed for the rig scale.

IK is blended in while `GroundedMovement` is active and the CharacterController is grounded. That covers grounded walking and the landing pose; it blends out for swimming and boat movement. The component uses Humanoid left/right foot bones and aligns foot rotation to the hit surface normal. It intentionally keeps animated foot X/Z positions and corrects height only, avoiding aggressive snapping at ledges.
