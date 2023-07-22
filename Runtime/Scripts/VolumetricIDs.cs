using UnityEngine;

namespace UniversalForwardPlusVolumetric
{
    public static class IDs
    {
        public static int _VBufferDensity = Shader.PropertyToID("_VBufferDensity");
        public static int _VBufferLighting = Shader.PropertyToID("_VBufferLighting");
        public static int _VBufferLightingFiltered = Shader.PropertyToID("_VBufferLightingFiltered");
        public static int _VBufferFeedback = Shader.PropertyToID("_VBufferFeedback");
        public static int _VBufferHistory = Shader.PropertyToID("_VBufferHistory");

        // Fog
        public static int _FogEnabled = Shader.PropertyToID("_FogEnabled");
        public static int _EnableVolumetricFog = Shader.PropertyToID("_EnableVolumetricFog");
        public static int _FogColorMode = Shader.PropertyToID("_FogColorMode");
        public static int _MaxEnvCubemapMip = Shader.PropertyToID("_MaxEnvCubemapMip");
        public static int _FogColor = Shader.PropertyToID("_FogColor");
        public static int _MipFogParameters = Shader.PropertyToID("_MipFogParameters");
        public static int _HeightFogParams = Shader.PropertyToID("_HeightFogParams");
        public static int _HeightFogBaseScattering = Shader.PropertyToID("_HeightFogBaseScattering");
        
        // Volumetric Lighting
        public static int _VolumetricFilteringEnabled = Shader.PropertyToID("_VolumetricFilteringEnabled");
        public static int _VBufferHistoryIsValid = Shader.PropertyToID("_VBufferHistoryIsValid");
        public static int _VBufferSliceCount = Shader.PropertyToID("_VBufferSliceCount");
        public static int _VBufferAnisotropy = Shader.PropertyToID("_VBufferAnisotropy");
        public static int _CornetteShanksConstant = Shader.PropertyToID("_CornetteShanksConstant");
        public static int _VBufferVoxelSize = Shader.PropertyToID("_VBufferVoxelSize");
        public static int _VBufferRcpSliceCount = Shader.PropertyToID("_VBufferRcpSliceCount");
        public static int _VBufferUnitDepthTexelSpacing = Shader.PropertyToID("_VBufferUnitDepthTexelSpacing");
        public static int _VBufferScatteringIntensity = Shader.PropertyToID("_VBufferScatteringIntensity");
        public static int _VBufferLastSliceDist = Shader.PropertyToID("_VBufferLastSliceDist");
        public static int _VBufferViewportSize = Shader.PropertyToID("_VBufferViewportSize");
        public static int _VBufferLightingViewportScale = Shader.PropertyToID("_VBufferLightingViewportScale");
        public static int _VBufferLightingViewportLimit = Shader.PropertyToID("_VBufferLightingViewportLimit");
        public static int _VBufferDistanceEncodingParams = Shader.PropertyToID("_VBufferDistanceEncodingParams");
        public static int _VBufferDistanceDecodingParams = Shader.PropertyToID("_VBufferDistanceDecodingParams");
        public static int _VBufferSampleOffset = Shader.PropertyToID("_VBufferSampleOffset");
        public static int _RTHandleScale = Shader.PropertyToID("_RTHandleScale");
        public static int _PrevCamPosRWS = Shader.PropertyToID("_PrevCamPosRWS");
        public static int _VBufferCoordToViewDirWS = Shader.PropertyToID("_VBufferCoordToViewDirWS");
        public static int _PrevMatrixVP = Shader.PropertyToID("_PrevMatrixVP");

        // MaxZ Generation
        public static int _InputTexture = Shader.PropertyToID("_InputTexture");
        public static int _OutputTexture = Shader.PropertyToID("_OutputTexture");
        public static int _SrcOffsetAndLimit = Shader.PropertyToID("_SrcOffsetAndLimit");
        public static int _DilationWidth = Shader.PropertyToID("_DilationWidth");
        public static int _MaxZMaskTexture = Shader.PropertyToID("_MaxZMaskTexture");
    }
}