using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UniversalForwardPlusVolumetric
{
    public struct VBufferParameters
    {
        public Vector3Int viewportSize;
        public float voxelSize;
        public Vector4 depthEncodingParams;
        public Vector4 depthDecodingParams;

        public VBufferParameters(Vector3Int viewportSize, float depthExtent, float camNear, float camFar, float camVFoV,
                                 float sliceDistributionUniformity, float voxelSize)
        {
            this.viewportSize = viewportSize;
            this.voxelSize = voxelSize;

            // The V-Buffer is sphere-capped, while the camera frustum is not.
            // We always start from the near plane of the camera.

            float aspectRatio = viewportSize.x / (float)viewportSize.y;
            float farPlaneHeight = 2.0f * Mathf.Tan(0.5f * camVFoV) * camFar;
            float farPlaneWidth = farPlaneHeight * aspectRatio;
            float farPlaneMaxDim = Mathf.Max(farPlaneWidth, farPlaneHeight);
            float farPlaneDist = Mathf.Sqrt(camFar * camFar + 0.25f * farPlaneMaxDim * farPlaneMaxDim);

            float nearDist = camNear;
            float farDist = Math.Min(nearDist + depthExtent, farPlaneDist);

            float c = 2 - 2 * sliceDistributionUniformity; // remap [0, 1] -> [2, 0]
            c = Mathf.Max(c, 0.001f);                // Avoid NaNs

            depthEncodingParams = ComputeLogarithmicDepthEncodingParams(nearDist, farDist, c);
            depthDecodingParams = ComputeLogarithmicDepthDecodingParams(nearDist, farDist, c);
        }

        private float ComputeViewportScale_Internal(int viewportSize, int bufferSize)
        {
            float rcpBufferSize = 1.0f / bufferSize;
            // Scale by (vp_dim / buf_dim).
            return viewportSize * rcpBufferSize;
        }

        private float ComputeViewportLimit_Internal(int viewportSize, int bufferSize)
        {
            float rcpBufferSize = 1.0f / bufferSize;

            // Clamp to (vp_dim - 0.5) / buf_dim.
            return (viewportSize - 0.5f) * rcpBufferSize;
        }


        public Vector3 ComputeViewportScale(Vector3Int bufferSize)
        {
            return new Vector3(ComputeViewportScale_Internal(viewportSize.x, bufferSize.x),
                ComputeViewportScale_Internal(viewportSize.y, bufferSize.y),
                ComputeViewportScale_Internal(viewportSize.z, bufferSize.z));
        }

        public Vector3 ComputeViewportLimit(Vector3Int bufferSize)
        {
            return new Vector3(ComputeViewportLimit_Internal(viewportSize.x, bufferSize.x),
                ComputeViewportLimit_Internal(viewportSize.y, bufferSize.y),
                ComputeViewportLimit_Internal(viewportSize.z, bufferSize.z));
        }

        public float ComputeLastSliceDistance(uint sliceCount)
        {
            float d = 1.0f - 0.5f / sliceCount;
            float ln2 = 0.69314718f;

            // DecodeLogarithmicDepthGeneralized(1 - 0.5 / sliceCount)
            return depthDecodingParams.x * Mathf.Exp(ln2 * d * depthDecodingParams.y) + depthDecodingParams.z;
        }

        // See EncodeLogarithmicDepthGeneralized().
        private static Vector4 ComputeLogarithmicDepthEncodingParams(float nearPlane, float farPlane, float c)
        {
            Vector4 depthParams = new Vector4();

            float n = nearPlane;
            float f = farPlane;

            depthParams.y = 1.0f / Mathf.Log(c * (f - n) + 1, 2);
            depthParams.x = Mathf.Log(c, 2) * depthParams.y;
            depthParams.z = n - 1.0f / c; // Same
            depthParams.w = 0.0f;

            return depthParams;
        }

        // See DecodeLogarithmicDepthGeneralized().
        private static Vector4 ComputeLogarithmicDepthDecodingParams(float nearPlane, float farPlane, float c)
        {
            Vector4 depthParams = new Vector4();

            float n = nearPlane;
            float f = farPlane;

            depthParams.x = 1.0f / c;
            depthParams.y = Mathf.Log(c * (f - n) + 1, 2);
            depthParams.z = n - 1.0f / c; // Same
            depthParams.w = 0.0f;

            return depthParams;
        }
    }

    public static class VolumetricUtils
    {
        private const float k_OptimalFogScreenResolutionPercentage = (1.0f / 8.0f) * 100;
        public static int DivRoundUp(int x, int y) => (x + y - 1) / y;

        public static void ComputeVolumetricFogSliceCountAndScreenFraction(VolumetricConfig config, out int sliceCount, out float screenFraction)
        {
            screenFraction = config.screenResolutionPercentage * 0.01f;
            sliceCount = config.volumeSliceCount;
        }

        public static Vector3Int ComputeVolumetricViewportSize(VolumetricConfig config, Camera camera, ref float voxelSize)
        {
            int viewportWidth = camera.scaledPixelWidth;
            int viewportHeight = camera.scaledPixelHeight;

            ComputeVolumetricFogSliceCountAndScreenFraction(config, out var sliceCount, out var screenFraction);
            if (config.screenResolutionPercentage == k_OptimalFogScreenResolutionPercentage)
                voxelSize = 8;
            else
                voxelSize = 1.0f / screenFraction; // Does not account for rounding (same function, above)

            int w = Mathf.RoundToInt(viewportWidth * screenFraction);
            int h = Mathf.RoundToInt(viewportHeight * screenFraction);

            // TODO:
            // Round to nearest multiple of viewCount so that each views have the exact same number of slices (important for XR)
            // int d = hdCamera.viewCount * Mathf.CeilToInt(sliceCount / hdCamera.viewCount);
            int d = sliceCount;

            return new Vector3Int(w, h, d);
        }

        public static VBufferParameters ComputeVolumetricBufferParameters(VolumetricConfig config, Camera camera)
        {
            float voxelSize = 0;
            Vector3Int viewportSize = ComputeVolumetricViewportSize(config, camera, ref voxelSize);

            return new VBufferParameters(viewportSize, config.depthExtent,
                camera.nearClipPlane,
                camera.farClipPlane,
                camera.fieldOfView,
                config.sliceDistributionUniformity,
                voxelSize);
        }

        public static float ComputZPlaneTexelSpacing(float planeDepth, float verticalFoV, float resolutionY)
        {
            float tanHalfVertFoV = Mathf.Tan(0.5f * verticalFoV);
            return tanHalfVertFoV * (2.0f / resolutionY) * planeDepth;
        }


        // This is a sequence of 7 equidistant numbers from 1/14 to 13/14.
        // Each of them is the centroid of the interval of length 2/14.
        // They've been rearranged in a sequence of pairs {small, large}, s.t. (small + large) = 1.
        // That way, the running average position is close to 0.5.
        // | 6 | 2 | 4 | 1 | 5 | 3 | 7 |
        // |   |   |   | o |   |   |   |
        // |   | o |   | x |   |   |   |
        // |   | x |   | x |   | o |   |
        // |   | x | o | x |   | x |   |
        // |   | x | x | x | o | x |   |
        // | o | x | x | x | x | x |   |
        // | x | x | x | x | x | x | o |
        // | x | x | x | x | x | x | x |
        public static float[] zSeq = { 7.0f / 14.0f, 3.0f / 14.0f, 11.0f / 14.0f, 5.0f / 14.0f, 9.0f / 14.0f, 1.0f / 14.0f, 13.0f / 14.0f };

        // Ref: https://en.wikipedia.org/wiki/Close-packing_of_equal_spheres
        // The returned {x, y} coordinates (and all spheres) are all within the (-0.5, 0.5)^2 range.
        // The pattern has been rotated by 15 degrees to maximize the resolution along X and Y:
        // https://www.desmos.com/calculator/kcpfvltz7c
        public static void GetHexagonalClosePackedSpheres7(Vector2[] coords)
        {
            float r = 0.17054068870105443882f;
            float d = 2 * r;
            float s = r * Mathf.Sqrt(3);

            // Try to keep the weighted average as close to the center (0.5) as possible.
            //  (7)(5)    ( )( )    ( )( )    ( )( )    ( )( )    ( )(o)    ( )(x)    (o)(x)    (x)(x)
            // (2)(1)(3) ( )(o)( ) (o)(x)( ) (x)(x)(o) (x)(x)(x) (x)(x)(x) (x)(x)(x) (x)(x)(x) (x)(x)(x)
            //  (4)(6)    ( )( )    ( )( )    ( )( )    (o)( )    (x)( )    (x)(o)    (x)(x)    (x)(x)
            coords[0] = new Vector2(0, 0);
            coords[1] = new Vector2(-d, 0);
            coords[2] = new Vector2(d, 0);
            coords[3] = new Vector2(-r, -s);
            coords[4] = new Vector2(r, s);
            coords[5] = new Vector2(r, -s);
            coords[6] = new Vector2(-r, s);

            // Rotate the sampling pattern by 15 degrees.
            const float cos15 = 0.96592582628906828675f;
            const float sin15 = 0.25881904510252076235f;

            for (int i = 0; i < 7; i++)
            {
                Vector2 coord = coords[i];

                coords[i].x = coord.x * cos15 - coord.y * sin15;
                coords[i].y = coord.x * sin15 + coord.y * cos15;
            }
        }

        public static float CornetteShanksPhasePartConstant(float g)
        {
            return (3.0f / (8.0f * Mathf.PI)) * (1.0f - g * g) / (2.0f + g * g);
        }

        public static float ScaleHeightFromLayerDepth(float d)
        {
            // Exp[-d / H] = 0.001
            // -d / H = Log[0.001]
            // H = d / -Log[0.001]
            return d * 0.144765f;
        }

        public static int CalculateMaxEnvCubemapMip()
        {
            int maxMip = 0;
            if (RenderSettings.defaultReflectionMode == UnityEngine.Rendering.DefaultReflectionMode.Custom)
            {
                var texture = RenderSettings.customReflectionTexture;
                if (texture == null)
                {
                    return 0;
                }
                return RenderSettings.customReflectionTexture.mipmapCount;
            }
            
            int res = RenderSettings.defaultReflectionResolution;
            while (res > 1)
            {
                res >>= 1;
                maxMip++;
            }
            return maxMip;
        }

    }
}