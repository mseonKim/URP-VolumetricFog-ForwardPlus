using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEngine;

namespace UniversalForwardPlusVolumetric.Editor
{
    [CustomEditor(typeof(FPVolumetricFogVolume))]
    public sealed class FPVolumetricFogVolumeEditor : UniversalRenderPipelineVolumeComponentEditor
    {
        private enum VolumetricLightingQuality
        {
            Low,
            Medium,
            High,
            Cinematic,
            Custom,
        }

        private static readonly GUIContent s_Title = EditorGUIUtility.TrTextContent("FPVolumetricFog");
        private static bool s_ShowAdvanced;

        private SerializedDataParameter m_Enabled;
        private SerializedDataParameter m_ColorMode;
        private SerializedDataParameter m_Color;
        private SerializedDataParameter m_Tint;
        private SerializedDataParameter m_MipFogMaxMip;
        private SerializedDataParameter m_MipFogNear;
        private SerializedDataParameter m_MipFogFar;
        private SerializedDataParameter m_BaseHeight;
        private SerializedDataParameter m_MaximumHeight;
        private SerializedDataParameter m_FogAttenuationDistance;

        private SerializedDataParameter m_VolumetricLighting;
        private SerializedDataParameter m_EnableDirectionalLight;
        private SerializedDataParameter m_EnablePointAndSpotLight;
        private SerializedDataParameter m_Albedo;
        private SerializedDataParameter m_DirectionalScatteringIntensity;
        private SerializedDataParameter m_LocalScatteringIntensity;
        private SerializedDataParameter m_Anisotropy;
        private SerializedDataParameter m_DepthExtent;
        private SerializedDataParameter m_ScreenResolutionPercentage;
        private SerializedDataParameter m_VolumeSliceCount;
        private SerializedDataParameter m_DenoiseMode;

        private SerializedDataParameter m_SampleOffsetWeight;
        private SerializedDataParameter m_AutoSliceDistribution;
        private SerializedDataParameter m_SliceDistributionUniformity;
        private SerializedDataParameter m_BlendWeight;

        private GUIStyle m_SectionTitleStyle;
        private GUIStyle m_SubsectionTitleStyle;
        private bool m_ForceCustomQuality;

        public override void OnEnable()
        {
            base.OnEnable();

            var o = new PropertyFetcher<FPVolumetricFogVolume>(serializedObject);
            m_Enabled = Unpack(o.Find(x => x.enabled));
            m_ColorMode = Unpack(o.Find(x => x.colorMode));
            m_Color = Unpack(o.Find(x => x.color));
            m_Tint = Unpack(o.Find(x => x.tint));
            m_MipFogMaxMip = Unpack(o.Find(x => x.mipFogMaxMip));
            m_MipFogNear = Unpack(o.Find(x => x.mipFogNear));
            m_MipFogFar = Unpack(o.Find(x => x.mipFogFar));
            m_BaseHeight = Unpack(o.Find(x => x.baseHeight));
            m_MaximumHeight = Unpack(o.Find(x => x.maximumHeight));
            m_FogAttenuationDistance = Unpack(o.Find(x => x.fogAttenuationDistance));

            m_VolumetricLighting = Unpack(o.Find(x => x.volumetricLighting));
            m_EnableDirectionalLight = Unpack(o.Find(x => x.enableDirectionalLight));
            m_EnablePointAndSpotLight = Unpack(o.Find(x => x.enablePointAndSpotLight));
            m_Albedo = Unpack(o.Find(x => x.albedo));
            m_DirectionalScatteringIntensity = Unpack(o.Find(x => x.directionalScatteringIntensity));
            m_LocalScatteringIntensity = Unpack(o.Find(x => x.localScatteringIntensity));
            m_Anisotropy = Unpack(o.Find(x => x.anisotropy));
            m_DepthExtent = Unpack(o.Find(x => x.depthExtent));
            m_ScreenResolutionPercentage = Unpack(o.Find(x => x.screenResolutionPercentage));
            m_VolumeSliceCount = Unpack(o.Find(x => x.volumeSliceCount));
            m_DenoiseMode = Unpack(o.Find(x => x.denoiseMode));

            m_SampleOffsetWeight = Unpack(o.Find(x => x.sampleOffsetWeight));
            m_AutoSliceDistribution = Unpack(o.Find(x => x.autoSliceDistribution));
            m_SliceDistributionUniformity = Unpack(o.Find(x => x.sliceDistributionUniformity));
            m_BlendWeight = Unpack(o.Find(x => x.blendWeight));

            m_SectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Max(EditorStyles.boldLabel.fontSize, 12),
                margin = new RectOffset(0, 0, 6, 4),
                padding = new RectOffset(0, 0, 2, 2)
            };

            m_SubsectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontStyle = FontStyle.Bold,
                margin = new RectOffset(0, 0, 4, 2)
            };

