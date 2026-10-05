using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.Items
{
    /// <summary>
    /// Deterministic runtime procedural mesh generator for the woven basket.
    /// Shared by fresh bootstrap and staged cold restore.
    /// Conforms strictly to authoritative catalog bounds (0.3 x 0.3 x 0.3m, 1.0kg, 4 slots).
    /// All geometry fits within [-0.145, 0.145] along X, Y, Z.
    /// Visual children contain no colliders or duplicate PhysicalItem components;
    /// the parent root owns the authoritative BoxCollider and Rigidbody.
    /// Owned meshes are explicitly disposed on destruction.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WovenBasketVisual : MonoBehaviour
    {
        public const string VisualGameObjectName = "Visual";
        public const string MeshName = "WovenBasketProceduralMesh";

        // Geometry bounds constants: strictly fits 0.30m x 0.30m x 0.30m
        public const float MaxExtentX = 0.145f;
        public const float MaxExtentY = 0.145f;
        public const float MaxExtentZ = 0.145f;

        [SerializeField] private Mesh generatedMesh;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;

        public Mesh GeneratedMesh => generatedMesh;

        public static GameObject CreateVisual(Transform parent, Material material, Vector3 dimensions)
        {
            var visualGo = new GameObject(VisualGameObjectName);
            visualGo.transform.SetParent(parent, false);
            visualGo.transform.localPosition = Vector3.zero;
            visualGo.transform.localRotation = Quaternion.identity;
            visualGo.transform.localScale = Vector3.one;

            var visualComp = visualGo.AddComponent<WovenBasketVisual>();
            visualComp.BuildMesh(material, dimensions);
            return visualGo;
        }

        public void BuildMesh(Material material, Vector3? maxDimensions = null)
        {
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();

            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();

            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
            }

            if (generatedMesh != null)
            {
                DisposeMesh();
            }

            Vector3 dims = maxDimensions ?? new Vector3(0.3f, 0.3f, 0.3f);
            float radiusBase = Mathf.Min(dims.x, dims.z) * 0.40f;
            float radiusTop = Mathf.Min(dims.x, dims.z) * 0.45f;
            float yBottom = -dims.y * 0.45f;
            float yRim = dims.y * 0.10f;
            float yHandlePeak = dims.y * 0.45f;

            generatedMesh = GenerateBasketMesh(radiusBase, radiusTop, yBottom, yRim, yHandlePeak);
            generatedMesh.name = MeshName;
            meshFilter.sharedMesh = generatedMesh;

            // Ensure no colliders exist on the visual object or its children
            var colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (Application.isPlaying) Destroy(colliders[i]);
                else DestroyImmediate(colliders[i]);
            }
        }

        private static Mesh GenerateBasketMesh(float rBase, float rTop, float yBase, float yRim, float yHandlePeak)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            void AddQuad(Vector3 v0, Vector3 v1, Vector2 uv0, Vector2 uv1, Vector3 v2, Vector3 v3, Vector2 uv2, Vector2 uv3)
            {
                int baseIdx = vertices.Count;
                vertices.Add(v0); vertices.Add(v1); vertices.Add(v2); vertices.Add(v3);
                Vector3 n0 = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                normals.Add(n0); normals.Add(n0); normals.Add(n0); normals.Add(n0);
                uvs.Add(uv0); uvs.Add(uv1); uvs.Add(uv2); uvs.Add(uv3);
                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 1);
                triangles.Add(baseIdx + 2);
                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 2);
                triangles.Add(baseIdx + 3);
            }

            void AddDoubleSidedQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
            {
                AddQuad(v0, v1, new Vector2(0, 0), new Vector2(1, 0), v2, v3, new Vector2(1, 1), new Vector2(0, 1));
                AddQuad(v3, v2, new Vector2(0, 1), new Vector2(1, 1), v1, v0, new Vector2(1, 0), new Vector2(0, 0));
            }

            const int RadialSegments = 16;
            // 1. Bottom disc
            int centerIdx = vertices.Count;
            vertices.Add(new Vector3(0, yBase, 0));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < RadialSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RadialSegments;
                vertices.Add(new Vector3(Mathf.Cos(angle) * rBase, yBase, Mathf.Sin(angle) * rBase));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(0.5f + Mathf.Cos(angle) * 0.5f, 0.5f + Mathf.Sin(angle) * 0.5f));
            }
            for (int i = 0; i < RadialSegments; i++)
            {
                int next = (i + 1) % RadialSegments;
                triangles.Add(centerIdx);
                triangles.Add(centerIdx + 1 + i);
                triangles.Add(centerIdx + 1 + next);
            }

            // 2. Vertical warp ribs
            for (int i = 0; i < RadialSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RadialSegments;
                float ribWidth = 0.012f;
                Vector3 radialDir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 tangentDir = new Vector3(-Mathf.Sin(angle), 0, Mathf.Cos(angle));

                Vector3 b0 = radialDir * rBase + tangentDir * (-ribWidth * 0.5f) + new Vector3(0, yBase, 0);
                Vector3 b1 = radialDir * rBase + tangentDir * (ribWidth * 0.5f) + new Vector3(0, yBase, 0);
                Vector3 t0 = radialDir * rTop + tangentDir * (-ribWidth * 0.5f) + new Vector3(0, yRim, 0);
                Vector3 t1 = radialDir * rTop + tangentDir * (ribWidth * 0.5f) + new Vector3(0, yRim, 0);

                AddDoubleSidedQuad(b0, b1, t1, t0);
            }

            // 3. Horizontal weft strands with undulating weave
            const int HorizontalBands = 5;
            for (int h = 0; h < HorizontalBands; h++)
            {
                float tH = (h + 0.5f) / HorizontalBands;
                float yBand = Mathf.Lerp(yBase + 0.01f, yRim - 0.01f, tH);
                float bandHeight = (yRim - yBase) / (HorizontalBands * 1.5f);
                float rBandNominal = Mathf.Lerp(rBase, rTop, tH);
                float phase = (h % 2 == 0) ? 0f : Mathf.PI;

                for (int i = 0; i < RadialSegments; i++)
                {
                    int next = (i + 1) % RadialSegments;
                    float a0 = i * Mathf.PI * 2f / RadialSegments;
                    float a1 = next * Mathf.PI * 2f / RadialSegments;

                    float wave0 = Mathf.Sin(i * Mathf.PI + phase) * 0.007f;
                    float wave1 = Mathf.Sin(next * Mathf.PI + phase) * 0.007f;

                    float r0 = rBandNominal + wave0;
                    float r1 = rBandNominal + wave1;

                    Vector3 p0Bottom = new Vector3(Mathf.Cos(a0) * r0, yBand - bandHeight * 0.5f, Mathf.Sin(a0) * r0);
                    Vector3 p1Bottom = new Vector3(Mathf.Cos(a1) * r1, yBand - bandHeight * 0.5f, Mathf.Sin(a1) * r1);
                    Vector3 p1Top = new Vector3(Mathf.Cos(a1) * r1, yBand + bandHeight * 0.5f, Mathf.Sin(a1) * r1);
                    Vector3 p0Top = new Vector3(Mathf.Cos(a0) * r0, yBand + bandHeight * 0.5f, Mathf.Sin(a0) * r0);

                    AddDoubleSidedQuad(p0Bottom, p1Bottom, p1Top, p0Top);
                }
            }

            // 4. Top Rim ring
            float rimThickness = 0.011f;
            for (int i = 0; i < RadialSegments; i++)
            {
                int next = (i + 1) % RadialSegments;
                float a0 = i * Mathf.PI * 2f / RadialSegments;
                float a1 = next * Mathf.PI * 2f / RadialSegments;

                Vector3 out0 = new Vector3(Mathf.Cos(a0) * (rTop + rimThickness), yRim, Mathf.Sin(a0) * (rTop + rimThickness));
                Vector3 out1 = new Vector3(Mathf.Cos(a1) * (rTop + rimThickness), yRim, Mathf.Sin(a1) * (rTop + rimThickness));
                Vector3 in1 = new Vector3(Mathf.Cos(a1) * (rTop - rimThickness), yRim, Mathf.Sin(a1) * (rTop - rimThickness));
                Vector3 in0 = new Vector3(Mathf.Cos(a0) * (rTop - rimThickness), yRim, Mathf.Sin(a0) * (rTop - rimThickness));

                AddDoubleSidedQuad(out0, out1, in1, in0);
            }

            // 5. Arched Handle / Grip spanning across top
            const int HandleSteps = 12;
            float handleHalfWidth = 0.014f;
            float handleArcRadius = rTop * 0.92f;
            for (int s = 0; s < HandleSteps; s++)
            {
                float t0 = (float)s / HandleSteps;
                float t1 = (float)(s + 1) / HandleSteps;

                float theta0 = Mathf.PI * (1f - t0);
                float theta1 = Mathf.PI * (1f - t1);

                float x0 = Mathf.Cos(theta0) * handleArcRadius;
                float y0 = yRim + Mathf.Sin(theta0) * (yHandlePeak - yRim);
                float x1 = Mathf.Cos(theta1) * handleArcRadius;
                float y1 = yRim + Mathf.Sin(theta1) * (yHandlePeak - yRim);

                Vector3 v0 = new Vector3(x0, y0, -handleHalfWidth);
                Vector3 v1 = new Vector3(x0, y0, handleHalfWidth);
                Vector3 v2 = new Vector3(x1, y1, handleHalfWidth);
                Vector3 v3 = new Vector3(x1, y1, -handleHalfWidth);

                AddDoubleSidedQuad(v0, v1, v2, v3);
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        private void OnDestroy()
        {
            DisposeMesh();
        }

        private void DisposeMesh()
        {
            if (generatedMesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(generatedMesh);
                }
                else
                {
                    DestroyImmediate(generatedMesh);
                }
                generatedMesh = null;
            }
        }
    }
}
