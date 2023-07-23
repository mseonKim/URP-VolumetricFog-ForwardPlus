using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

namespace UniversalForwardPlusVolumetric
{
    public class FPVolumetricLightingPass : ScriptableRenderPass
    {
        private static int s_VolumeVoxelizationCSKernal;
        private static int s_VBufferLightingCSKernal;
        private static int s_VBufferFilteringCSKernal;

        private VolumetricConfig m_Config;
        private ComputeShader m_VolumeVoxelizationCS;
        private ComputeShader m_VolumetricLightingCS;
        private ComputeShader m_VolumetricLightingFilteringCS;
        private Material m_ResolveMat;
        private RTHandle m_VBufferDensityHandle;
        private RTHandle m_VBufferLightingHandle;
        private RTHandle m_VBufferLightingFilteredHandle;
        private RTHandle[] m_VolumetricHistoryBuffers;
        private VBufferParameters m_VBufferParameters;
        private Matrix4x4[] m_VBufferCoordToViewDirWS;
        private Matrix4x4 m_PixelCoordToViewDirWS;
        private Matrix4x4 m_PrevMatrixVP;

        private Vector2[] m_xySeq;
        private bool m_FilteringNeedsExtraBuffer;
        private bool m_HistoryBufferAllocated;
        private bool m_VBufferHistoryIsValid;
        private int m_FrameIndex;
        private Vector3 m_PrevCamPosRWS;
        private CameraType m_PrevEditorCameraType;
        private ProfilingSampler m_ProfilingSampler;

        public FPVolumetricLightingPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            m_xySeq = new Vector2[7];
            m_VBufferCoordToViewDirWS = new Matrix4x4[1]; // Currently xr not supported
            m_PrevMatrixVP = Matrix4x4.identity;
        }

        public void Setup(VolumetricConfig config, in VBufferParameters vBufferParameters)
        {
            m_VolumeVoxelizationCS = config.volumeVoxelizationCS;
            m_VolumetricLightingCS = config.volumetricLightingCS;
            m_VolumetricLightingFilteringCS = config.volumetricLightingFilteringCS;
            m_ResolveMat = config.resolveMat;
            m_Config = config;
            m_VBufferParameters = vBufferParameters;
            m_ProfilingSampler = new ProfilingSampler("Volumetric Lighting");
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public void Dispose()
        {
            m_VBufferDensityHandle?.Release();
            m_VBufferLightingHandle?.Release();
            m_VBufferLightingFilteredHandle?.Release();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (m_VolumeVoxelizationCS == null
                || m_VolumetricLightingCS == null
                || m_VolumetricLightingFilteringCS == null)
                return;


            var vBufferViewportSize = m_VBufferParameters.viewportSize;
            var camera = renderingData.cameraData.camera;

            // Create render texture
            var desc = new RenderTextureDescriptor(vBufferViewportSize.x, vBufferViewportSize.y, RenderTextureFormat.ARGBHalf, 0);
            desc.dimension = TextureDimension.Tex3D;
            desc.volumeDepth = vBufferViewportSize.z;
            desc.enableRandomWrite = true;
            RenderingUtils.ReAllocateIfNeeded(ref m_VBufferDensityHandle, desc, FilterMode.Point, name:"_VBufferDensity");
            RenderingUtils.ReAllocateIfNeeded(ref m_VBufferLightingHandle, desc, FilterMode.Point, name:"_VBufferLighting");

            m_FilteringNeedsExtraBuffer = !(SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.LoadStore));

            // Filtering
            if (m_Config.filterVolume && m_FilteringNeedsExtraBuffer)
            {
                RenderingUtils.ReAllocateIfNeeded(ref m_VBufferLightingFilteredHandle, desc, FilterMode.Point, name:"VBufferLightingFiltered");
                CoreUtils.SetKeyword(m_VolumetricLightingFilteringCS, "NEED_SEPARATE_OUTPUT", m_FilteringNeedsExtraBuffer);
            }

            // History buffer
            if (NeedHistoryBufferAllocation())
            {
                DestroyHistoryBuffers();
                if (m_Config.enableReprojection)
                {
                    CreateHistoryBuffers(camera);
                }
                m_HistoryBufferAllocated = m_Config.enableReprojection;
            }

            // Set shader variables
            SetFogShaderVariables(cmd, camera);
            SetVolumetricShaderVariables(cmd, renderingData.cameraData);

            s_VolumeVoxelizationCSKernal = m_VolumeVoxelizationCS.FindKernel("VolumeVoxelization");
            s_VBufferLightingCSKernal = m_VolumetricLightingCS.FindKernel("VolumetricLighting");
            s_VBufferFilteringCSKernal = m_VolumetricLightingFilteringCS.FindKernel("FilterVolumetricLighting");
        }

