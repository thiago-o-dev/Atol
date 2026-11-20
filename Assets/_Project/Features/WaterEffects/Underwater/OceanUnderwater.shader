Shader "Custom/OceanUnderwater"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "OceanUnderwaterFullscreen"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float _OceanUnderwaterIntensity;
            float _OceanUnderwaterCameraDepth;
            float4 _OceanUnderwaterTint;
            float _OceanUnderwaterTintStrength;
            float _OceanUnderwaterRefraction;
            float _OceanUnderwaterCaustics;
            float _OceanUnderwaterDesaturation;
            float _OceanUnderwaterFogDensity;
            float4 _OceanUnderwaterFogColor;
            float _OceanUnderwaterFogIntensity;
            float _OceanUnderwaterMaxFogOpacity;
            float _OceanUnderwaterDepthDebug;
            float _OceanUnderwaterVignette;
            float _OceanUnderwaterCausticsSpeed;
            float _OceanUnderwaterExitBlur;
            float _OceanUnderwaterExitBlurRadiusPixels;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float intensity = saturate(_OceanUnderwaterIntensity);
                float fogIntensity = saturate(_OceanUnderwaterFogIntensity);
                float2 uv = input.texcoord;

                float time = _Time.y * max(_OceanUnderwaterCausticsSpeed, 0.0);
                float waveA = sin(uv.y * 47.0 + time * 1.7 + sin(uv.x * 19.0));
                float waveB = cos(uv.x * 39.0 - time * 1.25 + cos(uv.y * 23.0));
                float2 distortion = float2(waveA, waveB)
                    * _OceanUnderwaterRefraction * intensity;

                float2 distortedUV = saturate(uv + distortion);
                half3 originalColor = SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, uv).rgb;
                half3 sceneColor = SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, distortedUV).rgb;

                // Exit blur is independent from underwater fog and fades after leaving water.
                float blurAmount = saturate(_OceanUnderwaterExitBlur);
                float2 blurStep = (1.0 / max(_ScreenParams.xy, 1.0))
                    * max(_OceanUnderwaterExitBlurRadiusPixels, 0.0) * blurAmount;
                half3 blurredColor = originalColor * 0.20;
                blurredColor += SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, saturate(uv + float2( blurStep.x, 0.0))).rgb * 0.10;
                blurredColor += SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, saturate(uv + float2(-blurStep.x, 0.0))).rgb * 0.10;
                blurredColor += SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, saturate(uv + float2(0.0,  blurStep.y))).rgb * 0.10;
                blurredColor += SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, saturate(uv + float2(0.0, -blurStep.y))).rgb * 0.10;
                blurredColor += SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, saturate(uv + blurStep)).rgb * 0.10;
                blurredColor += SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, saturate(uv - blurStep)).rgb * 0.10;
                blurredColor += SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, saturate(uv + float2(blurStep.x, -blurStep.y))).rgb * 0.10;
                blurredColor += SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, saturate(uv + float2(-blurStep.x, blurStep.y))).rgb * 0.10;
                half3 aboveWaterColor = lerp(originalColor, blurredColor, blurAmount);

                // Sample depth from the SAME UV as sceneColor. Sampling depth at the
                // un-refracted UV makes a fish/background depth mismatch at refracted edges.
                float rawDepth = SampleSceneDepth(distortedUV);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                // Identify clear-depth pixels (skybox/background). The comparison differs
                // on reversed-Z platforms. Geometry pixels, including the shark, are
                // allowed to receive caustics; skybox pixels are not.
                float skyboxMask;
                #if UNITY_REVERSED_Z
                    skyboxMask = 1.0 - step(0.000001, rawDepth);
                #else
                    skyboxMask = step(0.999999, rawDepth);
                #endif
                float geometryMask = 1.0 - skyboxMask;

                // Optional diagnostic: geometry is grayscale by eye depth; clear-depth
                // pixels are magenta. If the shark is magenta, its depth pass is not
                // being included in the camera depth texture.
                if (_OceanUnderwaterDepthDebug > 0.5)
                {
                    float depthGray = saturate(eyeDepth / max(_ProjectionParams.z, 1.0));
                    half3 depthColor = lerp(depthGray.xxx, half3(1.0, 0.0, 1.0), skyboxMask);
                    return half4(depthColor, 1.0);
                }

                // Depth fog is controlled independently from the underwater tint/look,
                // allowing the controller's preemptive fog to work above the surface.
                float cameraDepthBoost = 1.0
                    + min(max(_OceanUnderwaterCameraDepth, 0.0), 20.0) * 0.035;
                float fog = 1.0 - exp(
                    -max(eyeDepth, 0.0) * max(_OceanUnderwaterFogDensity, 0.0) * cameraDepthBoost);
                fog = min(saturate(fog), saturate(_OceanUnderwaterMaxFogOpacity));
                fog *= fogIntensity;

                half3 tinted = sceneColor * _OceanUnderwaterTint.rgb;
                tinted = lerp(sceneColor, tinted, saturate(_OceanUnderwaterTintStrength));

                half luminance = dot(tinted, half3(0.2126, 0.7152, 0.0722));
                tinted = lerp(tinted, luminance.xxx, saturate(_OceanUnderwaterDesaturation));

                // Caustics affect geometry only, not skybox pixels. They are also reduced
                // where fog is strongest so they cannot wash out distant fish silhouettes.
                float2 causticUV = uv * float2(13.0, 9.0);
                float c1 = sin(causticUV.x + time + sin(causticUV.y * 1.7));
                float c2 = cos(causticUV.y * 1.3 - time * 0.83 + sin(causticUV.x * 1.4));
                float caustic = pow(saturate(0.5 + 0.5 * c1 * c2), 5.0);
                float causticVariation = lerp(0.72, 1.28, Hash21(floor(causticUV * 1.7)));
                float causticMask = geometryMask * (1.0 - fog);
                tinted += caustic * causticVariation
                    * _OceanUnderwaterCaustics * 0.16 * causticMask;

                float2 centered = uv * 2.0 - 1.0;
                float vignette = 1.0 - _OceanUnderwaterVignette
                    * smoothstep(0.35, 1.25, length(centered));
                tinted *= vignette;

                // First blend the non-fog underwater look, then apply fog independently.
                // This is what allows preemptive fog without enabling tint/refraction/caustics.
                half3 finalColor = lerp(aboveWaterColor, tinted, intensity);
                finalColor = lerp(finalColor, _OceanUnderwaterFogColor.rgb, fog);

                return half4(max(finalColor, 0.0), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
