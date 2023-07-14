using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UniversalForwardPlusVolumetric
{
    public enum VoxelSize
    {
        _PerPixel = 1,
        _4 = 4,
        _8 = 8,
        _16 = 16
    }

    [CreateAssetMenu(menuName = "UniversalVolumetric/VolumetricConfig")]
    public class VolumetricConfig : ScriptableObject
    {
        [Header("Resources")]
        public ComputeShader volumetricLightingCS;
        public ComputeShader volumetricLightingFilteringCS;
        public Material resolveMat;

        [Header("Volumetric Lighting")]
        public bool useVolumetricLighting = true;
        public VoxelSize voxelSize = VoxelSize._8;
    }
}
