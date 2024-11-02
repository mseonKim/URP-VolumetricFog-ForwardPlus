using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UniversalForwardPlusVolumetric
{
    public enum LocalVolumetricFogFalloffMode
    {
        /// <summary>Fade using a linear function.</summary>
        Linear,
        /// <summary>Fade using an exponential function.</summary>
        Exponential,
    }

    internal struct LocalVolumetricFogEngineData
    {
        public Vector3 scattering;    // [0, 1]
        public float extinction;    // [0, 1]
        public Vector3 textureTiling;
        public int invertFade;    // bool...
        public Vector3 textureScroll;
        public float rcpDistFadeLen;
        public Vector3 rcpPosFaceFade;
        public float endTimesRcpDistFadeLen;
        public Vector3 rcpNegFaceFade;
        // public LocalVolumetricFogBlendingMode blendingMode;
        public Vector3 albedo;
        public LocalVolumetricFogFalloffMode falloffMode;
    }

    internal struct OrientedBBox
    {
        // 3 x float4 = 48 bytes.
        // TODO: pack the axes into 16-bit UNORM per channel, and consider a quaternionic representation.
        public Vector3 right;
        public float extentX;
        public Vector3 up;
        public float extentY;
        public Vector3 center;
        public float extentZ;

        public Vector3 forward { get { return Vector3.Cross(up, right); } }

        public OrientedBBox(Matrix4x4 trs)
        {
            Vector3 vecX = trs.GetColumn(0);
            Vector3 vecY = trs.GetColumn(1);
            Vector3 vecZ = trs.GetColumn(2);

            center = trs.GetColumn(3);
            right = vecX * (1.0f / vecX.magnitude);
            up = vecY * (1.0f / vecY.magnitude);

            extentX = 0.5f * vecX.magnitude;
            extentY = 0.5f * vecY.magnitude;
            extentZ = 0.5f * vecZ.magnitude;
        }
    }

    [Serializable]
    public struct LocalVolumeShaderSetting
    {
        public ComputeShader shader;
        public string kernelName;
    }

    [ExecuteAlways]
    [RequireComponent(typeof(BoxCollider))]
    public class LocalVolumetricFog : MonoBehaviour
    {
        // Support upto 4 bounds due to performance in URP, will be sorted in render pass.
        public static List<LocalVolumetricFog> volumes = new List<LocalVolumetricFog>(4);
        public static Action onRegisterVolume;


        [Header("Volume")]
        [Tooltip("The color this fog scatters light to.")]
        public Color scatteringAlbedo = Color.white;
        [Min(0.05f), Tooltip("Density at the base of the fog. Determines how far you can see through the fog in meters.")]
        public float fogDistance = 10f; // meanFreePath
        public Texture mask;
        [HideInInspector] public LocalVolumeShaderSetting volumeShaderSetting; // unused currently

        /// <summary>Edge fade factor along the positive X, Y and Z axes.</summary>
        public Vector3 positiveFade = Vector3.one * 0.5f;
        /// <summary>Edge fade factor along the negative X, Y and Z axes.</summary>
        public Vector3 negativeFade = Vector3.one * 0.5f;
        /// <summary>Inverts the fade gradient.</summary>
        public bool invertFade;
        /// <summary>Distance at which density fading starts.</summary>
        public float distanceFadeStart = 50f;
        /// <summary>Distance at which density fading ends.</summary>
        public float distanceFadeEnd = 200f;
        public LocalVolumetricFogFalloffMode falloffMode;


        private BoxCollider m_Collider;
        private Vector3 m_Size;

        internal Vector3 center => m_Collider.center;

        ///<summary>
        /// Note - Should call this function from render pass only.
        ///</summary>
        internal static void RefreshVolumes()
        {
            volumes.Clear();
            onRegisterVolume?.Invoke();
        }

        internal static LocalVolumetricFog[] SortVolumes()
        {
            // Bubble sort
            int count = volumes.Count;
            int iterNumber = Math.Min(count, 4);
            var cameraPositionWS = Camera.main.transform.position;

            for (int i = 0; i < iterNumber; i++)
            {
                var current = i;
                var position = volumes[i].transform.position;
                var dist = Vector3.Distance(cameraPositionWS, position + volumes[i].center);
                var currentVolume = volumes[i];
                for (int i2 = i + 1; i2 < count; i2++)
                {
                    var dist2 = Vector3.Distance(cameraPositionWS, position + volumes[i].center);
                    if (dist2 < dist)
                    {
                        volumes[current] = volumes[i2];
                        volumes[i2] = currentVolume;
                        current = i2;
                    }
                }
            }

            LocalVolumetricFog[] result = new LocalVolumetricFog[iterNumber];
            for (int i = 0; i < iterNumber; i++)
            {
                result[i] = volumes[i];
            }

            return result;
        }

#if UNITY_EDITOR
        void Update()
        {
            if (m_Collider != null)
                m_Size = m_Collider.bounds.size;
        }
#endif

        void OnEnable()
        {
            m_Collider = GetComponent<BoxCollider>();
            m_Size = m_Collider.bounds.size;
            onRegisterVolume += RegisterVolume;
        }

        void Start()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                m_Size = m_Collider.bounds.size;
                // m_Collider.enabled = false;
            }
#else
            m_Collider.enabled = false;
#endif
        }

        void OnDisable()
        {
            onRegisterVolume -= RegisterVolume;
        }

        public void RegisterVolume()
        {
            Debug.Assert(m_Collider != null);
            volumes.Add(this);
        }

        public virtual void SetComputeShaderProperties(CommandBuffer cmd, ComputeShader cs, int kernel)
        {
            // Set mask texture and material properties here
        }

