// Copyright (c) 2016 Unity Technologies. MIT license - license_unity.txt
// #NVJOB Simple Boids. MIT license - license_nvjob.txt
// URP port for Unity 6. Material property names are kept compatible with the
// original Fish materials so existing assets retain their settings.
//
// Minimal underwater-depth fix: adds a DepthOnly pass using the same
// vertex deformation as ForwardLit, so the camera depth texture follows
// the animated fish mesh when URP performs a depth prepass.

Shader "#NVJOB/NX Shaders/Boids/Fish"
{
    Properties
    {
        [Header(Basic Settings)][Space(5)]
        [HDR]_Color("Main Color", Color) = (1,1,1,1)
        _MainTex("Base (RGB) Gloss (A)", 2D) = "white" {}
        _Saturation("Saturation", Range(0, 5)) = 1
        _Brightness("Brightness", Range(0, 5)) = 1
        _Contrast("Contrast", Range(0, 5)) = 1

        [Header(Specular Settings)][Space(5)]
        _SpecMap("Base (RGB) Gloss (A)", 2D) = "white" {}
        _SpecMapUV("Specularmap Uv", Float) = 1
        [HDR]_SpecColor("Specular Color", Color) = (0.5, 0.5, 0.5, 1)
        _Shininess("Shininess", Range(0.03, 2)) = 0.078125
        _SpecMapInts("Intensity SpecularMap", Range(0, 5)) = 1

        [Header(Occlusion Settings)][Space(5)]
        _OcclusionMap("Occlusionmap", 2D) = "white" {}
        _OcclusionMapUv("Occlusionmap Uv", Range(0.01, 50)) = 1
        _IntensityOc("Intensity Occlusion", Range(-20, 20)) = 1

        [Header(Normalmap Settings)][Space(5)]
        _BumpMap("Normalmap", 2D) = "bump" {}
        _IntensityNm("Intensity Normalmap", Range(-20, 20)) = 1
        _BumpMapD("Normalmap Detail", 2D) = "bump" {}
        _BumpMapDUV("Normalmap Detail Uv", Float) = 1
        _IntensityNmD("Intensity Normalmap Detail", Range(-20, 20)) = 1

        [Header(Reflection Settings)][Space(5)]
        [HDR]_ReflectColor("Reflection Color", Color) = (1,1,1,0.5)
        _EmissionTex("Emission (RGB) Gloss (A)", 2D) = "white" {}
        _Cube("Reflection Cubemap", Cube) = "" {}
        _IntensityRef("Intensity Reflection", Range(0, 20)) = 1
        _SaturationRef("Saturation Reflection", Range(0, 5)) = 1
        _ContrastRef("Contrast Reflection", Range(0, 5)) = 1
        _BiasNormal("Bias Normal", Range(-5, 5)) = 1

        [Header(Swimming Fish)][Space(5)]
        _SwimmingPower("Swimming Power", Range(0, 50)) = 1
        _SwimmingScale("Swimming Scale", Range(0.001, 50)) = 1
        _SwimmingSpeed("Swimming Speed", Range(0, 50)) = 2
        _WaveBody("Wave Body", Range(-10, 10)) = 0.3
        _Wobble("Wobble", Range(-50, 50)) = 1
        _WaveY("Wave Y", Range(0, 30)) = 0.3
        _WaveYSpeed("Wave Y Speed", Range(0, 30)) = 1
        [HideInInspector] _ScriptControl("Script Control", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }
        LOD 200
        Cull Off
        ZWrite On

        // IMPORTANT: this pass must deform vertices exactly like ForwardLit.
        // URP uses it when generating a depth prepass / camera depth texture.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Keep this UnityPerMaterial layout identical to ForwardLit for SRP Batcher compatibility.
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _ReflectColor;
                float4 _SpecColor;
                float4 _MainTex_ST;
                float4 _SpecMap_ST;
                float4 _OcclusionMap_ST;
                float4 _BumpMap_ST;
                float4 _BumpMapD_ST;
                float4 _EmissionTex_ST;
                half _Saturation;
                half _Brightness;
                half _Contrast;
                half _SpecMapUV;
                half _Shininess;
                half _SpecMapInts;
                half _OcclusionMapUv;
                half _IntensityOc;
                half _IntensityNm;
                half _BumpMapDUV;
                half _IntensityNmD;
                half _IntensityRef;
                half _SaturationRef;
                half _ContrastRef;
                half _BiasNormal;
                half _ScriptControl;
                half _SwimmingPower;
                half _SwimmingScale;
                half _SwimmingSpeed;
                half _WaveBody;
                half _Wobble;
                half _WaveY;
                half _WaveYSpeed;
            CBUFFER_END

            struct DepthOnlyAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthOnlyVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 ApplyFishAnimation(float3 positionOS)
            {
                float3 objectAnchorWS = TransformObjectToWorld(float3(1.0, 1.0, 1.0));
                half sinT = sin((_Time.y + sin(objectAnchorWS.y * 0.5) + _ScriptControl) * _SwimmingSpeed);
                half flap = sinT * _SwimmingPower;
                half zf = (positionOS.z * positionOS.z) - (positionOS.z * _WaveBody);
                positionOS.x += sin(zf / _SwimmingScale) * flap;
                positionOS.y += sin(zf / 10.0) * flap * _Wobble;
                positionOS.y += sin((_Time.y + sin((objectAnchorWS.x + objectAnchorWS.y) * _WaveY * 0.6)) * _WaveYSpeed) * _WaveY;
                return positionOS;
            }

            DepthOnlyVaryings DepthOnlyVertex(DepthOnlyAttributes input)
            {
                DepthOnlyVaryings output = (DepthOnlyVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 animatedPositionOS = ApplyFishAnimation(input.positionOS.xyz);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(animatedPositionOS);
                output.positionCS = positionInputs.positionCS;
                return output;
            }

            half4 DepthOnlyFragment(DepthOnlyVaryings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return 0;
            }
            ENDHLSL
        }

        // This pass is required by URP's depth/normals prepass paths (including
        // Depth Priming / Deferred + UniversalForwardOnly materials). It must use
        // the same animated vertex positions as ForwardLit and DepthOnly.
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }

            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _ReflectColor;
                float4 _SpecColor;
                float4 _MainTex_ST;
                float4 _SpecMap_ST;
                float4 _OcclusionMap_ST;
                float4 _BumpMap_ST;
                float4 _BumpMapD_ST;
                float4 _EmissionTex_ST;
                half _Saturation;
                half _Brightness;
                half _Contrast;
                half _SpecMapUV;
                half _Shininess;
                half _SpecMapInts;
                half _OcclusionMapUv;
                half _IntensityOc;
                half _IntensityNm;
                half _BumpMapDUV;
                half _IntensityNmD;
                half _IntensityRef;
                half _SaturationRef;
                half _ContrastRef;
                half _BiasNormal;
                half _ScriptControl;
                half _SwimmingPower;
                half _SwimmingScale;
                half _SwimmingSpeed;
                half _WaveBody;
                half _Wobble;
                half _WaveY;
                half _WaveYSpeed;
            CBUFFER_END

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 ApplyFishAnimation(float3 positionOS)
            {
                float3 objectAnchorWS = TransformObjectToWorld(float3(1.0, 1.0, 1.0));
                half sinT = sin((_Time.y + sin(objectAnchorWS.y * 0.5) + _ScriptControl) * _SwimmingSpeed);
                half flap = sinT * _SwimmingPower;
                half zf = (positionOS.z * positionOS.z) - (positionOS.z * _WaveBody);
                positionOS.x += sin(zf / _SwimmingScale) * flap;
                positionOS.y += sin(zf / 10.0) * flap * _Wobble;
                positionOS.y += sin((_Time.y + sin((objectAnchorWS.x + objectAnchorWS.y) * _WaveY * 0.6)) * _WaveYSpeed) * _WaveY;
                return positionOS;
            }

            DepthNormalsVaryings DepthNormalsVertex(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output = (DepthNormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 animatedPositionOS = ApplyFishAnimation(input.positionOS.xyz);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(animatedPositionOS);
                output.positionCS = positionInputs.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(DepthNormalsVaryings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normalWS = normalize(input.normalWS);
                return half4(normalWS * 0.5h + 0.5h, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);       SAMPLER(sampler_MainTex);
            TEXTURE2D(_SpecMap);       SAMPLER(sampler_SpecMap);
            TEXTURE2D(_OcclusionMap);  SAMPLER(sampler_OcclusionMap);
            TEXTURE2D(_BumpMap);       SAMPLER(sampler_BumpMap);
            TEXTURE2D(_BumpMapD);      SAMPLER(sampler_BumpMapD);
            TEXTURE2D(_EmissionTex);   SAMPLER(sampler_EmissionTex);
            TEXTURECUBE(_Cube);        SAMPLER(sampler_Cube);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _ReflectColor;
                float4 _SpecColor;
                float4 _MainTex_ST;
                float4 _SpecMap_ST;
                float4 _OcclusionMap_ST;
                float4 _BumpMap_ST;
                float4 _BumpMapD_ST;
                float4 _EmissionTex_ST;
                half _Saturation;
                half _Brightness;
                half _Contrast;
                half _SpecMapUV;
                half _Shininess;
                half _SpecMapInts;
                half _OcclusionMapUv;
                half _IntensityOc;
                half _IntensityNm;
                half _BumpMapDUV;
                half _IntensityNmD;
                half _IntensityRef;
                half _SaturationRef;
                half _ContrastRef;
                half _BiasNormal;
                half _ScriptControl;
                half _SwimmingPower;
                half _SwimmingScale;
                half _SwimmingSpeed;
                half _WaveBody;
                half _Wobble;
                half _WaveY;
                half _WaveYSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 ApplyFishAnimation(float3 positionOS)
            {
                float3 objectAnchorWS = TransformObjectToWorld(float3(1.0, 1.0, 1.0));
                half sinT = sin((_Time.y + sin(objectAnchorWS.y * 0.5) + _ScriptControl) * _SwimmingSpeed);
                half flap = sinT * _SwimmingPower;
                half zf = (positionOS.z * positionOS.z) - (positionOS.z * _WaveBody);
                positionOS.x += sin(zf / _SwimmingScale) * flap;
                positionOS.y += sin(zf / 10.0) * flap * _Wobble;
                positionOS.y += sin((_Time.y + sin((objectAnchorWS.x + objectAnchorWS.y) * _WaveY * 0.6)) * _WaveYSpeed) * _WaveY;
                return positionOS;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 animatedPositionOS = ApplyFishAnimation(input.positionOS.xyz);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(animatedPositionOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
                output.uv = input.uv;
                output.shadowCoord = TransformWorldToShadowCoord(output.positionWS);
                return output;
            }

            half3 EvaluateLight(Light light, half3 normalWS, half3 viewDirWS, half3 albedo, half specMask)
            {
                half ndotl = saturate(dot(normalWS, light.direction));
                half3 halfVector = SafeNormalize(light.direction + viewDirWS);
                half specularPower = exp2(10.0h * saturate(_SpecColor.a) + 1.0h);
                half specular = pow(saturate(dot(normalWS, halfVector)), specularPower) * specMask * _Shininess;
                half3 diffuseTerm = albedo * ndotl;
                half3 specularTerm = _SpecColor.rgb * specular;
                return (diffuseTerm + specularTerm) * light.color * light.distanceAttenuation * light.shadowAttenuation;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uvMain = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                half4 baseSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvMain) * _Color;
                half luminance = dot(baseSample.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                half3 albedo = lerp(luminance.xxx, baseSample.rgb, _Saturation) * _Brightness;
                albedo = (albedo - 0.5h) * _Contrast + 0.5h;

                float2 occlusionUV = input.uv * _OcclusionMap_ST.xy * _OcclusionMapUv + _OcclusionMap_ST.zw;
                half occlusion = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, occlusionUV).r;
                occlusion = (occlusion - 0.5h) * _IntensityOc + 0.5h;
                albedo *= occlusion;

                float2 normalUV = input.uv * _BumpMap_ST.xy + _BumpMap_ST.zw;
                half3 normalA = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, normalUV), _IntensityNm);
                float2 detailNormalUV = input.uv * _BumpMapD_ST.xy * _BumpMapDUV + _BumpMapD_ST.zw;
                half3 normalB = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMapD, sampler_BumpMapD, detailNormalUV), _IntensityNmD);
                half3 normalTS = normalize(half3(normalA.xy + normalB.xy, normalA.z * normalB.z));
                normalTS = normalize(lerp(half3(0.0h, 0.0h, 1.0h), normalTS, _BiasNormal));

                half3 normalWS = normalize(input.normalWS);
                half3 tangentWS = normalize(input.tangentWS.xyz);
                half3 bitangentWS = normalize(cross(normalWS, tangentWS) * input.tangentWS.w);
                normalWS = normalize(tangentWS * normalTS.x + bitangentWS * normalTS.y + normalWS * normalTS.z);

                float2 specUV = (input.uv * _SpecMap_ST.xy + _SpecMap_ST.zw) * _SpecMapUV;
                half specMask = SAMPLE_TEXTURE2D(_SpecMap, sampler_SpecMap, specUV).g;
                specMask = (specMask - 0.5h) * _SpecMapInts + 0.5h;

                half3 viewDirWS = SafeNormalize(GetCameraPositionWS() - input.positionWS);
                half3 lighting = SampleSH(normalWS) * albedo;
                Light mainLight = GetMainLight(input.shadowCoord);
                lighting += EvaluateLight(mainLight, normalWS, viewDirWS, albedo, specMask);

                #if defined(_ADDITIONAL_LIGHTS)
                    // Forward+ handles non-main directional lights separately.
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalWS = normalWS;
                    inputData.viewDirectionWS = viewDirWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                    #if USE_CLUSTER_LIGHT_LOOP
                        UNITY_LOOP for (uint lightIndex = 0u; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++lightIndex)
                        {
                            Light additionalLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                            lighting += EvaluateLight(additionalLight, normalWS, viewDirWS, albedo, specMask);
                        }
                    #endif

                    uint pixelLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light additionalLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                        lighting += EvaluateLight(additionalLight, normalWS, viewDirWS, albedo, specMask);
                    LIGHT_LOOP_END
                #endif

                half3 reflectionDir = reflect(-viewDirWS, normalWS);
                half3 reflection = SAMPLE_TEXTURECUBE(_Cube, sampler_Cube, reflectionDir).rgb * _IntensityRef;
                half reflectionLuminance = dot(reflection, half3(0.2126h, 0.7152h, 0.0722h));
                half3 reflectionColor = lerp(reflectionLuminance.xxx, reflection, _SaturationRef);
                reflectionColor = (reflectionColor - 0.5h) * _ContrastRef + 0.5h;
                half3 emission = SAMPLE_TEXTURE2D(_EmissionTex, sampler_EmissionTex, input.uv * _EmissionTex_ST.xy + _EmissionTex_ST.zw).rgb;
                emission *= reflectionColor * _ReflectColor.rgb;

                return half4(lighting + emission, baseSample.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowPassVertex(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float3 biasedPositionWS = ApplyShadowBias(positionWS, normalWS, lightDirectionWS);
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(biasedPositionWS));
                return output;
            }

            half4 ShadowPassFragment(ShadowVaryings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
