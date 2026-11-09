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
- Player: `SwimmingMovement` emits a trail while swimming and bursts on hop take-off/landing (`Emit Foam`, `Foam Full Speed`).
- Anything else: add `WaterContactFoam` and call its two methods.

## Tuning
- Fade time / visibility: `WaterFoamField > Lifetime`, `Intensity` (0 turns it off).
- Size and softness: `WaterContactFoam` trail/burst radius and strength.
- Colour: the water material's existing `Foam Color`.
- Cost: one 1024 half-float pass only while foam is alive; idle = zero GPU work.

Build note: the update shader sits in a `Resources` folder so it is never stripped.
