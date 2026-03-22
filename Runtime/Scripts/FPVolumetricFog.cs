// Ported from HDRP Volumetric fog
// Limitation: Only works for URP Forward+

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UniversalForwardPlusVolumetric
{
    public class FPVolumetricFog : ScriptableRendererFeature
    {
        private const string k_LogPrefix = "[UniversalFPVolumetricFog]";

        [Tooltip("Which render pass to render at. The default is BeforeRenderingPostProcessing")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        [SerializeField] private VolumetricFogResources resources;

        private GenerateMaxZPass m_GenerateMaxZPass;
        private FPVolumetricLightingPass m_VolumetricLightingPass;
        private VBufferParameters m_VBufferParameters;
        private bool m_HasLoggedMissingResources;

        public override void Create()
        {
            EnsureResources();
            m_GenerateMaxZPass = new GenerateMaxZPass(renderPassEvent);
            m_VolumetricLightingPass = new FPVolumetricLightingPass(renderPassEvent);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureResources();
        }
#endif

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Reflection)
                return;

#if UNITY_EDITOR
            if (cameraType != CameraType.SceneView && cameraType != CameraType.Game)
            {
                return;
            }
#endif

            EnsureResources();

            if (!TryResolveSettings(out var settings))
            {
                m_VolumetricLightingPass?.InvalidateHistory();
                return;
            }

            if (!settings.IsActiveForRendering)
            {
                m_VolumetricLightingPass?.InvalidateHistory();
                return;
            }

            if (!ValidateResources())
                return;

            m_VBufferParameters = VolumetricUtils.ComputeVolumetricBufferParameters(settings, renderingData.cameraData.camera, renderingData.cameraData.renderScale);

            m_GenerateMaxZPass.Setup(resources, m_VBufferParameters);
            renderer.EnqueuePass(m_GenerateMaxZPass);

            int historyInvalidationKey = settings.GetHistoryInvalidationHash();
            m_VolumetricLightingPass.Setup(settings, resources, m_VBufferParameters, historyInvalidationKey);
            renderer.EnqueuePass(m_VolumetricLightingPass);
        }

        protected override void Dispose(bool disposing)
        {
            m_GenerateMaxZPass?.Dispose();
            m_VolumetricLightingPass?.Dispose();
        }

        private void EnsureResources()
        {
            var resolvedResources = resources;
#if UNITY_EDITOR
            VolumetricFogResourceLoader.TryAssignDefaults(ref resolvedResources);
#endif
            resources = resolvedResources;
        }

        private bool ValidateResources()
        {
            if (resources.HasRequiredResources())
            {
                m_HasLoggedMissingResources = false;
                return true;
            }

            if (!m_HasLoggedMissingResources)
            {
                Debug.LogWarning($"{k_LogPrefix} Missing required renderer resources on {nameof(FPVolumetricFog)}: {resources.GetMissingRequiredResourceSummary()}", this);
                m_HasLoggedMissingResources = true;
            }

            return false;
        }

        private bool TryResolveSettings(out VolumetricFogSettings settings)
        {
            var stack = VolumeManager.instance?.stack;
            var volume = stack?.GetComponent<FPVolumetricFogVolume>();
            if (volume != null && volume.UsesVolumeSource())
            {
                settings = volume.ToSettings();
                return true;
            }

            settings = default;
            return false;
        }
    }
}
