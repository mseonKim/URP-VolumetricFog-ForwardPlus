using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniversalForwardPlusVolumetric
{
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
        public string kernalName;
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
        public float fogDistance = 150; // meanFreePath
        public Texture mask;
        public LocalVolumeShaderSetting volumeShaderSetting;
        private BoxCollider m_Collider;
        private Vector3 m_Size;

        internal Vector3 center => m_Collider.center;
        public float extinction => 1f / fogDistance;

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
            float maxDistance = 100000f;
            var cameraPositionWS = Camera.main.transform.position;

            for (int i = 0; i < iterNumber; i++)
            {
                var current = i;
                var dist = Vector3.Distance(cameraPositionWS, volumes[i].center);
                var currentVolume = volumes[i];
                for (int i2 = i + 1; i2 < count; i2++)
                {
                    var dist2 = Vector3.Distance(cameraPositionWS, volumes[i].center);
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
                m_Collider.enabled = false;
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

        internal OrientedBBox GetOBB()
        {
            return new OrientedBBox(Matrix4x4.TRS(transform.position, transform.rotation, m_Size));
        }

        internal virtual void SetComputeShaderProperties(CommandBuffer cmd)
        {
            // Set mask texture and material properties here
        }
    }
}
