using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniversalForwardPlusVolumetric
{
    public class SmokeVolume : LocalVolumetricFog
    {
        [Header("Smoke")]
        public Vector3 speed;

        internal override void SetComputeShaderProperties(CommandBuffer cmd)
        {
        }

    }
}
