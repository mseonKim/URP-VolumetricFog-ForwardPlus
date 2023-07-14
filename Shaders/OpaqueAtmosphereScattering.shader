Shader "Fog/OpaqueAtmosphericScattering"
{
    HLSLINCLUDE
        #pragma target 4.5
        #pragma editor_sync_compilation
        #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

        #pragma multi_compile_fragment _ DEBUG_DISPLAY

        #pragma enable_d3d11_debug_symbols

        #include "./VolumetricLightingCommon.hlsl"
        #include "./VolumetricLightingInput.hlsl"
        #include "./VolumetricLightingPass.hlsl"

        float4 Frag(Varyings input) : SV_Target
        {
            // UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            // float2 positionSS  = input.positionCS.xy;
            // float3 clipSpace = float3(positionSS / (_ScreenParams.xy) * float2(2.0, -2.0) - float2(1.0, -1.0), 1.0);
            // float4 HViewPos = mul(UNITY_MATRIX_I_P, float4(clipSpace, 1.0));
            // float3 V = normalize(mul((float3x3)UNITY_MATRIX_I_V, HViewPos.xyz / HViewPos.w)); 
            // float depth = LoadSceneDepth(positionSS);

            // float3 volColor, volOpacity;
            // AtmosphericScatteringCompute(input, V, depth, volColor, volOpacity);

            // return float4(volColor, 1.0 - volOpacity.x);
            return 0;
        }
    ENDHLSL

    SubShader
    {
        Tags{ "RenderPipeline" = "UniversalPipeline" }
        // 0: NOMSAA
        Pass
        {
            Name "NoMSAA"

            Cull Off    ZWrite Off
            // Blend One SrcAlpha, Zero One // Premultiplied alpha for RGB, preserve alpha for the alpha channel
            Blend One One, One One
            ZTest Less  // Required for XR occlusion mesh optimization

            HLSLPROGRAM
                #pragma multi_compile_fragment _ _FORWARD_PLUS
                #pragma vertex Vert
                #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "NoMSAAPerPixel"

            Cull Off    ZWrite Off
            Blend One One, One One
            ZTest Less  // Required for XR occlusion mesh optimization

            HLSLPROGRAM
                #pragma multi_compile_fragment _ _FORWARD_PLUS
                #pragma vertex Vert
                #pragma fragment FragPerPixel
            ENDHLSL
        }
    }
}
