// Full-screen fog of war for URP (used by a Full Screen Pass Renderer Feature).
// Reconstructs the world position of every pixel from the depth buffer, looks it up in
// the small fog grid texture (set globally by FogOfWar.cs) and multiplies the screen:
//   visible   -> x1 (no change)
//   explored  -> x exploredBrightness (dimmed)
//   unexplored-> fog colour (near black)
// Multiply blending means we never need a copy of the colour buffer = cheap on mobile.
Shader "AutoMars/FogOfWar"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Cull Off
        Blend DstColor Zero

        Pass
        {
            Name "FogOfWar"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_FogTex);
            TEXTURE2D(_FogPrevTex);
            SAMPLER(sampler_FogTex);

            float4 _FogParams;   // xy = world origin (x,z), zw = 1 / world size
            half4  _FogColor;    // colour of unexplored areas
            half4  _FogLook;     // x = explored brightness, y = blend prev->current
            half   _FogEnabled;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_FogEnabled < 0.5h) return half4(1, 1, 1, 1);

                float2 uv = input.texcoord;
            #if UNITY_REVERSED_Z
                float depth = SampleSceneDepth(uv);
            #else
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
            #endif
                float3 wpos = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                float2 fuv = (wpos.xz - _FogParams.xy) * _FogParams.zw;
                half2 cur  = SAMPLE_TEXTURE2D(_FogTex,     sampler_FogTex, fuv).rg;
                half2 prev = SAMPLE_TEXTURE2D(_FogPrevTex, sampler_FogTex, fuv).rg;
                half2 f = lerp(prev, cur, _FogLook.y);

                // fade to fog at the edge of the fog area (and beyond it)
                float2 e = min(fuv, 1.0 - fuv);
                half inside = (half)smoothstep(0.0, 0.012, min(e.x, e.y));

                half light = max(f.r, f.g * _FogLook.x) * inside;
                return half4(lerp(_FogColor.rgb, half3(1, 1, 1), light), 1);
            }
            ENDHLSL
        }
    }
}
