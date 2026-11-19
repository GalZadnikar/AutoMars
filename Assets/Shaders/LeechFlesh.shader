// Leech flesh (E3b): simple wet-flesh lighting (wrap diffuse, soft spec, pink rim "subsurface") that is
// masked per pixel by the fog of war. FogOfWar.cs pushes the fog grid as global textures; every pixel looks up
// its own world position there and is clipped (with a screen dither at the soft edge) where it is not lit:
//   visible now           -> drawn
//   explored but dim      -> drawn (_ExploredShow 1, default; 0 = hide it there too)
//   never explored (black)-> never drawn
// So a tentacle reaching out of the black fog only shows the part that is over ground you can see.
// Depth / DepthNormals passes clip the same way so the full-screen fog pass and SSAO see the same shape.
// Shadows are not cast (a shadow of the hidden part would give it away).
Shader "AutoMars/LeechFlesh"
{
    Properties
    {
        _BaseColor ("Base Colour", Color) = (0.93, 0.6, 0.62, 1)
        _VertexColorAmount ("Use Vertex Colour", Range(0, 1)) = 0
        _RimColor ("Rim (subsurface) Colour", Color) = (1, 0.45, 0.5, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _Wrap ("Diffuse Wrap", Range(0, 1)) = 0.45
        _Gloss ("Gloss", Range(0, 1)) = 0.65
        _Specular ("Specular", Range(0, 2)) = 0.6
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 0)
        _ExploredShow ("Show In Dim Explored Areas", Range(0, 1)) = 1
        _FogMaskOn ("Fog Mask On", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half  _VertexColorAmount;
            half4 _RimColor;
            half  _RimPower;
            half  _Wrap;
            half  _Gloss;
            half  _Specular;
            half4 _EmissionColor;
            half  _ExploredShow;
            half  _FogMaskOn;
        CBUFFER_END

        // globals from FogOfWar.cs
        TEXTURE2D(_FogTex);
        TEXTURE2D(_FogPrevTex);
        SAMPLER(sampler_FogTex);
        float4 _FogParams;   // xy = world origin (x,z), zw = 1 / world size
        half4  _FogLook;     // x = explored brightness, y = blend prev->current
        half   _FogEnabled;

        // 0..1: how lit this world position is
        half FogLight(float3 posWS)
        {
            if (_FogEnabled < 0.5h || _FogMaskOn < 0.5h) return 1.0h;
            float2 fuv = (posWS.xz - _FogParams.xy) * _FogParams.zw;
            half2 cur  = SAMPLE_TEXTURE2D(_FogTex,     sampler_FogTex, fuv).rg;
            half2 prev = SAMPLE_TEXTURE2D(_FogPrevTex, sampler_FogTex, fuv).rg;
            half2 f = lerp(prev, cur, _FogLook.y);
            float2 e = min(fuv, 1.0 - fuv);
            half inside = (half)smoothstep(0.0, 0.012, min(e.x, e.y));
            return max(f.r, f.g * _ExploredShow) * inside;   // explored counts as fully lit (it is visible on screen, only dimmed)
        }

        // screen-space dither so the edge of the light dissolves instead of a hard cut
        float Dither(float2 pixel)
        {
            return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
        }

        void FogClip(float3 posWS, float4 positionCS)
        {
            half light = FogLight(posWS);
            // only the outer rim of the black fog dissolves; anything you can see the ground of shows the leech fully
            half v = saturate((light - 0.08h) / 0.3h);
            clip(v - Dither(positionCS.xy) * 0.999 - 0.0005);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half4  color      : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                FogClip(i.positionWS, i.positionCS);

                half3 albedo = _BaseColor.rgb * lerp(half3(1, 1, 1), i.color.rgb, _VertexColorAmount);
                half3 N = normalize(i.normalWS);
                half3 V = normalize(GetWorldSpaceViewDir(i.positionWS));

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half atten = light.shadowAttenuation * light.distanceAttenuation;
                half ndl = dot(N, light.direction);
                half wrap = saturate((ndl + _Wrap) / (1.0h + _Wrap));
                half3 diffuse = albedo * (light.color * wrap * lerp(1.0h, atten, 0.8h) + SampleSH(N));

                half3 H = normalize(light.direction + V);
                half spec = pow(saturate(dot(N, H)), exp2(_Gloss * 8.0h + 1.0h)) * _Specular * saturate(ndl * 4.0h) * atten;

                half rim = pow(1.0h - saturate(dot(N, V)), _RimPower);
                half3 col = diffuse + light.color * spec + _RimColor.rgb * rim * 0.45h + _EmissionColor.rgb;
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half Frag(Varyings i) : SV_Target
            {
                FogClip(i.positionWS, i.positionCS);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                FogClip(i.positionWS, i.positionCS);
                return half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