#if ENABLE_URP_VOLUEMTRIC_FOG_RENDERGRAPH
        public virtual void SetComputeShaderProperties(ComputeCommandBuffer cmd, ComputeShader cs, int kernel)
        {
            // Set mask texture and material properties here
        }
#endif

        internal OrientedBBox GetOBB()
        {
            return new OrientedBBox(Matrix4x4.TRS(transform.position + m_Collider.center, transform.rotation, m_Size));
        }

        internal LocalVolumetricFogEngineData ConvertToEngineData()
        {
            LocalVolumetricFogEngineData data = new LocalVolumetricFogEngineData();

            data.extinction = 1f / fogDistance;
            data.scattering = data.extinction * (Vector4)scatteringAlbedo;

            // data.blendingMode = blendingMode;
            data.albedo = (Vector3)(Vector4)scatteringAlbedo;

            // data.textureScroll = textureOffset;
            // data.textureTiling = textureTiling;

            // Clamp to avoid NaNs.
            Vector3 positiveFade = this.positiveFade;
            Vector3 negativeFade = this.negativeFade;

            data.rcpPosFaceFade.x = Mathf.Min(1.0f / positiveFade.x, float.MaxValue);
            data.rcpPosFaceFade.y = Mathf.Min(1.0f / positiveFade.y, float.MaxValue);
            data.rcpPosFaceFade.z = Mathf.Min(1.0f / positiveFade.z, float.MaxValue);

            data.rcpNegFaceFade.y = Mathf.Min(1.0f / negativeFade.y, float.MaxValue);
            data.rcpNegFaceFade.x = Mathf.Min(1.0f / negativeFade.x, float.MaxValue);
            data.rcpNegFaceFade.z = Mathf.Min(1.0f / negativeFade.z, float.MaxValue);

            data.invertFade = invertFade ? 1 : 0;
            data.falloffMode = falloffMode;

            float distFadeLen = Mathf.Max(distanceFadeEnd - distanceFadeStart, 0.00001526f);

            data.rcpDistFadeLen = 1.0f / distFadeLen;
            data.endTimesRcpDistFadeLen = distanceFadeEnd * data.rcpDistFadeLen;

            return data;
        }
    }
}
