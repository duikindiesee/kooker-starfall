using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityLife.World
{
    /// <summary>
    /// Bounded, distance-adaptive rendering of the immutable procedural field. All three mesh
    /// resolutions are cached per chunk: revisiting a place never allocates another terrain mesh.
    /// Gameplay and saved edits remain independent of these visual meshes.
    /// </summary>
    public sealed class IslandRenderer : MonoBehaviour
    {
        sealed class Chunk
        {
            public int X, Z, Step = 16, Wanted = 16;
            public Vector3 Centre;
            public Bounds Bounds;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public readonly Mesh[] Meshes = new Mesh[3];
            public readonly int[] Triangles = new int[3];
            public readonly List<FloraBatch> Flora = new List<FloraBatch>(4);
            public float Distance;
        }

        sealed class FloraBatch
        {
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public int Count, Triangles;
            public float Range;
        }

        IslandField field;
        Camera view;
        Chunk[] chunks;
        Material terrainMaterial, treeMaterial, rockMaterial, floraMaterial, grassMaterial, oceanMaterial, skyMaterial;
        Mesh treeMesh, rockMesh, floraMesh, grassMesh, oceanMesh;
        Texture2D heightTexture;
        Light sun;
        readonly Plane[] frustum = new Plane[6];
        float nextDistanceUpdate;
        float detailFocusDistanceSquared = float.PositiveInfinity;
        bool initialized;

        public int VisibleChunks { get; private set; }
        public int HighDetailChunks { get; private set; }
        public int MediumDetailChunks { get; private set; }
        public int ResidentMeshes { get; private set; }
        public int RenderedTriangles { get; private set; }
        public int FloraInstances { get; private set; }
        public int TotalFloraInstances { get; private set; }
        public bool IsNight { get; private set; }
        public Vector3 DetailFocus { get; private set; }

        public void Initialize(IslandField island, Camera camera)
        {
            if (initialized) throw new InvalidOperationException("IslandRenderer.Initialize may only run once.");
            field = island;
            view = camera;
            DetailFocus = field.Landing;
            terrainMaterial = MakeMaterial("CityLife/IslandTerrain", "CityLife sand and stone");
            // Definition.seaLevel is a normalized generation threshold. World-space sea is zero.
            terrainMaterial.SetFloat("_SeaLevel", 0f);
            var groundAlbedo = Resources.Load<Texture2D>("CityLifeArt/DryGround_1K");
            if (groundAlbedo != null)
            {
                groundAlbedo.wrapMode = TextureWrapMode.Repeat;
                groundAlbedo.filterMode = FilterMode.Trilinear;
                groundAlbedo.anisoLevel = 4;
                terrainMaterial.SetTexture("_GroundAlbedo", groundAlbedo);
                terrainMaterial.SetFloat("_GroundTextureStrength", .64f);
                terrainMaterial.SetFloat("_GroundTiling", 1f / 3f);
            }
            treeMaterial = MakeMaterial("CityLife/IslandTerrain", "Kokerboom bark and succulent rosettes");
            treeMaterial.SetFloat("_FadeStart", 1300f);
            treeMaterial.SetFloat("_FadeEnd", 1550f);
            rockMaterial = MakeMaterial("CityLife/IslandTerrain", "Weathered desert stones");
            rockMaterial.SetFloat("_FadeStart", 760f);
            rockMaterial.SetFloat("_FadeEnd", 1000f);
            floraMaterial = MakeMaterial("CityLife/IslandTerrain", "Sparse bioluminescent flora");
            floraMaterial.SetFloat("_Emission", 0.2f);
            floraMaterial.SetFloat("_FadeStart", 650f);
            floraMaterial.SetFloat("_FadeEnd", 850f);
            grassMaterial = MakeMaterial("CityLife/IslandTerrain", "Procedural dry grass tufts");
            grassMaterial.SetFloat("_FadeStart", 180f);
            grassMaterial.SetFloat("_FadeEnd", 260f);
            treeMesh = IslandFloraGeometry.QuiverTree();
            rockMesh = IslandFloraGeometry.Rock();
            floraMesh = IslandFloraGeometry.NeonPlant();
            grassMesh = IslandFloraGeometry.DryGrass();
            BuildAtmosphere();
            BuildOcean();

            int count = field.Definition.cells / field.Definition.chunkCells;
            chunks = new Chunk[count * count];
            for (int z = 0; z < count; z++)
            for (int x = 0; x < count; x++)
            {
                var go = new GameObject($"Terrain [{x:D2},{z:D2}]");
                go.transform.SetParent(transform, false);
                var chunk = new Chunk { X = x, Z = z, Filter = go.AddComponent<MeshFilter>(), Renderer = go.AddComponent<MeshRenderer>() };
                chunk.Renderer.sharedMaterial = terrainMaterial;
                chunk.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                chunk.Renderer.receiveShadows = true;
                float span = (float)(field.Definition.chunkCells * field.Definition.cellMetres);
                chunk.Centre = new Vector3((x + 0.5f) * span - field.Definition.Width * 0.5f, 0, (z + 0.5f) * span - field.Definition.Width * 0.5f);
                chunk.Bounds = new Bounds(chunk.Centre + Vector3.up * 100f, new Vector3(span + 32f, 800f, span + 32f));
                chunks[z * count + x] = chunk;
                ApplyResolution(chunk, 16);
                ScatterFlora(chunk);
            }
            initialized = true;
            UpdateDistances();
            // The first camera view is fully resolved before the first frame, avoiding a visible
            // low-detail loading patch under the player. Later moves perform at most two upgrades.
            for (int i = 0; i < chunks.Length; i++)
                if (chunks[i].Wanted != 16) ApplyResolution(chunks[i], chunks[i].Wanted);
            UpdateStreaming();
        }

        static Material MakeMaterial(string shader, string label)
        {
            Shader found = Shader.Find(shader);
            if (found == null) throw new InvalidOperationException($"Required shader missing: {shader}. Include it in the build settings.");
            return new Material(found) { name = label, enableInstancing = true };
        }

        public void UpdateStreaming()
        {
            if (!initialized || !view) return;
            if (Time.unscaledTime >= nextDistanceUpdate)
            {
                UpdateDistances();
                nextDistanceUpdate = Time.unscaledTime + 0.2f;
            }
            // Select nearest pending changes without allocating a sorting collection each frame.
            for (int update = 0; update < 2; update++)
            {
                Chunk best = null;
                float bestDistance = float.PositiveInfinity;
                foreach (Chunk chunk in chunks)
                    if (chunk.Wanted != chunk.Step && chunk.Distance < bestDistance)
                    { best = chunk; bestDistance = chunk.Distance; }
                if (best == null) break;
                ApplyResolution(best, best.Wanted);
            }
            GeometryUtility.CalculateFrustumPlanes(view, frustum);
            VisibleChunks = HighDetailChunks = MediumDetailChunks = RenderedTriangles = FloraInstances = 0;
            foreach (Chunk chunk in chunks)
            {
                if (chunk.Step == 1) HighDetailChunks++;
                if (chunk.Step == 4) MediumDetailChunks++;
                bool visible = GeometryUtility.TestPlanesAABB(frustum, chunk.Bounds);
                if (visible)
                {
                    VisibleChunks++;
                    RenderedTriangles += chunk.Triangles[Level(chunk.Step)];
                }
                foreach (FloraBatch batch in chunk.Flora)
                {
                    bool active = visible && chunk.Distance < batch.Range;
                    batch.Renderer.enabled = active;
                    if (!active) continue;
                    FloraInstances += batch.Count;
                    RenderedTriangles += batch.Triangles;
                }
            }
        }

        void LateUpdate() => UpdateStreaming();

        void UpdateDistances()
        {
            Vector3 p = view.transform.position;
            foreach (Chunk chunk in chunks)
            {
                float dx = p.x - chunk.Centre.x, dz = p.z - chunk.Centre.z;
                chunk.Distance = Mathf.Sqrt(dx * dx + dz * dz);
                // Height-aware detail prevents a survey view paying for unseen four-metre detail.
                float vertical = Mathf.Max(0f, p.y - field.Ground(chunk.Centre.x, chunk.Centre.z));
                float distance = Mathf.Sqrt(chunk.Distance * chunk.Distance + vertical * vertical);
                // Hysteresis prevents a player on a threshold from repeatedly changing levels.
                float close = chunk.Step == 1 ? 580f : 480f;
                float middle = chunk.Step <= 4 ? 1450f : 1280f;
                chunk.Wanted = distance < close ? 1 : distance < middle ? 4 : 16;
            }
        }

        static int Level(int step) => step == 1 ? 0 : step == 4 ? 1 : 2;

        void ApplyResolution(Chunk chunk, int step)
        {
            int level = Level(step);
            if (chunk.Meshes[level] == null)
            {
                chunk.Meshes[level] = BuildChunk(chunk.X, chunk.Z, step, out int triangles);
                chunk.Triangles[level] = triangles;
                ResidentMeshes++;
            }
            chunk.Filter.sharedMesh = chunk.Meshes[level];
            chunk.Step = step;
        }

        Mesh BuildChunk(int cx, int cz, int step, out int triangleCount)
        {
            int size = field.Definition.chunkCells;
            int side = size / step + 1;
            int x0 = cx * size, z0 = cz * size;
            int surfaceCount = side * side;
            int skirtCount = side * 4;
            var vertices = new Vector3[surfaceCount + skirtCount];
            var normals = new Vector3[vertices.Length];
            var colors = new Color[vertices.Length];
            var indices = new int[(side - 1) * (side - 1) * 6 + (side - 1) * 24];
            int i = 0;
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++, i++)
            {
                int gx = x0 + x * step, gz = z0 + z * step;
                vertices[i] = field.Position(gx, gz, field.Height(gx, gz));
                normals[i] = field.Normal(gx, gz);
                colors[i] = field.ColorAt(gx, gz);
            }
            int t = 0;
            for (int z = 0; z < side - 1; z++)
            for (int x = 0; x < side - 1; x++)
            {
                int a = z * side + x, b = a + 1, c = a + side, d = c + 1;
                indices[t++] = a; indices[t++] = c; indices[t++] = b;
                indices[t++] = b; indices[t++] = c; indices[t++] = d;
            }
            // All levels sample exactly the same global vertices/normals. Downward skirts hide
            // T-junctions between levels; they are visual only and never participate in grounding.
            float depth = Mathf.Max(64f, (float)field.Definition.cellMetres * 20f);
            for (int edge = 0; edge < 4; edge++)
            for (int j = 0; j < side; j++)
            {
                int source = EdgeIndex(edge, j, side);
                int target = surfaceCount + edge * side + j;
                vertices[target] = vertices[source] - Vector3.up * depth;
                normals[target] = normals[source];
                colors[target] = colors[source];
                if (j == side - 1) continue;
                int next = EdgeIndex(edge, j + 1, side);
                indices[t++] = source; indices[t++] = target; indices[t++] = next;
                indices[t++] = next; indices[t++] = target; indices[t++] = target + 1;
            }
            var mesh = new Mesh { name = $"Island chunk {cx},{cz} / {step * field.Definition.cellMetres:0}m" };
            mesh.vertices = vertices; mesh.normals = normals; mesh.colors = colors; mesh.triangles = indices;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            triangleCount = indices.Length / 3;
            return mesh;
        }

        static int EdgeIndex(int edge, int j, int side)
        {
            switch (edge)
            {
                case 0: return j;
                case 1: return j * side + side - 1;
                case 2: return side * side - 1 - j;
                default: return (side - 1 - j) * side;
            }
        }

        void ScatterFlora(Chunk chunk)
        {
            var trees = new List<Matrix4x4>(16);
            var rocks = new List<Matrix4x4>(24);
            var flora = new List<Matrix4x4>(16);
            var grass = new List<Matrix4x4>(128);
            int size = field.Definition.chunkCells;
            int stride = Math.Max(2, (int)Math.Round(24.0 / field.Definition.cellMetres));
            float half = field.Definition.Width * 0.5f;
            for (int z = chunk.Z * size; z < (chunk.Z + 1) * size; z += stride)
            for (int x = chunk.X * size; x < (chunk.X + 1) * size; x += stride)
            {
                uint h = Hash((uint)(x * 73856093 ^ z * 19349663 ^ field.Definition.seed));
                float wx = (float)((x + Unit(h) * stride) * field.Definition.cellMetres) - half;
                float wz = (float)((z + Unit(Hash(h + 1)) * stride) * field.Definition.cellMetres) - half;
                float y = field.Ground(wx, wz);
                if (y < 3f) continue;
                Vector3 normal = field.Normal(x, z);
                byte biome = field.Biomes[z * field.Stride + x];
                if (normal.y < 0.70f) continue;
                float r = Unit(Hash(h + 2));
                Quaternion yaw = Quaternion.Euler(0, Unit(Hash(h + 3)) * 360f, 0);
                Vector3 p = new Vector3(wx, y - 0.12f, wz);
                if (biome != (byte)Biome.Forest && y > 8f && r < 0.073f)
                {
                    float age = Unit(Hash(h + 4));
                    float height = 4.5f + age * age * 10f;
                    trees.Add(Matrix4x4.TRS(p, yaw, new Vector3(height * 0.70f, height, height * 0.70f)));
                }
                else if (r > 0.81f && normal.y < 0.97f)
                {
                    float scale = 0.8f + Unit(Hash(h + 5)) * 3f;
                    rocks.Add(Matrix4x4.TRS(p, yaw, new Vector3(scale * 1.3f, scale * 0.65f, scale)));
                }
                else if ((biome == (byte)Biome.Forest && r < 0.33f) || r > 0.995f)
                {
                    float scale = 0.9f + Unit(Hash(h + 6)) * 2.1f;
                    flora.Add(Matrix4x4.TRS(p, yaw, new Vector3(scale, scale, scale)));
                }
            }
            // Fine arid ground cover is a separate decorative layer; it never changes terrain,
            // biome classification, world fingerprint, or saved edits. Coordinate hashes make
            // both sparse grass and smaller stones independent of frame/travel order.
            int detailStride = Math.Max(2, (int)Math.Round(12.0 / field.Definition.cellMetres));
            for (int z = chunk.Z * size; z < (chunk.Z + 1) * size; z += detailStride)
            for (int x = chunk.X * size; x < (chunk.X + 1) * size; x += detailStride)
            {
                uint h = Hash((uint)(x * 73856093 ^ z * 19349663 ^ field.Definition.seed ^ 0x53a76));
                float jx = x + Unit(h) * Math.Min(detailStride, (chunk.X + 1) * size - x);
                float jz = z + Unit(Hash(h + 1)) * Math.Min(detailStride, (chunk.Z + 1) * size - z);
                float wx = (float)(jx * field.Definition.cellMetres) - half;
                float wz = (float)(jz * field.Definition.cellMetres) - half;
                float y = field.Ground(wx, wz);
                int gx = Mathf.Clamp(Mathf.RoundToInt(jx), 0, field.Definition.cells);
                int gz = Mathf.Clamp(Mathf.RoundToInt(jz), 0, field.Definition.cells);
                if (y < 2.2f || field.Normal(gx, gz).y < .78f) continue;
                byte biome = field.Biomes[gz * field.Stride + gx];
                float r = Unit(Hash(h + 2));
                Quaternion yaw = Quaternion.Euler(0, Unit(Hash(h + 3)) * 360f, 0);
                Vector3 p = new Vector3(wx, y + .015f, wz);
                float density = biome == (byte)Biome.Forest ? .48f : biome == (byte)Biome.Beach ? .10f : .31f;
                if (r < density)
                {
                    float height = .30f + Unit(Hash(h + 4)) * .55f;
                    grass.Add(Matrix4x4.TRS(p, yaw, new Vector3(height * 1.1f, height, height * 1.1f)));
                    float dx = p.x - field.Landing.x, dz = p.z - field.Landing.z;
                    float distanceSquared = dx * dx + dz * dz;
                    if (distanceSquared < detailFocusDistanceSquared)
                    {
                        detailFocusDistanceSquared = distanceSquared;
                        DetailFocus = p;
                    }
                }
                else if (r > .90f)
                {
                    float scale = .22f + Unit(Hash(h + 5)) * .65f;
                    rocks.Add(Matrix4x4.TRS(p - Vector3.up * .10f, yaw, new Vector3(scale * 1.2f, scale * .68f, scale)));
                }
            }
            BuildFloraBatch(chunk, "Kokerbome", treeMesh, treeMaterial, trees, 1750f);
            BuildFloraBatch(chunk, "Desert stones", rockMesh, rockMaterial, rocks, 1200f);
            BuildFloraBatch(chunk, "Neon hollow flora", floraMesh, floraMaterial, flora, 1050f);
            BuildFloraBatch(chunk, "Dry grass", grassMesh, grassMaterial, grass, 460f);
        }

        void BuildFloraBatch(Chunk chunk, string name, Mesh source, Material material, List<Matrix4x4> matrices, float range)
        {
            if (matrices.Count == 0) return;
            var combine = new CombineInstance[matrices.Count];
            for (int i = 0; i < combine.Length; i++) combine[i] = new CombineInstance { mesh = source, transform = matrices[i] };
            var mesh = new Mesh { name = $"{name} [{chunk.X},{chunk.Z}]" };
            if ((long)source.vertexCount * matrices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.CombineMeshes(combine, true, true);
            mesh.RecalculateBounds();
            int triangles = (int)mesh.GetIndexCount(0) / 3;
            mesh.UploadMeshData(true);
            var go = new GameObject(mesh.name);
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            // Persistent scene renderers participate in both regular and SingleCameraRequest
            // rendering. This avoids a transient camera-scoped DrawMeshInstanced queue whose
            // submitted count did not prove that plants appeared in offscreen captures.
            chunk.Flora.Add(new FloraBatch { Mesh = mesh, Renderer = renderer, Count = matrices.Count, Triangles = triangles, Range = range });
            TotalFloraInstances += matrices.Count;
        }

        public static uint Hash(uint x)
        {
            unchecked { x ^= x >> 16; x *= 0x85ebca6b; x ^= x >> 13; x *= 0xc2b2ae35; return x ^ (x >> 16); }
        }
        static float Unit(uint value) => (value & 0x00ffffff) / 16777216f;

        void BuildOcean()
        {
            heightTexture = new Texture2D(field.Stride, field.Stride, TextureFormat.RFloat, false, true)
            { name = "Immutable island bathymetry", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float[] bathymetry = field.Heights;
            if (field.Edits.Count > 0)
            {
                bathymetry = (float[])field.Heights.Clone();
                foreach (var edit in field.Edits) bathymetry[edit.Key] += edit.Value;
            }
            heightTexture.SetPixelData(bathymetry, 0);
            heightTexture.Apply(false, true);
            oceanMaterial = MakeMaterial("CityLife/IslandOcean", "CityLife turquoise GPU ocean");
            oceanMaterial.SetTexture("_HeightMap", heightTexture);
            oceanMaterial.SetFloat("_WorldWidth", field.Definition.Width);
            oceanMaterial.SetFloat("_SeaLevel", 0f);
            int side = 129;
            int innerCount = side * side;
            int perimeterCount = (side - 1) * 4;
            var vertices = new Vector3[innerCount + perimeterCount];
            var indices = new int[(side - 1) * (side - 1) * 6 + perimeterCount * 6];
            float width = field.Definition.Width * 3f;
            // Preserve the original inner mesh density. A coarse outer apron moves the square
            // edge beyond the explorer's flight/orbit limits and 3-world-width far clip without
            // multiplying the whole ocean's vertex count. It changes no terrain/world data.
            float outerWidth = field.Definition.Width * 24f;
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
                vertices[z * side + x] = new Vector3((x / (float)(side - 1) - .5f) * width, 0f, (z / (float)(side - 1) - .5f) * width);
            int t = 0;
            for (int z = 0; z < side - 1; z++)
            for (int x = 0; x < side - 1; x++)
            {
                int a = z * side + x, c = a + side;
                indices[t++] = a; indices[t++] = c; indices[t++] = a + 1;
                indices[t++] = a + 1; indices[t++] = c; indices[t++] = c + 1;
            }
            for (int k = 0; k < perimeterCount; k++)
            {
                int edge = k / (side - 1), along = k % (side - 1);
                int a = EdgeIndex(edge, along, side);
                vertices[innerCount + k] = vertices[a] * (outerWidth / width);
            }
            for (int k = 0; k < perimeterCount; k++)
            {
                int next = (k + 1) % perimeterCount;
                int a = EdgeIndex(k / (side - 1), k % (side - 1), side);
                int b = EdgeIndex(next / (side - 1), next % (side - 1), side);
                int outerA = innerCount + k, outerB = innerCount + next;
                indices[t++] = a; indices[t++] = b; indices[t++] = outerA;
                indices[t++] = b; indices[t++] = outerB; indices[t++] = outerA;
            }
            oceanMesh = new Mesh { name = "Ocean GPU wave surface", vertices = vertices, triangles = indices };
            oceanMesh.RecalculateBounds();
            oceanMesh.bounds = new Bounds(Vector3.zero, new Vector3(outerWidth, 2f, outerWidth));
            oceanMesh.UploadMeshData(true);
            var ocean = new GameObject("Turquoise ocean · original CityLife wave field");
            ocean.transform.SetParent(transform, false);
            ocean.AddComponent<MeshFilter>().sharedMesh = oceanMesh;
            var renderer = ocean.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = oceanMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        void BuildAtmosphere()
        {
            var lightObject = new GameObject("CityLife sun");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.rotation = Quaternion.Euler(34f, -42f, 0);
            sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.None;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            skyMaterial = MakeMaterial("CityLife/IslandSky", "CityLife atmosphere and distant blue witness");
            RenderSettings.skybox = skyMaterial;
            skyMaterial.SetVector("_SunDirection", -sun.transform.forward);
            SetNight(false);
        }

        public void SetNight(bool night)
        {
            IsNight = night;
            if (!sun) return;
            sun.color = night ? new Color(0.56f, 0.68f, 1f) : new Color(1f, 0.88f, 0.69f);
            sun.intensity = night ? 0.40f : 1.13f;
            RenderSettings.ambientLight = night ? new Color(0.105f, 0.13f, 0.23f) : new Color(0.50f, 0.58f, 0.65f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = night ? 0.00011f : 0.000115f;
            RenderSettings.fogColor = night ? new Color(0.07f, 0.105f, 0.19f) : new Color(0.65f, 0.77f, 0.79f);
            skyMaterial.SetFloat("_Night", night ? 1f : 0f);
            Shader.SetGlobalColor("_CityLifeAmbient", night ? new Color(0.14f, 0.18f, 0.28f) : new Color(0.52f, 0.58f, 0.61f));
            if (floraMaterial) floraMaterial.SetFloat("_Emission", night ? 0.8f : 0.17f);
            if (oceanMaterial) oceanMaterial.SetFloat("_Night", night ? 1f : 0f);
        }

        void OnDestroy()
        {
            if (chunks != null) foreach (Chunk chunk in chunks)
            {
                foreach (Mesh mesh in chunk.Meshes) if (mesh) Destroy(mesh);
                foreach (FloraBatch batch in chunk.Flora) if (batch.Mesh) Destroy(batch.Mesh);
            }
            if (treeMesh) Destroy(treeMesh); if (rockMesh) Destroy(rockMesh); if (floraMesh) Destroy(floraMesh); if (grassMesh) Destroy(grassMesh); if (oceanMesh) Destroy(oceanMesh);
            if (heightTexture) Destroy(heightTexture);
            foreach (Material material in new[] { terrainMaterial, treeMaterial, rockMaterial, floraMaterial, grassMaterial, oceanMaterial, skyMaterial }) if (material) Destroy(material);
        }
    }
}
