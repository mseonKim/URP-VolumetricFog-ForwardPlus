#ifndef UNITY_VOLUMETRIC_LIGHTING_COMMON_INCLUDED
#define UNITY_VOLUMETRIC_LIGHTING_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
real LerpWhiteTo(real b, real t) { return (1.0 - t) + b * t; }  // To prevent compile error
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"

CBUFFER_START(ShaderVariablesFog)
    uint        _FogEnabled;
    uint        _EnableVolumetricFog;
    uint        _FogColorMode;
    uint        _MaxEnvCubemapMip;
    float4      _FogColor;
    float4      _MipFogParameters;
    float4      _HeightFogParams;
    float4      _HeightFogBaseScattering;
CBUFFER_END

#define FOGCOLORMODE_SKY_COLOR              1   // 0 = Constant color
#define ENVCONSTANTS_CONVOLUTION_MIP_COUNT  _MaxEnvCubemapMip
#define _MipFogNear                         _MipFogParameters.x
#define _MipFogFar                          _MipFogParameters.y
#define _MipFogMaxMip                       _MipFogParameters.z
#define _HeightFogBaseHeight                _HeightFogParams.x
#define _HeightFogBaseExtinction            _HeightFogParams.y
#define _HeightFogExponents                 _HeightFogParams.zw

CBUFFER_START(ShaderVariablesVolumetricLighting)
    uint        _VolumetricFilteringEnabled;
    uint        _VBufferHistoryIsValid;
    uint        _VBufferSliceCount;
    float       _VBufferAnisotropy;
    float       _CornetteShanksConstant;
    float       _VBufferVoxelSize;
    float       _VBufferRcpSliceCount;
    float       _VBufferUnitDepthTexelSpacing;
    float       _VBufferScatteringIntensity;
    float       _VBufferLocalScatteringIntensity;
    float       _VBufferLastSliceDist;
    float       _vbuffer_pad00_;
    float4      _VBufferViewportSize;
    float4      _VBufferLightingViewportScale;
    float4      _VBufferLightingViewportLimit;
    float4      _VBufferDistanceEncodingParams;
    float4      _VBufferDistanceDecodingParams;
    float4      _VBufferSampleOffset;
    float4      _VLightingRTHandleScale;
    float4x4    _VBufferCoordToViewDirWS;
CBUFFER_END

CBUFFER_START(ShaderVariablesLocalVolume)
    float4      _VolumetricMaterialObbRight;
    float4      _VolumetricMaterialObbUp;
    float4      _VolumetricMaterialObbExtents;
    float4      _VolumetricMaterialObbCenter;
    float4      _VolumetricMaterialAlbedo;
    float4      _VolumetricMaterialRcpPosFaceFade;
    float4      _VolumetricMaterialRcpNegFaceFade;
    float       _VolumetricMaterialInvertFade;
    float       _VolumetricMaterialExtinction;
    float       _VolumetricMaterialRcpDistFadeLen;
    float       _VolumetricMaterialEndTimesRcpDistFadeLen;
    float       _VolumetricMaterialFalloffMode;
    float       _LocalVolume_pad0_;
    float       _LocalVolume_pad1_;
    float       _LocalVolume_pad2_;
CBUFFER_END


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

float GetInversePreviousExposureMultiplier()
{
    return 1.0f;
}
float CurrentExposureMultiplier() // TODO: Replace with GetCurrentExposureMultiplier for URP17 once implemented
{
    return 1.0f;
}

// Copied from EntityLighting
real3 DecodeHDREnvironment(real4 encodedIrradiance, real4 decodeInstructions)
{
    // Take into account texture alpha if decodeInstructions.w is true(the alpha value affects the RGB channels)
    real alpha = max(decodeInstructions.w * (encodedIrradiance.a - 1.0) + 1.0, 0.0);

    // If Linear mode is not supported we can skip exponent part
    return (decodeInstructions.x * PositivePow(alpha, decodeInstructions.y)) * encodedIrradiance.rgb;
}

bool IsInRange(float x, float2 range)
{
    return clamp(x, range.x, range.y) == x;
}

// To avoid half precision issue on mobile, declare float functions.
float4 LinearizeRGBD_Float(float4 value)
{
    float d = value.a;
    float a = 1 - exp(-d);
    float r = (a >= FLT_EPS) ? (d * rcp(a)) : 1;
    return float4(r * value.rgb, d);
}
float4 DelinearizeRGBA_Float(float4 value)
{
    float d = value.a;
    float a = 1 - exp(-d);
    float i = (a >= FLT_EPS) ? (a * rcp(d)) : 1;
    return float4(i * value.rgb, a);
}
float4 DelinearizeRGBD_Float(real4 value)
{
    float d = value.a;
    float a = 1 - exp(-d);
    float i = (a >= FLT_EPS) ? (a * rcp(d)) : 1; // Prevent numerical explosion
    return float4(i * value.rgb, d);
}
float SafeDiv_Float(float numer, float denom)
{
    return (numer != denom) ? numer / denom : 1;
}
//

// Make new cookie sampling function to avoid 'cannot map expression to cs_5_0 instruction' error
real3 SampleMainLightCookieForVoxelLighting(float3 samplePositionWS)
{
    if(!IsMainLightCookieEnabled())
        return real3(1,1,1);

    float2 uv = ComputeLightCookieUVDirectional(_MainLightWorldToLight, samplePositionWS, float4(1, 1, 0, 0), URP_TEXTURE_WRAP_MODE_NONE);
    real4 color = SAMPLE_TEXTURE2D_LOD(_MainLightCookieTexture, sampler_MainLightCookieTexture, uv, 0);

    return IsMainLightCookieTextureRGBFormat() ? color.rgb
             : IsMainLightCookieTextureAlphaFormat() ? color.aaa
             : color.rrr;
}

