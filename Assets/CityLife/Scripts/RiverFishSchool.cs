using System;
using System.Collections.Generic;
using UnityEngine;
using CityLife.Items;

namespace CityLife.World
{
    /// <summary>
    /// Autonomous river fish ecology swimming in the clear freshwater canyon channel.
    /// Fish navigate shallow river runs and deep meander pools, reacting to currents
    /// and offering catchable high-protein sustenance for the inhabitant.
    /// </summary>
    public sealed class RiverFishSchool : MonoBehaviour
    {
        public const int DefaultFishCount = 16;
        public const float NutritionProtein = 2500f;
        public const float NutritionSatiety = 2000f;
        public const float DriveReductionEndorphins = 2200f;

        private static Mesh sharedBarberMesh;
        private static Material sharedBarberMaterial;
        private static Mesh sharedCarpMesh;
        private static Material sharedCarpMaterial;

        public static Mesh SharedBarberMesh
        {
            get => GetOrCreateBarberMesh();
            set => sharedBarberMesh = value;
        }

        public static Material SharedBarberMaterial
        {
            get => GetOrCreateBarberMaterial();
            set => sharedBarberMaterial = value;
        }

        public static Mesh SharedCarpMesh
        {
            get => GetOrCreateCarpMesh();
            set => sharedCarpMesh = value;
        }

        public static Material SharedCarpMaterial
        {
            get => GetOrCreateCarpMaterial();
            set => sharedCarpMaterial = value;
        }

        public static Mesh GetOrCreateBarberMesh()
        {
            if (sharedBarberMesh != null) return sharedBarberMesh;
#if UNITY_EDITOR
            var fbxPath = "Assets/CityLife/Art/Catfish/catfish_swim.fbx";
            var meshes = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (var obj in meshes)
            {
                if (obj is Mesh m && m.name.Contains("Catfish"))
                {
                    sharedBarberMesh = m;
                    return sharedBarberMesh;
                }
            }
#endif
            var smr = UnityEngine.Object.FindAnyObjectByType<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null && smr.sharedMesh.name.Contains("Catfish"))
            {
                sharedBarberMesh = smr.sharedMesh;
                return sharedBarberMesh;
            }
            return CreateProceduralFishMesh();
        }

        public static Material GetOrCreateBarberMaterial()
        {
            if (sharedBarberMaterial != null) return sharedBarberMaterial;
#if UNITY_EDITOR
            var matPath = "Assets/CityLife/Art/Catfish/Catfish_Material.mat";
            var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat != null)
            {
                sharedBarberMaterial = mat;
                return sharedBarberMaterial;
            }
#endif
            var smr = UnityEngine.Object.FindAnyObjectByType<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMaterial != null && smr.sharedMaterial.name.Contains("Catfish"))
            {
                sharedBarberMaterial = smr.sharedMaterial;
                return sharedBarberMaterial;
            }
            return CreateFishMaterial();
        }

        public static Mesh GetOrCreateCarpMesh()
        {
            if (sharedCarpMesh != null) return sharedCarpMesh;
#if UNITY_EDITOR
            var fbxPath = "Assets/CityLife/Art/Carp/carp_swim.fbx";
            var meshes = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (var obj in meshes)
            {
                if (obj is Mesh m && (m.name.Contains("Carp") || m.name.Contains("Mesh")))
                {
                    sharedCarpMesh = m;
                    return sharedCarpMesh;
                }
            }
#endif
            var smrs = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            foreach (var smr in smrs)
            {
                if (smr != null && smr.sharedMesh != null && (smr.sharedMesh.name.Contains("Carp") || smr.sharedMesh.name.Contains("Mesh")))
                {
                    sharedCarpMesh = smr.sharedMesh;
                    return sharedCarpMesh;
                }
            }
            return CreateProceduralFishMesh();
        }

        public static Material GetOrCreateCarpMaterial()
        {
            if (sharedCarpMaterial != null) return sharedCarpMaterial;
#if UNITY_EDITOR
            var matPath = "Assets/CityLife/Art/Carp/Carp_Material.mat";
            var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat != null)
            {
                sharedCarpMaterial = mat;
                return sharedCarpMaterial;
            }
