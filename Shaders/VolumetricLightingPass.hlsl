#ifndef UNITY_VOLUMETRIC_LIGHTING_PASS_INCLUDED
#define UNITY_VOLUMETRIC_LIGHTING_PASS_INCLUDED

#include "./VolumetricLightingCommon.hlsl"
#include "./VBuffer.hlsl"

half3 SampleSkyCubemap(float3 reflectVector, float mip)
{
    half4 encodedIrradiance = half4(SAMPLE_TEXTURECUBE_LOD(_GlossyEnvironmentCubeMap, sampler_GlossyEnvironmentCubeMap, reflectVector, mip));
    return DecodeHDREnvironment(encodedIrradiance, _GlossyEnvironmentCubeMap_HDR);
}

float3 GetFogColor(float3 V, float fragDist)
{
    float3 color = _FogColor.rgb;

    if (_FogColorMode == FOGCOLORMODE_SKY_COLOR)
    {
        // Based on Uncharted 4 "Mip Sky Fog" trick: http://advances.realtimerendering.com/other/2016/naughty_dog/NaughtyDog_TechArt_Final.pdf
        float mipLevel = (1.0 - _MipFogMaxMip * saturate((fragDist - _MipFogNear) / (_MipFogFar - _MipFogNear))) * (ENVCONSTANTS_CONVOLUTION_MIP_COUNT - 1);
        // For the atmospheric scattering, we use the GGX convoluted version of the cubemap. That matches the of the idnex 0
        color *= SampleSkyCubemap(-V, mipLevel); // '_FogColor' is the tint
    }

    return color;
}

void EvaluateAtmosphericScattering(PositionInputs posInput, float3 V, out float3 color, out float3 opacity)
{
    color = opacity = 0;
    float fogFragDist = distance(posInput.positionWS, GetCurrentViewPosition());

    if (_FogEnabled)
    {
        float4 volFog = float4(0.0, 0.0, 0.0, 0.0);
        float expFogStart = 0.0f;
        
        if (_EnableVolumetricFog)
        {
            bool doBiquadraticReconstruction = _VolumetricFilteringEnabled == 0; // Only if filtering is disabled.
            float4 value = SampleVBuffer(TEXTURE3D_ARGS(_VBufferLighting, s_linear_clamp_sampler),
                                            posInput.positionNDC,
                                            fogFragDist,
                                            _VBufferViewportSize,
                                            _VBufferLightingViewportScale.xyz,
                                            _VBufferLightingViewportLimit.xyz,
                                            _VBufferDistanceEncodingParams,
                                            _VBufferDistanceDecodingParams,
                                            true, doBiquadraticReconstruction, false);

            // TODO: add some slowly animated noise (dither?) to the reconstructed value.
            // TODO: re-enable tone mapping after implementing pre-exposure.
            volFog = DelinearizeRGBA_Float(float4(/*FastTonemapInvert*/(value.rgb), value.a));
            expFogStart = _VBufferLastSliceDist;
        }

        // TODO: if 'posInput.linearDepth' is computed using 'posInput.positionWS',
        // and the latter resides on the far plane, the computation will be numerically unstable.
        float distDelta = fogFragDist - expFogStart;

        if ((distDelta > 0))
        {
            // Apply the distant (fallback) fog.
            float3 positionWS = GetCurrentViewPosition() - V * expFogStart;
            float  startHeight = positionWS.y;
            float  cosZenith = -V.y;

            // For both homogeneous and exponential media,
            // Integrate[Transmittance[x] * Scattering[x], {x, 0, t}] = Albedo * Opacity[t].
            // Note that pulling the incoming radiance (which is affected by the fog) out of the
            // integral is wrong, as it means that shadow rays are not volumetrically shadowed.
            // This will result in fog looking overly bright.

            float3 volAlbedo = _HeightFogBaseScattering.xyz / _HeightFogBaseExtinction;
            float  odFallback = OpticalDepthHeightFog(_HeightFogBaseExtinction, _HeightFogBaseHeight,
                _HeightFogExponents, cosZenith, startHeight, distDelta);
            float  trFallback = TransmittanceFromOpticalDepth(odFallback);
            float  trCamera = 1 - volFog.a;

            volFog.rgb += trCamera * GetFogColor(V, fogFragDist) * CurrentExposureMultiplier() * volAlbedo * (1 - trFallback);
            volFog.a = 1 - (trCamera * trFallback);
        }

        color = volFog.rgb;
        opacity = volFog.a;
    }

}

void AtmosphericScatteringCompute(Varyings input, float3 V, float depth, out float3 color, out float3 opacity)
{
    PositionInputs posInput = GetPositionInput(input.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);

    if (depth == UNITY_RAW_FAR_CLIP_VALUE)
    {
        posInput.positionWS = GetCurrentViewPosition() - V * 100;
    }

    EvaluateAtmosphericScattering(posInput, V, color, opacity); // Premultiplied alpha
}

float4 FragVBuffer(Varyings input) : SV_Target
{
    // UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float2 positionSS  = input.positionCS.xy;
    float3 V= mul(-float4(positionSS, 1, 1), _PixelCoordToViewDirWS).xyz;
    float depth = LoadSceneDepth(positionSS);

    float3 volColor, volOpacity;
    AtmosphericScatteringCompute(input, V, depth, volColor, volOpacity);

    return float4(volColor, 1.0 - volOpacity.x);
}

#endif