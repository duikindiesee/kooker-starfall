using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using CityLife.Items;

namespace CityLife.World
{
    public sealed class IslandBootstrap : MonoBehaviour
    {
        private string stage = "Preparing CityLife island";
        private string error;
        private bool ready;
        private CancellationTokenSource cancellation;
        public IslandField Field { get; private set; }
        private IEnumerator Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            cancellation = new CancellationTokenSource();
            IslandDefinition definition = null;
            try
            {
                definition = JsonUtility.FromJson<IslandDefinition>(Resources.Load<TextAsset>("IslandDefinition").text);
                definition.Validate(); Field = new IslandField(definition);
            }
            catch (Exception ex) { Fail(ex); }
            if (error != null) yield break;
            stage = "Shaping the desert coast · seed " + definition.seed;
            var token = cancellation.Token;
            var work = Task.Run(() => Field.Generate(token), token);
            while (!work.IsCompleted) yield return null;
            if (work.IsFaulted) { Fail(work.Exception.GetBaseException()); yield break; }
            if (work.IsCanceled) yield break;
            stage = "Preparing land, sea and sky";
            yield return null;
            try
            {
                bool isolatedSmoke = Array.IndexOf(Environment.GetCommandLineArgs(), "-citylifeSmoke") >= 0;
                string folder = Path.Combine(Application.persistentDataPath, "Worlds", definition.Fingerprint());
                string editsPath = Path.Combine(folder, "edits.json");
                // This is a new offline world. Never touches browser IndexedDB or invents a service endpoint.
                var edits = !isolatedSmoke && File.Exists(editsPath) ? JsonUtility.FromJson<WorldEdits>(File.ReadAllText(editsPath)) : WorldEdits.Empty(definition);
                if (edits == null) throw new InvalidDataException("Cannot read world edits. The original save has been preserved.");
                edits.Apply(Field);
                if (!isolatedSmoke)
                {
                    Directory.CreateDirectory(folder);
                    string definitionPath = Path.Combine(folder, "world.json");
                    if (!File.Exists(definitionPath)) File.WriteAllText(definitionPath, JsonUtility.ToJson(definition, true));
                    if (!File.Exists(editsPath)) edits.Save(editsPath);
                }
                var cam = Camera.main;
                if (cam == null) { var go = new GameObject("Explorer Camera"); go.tag = "MainCamera"; cam = go.AddComponent<Camera>(); }
                cam.nearClipPlane = 0.15f; cam.farClipPlane = 24000; cam.fieldOfView = 55;
                var renderer = gameObject.AddComponent<IslandRenderer>();
                renderer.Initialize(Field, cam);
                var explorer = gameObject.AddComponent<IslandExplorer>();
                explorer.Initialize(Field, cam, renderer);
                SpawnLivingWorldInhabitant(cam, Field);
                Debug.Log("CITYLIFE_WORLD_READY " + JsonUtility.ToJson(new WorldEvidence {
                    worldId = definition.worldId, fingerprint = definition.Fingerprint(), baseHeightHash = Field.BaseHash(),
                    widthMetres = definition.Width, landKm2 = Field.LandAreaKm2, gentleLandKm2 = Field.FlatAreaKm2,
                    maxHeightMetres = Field.MaxHeight, generationMs = Field.GenerationMilliseconds,
                    graphicsDevice = SystemInfo.graphicsDeviceName, graphicsMemoryMb = SystemInfo.graphicsMemorySize,
                    sourceCommit = definition.sourceCommit, editsRevision = edits.revision
                }));
                ready = true;
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void SpawnLivingWorldInhabitant(Camera cam, IslandField field)
        {
            try
            {
                var spawn = field.Landing;
                spawn.y = field.Ground(spawn.x, spawn.z);

                var inhabitantGo = new GameObject("Island Inhabitant");
                inhabitantGo.transform.position = spawn;
                inhabitantGo.layer = 9;

                var actor = inhabitantGo.AddComponent<CharacterPreviewActor>();
                actor.ExternalDrive = true;
                actor.Capsule = inhabitantGo.AddComponent<CharacterController>();
                actor.Capsule.height = 1.85f;
                actor.Capsule.center = new Vector3(0, 0.93f, 0);
                actor.Capsule.radius = 0.3f;
                actor.Capsule.skinWidth = 0.025f;
                actor.Capsule.stepOffset = 0.25f;
                actor.Capsule.slopeLimit = 45f;

                var bodyVisual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                bodyVisual.name = "Inhabitant Body Visual";
                bodyVisual.transform.SetParent(inhabitantGo.transform, false);
                bodyVisual.transform.localPosition = new Vector3(0, 0.93f, 0);
                bodyVisual.transform.localScale = new Vector3(0.6f, 0.92f, 0.6f);
                var bodyCol = bodyVisual.GetComponent<Collider>();
                if (bodyCol != null) DestroyImmediate(bodyCol);

                var nav = inhabitantGo.AddComponent<NpcTerrainNavigation>();
                nav.IslandField = field;

                var brain = inhabitantGo.AddComponent<NpcAutonomy>();
                brain.Actor = actor;
                brain.TerrainNavigation = nav;
                brain.InstanceWorldId = field.Definition.worldId;
                brain.SpawnPosition = spawn;

                var perceptionGo = new GameObject("Perception");
                perceptionGo.transform.SetParent(inhabitantGo.transform, false);
                brain.Perception = perceptionGo.AddComponent<NpcPerception>();
                brain.Perception.WorldId = field.Definition.worldId;

                var logGo = new GameObject("DecisionLog");
                logGo.transform.SetParent(inhabitantGo.transform, false);
                brain.Log = logGo.AddComponent<NpcDecisionLog>();

                var physicalBootstrap = inhabitantGo.AddComponent<PhysicalItemBootstrap>();
                physicalBootstrap.Brain = brain;
                brain.PhysicalItems = physicalBootstrap;
                physicalBootstrap.OptInStarterLayout = true;

                // Create shared materials for physical item visuals
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader != null)
                {
                    physicalBootstrap.DemonstrationMaterial = new Material(shader) { name = "Demonstration Stone", color = new Color(0.55f, 0.52f, 0.48f) };
                    physicalBootstrap.StoneMaterial = new Material(shader) { name = "Island River Cobble", color = new Color(0.52f, 0.48f, 0.45f) };
                    physicalBootstrap.TinderMaterial = new Material(shader) { name = "Island Dry Tinder", color = new Color(0.48f, 0.38f, 0.22f) };
                    physicalBootstrap.WoodMaterial = new Material(shader) { name = "Island Quiver Wood", color = new Color(0.38f, 0.26f, 0.16f) };
                    physicalBootstrap.BasketMaterial = new Material(shader) { name = "Island Woven Basket", color = new Color(0.62f, 0.46f, 0.28f) };
                }

                // Extraterrestrial Scanner Terminal at landing camp
                var scannerObj = new GameObject("Alien artifact scanner terminal");
                Vector3 scannerPos = spawn + new Vector3(-2.8f, 0, 1.8f);
                scannerPos.y = field.Ground(scannerPos.x, scannerPos.z);
                scannerObj.transform.position = scannerPos;

                var plinth = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                plinth.name = "Scanner plinth";
                plinth.transform.SetParent(scannerObj.transform, false);
                plinth.transform.localScale = new Vector3(0.6f, 0.45f, 0.6f);
                plinth.transform.localPosition = new Vector3(0, 0.45f, 0);
                if (physicalBootstrap.StoneMaterial != null) plinth.GetComponent<MeshRenderer>().sharedMaterial = physicalBootstrap.StoneMaterial;

                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.name = "Holographic scanning pad";
                pad.transform.SetParent(scannerObj.transform, false);
                pad.transform.localScale = new Vector3(0.35f, 0.05f, 0.35f);
                pad.transform.localPosition = new Vector3(0, 0.92f, 0);

                var scanLightObj = new GameObject("Scan beam");
                scanLightObj.transform.SetParent(pad.transform, false);
                scanLightObj.transform.localPosition = new Vector3(0, 0.2f, 0);
                var scanLight = scanLightObj.AddComponent<Light>();
                scanLight.type = LightType.Point;
                scanLight.range = 2.0f;
                scanLight.color = new Color(0.2f, 0.85f, 1.0f);
                scanLight.intensity = 2.0f;

                var scanner = scannerObj.AddComponent<AlienArtifactScanner>();
                scanner.Brain = brain;
                scanner.ScanningPad = pad.transform;
                scanner.ScanLight = scanLight;
                scanner.VisualRenderer = pad.GetComponent<MeshRenderer>();

                // Stone Knapping Workstation
                var knappingObj = new GameObject("Stone knapping workstation");
                Vector3 knapPos = spawn + new Vector3(2.2f, 0, -1.4f);
                knapPos.y = field.Ground(knapPos.x, knapPos.z);
                knappingObj.transform.position = knapPos;
                var knapAnvil = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                knapAnvil.name = "Anvil stone";
                knapAnvil.transform.SetParent(knappingObj.transform, false);
                knapAnvil.transform.localScale = new Vector3(0.5f, 0.25f, 0.5f);
                knapAnvil.transform.localPosition = new Vector3(0, 0.25f, 0);
                if (physicalBootstrap.StoneMaterial != null) knapAnvil.GetComponent<MeshRenderer>().sharedMaterial = physicalBootstrap.StoneMaterial;

                var knapping = knappingObj.AddComponent<StoneKnappingWorkstation>();
                knapping.Brain = brain;
                knapping.Bootstrap = physicalBootstrap;
                knapping.AnvilPoint = knapAnvil.transform;

                // Stone Building Workstation (Masonry & Hearth/Windbreak Construction)
                var buildingObj = new GameObject("Stone building workstation");
                Vector3 buildPos = spawn + new Vector3(3.8f, 0, -2.5f);
                buildPos.y = field.Ground(buildPos.x, buildPos.z);
                buildingObj.transform.position = buildPos;
                var building = buildingObj.AddComponent<StoneBuildingWorkstation>();
                building.Brain = brain;
                building.Bootstrap = physicalBootstrap;
                building.ConstructionSite = buildPos;

                // Foraging Expedition Cycle
                var foraging = inhabitantGo.AddComponent<ForagingExpeditionCycle>();
                foraging.Brain = brain;
                foraging.Bootstrap = physicalBootstrap;

                // Natural Stone and Tinder Supply Points around the landing camp
                SpawnNaturalSupplies(field, spawn, physicalBootstrap);

                Debug.Log($"ISLAND_LIVING_WORLD_INITIALIZED: Inhabitant spawned at landing {spawn} with scanner terminal, knapping station, building station, and foraging loops.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[IslandBootstrap] Inhabitant spawn note: {ex.Message}");
            }
        }

