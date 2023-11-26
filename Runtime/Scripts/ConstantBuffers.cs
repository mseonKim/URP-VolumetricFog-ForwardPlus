using UnityEngine;
using UnityEngine.Rendering;

namespace UniversalForwardPlusVolumetric
{
    internal struct ShaderVariablesFog
    {
        public uint         _FogEnabled;
        public uint         _EnableVolumetricFog;
        public uint         _FogColorMode;
        public uint         _MaxEnvCubemapMip;
        public Vector4      _FogColor;
        public Vector4      _MipFogParameters;
        public Vector4      _HeightFogParams;
        public Vector4      _HeightFogBaseScattering;
    }

    internal struct ShaderVariablesVolumetricLighting
    {
        public uint         _VolumetricFilteringEnabled;
        public uint         _VBufferHistoryIsValid;
        public uint         _VBufferSliceCount;
        public float        _VBufferAnisotropy;
        public float        _CornetteShanksConstant;
        public float        _VBufferVoxelSize;
        public float        _VBufferRcpSliceCount;
        public float        _VBufferUnitDepthTexelSpacing;
        public float        _VBufferScatteringIntensity;
        public float        _VBufferLocalScatteringIntensity;
        public float        _VBufferLastSliceDist;
        public float        _vbuffer_pad00_;
        public Vector4      _VBufferViewportSize;
        public Vector4      _VBufferLightingViewportScale;
        public Vector4      _VBufferLightingViewportLimit;
        public Vector4      _VBufferDistanceEncodingParams;
        public Vector4      _VBufferDistanceDecodingParams;
        public Vector4      _VBufferSampleOffset;
        public Vector4      _VLightingRTHandleScale;
        public Matrix4x4    _VBufferCoordToViewDirWS;
    }

    internal struct ShaderVariablesLocalVolume
    {
        public Vector4      _VolumetricMaterialObbRight;
        public Vector4      _VolumetricMaterialObbUp;
        public Vector4      _VolumetricMaterialObbExtents;
        public Vector4      _VolumetricMaterialObbCenter;
        public Vector4      _VolumetricMaterialAlbedo;
        public Vector4      _VolumetricMaterialRcpPosFaceFade;
        public Vector4      _VolumetricMaterialRcpNegFaceFade;
        public float        _VolumetricMaterialInvertFade;
        public float        _VolumetricMaterialExtinction;
        public float        _VolumetricMaterialRcpDistFadeLen;
        public float        _VolumetricMaterialEndTimesRcpDistFadeLen;
        public float        _VolumetricMaterialFalloffMode;
        public float        _LocalVolume_pad0_;
        public float        _LocalVolume_pad1_;
        public float        _LocalVolume_pad2_;
    }
}