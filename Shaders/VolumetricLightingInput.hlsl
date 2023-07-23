#ifndef UNITY_VOLUMETRIC_LIGHTING_INPUT_INCLUDED
#define UNITY_VOLUMETRIC_LIGHTING_INPUT_INCLUDED

float4x4 _PixelCoordToViewDirWS;
TEXTURE3D(_VBufferLighting);
SAMPLER(s_linear_clamp_sampler);

struct Attributes
{
    uint vertexID : SV_VertexID;
    // UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    // UNITY_VERTEX_OUTPUT_STEREO
};

Varyings Vert(Attributes input)
{
    Varyings output;
    // UNITY_SETUP_INSTANCE_ID(input);
    // UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
    return output;
}

#endif