        private void SetFogShaderVariables(CommandBuffer cmd, Camera camera)
        {
            float extinction = 1.0f / m_Config.fogAttenuationDistance;
            Vector3 scattering = extinction * (Vector3)(Vector4)m_Config.albedo;
            float layerDepth = Mathf.Max(0.01f, m_Config.maximumHeight - m_Config.baseHeight);
            float H = VolumetricUtils.ScaleHeightFromLayerDepth(layerDepth);
            Vector2 heightFogExponents = new Vector2(1.0f / H, H);

            bool useSkyColor = m_Config.colorMode == FogColorMode.SkyColor;

            cmd.SetGlobalInt(IDs._FogEnabled, m_Config.enabled ? 1 : 0);
            cmd.SetGlobalInt(IDs._EnableVolumetricFog, m_Config.volumetricLighting ? 1 : 0);
            cmd.SetGlobalInt(IDs._FogColorMode, useSkyColor ? 1 : 0);
            cmd.SetGlobalInt(IDs._MaxEnvCubemapMip, VolumetricUtils.CalculateMaxEnvCubemapMip());
            cmd.SetGlobalVector(IDs._FogColor, useSkyColor ? m_Config.tint : m_Config.color);
            cmd.SetGlobalVector(IDs._MipFogParameters, new Vector4(m_Config.mipFogNear, m_Config.mipFogFar, m_Config.mipFogMaxMip, 0));
            cmd.SetGlobalVector(IDs._HeightFogParams, new Vector4(m_Config.baseHeight, extinction, heightFogExponents.x, heightFogExponents.y));
            cmd.SetGlobalVector(IDs._HeightFogBaseScattering, m_Config.volumetricLighting ? scattering : Vector4.one * extinction);
        }

