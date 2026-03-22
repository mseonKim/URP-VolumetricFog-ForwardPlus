using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UniversalForwardPlusVolumetric
{
    [Serializable]
    public sealed class FogColorModeParameter : VolumeParameter<FogColorMode>
    {
        public FogColorModeParameter(FogColorMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class DenoiseModeParameter : VolumeParameter<DenoiseMode>
    {
        public DenoiseModeParameter(DenoiseMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    [VolumeComponentMenu("Lighting/FPVolumetricFog")]
    [VolumeRequiresRendererFeatures(typeof(FPVolumetricFog))]
#if UNITY_6000_0_OR_NEWER
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#endif
    public sealed class FPVolumetricFogVolume : VolumeComponent
    {
        public FPVolumetricFogVolume()
        {
            displayName = "FPVolumetricFog";
        }

        [Tooltip("Enables the fog.")]
        public BoolParameter enabled = new BoolParameter(false);

        public FogColorModeParameter colorMode = new FogColorModeParameter(FogColorMode.SkyColor);

        [Tooltip("Specifies the constant color of the fog.")]
        public ColorParameter color = new ColorParameter(Color.grey, false, false, true);

        [Tooltip("Specifies the tint of the fog.")]
        public ColorParameter tint = new ColorParameter(Color.white, false, false, true);

        [Tooltip("Controls the maximum mip map for mip fog (0 is the lowest mip and 1 is the highest mip).")]
        public ClampedFloatParameter mipFogMaxMip = new ClampedFloatParameter(0.5f, 0f, 1f);

        [Tooltip("Sets the distance at which the minimum mip image of the blurred sky texture is used as the fog color.")]
        public MinFloatParameter mipFogNear = new MinFloatParameter(0f, 0f);

        [Tooltip("Sets the distance at which the maximum mip image of the blurred sky texture is used as the fog color.")]
        public MinFloatParameter mipFogFar = new MinFloatParameter(1000f, 0f);

        public FloatParameter baseHeight = new FloatParameter(0f);
        public FloatParameter maximumHeight = new FloatParameter(1000f);

        [Range(1f, 1000f)]
        public MinFloatParameter fogAttenuationDistance = new MinFloatParameter(50f, 1f);

        public BoolParameter volumetricLighting = new BoolParameter(true);
        public BoolParameter enableDirectionalLight = new BoolParameter(true);

        [Tooltip("Point and spot lights are only supported for Forward+.")]
        public BoolParameter enablePointAndSpotLight = new BoolParameter(true);

        public ColorParameter albedo = new ColorParameter(Color.white, false, false, true);
        public MinFloatParameter directionalScatteringIntensity = new MinFloatParameter(1f, 0f);
        public MinFloatParameter localScatteringIntensity = new MinFloatParameter(100f, 0f);
        public ClampedFloatParameter anisotropy = new ClampedFloatParameter(0f, -0.999f, 0.999f);

        [Tooltip("Sets the distance from the Camera near plane to the back of the volumetric lighting buffer.")]
        public NoInterpMinFloatParameter depthExtent = new NoInterpMinFloatParameter(64f, 0.001f);

        [Tooltip("Controls the resolution of the volumetric buffer relative to the screen resolution.")]
        public NoInterpClampedFloatParameter screenResolutionPercentage = new NoInterpClampedFloatParameter(12.5f, 6.25f, 50f);

        [Tooltip("Controls the number of slices used by the volumetric buffer along the camera focal axis.")]
        public NoInterpClampedIntParameter volumeSliceCount = new NoInterpClampedIntParameter(128, 1, 256);

        public DenoiseModeParameter denoiseMode = new DenoiseModeParameter(DenoiseMode.Both);
        public NoInterpClampedFloatParameter sampleOffsetWeight = new NoInterpClampedFloatParameter(1f, 0.001f, 1f);
        public BoolParameter autoSliceDistribution = new BoolParameter(true);
        public NoInterpClampedFloatParameter sliceDistributionUniformity = new NoInterpClampedFloatParameter(0.75f, 0f, 1f);
        public NoInterpClampedIntParameter blendWeight = new NoInterpClampedIntParameter(7, 1, 7);

        internal bool UsesVolumeSource()
        {
            return active && enabled.overrideState;
        }

        internal VolumetricFogSettings ToSettings()
        {
            return new VolumetricFogSettings
            {
                enabled = enabled.value,
                colorMode = colorMode.value,
                color = color.value,
                tint = tint.value,
                mipFogMaxMip = mipFogMaxMip.value,
                mipFogNear = mipFogNear.value,
                mipFogFar = mipFogFar.value,
                baseHeight = baseHeight.value,
                maximumHeight = maximumHeight.value,
                fogAttenuationDistance = fogAttenuationDistance.value,
                volumetricLighting = volumetricLighting.value,
                enableDirectionalLight = enableDirectionalLight.value,
                enablePointAndSpotLight = enablePointAndSpotLight.value,
                albedo = albedo.value,
                directionalScatteringIntensity = directionalScatteringIntensity.value,
                localScatteringIntensity = localScatteringIntensity.value,
                anisotropy = anisotropy.value,
                depthExtent = depthExtent.value,
                screenResolutionPercentage = screenResolutionPercentage.value,
                volumeSliceCount = volumeSliceCount.value,
                denoiseMode = denoiseMode.value,
                sampleOffsetWeight = sampleOffsetWeight.value,
                autoSliceDistribution = autoSliceDistribution.value,
                sliceDistributionUniformity = sliceDistributionUniformity.value,
                blendWeight = blendWeight.value,
            };
        }
    }
}
