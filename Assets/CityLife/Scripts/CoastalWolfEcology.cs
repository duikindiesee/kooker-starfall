using System;
using System.Collections.Generic;
using UnityEngine;
using CityLife.World;

namespace CityLife.World
{
    /// <summary>
    /// Autonomous coastal timber wolf ecology.
    /// Wolves roam the rugged upper canyon slopes, establishing danger in the wilderness.
    /// Inhabitants must remain alert, draw their heavy club, and execute defensive strikes
    /// to ward off stalks and protect their vital health.
    /// </summary>
    [SelectionBase]
    public sealed class CoastalWolfEcology : MonoBehaviour
    {
        public enum WolfState { Prowl, Alert, Stalk, Flee }

        [Header("Identity & Territory")]
        public string WolfId = "wild-canyon-wolf-01";
        public Vector3 TerritoryCenter;
        public float TerritoryRadius = 22f;

        [Header("State & Dynamic Senses")]
        public WolfState State = WolfState.Prowl;
        public float Health = 100f;
        public float AlertDistance = 18f;
        public float StalkDistance = 9f;
        public float AttackDistance = 2.2f;

        private float nextDecisionTime;
        private Vector3 targetMovePoint;
        private Transform threatInhabitant;
        private float fleeUntilTime;

        public static readonly Vector3[] AuthoredWolfSpawns = new Vector3[]
        {
            new Vector3(-45f, 0f, 195f),
            new Vector3(38f, 0f, 225f),
            new Vector3(-25f, 0f, 165f)
        };

        private static Mesh sharedWolfMesh;
        private static Material sharedWolfPeltMat;
        private static Material sharedWolfEyeMat;

        public static Mesh GetOrCreateWolfMesh()
        {
            if (sharedWolfMesh != null) return sharedWolfMesh;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();

            Color32 peltDark = new Color32(72, 68, 62, 255);    // Charcoal gray wolf back
            Color32 peltFlank = new Color32(110, 102, 92, 255); // Timber gray flank
            Color32 peltBelly = new Color32(165, 155, 140, 255);// Pale underbelly
            Color32 peltMuzzle = new Color32(42, 38, 35, 255);  // Blackened muzzle

            // Quadruped low-poly wolf geometry:
            // 1. Torso: tapered body (chest deeper than flank)
            AddBox(vertices, normals, colors, triangles,
                new Vector3(0f, 0.58f, 0.12f), new Vector3(0.32f, 0.38f, 0.65f), peltDark, peltFlank, peltBelly);

            // 2. Chest & Shoulder Mass
            AddBox(vertices, normals, colors, triangles,
                new Vector3(0f, 0.64f, 0.38f), new Vector3(0.36f, 0.44f, 0.38f), peltDark, peltFlank, peltBelly);

            // 3. Neck & Head
            AddBox(vertices, normals, colors, triangles,
                new Vector3(0f, 0.78f, 0.58f), new Vector3(0.24f, 0.30f, 0.28f), peltDark, peltFlank, peltFlank);

            // 4. Muzzle
            AddBox(vertices, normals, colors, triangles,
                new Vector3(0f, 0.72f, 0.76f), new Vector3(0.14f, 0.16f, 0.24f), peltMuzzle, peltMuzzle, peltMuzzle);

            // 5. Alert Ears (triangular upright wedges)
            AddWedge(vertices, normals, colors, triangles,
                new Vector3(-0.09f, 0.94f, 0.54f), new Vector3(0.06f, 0.14f, 0.08f), peltDark);
            AddWedge(vertices, normals, colors, triangles,
                new Vector3(0.09f, 0.94f, 0.54f), new Vector3(0.06f, 0.14f, 0.08f), peltDark);

            // 6. Four Legs (front-left, front-right, rear-left, rear-right)
            Vector3 legSize = new Vector3(0.11f, 0.46f, 0.12f);
            AddBox(vertices, normals, colors, triangles, new Vector3(-0.14f, 0.23f, 0.34f), legSize, peltFlank, peltFlank, peltDark);
            AddBox(vertices, normals, colors, triangles, new Vector3(0.14f, 0.23f, 0.34f), legSize, peltFlank, peltFlank, peltDark);
            AddBox(vertices, normals, colors, triangles, new Vector3(-0.13f, 0.23f, -0.22f), legSize, peltFlank, peltFlank, peltDark);
            AddBox(vertices, normals, colors, triangles, new Vector3(0.13f, 0.23f, -0.22f), legSize, peltFlank, peltFlank, peltDark);

            // 7. Bushy Tail
            AddBox(vertices, normals, colors, triangles,
                new Vector3(0f, 0.46f, -0.44f), new Vector3(0.12f, 0.16f, 0.34f), peltDark, peltFlank, peltBelly);

            sharedWolfMesh = new Mesh
            {
                name = "Procedural Timber Wolf Mesh",
                vertices = vertices.ToArray(),
                normals = normals.ToArray(),
                colors32 = colors.ToArray(),
                triangles = triangles.ToArray()
            };
            sharedWolfMesh.RecalculateBounds();
            return sharedWolfMesh;
        }

