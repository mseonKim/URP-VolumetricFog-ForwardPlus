#ifndef UNITY_VOLUMETRIC_LIGHTING_COMMON_INCLUDED
#define UNITY_VOLUMETRIC_LIGHTING_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
real LerpWhiteTo(real b, real t) { return (1.0 - t) + b * t; }  // To prevent compile error
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"

CBUFFER_START(ShaderVariablesFog)
    float4  _HeightFogParams;
    float4  _HeightFogBaseScattering;
CBUFFER_END

#define _HeightFogBaseHeight        _HeightFogParams.x
#define _HeightFogBaseExtinction    _HeightFogParams.y
#define _HeightFogExponents         _HeightFogParams.zw

CBUFFER_START(ShaderVariablesVolumetric)
    float4  _VBufferArtisticParams;
    float   _VBufferAnisotropy;
    float   _CornetteShanksConstant;
    uint    _VolumetricFilteringEnabled;
    uint    _VBufferHistoryIsValid;
    float4  _VBufferLightingViewportScale;
    float4  _VBufferLightingViewportLimit;
    float4  _VBufferDistanceEncodingParams;
    float4  _VBufferDistanceDecodingParams;
    float4  _VBufferSampleOffset;
    float4  _VBufferViewportSize;
    float   _VBufferVoxelSize;
    uint    _VBufferSliceCount;
    float   _VBufferRcpSliceCount;
    float   _VBufferUnitDepthTexelSpacing;
    float4  _RTHandleScale;
    // float4x4  _VBufferCoordToViewDirWS;  // TODO
CBUFFER_END

#define VBufferScatteringIntensity _VBufferArtisticParams.x

struct JitteredRay
{
    float3 originWS;
    float3 centerDirWS;
    float3 jitterDirWS;
    float3 xDirDerivWS;
    float3 yDirDerivWS;
    float  geomDist;

    float maxDist;
};

struct VoxelLighting
{
    float3 radianceComplete;
    float3 radianceNoPhase;
};

// Returns the forward (up) direction of the current view in the world space.
float3 GetViewUpDir()
{
    float4x4 viewMat = GetWorldToViewMatrix();
    return viewMat[1].xyz;
}

bool IsInRange(float x, float2 range)
{
    return clamp(x, range.x, range.y) == x;
}


VoxelLighting EvaluateVoxelLightingLocal(float2 pixelCoord, float extinction, float anisotropy,
                                         JitteredRay ray, float t0, float t1, float dt,
                                         float3 centerWS, float rndVal)
{
    VoxelLighting lighting;
    ZERO_INITIALIZE(VoxelLighting, lighting);

    float sampleOpticalDepth = extinction * dt;
    float sampleTransmittance = exp(-sampleOpticalDepth);
    float rcpExtinction = rcp(extinction);
    float weight = (rcpExtinction - rcpExtinction * sampleTransmittance) * rcpExtinction;

#if USE_FORWARD_PLUS
    uint lightIndex;
    ClusterIterator _urp_internal_clusterIterator = ClusterInit(GetNormalizedScreenSpaceUV(pixelCoord), centerWS, 0);
    [loop]
    while (ClusterNext(_urp_internal_clusterIterator, lightIndex))
    {
        lightIndex += URP_FP_DIRECTIONAL_LIGHTS_COUNT;
        // Light light = GetAdditionalLight(lightIndex, centerWS);
        // half3 L = light.color * light.distanceAttenuation;

    #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
        float4 lightPositionWS = _AdditionalLightsBuffer[lightIndex].position;
        half3 color = _AdditionalLightsBuffer[lightIndex].color.rgb;
        half4 distanceAndSpotAttenuation = _AdditionalLightsBuffer[lightIndex].attenuation;
        half4 spotDirection = _AdditionalLightsBuffer[lightIndex].spotDirection;
        uint lightLayerMask = _AdditionalLightsBuffer[lightIndex].layerMask;
    #else
        float4 lightPositionWS = _AdditionalLightsPosition[lightIndex];
        half3 color = _AdditionalLightsColor[lightIndex].rgb;
        half4 distanceAndSpotAttenuation = _AdditionalLightsAttenuation[lightIndex];
        half4 spotDirection = _AdditionalLightsSpotDir[lightIndex];
        uint lightLayerMask = asuint(_AdditionalLightsLayerMasks[lightIndex]);
    #endif

        // Jitter
        float lightSqRadius = rcp(distanceAndSpotAttenuation.x);
        float t, distSq, rcpPdf;
        ImportanceSamplePunctualLight(rndVal, lightPositionWS, lightSqRadius,
                                      ray.originWS, ray.jitterDirWS, t0, t1,
                                      t, distSq, rcpPdf);
        float3 positionWS = ray.originWS + t * ray.jitterDirWS;

        float3 lightVector = lightPositionWS.xyz - positionWS * lightPositionWS.w;
        float distanceSqr = dot(lightVector, lightVector);

        float distanceAttenuation = DistanceAttenuation(distanceSqr, distanceAndSpotAttenuation.xy);
        half3 lightDirection = half3(lightVector * rsqrt(distanceSqr));

        half SdotL = dot(spotDirection.xyz, lightDirection);
        half angleAttenuation = saturate(SdotL * distanceAndSpotAttenuation.z + distanceAndSpotAttenuation.w);
        angleAttenuation = angleAttenuation * angleAttenuation;
        // float angleAttenuation = AngleAttenuation(spotDirection.xyz, lightDirection, distanceAndSpotAttenuation.zw);

        float cosOuterAngle = -distanceAndSpotAttenuation.w / distanceAndSpotAttenuation.z;
        float cosInnerAngle = rcp(distanceAndSpotAttenuation.z) + cosOuterAngle;
        half3 L = color * distanceAttenuation * angleAttenuation;

        // TODO: 1. IES & Cookie

        // TODO: 2. Shadow?

        float3 centerL  = lightPositionWS.wyz - centerWS;
        float  cosTheta = dot(normalize(centerL), ray.centerDirWS);
        float  phase = CornetteShanksPhasePartVarying(anisotropy, cosTheta);

        lighting.radianceNoPhase += L * weight * rcpPdf;
        lighting.radianceComplete += L * weight * phase * rcpPdf;
    }
#endif

    return lighting;
}

#endif