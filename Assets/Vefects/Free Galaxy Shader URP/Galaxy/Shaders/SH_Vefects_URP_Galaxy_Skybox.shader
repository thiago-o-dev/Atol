Shader "Vefects/Skybox/SH_Vefects_URP_Galaxy"
{
    Properties
    {
        [Space(33)][Header(Galaxy Background)][Space(13)]
        [NoScaleOffset] _GalaxyTexture("Galaxy Texture", Cube) = "white" {}
        _GalaxyEmissionIntensity("Galaxy Emission Intensity", Float) = 1
        _GalaxyBaseColorIntensity("Galaxy Base Color Intensity", Range(0, 1)) = 0
        _GalaxyTint("Galaxy Tint", Color) = (1,1,1,0)
        _GalaxyHueShift("Galaxy Hue Shift", Float) = 0
        _GalaxySaturationShift("Galaxy Saturation Shift", Float) = 0

        [Space(33)][Header(Stars)][Space(13)]
        _StarsTexture("Stars Texture", 2D) = "white" {}
        _StarsTile("Stars Tile", Vector) = (1,1,0,0)
        _StarsTileOverall("Stars Tile Overall", Float) = 1
        _StarsSpeed("Stars Speed", Vector) = (-0.05,-0.1,0,0)
        _StarsColor01("Stars Color 01", Color) = (0,0.9407032,1,0)
        _StarsColor02("Stars Color 02", Color) = (0.7019608,0,1,0)
        _StarsHueShift("Stars Hue Shift", Float) = 0
        _StarsEmissionIntensity("Stars Emission Intensity", Float) = 333
        _StarsDistortion("Stars Distortion", Float) = 0

        [Space(33)][Header(Stars Noise)][Space(13)]
        _StarsNoiseTexture("Stars Noise Texture", 2D) = "black" {}
        _StarsNoiseTile("Stars Noise Tile", Vector) = (1,1,0,0)
        _StarsNoiseTileOverall("Stars Noise Tile Overall", Float) = 1
        _StarsNoiseSpeed("Stars Noise Speed", Vector) = (0.1,-0.05,0,0)

        [Space(33)][Header(Skybox Glow (Fresnel Adaptation))][Space(13)]
        _FresnelColor("Fresnel Color", Color) = (0,0.6859784,1,0)
        _FresnelHueShift("Fresnel Hue Shift", Float) = 0
        _FresnelEmissionIntensity("Fresnel Emission Intensity", Float) = 13
        _FresnelBias("Fresnel Bias", Float) = 0
        _FresnelScale("Fresnel Scale", Float) = 1
        _FresnelPower("Fresnel Power", Float) = 5
        [Toggle(_FRESNELINVERT_ON)] _FresnelInvert("Fresnel Invert", Float) = 0
        _FresnelHorizon("Glow Horizon Height", Range(-1, 1)) = 0
        _FresnelWidth("Glow Horizon Width", Range(0.001, 1)) = 0.25

        [Space(33)][Header(Cubemap Direction)][Space(13)]
        [Toggle] _UseReflectionVector("Flip Galaxy Cubemap Direction", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Skybox"
        }

        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            Name "GalaxySkybox"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _FRESNELINVERT_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURECUBE(_GalaxyTexture);
            SAMPLER(sampler_GalaxyTexture);
            TEXTURE2D(_StarsTexture);
            SAMPLER(sampler_StarsTexture);
            TEXTURE2D(_StarsNoiseTexture);
            SAMPLER(sampler_StarsNoiseTexture);

            CBUFFER_START(UnityPerMaterial)
                float _GalaxyEmissionIntensity;
                float _GalaxyBaseColorIntensity;
                float4 _GalaxyTint;
                float _GalaxyHueShift;
                float _GalaxySaturationShift;

                float4 _StarsTexture_ST;
                float4 _StarsNoiseTexture_ST;
                float4 _StarsTile;
                float _StarsTileOverall;
                float4 _StarsSpeed;
                float4 _StarsColor01;
                float4 _StarsColor02;
                float _StarsHueShift;
                float _StarsEmissionIntensity;
                float _StarsDistortion;

                float4 _StarsNoiseTile;
                float _StarsNoiseTileOverall;
                float4 _StarsNoiseSpeed;

                float4 _FresnelColor;
                float _FresnelHueShift;
                float _FresnelEmissionIntensity;
                float _FresnelBias;
                float _FresnelScale;
                float _FresnelPower;
                float _FresnelInvert;
                float _FresnelHorizon;
                float _FresnelWidth;
                float _UseReflectionVector;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 RGBToHSV(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 HSVToRGB(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            // Equirectangular UVs let the original 2D star/noise maps wrap around the sky.
            float2 DirectionToSkyUV(float3 direction)
            {
                direction = normalize(direction);
                float2 uv;
                uv.x = atan2(direction.x, direction.z) * (0.5 / PI) + 0.5;
                uv.y = asin(clamp(direction.y, -1.0, 1.0)) * (1.0 / PI) + 0.5;
                return uv;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Skyboxes must sit on the far clip plane so opaque scene geometry wins the depth test.
                output.positionCS.z = UNITY_RAW_FAR_CLIP_VALUE * output.positionCS.w;
                output.directionWS = TransformObjectToWorldDir(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 viewDirection = normalize(input.directionWS);
                // The source material sampled the camera-to-surface view vector by default.
                // A skybox ray points the other way, so the default keeps that same cubemap orientation.
                float3 cubeDirection = (_UseReflectionVector > 0.5) ? viewDirection : -viewDirection;

                // Galaxy cubemap: preserve the source shader's HSV, tint and emission processing.
                float3 galaxy = SAMPLE_TEXTURECUBE(_GalaxyTexture, sampler_GalaxyTexture, cubeDirection).rgb;
                float3 galaxyHSV = RGBToHSV(galaxy);
                float3 galaxyRGB = HSVToRGB(float3(
                    galaxyHSV.x + _GalaxyHueShift,
                    galaxyHSV.y + _GalaxySaturationShift,
                    galaxyHSV.z));
                float3 galaxyEmission = saturate(galaxyRGB) * _GalaxyTint.rgb * _GalaxyEmissionIntensity;

                // A skybox has no lit surface, so Base Color Intensity is represented as
                // an additional unlit galaxy fill contribution (0 leaves the source emission unchanged).
                float3 color = galaxyEmission * (1.0 + _GalaxyBaseColorIntensity);

                // Animated star and noise maps use spherical UVs instead of mesh UVs.
                float2 skyUV = DirectionToSkyUV(viewDirection);

                float2 starsUV = skyUV * _StarsTexture_ST.xy + _StarsTexture_ST.zw;
                starsUV = (starsUV * _StarsTile.xy) * _StarsTileOverall;
                starsUV += _Time.y * _StarsSpeed.xy;

                float2 noiseUV = skyUV * _StarsNoiseTexture_ST.xy + _StarsNoiseTexture_ST.zw;
                noiseUV = (noiseUV * _StarsNoiseTile.xy) * _StarsNoiseTileOverall;
                noiseUV += _Time.y * _StarsNoiseSpeed.xy;

                float4 noiseSample = SAMPLE_TEXTURE2D(_StarsNoiseTexture, sampler_StarsNoiseTexture, noiseUV);
                float4 starsSample = SAMPLE_TEXTURE2D(
                    _StarsTexture,
                    sampler_StarsTexture,
                    starsUV + float2(_StarsDistortion * noiseSample.g, _StarsDistortion * noiseSample.g));

                // Match the original shader's green-channel masking and color interpolation.
                float4 starsLerp = lerp(
                    _StarsColor01,
                    _StarsColor02,
                    saturate((starsSample.g * noiseSample.g) * 30.0));
                float3 starsMasked = lerp(float3(0.0, 0.0, 0.0), starsLerp.rgb, starsSample.g);
                float3 starsHSV = RGBToHSV(starsMasked);
                float3 starsRGB = HSVToRGB(float3(starsHSV.x + _StarsHueShift, starsHSV.y, starsHSV.z));
                color += saturate(starsRGB) * _StarsEmissionIntensity;

                // Fresnel needs a surface normal, which a skybox does not have.
                // Replace it with an adjustable glow band around a chosen horizon elevation.
                float horizonDistance = abs(viewDirection.y - _FresnelHorizon);
                float horizonNdotV = saturate(horizonDistance / max(_FresnelWidth, 0.001));
                float fresnel = _FresnelBias + _FresnelScale * pow(
                    max(1.0 - horizonNdotV, 0.0001),
                    max(_FresnelPower, 0.0001));
                float fresnelMask = saturate(fresnel);
                #if defined(_FRESNELINVERT_ON)
                    fresnelMask = 1.0 - fresnelMask;
                #endif

                float3 fresnelTinted = _FresnelColor.rgb * fresnelMask;
                float3 fresnelHSV = RGBToHSV(fresnelTinted);
                float3 fresnelRGB = HSVToRGB(float3(
                    fresnelHSV.x + _FresnelHueShift,
                    fresnelHSV.y,
                    fresnelHSV.z));
                color += saturate(fresnelRGB) * _FresnelEmissionIntensity;

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