VoxelLighting EvaluateVoxelLightingDirectional(float extinction, float anisotropy,
                                               JitteredRay ray, float t0, float t1, float dt, float rndVal)
{
    VoxelLighting lighting;
    ZERO_INITIALIZE(VoxelLighting, lighting);

    const float NdotL = 1;

    float tOffset, weight;
    ImportanceSampleHomogeneousMedium(rndVal, extinction, dt, tOffset, weight);

    float t = t0 + tOffset;
    float3 positionWS = ray.originWS + t * ray.jitterDirWS;

    // Main light
    {
        float  cosTheta = dot(_MainLightPosition.xyz, ray.centerDirWS);
        float  phase = CornetteShanksPhasePartVarying(anisotropy, cosTheta);

        // Evaluate sun shadow
        float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
        shadowCoord.w = max(shadowCoord.w, 0.001);
        Light mainLight = GetMainLight();
        mainLight.shadowAttenuation = MainLightShadow(shadowCoord, positionWS, 0, 0);
        half3 color = mainLight.color * lerp(_VBufferScatteringIntensity, mainLight.shadowAttenuation, mainLight.shadowAttenuation < 1);

        // Cookie
    #if defined(_LIGHT_COOKIES)
        color *= SampleMainLightCookieForVoxelLighting(positionWS);
    #endif

        lighting.radianceNoPhase += color * weight;
        lighting.radianceComplete += color * weight * phase;
    }

    // Additional light
#if USE_FORWARD_PLUS
    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
    #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
        float4 lightPositionWS = _AdditionalLightsBuffer[lightIndex].position;
        half3 color = _AdditionalLightsBuffer[lightIndex].color.rgb;
    #else
        float4 lightPositionWS = _AdditionalLightsPosition[lightIndex];
        half3 color = _AdditionalLightsColor[lightIndex].rgb;
    #endif
        
        float  cosTheta = dot(lightPositionWS.xyz, ray.centerDirWS);
        float  phase = CornetteShanksPhasePartVarying(anisotropy, cosTheta);

        // Shadow
        // Directional lights store direction in lightPosition.xyz and have .w set to 0.0.
        // This way the following code will work for both directional and punctual lights.
        float3 lightVector = lightPositionWS.xyz - positionWS * lightPositionWS.w;
        float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);

        half3 lightDirection = half3(lightVector * rsqrt(distanceSqr));
        half shadowAtten = AdditionalLightRealtimeShadow(lightIndex, positionWS, lightDirection);
        color *= lerp(_VBufferScatteringIntensity, shadowAtten, shadowAtten < 1);

        // Cookie
    #if defined(_LIGHT_COOKIES)
        color *= SampleAdditionalLightCookie(lightIndex, positionWS);
    #endif

        lighting.radianceNoPhase += color * weight;
        lighting.radianceComplete += color * weight * phase;
    }
#endif

    return lighting;
}


VoxelLighting EvaluateVoxelLightingLocal(float2 pixelCoord, float extinction, float anisotropy,
                                         JitteredRay ray, float t0, float t1, float dt,
                                         float3 centerWS, float rndVal)
{
    VoxelLighting lighting;
    ZERO_INITIALIZE(VoxelLighting, lighting);

#if USE_FORWARD_PLUS

    uint lightIndex;
    ClusterIterator _urp_internal_clusterIterator = ClusterInit(GetNormalizedScreenSpaceUV(pixelCoord), centerWS, 0);
    [loop]
    while (ClusterNext(_urp_internal_clusterIterator, lightIndex))
    {
        lightIndex += URP_FP_DIRECTIONAL_LIGHTS_COUNT;

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
        ImportanceSamplePunctualLight(rndVal, lightPositionWS.xyz, lightSqRadius,
                                      ray.originWS, ray.jitterDirWS, t0, t1,
                                      t, distSq, rcpPdf);
        float3 positionWS = ray.originWS + t * ray.jitterDirWS;

        float3 lightVector = lightPositionWS.xyz - positionWS * lightPositionWS.w;
        float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);

        half3 lightDirection = half3(lightVector * rsqrt(distanceSqr));
        float attenuation = DistanceAttenuation(distanceSqr, distanceAndSpotAttenuation.xy) * AngleAttenuation(spotDirection.xyz, lightDirection, distanceAndSpotAttenuation.zw);
        color *= attenuation;

        // Shadow
        half shadowAtten = AdditionalLightRealtimeShadow(lightIndex, positionWS, lightDirection);
        color *= lerp(_VBufferLocalScatteringIntensity, shadowAtten, shadowAtten < 1);

        // Cookie
    #if defined(_LIGHT_COOKIES)
        color *= SampleAdditionalLightCookie(lightIndex, positionWS);
    #endif

        float3 centerL  = lightPositionWS.wyz - centerWS;
        float  cosTheta = dot(centerL, ray.centerDirWS) * rsqrt(dot(centerL, centerL));
        float  phase = CornetteShanksPhasePartVarying(anisotropy, cosTheta);

        // Compute transmittance from 't0' to 't'.
        float weight = TransmittanceHomogeneousMedium(extinction, t - t0) * rcpPdf;

        lighting.radianceNoPhase += color * weight;
        lighting.radianceComplete += color * weight * phase;
    }
#endif

    return lighting;
}

#endif