/**
 * NOTE) Add below pass to your shader
 *       Pass
 *       {
 *           Name "SmokeVolumeDepth"
 *           Tags{"LightMode" = "SmokeVolumeDepth"}
 *
 *           ZWrite On
 *           ZTest LEqual
 *           Cull Off
 *           BlendOp Max
 *
 *           HLSLPROGRAM
 *	    
 *           #pragma vertex SmokeDepthVertex
 *           #pragma fragment SmokeDepthFragment
 *
 *           #include "Packages/com.unity.universal-forwardplus-volumetric/Shaders/LocalVolumetricFog/SmokeDepthPass.hlsl"
 *           ENDHLSL
 *       }
 */

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
        
        /*
         * [Enable once the interactive smoke is fixed]
         * public RenderTexture smokeMaskRT;
         * // Make sure that you Enabled the camera component & set as overlay mode so you can retrieve VP matrix,
         * // but don't link to camera stack to prevent rendering. 
         * public Camera maskCamera;
        */

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
            // cmd.SetComputeTextureParam(cs, kernel, IDs._InteractiveSmokeMask, smokeMaskRT);
            // cmd.SetComputeFloatParam(cs, IDs._SmokeCameraFarPlane, maskCamera.farClipPlane);
        }

        public override bool UpdateRenderTextureIfNeeded(ScriptableRenderContext context, CommandBuffer cmd,  ref RenderingData renderingData)
        {
            // [Enable once the interactive smoke is fixed]
            return false;

            // if (maskCamera == null || smokeMaskRT == null)
            //     return false;

            // var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
            // var drawSettings = RenderingUtils.CreateDrawingSettings(k_ShaderTagId, ref renderingData, SortingCriteria.CommonOpaque);

            // CoreUtils.SetRenderTarget(cmd, smokeMaskRT, ClearFlag.Color);
            // cmd.SetGlobalMatrix(IDs._SmokeVolumeViewProjM, maskCamera.previousViewProjectionMatrix);
            // context.ExecuteCommandBuffer(cmd);
            // cmd.Clear();
            // context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filteringSettings);
            // context.ExecuteCommandBuffer(cmd);
            // cmd.Clear();

            // return true;
        }

    }
}
