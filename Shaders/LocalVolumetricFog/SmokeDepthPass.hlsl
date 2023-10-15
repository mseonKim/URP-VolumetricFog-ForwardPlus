#ifndef SMOKE_DEPTH_PASS_INCLUDED
#define SMOKE_DEPTH_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

float4x4 _SmokeVolumeViewProjM;

struct Attributes
{
    float4 positionOS     : POSITION;
    // UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS   : SV_POSITION;
    // float3 positionWS   : TEXCOORD1;
    // UNITY_VERTEX_INPUT_INSTANCE_ID
    // UNITY_VERTEX_OUTPUT_STEREO
};

Varyings SmokeDepthVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    // UNITY_SETUP_INSTANCE_ID(input);
    // UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 positionWS = mul(UNITY_MATRIX_M, input.positionOS).xyz;
    output.positionCS = mul(_SmokeVolumeViewProjM, float4(positionWS, 1.0));

#if UNITY_REVERSED_Z
    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    return output;
}

float SmokeDepthFragment(Varyings input) : SV_TARGET
{
    // UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    
    return input.positionCS.z;
}
#endif
