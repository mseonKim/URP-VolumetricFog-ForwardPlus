using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_URP_VOLUEMTRIC_FOG_RENDERGRAPH
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace UniversalForwardPlusVolumetric
{
    public class GenerateMaxZPass : ScriptableRenderPass
    {
        private GenerateMaxZMaskPassData m_PassData;
        private RTHandle m_MaxZ8xBufferHandle;
        private RTHandle m_MaxZBufferHandle;
        private RTHandle m_DilatedMaxZBufferHandle;
        private VBufferParameters m_VBufferParameters;
        private ProfilingSampler m_ProfilingSampler;

        public GenerateMaxZPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            m_PassData = new GenerateMaxZMaskPassData();
        }

        public void Setup(VolumetricConfig config, in VBufferParameters vBufferParameters)
        {
            m_VBufferParameters = vBufferParameters;
            m_PassData.generateMaxZCS = config.generateMaxZCS;
            m_ProfilingSampler = new ProfilingSampler("Generate MaxZ");
        }

        public void Dispose()
        {
            m_MaxZ8xBufferHandle?.Release();
            m_MaxZBufferHandle?.Release();
            m_DilatedMaxZBufferHandle?.Release();
        }

#if UNITY_6000_0_OR_NEWER
        [Obsolete]
#endif
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (m_PassData.generateMaxZCS == null)
                return;
            
            CoreUtils.SetKeyword(m_PassData.generateMaxZCS, "PLANAR_OBLIQUE_DEPTH", false);

            m_PassData.maxZKernel = m_PassData.generateMaxZCS.FindKernel("ComputeMaxZ");
            m_PassData.maxZDownsampleKernel = m_PassData.generateMaxZCS.FindKernel("ComputeFinalMask");
            m_PassData.dilateMaxZKernel = m_PassData.generateMaxZCS.FindKernel("DilateMask");

            var camera = renderingData.cameraData.camera;
            Vector2Int intermediateMaskSize = new Vector2Int();
            Vector2Int finalMaskSize = new Vector2Int();

            intermediateMaskSize.x = VolumetricUtils.DivRoundUp(camera.scaledPixelWidth, 8);
            intermediateMaskSize.y = VolumetricUtils.DivRoundUp(camera.scaledPixelHeight, 8);
            finalMaskSize.x = intermediateMaskSize.x / 2;
            finalMaskSize.y = intermediateMaskSize.y / 2;

            m_PassData.intermediateMaskSize = intermediateMaskSize;
            m_PassData.finalMaskSize = finalMaskSize;

            var currentParams = m_VBufferParameters;
            float ratio = (float)currentParams.viewportSize.x / (float)camera.scaledPixelWidth;
            m_PassData.dilationWidth = ratio < 0.1f ? 2 : ratio < 0.5f ? 1 : 0;
            m_PassData.viewCount = 1;

            // Create render texture
            var desc = new RenderTextureDescriptor(Mathf.CeilToInt(Screen.width * 0.125f), Mathf.CeilToInt(Screen.height * 0.125f), RenderTextureFormat.RFloat, 0);
            desc.dimension = TextureDimension.Tex2D;
            desc.useDynamicScale = true;
            desc.enableRandomWrite = true;
            RenderingUtils.ReAllocateIfNeeded(ref m_MaxZ8xBufferHandle, desc, FilterMode.Bilinear, name:"MaxZ mask 8x");
            RenderingUtils.ReAllocateIfNeeded(ref m_MaxZBufferHandle, desc, FilterMode.Bilinear, name:"MaxZ mask");

            desc.width = Mathf.CeilToInt(Screen.width / 16.0f);
            desc.height = Mathf.CeilToInt(Screen.height / 16.0f);
            RenderingUtils.ReAllocateIfNeeded(ref m_DilatedMaxZBufferHandle, desc, FilterMode.Bilinear, name:"Dilated MaxZ mask");
        }

#if UNITY_6000_0_OR_NEWER
        [Obsolete]
