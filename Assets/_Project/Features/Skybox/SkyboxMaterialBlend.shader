Shader "Custom/SkyboxMaterialBlend"
{
    // Both sides of the blend can be a Cubemap OR a Vefects Galaxy:
    //   Side 1 (fade = 0) : _Sky1Mode  -> _Tex1 / _Tint1 / _Exposure1   or Galaxy (suffix _S1)
    //   Side 2 (fade = 1) : _Sky2Mode  -> _Tex2 / _Tint2 / _Exposure2   or Galaxy (no suffix)
    // Values are filled in by SkyboxController.
    Properties
    {
        _FadeRatio ("Fade Ratio", Range(0, 1)) = 0
        _Rotation ("Rotation", Range(0, 360)) = 0

        [Space(20)][Header(Side 1)]
        [Enum(Cubemap, 0, Galaxy, 1)] _Sky1Mode ("Side 1 Mode", Float) = 0
        [NoScaleOffset] _Tex1 ("Cubemap 1", Cube) = "grey" {}
        _Tint1 ("Tint 1", Color) = (.5, .5, .5, .5)
        _Exposure1 ("Exposure 1", Range(0, 8)) = 1.0

        [Space(10)][Header(Side 1 as Galaxy  Background)]
        [NoScaleOffset] _GalaxyTexture_S1("Galaxy Texture", Cube) = "white" {}
        _GalaxyEmissionIntensity_S1("Galaxy Emission Intensity", Float) = 1
        _GalaxyBaseColorIntensity_S1("Galaxy Base Color Intensity", Range(0, 1)) = 0
        _GalaxyTint_S1("Galaxy Tint", Color) = (1,1,1,0)
        _GalaxyHueShift_S1("Galaxy Hue Shift", Float) = 0
        _GalaxySaturationShift_S1("Galaxy Saturation Shift", Float) = 0

        [Space(10)][Header(Side 1 as Galaxy  Stars)]
        _StarsTexture_S1("Stars Texture", 2D) = "white" {}
        _StarsTile_S1("Stars Tile", Vector) = (1,1,0,0)
        _StarsTileOverall_S1("Stars Tile Overall", Float) = 1
        _StarsSpeed_S1("Stars Speed", Vector) = (-0.05,-0.1,0,0)
        _StarsColor01_S1("Stars Color 01", Color) = (0,0.9407032,1,0)
        _StarsColor02_S1("Stars Color 02", Color) = (0.7019608,0,1,0)
        _StarsHueShift_S1("Stars Hue Shift", Float) = 0
        _StarsEmissionIntensity_S1("Stars Emission Intensity", Float) = 333
        _StarsDistortion_S1("Stars Distortion", Float) = 0

        [Space(10)][Header(Side 1 as Galaxy  Stars Noise)]
        _StarsNoiseTexture_S1("Stars Noise Texture", 2D) = "black" {}
        _StarsNoiseTile_S1("Stars Noise Tile", Vector) = (1,1,0,0)
        _StarsNoiseTileOverall_S1("Stars Noise Tile Overall", Float) = 1
        _StarsNoiseSpeed_S1("Stars Noise Speed", Vector) = (0.1,-0.05,0,0)

        [Space(10)][Header(Side 1 as Galaxy  Glow)]
        _FresnelColor_S1("Fresnel Color", Color) = (0,0.6859784,1,0)
        _FresnelHueShift_S1("Fresnel Hue Shift", Float) = 0
        _FresnelEmissionIntensity_S1("Fresnel Emission Intensity", Float) = 13
        _FresnelBias_S1("Fresnel Bias", Float) = 0
        _FresnelScale_S1("Fresnel Scale", Float) = 1
        _FresnelPower_S1("Fresnel Power", Float) = 5
        [Toggle] _FresnelInvert_S1("Fresnel Invert", Float) = 0
        _FresnelHorizon_S1("Glow Horizon Height", Range(-1, 1)) = 0
        _FresnelWidth_S1("Glow Horizon Width", Range(0.001, 1)) = 0.25

        [Space(10)][Header(Side 1 as Galaxy  Cubemap Direction)]
        [Toggle] _UseReflectionVector_S1("Flip Galaxy Cubemap Direction", Float) = 0

        [Space(20)][Header(Side 2)]
        [Enum(Cubemap, 0, Galaxy, 1)] _Sky2Mode ("Side 2 Mode", Float) = 0
        [NoScaleOffset] _Tex2 ("Cubemap 2", Cube) = "grey" {}
        _Tint2 ("Tint 2", Color) = (.5, .5, .5, .5)
        _Exposure2 ("Exposure 2", Range(0, 8)) = 1.0

        [Space(10)][Header(Side 2 as Galaxy  Background)]
        [NoScaleOffset] _GalaxyTexture("Galaxy Texture", Cube) = "white" {}
        _GalaxyEmissionIntensity("Galaxy Emission Intensity", Float) = 1
        _GalaxyBaseColorIntensity("Galaxy Base Color Intensity", Range(0, 1)) = 0
        _GalaxyTint("Galaxy Tint", Color) = (1,1,1,0)
        _GalaxyHueShift("Galaxy Hue Shift", Float) = 0
        _GalaxySaturationShift("Galaxy Saturation Shift", Float) = 0

        [Space(10)][Header(Side 2 as Galaxy  Stars)]
        _StarsTexture("Stars Texture", 2D) = "white" {}
        _StarsTile("Stars Tile", Vector) = (1,1,0,0)
        _StarsTileOverall("Stars Tile Overall", Float) = 1
        _StarsSpeed("Stars Speed", Vector) = (-0.05,-0.1,0,0)
        _StarsColor01("Stars Color 01", Color) = (0,0.9407032,1,0)
        _StarsColor02("Stars Color 02", Color) = (0.7019608,0,1,0)
        _StarsHueShift("Stars Hue Shift", Float) = 0
        _StarsEmissionIntensity("Stars Emission Intensity", Float) = 333
        _StarsDistortion("Stars Distortion", Float) = 0

        [Space(10)][Header(Side 2 as Galaxy  Stars Noise)]
        _StarsNoiseTexture("Stars Noise Texture", 2D) = "black" {}
        _StarsNoiseTile("Stars Noise Tile", Vector) = (1,1,0,0)
        _StarsNoiseTileOverall("Stars Noise Tile Overall", Float) = 1
        _StarsNoiseSpeed("Stars Noise Speed", Vector) = (0.1,-0.05,0,0)

        [Space(10)][Header(Side 2 as Galaxy  Glow)]
        _FresnelColor("Fresnel Color", Color) = (0,0.6859784,1,0)
        _FresnelHueShift("Fresnel Hue Shift", Float) = 0
        _FresnelEmissionIntensity("Fresnel Emission Intensity", Float) = 13
        _FresnelBias("Fresnel Bias", Float) = 0
        _FresnelScale("Fresnel Scale", Float) = 1
        _FresnelPower("Fresnel Power", Float) = 5
        [Toggle] _FresnelInvert("Fresnel Invert", Float) = 0
        _FresnelHorizon("Glow Horizon Height", Range(-1, 1)) = 0
        _FresnelWidth("Glow Horizon Width", Range(0.001, 1)) = 0.25

        [Space(10)][Header(Side 2 as Galaxy  Cubemap Direction)]
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

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURECUBE(_Tex1);              SAMPLER(sampler_Tex1);
            TEXTURECUBE(_Tex2);              SAMPLER(sampler_Tex2);
            TEXTURECUBE(_GalaxyTexture_S1);    SAMPLER(sampler_GalaxyTexture_S1);
            TEXTURE2D(_StarsTexture_S1);       SAMPLER(sampler_StarsTexture_S1);
            TEXTURE2D(_StarsNoiseTexture_S1);  SAMPLER(sampler_StarsNoiseTexture_S1);
            TEXTURECUBE(_GalaxyTexture);    SAMPLER(sampler_GalaxyTexture);
            TEXTURE2D(_StarsTexture);       SAMPLER(sampler_StarsTexture);
            TEXTURE2D(_StarsNoiseTexture);  SAMPLER(sampler_StarsNoiseTexture);

            CBUFFER_START(UnityPerMaterial)
                float _FadeRatio;
                float _Rotation;

                float _Sky1Mode;
                float4 _Tex1_HDR;
                float4 _Tint1;
                float _Exposure1;

                float _Sky2Mode;
                float4 _Tex2_HDR;
                float4 _Tint2;
                float _Exposure2;

                float _GalaxyEmissionIntensity_S1;
                float _GalaxyBaseColorIntensity_S1;
                float4 _GalaxyTint_S1;
                float _GalaxyHueShift_S1;
                float _GalaxySaturationShift_S1;
                float4 _StarsTexture_S1_ST;
                float4 _StarsNoiseTexture_S1_ST;
                float4 _StarsTile_S1;
                float _StarsTileOverall_S1;
                float4 _StarsSpeed_S1;
                float4 _StarsColor01_S1;
                float4 _StarsColor02_S1;
                float _StarsHueShift_S1;
                float _StarsEmissionIntensity_S1;
                float _StarsDistortion_S1;
                float4 _StarsNoiseTile_S1;
                float _StarsNoiseTileOverall_S1;
                float4 _StarsNoiseSpeed_S1;
                float4 _FresnelColor_S1;
                float _FresnelHueShift_S1;
                float _FresnelEmissionIntensity_S1;
                float _FresnelBias_S1;
                float _FresnelScale_S1;
                float _FresnelPower_S1;
                float _FresnelInvert_S1;
                float _FresnelHorizon_S1;
                float _FresnelWidth_S1;
                float _UseReflectionVector_S1;

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

            struct GalaxyData
            {
                float emission, baseColor, hue, sat;
                float4 tint;
                float4 starsST, starsTile, starsSpeed, starsColor01, starsColor02;
                float starsTileOverall, starsHue, starsEmission, starsDistortion;
                float4 noiseST, noiseTile, noiseSpeed;
                float noiseTileOverall;
                float4 fresnelColor;
                float fresnelHue, fresnelEmission, fresnelBias, fresnelScale, fresnelPower;
                float fresnelInvert, fresnelHorizon, fresnelWidth, flipDir;
            };

            GalaxyData MakeGalaxy1()
            {
                GalaxyData g;
                g.emission = _GalaxyEmissionIntensity_S1;
                g.baseColor = _GalaxyBaseColorIntensity_S1;
                g.tint = _GalaxyTint_S1;
                g.hue = _GalaxyHueShift_S1;
                g.sat = _GalaxySaturationShift_S1;
                g.starsST = _StarsTexture_S1_ST;
                g.noiseST = _StarsNoiseTexture_S1_ST;
                g.starsTile = _StarsTile_S1;
                g.starsTileOverall = _StarsTileOverall_S1;
                g.starsSpeed = _StarsSpeed_S1;
                g.starsColor01 = _StarsColor01_S1;
                g.starsColor02 = _StarsColor02_S1;
                g.starsHue = _StarsHueShift_S1;
                g.starsEmission = _StarsEmissionIntensity_S1;
                g.starsDistortion = _StarsDistortion_S1;
                g.noiseTile = _StarsNoiseTile_S1;
                g.noiseTileOverall = _StarsNoiseTileOverall_S1;
                g.noiseSpeed = _StarsNoiseSpeed_S1;
                g.fresnelColor = _FresnelColor_S1;
                g.fresnelHue = _FresnelHueShift_S1;
                g.fresnelEmission = _FresnelEmissionIntensity_S1;
                g.fresnelBias = _FresnelBias_S1;
                g.fresnelScale = _FresnelScale_S1;
                g.fresnelPower = _FresnelPower_S1;
                g.fresnelInvert = _FresnelInvert_S1;
                g.fresnelHorizon = _FresnelHorizon_S1;
                g.fresnelWidth = _FresnelWidth_S1;
                g.flipDir = _UseReflectionVector_S1;
                return g;
            }

            GalaxyData MakeGalaxy2()
            {
                GalaxyData g;
                g.emission = _GalaxyEmissionIntensity;
                g.baseColor = _GalaxyBaseColorIntensity;
                g.tint = _GalaxyTint;
                g.hue = _GalaxyHueShift;
                g.sat = _GalaxySaturationShift;
                g.starsST = _StarsTexture_ST;
                g.noiseST = _StarsNoiseTexture_ST;
                g.starsTile = _StarsTile;
                g.starsTileOverall = _StarsTileOverall;
                g.starsSpeed = _StarsSpeed;
                g.starsColor01 = _StarsColor01;
                g.starsColor02 = _StarsColor02;
                g.starsHue = _StarsHueShift;
                g.starsEmission = _StarsEmissionIntensity;
                g.starsDistortion = _StarsDistortion;
                g.noiseTile = _StarsNoiseTile;
                g.noiseTileOverall = _StarsNoiseTileOverall;
                g.noiseSpeed = _StarsNoiseSpeed;
                g.fresnelColor = _FresnelColor;
                g.fresnelHue = _FresnelHueShift;
                g.fresnelEmission = _FresnelEmissionIntensity;
                g.fresnelBias = _FresnelBias;
                g.fresnelScale = _FresnelScale;
                g.fresnelPower = _FresnelPower;
                g.fresnelInvert = _FresnelInvert;
                g.fresnelHorizon = _FresnelHorizon;
                g.fresnelWidth = _FresnelWidth;
                g.flipDir = _UseReflectionVector;
                return g;
            }

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

            // Vefects Galaxy, ported 1:1 from SH_Vefects_URP_Galaxy. Textures are passed in
            // so the same code serves both sides of the blend.
            float3 EvalGalaxy(
                TEXTURECUBE_PARAM(galaxyTex, galaxySampler),
                TEXTURE2D_PARAM(starsTex, starsSampler),
                TEXTURE2D_PARAM(noiseTex, noiseSampler),
                GalaxyData g, float3 viewDirection)
            {
                float3 cubeDirection = (g.flipDir > 0.5) ? viewDirection : -viewDirection;

                float3 galaxy = SAMPLE_TEXTURECUBE(galaxyTex, galaxySampler, cubeDirection).rgb;
                float3 galaxyHSV = RGBToHSV(galaxy);
                float3 galaxyRGB = HSVToRGB(float3(
                    galaxyHSV.x + g.hue,
                    galaxyHSV.y + g.sat,
                    galaxyHSV.z));
                float3 galaxyEmission = saturate(galaxyRGB) * g.tint.rgb * g.emission;
                float3 color = galaxyEmission * (1.0 + g.baseColor);

                float2 skyUV = DirectionToSkyUV(viewDirection);

                float2 starsUV = skyUV * g.starsST.xy + g.starsST.zw;
                starsUV = (starsUV * g.starsTile.xy) * g.starsTileOverall;
                starsUV += _Time.y * g.starsSpeed.xy;

                float2 noiseUV = skyUV * g.noiseST.xy + g.noiseST.zw;
                noiseUV = (noiseUV * g.noiseTile.xy) * g.noiseTileOverall;
                noiseUV += _Time.y * g.noiseSpeed.xy;

                float4 noiseSample = SAMPLE_TEXTURE2D(noiseTex, noiseSampler, noiseUV);
                float4 starsSample = SAMPLE_TEXTURE2D(
                    starsTex, starsSampler,
                    starsUV + float2(g.starsDistortion * noiseSample.g, g.starsDistortion * noiseSample.g));

                float4 starsLerp = lerp(
                    g.starsColor01,
                    g.starsColor02,
                    saturate((starsSample.g * noiseSample.g) * 30.0));
                float3 starsMasked = lerp(float3(0.0, 0.0, 0.0), starsLerp.rgb, starsSample.g);
                float3 starsHSV = RGBToHSV(starsMasked);
                float3 starsRGB = HSVToRGB(float3(starsHSV.x + g.starsHue, starsHSV.y, starsHSV.z));
                color += saturate(starsRGB) * g.starsEmission;

                float horizonDistance = abs(viewDirection.y - g.fresnelHorizon);
                float horizonNdotV = saturate(horizonDistance / max(g.fresnelWidth, 0.001));
                float fresnel = g.fresnelBias + g.fresnelScale * pow(
                    max(1.0 - horizonNdotV, 0.0001),
                    max(g.fresnelPower, 0.0001));
                float fresnelMask = saturate(fresnel);
                if (g.fresnelInvert > 0.5)
                    fresnelMask = 1.0 - fresnelMask;

                float3 fresnelTinted = g.fresnelColor.rgb * fresnelMask;
                float3 fresnelHSV = RGBToHSV(fresnelTinted);
                float3 fresnelRGB = HSVToRGB(float3(
                    fresnelHSV.x + g.fresnelHue,
                    fresnelHSV.y,
                    fresnelHSV.z));
                color += saturate(fresnelRGB) * g.fresnelEmission;

                return color;
            }

            float3 EvalSide1(float3 dir)
            {
                [branch] if (_Sky1Mode > 0.5)
                    return EvalGalaxy(
                        TEXTURECUBE_ARGS(_GalaxyTexture_S1, sampler_GalaxyTexture_S1),
                        TEXTURE2D_ARGS(_StarsTexture_S1, sampler_StarsTexture_S1),
                        TEXTURE2D_ARGS(_StarsNoiseTexture_S1, sampler_StarsNoiseTexture_S1),
                        MakeGalaxy1(), dir);
                return EvalCube1(dir);
            }

            float3 EvalSide2(float3 dir)
            {
                [branch] if (_Sky2Mode > 0.5)
                    return EvalGalaxy(
                        TEXTURECUBE_ARGS(_GalaxyTexture, sampler_GalaxyTexture),
                        TEXTURE2D_ARGS(_StarsTexture, sampler_StarsTexture),
                        TEXTURE2D_ARGS(_StarsNoiseTexture, sampler_StarsNoiseTexture),
                        MakeGalaxy2(), dir);
                return EvalCube2(dir);
            }

            // Full blend. _FadeRatio is uniform, so the unused side costs nothing at 0 or 1.
            float3 EvalSky(float3 dir)
            {
                float3 c1 = 0;
                float3 c2 = 0;

                [branch] if (_FadeRatio < 0.9999)
                    c1 = EvalSide1(dir);
                [branch] if (_FadeRatio > 0.0001)
                    c2 = EvalSide2(dir);

                return lerp(c1, c2, _FadeRatio);
            }

            // ---- Cubemap bake (used by SkyboxController for water / reflections) ----
            float _SkyBakeFace; // global, set per face by the controller (0..5 = +X -X +Y -Y +Z -Z)

            struct BakeVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 clipXY : TEXCOORD0;
            };

            BakeVaryings VertBake(uint id : SV_VertexID)
            {
                BakeVaryings o;
                float2 p = float2((id << 1) & 2, id & 2);
                o.positionCS = float4(p * 2.0 - 1.0, 0.0, 1.0);
                o.clipXY = p * 2.0 - 1.0;
                return o;
            }

            float4 FragBake(BakeVaryings i) : SV_Target
            {
                float sc = i.clipXY.x;
                #if UNITY_UV_STARTS_AT_TOP
                    float tc = -i.clipXY.y;
                #else
                    float tc = i.clipXY.y;
                #endif

                float3 d;
                int face = (int)(_SkyBakeFace + 0.5);
                if (face == 0)      d = float3( 1.0, -tc, -sc);
                else if (face == 1) d = float3(-1.0, -tc,  sc);
                else if (face == 2) d = float3( sc,  1.0,  tc);
                else if (face == 3) d = float3( sc, -1.0, -tc);
                else if (face == 4) d = float3( sc, -tc,  1.0);
                else                d = float3(-sc, -tc, -1.0);

                // The sky mesh is rotated by +_Rotation, so a world direction shows
                // the sample at -_Rotation.
                d = RotateAroundYInDegrees(normalize(d), -_Rotation);
                float3 c = EvalSky(d);
                return float4(min(c, 60000.0), 1.0);
            }
        ENDHLSL

        Pass
        {
            Name "SkyboxMaterialBlend"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

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
                return half4(EvalSky(normalize(input.direction)), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "CubeBake"
            Tags { "LightMode" = "SkyBake" }
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VertBake
            #pragma fragment FragBake
            ENDHLSL
        }
    }
    Fallback Off
}
