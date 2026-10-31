TESSENDORF FFT OCEAN - UNITY 6 URP
================================

Files
-----
OceanFFT.compute     GPU spectrum generation, evolution, inverse FFT and maps.
SpectralOceanFFT.cs  Runtime component that drives three frequency cascades.
Wave.shader          URP Unlit material shader that samples the three cascades.

Setup
-----
1. Put the three files in your Unity project's Assets folder.
2. Create a Material using Shader > Custom > Wave.
3. Add SpectralOceanFFT to the water GameObject that has the MeshRenderer.
4. Assign OceanFFT.compute to the component's Ocean Compute field.
5. Assign the Wave material to the MeshRenderer.
6. Use a dense, horizontal XZ grid mesh. Do not use the stock low-resolution
   Unity Plane for close views. The medium cascade has 8 m minimum wavelengths;
   aim for about 2 m or less between geometry vertices if you want those waves
   represented by vertex displacement.
7. Ensure the mesh bounds include the expected vertical displacement.

Coordinate conventions
----------------------
- Unity world Y is up; the water plane lies in XZ.
- Wind Direction XZ uses Vector2.x = world X and Vector2.y = world Z.
- Wavelengths and patch lengths are measured in Unity world units (usually m).
- The simulation is world-space anchored; moving the player does not move the
  wave field. The water mesh may follow the camera, but preserve world-space
  coordinates when doing so.

Default cascades
----------------
Large:  patch 2048 m, wavelengths 24-1800 m, geometry displacement.
Medium: patch  512 m, wavelengths 8-100 m, geometry displacement.
Small:  patch  128 m, wavelengths 1.5-12 m, normal/foam detail only.

Lifecycle safety
----------------
The controller binds Texture2D.blackTexture as a non-null fallback when its
resources are released. _OceanEnabled is set to zero during teardown and set
to one only after all three cascades are successfully allocated and bound.
This avoids MaterialPropertyBlock.SetTexture(null), which throws in current
Unity versions, and prevents black fallback normal maps from being normalized.

Performance
-----------
The default FFT resolution is 256x256. Each cascade performs four compute
passes per frame: spectrum evolution, X inverse FFT, Z inverse FFT, and map
construction. Each 1D FFT uses group-shared ping-pong memory, so it does not
launch a separate global compute dispatch for each butterfly stage.

Quality/performance guidance:
- 128: lower-cost, suitable for distant or less turbulent water.
- 256: recommended starting point.
- 512: higher spectral resolution, substantially more GPU work and memory.

Tuning
------
- Increase Wind Speed to shift energy toward longer, stronger waves.
- Increase the Large cascade Amplitude Scale for taller open-ocean swells.
- Increase Choppiness for sharper crests and more horizontal displacement.
- Increase Phillips Amplitude to raise overall spectral energy.
- Adjust each cascade's min/max wavelength to redistribute frequency bands.

Notes / limitations
-------------------
- This is a Tessendorf/Phillips-style spectral ocean inspired by the public
  Sea of Thieves SIGGRAPH description; it is not Rare's proprietary source code
  or a claim to reproduce their exact artist settings.
- The foam mask is instantaneous and derived from the displacement Jacobian.
  Persistent foam advection/decay and object-intersection foam are not included.
- Surface geometry and rendering are provided; ship buoyancy / gameplay water
  queries must sample the same displacement fields separately.
- The current implementation uses finite differences of the inverse-FFT maps
  when building normals and the Jacobian.
- The three patches tile periodically. Independent random spectra and different
  patch lengths reduce obvious repetition, but this is not aperiodic synthesis.
- Requires a GPU and graphics API with compute shader and floating-point UAV
  support. Test on the target platform.