        private static void AddBox(List<Vector3> verts, List<Vector3> norms, List<Color32> cols, List<int> tris,
            Vector3 center, Vector3 size, Color32 topCol, Color32 sideCol, Color32 botCol)
        {
            Vector3 h = size * 0.5f;
            Vector3[] c = new Vector3[]
            {
                center + new Vector3(-h.x, -h.y, -h.z), // 0
                center + new Vector3( h.x, -h.y, -h.z), // 1
                center + new Vector3( h.x,  h.y, -h.z), // 2
                center + new Vector3(-h.x,  h.y, -h.z), // 3
                center + new Vector3(-h.x, -h.y,  h.z), // 4
                center + new Vector3( h.x, -h.y,  h.z), // 5
                center + new Vector3( h.x,  h.y,  h.z), // 6
                center + new Vector3(-h.x,  h.y,  h.z)  // 7
            };

            // 6 faces: Up, Down, Forward, Back, Left, Right
            AddQuad(verts, norms, cols, tris, c[3], c[2], c[6], c[7], Vector3.up, topCol);
            AddQuad(verts, norms, cols, tris, c[4], c[5], c[1], c[0], Vector3.down, botCol);
            AddQuad(verts, norms, cols, tris, c[7], c[6], c[5], c[4], Vector3.forward, sideCol);
            AddQuad(verts, norms, cols, tris, c[2], c[3], c[0], c[1], Vector3.back, sideCol);
            AddQuad(verts, norms, cols, tris, c[3], c[7], c[4], c[0], Vector3.left, sideCol);
            AddQuad(verts, norms, cols, tris, c[6], c[2], c[1], c[5], Vector3.right, sideCol);
        }

        private static void AddWedge(List<Vector3> verts, List<Vector3> norms, List<Color32> cols, List<int> tris,
            Vector3 center, Vector3 size, Color32 col)
        {
            Vector3 h = size * 0.5f;
            int b = verts.Count;
            verts.Add(center + new Vector3(-h.x, -h.y, -h.z));
            verts.Add(center + new Vector3( h.x, -h.y, -h.z));
            verts.Add(center + new Vector3(   0,  h.y,     0));
            verts.Add(center + new Vector3(   0, -h.y,  h.z));
            for (int i = 0; i < 4; i++) { norms.Add(Vector3.up); cols.Add(col); }
            tris.AddRange(new int[] { b, b + 1, b + 2, b + 1, b + 3, b + 2, b + 3, b, b + 2 });
        }

        private static void AddQuad(List<Vector3> verts, List<Vector3> norms, List<Color32> cols, List<int> tris,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 norm, Color32 col)
        {
            int baseIdx = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            for (int i = 0; i < 4; i++) { norms.Add(norm); cols.Add(col); }
            tris.Add(baseIdx); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
            tris.Add(baseIdx); tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);
        }

