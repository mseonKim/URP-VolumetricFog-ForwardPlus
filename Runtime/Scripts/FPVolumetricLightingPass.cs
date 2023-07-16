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
            public static int _VBufferAnisotropy = Shader.PropertyToID("_VBufferAnisotropy");
            public static int _VBufferVoxelSize = Shader.PropertyToID("_VBufferVoxelSize");
            public static int _VBufferViewportSize = Shader.PropertyToID("_VBufferViewportSize");
            public static int _VBufferSliceCount = Shader.PropertyToID("_VBufferSliceCount");
            public static int _VBufferRcpSliceCount = Shader.PropertyToID("_VBufferRcpSliceCount");
            public static int _VBufferUnitDepthTexelSpacing = Shader.PropertyToID("_VBufferUnitDepthTexelSpacing");
            public static int _VBufferDistanceEncodingParams = Shader.PropertyToID("_VBufferDistanceEncodingParams");
            public static int _VBufferDistanceDecodingParams = Shader.PropertyToID("_VBufferDistanceDecodingParams");
            public static int _VBufferSampleOffset = Shader.PropertyToID("_VBufferSampleOffset");
        }

        private VolumetricConfig m_Config;
        private ComputeShader m_VolumetricLightingCS;
        private ComputeShader m_VolumetricLightingFilteringCS;
        private Material m_ResolveMat;
        private RTHandle m_VBufferLightingHandle;
        private VBufferParameters m_VBufferParameters;

        private Vector2[] m_xySeq;
        private int m_FrameIndex;
        private static int s_VBufferLightingCSKernal;
        private static int s_VBufferFilteringCSKernal;
        private ProfilingSampler m_ProfilingSampler;

        public FPVolumetricLightingPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            m_xySeq = new Vector2[7];
        }

        public void Setup(VolumetricConfig config)
        {
            m_VolumetricLightingCS = config.volumetricLightingCS;
            m_VolumetricLightingFilteringCS = config.volumetricLightingFilteringCS;
            m_ResolveMat = config.resolveMat;
            m_Config = config;
            m_ProfilingSampler = new ProfilingSampler("Volumetric Lighting");
        }

        public void Dispose()
        {
            m_VBufferLightingHandle?.Release();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            m_VBufferParameters = VolumetricUtils.ComputeVolumetricBufferParameters(m_Config, renderingData.cameraData.camera);
            var vBufferViewportSize = m_VBufferParameters.viewportSize;

            var desc = new RenderTextureDescriptor(vBufferViewportSize.x, vBufferViewportSize.y, RenderTextureFormat.ARGBHalf, 0);
            desc.dimension = TextureDimension.Tex3D;
            desc.volumeDepth = vBufferViewportSize.z;
            desc.enableRandomWrite = true;
            RenderingUtils.ReAllocateIfNeeded(ref m_VBufferLightingHandle, desc, FilterMode.Trilinear, name:"_VBufferLighting");

            if (m_VolumetricLightingCS == null || m_VolumetricLightingFilteringCS == null)
                return;

            SetShaderVariables(cmd, renderingData.cameraData.camera);

            s_VBufferLightingCSKernal = m_VolumetricLightingCS.FindKernel("VolumetricLighting");
            m_VolumetricLightingCS.SetTexture(s_VBufferLightingCSKernal, IDs._VBufferLightingOutput, m_VBufferLightingHandle);

            s_VBufferFilteringCSKernal = m_VolumetricLightingFilteringCS.FindKernel("FilterVolumetricLighting");
            m_VolumetricLightingFilteringCS.SetTexture(s_VBufferFilteringCSKernal, IDs._VBufferLightingTexture, m_VBufferLightingHandle);


            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        private void SetShaderVariables(CommandBuffer cmd, Camera camera)
        {
            var vBufferViewportSize = m_VBufferParameters.viewportSize;
            var vFoV = camera.GetGateFittedFieldOfView() * Mathf.Deg2Rad;
            var unitDepthTexelSpacing = VolumetricUtils.ComputZPlaneTexelSpacing(1.0f, vFoV, vBufferViewportSize.y);

            VolumetricUtils.GetHexagonalClosePackedSpheres7(m_xySeq);
            int sampleIndex = m_FrameIndex % 7;
            var xySeqOffset = new Vector4();
            xySeqOffset.Set(m_xySeq[sampleIndex].x * m_Config.sampleOffsetWeight, m_xySeq[sampleIndex].y * m_Config.sampleOffsetWeight, VolumetricUtils.zSeq[sampleIndex], m_FrameIndex);

            cmd.SetGlobalFloat(IDs._VBufferAnisotropy, m_Config.anisotropy);
            cmd.SetGlobalFloat(IDs._VBufferVoxelSize, m_VBufferParameters.voxelSize);
            cmd.SetGlobalVector(IDs._VBufferViewportSize, new Vector4(vBufferViewportSize.x, vBufferViewportSize.y, 1.0f / vBufferViewportSize.x, 1.0f / vBufferViewportSize.y));
            cmd.SetGlobalInt(IDs._VBufferSliceCount, vBufferViewportSize.z);
            cmd.SetGlobalFloat(IDs._VBufferRcpSliceCount, 1f / vBufferViewportSize.z);
            cmd.SetGlobalFloat(IDs._VBufferUnitDepthTexelSpacing, unitDepthTexelSpacing);
            cmd.SetGlobalVector(IDs._VBufferDistanceEncodingParams, m_VBufferParameters.depthEncodingParams);
            cmd.SetGlobalVector(IDs._VBufferDistanceDecodingParams, m_VBufferParameters.depthDecodingParams);
            cmd.SetGlobalVector(IDs._VBufferSampleOffset, xySeqOffset);
            cmd.SetGlobalTexture(IDs._VBufferLightingTexture, m_VBufferLightingHandle);

            bool useReprojection = (m_Config.denoiseMode == DenoiseMode.Reprojection || m_Config.denoiseMode == DenoiseMode.Both);
            CoreUtils.SetKeyword(cmd, "ENABLE_REPROJECTION", useReprojection);
        }


        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (++m_FrameIndex >= 14)
            {
                m_FrameIndex = 0;
            }

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var voxelSize = m_VBufferParameters.voxelSize;
                if (m_VolumetricLightingCS != null
                    && m_VolumetricLightingFilteringCS != null
                    && Shader.GetGlobalTexture("_CameraDepthTexture") != null   // To prevent error log
                    && voxelSize > 1)
                {
                    int width = (int)((m_VBufferLightingHandle.rt.width + (voxelSize - 1)) / voxelSize);
                    int height = (int)((m_VBufferLightingHandle.rt.height + (voxelSize - 1)) / voxelSize);
                    m_VolumetricLightingCS.Dispatch(s_VBufferLightingCSKernal, width, height, 1);

                    if (m_Config.denoiseMode == DenoiseMode.Gaussian || m_Config.denoiseMode == DenoiseMode.Both)
                    {
                        m_VolumetricLightingFilteringCS.Dispatch(s_VBufferLightingCSKernal, width, height, m_VBufferParameters.viewportSize.z);                                
                    }
                }

                if (m_ResolveMat != null)
                {
                    if (m_Config.voxelMode == VoxelMode._PerPixel)
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

