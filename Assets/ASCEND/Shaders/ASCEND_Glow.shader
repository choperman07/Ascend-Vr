// ASCEND/Glow — additive soft glow for halos, light beams, lasers, particles (no bloom needed on Quest).
Shader "ASCEND/Glow"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (1,1,1,1)
        _BaseMap ("Texture", 2D) = "white" {}
        _Intensity ("Intensity", Float) = 1
        _Scroll ("UV Scroll (xy)", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Glow"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Intensity;
                float4 _Scroll;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            V vert (A input)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap) + _Scroll.xy * _Time.y;
                o.color = input.color;
                return o;
            }
            half4 frag (V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half a = t.a * i.color.a * _BaseColor.a;
                return half4(t.rgb * i.color.rgb * _BaseColor.rgb * a * _Intensity, 0);
            }
            ENDHLSL
        }
    }
}
