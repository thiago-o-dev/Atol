Shader "Custom/Wave"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [NoScaleOffset] _ShoreDistance("Shore Distance Field (Black = Shore, White = Deep Water)", 2D) = "white" {}
        [NoScaleOffset] _ShoreDepthModifier("Painted Seabed Depth Modifier (Black = Shallow, White = Deep)", 2D) = "white" {}
        [HideInInspector] _ShoreMaxDepthMeters("Maximum Inferred Water Depth (m)", Float) = 60
        [HideInInspector] _ShoreMapOriginXZ("Shore Map Origin XZ", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ShoreMapSizeXZ("Shore Map Size XZ", Vector) = (8192, 8192, 0, 0)
        [HideInInspector] _ShoreMapFlipV("Shore Map Flip V", Float) = 0
        [HideInInspector] _ShoreDistanceRangeMeters("Shore Distance Range (m)", Float) = 150
        [HideInInspector] _ShoreFadeStartMeters("Shore Fade Start (m)", Float) = 0
        [HideInInspector] _ShoreFadeEndMeters("Shore Fade End (m)", Float) = 100
        [HideInInspector] _NearShoreWaveStrength("Near Shore Wave Strength", Float) = 0.08
        [HideInInspector] _ShoreEdgeFadeMeters("Shore Edge Fade (m)", Float) = 3.5
        [HideInInspector] _ShoreBreakStartMeters("Shore Break Start (m)", Float) = 2
        [HideInInspector] _ShoreBreakPeakMeters("Shore Break Peak (m)", Float) = 22
        [HideInInspector] _ShoreBreakEndMeters("Shore Break End (m)", Float) = 90
        [HideInInspector] _ShoreDepthPerMeter("Shore Depth Per Meter", Float) = 0.12
        [HideInInspector] _BreakerHeightToDepth("Breaker Height / Depth", Float) = 0.78
        [HideInInspector] _ShoreShoalingScale("Shore Shoaling Scale", Float) = 1.18
        [HideInInspector] _ShoreHorizontalFloor("Shore Horizontal Floor", Float) = 0.12
        [HideInInspector] _ShoreHorizontalDrive("Shore Horizontal Drive", Float) = 0.45
        [HideInInspector] _ShoreChoppiness("Shore Choppiness", Float) = 1.55
        [HideInInspector] _ShoreNormalBoost("Shore Normal Boost", Float) = 1.25
        [HideInInspector] _ShoreFoamWidthMeters("Shore Foam Width (m)", Float) = 65
        [HideInInspector] _ShoreFoamEdgeFeatherMeters("Shore Foam Fade Length (m)", Float) = 85
        [HideInInspector] _ShoreFoamBlackThreshold("Dark Land Foam Sensitivity", Float) = 0.45
        [HideInInspector] _ShoreFoamBlackSoftness("Dark Land Transition Softness", Float) = 0.18
        [HideInInspector] _ShoreFoamEdgeNoiseMeters("Foam Boundary Irregularity (m)", Float) = 14
        [HideInInspector] _OpenOceanFoamMultiplier("Open Ocean Foam Multiplier", Float) = 0.035
        [HideInInspector] _ShoreFoamGain("Shore Foam Gain", Float) = 3.5
        [HideInInspector] _ShoreCrestJacobianThreshold("Shore Crest Jacobian Threshold", Float) = 0.94
        [HideInInspector] _ShoreCrestCompressionGain("Shore Crest Compression Gain", Float) = 8
        [HideInInspector] _ShoreCrestHeightGain("Shore Crest Height Gain", Float) = 0.45
        [HideInInspector] _ShoreWashSpeed("Shore Wash Speed", Float) = 2.2
        [HideInInspector] _ShoreWashSpacingMeters("Shore Wash Spacing (m)", Float) = 42
        [HideInInspector] _ShoreWashPulseStrength("Shore Wash Pulse Strength", Float) = 0.65
        [HideInInspector] _ShoreDebugView("Shore Debug View", Float) = 0

        [Header(WaterColors)]
        _DeepColor("Deep Water", Color) = (0.008, 0.045, 0.105, 1)
        _ShallowColor("Subsurface Water", Color) = (0.025, 0.24, 0.30, 1)
        _FresnelColor("Fresnel Color", Color) = (0.38, 0.72, 0.9, 1)
        _FoamColor("Foam Color", Color) = (0.88, 0.96, 1, 1)
        _SunColor("Sun Color", Color) = (1, 0.88, 0.68, 1)

        [Header(Lighting)]
        _SunDirection("Sun Direction (XYZ)", Vector) = (-0.4, 0.8, -0.3, 0)
        _FresnelPower("Fresnel Power", Range(1, 8)) = 5
        _FresnelStrength("Fresnel Strength", Range(0, 3)) = 0.8
        _SubsurfaceStrength("Subsurface Strength", Range(0, 2)) = 0.9
        _SpecularPower("Specular Power", Range(8, 256)) = 72
        _SpecularStrength("Specular Strength", Range(0, 5)) = 1.8

        [HideInInspector] _OceanDisplacement0("Ocean Displacement 0", 2D) = "black" {}
        [HideInInspector] _OceanDisplacement1("Ocean Displacement 1", 2D) = "black" {}
        [HideInInspector] _OceanDisplacement2("Ocean Displacement 2", 2D) = "black" {}
        [HideInInspector] _OceanNormal0("Ocean Normal 0", 2D) = "bump" {}
        [HideInInspector] _OceanNormal1("Ocean Normal 1", 2D) = "bump" {}
        [HideInInspector] _OceanNormal2("Ocean Normal 2", 2D) = "bump" {}
        [HideInInspector] _OceanPatchLength0("Patch Length 0", Float) = 2048
        [HideInInspector] _OceanPatchLength1("Patch Length 1", Float) = 512
        [HideInInspector] _OceanPatchLength2("Patch Length 2", Float) = 128
        [HideInInspector] _OceanHalfTexel("Ocean Half Texel", Float) = 0.001953125
        [HideInInspector] _OceanEnabled("Ocean Simulation Enabled", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            // Geometry+2: after opaque boats and the HullWaterMask depth plane (Geometry+1).
            "Queue" = "Geometry+2"
        }

        Pass
        {
            Name "OceanForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_ShoreDistance);
            SAMPLER(sampler_ShoreDistance);
            TEXTURE2D(_ShoreDepthModifier);
            SAMPLER(sampler_ShoreDepthModifier);

            TEXTURE2D(_OceanDisplacement0);
            SAMPLER(sampler_OceanDisplacement0);
            TEXTURE2D(_OceanDisplacement1);
            SAMPLER(sampler_OceanDisplacement1);
            TEXTURE2D(_OceanDisplacement2);
            SAMPLER(sampler_OceanDisplacement2);

            TEXTURE2D(_OceanNormal0);
            SAMPLER(sampler_OceanNormal0);
            TEXTURE2D(_OceanNormal1);
            SAMPLER(sampler_OceanNormal1);
            TEXTURE2D(_OceanNormal2);
            SAMPLER(sampler_OceanNormal2);

            // Contact foam painted by Features/WaterEffects (WaterFoamField). Global, set per frame.
            // _WaterFoamFieldParams = (window min X, window min Z, 1 / window size, enabled).
            TEXTURE2D(_WaterFoamField);
            SAMPLER(sampler_WaterFoamField);
            float4 _WaterFoamFieldParams;

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;

                half4 _DeepColor;
                half4 _ShallowColor;
                half4 _FresnelColor;
                half4 _FoamColor;
                half4 _SunColor;

                float4 _SunDirection;
                float _FresnelPower;
                float _FresnelStrength;
                float _SubsurfaceStrength;
                float _SpecularPower;
                float _SpecularStrength;

                float _OceanPatchLength0;
                float _OceanPatchLength1;
                float _OceanPatchLength2;
                float _OceanHalfTexel;
                float _OceanEnabled;


                float4 _ShoreMapOriginXZ;
                float4 _ShoreMapSizeXZ;
                float _ShoreMapFlipV;
                float _ShoreDistanceRangeMeters;
                float _ShoreMaxDepthMeters;
                float _ShoreFadeStartMeters;
                float _ShoreFadeEndMeters;
                float _NearShoreWaveStrength;
                float _ShoreEdgeFadeMeters;
                float _ShoreBreakStartMeters;
                float _ShoreBreakPeakMeters;
                float _ShoreBreakEndMeters;
                float _ShoreDepthPerMeter;
                float _BreakerHeightToDepth;
                float _ShoreShoalingScale;
                float _ShoreHorizontalFloor;
                float _ShoreHorizontalDrive;
                float _ShoreChoppiness;
                float _ShoreNormalBoost;
                float _ShoreFoamWidthMeters;
                float _ShoreFoamEdgeFeatherMeters;
                float _ShoreFoamBlackThreshold;
                float _ShoreFoamBlackSoftness;
                float _ShoreFoamEdgeNoiseMeters;
                float _OpenOceanFoamMultiplier;
                float _ShoreFoamGain;
                float _ShoreCrestJacobianThreshold;
                float _ShoreCrestCompressionGain;
                float _ShoreCrestHeightGain;
                float _ShoreWashSpeed;
                float _ShoreWashSpacingMeters;
                float _ShoreWashPulseStrength;
                float _ShoreDebugView;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 baseUV : TEXCOORD0;
                float2 oceanUV0 : TEXCOORD1;
                float2 oceanUV1 : TEXCOORD2;
                float2 oceanUV2 : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                float2 baseWorldXZ : TEXCOORD5;
            };

            // Quintic fade has zero first and second derivatives at both ends,
            // which avoids visible bands when displacement/foam controls meet.
            float SmootherStep(float edge0, float edge1, float value)
            {
                float t = saturate((value - edge0) / max(edge1 - edge0, 1e-4));
                return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
            }

            // Smooth value noise used only to break up the foam boundary. It is
            // evaluated in world space, so it does not swim when the camera moves.
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // Expanding ripple rings from Features/WaterEffects (WaterFoamField.EmitRipple).
            // Mostly a water-surface effect (normal tilt + faint tint); foam flecks are rare and small.
            #define MAX_WATER_RIPPLES 32
            float4 _WaterRippleDataA[MAX_WATER_RIPPLES]; // xy = centre XZ, z = start time, w = start radius
            float4 _WaterRippleDataB[MAX_WATER_RIPPLES]; // x = max radius, y = strength, z = foam amount, w = lifetime
            float4 _WaterRippleDataC[MAX_WATER_RIPPLES]; // xy = facing direction XZ, z = arc (0 full circle, 1 half circle)
            float4 _WaterRippleParams;               // x = count, y = current time

            void EvaluateRipples(float2 p, out float2 slope, out float tint, out float foamBits)
            {
                slope = 0.0;
                tint = 0.0;
                foamBits = 0.0;

                int count = (int)_WaterRippleParams.x;
                if (count <= 0)
                    return;

                float now = _WaterRippleParams.y;
                float lace = ValueNoise(p * 5.0) * 0.6 + ValueNoise(p * 13.0 + 7.1) * 0.4;

                [loop]
                for (int k = 0; k < count; k++)
                {
                    float4 a = _WaterRippleDataA[k];
                    float4 b = _WaterRippleDataB[k];
                    float age = now - a.z;
                    float life = max(b.w, 0.1);
                    if (age < 0.0 || age > life)
                        continue;

                    float u = age / life;
                    float2 d = p - a.xy;
                    float r = length(d);
                    if (r > b.x * 1.15)
                        continue;

                    // Ring radius decelerates as it spreads.
                    float ringRadius = lerp(a.w, b.x, 1.0 - pow(1.0 - u, 1.7));
                    float width = lerp(0.2, 0.6, u) * max(1.0, b.x * 0.06);
                    float x = (r - ringRadius) / width;
                    if (abs(x) > 3.0)
                        continue;

                    float2 dir = d / max(r, 1e-3);

                    // Half-circle ripples (moving objects) only exist on the facing side.
                    float side = dot(dir, _WaterRippleDataC[k].xy);
                    float arcMask = lerp(1.0, smoothstep(-0.25, 0.35, side), _WaterRippleDataC[k].z);
                    if (arcMask <= 0.001)
                        continue;

                    // Short wave packet: ~3 crests under a soft envelope.
                    float envelope = exp(-0.5 * x * x);
                    float phase = x * 3.6;
                    float h = cos(phase) * envelope;
                    float dhdr = (-sin(phase) * 3.6 * envelope - x * h) / width;

                    // Visible for the whole life, fading smoothly to nothing; thins as it spreads.
                    float fade = (1.0 - u) * (1.0 - u * u) * b.y * arcMask
                               * rsqrt(max(r / max(a.w, 0.1), 1.0));

                    slope += dir * dhdr * fade * 0.06 * (1.0 + 0.04 * b.x);
                    tint += abs(h) * fade;

                    // Rare small flecks, only on strong crests of big, fresh ripples.
                    float crest = saturate(h);
                    foamBits = max(foamBits,
                        smoothstep(0.82, 0.98, crest * (0.55 + lace * 0.9)) * b.z * saturate(fade * 1.5));
                }
            }

            float ShoreFoamBoundaryNoise(float2 worldXZ)
            {
                float broad = ValueNoise(worldXZ * 0.0045);
                float medium = ValueNoise(worldXZ * 0.013 + 17.7);
                float fine = ValueNoise(worldXZ * 0.031 + 43.1);
                return ((broad - 0.5) * 1.15
                      + (medium - 0.5) * 0.65
                      + (fine - 0.5) * 0.20) * 2.0;
            }

            float GetLocalWaterDepth(float2 worldXZ, float shoreDistanceMeters)
            {
                float2 mapSize = max(_ShoreMapSizeXZ.xy, float2(1.0, 1.0));
                float2 shoreUV = (worldXZ - _ShoreMapOriginXZ.xy) / mapSize;
                if (_ShoreMapFlipV > 0.5)
                    shoreUV.y = 1.0 - shoreUV.y;

                float uvValid = all(isfinite(shoreUV)) ? 1.0 : 0.0;
                if (uvValid < 0.5)
                    shoreUV = 0.0;
                float insideMap = uvValid * step(0.0, shoreUV.x) * step(shoreUV.x, 1.0)
                                * step(0.0, shoreUV.y) * step(shoreUV.y, 1.0);
                float paintedDepthFactor = SAMPLE_TEXTURE2D_LOD(
                    _ShoreDepthModifier, sampler_ShoreDepthModifier,
                    saturate(shoreUV), 0).r;
                // A corrupted / uninitialised map must not produce NaN depth and
                // poison the vertex position. Unknown depth defaults to open water.
                if (!isfinite(paintedDepthFactor))
                    paintedDepthFactor = 1.0;
                paintedDepthFactor = lerp(1.0, saturate(paintedDepthFactor), insideMap);

                float safeShoreDistance = isfinite(shoreDistanceMeters)
                    ? max(shoreDistanceMeters, 0.0)
                    : max(_ShoreDistanceRangeMeters, 1.0);
                float inferredDepth = min(
                    safeShoreDistance * max(_ShoreDepthPerMeter, 0.001),
                    max(_ShoreMaxDepthMeters, 1.0));
                float resultDepth = inferredDepth * paintedDepthFactor;
                return isfinite(resultDepth) ? max(0.05, resultDepth)
                    : max(_ShoreMaxDepthMeters, 1.0);
            }

            void GetShoreFactors(
                float2 worldXZ,
                out float distanceMeters,
                out float waveStrength,
                out float breakStrength,
                out float foamArea)
            {
                float2 mapSize = max(_ShoreMapSizeXZ.xy, float2(1.0, 1.0));
                float2 shoreUV = (worldXZ - _ShoreMapOriginXZ.xy) / mapSize;

                if (_ShoreMapFlipV > 0.5)
                    shoreUV.y = 1.0 - shoreUV.y;

                // Outside the authored map or an invalid mapping is treated as
                // open water. Invalid map coordinates must never remove a vertex.
                float uvValid = all(isfinite(shoreUV)) ? 1.0 : 0.0;
                if (uvValid < 0.5)
                    shoreUV = 0.0;
                float insideMap = uvValid * step(0.0, shoreUV.x) * step(shoreUV.x, 1.0)
                                * step(0.0, shoreUV.y) * step(shoreUV.y, 1.0);

                float sampledDistance01 = SAMPLE_TEXTURE2D_LOD(
                    _ShoreDistance, sampler_ShoreDistance, saturate(shoreUV), 0).r;
                // The shoreline map only controls displacement. If its sample is
                // invalid, prefer unmasked open water rather than letting NaN
                // propagate into the final vertex transform.
                if (!isfinite(sampledDistance01))
                    sampledDistance01 = 1.0;
                sampledDistance01 = lerp(1.0, saturate(sampledDistance01), insideMap);

                // The baker stores distance / Coastal Fade Distance.
                float safeDistanceRange = isfinite(_ShoreDistanceRangeMeters)
                    ? max(_ShoreDistanceRangeMeters, 1.0) : 150.0;
                distanceMeters = sampledDistance01 * safeDistanceRange;
                if (!isfinite(distanceMeters))
                    distanceMeters = safeDistanceRange;

                // Broad quintic fade: displacement approaches zero gradually at the
                // waterline, but the base mesh is NEVER clipped or removed.
                float fadeStart = max(_ShoreFadeStartMeters, 0.0);
                float fadeEnd = max(_ShoreFadeEndMeters, fadeStart + 0.1);
                float offshoreBlend = SmootherStep(fadeStart, fadeEnd, distanceMeters);
                waveStrength = lerp(saturate(_NearShoreWaveStrength), 1.0, offshoreBlend);

                // Broad breaker region. It peaks smoothly and does not multiply
                // overall displacement by an aggressive near-shore boost.
                float breakStart = max(_ShoreBreakStartMeters, 0.0);
                float breakPeak = max(_ShoreBreakPeakMeters, breakStart + 0.1);
                float breakEnd = max(_ShoreBreakEndMeters, breakPeak + 0.1);
                float breakingIn = SmootherStep(breakStart, breakPeak, distanceMeters);
                float breakingOut = 1.0 - SmootherStep(breakPeak, breakEnd, distanceMeters);
                breakStrength = saturate(breakingIn * breakingOut);

                // Dark land / near-black distance samples can pull the wash farther
                // offshore. The threshold is a normalized distance-map value: higher
                // values include lighter gray pixels, not just nearly-black shoreline.
                float darkThreshold = saturate(_ShoreFoamBlackThreshold);
                float darkSoftness = max(_ShoreFoamBlackSoftness, 0.001);
                float darkPixelInfluence = 1.0 - SmootherStep(
                    darkThreshold, min(1.0, darkThreshold + darkSoftness),
                    sampledDistance01);

                float foamWidth = max(_ShoreFoamWidthMeters, 0.1);
                float foamFeather = max(_ShoreFoamEdgeFeatherMeters, 0.1);
                float landExpansion = darkPixelInfluence
                    * darkThreshold * (foamWidth * 0.55 + foamFeather * 0.20);

                // Multi-scale world-space noise prevents a mathematically circular
                // distance contour from looking like a hard ring around every island.
                float edgeNoise = ShoreFoamBoundaryNoise(worldXZ)
                                * max(_ShoreFoamEdgeNoiseMeters, 0.0);
                float foamDistance = max(
                    0.0, distanceMeters - landExpansion + edgeNoise);

                // A rational tail gives a broad, asymptotic falloff. It has zero slope
                // at the full-foam boundary and never abruptly cuts the wash to zero.
                // Fade Length is the approximate distance for the foam to fall to ~6%.
                float beyondCore = max(0.0, foamDistance - foamWidth);
                float featherRatio = beyondCore / foamFeather;
                foamArea = rcp(1.0 + 16.0 * featherRatio * featherRatio);
                foamArea *= insideMap;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // Start with fully initialized outputs. Every later failure path
                // returns this source-mesh position rather than an uninitialized
                // clip coordinate or a zeroed world position.
                float2 sourceUV = TRANSFORM_TEX(IN.uv, _BaseMap);
                float3 sourcePositionOS = IN.positionOS.xyz;
                bool sourceVertexValid = all(isfinite(sourcePositionOS)) &&
                    !any(abs(sourcePositionOS) > 1e7);

                if (!sourceVertexValid)
                {
                    // A malformed vertex has no meaningful source position. Collapse
                    // only that invalid input to the mesh object's origin, which is
                    // preferable to propagating NaNs through the entire primitive.
                    sourcePositionOS = float3(0.0, 0.0, 0.0);
                }

                float3 basePositionWS = TransformObjectToWorld(sourcePositionOS);
                float4 sourcePositionHCS = TransformObjectToHClip(sourcePositionOS);
                float3 safeObjectOriginWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                bool basePositionValid = all(isfinite(basePositionWS)) &&
                    !any(abs(basePositionWS) > 1e7);
                bool safeOriginValid = all(isfinite(safeObjectOriginWS)) &&
                    !any(abs(safeObjectOriginWS) > 1e7);

                // Prefer the original transformed vertex. If the source data is
                // malformed but the object transform is still valid, use the
                // transformed mesh origin. Never leave a bad clip coordinate in
                // SV_POSITION just because an intermediate world position failed.
                float3 fallbackPositionWS = basePositionValid
                    ? basePositionWS
                    : (safeOriginValid ? safeObjectOriginWS : float3(0.0, 0.0, 0.0));
                float4 fallbackPositionHCS = TransformWorldToHClip(fallbackPositionWS);
                if (!all(isfinite(fallbackPositionHCS)))
                    fallbackPositionHCS = float4(0.0, 0.0, 0.0, 1.0);

                OUT.positionHCS = sourceVertexValid && basePositionValid &&
                    all(isfinite(sourcePositionHCS))
                    ? sourcePositionHCS
                    : fallbackPositionHCS;
                OUT.positionWS = fallbackPositionWS;
                OUT.baseWorldXZ = OUT.positionWS.xz;
                OUT.baseUV = sourceUV;
                OUT.oceanUV0 = float2(0.0, 0.0);
                OUT.oceanUV1 = float2(0.0, 0.0);
                OUT.oceanUV2 = float2(0.0, 0.0);

                // If either the source vertex or its object transform is invalid,
                // keep the initialized source fallback. Do not evaluate shoreline
                // textures or FFT displacement for this vertex.
                if (!sourceVertexValid || !basePositionValid)
                    return OUT;

                // Evaluate shore distance from the undeformed world position so
                // the coastal profile stays fixed when the player/camera moves.

                float shoreDistanceMeters;
                float shoreStrength;
                float shoreBreak;
                float shoreFoamArea;
                GetShoreFactors(
                    basePositionWS.xz,
                    shoreDistanceMeters,
                    shoreStrength,
                    shoreBreak,
                    shoreFoamArea);

                if (!isfinite(shoreDistanceMeters) || !isfinite(shoreStrength) ||
                    !isfinite(shoreBreak) || !isfinite(shoreFoamArea))
                {
                    shoreDistanceMeters = max(_ShoreDistanceRangeMeters, 1.0);
                    shoreStrength = 1.0;
                    shoreBreak = 0.0;
                    shoreFoamArea = 0.0;
                }

                float2 texelCenterOffset = _OceanHalfTexel.xx;
                float2 uv0 = basePositionWS.xz / max(_OceanPatchLength0, 1.0) + texelCenterOffset;
                float2 uv1 = basePositionWS.xz / max(_OceanPatchLength1, 1.0) + texelCenterOffset;
                float2 uv2 = basePositionWS.xz / max(_OceanPatchLength2, 1.0) + texelCenterOffset;

                float4 displacement0 = 0.0;
                float4 displacement1 = 0.0;
                if (_OceanEnabled > 0.5)
                {
                    displacement0 = SAMPLE_TEXTURE2D_LOD(
                        _OceanDisplacement0, sampler_OceanDisplacement0, uv0, 0);
                    displacement1 = SAMPLE_TEXTURE2D_LOD(
                        _OceanDisplacement1, sampler_OceanDisplacement1, uv1, 0);
                }

                // Large + medium cascades displace geometry. The finest cascade
                // remains shading-only to avoid vertex aliasing. Sanitize the GPU
                // sample first: NaN/Inf values must never poison SV_POSITION.
                float3 displacementWS = float3(
                    displacement0.x + displacement1.x,
                    displacement0.y + displacement1.y,
                    displacement0.z + displacement1.z);
                if (!all(isfinite(displacementWS)) || any(abs(displacementWS) > 10000.0))
                    displacementWS = 0.0;

                // Shoaling is a wide, gradual ramp: the crest gain builds as water
                // enters the shelf, then the depth limiter gently caps the height.
                // It is intentionally not a narrow multiplier spike at one distance.
                float shoalRamp = 1.0 - SmootherStep(
                    _ShoreBreakStartMeters, _ShoreBreakEndMeters, shoreDistanceMeters);
                float shoalScale = lerp(
                    1.0, max(_ShoreShoalingScale, 1.0), shoalRamp);
                float candidateHeight = displacementWS.y * shoalScale;

                float localDepth = GetLocalWaterDepth(
                    basePositionWS.xz, shoreDistanceMeters);
                float maxCrestAmplitude = max(
                    0.05,
                    0.5 * max(_BreakerHeightToDepth, 0.1) * localDepth);
                float depthLimitedHeight = maxCrestAmplitude * tanh(
                    candidateHeight / maxCrestAmplitude);
                float depthLimitBlend = 1.0 - SmootherStep(1.0, 14.0, localDepth);
                float limitedHeight = lerp(
                    candidateHeight, depthLimitedHeight, depthLimitBlend);

                // The water surface itself remains at basePositionWS when motion
                // fades to zero. We only attenuate displacement; there is no clip,
                // vertex deletion, or alpha fade. Negative troughs are suppressed
                // progressively near the coast so they cannot pull the surface
                // below the shoreline and make the water appear to recede/disappear.
                float stillBand = max(_ShoreEdgeFadeMeters, 0.5);
                float nearShoreBlend = SmootherStep(0.0, stillBand, shoreDistanceMeters);
                float troughSuppression = 1.0 - SmootherStep(
                    0.0, stillBand * 2.0, shoreDistanceMeters);
                float shoreSafeHeight = lerp(
                    max(limitedHeight, 0.0), limitedHeight, 1.0 - troughSuppression);
                float verticalMotion = shoreStrength * nearShoreBlend;
                displacementWS.y = shoreSafeHeight * verticalMotion;

                // Horizontal motion is separate from height gain. It can retain a
                // little forward crest travel in the breaker band, but also eases
                // smoothly to a stationary, still-visible surface at the waterline.
                float horizontalScale = lerp(
                    saturate(_ShoreHorizontalFloor), 1.0, nearShoreBlend);
                horizontalScale *= lerp(
                    1.0, max(_ShoreChoppiness, 1.0), shoreBreak);
                float horizontalDrive = saturate(
                    shoreStrength + _ShoreHorizontalDrive * shoreBreak);
                // The horizontal scale already contains the near-shore fade.
                // Applying nearShoreBlend a second time squared the fade and
                // created an unnecessarily sharp compression transition.
                displacementWS.xz *= horizontalScale * horizontalDrive;

                // Land pixels and any invalid vertex calculation fall back to the
                // original transformed world vertex. The water surface is never
                // clipped, discarded, or moved to an invalid position by this pass.
                if (shoreDistanceMeters <= 0.001)
                    displacementWS = 0.0;

                float3 displacedPositionWS = basePositionWS + displacementWS;
                if (!all(isfinite(displacedPositionWS)) ||
                    any(abs(displacedPositionWS) > 1e7))
                    displacedPositionWS = basePositionWS;

                float4 clipPosition = TransformWorldToHClip(displacedPositionWS);
                if (!all(isfinite(clipPosition)))
                {
                    displacedPositionWS = basePositionWS;
                    // The original source clip position was already validated at
                    // vertex entry. Reuse that exact finite value rather than
                    // repeating a potentially failing transform calculation.
                    clipPosition = OUT.positionHCS;
                }
                OUT.positionHCS = clipPosition;
                OUT.positionWS = displacedPositionWS;
                OUT.baseWorldXZ = basePositionWS.xz;
                OUT.baseUV = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.oceanUV0 = uv0;
                OUT.oceanUV1 = uv1;
                OUT.oceanUV2 = uv2;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(
                    _BaseMap, sampler_BaseMap, IN.baseUV) * _BaseColor;

                // Re-sample the shore field per pixel. Geometry displacement is
                // vertex-evaluated, but foam width / black sensitivity should not
                // inherit the water mesh's vertex spacing.
                float fragmentShoreDistance;
                float fragmentWaveStrength;
                float fragmentShoreBreak;
                float fragmentFoamArea;
                GetShoreFactors(
                    IN.baseWorldXZ,
                    fragmentShoreDistance,
                    fragmentWaveStrength,
                    fragmentShoreBreak,
                    fragmentFoamArea);

                // Runtime visualization makes map-origin/range mistakes obvious.
                // 1=distance, 2=wave attenuation, 3=breaker band, 4=foam coverage.
                if (_ShoreDebugView > 0.5 && _ShoreDebugView < 1.5)
                    return half4(fragmentShoreDistance / max(_ShoreDistanceRangeMeters, 1.0),
                                 fragmentShoreDistance / max(_ShoreDistanceRangeMeters, 1.0),
                                 fragmentShoreDistance / max(_ShoreDistanceRangeMeters, 1.0), 1.0);
                if (_ShoreDebugView > 1.5 && _ShoreDebugView < 2.5)
                    return half4(fragmentWaveStrength, fragmentWaveStrength, fragmentWaveStrength, 1.0);
                if (_ShoreDebugView > 2.5 && _ShoreDebugView < 3.5)
                    return half4(fragmentShoreBreak, fragmentShoreBreak, fragmentShoreBreak, 1.0);
                if (_ShoreDebugView > 3.5 && _ShoreDebugView < 4.5)
                    return half4(fragmentFoamArea, fragmentFoamArea, fragmentFoamArea, 1.0);
                if (_ShoreDebugView > 4.5 && _ShoreDebugView < 5.5)
                {
                    float depth01 = saturate(GetLocalWaterDepth(
                        IN.baseWorldXZ, fragmentShoreDistance) / max(_ShoreMaxDepthMeters, 1.0));
                    return half4(depth01, depth01, depth01, 1.0);
                }

                float4 normalData0 = SAMPLE_TEXTURE2D(
                    _OceanNormal0, sampler_OceanNormal0, IN.oceanUV0);
                float4 normalData1 = SAMPLE_TEXTURE2D(
                    _OceanNormal1, sampler_OceanNormal1, IN.oceanUV1);
                float4 normalData2 = SAMPLE_TEXTURE2D(
                    _OceanNormal2, sampler_OceanNormal2, IN.oceanUV2);

                float4 displacementData0 = SAMPLE_TEXTURE2D(
                    _OceanDisplacement0, sampler_OceanDisplacement0, IN.oceanUV0);
                float4 displacementData1 = SAMPLE_TEXTURE2D(
                    _OceanDisplacement1, sampler_OceanDisplacement1, IN.oceanUV1);
                float4 displacementData2 = SAMPLE_TEXTURE2D(
                    _OceanDisplacement2, sampler_OceanDisplacement2, IN.oceanUV2);

                float3 n0 = float3(0.0, 1.0, 0.0);
                float3 n1 = float3(0.0, 1.0, 0.0);
                float3 n2 = float3(0.0, 1.0, 0.0);

                if (_OceanEnabled > 0.5)
                {
                    n0 = all(isfinite(normalData0.xyz)) ? normalize(normalData0.xyz) : float3(0, 1, 0);
                    n1 = all(isfinite(normalData1.xyz)) ? normalize(normalData1.xyz) : float3(0, 1, 0);
                    n2 = all(isfinite(normalData2.xyz)) ? normalize(normalData2.xyz) : float3(0, 1, 0);
                }

                // The shoreline normal response is modest and independent from
                // displacement amplitude, avoiding the old over-sharpened bands.
                // Normal intensity must follow the same attenuation envelope as
                // vertex displacement. The breaker boost is an additional modest
                // multiplier, not a replacement that re-inflates near-shore normals.
                float shorelineMotion = SmootherStep(
                    0.0, max(_ShoreEdgeFadeMeters, 0.5), fragmentShoreDistance);
                float normalScale = fragmentWaveStrength * shorelineMotion * lerp(
                    1.0,
                    max(_ShoreNormalBoost, 1.0),
                    fragmentShoreBreak);
                float2 combinedSlope = (
                    n0.xz / max(n0.y, 0.12) +
                    n1.xz / max(n1.y, 0.12) +
                    n2.xz / max(n2.y, 0.12)) * normalScale;

                float2 rippleSlope;
                float rippleTint;
                float rippleFoam;
                EvaluateRipples(IN.positionWS.xz, rippleSlope, rippleTint, rippleFoam);
                combinedSlope += rippleSlope;
                float3 normalWS = normalize(float3(
                    combinedSlope.x, 1.0, combinedSlope.y));
                if (!all(isfinite(normalWS)))
                    normalWS = float3(0.0, 1.0, 0.0);

                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 sunDir = normalize(_SunDirection.xyz);
                float nDotV = saturate(dot(normalWS, viewDir));
                float nDotL = saturate(dot(normalWS, sunDir));

                float fresnel = pow(1.0 - nDotV, _FresnelPower);

                // Peak mechanics combine three physically useful cues:
                // compression (Jacobian), steep face angle, and crest height relative
                // to inferred local depth. Each cue is softened before combination.
                float rawJacobian = min(normalData0.a, normalData1.a);
                float peakHorizontalDrive = saturate(
                    fragmentWaveStrength * shorelineMotion
                    + _ShoreHorizontalDrive * fragmentShoreBreak * shorelineMotion);
                float peakCompression = lerp(
                    1.0, max(_ShoreChoppiness, 1.0), fragmentShoreBreak)
                    * peakHorizontalDrive;
                float effectiveJacobian = 1.0 + (rawJacobian - 1.0) * peakCompression;
                float compressionSignal = saturate(
                    (_ShoreCrestJacobianThreshold - effectiveJacobian)
                    * max(_ShoreCrestCompressionGain, 0.1));
                float compressionPeak = smoothstep(0.0, 1.0, compressionSignal);

                float faceSlope = 1.0 - saturate(min(n0.y, n1.y));
                float slopePeak = smoothstep(0.10, 0.62, faceSlope);

                float localDepth = GetLocalWaterDepth(
                    IN.baseWorldXZ, fragmentShoreDistance);
                float depthLimitedCrestAmplitude = max(
                    0.05,
                    0.5 * max(_BreakerHeightToDepth, 0.1) * localDepth);
                float crestShoalScale = lerp(
                    1.0,
                    max(_ShoreShoalingScale, 1.0),
                    1.0 - SmootherStep(
                        _ShoreBreakStartMeters, _ShoreBreakEndMeters,
                        fragmentShoreDistance));
                float combinedCrestHeight = max(
                    0.0,
                    (displacementData0.y + displacementData1.y)
                    * fragmentWaveStrength * crestShoalScale);
                float crestHeightRatio = combinedCrestHeight /
                    max(depthLimitedCrestAmplitude, 0.05);
                crestHeightRatio *= 1.0 + max(_ShoreCrestHeightGain, 0.0)
                    * fragmentShoreBreak;
                float depthBreakerPeak = smoothstep(0.45, 1.05, crestHeightRatio);

                // Soft union avoids a sudden threshold when one peak signal replaces
                // another. Compression is weighted most strongly for actual foldover.
                float geometricPeak = 1.0
                    - (1.0 - compressionPeak * 0.62)
                    * (1.0 - slopePeak * 0.16)
                    * (1.0 - depthBreakerPeak * 0.72);
                float crestPeak = saturate(geometricPeak);

                // Subsurface color brightens at steep, compressed crests, but the
                // mask is strongest around shore break rather than everywhere.
                float peakMask = crestPeak * fragmentShoreBreak;
                float subsurface = saturate(
                    peakMask * _SubsurfaceStrength + (1.0 - nDotV) * 0.16);

                half3 waterColor = lerp(
                    _DeepColor.rgb, _ShallowColor.rgb, subsurface);
                waterColor *= baseSample.rgb;
                waterColor *= 0.35 + 0.65 * nDotL;

                float3 halfVector = normalize(viewDir + sunDir);
                float specular = pow(
                    saturate(dot(normalWS, halfVector)),
                    _SpecularPower) * _SpecularStrength;
                waterColor += _SunColor.rgb * specular;
                waterColor += _FresnelColor.rgb * fresnel * _FresnelStrength;
                waterColor += _ShallowColor.rgb * rippleTint * 0.12;

                float foam = 0.0;
                if (_OceanEnabled > 0.5)
                {
                    float spectralFoam = 1.0 -
                        (1.0 - saturate(displacementData0.a)) *
                        (1.0 - saturate(displacementData1.a)) *
                        (1.0 - saturate(displacementData2.a));

                    // Keep open-ocean whitecaps low; nearshore foam is driven by
                    // depth-aware peaks and the broad coastal wash envelope.
                    float offshoreFoam = spectralFoam * lerp(
                        saturate(_OpenOceanFoamMultiplier), 0.28,
                        fragmentShoreBreak);

                    float washSpacing = max(_ShoreWashSpacingMeters, 5.0);
                    float alongshoreVariation =
                        2.4 * sin(IN.baseWorldXZ.x * 0.013
                                  + cos(IN.baseWorldXZ.y * 0.009))
                        + 1.6 * sin(IN.baseWorldXZ.y * 0.021
                                    - IN.baseWorldXZ.x * 0.006);
                    float washPhase = (
                        fragmentShoreDistance + alongshoreVariation
                        + _Time.y * max(_ShoreWashSpeed, 0.0))
                        * (6.28318530718 / washSpacing);
                    float washWave = 0.5 + 0.5 * sin(washPhase);
                    float washFront = smoothstep(0.18, 0.88, washWave);
                    float washMotion = lerp(
                        1.0 - saturate(_ShoreWashPulseStrength), 1.0, washFront);

                    // Keep a continuous foam base and let crests brighten it; do not
                    // gate the entire coastal wash on a single hard crest threshold.
                    float washEnergy = saturate(
                        (0.16 + crestPeak * 0.84) * _ShoreFoamGain * 0.34);
                    float washFoam = fragmentFoamArea * washEnergy * washMotion;

                    // Concentrated foam on the actual breaking crest, softened at the
                    // edge of the breaker band and merged with the wash continuously.
                    float breakingFoam = fragmentFoamArea * fragmentShoreBreak
                        * saturate(crestPeak * _ShoreFoamGain * 0.85);

                    foam = saturate(max(
                        offshoreFoam,
                        max(washFoam, breakingFoam)));
                }

                // Player / boat contact foam, read from the top-down foam field so it sits
                // exactly on the displaced water surface. Noise breaks the soft stamp
                // edges into lacy foam; it is skipped entirely when no field is active.
                if (_WaterFoamFieldParams.w > 0.0)
                {
                    float2 foamUV = (IN.positionWS.xz - _WaterFoamFieldParams.xy)
                                    * _WaterFoamFieldParams.z;
                    float2 edge = min(foamUV, 1.0 - foamUV);
                    float insideField = saturate(min(edge.x, edge.y) * 24.0);
                    float contact = SAMPLE_TEXTURE2D_LOD(
                        _WaterFoamField, sampler_WaterFoamField, foamUV, 0).r
                        * _WaterFoamFieldParams.w * insideField;

                    float2 noiseP = IN.positionWS.xz;
                    float lace = 0.6 * ValueNoise(noiseP * 1.9 + _Time.y * 0.35)
                               + 0.4 * ValueNoise(noiseP * 6.3 - _Time.y * 0.6);
                    float contactFoam = smoothstep(0.05, 0.4, contact * (0.6 + 0.8 * lace));
                    foam = saturate(max(foam, contactFoam));
                }

                foam = saturate(max(foam, rippleFoam));
                waterColor = lerp(waterColor, _FoamColor.rgb, foam);

                return half4(waterColor, 1.0);
            }

            ENDHLSL
        }
    }
}
