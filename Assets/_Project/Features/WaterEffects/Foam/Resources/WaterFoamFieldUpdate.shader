// Updates the scrolling top-down "foam field" render texture.
// Each pass: scroll the previous field by whole texels, fade it, then stamp this frame's
// contact segments (capsules) into it. The result is sampled by Wave.shader so the foam
// is drawn ON the water surface (see _WaterFoamField in Features/Water/Wave.shader).
Shader "Hidden/CoastalBreakers/WaterFoamFieldUpdate"
{
    Properties { _MainTex("Previous Field", 2D) = "black" {} }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            #define MAX_STAMPS 48

            sampler2D _MainTex;
            float2 _ScrollUV;        // (newWindowMin - oldWindowMin) / windowSize, a whole number of texels
            float4 _Window;          // xy = world min XZ, zw = world size XZ
            float _Retain;           // multiplicative fade this frame
            float _Cutoff;           // subtractive fade so values reach exactly zero
            int _StampCount;
            float4 _StampsA[MAX_STAMPS]; // xy = segment start (world XZ), zw = segment end
            float4 _StampsB[MAX_STAMPS]; // x = radius (m), y = strength 0..1

            float4 frag(v2f_img i) : SV_Target
            {
                float2 prevUV = i.uv + _ScrollUV;
                float inside = step(0.0, prevUV.x) * step(prevUV.x, 1.0) *
                               step(0.0, prevUV.y) * step(prevUV.y, 1.0);
                float prev = max(0.0, tex2D(_MainTex, prevUV).r * _Retain - _Cutoff) * inside;

                float2 world = _Window.xy + i.uv * _Window.zw;
                float added = 0.0;

                [loop]
                for (int k = 0; k < _StampCount; k++)
                {
                    float2 a = _StampsA[k].xy;
                    float2 b = _StampsA[k].zw;
                    float2 ab = b - a;
                    float t = saturate(dot(world - a, ab) / max(dot(ab, ab), 1e-4));
                    float d = length(world - (a + ab * t));
                    float f = saturate(1.0 - d / max(_StampsB[k].x, 1e-3));
                    f = f * f * (3.0 - 2.0 * f);               // soft edge
                    added = 1.0 - (1.0 - added) * (1.0 - f * _StampsB[k].y);
                }

                float result = 1.0 - (1.0 - prev) * (1.0 - added);
                return float4(result, 0.0, 0.0, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
