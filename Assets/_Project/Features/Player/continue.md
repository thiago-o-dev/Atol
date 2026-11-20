# Continue plan — Hulda fall timing and wave-ejection states

## Baseline

This package is based directly on the user's `Player_Animation_Updated(1).zip`, not on the later immediate-landing / fall-threshold iterations. Existing animation paths, idle/walk clips, boat animations, and prefab/controller references were retained.

## Work applied

1. **Grounded fall qualification (`PlayerAnimationController.cs`)**
   - Start counting only when the player is not grounded and `CharacterController.velocity.y` is descending past `minimumFallingSpeed`.
   - `Falling` becomes true after `fallDetectionDelay` (default 0.2 seconds).
   - Small drops and tiny grounding flickers never set the `Land` trigger.
   - A qualified landing sets `FallDuration` immediately on the contact frame, then fires `Land`.
   - `FallDuration` is the measured descending duration in seconds, clamped to `maximumFallDurationForBlend` (default 1.5 seconds).

2. **Animator Controller (`HuldaAnimatorController.controller`)
   - Added float parameter `FallDuration`.
   - Added grounded locomotion and reverse-walk transitions into `Air Falling` after the script qualifies a descent.
   - Changed jump fall crossfade to 0.12 seconds after the 0.2-second qualification.
   - Changed short-jump recovery to return to grounded locomotion, not the landing clip.
   - Ground landing is now gated by the one-shot `Land` trigger, rather than the raw `Grounded` bool.
   - Added direct qualified-landing transitions from grounded idle/walk, reverse walk, and Jumping Up so the trigger is not lost if contact happens on the transition frame.
   - Renamed the existing swimming descent state to `Swim Falling` and added a direct transition from `Swimming Locomotion`.
   - Added a 1D `Landing Strength Blend (FallDuration)` tree. At 0.2s it uses the `Air Falling` pose; at 1.5s it reaches the supplied `Falling To Landing` clip.

3. **Wave ejection (`Movement/SwimmingMovement.cs` + `PlayerController.cs`)
   - Added `IsWaveAirborne` without changing the existing meaning of `IsAirborne` (deliberate swim hop only).
   - A wave ejection is recognized after the body centre remains 0.2m above the sampled surface for 0.08s. Re-entry has a 0.05m hysteresis margin.
   - `PlayerController` no longer exits swimming only because the sampled water height falls below the body midpoint. It now also requires an enabled `CharacterController` reporting `isGrounded`.
   - While wave-ejected, the player remains on `SwimmingMovement`; `PlayerAnimationController` sets `Falling` and the Animator enters `Swim Falling`.
   - When the body returns to the water, `SwimLand` returns the Animator to swimming locomotion. If the player reaches dry ground, the existing leave-swimming transition applies after ground contact.

4. **Prefab and documentation**
   - Serialized the new detection/blend values in `Player.prefab`.
   - Updated `ANIMATION_SETUP.md` with the timing rules and test plan.
   - Included `Movement/SwimmingMovement.cs` with its original meta GUID so the new wave-airborne flag is available in the project.

## Unity 6000.5.6f validation still required

1. Import the package into the same project as the other movement features. Confirm the `SwimmingMovement.cs` meta GUID resolves to the existing component rather than creating a duplicate script asset.
2. Confirm the `Player.prefab` has the expected `PlayerController`, `SwimmingMovement`, `CharacterController`, and `PlayerAnimationController` references.
3. In Play Mode, walk off a very low ledge (<0.2s descending): there should be no `Air Falling` or landing animation.
4. Walk off a larger ledge: `Falling` should become true after roughly 0.2s of descent. Confirm landing immediately fires `Land` and sets `FallDuration` between 0.2 and 1.5.
5. Make a short jump: `Jumping Up` should return to locomotion without playing `Falling To Landing`.
6. Make a longer jump: `Jumping Up` → `Air Falling` → `Falling To Landing`. Inspect the blend tree: longer descents should bias more strongly toward the landing clip.
7. Swim through normal waves: no unwanted grounded-mode switch or false `Swim Falling` trigger.
8. Use a strong wave to lift the character clear of the water: active movement must remain `SwimmingMovement`, and the Animator should enter `Swim Falling`.
9. Re-enter the water: `SwimLand` should return to swimming locomotion.
10. Reach dry terrain after wave ejection: grounded movement should activate only after `CharacterController.isGrounded` becomes true and the water sample is below exit hysteresis.

## Notes / limitations

- Unity Editor and Play Mode were not available for this static asset edit. C# compilation, FBX import, and live animation timing remain unverified.
- The supplied archive contains only one grounded landing clip. The blend tree temporarily blends from `Air Falling` (weak end) to `Falling To Landing` (strong end). When a dedicated soft-landing clip exists, replace the low-end child with it for a more natural result.
- The 0.2-second qualification and wave-ejection thresholds are serialized in the Player prefab and can be tuned after real gameplay testing.
