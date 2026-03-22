using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UniversalForwardPlusVolumetric
{
    [Serializable]
    internal struct VolumetricFogSettings
    {
        public bool enabled;
        public FogColorMode colorMode;
        public Color color;
        public Color tint;
        public float mipFogMaxMip;
        public float mipFogNear;
        public float mipFogFar;
        public float baseHeight;
        public float maximumHeight;
        public float fogAttenuationDistance;
        public bool volumetricLighting;
        public bool enableDirectionalLight;
        public bool enablePointAndSpotLight;
        public Color albedo;
        public float directionalScatteringIntensity;
        public float localScatteringIntensity;
        public float anisotropy;
        public float depthExtent;
        public float screenResolutionPercentage;
        public int volumeSliceCount;
        public DenoiseMode denoiseMode;
        public float sampleOffsetWeight;
        public bool autoSliceDistribution;
        public float sliceDistributionUniformity;
        public int blendWeight;

        public bool filterVolume => denoiseMode == DenoiseMode.Gaussian || denoiseMode == DenoiseMode.Both;
        public bool enableReprojection => denoiseMode == DenoiseMode.Reprojection || denoiseMode == DenoiseMode.Both;
        public bool IsActiveForRendering => enabled && volumetricLighting;

        public static VolumetricFogSettings Default => new VolumetricFogSettings
        {
            enabled = false,
            colorMode = FogColorMode.SkyColor,
            color = Color.grey,
            tint = Color.white,
            mipFogMaxMip = 0.5f,
            mipFogNear = 0f,
            mipFogFar = 1000f,
            baseHeight = 0f,
            maximumHeight = 1000f,
            fogAttenuationDistance = 50f,
            volumetricLighting = true,
            enableDirectionalLight = true,
            enablePointAndSpotLight = true,
            albedo = Color.white,
            directionalScatteringIntensity = 1f,
            localScatteringIntensity = 100f,
            anisotropy = 0f,
            depthExtent = 64f,
            screenResolutionPercentage = 12.5f,
            volumeSliceCount = 128,
            denoiseMode = DenoiseMode.Both,
            sampleOffsetWeight = 1f,
            autoSliceDistribution = true,
            sliceDistributionUniformity = 0.75f,
            blendWeight = 7,
        };

        internal int GetHistoryInvalidationHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + enabled.GetHashCode();
                hash = hash * 31 + volumetricLighting.GetHashCode();
                hash = hash * 31 + denoiseMode.GetHashCode();
                hash = hash * 31 + depthExtent.GetHashCode();
                hash = hash * 31 + screenResolutionPercentage.GetHashCode();
                hash = hash * 31 + volumeSliceCount;
                hash = hash * 31 + sampleOffsetWeight.GetHashCode();
                hash = hash * 31 + autoSliceDistribution.GetHashCode();
                hash = hash * 31 + sliceDistributionUniformity.GetHashCode();
                hash = hash * 31 + anisotropy.GetHashCode();
                hash = hash * 31 + directionalScatteringIntensity.GetHashCode();
                hash = hash * 31 + localScatteringIntensity.GetHashCode();
                hash = hash * 31 + enableDirectionalLight.GetHashCode();
                hash = hash * 31 + enablePointAndSpotLight.GetHashCode();
                return hash;
            }
        }
    }

    [Serializable]
    internal struct VolumetricFogResources
    {
        public ComputeShader volumeVoxelizationCS;
        public ComputeShader volumetricLightingCS;
        public ComputeShader volumetricLightingFilteringCS;
        public ComputeShader generateMaxZCS;
        public ComputeShader defaultLocalVolumeShader;
        public Material resolveMat;

        internal bool HasRequiredResources()
        {
            return volumeVoxelizationCS != null
                   && volumetricLightingCS != null
                   && volumetricLightingFilteringCS != null
                   && generateMaxZCS != null
                   && resolveMat != null;
        }

        internal string GetMissingRequiredResourceSummary()
        {
            var missing = new List<string>(5);

            if (volumeVoxelizationCS == null)
                missing.Add(nameof(volumeVoxelizationCS));
            if (volumetricLightingCS == null)
                missing.Add(nameof(volumetricLightingCS));
            if (volumetricLightingFilteringCS == null)
                missing.Add(nameof(volumetricLightingFilteringCS));
            if (generateMaxZCS == null)
                missing.Add(nameof(generateMaxZCS));
            if (resolveMat == null)
                missing.Add(nameof(resolveMat));

            return string.Join(", ", missing);
        }
    }

#if UNITY_EDITOR
    internal static class VolumetricFogResourceLoader
    {
        private const string k_RuntimeFeaturePath = "Runtime/Scripts/FPVolumetricFog.cs";

        private static string s_PackageRootPath;

        internal static void TryAssignDefaults(ref VolumetricFogResources resources)
        {
            if (resources.volumeVoxelizationCS == null)
                resources.volumeVoxelizationCS = LoadAsset<ComputeShader>("Shaders/VolumeVoxelization.compute");
            if (resources.volumetricLightingCS == null)
                resources.volumetricLightingCS = LoadAsset<ComputeShader>("Shaders/VolumetricLighting.compute");
            if (resources.volumetricLightingFilteringCS == null)
                resources.volumetricLightingFilteringCS = LoadAsset<ComputeShader>("Shaders/VolumetricLightingFiltering.compute");
            if (resources.generateMaxZCS == null)
                resources.generateMaxZCS = LoadAsset<ComputeShader>("Shaders/GenerateMaxZ.compute");
            if (resources.defaultLocalVolumeShader == null)
                resources.defaultLocalVolumeShader = LoadAsset<ComputeShader>("Shaders/LocalVolumetricFog/SmokeVolumeMaterial.compute");
            if (resources.resolveMat == null)
                resources.resolveMat = LoadAsset<Material>("Runtime/Materials/FogResolve.mat");
        }

        private static T LoadAsset<T>(string relativePath) where T : UnityEngine.Object
        {
            var packageRoot = GetPackageRootPath();
            if (string.IsNullOrEmpty(packageRoot))
                return null;

            var assetPath = string.Concat(packageRoot, "/", relativePath).Replace("\\", "/");
            return AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }

        private static string GetPackageRootPath()
        {
            if (!string.IsNullOrEmpty(s_PackageRootPath))
                return s_PackageRootPath;

            var guids = AssetDatabase.FindAssets("FPVolumetricFog t:Script");
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
                if (!assetPath.EndsWith(k_RuntimeFeaturePath, StringComparison.Ordinal))
                    continue;

                s_PackageRootPath = assetPath.Substring(0, assetPath.Length - k_RuntimeFeaturePath.Length).TrimEnd('/');
                break;
            }

            return s_PackageRootPath;
        }
    }
#endif
}
