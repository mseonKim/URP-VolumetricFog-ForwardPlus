Shader "Fog/OpaqueAtmosphericScattering"
{
    HLSLINCLUDE
        #pragma target 4.5
        #pragma editor_sync_compilation
        #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

        // #pragma enable_d3d11_debug_symbols

        #include "./VolumetricLightingCommon.hlsl"
        #include "./VolumetricLightingInput.hlsl"
        #include "./VolumetricLightingPass.hlsl"

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
                #pragma fragment FragVBuffer
            ENDHLSL
        }
    }
}
