using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CityLife.World
{
    [RequireComponent(typeof(Renderer))]
    public sealed class CoastalPlanarReflection : MonoBehaviour
    {
        private Camera reflectionCamera;
        private RenderTexture reflectionTexture;
        private Material waterMaterial;
        private UniversalRenderPipeline.SingleCameraRequest request;
        private int renderedFrame = -1;
        private bool completedRender;
        private static bool rendering;
        public bool TextureReady => completedRender && reflectionTexture != null && reflectionTexture.IsCreated();
        public int LastRenderedFrame => renderedFrame;
        public RenderTexture CapturedTexture => TextureReady ? reflectionTexture : null;

        private void Awake()
        {
            waterMaterial = GetComponent<Renderer>().sharedMaterial;
            reflectionTexture = new RenderTexture(768, 384, 16, RenderTextureFormat.ARGB32)
            { name = "Starfall coastal planar reflection", useMipMap = false, autoGenerateMips = false };
            reflectionTexture.Create();
            var cameraObject = new GameObject("Coastal planar reflection camera") { hideFlags = HideFlags.HideAndDontSave };
            reflectionCamera = cameraObject.AddComponent<Camera>();
            reflectionCamera.enabled = false;
            reflectionCamera.targetTexture = reflectionTexture;
            request = new UniversalRenderPipeline.SingleCameraRequest { destination = reflectionTexture };
            waterMaterial.SetTexture("_PlanarReflectionTexture", reflectionTexture);
            waterMaterial.SetFloat("_PlanarReflectionAvailable", 0);
        }

        private void LateUpdate()
        {
            Camera source = Camera.main;
            if (rendering || source == null || reflectionCamera == null || renderedFrame == Time.frameCount) return;
            rendering = true;
            try
            {
                reflectionCamera.CopyFrom(source);
                reflectionCamera.enabled = false;
                reflectionCamera.targetTexture = reflectionTexture;
                reflectionCamera.cullingMask = source.cullingMask & ~(1 << 4);
                reflectionCamera.useOcclusionCulling = false;
                Vector3 position = source.transform.position; position.y = 2f * CoastalWater.Level - position.y;
                reflectionCamera.transform.position = position;
                Vector4 plane = new Vector4(0, 1, 0, -CoastalWater.Level);
                Matrix4x4 reflection = ReflectionMatrix(plane);
                reflectionCamera.worldToCameraMatrix = source.worldToCameraMatrix * reflection;
                Vector4 clipPlane = CameraSpacePlane(reflectionCamera, new Vector3(0, CoastalWater.Level, 0), Vector3.up, 1);
                reflectionCamera.projectionMatrix = source.CalculateObliqueMatrix(clipPlane);
                if (!RenderPipeline.SupportsRenderRequest(reflectionCamera, request))
                {
                    waterMaterial.SetFloat("_PlanarReflectionAvailable", 0);
                    return;
                }
                bool priorInvert = GL.invertCulling;
                try { GL.invertCulling = !priorInvert; RenderPipeline.SubmitRenderRequest(reflectionCamera, request); }
                finally { GL.invertCulling = priorInvert; }
                Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(reflectionCamera.projectionMatrix, true);
                waterMaterial.SetMatrix("_PlanarReflectionVP", gpuProjection * reflectionCamera.worldToCameraMatrix);
                waterMaterial.SetFloat("_PlanarReflectionAvailable", 1);
                completedRender = true;
                renderedFrame = Time.frameCount;
            }
            finally { rendering = false; }
        }

        private static Vector4 CameraSpacePlane(Camera camera, Vector3 point, Vector3 normal, float sideSign)
        {
            Vector3 offset = point + normal * .04f;
            Matrix4x4 matrix = camera.worldToCameraMatrix;
            Vector3 cameraPoint = matrix.MultiplyPoint(offset);
            Vector3 cameraNormal = matrix.MultiplyVector(normal).normalized * sideSign;
            return new Vector4(cameraNormal.x, cameraNormal.y, cameraNormal.z, -Vector3.Dot(cameraPoint, cameraNormal));
        }

        private static Matrix4x4 ReflectionMatrix(Vector4 plane)
        {
            Matrix4x4 m = Matrix4x4.identity;
            m.m00 = 1f - 2f * plane.x * plane.x; m.m01 = -2f * plane.x * plane.y; m.m02 = -2f * plane.x * plane.z; m.m03 = -2f * plane.w * plane.x;
            m.m10 = -2f * plane.y * plane.x; m.m11 = 1f - 2f * plane.y * plane.y; m.m12 = -2f * plane.y * plane.z; m.m13 = -2f * plane.w * plane.y;
            m.m20 = -2f * plane.z * plane.x; m.m21 = -2f * plane.z * plane.y; m.m22 = 1f - 2f * plane.z * plane.z; m.m23 = -2f * plane.w * plane.z;
            return m;
        }

        private void OnDestroy()
        {
            if (reflectionCamera != null) Destroy(reflectionCamera.gameObject);
            if (reflectionTexture != null) { reflectionTexture.Release(); Destroy(reflectionTexture); }
        }
    }
}
