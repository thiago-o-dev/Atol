// Draws nothing visible. It stamps stencil bit 0 wherever this mesh is visible (depth-tested
// against the opaque hull), and Wave.shader skips pixels with that bit set. Result: a hull
// interior without ocean water, while the hull walls, floor and player still render normally.
// Queue Geometry+1 so it runs after the opaque boat and before the water (Geometry+2).
Shader "CoastalBreakers/Hull Water Mask"
{
    Properties { }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+1"
        }

        Pass
        {
            Name "HullWaterMask"
            Cull Off
            ZWrite Off
            ZTest LEqual
            ColorMask 0

            Stencil
            {
                Ref 1
                ReadMask 1
                WriteMask 1
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
