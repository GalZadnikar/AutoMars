// BIOME PROTOTYPE - ground / rock shader for LowPolyGround.
// A small mask texture over the map (baked by BiomeMap from BiomeZones) holds the weights of up to 4 biomes:
//   biome 0 = everything not covered, biomes 1..3 = R, G, B.
// Borders are sharpened per pixel (smooth, curvy edges at any mesh resolution).
// Inside a biome every triangle gets its own tone between Colour A and Colour B (hash of the flat normal),
// which keeps the low-poly patchwork look. Lighting = URP's standard PBR (same as the Lit material it replaces).
Shader "BiomePrototype/BiomeGround"
{
    Properties
    {
        [Toggle] _RockMode ("Rock Mode (use rock colours)", Float) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.05
        _EdgeSoftness ("Edge Softness", Range(0.01, 0.5)) = 0.12
        [NoScaleOffset] _BiomeMask ("Biome Mask (set by BiomeMap)", 2D) = "black" {}
        _BiomeArea ("Biome Area (origin xz, 1/size xz)", Vector) = (-40, -40, 0.0125, 0.0125)

        _B0A ("Biome 0 Colour A", Color) = (0.8, 0.52, 0.33, 1)
        _B0B ("Biome 0 Colour B", Color) = (0.76, 0.48, 0.3, 1)
        _B0R ("Biome 0 Rock", Color) = (0.55, 0.34, 0.21, 1)
        _B1A ("Biome 1 Colour A", Color) = (0.8, 0.52, 0.33, 1)
        _B1B ("Biome 1 Colour B", Color) = (0.76, 0.48, 0.3, 1)
        _B1R ("Biome 1 Rock", Color) = (0.55, 0.34, 0.21, 1)
        _B2A ("Biome 2 Colour A", Color) = (0.8, 0.52, 0.33, 1)
        _B2B ("Biome 2 Colour B", Color) = (0.76, 0.48, 0.3, 1)
        _B2R ("Biome 2 Rock", Color) = (0.55, 0.34, 0.21, 1)
        _B3A ("Biome 3 Colour A", Color) = (0.8, 0.52, 0.33, 1)
        _B3B ("Biome 3 Colour B", Color) = (0.76, 0.48, 0.3, 1)
        _B3R ("Biome 3 Rock", Color) = (0.55, 0.34, 0.21, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BiomeMask); SAMPLER(sampler_BiomeMask);

        CBUFFER_START(UnityPerMaterial)
            float4 _BiomeArea;
            half4 _B0A, _B0B, _B0R, _B1A, _B1B, _B1R, _B2A, _B2B, _B2R, _B3A, _B3B, _B3R;
            half _RockMode, _Smoothness, _EdgeSoftness;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
        };

        // stable random 0..1 per flat-shaded triangle (all 3 vertices share the same normal)
        half TriangleHash(float3 n)
        {
            float3 q = round(n * 512.0);
            return (half)frac(sin(dot(q, float3(12.9898, 78.233, 37.719))) * 43758.5453);
        }

        half3 BiomeColour(float3 positionWS, float3 normalWS)
        {
            float2 uv = (positionWS.xz - _BiomeArea.xy) * _BiomeArea.zw;
            half3 m = SAMPLE_TEXTURE2D(_BiomeMask, sampler_BiomeMask, uv).rgb;
            half4 w = half4(saturate(1.0h - m.r - m.g - m.b), m.r, m.g, m.b);
            // sharpen the borders, then make the weights add up to 1 again
            w = smoothstep(0.5h - _EdgeSoftness, 0.5h + _EdgeSoftness, w);
            w /= max(w.x + w.y + w.z + w.w, 1e-4h);

            half h = TriangleHash(normalWS);
            half3 c0, c1, c2, c3;
            if (_RockMode > 0.5h)
            {
                half tone = lerp(0.88h, 1.08h, h);
                c0 = _B0R.rgb * tone; c1 = _B1R.rgb * tone; c2 = _B2R.rgb * tone; c3 = _B3R.rgb * tone;
            }
            else
            {
                c0 = lerp(_B0A.rgb, _B0B.rgb, h); c1 = lerp(_B1A.rgb, _B1B.rgb, h);
                c2 = lerp(_B2A.rgb, _B2B.rgb, h); c3 = lerp(_B3A.rgb, _B3B.rgb, h);
            }
            return c0 * w.x + c1 * w.y + c2 * w.z + c3 * w.w;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = BiomeColour(i.positionWS, i.normalWS);
                s.metallic = 0;
                s.smoothness = _Smoothness;
                s.occlusion = 1;
                s.alpha = 1;
                s.normalTS = half3(0, 0, 1);

                half4 c = UniversalFragmentPBR(inputData, s);
                c.rgb = MixFog(c.rgb, inputData.fogCoord);
                c.a = 1;
                return c;
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

            float4 vert(Attributes v) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }
            half4 frag() : SV_Target { return 0; }
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
            float4 vert(Attributes v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
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
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