        private void SetVolumetricShaderVariables(CommandBuffer cmd, CameraData cameraData)
        {
            var camera = cameraData.camera;
            var vBufferViewportSize = m_VBufferParameters.viewportSize;
            var vFoV = camera.GetGateFittedFieldOfView() * Mathf.Deg2Rad;
            var unitDepthTexelSpacing = VolumetricUtils.ComputZPlaneTexelSpacing(1.0f, vFoV, vBufferViewportSize.y);

            VolumetricUtils.GetHexagonalClosePackedSpheres7(m_xySeq);
            int sampleIndex = m_FrameIndex % 7;
            var xySeqOffset = new Vector4();
            xySeqOffset.Set(m_xySeq[sampleIndex].x * m_Config.sampleOffsetWeight, m_xySeq[sampleIndex].y * m_Config.sampleOffsetWeight, VolumetricUtils.zSeq[sampleIndex], m_FrameIndex);

            VolumetricUtils.GetPixelCoordToViewDirWS(cameraData, new Vector2(Screen.width, Screen.height), ref m_PixelCoordToViewDirWS);
            var viewportSize = new Vector4(vBufferViewportSize.x, vBufferViewportSize.y, 1.0f / vBufferViewportSize.x, 1.0f / vBufferViewportSize.y);
            VolumetricUtils.GetPixelCoordToViewDirWS(cameraData, viewportSize, ref m_VBufferCoordToViewDirWS);

            cmd.SetGlobalInt(IDs._VolumetricFilteringEnabled, m_Config.filterVolume ? 1 : 0);
            cmd.SetGlobalInt(IDs._VBufferHistoryIsValid, (m_Config.enableReprojection && m_VBufferHistoryIsValid) ? 1 : 0);
            cmd.SetGlobalInt(IDs._VBufferSliceCount, vBufferViewportSize.z);
            cmd.SetGlobalFloat(IDs._VBufferAnisotropy, m_Config.anisotropy);
            cmd.SetGlobalFloat(IDs._CornetteShanksConstant, VolumetricUtils.CornetteShanksPhasePartConstant(m_Config.anisotropy));
            cmd.SetGlobalFloat(IDs._VBufferVoxelSize, m_VBufferParameters.voxelSize);
            cmd.SetGlobalFloat(IDs._VBufferRcpSliceCount, 1f / vBufferViewportSize.z);
            cmd.SetGlobalFloat(IDs._VBufferUnitDepthTexelSpacing, unitDepthTexelSpacing);
            cmd.SetGlobalFloat(IDs._VBufferScatteringIntensity, m_Config.intensity);
            cmd.SetGlobalFloat(IDs._VBufferLastSliceDist, m_VBufferParameters.ComputeLastSliceDistance((uint)vBufferViewportSize.z));
            cmd.SetGlobalVector(IDs._VBufferViewportSize, viewportSize);
            cmd.SetGlobalVector(IDs._VBufferLightingViewportScale, m_VBufferParameters.ComputeViewportScale(vBufferViewportSize));
            cmd.SetGlobalVector(IDs._VBufferLightingViewportLimit, m_VBufferParameters.ComputeViewportLimit(vBufferViewportSize));
            cmd.SetGlobalVector(IDs._VBufferDistanceEncodingParams, m_VBufferParameters.depthEncodingParams);
            cmd.SetGlobalVector(IDs._VBufferDistanceDecodingParams, m_VBufferParameters.depthDecodingParams);
            cmd.SetGlobalVector(IDs._VBufferSampleOffset, xySeqOffset);
            cmd.SetGlobalVector(IDs._RTHandleScale, RTHandles.rtHandleProperties.rtHandleScale);
            cmd.SetGlobalTexture(IDs._VBufferLighting, m_VBufferLightingHandle);
            cmd.SetGlobalMatrix(IDs._VBufferCoordToViewDirWS, m_VBufferCoordToViewDirWS[0]);
            cmd.SetGlobalMatrix(IDs._PixelCoordToViewDirWS, m_PixelCoordToViewDirWS);

            CoreUtils.SetKeyword(m_VolumetricLightingCS, "ENABLE_REPROJECTION", m_Config.enableReprojection);
            CoreUtils.SetKeyword(m_VolumetricLightingCS, "ENABLE_ANISOTROPY", m_Config.anisotropy != 0f);
            CoreUtils.SetKeyword(m_VolumetricLightingCS, "SUPPORT_DIRECTIONAL_LIGHTS", m_Config.enableDirectionalLight);
            CoreUtils.SetKeyword(m_VolumetricLightingCS, "SUPPORT_LOCAL_LIGHTS", m_Config.enablePointAndSpotLight);
        }


        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            m_FrameIndex = (m_FrameIndex + 1) % 14;

            var camera = renderingData.cameraData.camera;
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var voxelSize = m_VBufferParameters.voxelSize;
                var vBufferViewportSize = m_VBufferParameters.viewportSize;

                // The shader defines GROUP_SIZE_1D = 8.
                int width = ((int)vBufferViewportSize.x + 7) / 8;
                int height = ((int)vBufferViewportSize.y + 7) / 8;

                // VBuffer Density
                if (m_VolumeVoxelizationCS != null)
                {
                    cmd.SetComputeTextureParam(m_VolumeVoxelizationCS, s_VolumeVoxelizationCSKernal, IDs._VBufferDensity, m_VBufferDensityHandle);
                    cmd.DispatchCompute(m_VolumeVoxelizationCS, s_VolumeVoxelizationCSKernal, width, height, 1);
                }

