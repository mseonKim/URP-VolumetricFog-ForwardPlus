// Ported from HDRP Volumetric fog
// Limitation: Only works for URP Forward+

using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UniversalForwardPlusVolumetric
{
    public class FPVolumetricFog : ScriptableRendererFeature
    {
        public VolumetricConfig config;
        private GenerateMaxZPass m_GenerateMaxZPass;
        private FPVolumetricLightingPass m_VolumetricLightingPass;
        private VBufferParameters m_VBufferParameters;

        public override void Create()
        {
            m_GenerateMaxZPass = new GenerateMaxZPass();
            m_VolumetricLightingPass = new FPVolumetricLightingPass();
        }
        
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (config == null)
                return;

            var cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Reflection)
                return;

            if (!config.volumetricLighting)
                return;

#if UNITY_EDITOR
            if (cameraType != CameraType.SceneView && cameraType != CameraType.Game)
            {
                return;
            }
#endif
            m_VBufferParameters = VolumetricUtils.ComputeVolumetricBufferParameters(config, renderingData.cameraData.camera, renderingData.cameraData.renderScale);

            m_GenerateMaxZPass.Setup(config, m_VBufferParameters);
            renderer.EnqueuePass(m_GenerateMaxZPass);
            m_VolumetricLightingPass.Setup(config, m_VBufferParameters);
            renderer.EnqueuePass(m_VolumetricLightingPass);
        }

        protected override void Dispose(bool disposing)
        {
            m_GenerateMaxZPass.Dispose();
            m_VolumetricLightingPass.Dispose();
        }

    }

}