# WaterEffects

All visual effects that belong to the water, in one place.

| Folder | What | Entry points |
|---|---|---|
| `Foam/` | Soft contact foam painted ON the water surface | `WaterFoamField`, `WaterContactFoam`, `BoatFoamEmitter`, `Resources/WaterFoamFieldUpdate.shader` |
| `Wake/` | Particle wake behind the boat | `BoatWakeEffect` |
| `Wind/` | World wind streak particles | `WorldWindParticleField`, `WindParticle.shader` |
| `Underwater/` | Camera underwater look | `OceanUnderwaterController`, `OceanUnderwater.shader` |

## How contact foam works
1. An actor says *what touched the water*: `WaterContactFoam.EmitTrail(from, to, intensity, dt)` or `EmitBurst(point, intensity)`.
2. `WaterFoamField` (one per scene, auto-created) keeps a scrolling top-down RenderTexture around the camera, fades it over `Lifetime`, and stamps the requests into it (capsule stamps, screen-blended, max 48 per frame).
3. `Features/Water/Wave.shader` samples it per pixel at the displaced surface XZ (`_WaterFoamField`, `_WaterFoamFieldParams`) and adds lacy noise, so the foam lives on the water surface itself.

## Who uses it
- Boat: `BoatController` auto-adds `BoatFoamEmitter` (toggle `Spawn Contact Foam`). It reads buoyancy probes through `OceanBuoyancy.TryGetProbeContact`, foams the waterline when moving, and splashes on `HullSlammed`.
- Player: `PlayerController` ensures a `PlayerWaterContactFoam` component exists. It samples the waterline independently of the active movement mode and emits foam while the capsule intersects shallow water, even when the player is still walking. `SwimmingMovement` handles swim trails and hop splashes.
- Deep submersion / no contact: player foam is gated by capsule submersion, so it fades out when the capsule is out of the water or almost entirely underwater.
- Water entry: a sustained downward fall above the water creates a compact, strong impact crest followed by a larger, much softer / blurrier ring. Ripple size is scaled by descending duration.
- Anything else: add `WaterContactFoam` and call its public emission methods.

## Tuning
- Fade time / visibility: `WaterFoamField > Lifetime`, `Intensity` (0 turns it off).
- Size and softness: `WaterContactFoam` trail/burst radius and strength.
- Colour: the water material's existing `Foam Color`.
- Cost: one 1024 half-float pass only while foam is alive; idle = zero GPU work.

Build note: the update shader sits in a `Resources` folder so it is never stripped.


## Ripple rendering notes

`Wave.shader` evaluates each ripple as a damped, radially spreading Gaussian wave packet. The radial support is feathered to zero before the shader early-out, avoiding a hard rectangular or abruptly clipped boundary. `WaterFoamField.EmitRipple` exposes `widthScale`: narrow values make a crisp impact ring; large values create a wide, low-frequency, blurred packet. `WaterContactFoam.EmitImpactRipplePair` emits the close strong crest and the broad weak follow-up ring.

For the research basis and Asset Store comparison, see [`WaterRippleResearch.md`](WaterRippleResearch.md).


## Player and boat movement wakes

Moving players and boats deposit circular height/vertical-velocity impulses into
`WaterWakeField` at distance intervals that scale with speed. Each source expands as a
radial wave; the two side-source tracks are offset by `tan(19.5°) * distance behind
the actor`, giving a Kelvin-style diverging V, with a weaker transverse pulse near the
actor. The field is simulated in two ping-pong render textures: RG stores height and
vertical velocity, a finite-difference Laplacian propagates the waves, a small blur
reduces grid noise, and frame-rate-correct damping (`0.98` per 60 Hz frame by default)
removes energy over time. `Wave.shader` samples the height for local vertex displacement
and its gradient for shading.

The player uses `PlayerWaterContactFoam` as the single continuous foam/wake owner in
both grounded wading and swimming. Eight independently tracked points around the
CharacterController footprint produce a broader trail similar to the boat hull outline.
`SwimmingMovement.UseExternalPlayerFoam` is set by `PlayerController` to prevent a
second, competing swim trail. Waterline contact fades the trail out when the actor is
above the surface or deeply submerged.
