using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CityLife.World.Editor
{
    public static class IntegratedCoastalBuild
    {
        public const string Version = "0.0.11-survival.1";
        public static bool Requested => System.Environment.GetCommandLineArgs().Contains("-starfallIntegrated");
        public static void Run()
        {
            if (!Requested || !Application.isBatchMode) throw new InvalidOperationException("Explicit isolated integrated batch required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            IslandValidation.Run(); NpcMilestoneValidation.Run(); NpcHybridValidation.Run();
            string foodChecksFolder=Path.Combine("evidence/local/food-checks","integrated-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            var foodChecks=Starfall.Food.FoodChecks.Run(foodChecksFolder);
            File.WriteAllLines(Path.Combine(foodChecksFolder,"passed.txt"),foodChecks);

            // AG1: Food ownership checkpoint & material transaction verification
            var materialChecks = CityLife.Items.ItemMaterialTransactionChecks.Run();
            string checkpointFolder = Path.Combine(Path.GetTempPath(), "sf-chk-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            if (Directory.Exists(checkpointFolder)) Directory.Delete(checkpointFolder, true);
            Directory.CreateDirectory(checkpointFolder);
            var checkpointChecks = Starfall.Food.FoodOwnershipCheckpointChecks.Run(checkpointFolder);

            // AG2: Basket persistence & Spatial Memory verification
            var basketPersistChecks = CityLife.Items.BasketPersistenceChecks.Run();
            string caveFolder = Path.Combine(Path.GetTempPath(), "sf-cave-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            if (Directory.Exists(caveFolder)) Directory.Delete(caveFolder, true);
            Directory.CreateDirectory(caveFolder);
            var caveFoodChecks = Starfall.Food.CaveFoodMemoryChecks.Run(caveFolder);

            // AG3: Natural stone supply procedural geometry verification
            var stoneChecks = CityLife.Stones.Editor.StoneValidation.Run();

            // AG4: Tinder dry brush procedural geometry & aggregation self-test verification
            var tinderSelfTests = CityLife.Fire.TinderValidation.RunAggregationSelfTests();
            var tinderValidation = CityLife.Fire.TinderValidation.RunValidation();
            if (tinderSelfTests == null || tinderSelfTests.status != "PASSED" || !CityLife.Fire.TinderValidation.EvaluateAggregationDecision(tinderValidation, out int tinderExit) || tinderExit != 0)
                throw new InvalidOperationException("Tinder dry brush validation failed.");

            // AG5: Fallen wood procedural supply verification
            var woodChecks = CityLife.Wood.FallenWoodChecks.Run();

            int totalPassed = foodChecks.Count + materialChecks.Count + checkpointChecks.Count +
                              basketPersistChecks.Count + caveFoodChecks.Count + stoneChecks.Count +
                              woodChecks.Count;
            Debug.Log($"STARFALL_INTEGRATED_VALIDATION_PASSED: {totalPassed} named checks verified across all AG1-AG5 lanes with zero errors.");

            KokerboomRender.BuildCoastalPlayableSlice();
        }
        public static void Attach(Camera camera, GameObject ground, string folder)
        {
            CharacterAssetImport.Run();
            foreach (var collider in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)) collider.gameObject.layer = 8;
            ground.layer = 10;
            var actorObject = new GameObject("First coastal inhabitant"); actorObject.layer = 9;
            var actor = actorObject.AddComponent<CharacterPreviewActor>(); actor.ExternalDrive = true;
            actor.Capsule = actorObject.AddComponent<CharacterController>();
            actor.Capsule.height = 1.85f; actor.Capsule.center = new Vector3(0, .93f, 0); actor.Capsule.radius = .3f;
            actor.Capsule.skinWidth = .025f; actor.Capsule.stepOffset = .25f; actor.Capsule.slopeLimit = 45;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAssetImport.Body));
            model.transform.SetParent(actorObject.transform, false); model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            actor.Animator = model.GetComponent<Animator>();
            if (actor.Animator.avatar == null || !actor.Animator.avatar.isValid || !actor.Animator.avatar.isHuman) throw new InvalidOperationException("Humanoid avatar invalid.");
            actor.Animator.applyRootMotion = false; actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var clips = AssetDatabase.LoadAllAssetsAtPath(CharacterAssetImport.Motions).OfType<AnimationClip>().ToArray();
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/IntegratedHumanoid.controller");
            foreach (var pair in new[] { ("Idle", "Idle_Loop"), ("Walk", "Walk_Loop"), ("Interact", "Interact"),
                ("Crouch", "Crouch_Idle_Loop"), ("CrouchWalk", "Crouch_Fwd_Loop"), ("Sit", "Sitting_Idle_Loop"),
                ("SitEnter", "Sitting_Enter"), ("SitExit", "Sitting_Exit"), ("Pickup", "PickUp_Table") })
            {
                var state = controller.layers[0].stateMachine.AddState(pair.Item1);
                state.motion = clips.Single(c => c.name == pair.Item2 || c.name == "Armature|" + pair.Item2);
                if (pair.Item1 == "Idle") controller.layers[0].stateMachine.defaultState = state;
            }
            actor.Animator.runtimeAnimatorController = controller;
            Material Material(string name, Color color)
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                m.color = color; m.SetFloat("_Smoothness", .15f); AssetDatabase.CreateAsset(m, folder + "/" + name + ".mat"); return m;
            }
            var skin = Material("Integrated skin", Color.white);
            skin.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CharacterAssetImport.Root + "Skin_Dark.png"));
            skin.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CharacterAssetImport.Root + "Skin_Normal.png")); skin.EnableKeyword("_NORMALMAP");
            var eyes = Material("Integrated eyes", Color.white); eyes.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CharacterAssetImport.Root + "Eye_Brown.png"));
            var brows = Material("Integrated brows", new Color(.045f, .027f, .017f));
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            { renderer.sharedMaterial = renderer.name == "Eyes" ? eyes : renderer.name == "Eyebrows" ? brows : skin; renderer.updateWhenOffscreen = true; }
            // A passed, explicitly merged clothing revision is mandatory before baking a candidate.
            var outfit = typeof(IntegratedCoastalBuild).Assembly.GetType("CityLife.World.Editor.HunterOutfitAuthoring");
            if (outfit == null) throw new InvalidOperationException("Accepted hunter clothing revision has not been integrated.");
            outfit.GetMethod("Attach").Invoke(null, new object[] { model, folder });
            // The corrected grip is deliberately retained in this candidate. Its
            // standalone evidence remains non-accepting until the combined player
            // proves hand fit and clearance on the actual canyon terrain.
            var heldClub = model.GetComponent<HunterClubCarry>();
            if (heldClub == null || heldClub.Club == null) throw new InvalidOperationException("Corrected hunter club was not authored.");
            model.transform.localScale = new Vector3(1.15f, 1, 1.08f);
            foreach (Transform t in model.GetComponentsInChildren<Transform>()) t.gameObject.layer = 9;

            actor.View = camera.gameObject.AddComponent<CharacterPreviewCamera>(); actor.View.Target = actor.transform;
            // Initial ordinary follow framing looks across the dry activity
            // terrace toward the river, not straight into the nearby east mesa.
            // Mouse orbit remains fully player-controlled after startup.
            actor.View.Yaw = -25; actor.View.Pitch = 12; actor.View.Distance = 5.8f;
            // Reuse the accepted interaction fixture's authority, not its old floor/courtyard.
            new GameObject("Warm starlight").AddComponent<Light>().enabled = false;
            NpcPreviewStage.Configure(actor, camera, folder);
            Object.DestroyImmediate(camera.GetComponent<NpcPreviewSmoke>());
            var brain = actorObject.GetComponent<NpcAutonomy>(); brain.InstanceWorldId = NpcTerrainNavigation.RegionId;
            brain.TerrainNavigation = actorObject.AddComponent<NpcTerrainNavigation>();
            brain.Perception.WorldId = brain.InstanceWorldId;
            foreach (var item in brain.Registry) item.WorldId = brain.InstanceWorldId;
            var activityOffset = new Vector3(CoastalTerrain.ActivityCentre.x, 0, CoastalTerrain.ActivityCentre.y);
            foreach (var item in brain.Registry)
            {
                Vector3 original = item.transform.position;
                float floor = CoastalTerrain.Height(original.x + activityOffset.x, original.z + activityOffset.z);
                item.transform.position = new Vector3(original.x + activityOffset.x, floor + original.y, original.z + activityOffset.z);
                if (item.Approach != null)
                    item.Approach.position = new Vector3(item.Approach.position.x + activityOffset.x, floor, item.Approach.position.z + activityOffset.z);
                if (item.Socket != null)
                    item.Socket.position = new Vector3(item.Socket.position.x + activityOffset.x, floor + 1.06f, item.Socket.position.z + activityOffset.z);
                var plinth = GameObject.Find(item.Kind == NpcObjectKind.Item ? item.StableId + " plinth" : item.StableId);
                if (plinth != null)
                    plinth.transform.position = new Vector3(plinth.transform.position.x + activityOffset.x, floor + .43f, plinth.transform.position.z + activityOffset.z);
            }
            brain.OptionalPlanner = actorObject.AddComponent<NpcOptionalPlanner>(); brain.OptionalPlanner.Brain = brain;
            var livingMemory = actorObject.AddComponent<StarfallLivingMemoryRuntime>();
            livingMemory.Brain = brain; livingMemory.Hud = camera.GetComponent<NpcDecisionHud>();
            brain.SpawnPosition = new Vector3(activityOffset.x - 4, 4.02f, activityOffset.z - 5); actor.Place(brain.SpawnPosition);
            var controls = camera.GetComponent<NpcPlayerControls>(); controls.PersistentMouseCapture = true;
            controls.CameraMinimum = new Vector3(CoastalTerrain.MinX + 3, -1, CoastalTerrain.MinZ + 3);
            controls.CameraMaximum = new Vector3(CoastalTerrain.MaxX - 3, 220, CoastalTerrain.MaxZ - 3);
            camera.GetComponent<NpcDecisionHud>().Detailed = false;
            camera.fieldOfView = 60; actor.View.Yaw = -25; actor.View.Pitch = 12; actor.View.Follow();
            // Keep the composed galaxy view and add background coverage behind it.
            var galaxy = GameObject.Find("Distant galaxy - procedural dust and stellar band");
            if (galaxy != null)
            {
                var skyTemplate = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                skyTemplate.name = "Surrounding procedural stars";
                Object.DestroyImmediate(skyTemplate.GetComponent<Collider>());
                skyTemplate.transform.localScale = Vector3.one * 9000;
                var surroundingSky = new Material(galaxy.GetComponent<MeshRenderer>().sharedMaterial);
                surroundingSky.name = "Surrounding stars"; surroundingSky.renderQueue = 999;
                skyTemplate.GetComponent<MeshRenderer>().sharedMaterial = surroundingSky;
                skyTemplate.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var environment = actorObject.AddComponent<IntegratedEnvironment>(); environment.Brain = brain;
            environment.Surface = brain.TerrainNavigation; environment.View = camera;
            environment.Sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional && l.enabled);
            // Use the verified authored refuge geometry and environmental state, but
            // replace its standalone fixture body with the sole integrated inhabitant.
            RefugeBuild.Attach(camera, ground);
            var refugeRuntime = camera.GetComponent<Starfall.Refuge.RefugeRuntime>();
            var fixtureBody = GameObject.Find("Refuge player capsule");
            var authoredRefuge = GameObject.Find("First refuge / authored v1");
            if (refugeRuntime == null || fixtureBody == null || authoredRefuge == null) throw new InvalidOperationException("Verified First Refuge attachment failed.");
            // Put the authored refuge on a broad, reachable east-bank shelf. The original
            // refuge floor is centred at (-10,0), so this translation moves its centre to
            // (-165,118) and lifts its ramp/floor with the actual terrain height.
            var refugeAnchor = new Vector3(CoastalTerrain.RefugeCentre.x, 0, CoastalTerrain.RefugeCentre.y);
            var refugeDelta = new Vector3(refugeAnchor.x + 10,
                CoastalTerrain.Height(refugeAnchor.x, refugeAnchor.z), refugeAnchor.z);
            authoredRefuge.transform.position += refugeDelta;
            refugeRuntime.Hearth += refugeDelta; refugeRuntime.Bed += refugeDelta; refugeRuntime.Storage += refugeDelta;
            refugeRuntime.OriginOffset = refugeDelta;
            refugeRuntime.Body = actor.Capsule; refugeRuntime.IntegratedMode = true;
            Object.DestroyImmediate(fixtureBody);
            // Refuge preflight conservatively classifies every existing collider as
            // geometry. Restore the integrated actor and authority targets afterward.
            actorObject.layer = 9;
            foreach (var item in brain.Registry)
                foreach (var collider in item.GetComponentsInChildren<Collider>(true)) collider.gameObject.layer = 11;
            actor.Place(brain.SpawnPosition); actor.View.Follow();
            var refuge = new GameObject("First refuge / discoverable place"); refuge.layer = 11; refuge.transform.position = refugeRuntime.Hearth;
            var refugeSensor = refuge.AddComponent<SphereCollider>(); refugeSensor.isTrigger = true; refugeSensor.radius = .4f;
            var place = refuge.AddComponent<NpcInteractable>(); place.StableId = "first-refuge"; place.WorldId = brain.InstanceWorldId;
            place.Kind = NpcObjectKind.Place; place.Approach = refuge.transform;
            brain.Registry = brain.Registry.Concat(new[] { place }).ToArray();
            var foodObject = new GameObject("Food and ecology / integrated adapter"); foodObject.transform.SetParent(ground.transform);
            var food = foodObject.AddComponent<Starfall.Food.IntegratedFoodRuntime>();
            food.Brain = brain;
            food.Attach(brain.transform, ground.transform, brain.InstanceWorldId);
            food.Refuge=refugeRuntime;
            refugeRuntime.FreshwaterSurface=food.SpringWaterRenderer;
            refugeRuntime.ValidateGeometry();
            if(!refugeRuntime.WaterVerified||refugeRuntime.FreshwaterWaterCount!=1||
                Mathf.Min(refugeRuntime.FloorY,refugeRuntime.IngressY)-refugeRuntime.MaximumDesignWaterY<.75f)
                throw new InvalidOperationException("Authored freshwater and regional water require a measured refuge freeboard.");
            var survival = actorObject.AddComponent<StarfallSurvivalAutonomy>();
            survival.Brain=brain;survival.Food=food;survival.Refuge=refugeRuntime;brain.Survival=survival;
            var deathDiagnostic=camera.gameObject.AddComponent<StarfallSurvivalDeathAcceptance>();
            deathDiagnostic.Brain=brain;deathDiagnostic.Survival=survival;deathDiagnostic.Food=food;deathDiagnostic.View=camera;
            var gameCapture=camera.gameObject.AddComponent<StarfallSurvivalGameCapture>();
            gameCapture.Brain=brain;gameCapture.View=camera;

            // Physical item foundation integration: bind authoritative ItemModel, PhysicalAuthority, and physical demonstration item to the canyon inhabitant
            var physicalBootstrap = actorObject.AddComponent<CityLife.Items.PhysicalItemBootstrap>();
            physicalBootstrap.Brain = brain; brain.PhysicalItems = physicalBootstrap;
            var demoMaterial = Material("Physical demonstration stone", new Color(0.56f, 0.54f, 0.52f));
            if (demoMaterial == null || demoMaterial.shader == null || !demoMaterial.shader.isSupported)
                throw new InvalidOperationException("Demonstration stone material requires a valid, supported Universal Render Pipeline/Lit shader.");
            physicalBootstrap.DemonstrationMaterial = demoMaterial;

            // Basket foundation & player integration: serialized basket material, opt-in starter layout, and container panel wiring
            var basketMaterial = Material("Woven basket material", new Color(0.62f, 0.46f, 0.28f));
            if (basketMaterial == null || basketMaterial.shader == null || !basketMaterial.shader.isSupported)
                throw new InvalidOperationException("Basket material requires a valid, supported Universal Render Pipeline/Lit shader.");
            physicalBootstrap.BasketMaterial = basketMaterial;
            physicalBootstrap.OptInStarterLayout = true;

            var containerPanel = camera.gameObject.AddComponent<PhysicalContainerPanel>();
            containerPanel.Controls = controls;
            containerPanel.Bootstrap = physicalBootstrap;
            controls.ContainerPanel = containerPanel;

            // Natural Stone Supply (AG3): place procedural river cobbles and fieldstone on activity terrace
            var stoneGroup = new GameObject("Natural stone supply points");
            stoneGroup.transform.SetParent(ground.transform, false);
            var stoneMat = Material("Natural coastal stone", new Color(0.55f, 0.52f, 0.48f));

            var cobbleObj = new GameObject("Natural river cobble");
            cobbleObj.transform.SetParent(stoneGroup.transform, false);
            cobbleObj.transform.position = new Vector3(CoastalTerrain.ActivityCentre.x + 3.2f, CoastalTerrain.Height(CoastalTerrain.ActivityCentre.x + 3.2f, CoastalTerrain.ActivityCentre.y - 1.5f), CoastalTerrain.ActivityCentre.y - 1.5f);
            var cobbleMesh = CityLife.Stones.StoneMeshGenerator.GenerateMesh(CityLife.Stones.StoneShapeKind.RiverCobble, seed: 101, variantIndex: 0, uniformScale: 1.0f, flatShaded: true);
            cobbleObj.AddComponent<MeshFilter>().sharedMesh = cobbleMesh;
            cobbleObj.AddComponent<MeshRenderer>().sharedMaterial = stoneMat;
            var cobbleCol = cobbleObj.AddComponent<MeshCollider>();
            cobbleCol.sharedMesh = cobbleMesh;
            cobbleObj.layer = 8;

            var fieldObj = new GameObject("Natural fieldstone");
            fieldObj.transform.SetParent(stoneGroup.transform, false);
            fieldObj.transform.position = new Vector3(CoastalTerrain.ActivityCentre.x - 2.8f, CoastalTerrain.Height(CoastalTerrain.ActivityCentre.x - 2.8f, CoastalTerrain.ActivityCentre.y + 2.0f), CoastalTerrain.ActivityCentre.y + 2.0f);
            var fieldMesh = CityLife.Stones.StoneMeshGenerator.GenerateMesh(CityLife.Stones.StoneShapeKind.Fieldstone, seed: 202, variantIndex: 1, uniformScale: 1.2f, flatShaded: true);
            fieldObj.AddComponent<MeshFilter>().sharedMesh = fieldMesh;
            fieldObj.AddComponent<MeshRenderer>().sharedMaterial = stoneMat;
            var fieldCol = fieldObj.AddComponent<MeshCollider>();
            fieldCol.sharedMesh = fieldMesh;
            fieldObj.layer = 8;

            // Tinder & Night Fire (AG4): place procedural dry-brush tinder bundle near refuge hearth
            var tinderParams = CityLife.Fire.TinderParameters.ForLod(0, 4217);
            var tinderObj = new GameObject("Dry brush tinder bundle");
            tinderObj.transform.SetParent(ground.transform, false);
            Vector3 tinderPos = refugeRuntime.Hearth + new Vector3(0.6f, 0, 0.4f);
            tinderPos.y = CoastalTerrain.Height(tinderPos.x, tinderPos.z);
            tinderObj.transform.position = tinderPos;
            var tinderMesh = CityLife.Fire.TinderGeometry.GenerateMesh(tinderParams);
            tinderObj.AddComponent<MeshFilter>().sharedMesh = tinderMesh;
            var tinderMat = Material("Tinder dry brush", new Color(0.48f, 0.38f, 0.22f));
            tinderObj.AddComponent<MeshRenderer>().sharedMaterial = tinderMat;
            var tinderCol = tinderObj.AddComponent<BoxCollider>();
            tinderCol.size = CityLife.Fire.TinderMetadata.TargetDimensionsMetres;
            tinderCol.center = new Vector3(0, CityLife.Fire.TinderMetadata.TargetDimensionsMetres.y * 0.5f, 0);
            tinderObj.layer = 8;

            // Fallen Wood Supply (AG5): place procedural fallen wood branches on terrace margin
            var woodGroup = new GameObject("Fallen wood supply");
            woodGroup.transform.SetParent(ground.transform, false);
            var branchProfile = CityLife.Wood.FallenWoodProfile.CreateBranchPreset();
            var branchMesh = CityLife.Wood.FallenWoodGenerator.GenerateMesh(branchProfile, 0x4A8C193Eu, out var branchMeta);
            var woodObj = new GameObject("Fallen wood branch");
            woodObj.transform.SetParent(woodGroup.transform, false);
            Vector3 branchPos = new Vector3(CoastalTerrain.ActivityCentre.x + 4.5f, 0, CoastalTerrain.ActivityCentre.y + 3.0f);
            branchPos.y = CoastalTerrain.Height(branchPos.x, branchPos.z);
            woodObj.transform.position = branchPos;
            woodObj.AddComponent<MeshFilter>().sharedMesh = branchMesh;
            var woodMat = Material("Fallen wood bark", new Color(0.38f, 0.26f, 0.16f));
            woodObj.AddComponent<MeshRenderer>().sharedMaterial = woodMat;
            var woodCol = woodObj.AddComponent<MeshCollider>();
            woodCol.sharedMesh = branchMesh;
            woodObj.layer = 8;

            var giant = GameObject.Find("Blue gas giant - procedural volumetric cloud bands");
            if (giant == null) throw new InvalidOperationException("Coastal giant missing.");
            IntegratedCelestial.PlaceDistantGiant(giant.transform,camera);
            var moonPositions = new[] { new Vector3(-800, 640, 3400), new Vector3(1400, 800, 3600), new Vector3(-450, 2100, 4200) };
            var moonSizes = new[] { 95f, 70f, 52f }; var moons = new Transform[3];
            var moonMaterial = Material("Distant moon rock", new Color(.5f, .65f, .77f));
            for (int i = 0; i < moons.Length; i++)
            {
                var moon = new GameObject("Distant moon " + (i + 1)); moon.transform.position = moonPositions[i]; moon.transform.localScale = Vector3.one * moonSizes[i];
                moon.AddComponent<MeshFilter>().sharedMesh = giant.GetComponent<MeshFilter>().sharedMesh;
                moon.AddComponent<MeshRenderer>().sharedMaterial = moonMaterial; moons[i] = moon.transform;
            }
            camera.farClipPlane = IntegratedCelestial.SkyFarClip;
            var celestial = camera.gameObject.AddComponent<IntegratedCelestial>(); celestial.Environment = environment; celestial.Giant = giant.transform; celestial.Moons = moons;
            var rainObject = new GameObject("Regional precipitation"); var rain = rainObject.AddComponent<ParticleSystem>(); rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = rain.main; main.maxParticles = 512; main.startLifetime = 1.4f; main.startSpeed = 0; main.startSize = .008f;
            main.startColor = new Color(.5f, .78f, .95f, .5f); main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = false;
            var shape = rain.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(18, 1, 18);
            var velocity = rain.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World; velocity.y = -13;
            var emission = rain.emission; emission.rateOverTime = 0;
            var rainRenderer = rain.GetComponent<ParticleSystemRenderer>();
            var rainMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            rainMaterial.name = "Translucent rain"; rainMaterial.SetColor("_BaseColor", new Color(.68f, .76f, .82f, .18f));
            rainMaterial.SetFloat("_Surface", 1); rainMaterial.SetFloat("_Blend", 0);
            rainMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            rainMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            rainMaterial.SetFloat("_ZWrite", 0); rainMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            rainMaterial.renderQueue = 3000; AssetDatabase.CreateAsset(rainMaterial, folder + "/TranslucentRain.mat");
            rainRenderer.sharedMaterial = rainMaterial;
            rainRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            rainRenderer.lengthScale = 1;
            rainRenderer.velocityScale = .007f;
            rainRenderer.maxParticleSize = .008f;
            rain.Play(); environment.Rain = rain;
            var acceptance = camera.gameObject.AddComponent<IntegratedAcceptance>(); acceptance.Brain = brain; acceptance.Controls = controls; acceptance.Environment = environment; acceptance.Food = food;
            Time.fixedDeltaTime = .02f; Physics.gravity = Vector3.down * 9.81f;
            File.WriteAllText(folder + "/component-binding.json", JsonUtility.ToJson(new Binding(), true));
        }
        [Serializable] private sealed class Binding
        {
            public string worldId = NpcTerrainNavigation.RegionId, terrain = CoastalTerrain.ContentRevision,
                scope = "Finite 1200x1600m Fish River Canyon inspired candidate with a traversable turquoise river-to-sea corridor, terrain-fitted authored First Refuge, visible berry bush, environment, inhabitant, food model and local thought protocol. Boats and unified save/load remain unimplemented.";
        }
    }
}
