// Ping-pong shallow-water wave solver.
// RG stores (height metres, vertical velocity metres/second). A finite-difference
// Laplacian propagates circular source impulses; light blur removes grid noise and
// frame-rate-correct damping dissipates energy like surface tension / viscosity.
Shader "Hidden/CoastalBreakers/WaterWakeFieldUpdate"
{
    Properties { _MainTex("Previous Height / Velocity", 2D) = "black" {} }

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

            #define MAX_WAKE_IMPULSES 32

            sampler2D _MainTex;
            float4 _Window;          // xy = world min XZ, zw = world size
            float2 _ScrollUV;
            float _DeltaTime;
            float _WaveSpeed;
            float _Damping;
            float _BlurAmount;
            float _MaximumHeight;
            float _TexelWorldSize;
            int _ImpulseCount;
            float4 _Impulses[MAX_WAKE_IMPULSES]; // xy = world XZ, z = radius, w = vertical velocity impulse

            float2 SampleState(float2 uv)
            {
                return tex2D(_MainTex, saturate(uv)).rg;
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 previousUV = i.uv + _ScrollUV;
                float inside = step(0.0, previousUV.x) * step(previousUV.x, 1.0) *
                               step(0.0, previousUV.y) * step(previousUV.y, 1.0);
                float2 state = SampleState(previousUV) * inside;

                float2 texelUV = float2(1.0 / _Window.z, 1.0 / _Window.w) * _TexelWorldSize;
                float2 stateL = SampleState(previousUV - float2(texelUV.x, 0.0));
                float2 stateR = SampleState(previousUV + float2(texelUV.x, 0.0));
                float2 stateD = SampleState(previousUV - float2(0.0, texelUV.y));
                float2 stateU = SampleState(previousUV + float2(0.0, texelUV.y));

                float inverseTexelSquared = rcp(max(_TexelWorldSize * _TexelWorldSize, 1e-5));
                float laplacian = (stateL.r + stateR.r + stateD.r + stateU.r - 4.0 * state.r)
                                * inverseTexelSquared;
                float velocity = state.g + (_WaveSpeed * _WaveSpeed) * laplacian * _DeltaTime;
                float height = state.r + velocity * _DeltaTime;

                // Smooth both packed fields (height AND vertical velocity). This damps
                // grid noise without imposing a hard support mask or flattening the wake.
                float2 evolvedState = float2(height, velocity);
                float2 neighbourAverage = 0.25 * (stateL + stateR + stateD + stateU);
                evolvedState = lerp(evolvedState, neighbourAverage, saturate(_BlurAmount));
                height = evolvedState.r * _Damping;
                velocity = evolvedState.g * _Damping;

                float2 world = _Window.xy + i.uv * _Window.zw;
                [loop]
                for (int k = 0; k < min(_ImpulseCount, MAX_WAKE_IMPULSES); k++)
                {
                    float4 impulse = _Impulses[k];
                    float2 delta = world - impulse.xy;
                    float radius = max(impulse.z, 0.05);
                    float gaussian = exp(-0.5 * dot(delta, delta) / (radius * radius));
                    velocity += impulse.w * gaussian;
                }

                height = clamp(height, -max(_MaximumHeight, 0.05), max(_MaximumHeight, 0.05));
                velocity = clamp(velocity, -3.0, 3.0);
                return float4(height, velocity, 0.0, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
