// Ported from HDRP Volumetric fog
// Limitation: Only works for URP Forward+

using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UniversalForwardPlusVolumetric
{
    public class FPVolumetricFog : ScriptableRendererFeature
    {
        public VolumetricConfig config;
        private FPVolumetricLightingPass m_VolumetricLightingPass;

        public override void Create()
        {
            m_VolumetricLightingPass = new FPVolumetricLightingPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (config == null)
                return;

            if (config.useVolumetricLighting)
            {
                m_VolumetricLightingPass.Setup(config);
                renderer.EnqueuePass(m_VolumetricLightingPass);
            }
        }

    }

}

