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
        private readonly static int _MaskTexture = Shader.PropertyToID("_MaskTexture");
        private readonly static int _InteractiveSmokeMask = Shader.PropertyToID("_InteractiveSmokeMask");
        private readonly static int _SmokeVolumeViewProjM = Shader.PropertyToID("_SmokeVolumeViewProjM");

        [Header("Smoke")]
        public Vector3 speed;
        public RenderTexture smokeMaskRT;
        
        // Make sure that you Enabled the camera component & set as overlay mode so you can retrieve VP matrix,
        // but don't link to camera stack to prevent rendering. 
        public Camera maskCamera;

        void Awake()
        {
            k_ShaderTagId = new ShaderTagId("SmokeVolumeDepth");
        }

        public override void SetComputeShaderProperties(CommandBuffer cmd, ComputeShader cs, int kernel)
        {
            cmd.SetComputeTextureParam(cs, kernel, _MaskTexture, mask);
            cmd.SetComputeTextureParam(cs, kernel, _InteractiveSmokeMask, smokeMaskRT);
        }

        public override bool UpdateRenderTextureIfNeeded(ScriptableRenderContext context, CommandBuffer cmd,  ref RenderingData renderingData)
        {
            if (maskCamera == null || smokeMaskRT == null)
                return false;

            var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
            var drawSettings = RenderingUtils.CreateDrawingSettings(k_ShaderTagId, ref renderingData, SortingCriteria.CommonOpaque);

            CoreUtils.SetRenderTarget(cmd, smokeMaskRT, ClearFlag.Color);
            cmd.SetGlobalMatrix(_SmokeVolumeViewProjM, maskCamera.previousViewProjectionMatrix);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filteringSettings);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            return true;
        }

    }
}
