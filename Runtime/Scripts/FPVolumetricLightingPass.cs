using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
#if ENABLE_URP_VOLUEMTRIC_FOG_RENDERGRAPH
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
#endif

namespace UniversalForwardPlusVolumetric
{
    public class FPVolumetricLightingPass : ScriptableRenderPass
    {
        private class TAAData
        {
            public Matrix4x4 prevMatrixVP;
            public Vector2[] xySeq;
            public bool filteringNeedsExtraBuffer;
            public bool historyBufferAllocated;
            public bool vBufferHistoryIsValid;
            public int frameIndex;
            public Vector3 prevCamPosRWS;

            public TAAData()
            {
                xySeq = new Vector2[7];
                prevMatrixVP = Matrix4x4.identity;
            }
        }

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
        private LocalVolumetricFog[] m_LocalVolumes;
        private Matrix4x4[] m_VBufferCoordToViewDirWS;
        private Matrix4x4 m_PixelCoordToViewDirWS;

        private static TAAData s_TAAData;
        private ProfilingSampler m_ProfilingSampler;

        // CBuffers
        private ShaderVariablesFog m_FogCB = new ShaderVariablesFog();
        private ShaderVariablesVolumetricLighting m_VolumetricLightingCB = new ShaderVariablesVolumetricLighting();
        private ShaderVariablesLocalVolume m_LocalVolumeCB = new ShaderVariablesLocalVolume();


        public FPVolumetricLightingPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            m_VBufferCoordToViewDirWS = new Matrix4x4[1]; // Currently xr not supported
            s_TAAData = new TAAData();
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
            DestroyHistoryBuffers();
        }

#if UNITY_6000_0_OR_NEWER
        [Obsolete]
#endif
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