        private void SpawnNaturalSupplies(IslandField field, Vector3 campPos, PhysicalItemBootstrap bootstrap)
        {
            var supplyGroup = new GameObject("Camp natural resources");
            var offsets = new[]
            {
                (new Vector3(3.5f, 0, 1.2f), "stone-river-cobble"),
                (new Vector3(-3.2f, 0, 2.5f), "stone-fieldstone"),
                (new Vector3(2.5f, 0, -1.8f), "stone-flat-slab"),
                (new Vector3(-1.8f, 0, 3.2f), "stone-fieldstone"),
                (new Vector3(1.8f, 0, 3.8f), "fire-tinder-bundle"),
                (new Vector3(-2.1f, 0, -3.4f), "wood-fallen-branch"),
                (new Vector3(0.5f, 0, 1.5f), "container-basket")
            };

            int idx = 1;
            foreach (var (off, typeId) in offsets)
            {
                Vector3 pos = campPos + off;
                pos.y = field.Ground(pos.x, pos.z) + 0.1f;
                bootstrap.CreatePhysicalItem($"island-{typeId}-{idx++:D2}", typeId, pos, Quaternion.identity);
            }
        }

        private void Fail(Exception ex) { error = ex.Message; stage = "World could not open"; Debug.LogException(ex); }
        private void OnDestroy() { cancellation?.Cancel(); cancellation?.Dispose(); }
        private void OnGUI()
        {
            if (ready) return;
            GUI.backgroundColor = new Color(0.025f, 0.065f, 0.1f, 0.98f);
            var title = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter };
            var body = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
            GUI.Label(new Rect(0, Screen.height / 2 - 80, Screen.width, 50), "CITYLIFE", title);
            GUI.Label(new Rect(40, Screen.height / 2 - 15, Screen.width - 80, 100), stage + (error == null ? "" : "\n" + error), body);
        }
        [Serializable] private sealed class WorldEvidence
        {
            public string worldId, fingerprint, baseHeightHash, sourceCommit, graphicsDevice;
            public float widthMetres, landKm2, gentleLandKm2, maxHeightMetres;
            public long generationMs;
            public int graphicsMemoryMb, editsRevision;
        }
    }
}
