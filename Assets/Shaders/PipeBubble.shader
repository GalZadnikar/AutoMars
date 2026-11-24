Shader "AutoMars/PipeBubble"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 0.55, 0.15, 1)
        _Power ("Rim Power", Float) = 2.2
        _Fill ("Fill", Range(0, 1)) = 0.1
        _Alpha ("Alpha", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Power;
                half _Fill;
                half _Alpha;
            CBUFFER_END

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(ws);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half ndv = saturate(dot(normalize(i.normalWS), normalize(i.viewWS)));
                half rim = pow(1.0 - ndv, _Power);
                return half4(_Color.rgb, saturate(rim + _Fill) * _Alpha * _Color.a);
            }
            ENDHLSL
        }
    }
}
