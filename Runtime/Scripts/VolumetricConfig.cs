using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UniversalForwardPlusVolumetric
{
    public enum FogColorMode
    {
        /// <summary>Fog is a constant color.</summary>
        ConstantColor,
        /// <summary>Fog uses the current sky to determine its color.</summary>
        SkyColor,
    }

    public enum VoxelMode
    {
        _PerPixel,
        _VBuffer
    }

    public enum DenoiseMode
    {
        None,
        Reprojection,
        Gaussian,
        Both
    }

    [CreateAssetMenu(menuName = "UniversalVolumetric/VolumetricFogConfig")]
    public class VolumetricConfig : ScriptableObject
    {
        [Header("Resources")]
        public ComputeShader volumeVoxelizationCS;
        public ComputeShader volumetricLightingCS;
        public ComputeShader volumetricLightingFilteringCS;
        public ComputeShader generateMaxZCS;
        public Material resolveMat;

        [Header("Fog")]
        // Fog Base
        [Tooltip("Enables the fog.")]
        public bool enabled = false;
        public FogColorMode colorMode = FogColorMode.SkyColor;
        [Tooltip("Specifies the constant color of the fog.")]
        public Color color = Color.grey;
        [Tooltip("Specifies the tint of the fog.")]
        public Color tint = Color.white;
        [Tooltip("Sets the maximum fog distance when it shades the skybox or the Far Clipping Plane of the Camera.")]
        public float maxFogDistance = 5000f;
        [Tooltip("Controls the maximum mip map for mip fog (0 is the lowest mip and 1 is the highest mip).")]
        [Range(0f, 1f)] public float mipFogMaxMip = 0.5f;
        [Tooltip("Sets the distance at which the minimum mip image of the blurred sky texture as the fog color.")]
        public float mipFogNear = 0f;
        [Tooltip("Sets the distance at which the maximum mip image of the blurred sky texture as the fog color.")]
        public float mipFogFar = 1000f;

        // Height Fog
        public float baseHeight;
        public float maximumHeight = 1000f;
        [Range(1f, 400f)]
        public float fogAttenuationDistance = 50f;

        [Header("Volumetric Lighting")]
        public bool useVolumetricLighting = true;
        [HideInInspector] public VoxelMode voxelMode = VoxelMode._VBuffer;    // TODO: Remove this
        public Color albedo = Color.white;
        [Range(0f, 10f)]
        public float intensity = 1f;
        [Range(-1f, 1f)]
        public float anisotropy;
        [Tooltip("Sets the distance (in meters) from the Camera's Near Clipping Plane to the back of the Camera's volumetric lighting buffer. The lower the distance is, the higher the fog quality is.")]
        [Range(0.1f, 64f)]
        public float depthExtent = 64f;
        [Tooltip("Controls the resolution of the volumetric buffer (3D texture) along the x-axis and y-axis relative to the resolution of the screen.")]
        [Range(6.25f, 50f)]
        public float screenResolutionPercentage = 12.5f;
        [Tooltip("Controls the number of slices to use the volumetric buffer (3D texture) along the camera's focal axis.")]
        [Range(1, 256)]
        public int volumeSliceCount = 64;

        public DenoiseMode denoiseMode = DenoiseMode.Gaussian;
        public bool filterVolume => (denoiseMode == DenoiseMode.Gaussian || denoiseMode == DenoiseMode.Both);
        public bool enableReprojection => (denoiseMode == DenoiseMode.Reprojection || denoiseMode == DenoiseMode.Both);
        
        [Range(0.001f, 1f)]
        public float sampleOffsetWeight = 1f;
        [Tooltip("Controls the distribution of slices along the Camera's focal axis. 0 is exponential distribution and 1 is linear distribution.")]
        [Range(0, 1f)]
        public float sliceDistributionUniformity = 0.75f;
    }
}