                // VBuffer Lighting
                if (m_VolumetricLightingCS != null
                    && m_VolumetricLightingFilteringCS != null
                    && Shader.GetGlobalTexture("_CameraDepthTexture") != null)   // To prevent error log
                {
                    cmd.SetComputeTextureParam(m_VolumetricLightingCS, s_VBufferLightingCSKernal, IDs._VBufferDensity, m_VBufferDensityHandle);
                    cmd.SetComputeTextureParam(m_VolumetricLightingCS, s_VBufferLightingCSKernal, IDs._VBufferLighting, m_VBufferLightingHandle);
                    if (m_Config.enableReprojection)
                    {
                        var currIdx = (m_FrameIndex + 0) & 1;
                        var prevIdx = (m_FrameIndex + 1) & 1;
                        cmd.SetComputeVectorParam(m_VolumetricLightingCS, IDs._PrevCamPosRWS, m_PrevCamPosRWS);
                        cmd.SetComputeMatrixParam(m_VolumetricLightingCS, IDs._PrevMatrixVP, m_PrevMatrixVP);
                        cmd.SetComputeTextureParam(m_VolumetricLightingCS, s_VBufferLightingCSKernal, IDs._VBufferFeedback, m_VolumetricHistoryBuffers[currIdx]);
                        cmd.SetComputeTextureParam(m_VolumetricLightingCS, s_VBufferLightingCSKernal, IDs._VBufferHistory, m_VolumetricHistoryBuffers[prevIdx]);
                    }
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
                    CoreUtils.DrawFullScreen(cmd, m_ResolveMat);
                }

            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            if (m_Config.enableReprojection && !m_VBufferHistoryIsValid)
            {
                m_VBufferHistoryIsValid = true;
            }

            // Set prev cam data
            m_PrevCamPosRWS = camera.transform.position;
            VolumetricUtils.SetCameraMatrices(renderingData.cameraData, out var v, out var p, out m_PrevMatrixVP, out var invvp);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
        }

        private void CreateHistoryBuffers(Camera camera)
        {
            if (!m_Config.volumetricLighting)
                return;
            
            Debug.Assert(m_VolumetricHistoryBuffers == null);

            m_VolumetricHistoryBuffers = new RTHandle[2];
            var viewportSize = m_VBufferParameters.viewportSize;

            for (int i = 0; i < 2; i++)
            {
                m_VolumetricHistoryBuffers[i] = RTHandles.Alloc(viewportSize.x, viewportSize.y, viewportSize.z, colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                    dimension: TextureDimension.Tex3D, enableRandomWrite: true, name: string.Format("VBufferHistory{0}", i));
            }

            m_VBufferHistoryIsValid = false;
        }

        private void DestroyHistoryBuffers()
        {
            if (m_VolumetricHistoryBuffers == null)
                return;

            for (int i = 0; i < 2; i++)
            {
                RTHandles.Release(m_VolumetricHistoryBuffers[i]);
            }

            m_VolumetricHistoryBuffers = null;
            m_VBufferHistoryIsValid = false;
        }

        private bool NeedHistoryBufferAllocation()
        {
            if (!m_Config.volumetricLighting || !m_Config.enableReprojection)
                return false;
            
            if (m_VolumetricHistoryBuffers == null)
                return true;

            if (m_HistoryBufferAllocated != m_Config.enableReprojection)
                return true;

            var viewportSize = m_VBufferParameters.viewportSize;
            if (m_VolumetricHistoryBuffers[0].rt.width != viewportSize.x ||
                m_VolumetricHistoryBuffers[0].rt.height != viewportSize.y ||
                m_VolumetricHistoryBuffers[0].rt.volumeDepth != viewportSize.z)
                return true;
            
            return false;
        }
    }
}