#endif
            var smrs = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            foreach (var smr in smrs)
            {
                if (smr != null && smr.sharedMaterial != null && smr.sharedMaterial.name.Contains("Carp"))
                {
                    sharedCarpMaterial = smr.sharedMaterial;
                    return sharedCarpMaterial;
                }
            }
            return CreateFishMaterial();
        }

        private void Awake()
        {
            if (sharedBarberMesh == null || sharedBarberMaterial == null || sharedCarpMesh == null || sharedCarpMaterial == null)
            {
                var smrs = GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in smrs)
                {
                    if (smr == null) continue;
                    if (sharedBarberMesh == null && smr.sharedMesh != null && smr.sharedMesh.name.Contains("Catfish")) sharedBarberMesh = smr.sharedMesh;
                    if (sharedBarberMaterial == null && smr.sharedMaterial != null && smr.sharedMaterial.name.Contains("Catfish")) sharedBarberMaterial = smr.sharedMaterial;
                    if (sharedCarpMesh == null && smr.sharedMesh != null && (smr.sharedMesh.name.Contains("Carp") || smr.sharedMesh.name.Contains("Mesh"))) sharedCarpMesh = smr.sharedMesh;
                    if (sharedCarpMaterial == null && smr.sharedMaterial != null && smr.sharedMaterial.name.Contains("Carp")) sharedCarpMaterial = smr.sharedMaterial;
                }
            }
        }

        [Serializable]
        public class RiverFishInstance
        {
            public GameObject gameObject;
            public Vector3 swimCenter;
            public float swimRadius;
            public float swimSpeed;
            public float phase;
            public float swimDepth;
            public float scale;
            public Transform bodyTransform;
            public NpcInteractable interactable;
            public PhysicalItem physicalItem;
            public Animation animation;
        }

        public readonly List<RiverFishInstance> ActiveFish = new List<RiverFishInstance>();

        private void Update()
        {
            float t = Time.time;
            for (int i = 0; i < ActiveFish.Count; i++)
            {
                var fish = ActiveFish[i];
                if (fish == null || fish.gameObject == null || !fish.gameObject.activeSelf) continue;

                // Circular / elliptical orbit around local pool center
                float angle = t * fish.swimSpeed + fish.phase;
                float currentX = fish.swimCenter.x + Mathf.Cos(angle) * fish.swimRadius;
                float currentZ = fish.swimCenter.z + Mathf.Sin(angle) * (fish.swimRadius * 1.8f);

                // River cruising depth: strictly bound entire animated body and fins
                // between the riverbed substrate and the water surface.
                float groundY = CoastalTerrain.Height(currentX, currentZ);
                float waterY = CoastalWater.Level; // -2.0f

                bool isCarp = fish.interactable != null && fish.interactable.StableId.Contains("carp");
                // Base mesh extents from pivot:
                // Carp: Dorsal fin apex = +0.36m, Pelvic fin nadir = -0.35m
                // Catfish: Dorsal fin apex = +0.14m, Pelvic/pectoral nadir = -0.14m
                float hTop = (isCarp ? 0.36f : 0.14f) * fish.scale;
                float hBottom = (isCarp ? 0.35f : 0.14f) * fish.scale;

                // Lateral bank / shallows containment: if local water depth cannot contain fish,
                // nudge orbit inward toward deeper pool center
                float localDepth = waterY - groundY;
                float minRequiredDepth = hTop + hBottom + 0.10f;
                if (localDepth < minRequiredDepth)
                {
                    currentX = Mathf.Lerp(currentX, fish.swimCenter.x, 0.5f);
                    currentZ = Mathf.Lerp(currentZ, fish.swimCenter.z, 0.5f);
                    groundY = CoastalTerrain.Height(currentX, currentZ);
                }

                float minY = groundY + hBottom + 0.05f; // Safe clearance above substrate
                float maxY = waterY - hTop - 0.03f;     // Safe clearance below surface

                float fishY;
                if (maxY > minY)
                {
                    // Niche depth partitioning: Carp cruise just beneath surface (0.82) with vivid visible scales,
                    // Catfish cruise mid-depth (0.48) with clear silhouette above gravel bed
                    float depthFrac = isCarp ? 0.82f : 0.48f;
                    float undulation = Mathf.Sin(t * 1.2f + fish.phase) * (isCarp ? 0.04f : 0.02f);
                    fishY = Mathf.Clamp(Mathf.Lerp(minY, maxY, depthFrac) + undulation, minY, maxY);
                }
                else
                {
                    fishY = (minY + maxY) * 0.5f;
                }

                Vector3 newPos = new Vector3(currentX, fishY, currentZ);
                Vector3 delta = newPos - fish.gameObject.transform.position;
                fish.gameObject.transform.position = newPos;

                if (delta.sqrMagnitude > 0.0001f)
                {
                    // Face swimming direction; smooth turn with slight yaw sway overlay
                    Quaternion lookRot = Quaternion.LookRotation(delta.normalized, Vector3.up);
                    float wiggle = Mathf.Sin(t * 3.5f + fish.phase) * 3.5f;
                    fish.gameObject.transform.rotation = lookRot * Quaternion.Euler(0, wiggle, 0);
                }
            }
        }

        public static Mesh CreateProceduralFishMesh()
        {
            var mesh = new Mesh { name = "Freshwater River Trout Mesh" };

            // 12 body cross-sections along fish length (0.35m long, 0.12m deep, 0.07m wide)
            const int rings = 10;
            const int segments = 8;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int r = 0; r <= rings; r++)
            {
                float fr = r / (float)rings;
                float z = (fr - 0.5f) * 0.35f;

                // Spindle profile: tapered head (fr=0) and tail (fr=1), wide belly (fr=0.35)
                float profile = Mathf.Sin(fr * Mathf.PI);
                float radiusY = profile * 0.06f + 0.008f;
                float radiusX = profile * 0.035f + 0.004f;

                for (int s = 0; s <= segments; s++)
                {
                    float angle = (s / (float)segments) * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * radiusX;
                    float y = Mathf.Sin(angle) * radiusY;

                    Vector3 pos = new Vector3(x, y, z);
                    vertices.Add(pos);
                    normals.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0).normalized);
                    uvs.Add(new Vector2(s / (float)segments, fr));
                }
            }

            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int curr = r * (segments + 1) + s;
                    int next = curr + (segments + 1);

                    triangles.Add(curr);
                    triangles.Add(next);
                    triangles.Add(curr + 1);

                    triangles.Add(curr + 1);
                    triangles.Add(next);
                    triangles.Add(next + 1);
                }
            }

            // Caudal (tail) fin
            int tailBase = vertices.Count;
            float tailZ = 0.175f;
            vertices.Add(new Vector3(0, 0.055f, tailZ + 0.075f));  // Upper fin tip
            vertices.Add(new Vector3(0, -0.055f, tailZ + 0.075f)); // Lower fin tip
            vertices.Add(new Vector3(0, 0.015f, tailZ));           // Base top
            vertices.Add(new Vector3(0, -0.015f, tailZ));          // Base bottom
            for (int i = 0; i < 4; i++)
            {
                normals.Add(Vector3.right);
                uvs.Add(new Vector2(0.5f, 0.5f));
            }

            triangles.Add(tailBase); triangles.Add(tailBase + 2); triangles.Add(tailBase + 3);
            triangles.Add(tailBase); triangles.Add(tailBase + 3); triangles.Add(tailBase + 1);
            triangles.Add(tailBase + 3); triangles.Add(tailBase + 2); triangles.Add(tailBase);
            triangles.Add(tailBase + 1); triangles.Add(tailBase + 3); triangles.Add(tailBase);

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Material CreateFishMaterial()
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");
            var mat = new Material(litShader)
            {
                name = "Freshwater River Trout Silvery Scales"
            };
            // Silvery greenish-slate back with pale iridescent belly
            mat.SetColor("_BaseColor", new Color(0.48f, 0.62f, 0.65f, 1.0f));
            mat.SetFloat("_Smoothness", 0.72f);
            mat.SetFloat("_Metallic", 0.35f);
            return mat;
        }

        public static List<GameObject> SpawnFishSchool(
            Transform parent, 
            string worldId, 
            out RiverFishSchool schoolComp, 
            GameObject barberPrefab = null, 
            Material barberMaterial = null,
            GameObject carpPrefab = null,
            Material carpMaterial = null)
        {
            var schoolGo = new GameObject("River Freshwater Fish School");
            schoolGo.transform.SetParent(parent, false);
            schoolComp = schoolGo.AddComponent<RiverFishSchool>();

#if UNITY_EDITOR
            if (barberPrefab == null)
            {
                barberPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CityLife/Art/Catfish/catfish_swim.fbx");
            }
            if (barberMaterial == null)
            {
                barberMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/CityLife/Art/Catfish/Catfish_Material.mat");
            }
            if (carpPrefab == null)
            {
                carpPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CityLife/Art/Carp/carp_swim.fbx");
            }
            if (carpMaterial == null)
            {
                carpMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/CityLife/Art/Carp/Carp_Material.mat");
            }
#endif
            if (barberMaterial != null) sharedBarberMaterial = barberMaterial;
            if (carpMaterial != null) sharedCarpMaterial = carpMaterial;

            // 1. Authored African Sharptooth Catfish (Barber) pool centers along deep channels and plunge pool
            var poolSpawns = new (Vector3 center, float radius, float speed, float depth, float scale)[]
            {
                // Waterfall Plunge Pool & South Gorge Basin
                (new Vector3(25f, -2.18f, -234f), 5.2f, 0.38f, 0.35f, 1.05f),
                (new Vector3(18f, -2.18f, -225f), 4.8f, 0.42f, 0.30f, 0.95f),
                (new Vector3(26f, -2.18f, -210f), 4.2f, 0.45f, 0.28f, 0.88f),
                (new Vector3(16f, -2.18f, -195f), 5.0f, 0.40f, 0.25f, 0.82f),
                (new Vector3(22f, -2.18f, -170f), 5.8f, 0.44f, 0.32f, 0.90f),
                (new Vector3(18f, -2.18f, -145f), 6.5f, 0.46f, 0.35f, 0.92f),
                (new Vector3(10f, -2.18f, -115f), 5.5f, 0.42f, 0.28f, 0.80f),
                (new Vector3(-8f, -2.7f, -85f), 6.0f, 0.48f, 0.30f, 0.85f),
                (new Vector3(6f, -2.8f, -55f), 5.2f, 0.40f, 0.25f, 0.70f),
                (new Vector3(-4f, -2.5f, -32f), 4.5f, 0.42f, 0.22f, 0.62f),
                // River Ford & Central Meanders
                (new Vector3(0f, -2.2f, -12f), 2.8f, 0.36f, 0.16f, 0.52f),
                (new Vector3(2f, -2.18f, -15f), 3.2f, 0.38f, 0.18f, 0.55f),
                (new Vector3(-12f, -2.3f, 8f), 4.8f, 0.35f, 0.20f, 0.58f),
                (new Vector3(2f, -2.4f, 20f), 4.5f, 0.40f, 0.22f, 0.60f),
                (new Vector3(14f, -2.6f, 32f), 5.5f, 0.44f, 0.24f, 0.65f),
                (new Vector3(24f, -2.8f, 52f), 5.0f, 0.42f, 0.25f, 0.68f)
            };

            var spawnedObjects = new List<GameObject>();
            for (int i = 0; i < poolSpawns.Length; i++)
            {
                var sp = poolSpawns[i];
                string fishId = $"river-fish-{i + 1:D2}";

                var fishGo = new GameObject(fishId);
                fishGo.transform.SetParent(schoolGo.transform, false);
                fishGo.transform.position = sp.center;
                fishGo.layer = 11; // Interactable

                Animation animComp = null;
                if (barberPrefab != null)
                {
                    var visual = UnityEngine.Object.Instantiate(barberPrefab, fishGo.transform);
                    visual.name = "BarberVisual";
                    visual.transform.localPosition = Vector3.zero;
                    visual.transform.localRotation = Quaternion.identity;
                    visual.transform.localScale = Vector3.one * sp.scale;

                    foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                    {
                        t.gameObject.layer = 11;
                    }

                    var smrs = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    foreach (var smr in smrs)
                    {
                        if (barberMaterial != null) smr.sharedMaterial = barberMaterial;
                        smr.updateWhenOffscreen = true;
                        if (sharedBarberMesh == null && smr.sharedMesh != null)
                        {
                            sharedBarberMesh = smr.sharedMesh;
                        }
                    }

                    animComp = visual.GetComponent<Animation>() ?? visual.GetComponentInChildren<Animation>();
                    if (animComp != null)
                    {
                        animComp.wrapMode = WrapMode.Loop;
                        animComp.playAutomatically = true;
                        if (animComp.clip != null)
                        {
                            string clipName = animComp.clip.name;
                            if (animComp[clipName] != null)
                            {
                                animComp[clipName].wrapMode = WrapMode.Loop;
                                animComp[clipName].time = (i * 0.73f) % animComp.clip.length;
                                animComp[clipName].speed = UnityEngine.Random.Range(0.85f, 1.15f);
                            }
                            animComp.Play(clipName);
                        }
                        else
                        {
                            animComp.Play();
                        }
                    }
                }
                else
                {
                    var mf = fishGo.AddComponent<MeshFilter>();
                    mf.sharedMesh = CreateProceduralFishMesh();
                    var mr = fishGo.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = CreateFishMaterial();
                }

                var col = fishGo.AddComponent<SphereCollider>();
                col.radius = 0.35f * sp.scale;
                col.isTrigger = true;

                var approach = new GameObject(fishId + " approach");
                approach.transform.SetParent(fishGo.transform, false);
                approach.transform.localPosition = Vector3.zero;

                var ni = fishGo.AddComponent<NpcInteractable>();
                ni.StableId = fishId;
                ni.WorldId = worldId;
                ni.Kind = NpcObjectKind.Item;
                ni.Permission = true;
                ni.Approach = approach.transform;

                var phys = fishGo.AddComponent<PhysicalItem>();
                phys.itemId = fishId;
                phys.itemTypeId = "food-river-fish";
                phys.massKg = 0.65f * sp.scale;
                phys.dimensions = new PhysicalDimensions(0.40f * sp.scale, 0.25f * sp.scale, 1.10f * sp.scale);
                phys.ConfigureComponents();

                var instance = new RiverFishInstance
                {
                    gameObject = fishGo,
                    swimCenter = sp.center,
                    swimRadius = sp.radius,
                    swimSpeed = sp.speed,
                    swimDepth = sp.depth,
                    scale = sp.scale,
                    phase = i * (Mathf.PI * 2f / poolSpawns.Length),
                    bodyTransform = fishGo.transform,
                    interactable = ni,
                    physicalItem = phys,
                    animation = animComp
                };
                schoolComp.ActiveFish.Add(instance);
                spawnedObjects.Add(fishGo);
            }

            // 2. Authored Gauteng Common Carp population across 5 ecological zones
            // Scales calibrated to authentic wild river sizes (base model length = 1.85m, height = 0.71m):
            // - Standard river adults (0.28x - 0.42x): 52cm - 78cm length, 20cm - 30cm body height
            // - Large trophy carp (0.45x - 0.55x): 83cm - 1.02m length, 32cm - 39cm body height
            // - Deep gorge plunge-pool monster (0.65x): 1.20m length in 3.5m+ deep water basin
            var carpSpawns = new (Vector3 center, float radius, float speed, float depth, float scale)[]
            {
                // Zone 1: Waterfall lower plunge outflow & deep canyon run
                (new Vector3(28f, -2.18f, -190f), 4.6f, 0.42f, 0.28f, 0.48f),
                (new Vector3(20f, -2.18f, -182f), 5.2f, 0.38f, 0.32f, 0.65f),    // Deep plunge-pool monster carp (1.20m)
                (new Vector3(24f, -2.18f, -165f), 4.8f, 0.45f, 0.30f, 0.42f),
                (new Vector3(14f, -2.18f, -152f), 5.5f, 0.40f, 0.26f, 0.45f),
                (new Vector3(26f, -2.18f, -138f), 4.2f, 0.44f, 0.34f, 0.52f),    // Trophy canyon carp (0.96m)

                // Zone 2: South gorge deep pools & rock shelves
                (new Vector3(12f, -2.18f, -125f), 4.5f, 0.46f, 0.28f, 0.36f),
                (new Vector3(4f, -2.4f, -100f), 5.0f, 0.42f, 0.30f, 0.44f),
                (new Vector3(-6f, -2.6f, -75f), 5.8f, 0.40f, 0.32f, 0.55f),     // Deep gorge trophy carp (1.02m)
                (new Vector3(2f, -2.7f, -65f), 4.6f, 0.44f, 0.25f, 0.38f),

                // Zone 3: Mid-river reed sanctuary
                (new Vector3(-6f, -2.4f, -45f), 4.0f, 0.38f, 0.22f, 0.32f),
                (new Vector3(4f, -2.3f, -38f), 3.8f, 0.42f, 0.20f, 0.34f),
                (new Vector3(-2f, -2.25f, -28f), 4.5f, 0.36f, 0.24f, 0.42f),
                (new Vector3(8f, -2.25f, -22f), 4.2f, 0.40f, 0.22f, 0.38f),

                // Zone 4: River Ford deep pool & gravel shallows transition
                (new Vector3(-4f, -2.2f, -8f), 3.2f, 0.34f, 0.16f, 0.30f),
                (new Vector3(6f, -2.2f, -2f), 3.5f, 0.38f, 0.18f, 0.32f),
                (new Vector3(-8f, -2.25f, 6f), 4.0f, 0.36f, 0.20f, 0.35f),
                (new Vector3(4f, -2.3f, 12f), 3.6f, 0.40f, 0.22f, 0.36f),

                // Zone 5: North meanders & delta reach
                (new Vector3(-6f, -2.35f, 26f), 4.8f, 0.42f, 0.24f, 0.38f),
                (new Vector3(8f, -2.5f, 38f), 5.2f, 0.44f, 0.26f, 0.45f),
                (new Vector3(18f, -2.7f, 48f), 5.0f, 0.40f, 0.28f, 0.48f),
                (new Vector3(26f, -2.8f, 62f), 5.5f, 0.46f, 0.30f, 0.50f)
            };

            for (int i = 0; i < carpSpawns.Length; i++)
            {
                var sp = carpSpawns[i];
                string carpId = $"river-carp-{i + 1:D2}";

                var carpGo = new GameObject(carpId);
                carpGo.transform.SetParent(schoolGo.transform, false);
                carpGo.transform.position = sp.center;
                carpGo.layer = 11; // Interactable

                Animation animComp = null;
                if (carpPrefab != null)
                {
                    var visual = UnityEngine.Object.Instantiate(carpPrefab, carpGo.transform);
                    visual.name = "CarpVisual";
                    visual.transform.localPosition = Vector3.zero;
                    visual.transform.localRotation = Quaternion.identity;
                    visual.transform.localScale = Vector3.one * sp.scale;

                    foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                    {
                        t.gameObject.layer = 11;
                    }

                    var smrs = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    foreach (var smr in smrs)
                    {
                        if (carpMaterial != null) smr.sharedMaterial = carpMaterial;
                        smr.updateWhenOffscreen = true; // Keep procedural skinned swim mesh visible at all times
                        if (sharedCarpMesh == null && smr.sharedMesh != null)
                        {
                            sharedCarpMesh = smr.sharedMesh;
                        }
                    }

                    animComp = visual.GetComponent<Animation>() ?? visual.GetComponentInChildren<Animation>();
                    if (animComp != null)
                    {
                        animComp.wrapMode = WrapMode.Loop;
                        animComp.playAutomatically = true;
                        if (animComp.clip != null)
                        {
                            string clipName = animComp.clip.name;
                            if (animComp[clipName] != null)
                            {
                                animComp[clipName].wrapMode = WrapMode.Loop;
                                animComp[clipName].time = (i * 0.61f) % animComp.clip.length;
                                animComp[clipName].speed = UnityEngine.Random.Range(0.85f, 1.15f);
                            }
                            animComp.Play(clipName);
                        }
                        else
                        {
                            animComp.Play();
                        }
                    }
                }
                else
                {
                    var mf = carpGo.AddComponent<MeshFilter>();
                    mf.sharedMesh = CreateProceduralFishMesh();
                    var mr = carpGo.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = CreateFishMaterial();
                }

                var col = carpGo.AddComponent<SphereCollider>();
                col.radius = 0.38f * sp.scale;
                col.isTrigger = true;

                var approach = new GameObject(carpId + " approach");
                approach.transform.SetParent(carpGo.transform, false);
                approach.transform.localPosition = Vector3.zero;

                var ni = carpGo.AddComponent<NpcInteractable>();
                ni.StableId = carpId;
                ni.WorldId = worldId;
                ni.Kind = NpcObjectKind.Item;
                ni.Permission = true;
                ni.Approach = approach.transform;

                var phys = carpGo.AddComponent<PhysicalItem>();
                phys.itemId = carpId;
                phys.itemTypeId = "food-river-carp";
                phys.massKg = Mathf.Max(0.5f, 22f * Mathf.Pow(sp.scale, 3f));
                phys.dimensions = new PhysicalDimensions(0.48f * sp.scale, 0.71f * sp.scale, 1.85f * sp.scale);
                phys.ConfigureComponents();

                var instance = new RiverFishInstance
                {
                    gameObject = carpGo,
                    swimCenter = sp.center,
                    swimRadius = sp.radius,
                    swimSpeed = sp.speed,
                    swimDepth = sp.depth,
                    scale = sp.scale,
                    phase = (i * 0.58f + 1.2f) % (Mathf.PI * 2f),
                    bodyTransform = carpGo.transform,
                    interactable = ni,
                    physicalItem = phys,
                    animation = animComp
                };
                schoolComp.ActiveFish.Add(instance);
                spawnedObjects.Add(carpGo);
            }

            return spawnedObjects;
        }

        private static Mesh CreateFishMeshAsset(Mesh fishMesh)
        {
            if (fishMesh != null) return fishMesh;
            return CreateProceduralFishMesh();
        }

        public static bool VerifyFishEcology(out string receipt)
        {
            var mesh = CreateProceduralFishMesh();
            if (mesh == null || mesh.vertexCount < 50 || mesh.triangles.Length < 60)
            {
                receipt = "River fish mesh failed procedural vertex generation.";
                return false;
            }

            // Verify bounds dimensions match standard river trout
            var bounds = mesh.bounds;
            if (bounds.size.z < 0.25f || bounds.size.z > 0.50f)
            {
                receipt = $"Fish length {bounds.size.z:F2}m out of realistic bounds [0.25, 0.50].";
                return false;
            }

            // Verify all key river coordinates are freshwater river channel
            var testCoords = new[]
            {
                new Vector3(2f, -2.18f, -15f),
                new Vector3(-4f, -2.5f, -32f),
                new Vector3(6f, -2.8f, -55f)
            };
            foreach (var c in testCoords)
            {
                if (!CoastalTerrain.IsFreshwaterRiver(c.x, c.z, c.y, CoastalWater.Level))
                {
                    receipt = $"Fish spawn point ({c.x}, {c.z}) rejected as non-freshwater river.";
                    return false;
                }
            }

            receipt = $"Verified river fish ecology with {mesh.vertexCount} verts, {mesh.triangles.Length / 3} triangles, and valid freshwater river coordinates.";
            return true;
        }
    }
}