#endif
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var data = m_PassData;
            var cs = data.generateMaxZCS;

            if (cs == null)
                return;

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var kernel = data.maxZKernel;
                int maskW = data.intermediateMaskSize.x;
                int maskH = data.intermediateMaskSize.y;

                int dispatchX = maskW;
                int dispatchY = maskH;

                cmd.SetComputeTextureParam(cs, kernel, IDs._OutputTexture, m_MaxZ8xBufferHandle);
                cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, data.viewCount);

                // --------------------------------------------------------------
                // Downsample to 16x16 and compute gradient if required

                kernel = data.maxZDownsampleKernel;

                cmd.SetComputeTextureParam(cs, kernel, IDs._InputTexture, m_MaxZ8xBufferHandle);
                cmd.SetComputeTextureParam(cs, kernel, IDs._OutputTexture, m_MaxZBufferHandle);

                Vector4 srcLimitAndDepthOffset = new Vector4(maskW, maskH, 0, 0);
                cmd.SetComputeVectorParam(cs, IDs._SrcOffsetAndLimit, srcLimitAndDepthOffset);
                cmd.SetComputeFloatParam(cs, IDs._DilationWidth, data.dilationWidth);

                int finalMaskW = Mathf.CeilToInt(maskW / 2.0f);
                int finalMaskH = Mathf.CeilToInt(maskH / 2.0f);

                dispatchX = VolumetricUtils.DivRoundUp(finalMaskW, 8);
                dispatchY = VolumetricUtils.DivRoundUp(finalMaskH, 8);

                cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, data.viewCount);


                // --------------------------------------------------------------
                // Dilate max Z and gradient.
                kernel = data.dilateMaxZKernel;

                cmd.SetComputeTextureParam(cs, kernel, IDs._InputTexture, m_MaxZBufferHandle);
                cmd.SetComputeTextureParam(cs, kernel, IDs._OutputTexture, m_DilatedMaxZBufferHandle);

                srcLimitAndDepthOffset.x = finalMaskW;
                srcLimitAndDepthOffset.y = finalMaskH;
                cmd.SetComputeVectorParam(cs, IDs._SrcOffsetAndLimit, srcLimitAndDepthOffset);

                cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, data.viewCount);

                // Set global texture for volumetric passes
                cmd.SetGlobalTexture(IDs._MaxZMaskTexture, m_DilatedMaxZBufferHandle);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
        }

        private class GenerateMaxZMaskPassData
        {
            public ComputeShader generateMaxZCS;
            public int maxZKernel;
            public int maxZDownsampleKernel;
            public int dilateMaxZKernel;

            public Vector2Int intermediateMaskSize;
            public Vector2Int finalMaskSize;
            // public Vector2Int minDepthMipOffset;

            public float dilationWidth;
            public int viewCount;
        }

