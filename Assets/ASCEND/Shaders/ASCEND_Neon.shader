// ASCEND/Neon v3 — the one material used by almost everything in the world.
// Vertex colour RGB = albedo tint, vertex colour A = emission (0 = lit surface, 1 = glowing neon).
// v3 adds real PBR-ish shading tuned for Quest 2: normal map + detail normal (felt fibres, leather grain),
// mask map (R metallic, G occlusion, A smoothness), the main light plus every additional light (table
// downlights, neon spill), image-based reflections from the baked probes and a soft rim.
Shader "ASCEND/Neon"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _BaseMap ("Albedo", 2D) = "white" {}
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0,2)) = 1
        [Normal] _DetailNormalMap ("Detail Normal (own tiling)", 2D) = "bump" {}
        _DetailNormalScale ("Detail Strength", Range(0,2)) = 0.6
        _MaskMap ("Mask (R metal, G AO, A smooth)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _EmissionBoost ("Emission Boost", Float) = 1.6
        _EmitAmount ("Emission (no vertex colour)", Range(0,1)) = 0
        _RimColor ("Rim Colour", Color) = (0.55,0.4,1,1)
        _RimPower ("Rim Power", Float) = 3
        _RimStrength ("Rim Strength", Range(0,2)) = 0.35
        _UseVertexColor ("Use Vertex Colour", Float) = 1
        _Reflect ("Reflection", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.85
        _Specular ("Specular", Range(0,2)) = 0.4
        _AOStrength ("Occlusion Strength", Range(0,1)) = 1
        [Toggle(_NORMALMAP)] _UseNormal ("Use Normal Map", Float) = 0
        [Toggle(_DETAIL)] _UseDetail ("Use Detail Normal", Float) = 0
        [Toggle(_MASKMAP)] _UseMask ("Use Mask Map", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _DetailNormalMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half _DetailNormalScale;
            half _Metallic;
            half _EmissionBoost;
            half _EmitAmount;
            half4 _RimColor;
            half _RimPower;
            half _RimStrength;
            half _UseVertexColor;
            half _Reflect;
            half _Smoothness;
            half _Specular;
            half _AOStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _DETAIL
            #pragma shader_feature_local _MASKMAP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_DetailNormalMap); SAMPLER(sampler_DetailNormalMap);
            TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv         : TEXCOORD0;   // xy base, zw detail
                half4  color      : COLOR;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
                float4 tangentWS  : TEXCOORD4;
                half3  vertexLight: TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = nrm.normalWS;
                o.tangentWS = float4(nrm.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                o.uv.xy = TRANSFORM_TEX(input.uv, _BaseMap);
                o.uv.zw = TRANSFORM_TEX(input.uv, _DetailNormalMap);
                half4 vc = input.color;
                o.color = lerp(half4(1, 1, 1, 0), vc, _UseVertexColor);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    o.vertexLight = VertexLighting(p.positionWS, nrm.normalWS);
                #endif
                return o;
            }

            half3 Shade(Light light, half3 albedo, half3 specCol, float3 n, float3 v, half smooth)
            {
                half atten = light.distanceAttenuation * light.shadowAttenuation;
                half ndl = saturate(dot(n, light.direction));
                half3 h = SafeNormalize(light.direction + v);
                half spec = pow(saturate(dot(n, h)), 8 + 248 * smooth * smooth) * (0.3 + 1.7 * smooth * smooth);
                return light.color * atten * (albedo * ndl + specCol * spec * ndl);
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv.xy);
                half3 albedo = tex.rgb * _BaseColor.rgb * i.color.rgb;
                half emit = saturate(max(i.color.a, _EmitAmount));

                half metal = _Metallic, smooth = _Smoothness, ao = 1;
                #ifdef _MASKMAP
                    half4 m = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, i.uv.xy);
                    metal = max(metal, m.r); ao = lerp(1, m.g, _AOStrength); smooth = m.a * _Smoothness * 1.15;
                #endif

                float3 n = normalize(i.normalWS);
                #if defined(_NORMALMAP) || defined(_DETAIL)
                    half3 tn = half3(0, 0, 1);
                    #ifdef _NORMALMAP
                        tn = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv.xy), _BumpScale);
                    #endif
                    #ifdef _DETAIL
                        half3 dn = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, i.uv.zw), _DetailNormalScale);
                        tn = normalize(half3(tn.xy + dn.xy, tn.z * dn.z));
                    #endif
                    float sgn = i.tangentWS.w;
                    float3 t = normalize(i.tangentWS.xyz);
                    float3 b = sgn * cross(n, t);
                    n = normalize(TransformTangentToWorld(tn, half3x3(t, b, n)));
                #endif

                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 diffCol = albedo * (1 - metal * 0.92);
                half3 specCol = lerp(half3(0.04, 0.04, 0.04) * _Specular * 2.5, albedo, metal);

                half3 lit = diffCol * SampleSH(n) * ao;
                Light mainLight = GetMainLight();
                lit += Shade(mainLight, diffCol, specCol, n, v, smooth);

                #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.positionWS;
                    inputData.normalWS = n;
                    inputData.viewDirectionWS = v;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    uint count = GetAdditionalLightsCount();
                    #if USE_FORWARD_PLUS
                    for (uint li = 0; li < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); li++)
                    {
                        Light l = GetAdditionalLight(li, i.positionWS, half4(1,1,1,1));
                        lit += Shade(l, diffCol, specCol, n, v, smooth);
                    }
                    #endif
                    LIGHT_LOOP_BEGIN(count)
                        Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1,1,1,1));
                        lit += Shade(l, diffCol, specCol, n, v, smooth);
                    LIGHT_LOOP_END
                #elif defined(_ADDITIONAL_LIGHTS_VERTEX)
                    lit += i.vertexLight * diffCol;
                #endif

                half nv = saturate(dot(n, v));
                half rim = pow(1.0h - nv, _RimPower) * _RimStrength;
                lit += _RimColor.rgb * rim;

                half refl = max(_Reflect, metal * 0.85);
                if (refl > 0.001h)
                {
                    half3 r = reflect(-v, n);
                    half fres = lerp(0.2h + 0.8h * pow(1.0h - nv, 4), 1.0h, metal);
                    half3 env = GlossyEnvironmentReflection(r, 1.0h - smooth, ao);
                    lit += env * refl * fres * lerp(half3(1, 1, 1), albedo * 1.4, metal);
                }

                half3 col = lerp(lit, albedo * _EmissionBoost, emit);
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vert (A input)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }
            half frag (V i) : SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 n : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert (A input)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.n = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }
            half4 frag (V i) : SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); return half4(normalize(i.n), 0); }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