        public static List<GameObject> SpawnWolves(Transform parent)
        {
            var results = new List<GameObject>();
            var mesh = GetOrCreateWolfMesh();

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sharedWolfPeltMat == null)
            {
                sharedWolfPeltMat = new Material(litShader) { name = "Coastal Timber Wolf Pelt" };
                sharedWolfPeltMat.color = Color.white; // vertex colors modulate fur shades
            }

            for (int i = 0; i < AuthoredWolfSpawns.Length; i++)
            {
                Vector3 p = AuthoredWolfSpawns[i];
                float y = CoastalTerrain.Height(p.x, p.z);
                Vector3 spawnPos = new Vector3(p.x, y, p.z);

                string id = $"wild-canyon-wolf-{i + 1:D2}";
                var wolfObj = new GameObject(id);
                wolfObj.transform.SetParent(parent, false);
                wolfObj.transform.position = spawnPos;
                wolfObj.layer = 11;

                var mf = wolfObj.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;

                var mr = wolfObj.AddComponent<MeshRenderer>();
                mr.sharedMaterial = sharedWolfPeltMat;

                var col = wolfObj.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 0.45f, 0.1f);
                col.size = new Vector3(0.55f, 0.85f, 1.4f);
                col.isTrigger = true;

                var approach = new GameObject(id + " approach");
                approach.transform.SetParent(wolfObj.transform, false);
                approach.transform.localPosition = new Vector3(0f, 0f, 1.2f);

                var ni = wolfObj.AddComponent<NpcInteractable>();
                ni.StableId = id;
                ni.WorldId = NpcTerrainNavigation.RegionId;
                ni.Kind = NpcObjectKind.Place;
                ni.ObservedType = "wolf";
                ni.Permission = true;
                ni.Approach = approach.transform;

                var wolfComp = wolfObj.AddComponent<CoastalWolfEcology>();
                wolfComp.WolfId = id;
                wolfComp.TerritoryCenter = spawnPos;

