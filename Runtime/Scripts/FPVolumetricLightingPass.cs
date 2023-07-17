using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

namespace UniversalForwardPlusVolumetric
{
    public class FPVolumetricLightingPass : ScriptableRenderPass
    {
        private static class IDs
        {
            public static int _VBufferLighting = Shader.PropertyToID("_VBufferLighting");
            public static int _VBufferLightingOutput = Shader.PropertyToID("_VBufferLightingOutput");
            public static int _VBufferLightingFiltered = Shader.PropertyToID("_VBufferLightingFiltered");
            public static int _VBufferAnisotropy = Shader.PropertyToID("_VBufferAnisotropy");
            public static int _VBufferVoxelSize = Shader.PropertyToID("_VBufferVoxelSize");
            public static int _VBufferViewportSize = Shader.PropertyToID("_VBufferViewportSize");
            public static int _VBufferSliceCount = Shader.PropertyToID("_VBufferSliceCount");
            public static int _VBufferRcpSliceCount = Shader.PropertyToID("_VBufferRcpSliceCount");
            public static int _VBufferLightingViewportScale = Shader.PropertyToID("_VBufferLightingViewportScale");
            public static int _VBufferLightingViewportLimit = Shader.PropertyToID("_VBufferLightingViewportLimit");
            public static int _VBufferUnitDepthTexelSpacing = Shader.PropertyToID("_VBufferUnitDepthTexelSpacing");
            public static int _VBufferDistanceEncodingParams = Shader.PropertyToID("_VBufferDistanceEncodingParams");
            public static int _VBufferDistanceDecodingParams = Shader.PropertyToID("_VBufferDistanceDecodingParams");
            public static int _VBufferSampleOffset = Shader.PropertyToID("_VBufferSampleOffset");
            public static int _RTHandleScale = Shader.PropertyToID("_RTHandleScale");
        }
        private static int s_VBufferLightingCSKernal;
        private static int s_VBufferFilteringCSKernal;

        private ComputeShader m_VolumetricLightingCS;
        private VolumetricConfig m_Config;
        private ComputeShader m_VolumetricLightingFilteringCS;
        private Material m_ResolveMat;
        private RTHandle m_VBufferLightingHandle;
        private RTHandle m_VBufferLightingFilteredHandle;
        private VBufferParameters m_VBufferParameters;

        private Vector2[] m_xySeq;
        private bool m_FilteringNeedsExtraBuffer;
        private int m_FrameIndex;
        private ProfilingSampler m_ProfilingSampler;

        public FPVolumetricLightingPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            m_xySeq = new Vector2[7];
        }

        public void Setup(VolumetricConfig config, in VBufferParameters vBufferParameters)
        {
            m_VolumetricLightingCS = config.volumetricLightingCS;
            m_VolumetricLightingFilteringCS = config.volumetricLightingFilteringCS;
            m_ResolveMat = config.resolveMat;
            m_Config = config;
            m_VBufferParameters = vBufferParameters;
            m_ProfilingSampler = new ProfilingSampler("Volumetric Lighting");
        }

        public void Dispose()
        {
            m_VBufferLightingHandle?.Release();
            m_VBufferLightingFilteredHandle?.Release();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (m_VolumetricLightingCS == null || m_VolumetricLightingFilteringCS == null)
                return;

            ConfigureInput(ScriptableRenderPassInput.Depth);

            var vBufferViewportSize = m_VBufferParameters.viewportSize;

            // Create render texture
            var desc = new RenderTextureDescriptor(vBufferViewportSize.x, vBufferViewportSize.y, RenderTextureFormat.ARGBHalf, 0);
            desc.dimension = TextureDimension.Tex3D;
            desc.volumeDepth = vBufferViewportSize.z;
            desc.enableRandomWrite = true;
            RenderingUtils.ReAllocateIfNeeded(ref m_VBufferLightingHandle, desc, FilterMode.Trilinear, name:"_VBufferLighting");

            m_FilteringNeedsExtraBuffer = !(SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.LoadStore));

