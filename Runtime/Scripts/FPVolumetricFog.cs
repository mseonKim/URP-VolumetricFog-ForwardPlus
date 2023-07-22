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
        private VBufferParameters vBufferParameters;

        public override void Create()
        {
            m_GenerateMaxZPass = new GenerateMaxZPass();
            m_VolumetricLightingPass = new FPVolumetricLightingPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (config == null)
                return;

            vBufferParameters = VolumetricUtils.ComputeVolumetricBufferParameters(config, renderingData.cameraData.camera);

            m_GenerateMaxZPass.Setup(config, vBufferParameters);
            renderer.EnqueuePass(m_GenerateMaxZPass);

            if (config.volumetricLighting)
            {
                m_VolumetricLightingPass.Setup(config, vBufferParameters);
                renderer.EnqueuePass(m_VolumetricLightingPass);
            }
        }

    }

}

