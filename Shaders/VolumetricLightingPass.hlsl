#ifndef UNITY_VOLUMETRIC_LIGHTING_PASS_INCLUDED
#define UNITY_VOLUMETRIC_LIGHTING_PASS_INCLUDED

#include "./VBuffer.hlsl"

void AtmosphericScatteringCompute(Varyings input, float3 V, float depth, out float3 color, out float3 opacity)
{
    PositionInputs posInput = GetPositionInput(input.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);

    if (depth == UNITY_RAW_FAR_CLIP_VALUE)
    {
        posInput.positionWS = GetCurrentViewPosition() - V * 100;
    }

    float fogFragDist = distance(posInput.positionWS, GetCurrentViewPosition());

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
    float4 volFog = DelinearizeRGBA(float4(/*FastTonemapInvert*/(value.rgb), value.a));
    color = volFog.rgb;
    opacity = volFog.a;
}

float4 FragVBuffer(Varyings input) : SV_Target
{
    // UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float2 positionSS  = input.positionCS.xy;
    float3 clipSpace = float3(positionSS * _ScreenSize.zw * float2(2.0, -2.0) - float2(1.0, -1.0), 1.0);
    float4 HViewPos = mul(UNITY_MATRIX_I_P, float4(clipSpace, 1.0));
    float3 V = normalize(mul((float3x3)UNITY_MATRIX_I_V, HViewPos.xyz / HViewPos.w)); 
    float depth = LoadSceneDepth(positionSS);

    float3 volColor, volOpacity;
    AtmosphericScatteringCompute(input, V, depth, volColor, volOpacity);

    return float4(volColor, 1.0 - volOpacity.x);
    // return 0;
}

