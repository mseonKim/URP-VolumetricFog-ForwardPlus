using UnityEngine;

namespace UniversalForwardPlusVolumetric
{
    internal static class IDs
    {
        public readonly static int _ShaderVariablesFog = Shader.PropertyToID("ShaderVariablesFog");
        public readonly static int _ShaderVariablesVolumetricLighting = Shader.PropertyToID("ShaderVariablesVolumetricLighting");
        public readonly static int _ShaderVariablesLocalVolume = Shader.PropertyToID("ShaderVariablesLocalVolume");

        
        public readonly static int _VBufferDensity = Shader.PropertyToID("_VBufferDensity");
        public readonly static int _VBufferLighting = Shader.PropertyToID("_VBufferLighting");
        public readonly static int _VBufferLightingFiltered = Shader.PropertyToID("_VBufferLightingFiltered");
        public readonly static int _VBufferFeedback = Shader.PropertyToID("_VBufferFeedback");
        public readonly static int _VBufferHistory = Shader.PropertyToID("_VBufferHistory");

        // Fog
        public readonly static int _FogEnabled = Shader.PropertyToID("_FogEnabled");
        public readonly static int _EnableVolumetricFog = Shader.PropertyToID("_EnableVolumetricFog");
        public readonly static int _FogColorMode = Shader.PropertyToID("_FogColorMode");
        public readonly static int _MaxEnvCubemapMip = Shader.PropertyToID("_MaxEnvCubemapMip");
        public readonly static int _FogColor = Shader.PropertyToID("_FogColor");
        public readonly static int _MipFogParameters = Shader.PropertyToID("_MipFogParameters");
        public readonly static int _HeightFogParams = Shader.PropertyToID("_HeightFogParams");
        public readonly static int _HeightFogBaseScattering = Shader.PropertyToID("_HeightFogBaseScattering");
        
        // Volumetric Lighting
        public readonly static int _VolumetricFilteringEnabled = Shader.PropertyToID("_VolumetricFilteringEnabled");
        public readonly static int _VBufferHistoryIsValid = Shader.PropertyToID("_VBufferHistoryIsValid");
        public readonly static int _VBufferSliceCount = Shader.PropertyToID("_VBufferSliceCount");
        public readonly static int _VBufferAnisotropy = Shader.PropertyToID("_VBufferAnisotropy");
        public readonly static int _CornetteShanksConstant = Shader.PropertyToID("_CornetteShanksConstant");
        public readonly static int _VBufferVoxelSize = Shader.PropertyToID("_VBufferVoxelSize");
        public readonly static int _VBufferRcpSliceCount = Shader.PropertyToID("_VBufferRcpSliceCount");
        public readonly static int _VBufferUnitDepthTexelSpacing = Shader.PropertyToID("_VBufferUnitDepthTexelSpacing");
        public readonly static int _VBufferScatteringIntensity = Shader.PropertyToID("_VBufferScatteringIntensity");
        public readonly static int _VBufferLastSliceDist = Shader.PropertyToID("_VBufferLastSliceDist");
        public readonly static int _VBufferViewportSize = Shader.PropertyToID("_VBufferViewportSize");
        public readonly static int _VBufferLightingViewportScale = Shader.PropertyToID("_VBufferLightingViewportScale");
        public readonly static int _VBufferLightingViewportLimit = Shader.PropertyToID("_VBufferLightingViewportLimit");
        public readonly static int _VBufferDistanceEncodingParams = Shader.PropertyToID("_VBufferDistanceEncodingParams");
        public readonly static int _VBufferDistanceDecodingParams = Shader.PropertyToID("_VBufferDistanceDecodingParams");
        public readonly static int _VBufferSampleOffset = Shader.PropertyToID("_VBufferSampleOffset");
        public readonly static int _RTHandleScale = Shader.PropertyToID("_RTHandleScale");
        public readonly static int _PrevCamPosRWS = Shader.PropertyToID("_PrevCamPosRWS");
        public readonly static int _VBufferCoordToViewDirWS = Shader.PropertyToID("_VBufferCoordToViewDirWS");
        public readonly static int _PixelCoordToViewDirWS = Shader.PropertyToID("_PixelCoordToViewDirWS");
        public readonly static int _PrevMatrixVP = Shader.PropertyToID("_PrevMatrixVP");

        // MaxZ Generation
        public readonly static int _InputTexture = Shader.PropertyToID("_InputTexture");
        public readonly static int _OutputTexture = Shader.PropertyToID("_OutputTexture");
        public readonly static int _SrcOffsetAndLimit = Shader.PropertyToID("_SrcOffsetAndLimit");
        public readonly static int _DilationWidth = Shader.PropertyToID("_DilationWidth");
        public readonly static int _MaxZMaskTexture = Shader.PropertyToID("_MaxZMaskTexture");
    }
}