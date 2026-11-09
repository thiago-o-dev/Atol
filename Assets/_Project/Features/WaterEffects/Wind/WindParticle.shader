Shader "CoastalBreakers/Particles/Soft Wind Wisps"
{
    Properties
    {
        _Opacity ("Opacity", Range(0, 1)) = 0.78
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "WindWispForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Stretch billboards use the particle quad's UV.y as its long axis.
                // The filament intentionally fills much more of the width than the
                // previous hairline, so it remains visible at normal gameplay scale.
                float along = saturate(input.uv.y);
                float across = input.uv.x - 0.5;
                float centeredAlong = along * 2.0 - 1.0;

                float wave = sin(centeredAlong * 5.0 + _Time.y * 2.8) * 0.055
                           + sin(centeredAlong * 10.5 - _Time.y * 1.7) * 0.025;
                float taper = pow(saturate(sin(3.14159265 * along)), 0.62);
                float halfWidth = lerp(0.065, 0.185, taper);
                float distanceToFilament = abs(across - wave);

                float brightCore = 1.0 - smoothstep(halfWidth * 0.34, halfWidth * 0.82, distanceToFilament);
                float softHalo = 1.0 - smoothstep(halfWidth * 0.55, halfWidth * 1.9, distanceToFilament);
                float longitudinalFade = smoothstep(0.0, 0.085, along)
                                       * (1.0 - smoothstep(0.88, 1.0, along));
                float gentleVariation = 0.82 + 0.18 * sin(along * 19.0 + _Time.y * 2.2);

                float filament = saturate(brightCore * 0.68 + softHalo * 0.32);
                half alpha = (half)(filament * longitudinalFade * gentleVariation)
                           * input.color.a * _Opacity;
                return half4(input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