            s_TAAData.filteringNeedsExtraBuffer = !SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.LoadStore);

            // Filtering
            if (m_Config.filterVolume && s_TAAData.filteringNeedsExtraBuffer)
            {
                RenderingUtils.ReAllocateIfNeeded(ref m_VBufferLightingFilteredHandle, desc, FilterMode.Point, name:"VBufferLightingFiltered");
                CoreUtils.SetKeyword(m_VolumetricLightingFilteringCS, "NEED_SEPARATE_OUTPUT", s_TAAData.filteringNeedsExtraBuffer);
            }

            // History buffer
            if (NeedHistoryBufferAllocation(m_Config))
            {
                DestroyHistoryBuffers();
                if (m_Config.enableReprojection)
                {
                    CreateHistoryBuffers(m_Config);
                }
                s_TAAData.historyBufferAllocated = m_Config.enableReprojection;
            }

            // Set shader variables
            SetFogShaderVariables(cmd);
            SetVolumetricShaderVariables(cmd, renderingData.cameraData);

            s_VolumeVoxelizationCSKernal = m_VolumeVoxelizationCS.FindKernel("VolumeVoxelization");
            s_VBufferLightingCSKernal = m_VolumetricLightingCS.FindKernel("VolumetricLighting");
            s_VBufferFilteringCSKernal = m_VolumetricLightingFilteringCS.FindKernel("FilterVolumetricLighting");

            // Local Volumes
            LocalVolumetricFog.RefreshVolumes();
            m_LocalVolumes = LocalVolumetricFog.SortVolumes();
        }

        private void SetFogShaderVariables(CommandBuffer cmd)
        {
            float extinction = 1.0f / m_Config.fogAttenuationDistance;
            Vector3 scattering = extinction * (Vector3)(Vector4)m_Config.albedo;
            float layerDepth = Mathf.Max(0.01f, m_Config.maximumHeight - m_Config.baseHeight);
            float H = VolumetricUtils.ScaleHeightFromLayerDepth(layerDepth);
            Vector2 heightFogExponents = new Vector2(1.0f / H, H);

            bool useSkyColor = m_Config.colorMode == FogColorMode.SkyColor;

            m_FogCB._FogEnabled = m_Config.enabled ? 1u : 0u;
            m_FogCB._EnableVolumetricFog = m_Config.volumetricLighting ? 1u : 0u;
            m_FogCB._FogColorMode = useSkyColor ? 1u : 0u;
            m_FogCB._MaxEnvCubemapMip = (uint)VolumetricUtils.CalculateMaxEnvCubemapMip();
            m_FogCB._FogColor = useSkyColor ? m_Config.tint : m_Config.color;
            m_FogCB._MipFogParameters = new Vector4(m_Config.mipFogNear, m_Config.mipFogFar, m_Config.mipFogMaxMip, 0);
            m_FogCB._HeightFogParams = new Vector4(m_Config.baseHeight, extinction, heightFogExponents.x, heightFogExponents.y);
            m_FogCB._HeightFogBaseScattering = m_Config.volumetricLighting ? scattering : Vector4.one * extinction;

            ConstantBuffer.PushGlobal(cmd, m_FogCB, IDs._ShaderVariablesFog);
        }

        private void SetVolumetricShaderVariables(CommandBuffer cmd, CameraData cameraData)
        {
            var camera = cameraData.camera;
            var vBufferViewportSize = m_VBufferParameters.viewportSize;
            var vFoV = camera.GetGateFittedFieldOfView() * Mathf.Deg2Rad;
            var unitDepthTexelSpacing = VolumetricUtils.ComputZPlaneTexelSpacing(1.0f, vFoV, vBufferViewportSize.y);

            VolumetricUtils.GetHexagonalClosePackedSpheres7(s_TAAData.xySeq);
            int sampleIndex = s_TAAData.frameIndex % 7;
            var xySeqOffset = new Vector4();
            xySeqOffset.Set(s_TAAData.xySeq[sampleIndex].x * m_Config.sampleOffsetWeight, s_TAAData.xySeq[sampleIndex].y * m_Config.sampleOffsetWeight, VolumetricUtils.zSeq[sampleIndex], s_TAAData.frameIndex);

            VolumetricUtils.GetPixelCoordToViewDirWS(cameraData, new Vector4(Screen.width, Screen.height, 1f / Screen.width, 1f / Screen.height), ref m_PixelCoordToViewDirWS);
            var viewportSize = new Vector4(vBufferViewportSize.x, vBufferViewportSize.y, 1.0f / vBufferViewportSize.x, 1.0f / vBufferViewportSize.y);
            VolumetricUtils.GetPixelCoordToViewDirWS(cameraData, viewportSize, ref m_VBufferCoordToViewDirWS);


            m_VolumetricLightingCB._VolumetricFilteringEnabled = m_Config.filterVolume ? 1u : 0u;
            m_VolumetricLightingCB._VBufferHistoryIsValid = (m_Config.enableReprojection && s_TAAData.vBufferHistoryIsValid) ? 1u : 0u;
            m_VolumetricLightingCB._VBufferSliceCount = (uint)vBufferViewportSize.z;
            m_VolumetricLightingCB._VBufferAnisotropy = m_Config.anisotropy;
            m_VolumetricLightingCB._CornetteShanksConstant = VolumetricUtils.CornetteShanksPhasePartConstant(m_Config.anisotropy);
            m_VolumetricLightingCB._VBufferVoxelSize = m_VBufferParameters.voxelSize;
            m_VolumetricLightingCB._VBufferRcpSliceCount = 1f / vBufferViewportSize.z;
            m_VolumetricLightingCB._VBufferUnitDepthTexelSpacing = unitDepthTexelSpacing;
            m_VolumetricLightingCB._VBufferScatteringIntensity = m_Config.directionalScatteringIntensity;
            m_VolumetricLightingCB._VBufferLocalScatteringIntensity = m_Config.localScatteringIntensity;
            m_VolumetricLightingCB._VBufferLastSliceDist = m_VBufferParameters.ComputeLastSliceDistance((uint)vBufferViewportSize.z);
            m_VolumetricLightingCB._VBufferViewportSize = viewportSize;
            m_VolumetricLightingCB._VBufferLightingViewportScale = m_VBufferParameters.ComputeViewportScale(vBufferViewportSize);
            m_VolumetricLightingCB._VBufferLightingViewportLimit = m_VBufferParameters.ComputeViewportLimit(vBufferViewportSize);
            m_VolumetricLightingCB._VBufferDistanceEncodingParams = m_VBufferParameters.depthEncodingParams;
            m_VolumetricLightingCB._VBufferDistanceDecodingParams = m_VBufferParameters.depthDecodingParams;
            m_VolumetricLightingCB._VBufferSampleOffset = xySeqOffset;
        #if UNITY_EDITOR    // _RTHandleScale is different for scene & game view.
            m_VolumetricLightingCB._VLightingRTHandleScale = Vector4.one;
        #else
            m_VolumetricLightingCB._VLightingRTHandleScale = RTHandles.rtHandleProperties.rtHandleScale;
        #endif
            m_VolumetricLightingCB._VBufferCoordToViewDirWS = m_VBufferCoordToViewDirWS[0];

            ConstantBuffer.PushGlobal(cmd, m_VolumetricLightingCB, IDs._ShaderVariablesVolumetricLighting);

            cmd.SetGlobalTexture(IDs._VBufferLighting, m_VBufferLightingHandle);
            cmd.SetGlobalMatrix(IDs._PixelCoordToViewDirWS, m_PixelCoordToViewDirWS);

            CoreUtils.SetKeyword(m_VolumetricLightingCS, "ENABLE_REPROJECTION", m_Config.enableReprojection);
            CoreUtils.SetKeyword(m_VolumetricLightingCS, "ENABLE_ANISOTROPY", m_Config.anisotropy != 0f);
            CoreUtils.SetKeyword(m_VolumetricLightingCS, "SUPPORT_DIRECTIONAL_LIGHTS", m_Config.enableDirectionalLight);
            CoreUtils.SetKeyword(m_VolumetricLightingCS, "SUPPORT_LOCAL_LIGHTS", m_Config.enablePointAndSpotLight);
        }

        private static void UpdateVolumeShaderVariables(ref ShaderVariablesLocalVolume cb, LocalVolumetricFog volume)
        {
            var obb = volume.GetOBB();
            var engineData = volume.ConvertToEngineData();
            cb._VolumetricMaterialObbRight = obb.right;
            cb._VolumetricMaterialObbUp = obb.up;
            cb._VolumetricMaterialObbExtents = new Vector3(obb.extentX, obb.extentY, obb.extentZ);
            cb._VolumetricMaterialObbCenter = obb.center;
            
            cb._VolumetricMaterialAlbedo = engineData.albedo;
            cb._VolumetricMaterialExtinction = engineData.extinction;
            
            cb._VolumetricMaterialRcpPosFaceFade = engineData.rcpPosFaceFade;
            cb._VolumetricMaterialRcpNegFaceFade = engineData.rcpNegFaceFade;
            cb._VolumetricMaterialInvertFade = engineData.invertFade;

            cb._VolumetricMaterialRcpDistFadeLen = engineData.rcpDistFadeLen;
            cb._VolumetricMaterialEndTimesRcpDistFadeLen = engineData.endTimesRcpDistFadeLen;
            cb._VolumetricMaterialFalloffMode = (int)engineData.falloffMode;
        }

