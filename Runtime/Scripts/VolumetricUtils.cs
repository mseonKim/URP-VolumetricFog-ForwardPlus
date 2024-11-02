using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_URP_VOLUEMTRIC_FOG_RENDERGRAPH
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace UniversalForwardPlusVolumetric
{
    public struct VBufferParameters
    {
        public Vector3Int viewportSize;
        public float voxelSize;
        public Vector4 depthEncodingParams;
        public Vector4 depthDecodingParams;

        public VBufferParameters(Vector3Int viewportSize, float depthExtent, Camera camera,
                                 float sliceDistributionUniformity, float voxelSize, bool autoSliceDistribution = false)
        {
            this.viewportSize = viewportSize;
            this.voxelSize = voxelSize;

            var camNear = camera.nearClipPlane;
            var camFar = camera.farClipPlane;
            var camVFoV = camera.fieldOfView;

            // The V-Buffer is sphere-capped, while the camera frustum is not.
            // We always start from the near plane of the camera.

            float aspectRatio = viewportSize.x / (float)viewportSize.y;
            float farPlaneHeight = 2.0f * Mathf.Tan(0.5f * camVFoV) * camFar;
            float farPlaneWidth = farPlaneHeight * aspectRatio;
            float farPlaneMaxDim = Mathf.Max(farPlaneWidth, farPlaneHeight);
            float farPlaneDist = Mathf.Sqrt(camFar * camFar + 0.25f * farPlaneMaxDim * farPlaneMaxDim);

            float nearDist = camNear;
            float farDist = Math.Min(nearDist + depthExtent, farPlaneDist);

            if (autoSliceDistribution)
            {
                // Set slice distribution by distance from (0, 0, 0)
                var dist = Vector3.Distance(camera.transform.position, Vector3.zero);
                var x = dist / depthExtent;
                // For distant view, 1 is the best to reduce ray artifact. So force to make one if the distance is greater than half of the depth extent.
                sliceDistributionUniformity = Mathf.Clamp01(2 * x); // rcp(0.5) = 2
            }
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

            return new VBufferParameters(viewportSize,
                                        config.depthExtent,
                                        camera,
                                        config.sliceDistributionUniformity,
                                        voxelSize,
                                        config.autoSliceDistribution);
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
            if (RenderSettings.defaultReflectionMode == DefaultReflectionMode.Custom)
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

        internal static void SetCameraMatrices(CameraData cameraData, out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out Matrix4x4 viewProjMatrix, out Matrix4x4 invViewProjMatrix)
        {
            var camera = cameraData.camera;
            viewMatrix = camera.worldToCameraMatrix;
            projMatrix = cameraData.GetGPUProjectionMatrix();
            viewProjMatrix = projMatrix * viewMatrix;
            invViewProjMatrix = viewProjMatrix.inverse;
        }

        /// <summary>
        /// Determine if a projection matrix is off-center (asymmetric).
        /// </summary>
        /// <param name="matrix"></param>
        /// <returns></returns>
        internal static bool IsProjectionMatrixAsymmetric(in Matrix4x4 matrix)
            => matrix.m02 != 0 || matrix.m12 != 0;

        /// <summary>Get the aspect ratio of a projection matrix.</summary>
        /// <param name="matrix"></param>
        /// <returns></returns>
        internal static float ProjectionMatrixAspect(in Matrix4x4 matrix)
            => - matrix.m11 / matrix.m00;

        internal static Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(float verticalFoV, Vector2 lensShift, Vector4 screenSize, Matrix4x4 worldToViewMatrix, bool renderToCubemap, float aspectRatio = -1, bool isOrthographic = false)
        {
            Matrix4x4 viewSpaceRasterTransform;

            if (isOrthographic)
            {
                // For ortho cameras, project the skybox with no perspective
                // the same way as builtin does (case 1264647)
                viewSpaceRasterTransform = new Matrix4x4(
                    new Vector4(-2.0f * screenSize.z, 0.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, -2.0f * screenSize.w, 0.0f, 0.0f),
                    new Vector4(1.0f, 1.0f, -1.0f, 0.0f),
                    new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            }
            else
            {
                // Compose the view space version first.
                // V = -(X, Y, Z), s.t. Z = 1,
                // X = (2x / resX - 1) * tan(vFoV / 2) * ar = x * [(2 / resX) * tan(vFoV / 2) * ar] + [-tan(vFoV / 2) * ar] = x * [-m00] + [-m20]
                // Y = (2y / resY - 1) * tan(vFoV / 2)      = y * [(2 / resY) * tan(vFoV / 2)]      + [-tan(vFoV / 2)]      = y * [-m11] + [-m21]

                aspectRatio = aspectRatio < 0 ? screenSize.x * screenSize.w : aspectRatio;
                float tanHalfVertFoV = Mathf.Tan(0.5f * verticalFoV);

                // Compose the matrix.
                float m21 = (1.0f - 2.0f * lensShift.y) * tanHalfVertFoV;
                float m11 = -2.0f * screenSize.w * tanHalfVertFoV;

                float m20 = (1.0f - 2.0f * lensShift.x) * tanHalfVertFoV * aspectRatio;
                float m00 = -2.0f * screenSize.z * tanHalfVertFoV * aspectRatio;

                if (renderToCubemap)
                {
                    // Flip Y.
                    m11 = -m11;
                    m21 = -m21;
                }

                viewSpaceRasterTransform = new Matrix4x4(new Vector4(m00, 0.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, m11, 0.0f, 0.0f),
                    new Vector4(m20, m21, -1.0f, 0.0f),
                    new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            }

            // Remove the translation component.
            var homogeneousZero = new Vector4(0, 0, 0, 1);
            worldToViewMatrix.SetColumn(3, homogeneousZero);

            // Flip the Z to make the coordinate system left-handed.
            worldToViewMatrix.SetRow(2, -worldToViewMatrix.GetRow(2));

            // Transpose for HLSL.
            return Matrix4x4.Transpose(worldToViewMatrix.transpose * viewSpaceRasterTransform);
        }

        /// <summary>
        /// Compute the matrix from screen space (pixel) to world space direction (RHS).
        ///
        /// You can use this matrix on the GPU to compute the direction to look in a cubemap for a specific
        /// screen pixel.
        /// </summary>
        /// <param name="resolution">The target texture resolution.</param>
        /// <param name="aspect">
        /// The aspect ratio to use.
        ///
        /// if negative, then the aspect ratio of <paramref name="resolution"/> will be used.
        ///
        /// It is different from the aspect ratio of <paramref name="resolution"/> for anamorphic projections.
        /// </param>
        /// <returns></returns>
        internal static Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(CameraData cameraData, Vector4 resolution)
        {
            var camera = cameraData.camera;
            SetCameraMatrices(cameraData, out var viewMatrix, out var cameraProj, out var viewProjMatrix, out var invViewProjMatrix);

            // In XR mode use a more generic matrix to account for asymmetry in the projection
            var useGenericMatrix = cameraData.xr.enabled;

            // Asymmetry is also possible from a user-provided projection, so we must check for it too.
            // Note however, that in case of physical camera, the lens shift term is the only source of
            // asymmetry, and this is accounted for in the optimized path below. Additionally, Unity C++ will
            // automatically disable physical camera when the projection is overridden by user.
            useGenericMatrix |= IsProjectionMatrixAsymmetric(cameraProj) && !camera.usePhysicalProperties;

            if (useGenericMatrix)
            {
                var viewSpaceRasterTransform = new Matrix4x4(
                    new Vector4(2.0f * resolution.z, 0.0f, 0.0f, -1.0f),
                    new Vector4(0.0f, -2.0f * resolution.w, 0.0f, 1.0f),
                    new Vector4(0.0f, 0.0f, 1.0f, 0.0f),
                    new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

                var transformT = invViewProjMatrix.transpose * Matrix4x4.Scale(new Vector3(-1.0f, -1.0f, -1.0f));
                return viewSpaceRasterTransform * transformT;
            }

            float verticalFoV = camera.GetGateFittedFieldOfView() * Mathf.Deg2Rad;
            if (!camera.usePhysicalProperties)
            {
                verticalFoV = Mathf.Atan(-1.0f / cameraProj[1, 1]) * 2;
            }
            Vector2 lensShift = camera.GetGateFittedLensShift();

            float aspect = ProjectionMatrixAspect(cameraProj);
            return ComputePixelCoordToWorldSpaceViewDirectionMatrix(verticalFoV, lensShift, resolution, viewMatrix, false, aspect, camera.orthographic);
        }

        /// <param name="aspect">
        /// The aspect ratio to use.
        /// if negative, then the aspect ratio of <paramref name="resolution"/> will be used.
        /// It is different from the aspect ratio of <paramref name="resolution"/> for anamorphic projections.
        /// </param>
        public static void GetPixelCoordToViewDirWS(CameraData cameraData, Vector4 resolution, ref Matrix4x4[] transforms)
        {
            // if (cameraData.xr.singlePassEnabled)
            // {
            //     for (int viewIndex = 0; viewIndex < viewCount; ++viewIndex)
            //     {
            //         transforms[viewIndex] = ComputePixelCoordToWorldSpaceViewDirectionMatrix(m_XRViewConstants[viewIndex], resolution, aspect);
            //     }
            // }
            // else
            // {
            transforms[0] = ComputePixelCoordToWorldSpaceViewDirectionMatrix(cameraData, resolution);
            // }
        }

        public static void GetPixelCoordToViewDirWS(CameraData cameraData, Vector4 resolution, ref Matrix4x4 transform)
        {
            transform = ComputePixelCoordToWorldSpaceViewDirectionMatrix(cameraData, resolution);
        }

        
#if ENABLE_URP_VOLUEMTRIC_FOG_RENDERGRAPH
        internal static void SetCameraMatrices(UniversalCameraData cameraData, out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out Matrix4x4 viewProjMatrix, out Matrix4x4 invViewProjMatrix)
        {
            var camera = cameraData.camera;
            viewMatrix = camera.worldToCameraMatrix;
            projMatrix = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true);
            viewProjMatrix = projMatrix * viewMatrix;
            invViewProjMatrix = viewProjMatrix.inverse;
        }

        internal static Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(UniversalCameraData cameraData, Vector4 resolution)
        {
            var camera = cameraData.camera;
            SetCameraMatrices(cameraData, out var viewMatrix, out var cameraProj, out var viewProjMatrix, out var invViewProjMatrix);

            // In XR mode use a more generic matrix to account for asymmetry in the projection
            var useGenericMatrix = cameraData.xr.enabled;

            // Asymmetry is also possible from a user-provided projection, so we must check for it too.
            // Note however, that in case of physical camera, the lens shift term is the only source of
            // asymmetry, and this is accounted for in the optimized path below. Additionally, Unity C++ will
            // automatically disable physical camera when the projection is overridden by user.
            useGenericMatrix |= IsProjectionMatrixAsymmetric(cameraProj) && !camera.usePhysicalProperties;

            if (useGenericMatrix)
            {
                var viewSpaceRasterTransform = new Matrix4x4(
                    new Vector4(2.0f * resolution.z, 0.0f, 0.0f, -1.0f),
                    new Vector4(0.0f, -2.0f * resolution.w, 0.0f, 1.0f),
                    new Vector4(0.0f, 0.0f, 1.0f, 0.0f),
                    new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

                var transformT = invViewProjMatrix.transpose * Matrix4x4.Scale(new Vector3(-1.0f, -1.0f, -1.0f));
                return viewSpaceRasterTransform * transformT;
            }

            float verticalFoV = camera.GetGateFittedFieldOfView() * Mathf.Deg2Rad;
            if (!camera.usePhysicalProperties)
            {
                verticalFoV = Mathf.Atan(-1.0f / cameraProj[1, 1]) * 2;
            }
            Vector2 lensShift = camera.GetGateFittedLensShift();

            float aspect = ProjectionMatrixAspect(cameraProj);
            return ComputePixelCoordToWorldSpaceViewDirectionMatrix(verticalFoV, lensShift, resolution, viewMatrix, false, aspect, camera.orthographic);
        }

        public static void GetPixelCoordToViewDirWS(UniversalCameraData cameraData, Vector4 resolution, ref Matrix4x4[] transforms)
        {
            transforms[0] = ComputePixelCoordToWorldSpaceViewDirectionMatrix(cameraData, resolution);
        }

        public static void GetPixelCoordToViewDirWS(UniversalCameraData cameraData, Vector4 resolution, ref Matrix4x4 transform)
        {
            transform = ComputePixelCoordToWorldSpaceViewDirectionMatrix(cameraData, resolution);
        }
#endif
    }
}