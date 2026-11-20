# Water Ripples and Interactive Water Shaders — Technical Research

## Scope

This note reviews research and Unity Asset Store examples relevant to the project's FFT ocean, analytic local ripples, and character contact foam. The goal is not to import a third-party water system wholesale: the project already has a custom URP-style `Wave.shader`, a GPU FFT ocean, and a render-texture foam field. The most useful path is to borrow established modelling ideas while preserving the existing ocean and its sampling contract.

## Research papers and technical references

### 1. Schreck, Hafner, and Wojtan (2019) — *Fundamental Solutions for Water Wave Animation*

**Publication:** ACM Transactions on Graphics 38(4) (SIGGRAPH 2019). DOI: [10.1145/3306346.3323002](https://doi.org/10.1145/3306346.3323002).

The paper derives an analytical closed-form treatment for circular water ripples, then extends fundamental-solution ideas to waves interacting with complex obstacles. It specifically targets detailed, high-frequency ripple structure without requiring a uniformly dense simulation grid across an effectively unbounded ocean.

**Relevance here:** this supports treating local impacts as a damped radial wave packet layered over the large-scale FFT ocean. The project's analytic shader is a lightweight approximation rather than an implementation of the paper's full method; its Gaussian envelope, radial attenuation, and continuous spatial fade are chosen to reduce harsh support boundaries and visually abrupt cutoffs. For a later high-fidelity pass, the paper's dispersive closed-form solution is the strongest basis to replace the current approximate carrier.

**Links:** [ISTA publication page](https://visualcomputing.ist.ac.at/publications/2019/FundamentalSolutionForWaterWave/) · [Author-version PDF](https://pub.ista.ac.at/group_wojtan/projects/2019_Schreck_FSfWWA/fsww_author_version.pdf).

### 2. Jeschke and Wojtan (2023) — *Generalizing Shallow Water Simulations with Dispersive Surface Waves*

**Publication:** ACM Transactions on Graphics 42(4), Article 83 (2023). DOI: [10.1145/3592098](https://doi.org/10.1145/3592098).

This method partitions water motion into bulk flow approximated by shallow-water equations and surface waves approximated by Airy wave theory, solving the regimes separately and recombining the velocities. It demonstrates interactions such as boat wakes washing up a beach and dam-break waves with interference and eddies.

**Relevance here:** our current system has a large-scale FFT surface plus localized ripple events and contact foam. The paper is a useful conceptual guide for separating long-scale ocean motion from short-scale local disturbances. It is more expensive and broader in scope than the current gameplay ripple shader, so it is a reference for future water interaction, not a direct drop-in fix.

**Links:** [ISTA record and paper download](https://research-explorer.ista.ac.at/record/14240) · [ISTA publication page](https://visualcomputing.ist.ac.at/publications/2023/GSWSDSW/).

### 3. Jeschke et al. (2018) — *Water Surface Wavelets*

**Publication:** ACM Transactions on Graphics (SIGGRAPH 2018).

The work addresses the trade-off between efficient Fourier-based ocean methods and more expensive approaches that handle moving obstacles, proposing real-time wave interactions with moving obstacles while retaining small-scale detail and large simulation domains.

**Relevance here:** useful background for extending the boat wake and player-impact systems into obstacle-aware wave responses. The existing foam render texture already provides a cheap, persistent interaction mask; a later extension could use localized wavelet impulses for displacement while retaining the foam field for whitewater.

**Link:** [ISTA publication page](https://visualcomputing.ist.ac.at/publications/2018/WSW/).

### 4. Finch — *Effective Water Simulation from Physical Models* (GPU Gems)

This practical rendering chapter combines geometric wave undulation with a dynamic normal map, using sums of physically interpretable sine waves to capture both large-scale surface shape and finer details.

**Relevance here:** supports the current separation between FFT geometry and fragment-stage ripple normal detail. It is particularly pertinent to the decision not to add high-frequency micro-ripples directly to the ocean mesh, where the mesh tessellation could undersample them.

**Link:** [NVIDIA GPU Gems chapter](https://developer.nvidia.com/gpugems/gpugems/part-i-natural-effects/chapter-1-effective-water-simulation-physical-models).

### 5. Mikeš (2015) — *Real-time Water Simulation with Wave Particles on the GPU*

This student paper describes a GPU wave-particle approach for real-time interactive water with rigid-body interaction, including boat and falling-object examples.

**Relevance here:** wave-particle methods are a possible future alternative if local interactions need to affect both surface height and propagation rather than only perturbing the fragment normal. It is a conference/student paper rather than a peer-reviewed journal article, so it should be treated as an implementation reference, not the primary scientific basis.

**Link:** [CESCG paper PDF](https://old.cescg.org/CESCG-2015/papers/Mikes-Real-time_Water_Simulation_with_Wave_Particles_on_the_GPU.pdf).

## Unity Asset Store shader / water systems reviewed

The entries below are comparisons of features and pipeline compatibility as listed by Unity Asset Store pages at the time of review. No third-party code or art has been copied into this project.

| Asset | Relevant listed features | Compatibility / fit |
|---|---|---|
| [Ripple Shader Pack — Real-Time Water Ripples](https://assetstore.unity.com/packages/vfx/shaders/ripple-shader-pack-real-time-water-ripples-321473) | Object-triggered trails, mouse-triggered rings, raindrop ripples, and continuous ripples for floating objects. | Store page lists Built-in and URP compatibility for Unity 2022.3.27, not HDRP (latest listed release 1.0.0, June 2025). A useful small-scale reference for event authoring, but not a replacement for our FFT wave surface. |
| [Procedural Water Shader](https://assetstore.unity.com/packages/vfx/shaders/procedural-water-shader-353486) | Gerstner vertex displacement, depth-based colour, shoreline/intersection foam, Fresnel/specular lighting, and optional micro-surface ripples. | Store page lists Built-in / URP / HDRP (latest listed 1.1.0, Jan 2026; original Unity 2022.3.62). Useful as a concise reference for combining macro waves, shoreline foam, and micro-ripples. |
| [Poseidon 2 — Low Poly Water System](https://assetstore.unity.com/packages/vfx/shaders/poseidon-2-low-poly-water-system-357908) | Modular Shader Graph features: small ripples, unidirectional waves, world-space wave masks, mesh noise, scrolling normals, light absorption, Fresnel, intersection foam, and water-entry/exit events. | Version 2.0.6 (July 2026); lists Built-in / URP, not HDRP. Useful as a reference for modular effect separation and gameplay-facing water queries/events. |
| [WebGPU Interactive Water](https://assetstore.unity.com/packages/vfx/shaders/webgpu-interactive-water-390962) | GPU-compute ripple simulation reacting to objects and character movement, plus analytic wind waves, foam particles, caustics, and depth-aware shading. | Specifically lists Unity 6000.3.9 URP and WebGPU browser builds (released Aug 2026). It is not a safe drop-in for a non-WebGPU build or other render pipelines. It is the closest store example to fully simulated interaction, but its backend is a major integration constraint. |
| [Stylized Water Shader Pack — URP & Built-In](https://assetstore.unity.com/packages/vfx/shaders/stylized-water-shader-pack-urp-built-in-390566) | Animated vertex waves, intersection foam, procedural or texture ripples, height-based colour, Fresnel, and opacity controls. | Store listing says tested with Unity 2022.3 LTS and Unity 6.0 LTS, URP / Built-in; no HDRP. Useful reference for stylized foam/ripple controls. |

**Compatibility note:** these are feature and pipeline comparisons, not claims that the packages have been tested in this project. Store compatibility badges for Unity 2022.3 or 6000.3 do not guarantee an import into every Unity 6000.5.6f1 project. The WebGPU package in particular requires a WebGPU target and URP.

## What changed in this project

1. **Ripple boundary:** the shader now evaluates an unmasked Gaussian-modulated carrier. It no longer applies a rectangular support mask or a visible outer boundary; it only skips pixels farther than 8.5 packet widths, where the Gaussian energy is effectively zero. Its analytic derivative is used for the normal perturbation, so the slope follows the same continuous envelope as the ring rather than acquiring a second, mismatched edge.
2. **Two-part jump / fall impact:** impacts emit a strong, narrow first ring immediately, followed 0.22 seconds later by a weak, much broader second ring. The second ring has a larger radius, a much wider line envelope, fewer carrier oscillations, no foam flecks, and a longer life. Both ring radii scale with descending duration. The delayed start is stored in the ripple event, so no coroutine or scene object is needed.
3. **Ripple width and timing control:** `WaterFoamField.EmitRipple` now takes optional `widthScale` and `startDelay` overloads. Existing callers retain width scale `1` and zero delay; impacts use a narrow `0.82` first ring and a wide `4.2` secondary ring, delayed by `0.22` seconds.
4. **Shallow-water walking foam:** a player-level `PlayerWaterContactFoam` component is added automatically by `PlayerController`, independent of active movement mode. It samples the animated FFT surface and emits trails while the player capsule partly intersects the water, even if the controller remains in grounded movement.
5. **Foam gating:** both the wading emitter and swimming emitter gate foam by the waterline fraction through the capsule. Foam fades out when the capsule is clear of the surface and when it is deeply submerged.
6. **High fall into water:** the player-level emitter measures descending time while the capsule is above the water. Crossing into the water after the minimum duration emits a splash and the paired impact rings. Swimming hop landings use the same paired-ripple helper, scaled by the descending portion of the hop.

## Limitations / next steps

- Local impact ripples still modify the water's fragment normal / tint rather than displacing the water mesh. The unmasked Gaussian removes the artificial patch edge, but it cannot create a true height ridge or silhouette change. If the remaining effect still reads as a flat decal, the next substantial step is to add the same ripple height function to the vertex displacement path (with enough mesh density), or move local impacts to a wavelet / shallow-water heightfield. GPU Gems describes the practical division between geometric base undulation and finer normal-map waves; the Schreck et al. and Jeschke et al. papers provide stronger bases for analytical circular ripples and obstacle-aware wave propagation.
- The paired impact values are visual defaults and need scene-scale tuning in Play Mode. The broad second ring is intentionally low contrast and begins 0.22 seconds after the first.
- Contact uses one asynchronous FFT height sample at the player's capsule centre. For very narrow shoreline geometry or large characters, sample multiple foot/shoulder points and combine a local waterline estimate.
- The emitter depends on `SpectralOceanFFT.CanSampleSurface` and `RequestSurfaceSample`. If async GPU readback is unsupported, the player-level shallow-water foam does not emit until a valid surface sample is available.
- Unity shader compilation and Play Mode profiling are still required. In particular, validate that the expanded ring support does not exceed visual expectations on the actual water mesh and camera distance.
