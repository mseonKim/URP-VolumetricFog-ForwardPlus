using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UniversalForwardPlusVolumetric
{
    public class FPVolumetricLightingPass : ScriptableRenderPass
    {
        private class IDs
        {
            public static int _VBufferLightingTexture = Shader.PropertyToID("_VBufferLighting");
            public static int _VBufferLightingOutput = Shader.PropertyToID("_VBufferLightingOutput");
        }

        private int m_VoxelSize;
        private ComputeShader m_VolumetricLightingCS;
        private ComputeShader m_VolumetricLightingFilteringCS;
        private Material m_ResolveMat;
        private RTHandle m_VBufferLightingHandle;
        private int m_VBufferLightingCSKernal;
        private int m_VBufferFilteringCSKernal;
        private ProfilingSampler m_ProfilingSampler;

        public FPVolumetricLightingPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }

        public void Setup(VolumetricConfig config)
        {
            m_VolumetricLightingCS = config.volumetricLightingCS;
            m_VolumetricLightingFilteringCS = config.volumetricLightingFilteringCS;
            m_ResolveMat = config.resolveMat;
            m_VoxelSize = (int)config.voxelSize;
            m_ProfilingSampler = new ProfilingSampler("Volumetric Lighting");
        }

        public void Dispose()
        {
            m_VBufferLightingHandle?.Release();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = new RenderTextureDescriptor(Screen.width / m_VoxelSize, Screen.height / m_VoxelSize, RenderTextureFormat.ARGBHalf, 0);
            desc.dimension = TextureDimension.Tex3D;
            desc.volumeDepth = 64;
            desc.enableRandomWrite = true;
            RenderingUtils.ReAllocateIfNeeded(ref m_VBufferLightingHandle, desc, FilterMode.Trilinear, name:"_VBufferLighting");

            if (m_VolumetricLightingCS == null || m_VolumetricLightingFilteringCS == null)
                return;

            m_VBufferLightingCSKernal = m_VolumetricLightingCS.FindKernel("VolumetricLighting");
            m_VolumetricLightingCS.SetTexture(m_VBufferLightingCSKernal, IDs._VBufferLightingOutput, m_VBufferLightingHandle);

            m_VBufferFilteringCSKernal = m_VolumetricLightingFilteringCS.FindKernel("FilterVolumetricLighting");
            m_VolumetricLightingFilteringCS.SetTexture(m_VBufferFilteringCSKernal, IDs._VBufferLightingTexture, m_VBufferLightingHandle);

            cmd.SetGlobalTexture(IDs._VBufferLightingTexture, m_VBufferLightingHandle);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                if (m_VolumetricLightingCS != null && m_VolumetricLightingFilteringCS != null && m_VoxelSize > 1)
                {
                    m_VolumetricLightingCS.Dispatch(m_VBufferLightingCSKernal,
                                                    (m_VBufferLightingHandle.rt.width + (m_VoxelSize - 1)) / m_VoxelSize,
                                                    (m_VBufferLightingHandle.rt.height + (m_VoxelSize - 1)) / m_VoxelSize, 1);

                    // m_VolumetricLightingFilteringCS.Dispatch(m_VBufferLightingCSKernal,
                    //                                 (m_VBufferLightingHandle.rt.width + (m_VoxelSize - 1)) / m_VoxelSize,
                    //                                 (m_VBufferLightingHandle.rt.height + (m_VoxelSize - 1)) / m_VoxelSize,
                    //                                 64);                                

                }

                if (m_ResolveMat != null)
                {
                    if (m_VoxelSize == 1)
                    {
                        // Run per pixel pass
                        CoreUtils.DrawFullScreen(cmd, m_ResolveMat, null, 1);
                    }
                    else
                    {
                        CoreUtils.DrawFullScreen(cmd, m_ResolveMat);
                    }
                }

            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
        }
    }
}

