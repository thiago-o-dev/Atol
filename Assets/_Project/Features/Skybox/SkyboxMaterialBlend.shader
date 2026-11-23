Shader "Custom/SkyboxMaterialBlend"
{
    // Skybox 1 : always a Cubemap (fixed).
    // Skybox 2 : Cubemap OR Vefects Galaxy (selected by _Sky2Mode, set automatically by SkyboxController).
    Properties
    {
        _FadeRatio ("Fade Ratio", Range(0, 1)) = 0
        _Rotation ("Rotation", Range(0, 360)) = 0

        [Space(20)][Header(Skybox 1 (Cubemap))]
        [NoScaleOffset] _Tex1 ("Cubemap 1", Cube) = "grey" {}
        _Tint1 ("Tint 1", Color) = (.5, .5, .5, .5)
        _Exposure1 ("Exposure 1", Range(0, 8)) = 1.0

        [Space(20)][Header(Skybox 2)]
        [Enum(Cubemap, 0, Galaxy, 1)] _Sky2Mode ("Skybox 2 Mode", Float) = 0

        [Space(10)][Header(Skybox 2 as Cubemap)]
        [NoScaleOffset] _Tex2 ("Cubemap 2", Cube) = "grey" {}
        _Tint2 ("Tint 2", Color) = (.5, .5, .5, .5)
        _Exposure2 ("Exposure 2", Range(0, 8)) = 1.0

        [Space(20)][Header(Skybox 2 as Galaxy  Background)]
        [NoScaleOffset] _GalaxyTexture("Galaxy Texture", Cube) = "white" {}
        _GalaxyEmissionIntensity("Galaxy Emission Intensity", Float) = 1
        _GalaxyBaseColorIntensity("Galaxy Base Color Intensity", Range(0, 1)) = 0
        _GalaxyTint("Galaxy Tint", Color) = (1,1,1,0)
        _GalaxyHueShift("Galaxy Hue Shift", Float) = 0
        _GalaxySaturationShift("Galaxy Saturation Shift", Float) = 0

        [Space(10)][Header(Skybox 2 as Galaxy  Stars)]
        _StarsTexture("Stars Texture", 2D) = "white" {}
        _StarsTile("Stars Tile", Vector) = (1,1,0,0)
        _StarsTileOverall("Stars Tile Overall", Float) = 1
        _StarsSpeed("Stars Speed", Vector) = (-0.05,-0.1,0,0)
        _StarsColor01("Stars Color 01", Color) = (0,0.9407032,1,0)
        _StarsColor02("Stars Color 02", Color) = (0.7019608,0,1,0)
        _StarsHueShift("Stars Hue Shift", Float) = 0
        _StarsEmissionIntensity("Stars Emission Intensity", Float) = 333
        _StarsDistortion("Stars Distortion", Float) = 0

        [Space(10)][Header(Skybox 2 as Galaxy  Stars Noise)]
        _StarsNoiseTexture("Stars Noise Texture", 2D) = "black" {}
        _StarsNoiseTile("Stars Noise Tile", Vector) = (1,1,0,0)
        _StarsNoiseTileOverall("Stars Noise Tile Overall", Float) = 1
        _StarsNoiseSpeed("Stars Noise Speed", Vector) = (0.1,-0.05,0,0)

        [Space(10)][Header(Skybox 2 as Galaxy  Glow)]
        _FresnelColor("Fresnel Color", Color) = (0,0.6859784,1,0)
        _FresnelHueShift("Fresnel Hue Shift", Float) = 0
        _FresnelEmissionIntensity("Fresnel Emission Intensity", Float) = 13
        _FresnelBias("Fresnel Bias", Float) = 0
        _FresnelScale("Fresnel Scale", Float) = 1
        _FresnelPower("Fresnel Power", Float) = 5
        [Toggle] _FresnelInvert("Fresnel Invert", Float) = 0
        _FresnelHorizon("Glow Horizon Height", Range(-1, 1)) = 0
        _FresnelWidth("Glow Horizon Width", Range(0.001, 1)) = 0.25

        [Space(10)][Header(Skybox 2 as Galaxy  Cubemap Direction)]
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
            Name "SkyboxMaterialBlend"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURECUBE(_Tex1);              SAMPLER(sampler_Tex1);
            TEXTURECUBE(_Tex2);              SAMPLER(sampler_Tex2);
            TEXTURECUBE(_GalaxyTexture);     SAMPLER(sampler_GalaxyTexture);
            TEXTURE2D(_StarsTexture);        SAMPLER(sampler_StarsTexture);
            TEXTURE2D(_StarsNoiseTexture);   SAMPLER(sampler_StarsNoiseTexture);

            CBUFFER_START(UnityPerMaterial)
                float _FadeRatio;
                float _Rotation;

                float4 _Tex1_HDR;
                float4 _Tint1;
                float _Exposure1;

                float _Sky2Mode;
                float4 _Tex2_HDR;
                float4 _Tint2;
                float _Exposure2;

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
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ---------------------------------------------------------------- helpers

            float3 RotateAroundYInDegrees(float3 v, float degrees)
            {
                float alpha = degrees * PI / 180.0;
                float s, c;
                sincos(alpha, s, c);
                float2x2 m = float2x2(c, -s, s, c);
                return float3(mul(m, v.xz), v.y).xzy;
            }

            // Same as UnityCG DecodeHDR (linear path), so we do not depend on UnityCG.cginc.
            float3 DecodeCubeHDR(float4 data, float4 decode)
            {
                float alpha = decode.w * (data.a - 1.0) + 1.0;
                return (decode.x * pow(alpha, decode.y)) * data.rgb;
            }

            // Equals unity_ColorSpaceDouble.rgb (2.0 in gamma, ~4.59 in linear).
            float ColorSpaceDouble()
            {
                #if defined(UNITY_COLORSPACE_GAMMA)
                    return 2.0;
                #else
                    return 4.59479380;
                #endif
            }

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

            float2 DirectionToSkyUV(float3 direction)
            {
                direction = normalize(direction);
                float2 uv;
                uv.x = atan2(direction.x, direction.z) * (0.5 / PI) + 0.5;
                uv.y = asin(clamp(direction.y, -1.0, 1.0)) * (1.0 / PI) + 0.5;
                return uv;
            }

            // ---------------------------------------------------------------- skybox evaluators

            float3 EvalCube1(float3 dir)
            {
                float4 t = SAMPLE_TEXTURECUBE(_Tex1, sampler_Tex1, dir);
                return DecodeCubeHDR(t, _Tex1_HDR) * _Tint1.rgb * _Exposure1 * ColorSpaceDouble();
            }

            float3 EvalCube2(float3 dir)
            {
                float4 t = SAMPLE_TEXTURECUBE(_Tex2, sampler_Tex2, dir);
                return DecodeCubeHDR(t, _Tex2_HDR) * _Tint2.rgb * _Exposure2 * ColorSpaceDouble();
            }

            // Vefects Galaxy, ported 1:1 from SH_Vefects_URP_Galaxy.
            float3 EvalGalaxy(float3 viewDirection)
            {
                float3 cubeDirection = (_UseReflectionVector > 0.5) ? viewDirection : -viewDirection;

                float3 galaxy = SAMPLE_TEXTURECUBE(_GalaxyTexture, sampler_GalaxyTexture, cubeDirection).rgb;
                float3 galaxyHSV = RGBToHSV(galaxy);
                float3 galaxyRGB = HSVToRGB(float3(
                    galaxyHSV.x + _GalaxyHueShift,
                    galaxyHSV.y + _GalaxySaturationShift,
                    galaxyHSV.z));
                float3 galaxyEmission = saturate(galaxyRGB) * _GalaxyTint.rgb * _GalaxyEmissionIntensity;
                float3 color = galaxyEmission * (1.0 + _GalaxyBaseColorIntensity);

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

                float4 starsLerp = lerp(
                    _StarsColor01,
                    _StarsColor02,
                    saturate((starsSample.g * noiseSample.g) * 30.0));
                float3 starsMasked = lerp(float3(0.0, 0.0, 0.0), starsLerp.rgb, starsSample.g);
                float3 starsHSV = RGBToHSV(starsMasked);
                float3 starsRGB = HSVToRGB(float3(starsHSV.x + _StarsHueShift, starsHSV.y, starsHSV.z));
                color += saturate(starsRGB) * _StarsEmissionIntensity;

                float horizonDistance = abs(viewDirection.y - _FresnelHorizon);
                float horizonNdotV = saturate(horizonDistance / max(_FresnelWidth, 0.001));
                float fresnel = _FresnelBias + _FresnelScale * pow(
                    max(1.0 - horizonNdotV, 0.0001),
                    max(_FresnelPower, 0.0001));
                float fresnelMask = saturate(fresnel);
                if (_FresnelInvert > 0.5)
                    fresnelMask = 1.0 - fresnelMask;

                float3 fresnelTinted = _FresnelColor.rgb * fresnelMask;
                float3 fresnelHSV = RGBToHSV(fresnelTinted);
                float3 fresnelRGB = HSVToRGB(float3(
                    fresnelHSV.x + _FresnelHueShift,
                    fresnelHSV.y,
                    fresnelHSV.z));
                color += saturate(fresnelRGB) * _FresnelEmissionIntensity;

                return color;
            }

            // ---------------------------------------------------------------- vertex / fragment

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(RotateAroundYInDegrees(input.positionOS.xyz, _Rotation));
                output.positionCS.z = UNITY_RAW_FAR_CLIP_VALUE * output.positionCS.w;
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 dir = normalize(input.direction);

                // _FadeRatio / _Sky2Mode are uniform, so these branches are coherent
                // and the unused skybox costs nothing at the endpoints (0 or 1).
                float3 c1 = 0;
                float3 c2 = 0;

                [branch] if (_FadeRatio < 0.9999)
                    c1 = EvalCube1(dir);

                [branch] if (_FadeRatio > 0.0001)
                {
                    [branch] if (_Sky2Mode > 0.5)
                        c2 = EvalGalaxy(dir);
                    else
                        c2 = EvalCube2(dir);
                }

                return half4(lerp(c1, c2, _FadeRatio), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}