            m_ForceCustomQuality = GetPresetFromValues() == VolumetricLightingQuality.Custom;
        }

        public override GUIContent GetDisplayTitle()
        {
            return s_Title;
        }

        public override void OnInspectorGUI()
        {
            DrawSectionHeader("Base", false);
            PropertyField(m_Enabled);
            PropertyField(m_ColorMode);
            PropertyField(m_Color);
            PropertyField(m_Tint);
            PropertyField(m_MipFogMaxMip);
            PropertyField(m_MipFogNear);
            PropertyField(m_MipFogFar);
            PropertyField(m_BaseHeight);
            PropertyField(m_MaximumHeight);
            PropertyField(m_FogAttenuationDistance);

            DrawSectionHeader("Volumetric Lighting");
            PropertyField(m_VolumetricLighting);
            PropertyField(m_EnableDirectionalLight);
            PropertyField(m_EnablePointAndSpotLight);
            PropertyField(m_Albedo);
            PropertyField(m_DirectionalScatteringIntensity);
            PropertyField(m_LocalScatteringIntensity);
            PropertyField(m_Anisotropy);
            PropertyField(m_DepthExtent);
            DrawQualitySettings();

            EditorGUILayout.LabelField(string.Empty, GUI.skin.horizontalSlider);
            s_ShowAdvanced = EditorGUILayout.BeginFoldoutHeaderGroup(s_ShowAdvanced, "Volumetric Lighting - Advanced");
            if (s_ShowAdvanced)
            {
                EditorGUI.indentLevel++;
                PropertyField(m_SampleOffsetWeight);
                PropertyField(m_AutoSliceDistribution);
                PropertyField(m_SliceDistributionUniformity);
                PropertyField(m_BlendWeight);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawSectionHeader(string title, bool splitSection = true)
        {
            if (splitSection)
                EditorGUILayout.LabelField(string.Empty, GUI.skin.horizontalSlider);
            EditorGUILayout.LabelField(title, m_SectionTitleStyle);
        }

        private void DrawQualitySettings()
        {
            EditorGUI.indentLevel++;
            var displayedQuality = GetDisplayedQuality();
            EditorGUI.BeginChangeCheck();
            var selectedQuality = (VolumetricLightingQuality)EditorGUILayout.EnumPopup("Quality", displayedQuality);
            if (EditorGUI.EndChangeCheck())
            {
                HandleQualitySelection(selectedQuality);
                displayedQuality = GetDisplayedQuality();
            }

            using (new EditorGUI.DisabledScope(displayedQuality != VolumetricLightingQuality.Custom))
            {
                PropertyField(m_ScreenResolutionPercentage);
                PropertyField(m_VolumeSliceCount);
                PropertyField(m_DenoiseMode);
            }
            EditorGUI.indentLevel--;
        }

        private void HandleQualitySelection(VolumetricLightingQuality selectedQuality)
        {
            if (selectedQuality == VolumetricLightingQuality.Custom)
            {
                m_ForceCustomQuality = true;
                return;
            }

            m_ForceCustomQuality = false;
            ApplyPreset(selectedQuality);
        }

        private VolumetricLightingQuality GetDisplayedQuality()
        {
            if (m_ForceCustomQuality)
                return VolumetricLightingQuality.Custom;

            return GetPresetFromValues();
        }

        private VolumetricLightingQuality GetPresetFromValues()
        {
            foreach (VolumetricLightingQuality quality in System.Enum.GetValues(typeof(VolumetricLightingQuality)))
            {
                if (quality == VolumetricLightingQuality.Custom)
                    continue;

                var preset = GetPreset(quality);
                if (preset == null)
                    continue;

                if (Mathf.Abs(m_ScreenResolutionPercentage.value.floatValue - preset.Value.screenResolutionPercentage) > 0.0001f)
                    continue;

                if (m_VolumeSliceCount.value.intValue != preset.Value.volumeSliceCount)
                    continue;

                if (m_DenoiseMode.value.enumValueIndex != (int)preset.Value.denoiseMode)
                    continue;

                return quality;
            }

            return VolumetricLightingQuality.Custom;
        }

        private void ApplyPreset(VolumetricLightingQuality quality)
        {
            var preset = GetPreset(quality);
            if (preset == null)
                return;

            if (!m_ScreenResolutionPercentage.overrideState.boolValue)
            {
                m_ScreenResolutionPercentage.overrideState.boolValue = true;
            }

            if (!m_VolumeSliceCount.overrideState.boolValue)
            {
                m_VolumeSliceCount.overrideState.boolValue = true;
            }

            if (!m_DenoiseMode.overrideState.boolValue)
            {
                m_DenoiseMode.overrideState.boolValue = true;
            }

            if (Mathf.Abs(m_ScreenResolutionPercentage.value.floatValue - preset.Value.screenResolutionPercentage) > 0.0001f)
            {
                m_ScreenResolutionPercentage.value.floatValue = preset.Value.screenResolutionPercentage;
            }

            if (m_VolumeSliceCount.value.intValue != preset.Value.volumeSliceCount)
            {
                m_VolumeSliceCount.value.intValue = preset.Value.volumeSliceCount;
            }

            if (m_DenoiseMode.value.enumValueIndex != (int)preset.Value.denoiseMode)
            {
                m_DenoiseMode.value.enumValueIndex = (int)preset.Value.denoiseMode;
            }
        }

        private static (float screenResolutionPercentage, int volumeSliceCount, DenoiseMode denoiseMode)? GetPreset(VolumetricLightingQuality quality)
        {
            switch (quality)
            {
                case VolumetricLightingQuality.Low:
                    return (6.25f, 64, DenoiseMode.Gaussian);
                case VolumetricLightingQuality.Medium:
                    return (12.5f, 128, DenoiseMode.Both);
                case VolumetricLightingQuality.High:
                    return (16.67f, 170, DenoiseMode.Both);
                case VolumetricLightingQuality.Cinematic:
                    return (25f, 256, DenoiseMode.Both);
                default:
                    return null;
            }
        }
    }
}
