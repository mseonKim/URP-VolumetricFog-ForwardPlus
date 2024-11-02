using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UniversalForwardPlusVolumetric
{
    public class SmokeVolume : LocalVolumetricFog
    {
        private static ShaderTagId k_ShaderTagId;
        private static class IDs
        {
            public readonly static int _MaskTexture = Shader.PropertyToID("_MaskTexture");
            public readonly static int _InteractiveSmokeMask = Shader.PropertyToID("_InteractiveSmokeMask");
            public readonly static int _SmokeVolumeViewProjM = Shader.PropertyToID("_SmokeVolumeViewProjM");
            public readonly static int _SmokeCameraFarPlane = Shader.PropertyToID("_SmokeCameraFarPlane");
            public readonly static int _SmokeVolumeParams0 = Shader.PropertyToID("_SmokeVolumeParams0");
            public readonly static int _SmokeVolumeParams1 = Shader.PropertyToID("_SmokeVolumeParams1");
        }

        [Header("Smoke")]
        public float windSpeed = 1f;
        public Vector2 windDirection = new Vector2(1f, 0f);
        public float counterFlowSpeed = 1f;
        public float tiling = 0.5f;
        public float detailNoiseTiling = 0.85f;
        public float flatten = 2f;
        

        void Awake()
        {
            k_ShaderTagId = new ShaderTagId("SmokeVolumeDepth");
        }

        public override void SetComputeShaderProperties(CommandBuffer cmd, ComputeShader cs, int kernel)
        {
            cmd.SetComputeTextureParam(cs, kernel, IDs._MaskTexture, mask);
            var normalizedWindDirection = windDirection.normalized;
            cmd.SetComputeVectorParam(cs, IDs._SmokeVolumeParams0, new Vector4(windSpeed, counterFlowSpeed, normalizedWindDirection.x, normalizedWindDirection.y));
            cmd.SetComputeVectorParam(cs, IDs._SmokeVolumeParams1, new Vector4(tiling, detailNoiseTiling, flatten, 0));
        }

#if ENABLE_URP_VOLUEMTRIC_FOG_RENDERGRAPH
        public override void SetComputeShaderProperties(ComputeCommandBuffer cmd, ComputeShader cs, int kernel)
        {
            cs.SetTexture(kernel, IDs._MaskTexture, mask);
            var normalizedWindDirection = windDirection.normalized;
            cmd.SetComputeVectorParam(cs, IDs._SmokeVolumeParams0, new Vector4(windSpeed, counterFlowSpeed, normalizedWindDirection.x, normalizedWindDirection.y));
            cmd.SetComputeVectorParam(cs, IDs._SmokeVolumeParams1, new Vector4(tiling, detailNoiseTiling, flatten, 0));
        }
#endif
    }
}
