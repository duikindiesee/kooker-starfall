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
        private static RiverFishSchool instance;
        public static RiverFishSchool Instance
        {
            get
            {
                if (instance != null) return instance;
                instance = UnityEngine.Object.FindAnyObjectByType<RiverFishSchool>();
                return instance;
            }
            set => instance = value;
        }

        public const int DefaultFishCount = 16;
        public const float MinWaterDepth = 0.40f;
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
            Instance = this;
            EnsureSharedAssets();
            ReconstructActiveFishIfEmpty();
        }

        private void OnEnable()
        {
            Instance = this;
        }

        private void Start()
        {
            EnsureSharedAssets();
            ReconstructActiveFishIfEmpty();
            EnsureFishKinematicsAndAnimation();
        }

        private void EnsureSharedAssets()
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

        public void ReconstructActiveFishIfEmpty()
        {
            if (ActiveFish == null) ActiveFish = new List<RiverFishInstance>();
            ActiveFish.RemoveAll(f => f == null || f.gameObject == null);
            if (ActiveFish.Count > 0) return;

            // Search children for baked fish instances
            var allChildren = GetComponentsInChildren<Transform>(true);
            int fishIndex = 0;
            for (int i = 0; i < allChildren.Length; i++)
            {
                var child = allChildren[i];
                if (child == null || child == transform) continue;
                if (child.parent != transform) continue;
                string name = child.gameObject.name;
                if (name.EndsWith(" approach", StringComparison.Ordinal)) continue;
                if (!name.StartsWith("river-fish-", StringComparison.Ordinal) &&
                    !name.StartsWith("river-carp-", StringComparison.Ordinal))
                    continue;

                var ni = child.GetComponent<NpcInteractable>();
                var phys = child.GetComponent<PhysicalItem>();
                var anim = child.GetComponentInChildren<Animation>(true);
                bool isCarp = name.StartsWith("river-carp-", StringComparison.Ordinal);

                float scale = 0.85f;
                var smr = child.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr != null && smr.transform.parent != null)
                {
                    scale = smr.transform.localScale.x;
                }

                Vector3 currentPos = child.position;
                var instance = new RiverFishInstance
                {
                    gameObject = child.gameObject,
                    swimCenter = new Vector3(currentPos.x, CoastalWater.Level - 0.22f, currentPos.z),
                    swimRadius = isCarp ? 3.8f : 4.5f,
                    swimSpeed = isCarp ? 0.40f : 0.42f,
                    swimDepth = isCarp ? 0.18f : 0.24f,
                    scale = scale > 0.05f ? scale : 0.85f,
                    phase = fishIndex * 0.48f,
                    bodyTransform = child,
                    interactable = ni,
                    physicalItem = phys,
                    animation = anim
                };
                ActiveFish.Add(instance);
                fishIndex++;
            }
        }

        public void EnsureFishKinematicsAndAnimation()
        {
            for (int i = 0; i < ActiveFish.Count; i++)
            {
                var fish = ActiveFish[i];
                if (fish == null || fish.gameObject == null) continue;

                if (fish.physicalItem != null && fish.physicalItem.Body != null)
                {
                    fish.physicalItem.Body.linearVelocity = Vector3.zero;
                    fish.physicalItem.Body.angularVelocity = Vector3.zero;
                    fish.physicalItem.Body.isKinematic = true;
                    fish.physicalItem.Body.useGravity = false;
                }

                var smrs = fish.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                bool isCarp = (fish.interactable != null && fish.interactable.StableId != null && fish.interactable.StableId.Contains("carp")) ||
                              fish.gameObject.name.Contains("carp");
                foreach (var smr in smrs)
                {
                    if (smr == null) continue;
                    smr.enabled = true;
                    smr.updateWhenOffscreen = true;
                    if (smr.sharedMaterial == null)
                    {
                        smr.sharedMaterial = isCarp ? SharedCarpMaterial : SharedBarberMaterial;
                    }
                    if (smr.sharedMesh == null)
                    {
                        smr.sharedMesh = isCarp ? SharedCarpMesh : SharedBarberMesh;
                    }
                }

                var anim = fish.gameObject.GetComponentInChildren<Animation>(true);
                if (anim != null)
                {
                    anim.enabled = true;
                    anim.playAutomatically = true;
                    anim.wrapMode = WrapMode.Loop;
                    if (anim.clip != null)
                    {
                        string clipName = anim.clip.name;
                        if (anim[clipName] != null)
                        {
                            anim[clipName].wrapMode = WrapMode.Loop;
                            anim[clipName].time = (i * 0.65f) % anim.clip.length;
                        }
                        anim.Play(clipName);
                    }
                    else
                    {
                        anim.Play();
                    }
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
            public bool isReserved;
        }

        [SerializeField]
        public List<RiverFishInstance> ActiveFish = new List<RiverFishInstance>();

        public static Vector3 EvaluateFishPosition(
            Vector3 swimCenter, 
            float swimRadius, 
            float swimSpeed, 
            float phase, 
            float scale, 
            bool isCarp, 
            float simTime, 
            float waterY)
        {
            float angle = simTime * swimSpeed + phase;
            float currentX = swimCenter.x + Mathf.Cos(angle) * swimRadius;
            float currentZ = swimCenter.z + Mathf.Sin(angle) * (swimRadius * 1.6f);

            // Ford shallows avoidance: deflect fish away from crossing ford (z in [-20, -8]) into deeper channel runs
            if (currentZ >= -20f && currentZ <= -8f)
            {
                if (currentZ > -14f) currentZ = Mathf.Lerp(currentZ, -6.5f, 0.75f);
                else currentZ = Mathf.Lerp(currentZ, -21.5f, 0.75f);
            }

            float channelX = CoastalTerrain.RiverCenterlineX(currentZ);
            float groundY = CoastalTerrain.Height(currentX, currentZ);
            float localDepth = waterY - groundY;

            // Lateral bank containment: steer back toward deep river channel if orbit nears shore or clay banks
            if (localDepth < MinWaterDepth)
            {
                currentX = Mathf.Lerp(currentX, channelX, 0.85f);
                groundY = CoastalTerrain.Height(currentX, currentZ);
                localDepth = waterY - groundY;
                if (localDepth < MinWaterDepth)
                {
                    currentX = channelX;
                    groundY = CoastalTerrain.Height(currentX, currentZ);
                    localDepth = waterY - groundY;
                }
            }

            float effectiveScale = scale;
            if (localDepth < 0.45f)
            {
                effectiveScale = Mathf.Min(scale, Mathf.Max(0.20f, localDepth * 0.85f));
            }

            float hTop = (isCarp ? 0.36f : 0.14f) * effectiveScale;
            float hBottom = (isCarp ? 0.35f : 0.14f) * effectiveScale;

            float minY = groundY + hBottom + 0.02f;
            float maxY = waterY - hTop - 0.02f;

            float fishY;
            if (maxY > minY)
            {
                float depthFrac = isCarp ? 0.85f : 0.50f;
                float undulation = Mathf.Sin(simTime * 1.5f + phase) * (isCarp ? 0.03f : 0.015f);
                fishY = Mathf.Clamp(Mathf.Lerp(minY, maxY, depthFrac) + undulation, minY, maxY);
            }
            else
            {
                fishY = (groundY + waterY) * 0.5f;
            }

            // Invariant: dorsal fin must stay strictly submerged below water surface, never above dry ground
            float maxAllowedY = waterY - hTop - 0.015f;
            if (fishY > maxAllowedY) fishY = maxAllowedY;
            if (fishY < groundY + 0.02f) fishY = Mathf.Min(groundY + 0.02f, maxAllowedY);

            return new Vector3(currentX, fishY, currentZ);
        }

        private void Update()
        {
            float t = Time.time;
            float waterY = CoastalWater.CurrentLevel;

            for (int i = 0; i < ActiveFish.Count; i++)
            {
                var fish = ActiveFish[i];
                if (fish == null || fish.gameObject == null || !fish.gameObject.activeSelf) continue;

                // If reserved on fishing line, carried in hand, stowed in satchel, or undergoing eating/cooking, don't simulate swimming orbit
                if (fish.isReserved || (fish.physicalItem != null && (fish.physicalItem.IsCarried || fish.physicalItem.IsStored)))
                {
                    continue;
                }

                // Enforce kinematic state during swimming so physics engine never pulls fish down
                if (fish.physicalItem != null && fish.physicalItem.Body != null)
                {
                    if (!fish.physicalItem.Body.isKinematic)
                    {
                        fish.physicalItem.Body.linearVelocity = Vector3.zero;
                        fish.physicalItem.Body.angularVelocity = Vector3.zero;
                        fish.physicalItem.Body.isKinematic = true;
                        fish.physicalItem.Body.useGravity = false;
                    }
                }

                bool isCarp = (fish.interactable != null && fish.interactable.StableId != null && fish.interactable.StableId.Contains("carp")) ||
                              fish.gameObject.name.Contains("carp");

                Vector3 newPos = EvaluateFishPosition(
                    fish.swimCenter, 
                    fish.swimRadius, 
                    fish.swimSpeed, 
                    fish.phase, 
                    fish.scale, 
                    isCarp, 
                    t, 
                    waterY);

                Vector3 delta = newPos - fish.gameObject.transform.position;
                fish.gameObject.transform.position = newPos;

                if (delta.sqrMagnitude > 0.00001f)
                {
                    Quaternion lookRot = Quaternion.LookRotation(delta.normalized, Vector3.up);
                    float wiggle = Mathf.Sin(t * 3.8f + fish.phase) * 3.5f;
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
                (new Vector3(25f, -2.18f, -234f), 5.2f, 0.38f, 0.35f, 1.15f),
                (new Vector3(18f, -2.18f, -225f), 4.8f, 0.42f, 0.30f, 1.05f),
                (new Vector3(26f, -2.18f, -210f), 4.2f, 0.45f, 0.28f, 0.98f),
                (new Vector3(16f, -2.18f, -195f), 5.0f, 0.40f, 0.25f, 0.92f),
                (new Vector3(22f, -2.18f, -170f), 5.8f, 0.44f, 0.32f, 1.00f),
                (new Vector3(18f, -2.18f, -145f), 6.5f, 0.46f, 0.35f, 0.95f),
                (new Vector3(10f, -2.18f, -115f), 5.5f, 0.42f, 0.28f, 0.88f),
                (new Vector3(-8f, -2.7f, -85f), 6.0f, 0.48f, 0.30f, 0.92f),
                (new Vector3(6f, -2.8f, -55f), 5.2f, 0.40f, 0.25f, 0.85f),
                (new Vector3(-4f, -2.5f, -32f), 4.5f, 0.42f, 0.22f, 0.82f),
                // Deep River Runs & Central Meanders (away from shallow crossing ford)
                (new Vector3(-4f, -2.3f, -26f), 4.5f, 0.40f, 0.22f, 0.75f),
                (new Vector3(2f, -2.25f, 4f), 4.2f, 0.42f, 0.20f, 0.78f),
                (new Vector3(-8f, -2.3f, 8f), 4.8f, 0.38f, 0.20f, 0.85f),
                (new Vector3(2f, -2.4f, 20f), 4.5f, 0.40f, 0.22f, 0.90f),
                (new Vector3(14f, -2.6f, 32f), 5.5f, 0.44f, 0.24f, 0.88f),
                (new Vector3(24f, -2.8f, 52f), 5.0f, 0.42f, 0.25f, 0.95f)
            };

            var spawnedObjects = new List<GameObject>();
            for (int i = 0; i < poolSpawns.Length; i++)
            {
                var sp = poolSpawns[i];
                string fishId = $"river-fish-{i + 1:D2}";

                float channelX = CoastalTerrain.RiverCenterlineX(sp.center.z);
                float lateralMax = Mathf.Min(sp.radius * 0.35f, 3.0f);
                float lateralOffset = Mathf.Clamp(sp.center.x - channelX, -lateralMax, lateralMax);
                Vector3 validCenter = new Vector3(channelX + lateralOffset, CoastalWater.Level - 0.22f, sp.center.z);

                var fishGo = new GameObject(fishId);
                fishGo.transform.SetParent(schoolGo.transform, false);
                fishGo.transform.position = validCenter;
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
                if (phys.Body != null)
                {
                    phys.Body.linearVelocity = Vector3.zero;
                    phys.Body.angularVelocity = Vector3.zero;
                    phys.Body.isKinematic = true;
                    phys.Body.useGravity = false;
                }

                var instance = new RiverFishInstance
                {
                    gameObject = fishGo,
                    swimCenter = validCenter,
                    swimRadius = Mathf.Min(sp.radius, 4.0f),
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
                (new Vector3(28f, -2.18f, -190f), 4.6f, 0.42f, 0.28f, 0.85f),
                (new Vector3(20f, -2.18f, -182f), 5.2f, 0.38f, 0.32f, 1.15f),    // Deep plunge-pool monster carp (2.1m)
                (new Vector3(24f, -2.18f, -165f), 4.8f, 0.45f, 0.30f, 0.82f),
                (new Vector3(14f, -2.18f, -152f), 5.5f, 0.40f, 0.26f, 0.85f),
                (new Vector3(26f, -2.18f, -138f), 4.2f, 0.44f, 0.34f, 0.95f),    // Trophy canyon carp

                // Zone 2: South gorge deep pools & rock shelves
                (new Vector3(12f, -2.18f, -125f), 4.5f, 0.46f, 0.28f, 0.80f),
                (new Vector3(4f, -2.4f, -100f), 5.0f, 0.42f, 0.30f, 0.88f),
                (new Vector3(-6f, -2.6f, -75f), 5.8f, 0.40f, 0.32f, 1.05f),     // Deep gorge trophy carp
                (new Vector3(2f, -2.7f, -65f), 4.6f, 0.44f, 0.25f, 0.82f),

                // Zone 3: Mid-river reed sanctuary
                (new Vector3(-6f, -2.4f, -45f), 4.0f, 0.38f, 0.22f, 0.78f),
                (new Vector3(4f, -2.3f, -38f), 3.8f, 0.42f, 0.20f, 0.82f),
                (new Vector3(-2f, -2.25f, -28f), 4.5f, 0.36f, 0.24f, 0.88f),
                (new Vector3(8f, -2.25f, -22f), 4.2f, 0.40f, 0.22f, 0.84f),

                // Zone 4: River Ford transition & deep pool meanders (away from shallow ford)
                (new Vector3(1f, -2.25f, -25f), 4.0f, 0.42f, 0.22f, 0.65f),      // Deep run south of terrace
                (new Vector3(-2f, -2.25f, -21f), 4.2f, 0.44f, 0.20f, 0.68f),     // Deep meander
                (new Vector3(3f, -2.25f, -5f), 3.8f, 0.40f, 0.20f, 0.62f),       // North terrace deep run
                (new Vector3(-4f, -2.2f, -8f), 3.5f, 0.38f, 0.14f, 0.42f),
                (new Vector3(5f, -2.2f, -2f), 3.8f, 0.40f, 0.16f, 0.48f),
                (new Vector3(-6f, -2.25f, 6f), 4.2f, 0.42f, 0.20f, 0.65f),
                (new Vector3(4f, -2.3f, 12f), 3.6f, 0.44f, 0.22f, 0.75f),

                // Zone 5: North meanders & delta reach
                (new Vector3(-6f, -2.35f, 26f), 4.8f, 0.42f, 0.24f, 0.85f),
                (new Vector3(8f, -2.5f, 38f), 5.2f, 0.44f, 0.26f, 0.90f),
                (new Vector3(18f, -2.7f, 48f), 5.0f, 0.40f, 0.28f, 0.95f),
                (new Vector3(26f, -2.8f, 62f), 5.5f, 0.46f, 0.30f, 1.00f)
            };

            for (int i = 0; i < carpSpawns.Length; i++)
            {
                var sp = carpSpawns[i];
                string carpId = $"river-carp-{i + 1:D2}";

                float channelX = CoastalTerrain.RiverCenterlineX(sp.center.z);
                float lateralMax = Mathf.Min(sp.radius * 0.35f, 3.0f);
                float lateralOffset = Mathf.Clamp(sp.center.x - channelX, -lateralMax, lateralMax);
                Vector3 validCenter = new Vector3(channelX + lateralOffset, CoastalWater.Level - 0.22f, sp.center.z);

                var carpGo = new GameObject(carpId);
                carpGo.transform.SetParent(schoolGo.transform, false);
                carpGo.transform.position = validCenter;
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
                if (phys.Body != null)
                {
                    phys.Body.linearVelocity = Vector3.zero;
                    phys.Body.angularVelocity = Vector3.zero;
                    phys.Body.isKinematic = true;
                    phys.Body.useGravity = false;
                }

                var instance = new RiverFishInstance
                {
                    gameObject = carpGo,
                    swimCenter = validCenter,
                    swimRadius = Mathf.Min(sp.radius, 4.0f),
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
#if UNITY_EDITOR
            var editorTestType = Type.GetType("CityLife.World.Editor.RiverFishValidationTests, Assembly-CSharp-Editor");
            if (editorTestType != null)
            {
                var method = editorTestType.GetMethod("RunAllChecks", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method != null)
                {
                    object[] args = new object[] { null };
                    bool passed = (bool)method.Invoke(null, args);
                    receipt = (string)args[0];
                    return passed;
                }
            }
#endif
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

        public bool HasFishNear(Vector3 searchPos, float maxRadius)
        {
            if (ActiveFish == null || ActiveFish.Count == 0) return false;
            float maxDistSq = maxRadius * maxRadius;
            for (int i = 0; i < ActiveFish.Count; i++)
            {
                var f = ActiveFish[i];
                if (f == null || f.gameObject == null || !f.gameObject.activeSelf) continue;
                if (f.isReserved) continue;
                if (f.physicalItem != null && (f.physicalItem.IsCarried || f.physicalItem.IsStored)) continue;
                if ((f.gameObject.transform.position - searchPos).sqrMagnitude <= maxDistSq) return true;
            }
            return false;
        }

        public bool TryReserveFishNear(
            Vector3 searchPos, 
            float maxRadius, 
            out RiverFishInstance reservedFish)
        {
            reservedFish = null;
            if (ActiveFish == null || ActiveFish.Count == 0) return false;

            int bestIndex = -1;
            float bestDistSq = maxRadius * maxRadius;

            for (int i = 0; i < ActiveFish.Count; i++)
            {
                var f = ActiveFish[i];
                if (f == null || f.gameObject == null || !f.gameObject.activeSelf) continue;
                if (f.isReserved) continue;
                if (f.physicalItem != null && (f.physicalItem.IsCarried || f.physicalItem.IsStored)) continue;

                float dsq = (f.gameObject.transform.position - searchPos).sqrMagnitude;
                if (dsq < bestDistSq)
                {
                    bestDistSq = dsq;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0) return false;

            reservedFish = ActiveFish[bestIndex];
            reservedFish.isReserved = true;
            return true;
        }

        public void ReleaseReservation(RiverFishInstance fish)
        {
            if (fish == null) return;
            fish.isReserved = false;
            if (fish.gameObject != null && fish.gameObject.activeSelf)
            {
                if (fish.physicalItem != null && fish.physicalItem.Body != null)
                {
                    fish.physicalItem.Body.linearVelocity = Vector3.zero;
                    fish.physicalItem.Body.angularVelocity = Vector3.zero;
                    fish.physicalItem.Body.isKinematic = true;
                    fish.physicalItem.Body.useGravity = false;
                }
            }
        }

        public void CompleteCatch(RiverFishInstance fish)
        {
            if (fish == null) return;
            fish.isReserved = false;
            if (ActiveFish != null)
            {
                ActiveFish.Remove(fish);
            }
        }

        public bool TryCatchFish(
            Vector3 searchPos, 
            float maxRadius, 
            out string speciesTypeId, 
            out float fishScale, 
            out GameObject caughtGo)
        {
            speciesTypeId = null;
            fishScale = 0.85f;
            caughtGo = null;

            if (!TryReserveFishNear(searchPos, maxRadius, out var target))
            {
                return false;
            }

            bool isCarp = (target.interactable != null && target.interactable.StableId != null && target.interactable.StableId.Contains("carp")) ||
                          (target.gameObject != null && target.gameObject.name.Contains("carp"));

            speciesTypeId = isCarp ? "food-river-carp" : "food-river-fish";
            fishScale = target.scale > 0.05f ? target.scale : (isCarp ? 0.85f : 0.75f);
            caughtGo = target.gameObject;

            CompleteCatch(target);
            return true;
        }

        public static bool CanFishInRiver(Vector3 casterPos, Vector3 castTargetPos, out string reason)
        {
            reason = null;
            float dist = Vector3.Distance(casterPos, castTargetPos);
            if (dist < 2.0f || dist > 18.0f)
            {
                reason = "Cast distance out of range [2m, 18m]";
                return false;
            }

            float waterLevel = CoastalWater.CurrentLevel;
            if (!CoastalTerrain.IsFreshwaterRiver(castTargetPos.x, castTargetPos.z, castTargetPos.y, waterLevel))
            {
                reason = "Target location is not in freshwater river";
                return false;
            }

            float groundY = CoastalTerrain.Height(castTargetPos.x, castTargetPos.z);
            float depth = waterLevel - groundY;
            if (depth < 0.25f)
            {
                reason = "Water too shallow for river fishing";
                return false;
            }

            return true;
        }
    }
}
