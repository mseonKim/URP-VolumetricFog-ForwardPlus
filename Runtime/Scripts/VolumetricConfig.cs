using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UniversalForwardPlusVolumetric
{
    public enum VoxelMode
    {
        _PerPixel,
        _VBuffer
    }

    public enum FogControl
    {
        /// <summary>
        /// Use this mode if you want to change the fog control properties based on a higher abstraction level centered around performance.
        /// </summary>
        Balance,

        /// <summary>
        /// Use this mode if you want to have direct access to the internal properties that control volumetric fog.
        /// </summary>
        Manual
    }

    public enum DenoiseMode
    {
        None,
        Reprojection,
        Gaussian,
        Both
    }

    [CreateAssetMenu(menuName = "UniversalVolumetric/VolumetricConfig")]
    public class VolumetricConfig : ScriptableObject
    {
        [Header("Resources")]
        public ComputeShader volumeVoxelizationCS;
        public ComputeShader volumetricLightingCS;
        public ComputeShader volumetricLightingFilteringCS;
        public ComputeShader generateMaxZCS;
        public Material resolveMat;

        [Header("Fog")]
        // Height Fog
        public float baseHeight;
        public float maximumHeight = 1000f;
        [Range(1f, 400f)]
        public float fogAttenuationDistance = 50f;

        [Header("Volumetric Lighting")]
        public bool useVolumetricLighting = true;
        public VoxelMode voxelMode = VoxelMode._VBuffer;    // TODO: Remove this
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
