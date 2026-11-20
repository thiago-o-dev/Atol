// Draws nothing visible; it only writes DEPTH. Queue Geometry+1 runs after the opaque boat
// (so walls, floor and player are already drawn and never hidden) and before the water
// (Geometry+2). The water's normal depth test then rejects water that lies BEHIND this plane
// (inside the hull) while water in FRONT of it (a wave crest between camera and boat) still draws.
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
            ZWrite On
            ZTest LEqual
            ColorMask 0

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
