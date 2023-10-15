using UnityEngine;

namespace UniversalForwardPlusVolumetric
{
    internal static class IDs
    {
        public readonly static int _ShaderVariablesFog = Shader.PropertyToID("ShaderVariablesFog");
        public readonly static int _ShaderVariablesVolumetricLighting = Shader.PropertyToID("ShaderVariablesVolumetricLighting");
        public readonly static int _ShaderVariablesLocalVolume = Shader.PropertyToID("ShaderVariablesLocalVolume");

        // VBuffers
        public readonly static int _VBufferDensity = Shader.PropertyToID("_VBufferDensity");
        public readonly static int _VBufferLighting = Shader.PropertyToID("_VBufferLighting");
        public readonly static int _VBufferLightingFiltered = Shader.PropertyToID("_VBufferLightingFiltered");
        public readonly static int _VBufferFeedback = Shader.PropertyToID("_VBufferFeedback");
        public readonly static int _VBufferHistory = Shader.PropertyToID("_VBufferHistory");

        // Volumetric Lighting
        public readonly static int _VBufferDistanceDecodingParams = Shader.PropertyToID("_VBufferDistanceDecodingParams");
        public readonly static int _PrevCamPosRWS = Shader.PropertyToID("_PrevCamPosRWS");
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