#if UNITY_6000_0_OR_NEWER
        [Obsolete]
#endif
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            s_TAAData.frameIndex = (s_TAAData.frameIndex + 1) % 14;

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

                // Local Volume material
                if (m_LocalVolumes != null && m_LocalVolumes.Length > 0)
                {
                    for (int i = 0; i < Math.Min(m_LocalVolumes.Length, 4); i++)
                    {
                        var shaderSetting = m_LocalVolumes[i].volumeShaderSetting;
                        bool isShaderValid = shaderSetting.shader != null;
                        var cs = isShaderValid ? shaderSetting.shader : m_Config.defaultLocalVolumeShader;

                        if (cs != null)
                        {
                            // Compute density
                            var kernel = isShaderValid ? cs.FindKernel(shaderSetting.kernelName) : cs.FindKernel("SmokeVolumeMaterial");
                            UpdateVolumeShaderVariables(ref m_LocalVolumeCB, m_LocalVolumes[i]);
                            // TODO: Set properties to shader from child local volumetric fog setting
                            m_LocalVolumes[i].SetComputeShaderProperties(cmd, cs, kernel);
                            cmd.SetComputeTextureParam(cs, kernel, IDs._VBufferDensity, m_VBufferDensityHandle);
                            ConstantBuffer.Push(cmd, m_LocalVolumeCB, cs, IDs._ShaderVariablesLocalVolume);
                            cmd.DispatchCompute(cs, kernel, width, height, 1);
                        }
                    }
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
                        var currIdx = (s_TAAData.frameIndex + 0) & 1;
                        var prevIdx = (s_TAAData.frameIndex + 1) & 1;
                        cmd.SetComputeVectorParam(m_VolumetricLightingCS, IDs._PrevCamPosRWS, s_TAAData.prevCamPosRWS);
                        cmd.SetComputeMatrixParam(m_VolumetricLightingCS, IDs._PrevMatrixVP, s_TAAData.prevMatrixVP);
                        cmd.SetComputeTextureParam(m_VolumetricLightingCS, s_VBufferLightingCSKernal, IDs._VBufferFeedback, m_VolumetricHistoryBuffers[currIdx]);
                        cmd.SetComputeTextureParam(m_VolumetricLightingCS, s_VBufferLightingCSKernal, IDs._VBufferHistory, m_VolumetricHistoryBuffers[prevIdx]);
                    }
                    cmd.DispatchCompute(m_VolumetricLightingCS, s_VBufferLightingCSKernal, width, height, 1);

                    if (m_Config.filterVolume)
                    {
                        cmd.SetComputeTextureParam(m_VolumetricLightingFilteringCS, s_VBufferFilteringCSKernal, IDs._VBufferLighting, m_VBufferLightingHandle);
                        if (s_TAAData.filteringNeedsExtraBuffer)
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

            if (m_Config.enableReprojection && !s_TAAData.vBufferHistoryIsValid)
            {
                s_TAAData.vBufferHistoryIsValid = true;
            }

            // Set prev cam data
            s_TAAData.prevCamPosRWS = camera.transform.position;
            VolumetricUtils.SetCameraMatrices(renderingData.cameraData, out var v, out var p, out s_TAAData.prevMatrixVP, out var invvp);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
        }

        private void CreateHistoryBuffers(VolumetricConfig config)
        {
            if (!config.volumetricLighting)
                return;
            
            Debug.Assert(m_VolumetricHistoryBuffers == null);

            m_VolumetricHistoryBuffers = new RTHandle[2];
            var viewportSize = m_VBufferParameters.viewportSize;

            for (int i = 0; i < 2; i++)
            {
                m_VolumetricHistoryBuffers[i] = RTHandles.Alloc(viewportSize.x, viewportSize.y, viewportSize.z, colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                    dimension: TextureDimension.Tex3D, enableRandomWrite: true, name: string.Format("VBufferHistory{0}", i));
            }

            s_TAAData.vBufferHistoryIsValid = false;
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
            s_TAAData.vBufferHistoryIsValid = false;
        }

        private bool NeedHistoryBufferAllocation(VolumetricConfig config)
        {
            if (!config.volumetricLighting || !config.enableReprojection)
                return false;
            
            if (m_VolumetricHistoryBuffers == null)
                return true;

            if (s_TAAData.historyBufferAllocated != config.enableReprojection)
                return true;

            var viewportSize = m_VBufferParameters.viewportSize;
            if (m_VolumetricHistoryBuffers[0].rt.width != viewportSize.x ||
                m_VolumetricHistoryBuffers[0].rt.height != viewportSize.y ||
                m_VolumetricHistoryBuffers[0].rt.volumeDepth != viewportSize.z)
                return true;
            
            return false;
        }
        

