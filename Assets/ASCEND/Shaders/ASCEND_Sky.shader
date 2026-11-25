// ASCEND/Sky — night sky over the city: gradient, city glow on the horizon, twinkling stars and a moon.
Shader "ASCEND/Sky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.02,0.015,0.06,1)
        _HorizonColor ("Horizon", Color) = (0.32,0.10,0.34,1)
        _GlowColor ("City Glow", Color) = (1.0,0.45,0.35,1)
        _BottomColor ("Bottom", Color) = (0.03,0.02,0.05,1)
        _StarDensity ("Star Density", Float) = 140
        _MoonDir ("Moon Direction", Vector) = (-0.45,0.42,0.78,0)
        _MoonColor ("Moon", Color) = (1.0,0.93,0.82,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _HorizonColor;
                half4 _GlowColor;
                half4 _BottomColor;
                float _StarDensity;
                float4 _MoonDir;
                half4 _MoonColor;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert (A input)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.dir = input.positionOS.xyz;
                return o;
            }
            float hash31(float3 p) { p = frac(p * 0.1031); p += dot(p, p.yzx + 33.33); return frac((p.x + p.y) * p.z); }
            half4 frag (V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = normalize(i.dir);
                float y = d.y;
                half3 col = lerp(_HorizonColor.rgb, _TopColor.rgb, saturate(pow(max(y, 0), 0.45)));
                col = lerp(col, _BottomColor.rgb, saturate(-y * 6));
                col += _GlowColor.rgb * exp(-abs(y) * 18) * 0.55;
                // stars
                float3 cell = floor(d * _StarDensity);
                float h = hash31(cell);
                float star = step(0.9965, h) * saturate(y * 4);
                float tw = 0.6 + 0.4 * sin(_Time.y * (2 + h * 6) + h * 50);
                col += star * tw * half3(0.9, 0.9, 1.0);
                // moon
                float3 md = normalize(_MoonDir.xyz);
                float m = dot(d, md);
                col += _MoonColor.rgb * smoothstep(0.9993, 0.9996, m);
                col += _MoonColor.rgb * pow(saturate(m), 400) * 0.35;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
