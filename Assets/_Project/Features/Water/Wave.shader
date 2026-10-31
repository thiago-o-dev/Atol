Shader "Custom/Wave"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}

        [Header(Water Colors)]
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
            "Queue" = "Geometry"
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
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // Use undeformed world position as the simulation coordinate.
                // This keeps the waves anchored in world space when the player moves.
                float3 basePositionWS = TransformObjectToWorld(IN.positionOS.xyz);

                float2 texelCenterOffset = _OceanHalfTexel.xx;
                float2 uv0 = basePositionWS.xz / max(_OceanPatchLength0, 1.0) + texelCenterOffset;
                float2 uv1 = basePositionWS.xz / max(_OceanPatchLength1, 1.0) + texelCenterOffset;
                float2 uv2 = basePositionWS.xz / max(_OceanPatchLength2, 1.0) + texelCenterOffset;

                float4 displacement0 = SAMPLE_TEXTURE2D_LOD(
                    _OceanDisplacement0, sampler_OceanDisplacement0, uv0, 0);
                float4 displacement1 = SAMPLE_TEXTURE2D_LOD(
                    _OceanDisplacement1, sampler_OceanDisplacement1, uv1, 0);

                // The finest cascade is normal/foam detail only. Keeping it out
                // of geometry avoids vertex aliasing on ordinary ocean grids.
                float3 displacementWS = float3(
                    displacement0.x + displacement1.x,
                    displacement0.y + displacement1.y,
                    displacement0.z + displacement1.z
                );

                float3 displacedPositionWS = basePositionWS + displacementWS;

                OUT.positionHCS = TransformWorldToHClip(displacedPositionWS);
                OUT.positionWS = displacedPositionWS;
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

                // The controller sets _OceanEnabled only after all GPU
                // resources have been allocated and bound. During startup or
                // teardown, use a valid flat normal rather than normalizing the
                // black fallback textures (which would produce undefined data).
                float3 n0 = float3(0.0, 1.0, 0.0);
                float3 n1 = float3(0.0, 1.0, 0.0);
                float3 n2 = float3(0.0, 1.0, 0.0);

                if (_OceanEnabled > 0.5)
                {
                    n0 = normalize(normalData0.xyz);
                    n1 = normalize(normalData1.xyz);
                    n2 = normalize(normalData2.xyz);
                }

                // Combine the cascades as surface gradients. This is more stable
                // than simply averaging three normalized normal vectors.
                float2 combinedSlope =
                    n0.xz / max(n0.y, 0.12) +
                    n1.xz / max(n1.y, 0.12) +
                    n2.xz / max(n2.y, 0.12);

                float3 normalWS = normalize(float3(
                    combinedSlope.x, 1.0, combinedSlope.y));

                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 sunDir = normalize(_SunDirection.xyz);

                float nDotV = saturate(dot(normalWS, viewDir));
                float nDotL = saturate(dot(normalWS, sunDir));

                // Fresnel and the wave-peak mask drive the water's color shift.
                float fresnel = pow(1.0 - nDotV, _FresnelPower);
                float peakMask = saturate(
                    (1.0 - n0.y) * 0.65 +
                    (1.0 - n1.y) * 0.25 +
                    (1.0 - n2.y) * 0.10);

                float subsurface = saturate(
                    peakMask * _SubsurfaceStrength +
                    (1.0 - nDotV) * 0.16);

                half3 waterColor = lerp(
                    _DeepColor.rgb,
                    _ShallowColor.rgb,
                    subsurface);

                waterColor *= baseSample.rgb;
                waterColor *= 0.35 + 0.65 * nDotL;

                // Broad sun glint, deliberately softer than a tiny hard highlight.
                float3 halfVector = normalize(viewDir + sunDir);
                float specular = pow(
                    saturate(dot(normalWS, halfVector)),
                    _SpecularPower) * _SpecularStrength;

                waterColor += _SunColor.rgb * specular;
                waterColor += _FresnelColor.rgb * fresnel * _FresnelStrength;

                // Union the whitecap masks from the three frequency bands.
                float foam = 0.0;
                if (_OceanEnabled > 0.5)
                {
                    foam = 1.0 -
                        (1.0 - saturate(displacementData0.a)) *
                        (1.0 - saturate(displacementData1.a)) *
                        (1.0 - saturate(displacementData2.a));
                }

                waterColor = lerp(waterColor, _FoamColor.rgb, saturate(foam));

                return half4(waterColor, 1.0);
            }

            ENDHLSL
        }
    }
}