            if (m_Config.filterVolume && m_FilteringNeedsExtraBuffer)
            {
                RenderingUtils.ReAllocateIfNeeded(ref m_VBufferLightingFilteredHandle, desc, FilterMode.Trilinear, name:"VBufferLightingFiltered");
                CoreUtils.SetKeyword(m_VolumetricLightingFilteringCS, "NEED_SEPARATE_OUTPUT", m_FilteringNeedsExtraBuffer);
            }

            // Set shader variables
            SetShaderVariables(cmd, renderingData.cameraData.camera);

            s_VBufferLightingCSKernal = m_VolumetricLightingCS.FindKernel("VolumetricLighting");
            s_VBufferFilteringCSKernal = m_VolumetricLightingFilteringCS.FindKernel("FilterVolumetricLighting");
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
            cmd.SetGlobalVector(IDs._VBufferLightingViewportScale, m_VBufferParameters.ComputeViewportScale(vBufferViewportSize));
            cmd.SetGlobalVector(IDs._VBufferLightingViewportLimit, m_VBufferParameters.ComputeViewportLimit(vBufferViewportSize));
            cmd.SetGlobalFloat(IDs._VBufferRcpSliceCount, 1f / vBufferViewportSize.z);
            cmd.SetGlobalFloat(IDs._VBufferUnitDepthTexelSpacing, unitDepthTexelSpacing);
            cmd.SetGlobalVector(IDs._VBufferDistanceEncodingParams, m_VBufferParameters.depthEncodingParams);
            cmd.SetGlobalVector(IDs._VBufferDistanceDecodingParams, m_VBufferParameters.depthDecodingParams);
            cmd.SetGlobalVector(IDs._VBufferSampleOffset, xySeqOffset);
            cmd.SetGlobalTexture(IDs._VBufferLighting, m_VBufferLightingHandle);
            cmd.SetGlobalVector(IDs._RTHandleScale, RTHandles.rtHandleProperties.rtHandleScale);

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
                var vBufferViewportSize = m_VBufferParameters.viewportSize;

                if (m_VolumetricLightingCS != null
                    && m_VolumetricLightingFilteringCS != null
                    && Shader.GetGlobalTexture("_CameraDepthTexture") != null   // To prevent error log
                    // && Shader.GetGlobalTexture("_MaxZMaskTexture") != null
                    )
                {
                    // The shader defines GROUP_SIZE_1D = 8.
                    int width = ((int)vBufferViewportSize.x + 7) / 8;
                    int height = ((int)vBufferViewportSize.y + 7) / 8;
                    cmd.SetComputeTextureParam(m_VolumetricLightingCS, s_VBufferLightingCSKernal, IDs._VBufferLightingOutput, m_VBufferLightingHandle);
                    cmd.DispatchCompute(m_VolumetricLightingCS, s_VBufferLightingCSKernal, width, height, 1);

                    if (m_Config.filterVolume)
                    {
                        cmd.SetComputeTextureParam(m_VolumetricLightingFilteringCS, s_VBufferFilteringCSKernal, IDs._VBufferLighting, m_VBufferLightingHandle);
                        if (m_FilteringNeedsExtraBuffer)
                        {
                            cmd.SetComputeTextureParam(m_VolumetricLightingFilteringCS, s_VBufferFilteringCSKernal, IDs._VBufferLightingFiltered, m_VBufferLightingFilteredHandle);
                        }
                        cmd.DispatchCompute(m_VolumetricLightingFilteringCS, s_VBufferLightingCSKernal,
                                            VolumetricUtils.DivRoundUp((int)vBufferViewportSize.x, 8),
                                            VolumetricUtils.DivRoundUp((int)vBufferViewportSize.y, 8),
                                            m_VBufferParameters.viewportSize.z);
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