                results.Add(wolfObj);
            }
            return results;
        }

        private void Start()
        {
            targetMovePoint = transform.position;
            nextDecisionTime = Time.time + UnityEngine.Random.Range(1f, 3f);
        }

        private void Update()
        {
            if (threatInhabitant == null)
            {
                var brain = FindFirstObjectByType<NpcAutonomy>();
                if (brain != null) threatInhabitant = brain.transform;
            }

            float dt = Time.deltaTime;
            float distToThreat = threatInhabitant != null
                ? Vector3.Distance(transform.position, threatInhabitant.position)
                : 999f;

            // Flee timer management
            if (State == WolfState.Flee)
            {
                if (Time.time >= fleeUntilTime)
                {
                    State = WolfState.Prowl;
                }
                else
                {
                    // Run away from threat
                    if (threatInhabitant != null)
                    {
                        Vector3 awayDir = (transform.position - threatInhabitant.position).normalized;
                        awayDir.y = 0;
                        MoveInDirection(awayDir, 3.8f, dt);
                    }
                    return;
                }
            }

            // Sensory state transitions
            if (distToThreat <= StalkDistance)
            {
                State = WolfState.Stalk;
            }
            else if (distToThreat <= AlertDistance)
            {
                State = WolfState.Alert;
            }
            else
            {
                State = WolfState.Prowl;
            }

            // State behaviors
            if (State == WolfState.Stalk)
            {
                // Face and stalk slowly towards inhabitant
                Vector3 toThreat = (threatInhabitant.position - transform.position);
                toThreat.y = 0;
                if (toThreat.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.RotateTowards(transform.rotation,
                        Quaternion.LookRotation(toThreat), 120f * dt);
                }

                // If armed club is in hand, wolf maintains wary standoff distance (~4m)
                var brain = threatInhabitant.GetComponent<NpcAutonomy>();
                var club = brain != null ? brain.GetComponentInChildren<HunterClubCarry>() : null;
                bool inhabitantArmed = club != null && !club.Stowed;

                float stopDist = inhabitantArmed ? 4.2f : AttackDistance;
                if (distToThreat > stopDist)
                {
                    MoveInDirection(toThreat.normalized, inhabitantArmed ? 0.9f : 1.6f, dt);
                }
            }
            else if (State == WolfState.Alert)
            {
                // Stand ground, face inhabitant
                Vector3 toThreat = (threatInhabitant.position - transform.position);
                toThreat.y = 0;
                if (toThreat.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.RotateTowards(transform.rotation,
                        Quaternion.LookRotation(toThreat), 90f * dt);
                }
            }
            else // Prowl
            {
                if (Time.time >= nextDecisionTime)
                {
                    nextDecisionTime = Time.time + UnityEngine.Random.Range(4f, 9f);
                    Vector2 randCircle = UnityEngine.Random.insideUnitCircle * TerritoryRadius;
                    targetMovePoint = TerritoryCenter + new Vector3(randCircle.x, 0f, randCircle.y);
                    targetMovePoint.y = CoastalTerrain.Height(targetMovePoint.x, targetMovePoint.z);
                }

                Vector3 delta = targetMovePoint - transform.position;
                delta.y = 0;
                if (delta.magnitude > 1.0f)
                {
                    MoveInDirection(delta.normalized, 0.8f, dt);
                }
            }
        }

        private void MoveInDirection(Vector3 dir, float speed, float dt)
        {
            if (dir.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(dir), 180f * dt);

            Vector3 nextPos = transform.position + dir * speed * dt;
            nextPos.y = CoastalTerrain.Height(nextPos.x, nextPos.z);

            // Ground height clamping & safe bounds
            nextPos.x = Mathf.Clamp(nextPos.x, NpcTerrainNavigation.SafeMinX + 15f, NpcTerrainNavigation.SafeMaxX - 15f);
            nextPos.z = Mathf.Clamp(nextPos.z, NpcTerrainNavigation.SafeMinZ + 15f, NpcTerrainNavigation.SafeMaxZ - 15f);
            transform.position = nextPos;
        }

        private static int dynamicDropCounter = 100;

        public static GameObject SpawnMeatDrop(Vector3 position, Transform parent = null)
        {
            string id = $"wild-meat-{++dynamicDropCounter}";
            var meatObj = new GameObject(id);
            if (parent != null) meatObj.transform.SetParent(parent, false);
            meatObj.transform.position = position;
            meatObj.layer = 11;

            var col = meatObj.AddComponent<BoxCollider>();
            col.size = new Vector3(0.24f, 0.16f, 0.12f);
            col.isTrigger = true;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            var vCol = visual.GetComponent<Collider>();
            if (vCol != null) UnityEngine.Object.DestroyImmediate(vCol);
            visual.transform.SetParent(meatObj.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = new Vector3(0.22f, 0.12f, 0.10f);

            var mr = visual.GetComponent<MeshRenderer>();
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var meatMat = new Material(litShader) { name = "Raw Venison Mat" };
            meatMat.color = new Color(0.68f, 0.14f, 0.12f, 1f);
            mr.sharedMaterial = meatMat;

            var approach = new GameObject(id + " approach");
            approach.transform.SetParent(meatObj.transform, false);
            approach.transform.localPosition = Vector3.zero;

            var ni = meatObj.AddComponent<NpcInteractable>();
            ni.StableId = id;
            ni.WorldId = NpcTerrainNavigation.RegionId;
            ni.Kind = NpcObjectKind.Item;
            ni.Permission = true;
            ni.Approach = approach.transform;

            var phys = meatObj.AddComponent<CityLife.Items.PhysicalItem>();
            phys.itemId = id;
            phys.itemTypeId = "food-wolf-meat";
            phys.massKg = 1.4f;
            phys.dimensions = new CityLife.Items.PhysicalDimensions(0.24f, 0.16f, 0.10f);
            phys.ConfigureComponents();

            var brain = UnityEngine.Object.FindAnyObjectByType<NpcAutonomy>();
            if (brain != null && brain.Registry != null)
            {
                var list = new List<NpcInteractable>(brain.Registry) { ni };
                brain.Registry = list.ToArray();
            }

            return meatObj;
        }

        public static GameObject SpawnLeatherDrop(Vector3 position, Transform parent = null)
        {
            string id = $"wild-leather-{++dynamicDropCounter}";
            var leatherObj = new GameObject(id);
            if (parent != null) leatherObj.transform.SetParent(parent, false);
            leatherObj.transform.position = position;
            leatherObj.layer = 11;

            var col = leatherObj.AddComponent<BoxCollider>();
            col.size = new Vector3(0.32f, 0.14f, 0.18f);
            col.isTrigger = true;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.name = "Visual";
            var vCol = visual.GetComponent<Collider>();
            if (vCol != null) UnityEngine.Object.DestroyImmediate(vCol);
            visual.transform.SetParent(leatherObj.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(0, 0, 90f);
            visual.transform.localScale = new Vector3(0.12f, 0.16f, 0.12f);

            var mr = visual.GetComponent<MeshRenderer>();
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var leatherMat = new Material(litShader) { name = "Wolf Leather Mat" };
            leatherMat.color = new Color(0.55f, 0.42f, 0.30f, 1f);
            mr.sharedMaterial = leatherMat;

            var approach = new GameObject(id + " approach");
            approach.transform.SetParent(leatherObj.transform, false);
            approach.transform.localPosition = Vector3.zero;

            var ni = leatherObj.AddComponent<NpcInteractable>();
            ni.StableId = id;
            ni.WorldId = NpcTerrainNavigation.RegionId;
            ni.Kind = NpcObjectKind.Item;
            ni.Permission = true;
            ni.Approach = approach.transform;

            var phys = leatherObj.AddComponent<CityLife.Items.PhysicalItem>();
            phys.itemId = id;
            phys.itemTypeId = "material-wolf-leather";
            phys.massKg = 0.95f;
            phys.dimensions = new CityLife.Items.PhysicalDimensions(0.35f, 0.22f, 0.08f);
            phys.ConfigureComponents();

            var brain = UnityEngine.Object.FindAnyObjectByType<NpcAutonomy>();
            if (brain != null && brain.Registry != null)
            {
                var list = new List<NpcInteractable>(brain.Registry) { ni };
                brain.Registry = list.ToArray();
            }

            return leatherObj;
        }

        /// <summary>
        /// Called when the inhabitant performs a defensive club strike against this wolf.
        /// Causes the wolf to drop fresh venison meat and cured leather hide, yelp, recoil, and flee back into rugged canyon scrub.
        /// </summary>
        public void TakeClubHit(Vector3 strikerPosition)
        {
            Vector3 dropPos = transform.position;
            dropPos.y = CoastalTerrain.Height(dropPos.x, dropPos.z) + 0.05f;

            // Deliver meat and leather drops at combat encounter location
            SpawnMeatDrop(dropPos + Vector3.left * 0.35f);
            SpawnLeatherDrop(dropPos + Vector3.right * 0.35f);

            Vector3 recoilDir = (transform.position - strikerPosition).normalized;
            recoilDir.y = 0;
            if (recoilDir.sqrMagnitude < 0.01f) recoilDir = -transform.forward;

            // Immediate physical recoil
            transform.position += recoilDir * 2.5f;
            transform.position = new Vector3(transform.position.x,
                CoastalTerrain.Height(transform.position.x, transform.position.z),
                transform.position.z);

            State = WolfState.Flee;
            fleeUntilTime = Time.time + 14f; // Flees for 14 seconds
        }

        public static bool VerifyWolfEcology(out string receipt)
        {
            if (AuthoredWolfSpawns.Length < 3)
            {
                receipt = "Wolf authored locations insufficient (< 3).";
                return false;
            }

            var mesh = GetOrCreateWolfMesh();
            if (mesh == null || mesh.vertexCount == 0 || mesh.triangles.Length == 0)
            {
                receipt = "Wolf procedural mesh generation failed or empty.";
                return false;
            }

            receipt = $"Wolf ecology verified: {AuthoredWolfSpawns.Length} pack spawns defined, mesh vertices={mesh.vertexCount}, triangles={mesh.triangles.Length / 3}.";
            return true;
        }
    }
}