#if ENABLE_URP_VOLUEMTRIC_FOG_RENDERGRAPH
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
        {
            if (m_PassData.generateMaxZCS == null)
                return;

            using (var builder = renderGraph.AddComputePass<GenerateMaxZMaskRGPassData>("Generate MaxZ", out var passData, m_ProfilingSampler))
            {
                passData.generateMaxZCS = m_PassData.generateMaxZCS;

                UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();
                UniversalResourceData resourceData = frameContext.Get<UniversalResourceData>();

                CoreUtils.SetKeyword(passData.generateMaxZCS, "PLANAR_OBLIQUE_DEPTH", false);
                builder.AllowGlobalStateModification(true);
                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);

                passData.maxZKernel = passData.generateMaxZCS.FindKernel("ComputeMaxZ");
                passData.maxZDownsampleKernel = passData.generateMaxZCS.FindKernel("ComputeFinalMask");
                passData.dilateMaxZKernel = passData.generateMaxZCS.FindKernel("DilateMask");

                var camera = cameraData.camera;
                Vector2Int intermediateMaskSize = new Vector2Int();
                Vector2Int finalMaskSize = new Vector2Int();

                intermediateMaskSize.x = VolumetricUtils.DivRoundUp(camera.scaledPixelWidth, 8);
                intermediateMaskSize.y = VolumetricUtils.DivRoundUp(camera.scaledPixelHeight, 8);
                finalMaskSize.x = intermediateMaskSize.x / 2;
                finalMaskSize.y = intermediateMaskSize.y / 2;

                passData.intermediateMaskSize = intermediateMaskSize;
                passData.finalMaskSize = finalMaskSize;

                var currentParams = m_VBufferParameters;
                float ratio = (float)currentParams.viewportSize.x / (float)camera.scaledPixelWidth;
                passData.dilationWidth = ratio < 0.1f ? 2 : ratio < 0.5f ? 1 : 0;
                passData.viewCount = 1;

                // Create render texture
                var desc = new RenderTextureDescriptor(Mathf.CeilToInt(Screen.width * 0.125f), Mathf.CeilToInt(Screen.height * 0.125f), RenderTextureFormat.RFloat, 0);
                desc.dimension = TextureDimension.Tex2D;
                desc.useDynamicScale = true;
                desc.enableRandomWrite = true;
                var maxZ8xBufferHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "MaxZ mask 8x", false, FilterMode.Bilinear);
                var maxZBufferHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "MaxZ mask", false, FilterMode.Bilinear);
                builder.UseTexture(maxZ8xBufferHandle, AccessFlags.ReadWrite);
                builder.UseTexture(maxZBufferHandle, AccessFlags.ReadWrite);

                desc.width = Mathf.CeilToInt(Screen.width / 16.0f);
                desc.height = Mathf.CeilToInt(Screen.height / 16.0f);
                var dilatedMaxZBufferHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "Dilated MaxZ mask", false, FilterMode.Bilinear);
                builder.UseTexture(dilatedMaxZBufferHandle, AccessFlags.ReadWrite);

                passData.maxZ8xBufferHandle = maxZ8xBufferHandle;
                passData.maxZBufferHandle = maxZBufferHandle;
                passData.dilatedMaxZBufferHandle = dilatedMaxZBufferHandle;

                builder.SetRenderFunc((GenerateMaxZMaskRGPassData data, ComputeGraphContext context) => ExecutePass(context.cmd, data, m_ProfilingSampler));
            }
        }

        private static void ExecutePass(ComputeCommandBuffer cmd, GenerateMaxZMaskRGPassData data, ProfilingSampler profilingSampler)
        {
            var cs = data.generateMaxZCS;
            if (cs == null)
                return;

            using (new ProfilingScope(cmd, profilingSampler))
            {
                var kernel = data.maxZKernel;
                int maskW = data.intermediateMaskSize.x;
                int maskH = data.intermediateMaskSize.y;

                int dispatchX = maskW;
                int dispatchY = maskH;

                cmd.SetComputeTextureParam(cs, kernel, IDs._OutputTexture, data.maxZ8xBufferHandle);
                cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, data.viewCount);

                // --------------------------------------------------------------
                // Downsample to 16x16 and compute gradient if required

                kernel = data.maxZDownsampleKernel;

                cmd.SetComputeTextureParam(cs, kernel, IDs._InputTexture, data.maxZ8xBufferHandle);
                cmd.SetComputeTextureParam(cs, kernel, IDs._OutputTexture, data.maxZBufferHandle);

                Vector4 srcLimitAndDepthOffset = new Vector4(maskW, maskH, 0, 0);
                cmd.SetComputeVectorParam(cs, IDs._SrcOffsetAndLimit, srcLimitAndDepthOffset);
                cmd.SetComputeFloatParam(cs, IDs._DilationWidth, data.dilationWidth);

                int finalMaskW = Mathf.CeilToInt(maskW / 2.0f);
                int finalMaskH = Mathf.CeilToInt(maskH / 2.0f);

                dispatchX = VolumetricUtils.DivRoundUp(finalMaskW, 8);
                dispatchY = VolumetricUtils.DivRoundUp(finalMaskH, 8);

                cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, data.viewCount);


                // --------------------------------------------------------------
                // Dilate max Z and gradient.
                kernel = data.dilateMaxZKernel;

                cmd.SetComputeTextureParam(cs, kernel, IDs._InputTexture, data.maxZBufferHandle);
                cmd.SetComputeTextureParam(cs, kernel, IDs._OutputTexture, data.dilatedMaxZBufferHandle);

                srcLimitAndDepthOffset.x = finalMaskW;
                srcLimitAndDepthOffset.y = finalMaskH;
                cmd.SetComputeVectorParam(cs, IDs._SrcOffsetAndLimit, srcLimitAndDepthOffset);

                cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, data.viewCount);

                // Set global texture for volumetric passes
                cmd.SetGlobalTexture(IDs._MaxZMaskTexture, data.dilatedMaxZBufferHandle);
            }
        }

        private class GenerateMaxZMaskRGPassData
        {
            public ComputeShader generateMaxZCS;
            public int maxZKernel;
            public int maxZDownsampleKernel;
            public int dilateMaxZKernel;

            public Vector2Int intermediateMaskSize;
            public Vector2Int finalMaskSize;

            public TextureHandle maxZ8xBufferHandle;
            public TextureHandle maxZBufferHandle;
            public TextureHandle dilatedMaxZBufferHandle;

            public float dilationWidth;
            public int viewCount;
        }
#endif
    }
}
