// SAND PROTOTYPE - soft toon look for the sand layer (SandField).
// Vertex colour: R = how deep in the sand (darker at the bottom of cut walls), A = sand amount (cut away when ~0).
Shader "SandPrototype/SandToon"
{
    Properties
    {
        _ColorTop ("Lit", Color) = (1, 0.84, 0.38, 1)
        _ColorShade ("Shade", Color) = (0.97, 0.64, 0.25, 1)
        _ColorDeep ("Wall Bottom", Color) = (0.86, 0.5, 0.2, 1)
        _ShadeSoft ("Shade Softness", Range(0.01, 1)) = 0.5
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.45
        _RimColor ("Rim", Color) = (1, 0.95, 0.75, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _ColorTop, _ColorShade, _ColorDeep, _RimColor;
            half _ShadeSoft, _ShadowStrength, _RimPower, _RimStrength;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            half4  color      : COLOR;
        };
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
                half4  color      : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(i.color.a - 0.02h);
                float3 n = normalize(i.normalWS);
                Light l = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = dot(n, l.direction) * 0.5h + 0.5h;
                half lit = smoothstep(0.5h - _ShadeSoft * 0.5h, 0.5h + _ShadeSoft * 0.5h, ndl);
                lit *= lerp(1.0h - _ShadowStrength, 1.0h, l.shadowAttenuation);
                half3 col = lerp(_ColorShade.rgb, _ColorTop.rgb, lit);
                col = lerp(_ColorDeep.rgb, col, i.color.r);
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0h - saturate(dot(n, viewDir)), _RimPower) * _RimStrength;
                col += _RimColor.rgb * rim;
                col *= lerp(half3(1, 1, 1), l.color, 0.5h);
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;

            struct Varyings { float4 positionCS : SV_POSITION; half alpha : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                o.alpha = v.color.a;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { clip(i.alpha - 0.02h); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Varyings { float4 positionCS : SV_POSITION; half alpha : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.alpha = v.color.a;
                return o;
            }
            half frag(Varyings i) : SV_Target { clip(i.alpha - 0.02h); return i.positionCS.z; }
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
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; half alpha : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.alpha = v.color.a;
                return o;
            }
            half4 frag(Varyings i) : SV_Target { clip(i.alpha - 0.02h); return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