#if ENABLE_URP_VOLUEMTRIC_FOG_RENDERGRAPH
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
        {
            if (m_VolumeVoxelizationCS == null
                || m_VolumetricLightingCS == null
                || m_VolumetricLightingFilteringCS == null)
                return;
            
            var blitParameters = new RenderGraphUtils.BlitMaterialParameters();

            using (var builder = renderGraph.AddComputePass<RenderGraphPassData>("Volumetric Lighting", out var passData))
            {
                passData.config = m_Config;
                passData.vBufferParameters = m_VBufferParameters;
                passData.volumeVoxelizationCS = m_VolumeVoxelizationCS;
                passData.volumetricLightingCS = m_VolumetricLightingCS;
                passData.volumetricLightingFilteringCS = m_VolumetricLightingFilteringCS;

                // Get the data needed to create the list of objects to draw
                UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();
                UniversalResourceData resourceData = frameContext.Get<UniversalResourceData>();
                passData.cameraData = cameraData;
                
                builder.AllowGlobalStateModification(true);
                
                // Setup
                var config = passData.config;
                var vBufferViewportSize = passData.vBufferParameters.viewportSize;

                // Create render texture
                var desc = new RenderTextureDescriptor(vBufferViewportSize.x, vBufferViewportSize.y, RenderTextureFormat.ARGBHalf, 0);
                desc.dimension = TextureDimension.Tex3D;
                desc.volumeDepth = vBufferViewportSize.z;
                desc.enableRandomWrite = true;
                var vBufferDensityHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_VBufferDensity", false);
                var vBufferLightingHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_VBufferLighting", false);

                builder.UseTexture(vBufferDensityHandle, AccessFlags.ReadWrite);
                builder.UseTexture(vBufferLightingHandle, AccessFlags.ReadWrite);
                passData.vBufferDensityHandle = vBufferDensityHandle;
                passData.vBufferLightingHandle = vBufferLightingHandle;
                s_TAAData.filteringNeedsExtraBuffer = !SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormatUsage.LoadStore);

                // Filtering
                if (config.filterVolume && s_TAAData.filteringNeedsExtraBuffer)
                {
                    var vBufferLightingFilteredHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "VBufferLightingFiltered", false);
                    builder.UseTexture(vBufferLightingFilteredHandle, AccessFlags.ReadWrite);
                    CoreUtils.SetKeyword(passData.volumetricLightingFilteringCS, "NEED_SEPARATE_OUTPUT", s_TAAData.filteringNeedsExtraBuffer);
                    passData.vBufferLightingFilteredHandle = vBufferLightingFilteredHandle;
                }

                // History buffer
                if (NeedHistoryBufferAllocation(config))
                {
                    DestroyHistoryBuffers();
                    if (config.enableReprojection)
                    {
                        CreateHistoryBuffers(config);
                    }
                    s_TAAData.historyBufferAllocated = config.enableReprojection;
                }

                if (m_VolumetricHistoryBuffers != null)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        var texture = renderGraph.ImportTexture(m_VolumetricHistoryBuffers[i]);
                        passData.volumetricHistoryBuffers[i] = texture;
                        builder.UseTexture(texture);
                    }
                }

                // Set shader variables
                SetFogShaderVariables(passData);
                SetVolumetricShaderVariables(cameraData, passData);

                s_VolumeVoxelizationCSKernal = passData.volumeVoxelizationCS.FindKernel("VolumeVoxelization");
                s_VBufferLightingCSKernal = passData.volumetricLightingCS.FindKernel("VolumetricLighting");
                s_VBufferFilteringCSKernal = passData.volumetricLightingFilteringCS.FindKernel("FilterVolumetricLighting");

                // Local Volumes
                LocalVolumetricFog.RefreshVolumes();
                passData.localVolumes = LocalVolumetricFog.SortVolumes();

                builder.SetRenderFunc((RenderGraphPassData data, ComputeGraphContext context) => ExecutePass(context.cmd, data));
                
                // Use member pass data to transfer blit parameters.
                blitParameters.source = resourceData.cameraColor;
                blitParameters.destination = resourceData.cameraColor;
                blitParameters.material = m_ResolveMat;
            }
            
            using (var builder = renderGraph.AddRasterRenderPass<RenderGraphPassData>("Volumetric Lighting Resolve", out var passData))
            {
                // Resolve
                // builder.UseTexture(blitParameters.source, AccessFlags.Read);
                builder.SetRenderAttachment(blitParameters.destination, 0);
                builder.SetRenderFunc((RenderGraphPassData data, RasterGraphContext context) =>
                {
                    if (blitParameters.material != null)
                    {
                        Blitter.BlitTexture(context.cmd, blitParameters.source, Vector2.one, blitParameters.material, 0);
                    }
                });
            }
        }

        private static void ExecutePass(ComputeCommandBuffer cmd, RenderGraphPassData data)
        {
            s_TAAData.frameIndex = (s_TAAData.frameIndex + 1) % 14;

            var config = data.config;
            var camera = data.cameraData.camera;
            var voxelSize = data.vBufferParameters.voxelSize;
            var vBufferViewportSize = data.vBufferParameters.viewportSize;

            // The shader defines GROUP_SIZE_1D = 8.
            int width = ((int)vBufferViewportSize.x + 7) / 8;
            int height = ((int)vBufferViewportSize.y + 7) / 8;

            // VBuffer Density
            if (data.volumeVoxelizationCS != null)
            {
                cmd.SetComputeTextureParam(data.volumeVoxelizationCS, s_VolumeVoxelizationCSKernal, IDs._VBufferDensity, data.vBufferDensityHandle);
                cmd.DispatchCompute(data.volumeVoxelizationCS, s_VolumeVoxelizationCSKernal, width, height, 1);
            }
            
            // Set global shader variables
            cmd.SetGlobalTexture(IDs._VBufferLighting, data.vBufferLightingHandle);
            cmd.SetGlobalMatrix(IDs._PixelCoordToViewDirWS, data.pixelCoordToViewDirWS);

            // Local Volume material
            var localVolumes = data.localVolumes;
            if (localVolumes != null && localVolumes.Length > 0)
            {
                for (int i = 0; i < Math.Min(localVolumes.Length, 4); i++)
                {
                    var shaderSetting = localVolumes[i].volumeShaderSetting;
                    bool isShaderValid = shaderSetting.shader != null;
                    var cs = isShaderValid ? shaderSetting.shader : config.defaultLocalVolumeShader;

                    if (cs != null)
                    {
                        // Compute density
                        var kernel = isShaderValid ? cs.FindKernel(shaderSetting.kernelName) : cs.FindKernel("SmokeVolumeMaterial");
                        UpdateVolumeShaderVariables(ref data.localVolumeCB, localVolumes[i]);
                        // TODO: Set properties to shader from child local volumetric fog setting
                        localVolumes[i].SetComputeShaderProperties(cmd, cs, kernel);
                        cmd.SetComputeTextureParam(cs, kernel, IDs._VBufferDensity, data.vBufferDensityHandle);
                        ConstantBuffer.Push(data.localVolumeCB, cs, IDs._ShaderVariablesLocalVolume);
                        cmd.DispatchCompute(cs, kernel, width, height, 1);
                    }
                }
            }

            // VBuffer Lighting
            var volumetricLightingCS = data.volumetricLightingCS;
            var volumetricLightingFilteringCS = data.volumetricLightingFilteringCS;
            if (volumetricLightingCS != null
                && volumetricLightingFilteringCS != null
                && Shader.GetGlobalTexture("_CameraDepthTexture") != null)   // To prevent error log
            {
                cmd.SetComputeTextureParam(volumetricLightingCS, s_VBufferLightingCSKernal, IDs._VBufferDensity, data.vBufferDensityHandle);
                cmd.SetComputeTextureParam(volumetricLightingCS, s_VBufferLightingCSKernal, IDs._VBufferLighting, data.vBufferLightingHandle);
                if (config.enableReprojection)
                {
                    var currIdx = (s_TAAData.frameIndex + 0) & 1;
                    var prevIdx = (s_TAAData.frameIndex + 1) & 1;
                    cmd.SetComputeVectorParam(volumetricLightingCS, IDs._PrevCamPosRWS, s_TAAData.prevCamPosRWS);
                    cmd.SetComputeMatrixParam(volumetricLightingCS, IDs._PrevMatrixVP, s_TAAData.prevMatrixVP);
                    cmd.SetComputeTextureParam(volumetricLightingCS, s_VBufferLightingCSKernal, IDs._VBufferFeedback, data.volumetricHistoryBuffers[currIdx]);
                    cmd.SetComputeTextureParam(volumetricLightingCS, s_VBufferLightingCSKernal, IDs._VBufferHistory, data.volumetricHistoryBuffers[prevIdx]);
                }
                cmd.DispatchCompute(volumetricLightingCS, s_VBufferLightingCSKernal, width, height, 1);

                if (config.filterVolume)
                {
                    cmd.SetComputeTextureParam(volumetricLightingFilteringCS, s_VBufferFilteringCSKernal, IDs._VBufferLighting, data.vBufferLightingHandle);
                    if (s_TAAData.filteringNeedsExtraBuffer)
                    {
                        cmd.SetComputeTextureParam(volumetricLightingFilteringCS, s_VBufferFilteringCSKernal, IDs._VBufferLightingFiltered, data.vBufferLightingFilteredHandle);
                    }
                    cmd.DispatchCompute(volumetricLightingFilteringCS, s_VBufferLightingCSKernal,
                                        VolumetricUtils.DivRoundUp((int)vBufferViewportSize.x, 8),
                                        VolumetricUtils.DivRoundUp((int)vBufferViewportSize.y, 8),
                                        data.vBufferParameters.viewportSize.z);
                }
            }

            if (config.enableReprojection && !s_TAAData.vBufferHistoryIsValid)
            {
                s_TAAData.vBufferHistoryIsValid = true;
            }

            // Set prev cam data
            s_TAAData.prevCamPosRWS = camera.transform.position;
            VolumetricUtils.SetCameraMatrices(data.cameraData, out var v, out var p, out s_TAAData.prevMatrixVP, out var invvp);
        }

        private void SetFogShaderVariables(RenderGraphPassData passData)
        {
            var config = passData.config;

            float extinction = 1.0f / config.fogAttenuationDistance;
            Vector3 scattering = extinction * (Vector3)(Vector4)config.albedo;
            float layerDepth = Mathf.Max(0.01f, config.maximumHeight - config.baseHeight);
            float H = VolumetricUtils.ScaleHeightFromLayerDepth(layerDepth);
            Vector2 heightFogExponents = new Vector2(1.0f / H, H);

            bool useSkyColor = config.colorMode == FogColorMode.SkyColor;

            passData.fogCB._FogEnabled = config.enabled ? 1u : 0u;
            passData.fogCB._EnableVolumetricFog = config.volumetricLighting ? 1u : 0u;
            passData.fogCB._FogColorMode = useSkyColor ? 1u : 0u;
            passData.fogCB._MaxEnvCubemapMip = (uint)VolumetricUtils.CalculateMaxEnvCubemapMip();
            passData.fogCB._FogColor = useSkyColor ? config.tint : config.color;
            passData.fogCB._MipFogParameters = new Vector4(config.mipFogNear, config.mipFogFar, config.mipFogMaxMip, 0);
            passData.fogCB._HeightFogParams = new Vector4(config.baseHeight, extinction, heightFogExponents.x, heightFogExponents.y);
            passData.fogCB._HeightFogBaseScattering = config.volumetricLighting ? scattering : Vector4.one * extinction;

            ConstantBuffer.PushGlobal(passData.fogCB, IDs._ShaderVariablesFog);
        }

        private void SetVolumetricShaderVariables(UniversalCameraData cameraData, RenderGraphPassData passData)
        {
            var config = passData.config;
            var vBufferParameters = passData.vBufferParameters;
            var camera = cameraData.camera;
            var vBufferViewportSize = vBufferParameters.viewportSize;
            var vFoV = camera.GetGateFittedFieldOfView() * Mathf.Deg2Rad;
            var unitDepthTexelSpacing = VolumetricUtils.ComputZPlaneTexelSpacing(1.0f, vFoV, vBufferViewportSize.y);

            VolumetricUtils.GetHexagonalClosePackedSpheres7(s_TAAData.xySeq);
            int sampleIndex = s_TAAData.frameIndex % 7;
            var xySeqOffset = new Vector4();
            xySeqOffset.Set(s_TAAData.xySeq[sampleIndex].x * config.sampleOffsetWeight, s_TAAData.xySeq[sampleIndex].y * config.sampleOffsetWeight, VolumetricUtils.zSeq[sampleIndex], s_TAAData.frameIndex);

            VolumetricUtils.GetPixelCoordToViewDirWS(cameraData, new Vector4(Screen.width, Screen.height, 1f / Screen.width, 1f / Screen.height), ref passData.pixelCoordToViewDirWS);
            var viewportSize = new Vector4(vBufferViewportSize.x, vBufferViewportSize.y, 1.0f / vBufferViewportSize.x, 1.0f / vBufferViewportSize.y);
            VolumetricUtils.GetPixelCoordToViewDirWS(cameraData, viewportSize, ref passData.vBufferCoordToViewDirWS);

            passData.volumetricLightingCB._VolumetricFilteringEnabled = config.filterVolume ? 1u : 0u;
            passData.volumetricLightingCB._VBufferHistoryIsValid = (config.enableReprojection && s_TAAData.vBufferHistoryIsValid) ? 1u : 0u;
            passData.volumetricLightingCB._VBufferSliceCount = (uint)vBufferViewportSize.z;
            passData.volumetricLightingCB._VBufferAnisotropy = config.anisotropy;
            passData.volumetricLightingCB._CornetteShanksConstant = VolumetricUtils.CornetteShanksPhasePartConstant(config.anisotropy);
            passData.volumetricLightingCB._VBufferVoxelSize = vBufferParameters.voxelSize;
            passData.volumetricLightingCB._VBufferRcpSliceCount = 1f / vBufferViewportSize.z;
            passData.volumetricLightingCB._VBufferUnitDepthTexelSpacing = unitDepthTexelSpacing;
            passData.volumetricLightingCB._VBufferScatteringIntensity = config.directionalScatteringIntensity;
            passData.volumetricLightingCB._VBufferLocalScatteringIntensity = config.localScatteringIntensity;
            passData.volumetricLightingCB._VBufferLastSliceDist = vBufferParameters.ComputeLastSliceDistance((uint)vBufferViewportSize.z);
            passData.volumetricLightingCB._VBufferViewportSize = viewportSize;
            passData.volumetricLightingCB._VBufferLightingViewportScale = vBufferParameters.ComputeViewportScale(vBufferViewportSize);
            passData.volumetricLightingCB._VBufferLightingViewportLimit = vBufferParameters.ComputeViewportLimit(vBufferViewportSize);
            passData.volumetricLightingCB._VBufferDistanceEncodingParams = vBufferParameters.depthEncodingParams;
            passData.volumetricLightingCB._VBufferDistanceDecodingParams = vBufferParameters.depthDecodingParams;
            passData.volumetricLightingCB._VBufferSampleOffset = xySeqOffset;
        #if UNITY_EDITOR    // _RTHandleScale is different for scene & game view.
            passData.volumetricLightingCB._VLightingRTHandleScale = Vector4.one;
        #else
            passData.volumetricLightingCB._VLightingRTHandleScale = RTHandles.rtHandleProperties.rtHandleScale;
        #endif
            passData.volumetricLightingCB._VBufferCoordToViewDirWS = passData.vBufferCoordToViewDirWS[0];

            ConstantBuffer.PushGlobal(passData.volumetricLightingCB, IDs._ShaderVariablesVolumetricLighting);
            
            CoreUtils.SetKeyword(passData.volumetricLightingCS, "ENABLE_REPROJECTION", config.enableReprojection);
            CoreUtils.SetKeyword(passData.volumetricLightingCS, "ENABLE_ANISOTROPY", config.anisotropy != 0f);
            CoreUtils.SetKeyword(passData.volumetricLightingCS, "SUPPORT_DIRECTIONAL_LIGHTS", config.enableDirectionalLight);
            CoreUtils.SetKeyword(passData.volumetricLightingCS, "SUPPORT_LOCAL_LIGHTS", config.enablePointAndSpotLight);
        }

        // Array variables should be initialized every frame since passData keeps previous data.
        private class RenderGraphPassData
        {
            public VolumetricConfig config;
            public ComputeShader volumeVoxelizationCS;
            public ComputeShader volumetricLightingCS;
            public ComputeShader volumetricLightingFilteringCS;
            public TextureHandle vBufferDensityHandle;
            public TextureHandle vBufferLightingHandle;
            public TextureHandle vBufferLightingFilteredHandle;
            public TextureHandle[] volumetricHistoryBuffers;
            public VBufferParameters vBufferParameters;
            public LocalVolumetricFog[] localVolumes;
            public Matrix4x4[] vBufferCoordToViewDirWS;
            public Matrix4x4 pixelCoordToViewDirWS;
            
            public UniversalCameraData cameraData;

            public ShaderVariablesFog fogCB = new ShaderVariablesFog();
            public ShaderVariablesVolumetricLighting volumetricLightingCB = new ShaderVariablesVolumetricLighting();
            public ShaderVariablesLocalVolume localVolumeCB = new ShaderVariablesLocalVolume();
            
            public RenderGraphPassData()
            {
                vBufferCoordToViewDirWS = new Matrix4x4[1];
                volumetricHistoryBuffers = new TextureHandle[2];
            }
        }
#endif
    }
}