float4 FragPerPixel(Varyings input) : SV_Target
{
    // UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float3 F = -UNITY_MATRIX_V[2].xyz;
    float3 U = UNITY_MATRIX_V[1].xyz;
    float3 R = cross(F, U);
    
    float3 ClipSpace = float3(input.positionCS.xy * _ScreenSize.zw * float2(2.0, -2.0) - float2(1.0, -1.0), 1.0);
    float4 HViewPos = mul(unity_MatrixInvP, float4(ClipSpace, 1.0));
    float3 WorldDir = mul((float3x3)unity_MatrixInvV, HViewPos.xyz / HViewPos.w);

    float3 rayDirWS       = WorldDir; 
    float3 rightDirWS     = cross(rayDirWS, U);
    float  rcpLenRayDir   = rsqrt(dot(rayDirWS, rayDirWS));
    float  rcpLenRightDir = rsqrt(dot(rightDirWS, rightDirWS));

    JitteredRay ray;
    ray.originWS = _WorldSpaceCameraPos.xyz;
    ray.centerDirWS = rayDirWS * rcpLenRayDir;

    float FdotD = dot(F, ray.centerDirWS);
    float unitDistFaceSize = _VBufferUnitDepthTexelSpacing * FdotD * rcpLenRayDir;

    ray.xDirDerivWS = rightDirWS * (rcpLenRightDir * unitDistFaceSize); // Normalize & rescale
    ray.yDirDerivWS = cross(ray.xDirDerivWS, ray.centerDirWS); // Will have the length of 'unitDistFaceSize' by construction

#ifdef ENABLE_REPROJECTION
    float2 sampleOffset = _VBufferSampleOffset.xy;
#else
    float2 sampleOffset = 0;
#endif

    ray.jitterDirWS = normalize(ray.centerDirWS + sampleOffset.x * ray.xDirDerivWS
                                                + sampleOffset.y * ray.yDirDerivWS);
    float tStart = _ProjectionParams.y / dot(ray.jitterDirWS, F); // _ProjectionParams.y = Near

    // We would like to determine the screen pixel (at the full resolution) which
    // the jittered ray corresponds to. The exact solution can be obtained by intersecting
    // the ray with the screen plane, e.i. (ViewSpace(jitterDirWS).z = 1). That's a little expensive.
    // So, as an approximation, we ignore the curvature of the frustum.
    uint2 pixelCoord = (uint2)((input.positionCS.xy + sampleOffset));

    ray.geomDist = FLT_INF;
    ray.maxDist = FLT_INF;
    float deviceDepth = LoadSceneDepth(pixelCoord);
    if (deviceDepth > 0) // Skip the skybox
    {
        // Convert it to distance along the ray. Doesn't work with tilt shift, etc.
        float linearDepth = LinearEyeDepth(deviceDepth, _ZBufferParams);
        ray.geomDist = linearDepth * rcp(dot(ray.jitterDirWS, F));

        // This should really be using a max sampler here. This is a bit overdilating given that it is already dilated.
        // Better to be safer though.
        // float4 d = GATHER_RED_TEXTURE2D_X(_MaxZMaskTexture, s_point_clamp_sampler, UV) * rcp(dot(ray.jitterDirWS, F));
        // ray.maxDist = max(Max3(d.x, d.y, d.z), d.w);
        ray.maxDist = ray.geomDist;
    }

    float   t0 = max(tStart, DecodeLogarithmicDepthGeneralized(0, _VBufferDistanceDecodingParams));
    float   de = _VBufferRcpSliceCount;
    
    float3  totalRadiance = 0;
    float   opticalDepth = 0;
    float3  throughput = 1.0;
    float   anisotropy = _VBufferAnisotropy;

    // Ray marching
    uint slice = 0;
    for (; slice < _VBufferSliceCount; slice++)
    {
        float e1 = slice * de + de; // (slice + 1) / sliceCount
        float t1 = max(tStart, DecodeLogarithmicDepthGeneralized(e1, _VBufferDistanceDecodingParams));
        float tNext = t1;

        bool containsOpaqueGeometry = IsInRange(ray.geomDist, float2(t0, t1));
        if (containsOpaqueGeometry)
        {
            // Only integrate up to the opaque surface (make the voxel shorter, but not completely flat).
            // Note that we can NOT completely stop integrating when the ray reaches geometry, since
            // otherwise we get flickering at geometric discontinuities if reprojection is enabled.
            // In this case, a temporally stable light leak is better than flickering.
            t1 = max(t0 * 1.0001, ray.geomDist);
        }

        float dt = t1 - t0;
        if (dt <= 0.0)
        {
            t0 = t1;
            continue;
        }

        float  t = DecodeLogarithmicDepthGeneralized(e1 - 0.5 * de, _VBufferDistanceDecodingParams);
        float3 centerWS = ray.centerDirWS * t + ray.originWS;
        float3 radiance = 0;

        float3 scattering = 0.05;
        float extinction = 0.05;

        // Perform per-pixel randomization by adding an offset and then sampling uniformly
        // (in the log space) in a vein similar to Stochastic Universal Sampling:
        // https://en.wikipedia.org/wiki/Stochastic_universal_sampling
        float perPixelRandomOffset = GenerateHashedRandomFloat(input.positionCS.xy);

    #ifdef ENABLE_REPROJECTION
        // This is a time-based sequence of 7 equidistant numbers from 1/14 to 13/14.
        // Each of them is the centroid of the interval of length 2/14.
        float rndVal = frac(perPixelRandomOffset + _VBufferSampleOffset.z);
    #else
        float rndVal = frac(perPixelRandomOffset + 0.5);
    #endif


        VoxelLighting aggregateLighting;
        ZERO_INITIALIZE(VoxelLighting, aggregateLighting);

        // Prevent division by 0.
        extinction = max(extinction, FLT_MIN);

        float sampleOpticalDepth = extinction * dt;
        float sampleTransmittance = exp(-sampleOpticalDepth);

        // TODO: Directional

        // Local
        {
            VoxelLighting lighting = EvaluateVoxelLightingLocal(pixelCoord, extinction, anisotropy,
                                                                ray, t0, t1, dt, centerWS, rndVal);
            aggregateLighting.radianceNoPhase  += lighting.radianceNoPhase;
            aggregateLighting.radianceComplete += lighting.radianceComplete;
        }

        float phase = _CornetteShanksConstant; // or rcp(4.0 * PI)
        float4 blendValue = float4(aggregateLighting.radianceComplete,  extinction * dt);
        totalRadiance += throughput * scattering * (phase * blendValue.rgb);
        opticalDepth += 0.5 * blendValue.a;

        throughput *= sampleTransmittance;

        opticalDepth += 0.5 * blendValue.a;

        if (t0 * 0.99 > ray.maxDist)
        {
            break;
        }
        t0 = tNext;
    }

    return float4(totalRadiance, opticalDepth);
}

#endif