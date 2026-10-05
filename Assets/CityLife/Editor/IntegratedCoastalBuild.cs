using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using CityLife.Items;
using Object = UnityEngine.Object;

namespace CityLife.World.Editor
{
    public static class IntegratedCoastalBuild
    {
        public const string Version = "0.0.11-survival.1";
        public static bool Requested => System.Environment.GetCommandLineArgs().Contains("-starfallIntegrated");
        public static void Run()
        {
            ExecuteAllChecks();
            KokerboomRender.BuildCoastalPlayableSlice();
        }

        public static int ExecuteAllChecks()
        {
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

            // Extraterrestrial Artifact Scanner: resource analysis & physical fact verification
            var scannerMock = new GameObject("Test scanner").AddComponent<AlienArtifactScanner>();
            var tinderScan = scannerMock.PerformScan("fire-tinder-bundle");
            var cobbleScan = scannerMock.PerformScan("stone-river-cobble");
            var fieldScan = scannerMock.PerformScan("stone-fieldstone");
            var woodScan = scannerMock.PerformScan("wood-fallen-branch");
            var basketScan = scannerMock.PerformScan("container-basket");
            if (tinderScan.FlammabilityRating <= 0.9f || cobbleScan.ThermalMassRating <= 0.8f ||
                fieldScan.ThermalMassRating <= 0.8f || woodScan.FuelEnergyRating <= 0.9f ||
                basketScan.PracticalUtility.Length == 0)
                throw new InvalidOperationException("Alien scanner resource analysis verification failed.");
            UnityEngine.Object.DestroyImmediate(scannerMock.gameObject);

            // Evening Refuge Hearth: fuel storage and dusk ignition verification
            var testHearth = new Starfall.Refuge.HearthState();
            testHearth.AddLog();
            testHearth.AddLog();
            bool hearthIgnited = testHearth.Ignite(0.0f, 2.0f, true);
            float radiatedHeat = testHearth.HeatAt(1.0f);
            if (!hearthIgnited || !testHearth.Burning || radiatedHeat <= 0f)
                throw new InvalidOperationException("Evening refuge hearth ignition verification failed.");

            // Stone Knapping Workstation verification
            if (!StoneKnappingWorkstation.VerifyKnappingLogic(out string knapReceipt))
                throw new InvalidOperationException($"Stone knapping workstation verification failed: {knapReceipt}");

            // Stone Building Workstation verification
            if (!StoneBuildingWorkstation.VerifyBuildingLogic(out string buildReceipt))
                throw new InvalidOperationException($"Stone building workstation verification failed: {buildReceipt}");

            // Foraging Expedition Cycle verification
            if (!ForagingExpeditionCycle.VerifyForagingLogic(out string forageReceipt))
                throw new InvalidOperationException($"Foraging expedition verification failed: {forageReceipt}");

            string mapChecksFolder = Path.Combine(Path.GetTempPath(), "sf-map-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            if (Directory.Exists(mapChecksFolder)) Directory.Delete(mapChecksFolder, true);
            Directory.CreateDirectory(mapChecksFolder);
            var mapChecks = Starfall.Food.StarfallMapChecks.Run(mapChecksFolder);

            // Dual-Hand Carry & Leather Bag Expansion verification
            var dualHandChecks = CityLife.Items.DualHandCarryChecks.Run();

            // Marine Protein Crab verification
            if (!CoastalCrabDistribution.VerifyCrabEcology(out string crabReceipt))
                throw new InvalidOperationException($"Protein crab ecology verification failed: {crabReceipt}");

            // Tidal Driftwood Inflow verification
            if (!DriftwoodTideDeposit.VerifyDriftwoodEcology(out string driftReceipt))
                throw new InvalidOperationException($"Tidal driftwood ecology verification failed: {driftReceipt}");

            // Canyon Micro-Weather verification
            if (!CanyonMicroWeather.VerifyMicroWeather(out string weatherReceipt))
                throw new InvalidOperationException($"Canyon micro-weather verification failed: {weatherReceipt}");

            // Freshwater River Fish Ecology verification
            if (!RiverFishSchool.VerifyFishEcology(out string fishReceipt))
                throw new InvalidOperationException($"River fish ecology verification failed: {fishReceipt}");
            if (!RiverFishValidationTests.RunAllChecks(out string riverFishReceipt))
                throw new InvalidOperationException($"River fish validation suite failed: {riverFishReceipt}");

            // Coastal Timber Wolf Ecology verification
            if (!CoastalWolfEcology.VerifyWolfEcology(out string wolfReceipt))
                throw new InvalidOperationException($"Wolf ecology verification failed: {wolfReceipt}");

            // Riverbank Pilot Slice verification (grounding, hollow log cavity passage, waterline sedge clustering)
            RiverbankPilotAuthoring.ConfigureAssets();
            if (!RiverbankPilotSlice.VerifyPilotSlice(out string pilotReceipt))
                throw new InvalidOperationException($"Riverbank pilot slice verification failed: {pilotReceipt}");

            // River Fishing Mechanics & Atomic Catch-to-Hand/Store/Eat verification
            var fishingChecks = CityLife.Items.Editor.FishingIntegrationChecks.Run();

            // 'Go fish' Command Loop, Club Stowing & Public Negative Routing verification
            if (!GoFishCommandValidationTests.RunAllChecks(out string goFishReceipt, out var goFishPassed))
                throw new InvalidOperationException($"Go fish command validation failed: {goFishReceipt}");

            int totalPassed = foodChecks.Count + materialChecks.Count + checkpointChecks.Count +
                              basketPersistChecks.Count + caveFoodChecks.Count + stoneChecks.Count +
                              woodChecks.Count + mapChecks.Count + dualHandChecks.Count + fishingChecks.Count +
                              goFishPassed.Count + 17;
            Debug.Log($"STARFALL_INTEGRATED_VALIDATION_PASSED: {totalPassed} named checks verified across all AG1-AG5 lanes, dual-hand carry, survival cycle, masonry, map fog-of-war, marine crabs, tidal driftwood, micro-weathers, freshwater river drinking, South canyon waterfall cascade, river fish ecology, automated river fish kinematic visibility verification, waist moonbag, coastal timber wolf ecology, riverbank pilot slice, handcrafted river fishing rod mechanics, and 'go fish' directive/catch sequence loop with zero errors.");
            return totalPassed;
        }

        public static void RunValidationOnly()
        {
            if (!Requested || !Application.isBatchMode) throw new InvalidOperationException("Explicit isolated integrated batch required.");
            int passed = ExecuteAllChecks();
            Debug.Log($"[RunValidationOnly] SUCCESS: {passed} named checks verified in memory.");
            EditorApplication.Exit(0);
        }
        public static void Attach(Camera camera, GameObject ground, string folder)
        {
            CharacterAssetImport.Run();
            foreach (var collider in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)) collider.gameObject.layer = 8;
            ground.layer = 10;
            var actorObject = new GameObject("First coastal inhabitant"); actorObject.layer = 9;
            var actor = actorObject.AddComponent<CharacterPreviewActor>(); actor.ExternalDrive = true;
            actor.Capsule = actorObject.AddComponent<CharacterController>();
            actor.Capsule.height = 1.85f; actor.Capsule.center = new Vector3(0, .96f, 0); actor.Capsule.radius = .3f;
            actor.Capsule.skinWidth = .025f; actor.Capsule.stepOffset = .38f; actor.Capsule.slopeLimit = 52;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAssetImport.Body));
            model.transform.SetParent(actorObject.transform, false);
            model.transform.localPosition = new Vector3(0, .12f, 0);
            model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            actor.Animator = model.GetComponent<Animator>();
            if (actor.Animator.avatar == null || !actor.Animator.avatar.isValid || !actor.Animator.avatar.isHuman) throw new InvalidOperationException("Humanoid avatar invalid.");
            actor.Animator.applyRootMotion = false; actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var clips = AssetDatabase.LoadAllAssetsAtPath(CharacterAssetImport.Motions).OfType<AnimationClip>().ToArray();
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/IntegratedHumanoid.controller");
            foreach (var pair in new[] { ("Idle", "Idle_Loop"), ("Walk", "Walk_Loop"), ("Interact", "Interact"),
                ("Crouch", "Crouch_Idle_Loop"), ("CrouchWalk", "Crouch_Fwd_Loop"), ("Sit", "Sitting_Idle_Loop"),
                ("SitEnter", "Sitting_Enter"), ("SitExit", "Sitting_Exit"), ("Pickup", "PickUp_Table"),
                ("Swim", "Swim_Fwd_Loop"), ("SwimIdle", "Swim_Idle_Loop") })
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
            // Direct autonomous inhabitant configuration for the living world.
            // Test harness items (crystals & depot plinths) remain solely in isolated unit test stages.
            actor.ExternalDrive = true;
            camera.fieldOfView = 60;
            var brain = actorObject.AddComponent<NpcAutonomy>();
            brain.Actor = actor;
            brain.Perception = actorObject.AddComponent<NpcPerception>();
            brain.Log = actorObject.AddComponent<NpcDecisionLog>();
            brain.Registry = System.Array.Empty<NpcInteractable>();
            var hud = camera.gameObject.AddComponent<NpcDecisionHud>();
            hud.Brain = brain;
            hud.View = camera;
            var display = camera.gameObject.AddComponent<PreviewDisplayMode>();
            display.MenuOnly = true;
            var controls = camera.gameObject.AddComponent<NpcPlayerControls>();
            controls.Brain = brain;
            controls.Hud = hud;
            controls.View = actor.View;
            controls.Display = display;
            hud.Controls = controls;
            Time.fixedDeltaTime = NpcAutonomy.StepSeconds;

            brain.InstanceWorldId = NpcTerrainNavigation.RegionId;
            brain.TerrainNavigation = actorObject.AddComponent<NpcTerrainNavigation>();
            brain.Perception.WorldId = brain.InstanceWorldId;
            var activityOffset = new Vector3(CoastalTerrain.ActivityCentre.x, 0, CoastalTerrain.ActivityCentre.y);
            brain.OptionalPlanner = actorObject.AddComponent<NpcOptionalPlanner>(); brain.OptionalPlanner.Brain = brain;
            var livingMemory = actorObject.AddComponent<StarfallLivingMemoryRuntime>();
            // Spawn cleanly in the open river canyon valley on the east riverbank,
            // overlooking the freshwater river, crossing shallows, and southern waterfall gorge.
            float spawnX = 34.5f;
            float spawnZ = -22.0f;
            float spawnY = CoastalTerrain.Height(spawnX, spawnZ) + 0.05f;
            Vector3 canyonSpawn = new Vector3(spawnX, spawnY, spawnZ);
            brain.SpawnPosition = canyonSpawn;
            actor.Place(brain.SpawnPosition);
            controls.PersistentMouseCapture = true;
            controls.CameraMinimum = new Vector3(CoastalTerrain.MinX + 3, -1, CoastalTerrain.MinZ + 3);
            controls.CameraMaximum = new Vector3(CoastalTerrain.MaxX - 3, 220, CoastalTerrain.MaxZ - 3);
            camera.GetComponent<NpcDecisionHud>().Detailed = false;
            var camData = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (camData != null)
            {
                camData.requiresDepthTexture = true;
                camData.requiresColorTexture = true;
            }
            camera.fieldOfView = 60; actor.View.Yaw = -35; actor.View.Pitch = 12; actor.View.Distance = 4.8f; actor.View.Follow();
            var galaxy = GameObject.Find("Distant galaxy - procedural dust and stellar band");
            if (galaxy != null)
            {
                var skyTemplate = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                skyTemplate.name = "Surrounding procedural stars";
                Object.DestroyImmediate(skyTemplate.GetComponent<Collider>());
                skyTemplate.transform.localScale = Vector3.one * 9000;
                Shader envSkyShader = Shader.Find("Starfall/EnvironmentSky") ?? Shader.Find("Hidden/Starfall/CoastalGalaxy");
                var surroundingSky = new Material(envSkyShader) { name = "Surrounding stars", renderQueue = 998 };
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
            // Maintain spawn cleanly in the open river canyon valley on the east riverbank,
            // overlooking the river, crossing shallows, and southern waterfall gorge.
            brain.SpawnPosition = canyonSpawn;
            actor.Place(brain.SpawnPosition);
            actor.View.Yaw = -35; actor.View.Pitch = 12; actor.View.Distance = 4.8f; actor.View.Follow();
            var refuge = new GameObject("First refuge / discoverable place"); refuge.layer = 11; refuge.transform.position = refugeRuntime.Hearth;
            var refugeSensor = refuge.AddComponent<SphereCollider>(); refugeSensor.isTrigger = true; refugeSensor.radius = .4f;
            var place = refuge.AddComponent<NpcInteractable>(); place.StableId = "first-refuge"; place.WorldId = brain.InstanceWorldId;
            place.Kind = NpcObjectKind.Place; place.Approach = refuge.transform;
            var hearthCooker = refuge.AddComponent<CityLife.Food.HearthCooking>();
            hearthCooker.Refuge = refugeRuntime;
            hearthCooker.AuthoredHearthPosition = refugeRuntime.Hearth;
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
            actorObject.AddComponent<CityLife.Food.HearthCooking>().Refuge = refugeRuntime;
            var mapHud = camera.gameObject.AddComponent<StarfallMapHud>();
            mapHud.Brain=brain;mapHud.Survival=survival;mapHud.Food=food;mapHud.View=camera;survival.MapHud=mapHud;
            var deathDiagnostic=camera.gameObject.AddComponent<StarfallSurvivalDeathAcceptance>();
            deathDiagnostic.Brain=brain;deathDiagnostic.Survival=survival;deathDiagnostic.Food=food;deathDiagnostic.View=camera;
            var gameCapture=camera.gameObject.AddComponent<StarfallSurvivalGameCapture>();
            gameCapture.Brain=brain;gameCapture.View=camera;

            // Hunter Waist Moonbag: side belt pouch holds up to 2 fruits for long journeys
            var moonbag = actorObject.AddComponent<HunterMoonbag>();
            var sourfigFruitMat = Material("Sourfig Fruit Material", new Color(0.72f, 0.18f, 0.52f));
            var hipsBone = actor.Animator != null ? actor.Animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hipsBone != null) moonbag.AttachVisuals(hipsBone, sourfigFruitMat);

            // Physical item foundation integration: bind authoritative ItemModel, PhysicalAuthority, and physical demonstration item to the canyon inhabitant
            var physicalBootstrap = actorObject.AddComponent<CityLife.Items.PhysicalItemBootstrap>();
            physicalBootstrap.Brain = brain; brain.PhysicalItems = physicalBootstrap;
            var demoMaterial = Material("Physical demonstration stone", new Color(0.56f, 0.54f, 0.52f));
            if (demoMaterial == null || demoMaterial.shader == null || !demoMaterial.shader.isSupported)
                throw new InvalidOperationException("Demonstration stone material requires a valid, supported Universal Render Pipeline/Lit shader.");
            physicalBootstrap.DemonstrationMaterial = demoMaterial;

            // Natural material definitions for procedural resources
            var stoneMat = Material("Natural coastal stone", new Color(0.55f, 0.52f, 0.48f));
            var tinderMat = Material("Tinder dry brush", new Color(0.48f, 0.38f, 0.22f));
            var woodMat = Material("Fallen wood bark", new Color(0.38f, 0.26f, 0.16f));
            physicalBootstrap.StoneMaterial = stoneMat;
            physicalBootstrap.TinderMaterial = tinderMat;
            physicalBootstrap.WoodMaterial = woodMat;

            // Basket foundation & player integration: serialized basket material, opt-in starter layout, and container panel wiring
            var basketMaterial = Material("Woven basket material", new Color(0.62f, 0.46f, 0.28f));
            if (basketMaterial == null || basketMaterial.shader == null || !basketMaterial.shader.isSupported)
                throw new InvalidOperationException("Basket material requires a valid, supported Universal Render Pipeline/Lit shader.");
            physicalBootstrap.BasketMaterial = basketMaterial;
            physicalBootstrap.OptInStarterLayout = false;

            var containerPanel = camera.gameObject.AddComponent<PhysicalContainerPanel>();
            containerPanel.Controls = controls;
            containerPanel.Bootstrap = physicalBootstrap;
            controls.ContainerPanel = containerPanel;

            var fishing = actorObject.AddComponent<FishingInteraction>();
            fishing.Brain = brain;
            controls.Fishing = fishing;
            actorObject.AddComponent<FishingCampSupplies>().Brain = brain;

            // Autonomous Evening Refuge Fire Survival Cycle
            var eveningFire = actorObject.AddComponent<EveningRefugeFireCycle>();
            eveningFire.Brain = brain;
            eveningFire.Refuge = refugeRuntime;
            eveningFire.Scanner = null;
            eveningFire.Bootstrap = physicalBootstrap;
            eveningFire.Environment = environment;

            // Stone Knapping Workstation: crafted sharp blade & fire striker
            var knappingObj = new GameObject("Stone knapping workstation");
            knappingObj.transform.SetParent(ground.transform, false);
            Vector3 knapPos = new Vector3(CoastalTerrain.ActivityCentre.x + 1.4f, 0, CoastalTerrain.ActivityCentre.y - 1.8f);
            knapPos.y = CoastalTerrain.Height(knapPos.x, knapPos.z);
            knappingObj.transform.position = knapPos;
            var knapAnvil = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            knapAnvil.name = "Anvil stone";
            knapAnvil.transform.SetParent(knappingObj.transform, false);
            knapAnvil.transform.localScale = new Vector3(0.5f, 0.25f, 0.5f);
            knapAnvil.transform.localPosition = new Vector3(0, 0.25f, 0);
            var anvilRend = knapAnvil.GetComponent<MeshRenderer>();
            if (anvilRend != null) anvilRend.sharedMaterial = stoneMat;
            var knapping = knappingObj.AddComponent<StoneKnappingWorkstation>();
            knapping.Brain = brain;
            knapping.Bootstrap = physicalBootstrap;
            knapping.AnvilPoint = knapAnvil.transform;

            // Stone Building Workstation: Masonry & Hearth/Windbreak Construction on Canyon Terrace
            var buildingObj = new GameObject("Stone building workstation");
            buildingObj.transform.SetParent(ground.transform, false);
            Vector3 buildPos = new Vector3(CoastalTerrain.ActivityCentre.x + 3.8f, 0, CoastalTerrain.ActivityCentre.y - 3.2f);
            buildPos.y = CoastalTerrain.Height(buildPos.x, buildPos.z);
            buildingObj.transform.position = buildPos;
            var building = buildingObj.AddComponent<StoneBuildingWorkstation>();
            building.Brain = brain;
            building.Bootstrap = physicalBootstrap;
            building.ConstructionSite = buildPos;

            // Autonomous Foraging Expedition Cycle
            var foraging = actorObject.AddComponent<ForagingExpeditionCycle>();
            foraging.Brain = brain;
            foraging.Refuge = refugeRuntime;
            foraging.Bootstrap = physicalBootstrap;
            foraging.Hud = camera.GetComponent<NpcDecisionHud>();
            foraging.BuildingWorkstation = building;
            foraging.KnappingWorkstation = knapping;
            foraging.Scanner = null;
            brain.Foraging = foraging;

            // Natural Stone Supply (AG3): place procedural river cobbles, fieldstones, and flat slabs on activity terrace
            var stoneGroup = new GameObject("Natural stone supply points");
            stoneGroup.transform.SetParent(ground.transform, false);

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

            var slabObj = new GameObject("Natural flat slab");
            slabObj.transform.SetParent(stoneGroup.transform, false);
            slabObj.transform.position = new Vector3(CoastalTerrain.ActivityCentre.x + 1.2f, CoastalTerrain.Height(CoastalTerrain.ActivityCentre.x + 1.2f, CoastalTerrain.ActivityCentre.y + 3.5f), CoastalTerrain.ActivityCentre.y + 3.5f);
            var slabMesh = CityLife.Stones.StoneMeshGenerator.GenerateMesh(CityLife.Stones.StoneShapeKind.FlatSlab, seed: 303, variantIndex: 0, uniformScale: 1.1f, flatShaded: true);
            slabObj.AddComponent<MeshFilter>().sharedMesh = slabMesh;
            slabObj.AddComponent<MeshRenderer>().sharedMaterial = stoneMat;
            var slabCol = slabObj.AddComponent<MeshCollider>();
            slabCol.sharedMesh = slabMesh;
            slabObj.layer = 8;

            // Tinder & Night Fire (AG4): place procedural dry-brush tinder bundle near refuge hearth
            var tinderParams = CityLife.Fire.TinderParameters.ForLod(0, 4217);
            var tinderObj = new GameObject("Dry brush tinder bundle");
            tinderObj.transform.SetParent(ground.transform, false);
            Vector3 tinderPos = refugeRuntime.Hearth + new Vector3(0.6f, 0, 0.4f);
            tinderPos.y = CoastalTerrain.Height(tinderPos.x, tinderPos.z);
            tinderObj.transform.position = tinderPos;
            var tinderMesh = CityLife.Fire.TinderGeometry.GenerateMesh(tinderParams);
            tinderObj.AddComponent<MeshFilter>().sharedMesh = tinderMesh;
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
            woodObj.AddComponent<MeshRenderer>().sharedMaterial = woodMat;
            var woodCol = woodObj.AddComponent<MeshCollider>();
            woodCol.sharedMesh = branchMesh;
            woodObj.layer = 8;

            // Natural River Bed Pebbles & Shallow Crossing Cobbles
            var riverStones = new (float x, float z, float scale, bool isPebble, int seed)[]
            {
                // Southern upstream shallow gravel beds
                (12f, -160f, 0.60f, true, 501),
                (15f, -140f, 0.65f, true, 502),
                (17f, -118f, 1.10f, false, 503), // River crossing cobble
                (14f, -95f, 0.58f, true, 504),
                // Mid-canyon river meanders & sandbars
                (8f, -70f, 0.62f, true, 505),
                (5f, -48f, 0.55f, true, 506),
                (2f, -25f, 1.15f, false, 507),  // Shallow crossing cobble
                (-1f, -6f, 0.64f, true, 508),
                (3f, 14f, 0.58f, true, 509),
                (8f, 35f, 1.05f, false, 510),   // Stepping cobble
                // Northern downstream shallows & gravel bars
                (18f, 60f, 0.60f, true, 511),
                (28f, 85f, 0.56f, true, 512),
                (42f, 110f, 1.20f, false, 513),  // Lower crossing cobble
                (55f, 132f, 0.68f, true, 514),
                (64f, 155f, 0.62f, true, 515),
                // Shallow margin pebbles (easily reached from banks)
                (9f, -12f, 0.52f, true, 516),
                (-4f, 22f, 0.54f, true, 517),
                (22f, 48f, 0.60f, true, 518)
            };

            var riverPebbleInteractables = new List<NpcInteractable>();
            for (int i = 0; i < riverStones.Length; i++)
            {
                var s = riverStones[i];
                float ry = CoastalTerrain.Height(s.x, s.z);
                string stoneId = s.isPebble ? $"river-pebble-{i + 1:D2}" : $"river-cobble-{i + 1:D2}";
                string stoneTypeId = s.isPebble ? "stone-river-pebble" : "stone-river-cobble";
                float stoneMass = s.isPebble ? 0.65f : 1.8f;
                Vector3 stoneDim = s.isPebble ? new Vector3(0.12f, 0.08f, 0.10f) * (s.scale / 0.6f) : new Vector3(0.24f, 0.16f, 0.20f) * (s.scale / 1.1f);

                var stoneObj = new GameObject(stoneId);
                stoneObj.transform.SetParent(stoneGroup.transform, false);
                stoneObj.transform.position = new Vector3(s.x, ry + stoneDim.y * 0.45f, s.z);
                stoneObj.transform.rotation = Quaternion.Euler((s.seed * 29) % 360, (s.seed * 53) % 360, (s.seed * 17) % 360);

                var stoneMesh = CityLife.Stones.StoneMeshGenerator.GenerateMesh(
                    CityLife.Stones.StoneShapeKind.RiverCobble,
                    seed: s.seed,
                    variantIndex: i % 3,
                    uniformScale: s.scale,
                    flatShaded: true
                );
                stoneObj.AddComponent<MeshFilter>().sharedMesh = stoneMesh;
                stoneObj.AddComponent<MeshRenderer>().sharedMaterial = stoneMat;

                var stoneCol = stoneObj.AddComponent<MeshCollider>();
                stoneCol.sharedMesh = stoneMesh;
                stoneCol.convex = true;
                stoneObj.layer = 8;

                var approachObj = new GameObject(stoneId + " approach");
                approachObj.transform.SetParent(stoneObj.transform, false);
                approachObj.transform.localPosition = Vector3.zero;

                var ni = stoneObj.AddComponent<NpcInteractable>();
                ni.StableId = stoneId;
                ni.WorldId = brain.InstanceWorldId;
                ni.Kind = NpcObjectKind.Item;
                ni.Permission = true;
                ni.Approach = approachObj.transform;

                var phys = stoneObj.AddComponent<PhysicalItem>();
                phys.itemId = stoneId;
                phys.itemTypeId = stoneTypeId;
                phys.massKg = stoneMass;
                phys.dimensions = new PhysicalDimensions(stoneDim.x, stoneDim.y, stoneDim.z);
                phys.ConfigureComponents();

                riverPebbleInteractables.Add(ni);
            }
            brain.Registry = brain.Registry.Concat(riverPebbleInteractables).ToArray();

            // Leather gathering bag for pebble storage & carry capacity expansion
            var bagObj = new GameObject("inhabitant-leather-bag");
            bagObj.transform.SetParent(ground.transform, false);
            Vector3 bagPos = refugeRuntime.Storage + new Vector3(0.3f, 0.1f, 0.3f);
            bagPos.y = CoastalTerrain.Height(bagPos.x, bagPos.z) + 0.15f;
            bagObj.transform.position = bagPos;
            var bagCol = bagObj.AddComponent<BoxCollider>();
            bagCol.size = new Vector3(0.25f, 0.20f, 0.20f);
            bagObj.layer = 8;

            var bagApproachObj = new GameObject("leather bag approach");
            bagApproachObj.transform.SetParent(bagObj.transform, false);
            bagApproachObj.transform.localPosition = Vector3.zero;

            var bagNi = bagObj.AddComponent<NpcInteractable>();
            bagNi.StableId = "inhabitant-leather-bag";
            bagNi.WorldId = brain.InstanceWorldId;
            bagNi.Kind = NpcObjectKind.Item;
            bagNi.Permission = true;
            bagNi.Approach = bagApproachObj.transform;

            var bagPhys = bagObj.AddComponent<PhysicalItem>();
            bagPhys.itemId = "inhabitant-leather-bag";
            bagPhys.itemTypeId = "container-leather-bag";
            bagPhys.massKg = 0.45f;
            bagPhys.dimensions = new PhysicalDimensions(0.25f, 0.20f, 0.20f);
            bagPhys.ConfigureComponents();
            brain.Registry = brain.Registry.Concat(new[] { bagNi }).ToArray();

            // Driftwood along the Riverbank (washed down from upper canyon waterfall)
            var driftwoodObj = new GameObject("Riverbank driftwood branch");
            driftwoodObj.transform.SetParent(woodGroup.transform, false);
            Vector3 driftPos = new Vector3(22.0f, CoastalTerrain.Height(22.0f, -65.0f), -65.0f);
            driftwoodObj.transform.position = driftPos;
            var driftProfile = CityLife.Wood.FallenWoodProfile.CreateBranchPreset();
            var driftMesh = CityLife.Wood.FallenWoodGenerator.GenerateMesh(driftProfile, 0x7E3F101Au, out _);
            driftwoodObj.AddComponent<MeshFilter>().sharedMesh = driftMesh;
            driftwoodObj.AddComponent<MeshRenderer>().sharedMaterial = woodMat;
            var driftCol = driftwoodObj.AddComponent<MeshCollider>();
            driftCol.sharedMesh = driftMesh;
            driftwoodObj.layer = 8;

            // Tidal Protein Crabs along coastal shallows & mudflats
            var crabObjects = CoastalCrabDistribution.SpawnCrabs(ground.transform);
            var crabInteractables = new List<NpcInteractable>();
            for (int i = 0; i < crabObjects.Count; i++)
            {
                var crabObj = crabObjects[i];
                string crabId = $"coastal-crab-{i + 1:D2}";
                var crabApproach = new GameObject(crabId + " approach");
                crabApproach.transform.SetParent(crabObj.transform, false);
                crabApproach.transform.localPosition = Vector3.zero;

                var crabNi = crabObj.AddComponent<NpcInteractable>();
                crabNi.StableId = crabId;
                crabNi.WorldId = brain.InstanceWorldId;
                crabNi.Kind = NpcObjectKind.Item;
                crabNi.Permission = true;
                crabNi.Approach = crabApproach.transform;

                var crabPhys = crabObj.AddComponent<PhysicalItem>();
                crabPhys.itemId = crabId;
                crabPhys.itemTypeId = "food-protein-crab";
                crabPhys.massKg = 0.45f;
                crabPhys.dimensions = new PhysicalDimensions(0.22f, 0.16f, 0.09f);
                crabPhys.ConfigureComponents();

                crabInteractables.Add(crabNi);
            }
            brain.Registry = brain.Registry.Concat(crabInteractables).ToArray();

            // Tidal Inflowing Driftwood Logs along delta sandbars
            var driftObjects = DriftwoodTideDeposit.SpawnDriftwood(ground.transform);
            var driftInteractables = new List<NpcInteractable>();
            for (int i = 0; i < driftObjects.Count; i++)
            {
                var driftGo = driftObjects[i];
                string driftId = $"tidal-driftwood-{i + 1:D2}";
                var driftApproach = new GameObject(driftId + " approach");
                driftApproach.transform.SetParent(driftGo.transform, false);
                driftApproach.transform.localPosition = Vector3.zero;

                var driftNi = driftGo.AddComponent<NpcInteractable>();
                driftNi.StableId = driftId;
                driftNi.WorldId = brain.InstanceWorldId;
                driftNi.Kind = NpcObjectKind.Item;
                driftNi.Permission = true;
                driftNi.Approach = driftApproach.transform;

                var driftPhys = driftGo.AddComponent<PhysicalItem>();
                driftPhys.itemId = driftId;
                driftPhys.itemTypeId = "wood-driftwood-log";
                driftPhys.massKg = 4.2f;
                driftPhys.dimensions = new PhysicalDimensions(0.95f, 0.22f, 0.22f);
                driftPhys.ConfigureComponents();

                driftInteractables.Add(driftNi);
            }
            brain.Registry = brain.Registry.Concat(driftInteractables).ToArray();

            // Dynamic River Driftwood Flotsam Flow downstream from waterfall with continuous replenishment
            var riverFlow = RiverDriftwoodFlow.Create(ground.transform, brain.InstanceWorldId);
            var flowInteractables = new List<NpcInteractable>();
            for (int i = 0; i < riverFlow.ActiveLogs.Count; i++)
            {
                var log = riverFlow.ActiveLogs[i];
                if (log.interactable != null) flowInteractables.Add(log.interactable);
            }
            brain.Registry = brain.Registry.Concat(flowInteractables).ToArray();

            // Canyon Riverbank World Decoration (waterline pebble beds, riparian reeds, dry tussocks)
            RiverbankDecoration.SpawnDecorations(ground.transform);

            // Coherent Riverbank Pilot Slice (weathered boulders, hollow driftwood log with open cavity, riparian sedges)
            RiverbankPilotSlice.SpawnPilotSlice(ground.transform);

            // Freshwater River Fish Schools (Catfish / Barber & Gauteng Common Carp) in the river channel
            var catfishPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CityLife/Art/Catfish/catfish_swim.fbx");
            var catfishMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/CityLife/Art/Catfish/Catfish_Material.mat");
            var carpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CityLife/Art/Carp/carp_swim.fbx");
            var carpMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/CityLife/Art/Carp/Carp_Material.mat");
            var fishObjects = RiverFishSchool.SpawnFishSchool(ground.transform, brain.InstanceWorldId, out var fishSchoolComp, catfishPrefab, catfishMat, carpPrefab, carpMat);
            var fishInteractables = new List<NpcInteractable>();
            for (int i = 0; i < fishSchoolComp.ActiveFish.Count; i++)
            {
                var f = fishSchoolComp.ActiveFish[i];
                if (f.interactable != null) fishInteractables.Add(f.interactable);
            }
            brain.Registry = brain.Registry.Concat(fishInteractables).ToArray();

            // Starter rod on accessible shore; the former boulder shelf failed
            // navigation and left the nearest reachable point outside pickup range.
            Vector3 rodPos = FishingRodItem.InitialWorldPosition;
            var worldRod = FishingRodItem.SpawnWorldFishingRod(ground.transform, brain.InstanceWorldId, rodPos, "tool-fishing-rod-01");
            var rodNi = worldRod.GetComponent<NpcInteractable>();
            if (rodNi != null)
            {
                brain.Registry = brain.Registry.Concat(new[] { rodNi }).ToArray();
            }

            // Autonomous Coastal Timber Wolves in rugged upper canyon slopes
            var wolfObjects = CoastalWolfEcology.SpawnWolves(ground.transform);
            var wolfInteractables = new List<NpcInteractable>();
            for (int i = 0; i < wolfObjects.Count; i++)
            {
                var wolfNi = wolfObjects[i].GetComponent<NpcInteractable>();
                if (wolfNi != null) wolfInteractables.Add(wolfNi);
            }
            brain.Registry = brain.Registry.Concat(wolfInteractables).ToArray();

            // Secondary Cave Hearth Station at Refuge Cave entrance
            var caveHearthObj = new GameObject("Refuge Cave Hearth Station");
            caveHearthObj.transform.SetParent(ground.transform, false);
            Vector3 caveHearthPos = new Vector3(CoastalTerrain.RefugeCentre.x + 2.5f, CoastalTerrain.Height(CoastalTerrain.RefugeCentre.x + 2.5f, CoastalTerrain.RefugeCentre.y - 1.0f), CoastalTerrain.RefugeCentre.y - 1.0f);
            caveHearthObj.transform.position = caveHearthPos;
            var caveHearthComp = caveHearthObj.AddComponent<StoneBuildingWorkstation>();
            caveHearthComp.Brain = brain;
            caveHearthComp.Bootstrap = physicalBootstrap;
            caveHearthComp.ConstructionSite = caveHearthPos;
            caveHearthComp.CurrentTarget = StoneStructureKind.HearthRing;

            // Fortified Wolf Defense Perimeter Wall Workstation at Refuge Cave entrance
            var refugeWallObj = new GameObject("Refuge Wolf Defense Wall Station");
            refugeWallObj.transform.SetParent(ground.transform, false);
            Vector3 refugeWallPos = new Vector3(CoastalTerrain.RefugeCentre.x + 3.8f, CoastalTerrain.Height(CoastalTerrain.RefugeCentre.x + 3.8f, CoastalTerrain.RefugeCentre.y + 0.4f), CoastalTerrain.RefugeCentre.y + 0.4f);
            refugeWallObj.transform.position = refugeWallPos;
            var refugeWallComp = refugeWallObj.AddComponent<StoneBuildingWorkstation>();
            refugeWallComp.Brain = brain;
            refugeWallComp.Bootstrap = physicalBootstrap;
            refugeWallComp.ConstructionSite = refugeWallPos;
            refugeWallComp.CurrentTarget = StoneStructureKind.PackedWolfShelter;
            refugeWallComp.UpdateRequirementsForTarget(StoneStructureKind.PackedWolfShelter);

            // South Canyon Waterfall Cascade and Foaming Plunge Pool
            CreateWaterfallCascade(ground.transform, folder);

            // Canyon Arid Dry Grass tufts across terraces, riverbank, and refuge
            CreateCanyonDryGrass(ground.transform, folder);

            // Spread-out Kokerboom tree distribution across canyon ridges, terraces, and riverbanks
            var treeGroup = new GameObject("Canyon Kokerboom trees");
            treeGroup.transform.SetParent(ground.transform, false);
            var treePlacements = new (float x, float z, float age, int seed)[]
            {
                // East Terrace Rim Overlooks
                (142f, -62f, 0.95f, 4201),
                (115f, -112f, 0.75f, 4202),
                (92f, -38f, 0.85f, 4203),
                (152f, -88f, 0.60f, 4204),
                // Riverbank Foothills & Meanders
                (36f, -44f, 0.80f, 4205),
                (42f, 18f, 0.70f, 4206),
                (-32f, 48f, 0.90f, 4207),
                (-48f, 122f, 0.65f, 4208),
                (24f, -125f, 0.75f, 4209),
                // Refuge Cave Rock Shelves & Exterior Benches
                (-138f, 132f, 0.90f, 4210),
                (-178f, 96f, 0.80f, 4211),
                (-122f, 145f, 0.55f, 4212),
                // Canyon Floor & Wall Overlook Benches
                (68f, -148f, 0.70f, 4213),
                (-78f, -28f, 0.85f, 4214),
                (76f, 98f, 0.65f, 4215),
                (-98f, 192f, 0.90f, 4216),
                // Southern Waterfall Approach
                (18f, -172f, 0.80f, 4217),
                (-22f, -215f, 0.75f, 4218),
                (32f, -265f, 0.60f, 4219)
            };
            foreach (var tp in treePlacements)
            {
                float ty = CoastalTerrain.Height(tp.x, tp.z);
                if (ty <= CoastalWater.Level + 1.2f) continue;
                // Share 2 cached archetypes at LOD 1 to keep binary asset footprint well within Unity's 2GB file limit
                int archSeed = (tp.seed % 2 == 0) ? 4242 : 4243;
                float archAge = (tp.seed % 2 == 0) ? 0.85f : 0.65f;
                var tree = KokerboomGeometry.Create(archSeed, archAge, 1);
                if (tree != null)
                {
                    tree.transform.SetParent(treeGroup.transform, false);
                    tree.transform.position = new Vector3(tp.x, ty, tp.z);
                    tree.transform.rotation = Quaternion.Euler(0, (tp.seed * 37) % 360, 0);
                    float scale = Mathf.Lerp(0.85f, 1.15f, (tp.seed % 7) / 6f);
                    tree.transform.localScale = Vector3.one * scale;
                }
            }

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
            camera.gameObject.AddComponent<StarfallAmbientSoundscape>();
            var acceptance = camera.gameObject.AddComponent<IntegratedAcceptance>(); acceptance.Brain = brain; acceptance.Controls = controls; acceptance.Environment = environment; acceptance.Food = food;
            Time.fixedDeltaTime = .02f; Physics.gravity = Vector3.down * 9.81f;
            File.WriteAllText(folder + "/component-binding.json", JsonUtility.ToJson(new Binding(), true));
        }
        [Serializable] private sealed class Binding
        {
            public string worldId = NpcTerrainNavigation.RegionId, terrain = CoastalTerrain.ContentRevision,
                scope = "Finite 1200x1600m Fish River Canyon inspired candidate with a traversable turquoise river-to-sea corridor, terrain-fitted authored First Refuge, visible berry bush, environment, inhabitant, food model and local thought protocol. Boats and unified save/load remain unimplemented.";
        }

        private static GameObject CreateWaterfallCascade(Transform parent, string folder)
        {
            var root = new GameObject("South Canyon Waterfall Cascade");
            root.transform.SetParent(parent, false);

            Shader waterShader = Shader.Find("CityLife/WaterfallCascade") ?? Shader.Find("CityLife/CoastalWater") ?? Shader.Find("Universal Render Pipeline/Unlit");

            var cascadeMat = new Material(waterShader) { name = "Waterfall cascade procedural" };
            if (waterShader.name == "CityLife/WaterfallCascade")
            {
                cascadeMat.SetColor("_DeepColor", new Color(0.04f, 0.45f, 0.65f, 0.92f));
                cascadeMat.SetColor("_ShallowColor", new Color(0.12f, 0.88f, 0.92f, 0.88f));
                cascadeMat.SetColor("_FoamColor", new Color(0.96f, 0.98f, 1.0f, 0.98f));
                cascadeMat.SetFloat("_FlowSpeedMain", 4.2f);
                cascadeMat.SetFloat("_FlowSpeedTurbulent", 7.5f);
                cascadeMat.SetFloat("_FoamAeration", 0.48f);
                cascadeMat.SetFloat("_EdgeSoftness", 0.65f);
            }
            else if (waterShader.name == "CityLife/CoastalWater")
            {
                cascadeMat.SetColor("_ShallowColor", new Color(0.15f, 0.92f, 0.95f, 0.9f));
                cascadeMat.SetColor("_RiverColor", new Color(0.08f, 0.78f, 0.88f, 0.92f));
                cascadeMat.SetColor("_DeepColor", new Color(0.04f, 0.45f, 0.65f, 0.95f));
                cascadeMat.SetColor("_FoamColor", new Color(0.92f, 0.98f, 1.0f, 0.98f));
                cascadeMat.SetFloat("_WaveStrength", 1.0f);
            }
            if (!string.IsNullOrEmpty(folder))
            {
                AssetDatabase.CreateAsset(cascadeMat, folder + "/WaterfallCascade.mat");
            }

            // Material for outer volumetric spray veil
            var veilMat = new Material(cascadeMat) { name = "Waterfall spray veil" };
            if (waterShader.name == "CityLife/WaterfallCascade")
            {
                veilMat.SetFloat("_FoamAeration", 0.65f);
                veilMat.SetFloat("_FlowSpeedTurbulent", 9.5f);
            }
            if (!string.IsNullOrEmpty(folder))
            {
                AssetDatabase.CreateAsset(veilMat, folder + "/WaterfallSprayVeil.mat");
            }

            // Dedicated material for churning radial plunge pool with expanding ripples
            var poolMat = new Material(cascadeMat) { name = "Waterfall plunge pool procedural" };
            if (waterShader.name == "CityLife/WaterfallCascade")
            {
                poolMat.SetFloat("_IsPlungePool", 1.0f);
                poolMat.SetFloat("_FoamAeration", 0.72f);
                poolMat.SetFloat("_FlowSpeedTurbulent", 5.5f);
            }
            if (!string.IsNullOrEmpty(folder))
            {
                AssetDatabase.CreateAsset(poolMat, folder + "/WaterfallPlungePool.mat");
            }

            Mesh BuildFlowingCurtain(string name, int sx, int sy, float x0, float x1, float yTop, float yBot, float zTop, float zBot, float bowOffset, float uvYScale, float wingDepth = 5.5f)
            {
                var verts = new Vector3[(sx + 1) * (sy + 1)];
                var norms = new Vector3[verts.Length];
                var uvs = new Vector2[verts.Length];
                var tris = new int[sx * sy * 6];

                for (int y = 0; y <= sy; y++)
                {
                    float ty = y / (float)sy;
                    float curY = Mathf.Lerp(yTop, yBot, ty);
                    float curZ = Mathf.Lerp(zTop, zBot, Mathf.Pow(ty, 0.72f)) + Mathf.Sin(ty * Mathf.PI) * bowOffset;

                    for (int x = 0; x <= sx; x++)
                    {
                        float tx = x / (float)sx;
                        Vector3 pos;
                        Vector3 n;

                        if (tx < 0.22f)
                        {
                            // West return wing: extends back into west basalt rock chasm wall
                            float wingT = tx / 0.22f; // 0 deep inside rock wall, 1 at front corner
                            float wx = Mathf.Lerp(x0 - 2.8f, x0, wingT);
                            float wz = Mathf.Lerp(curZ - wingDepth, curZ, wingT);
                            float ripple = Mathf.Sin(wingT * 12f + ty * 20f) * 0.18f;
                            pos = new Vector3(wx + ripple * 0.5f, curY, wz + ripple);
                            n = Vector3.Lerp(new Vector3(-1f, 0.2f, 0.1f), new Vector3(-0.4f, 0.3f, 0.8f), wingT).normalized;
                        }
                        else if (tx > 0.78f)
                        {
                            // East return wing: extends back into east basalt rock chasm wall
                            float wingT = (tx - 0.78f) / 0.22f; // 0 at front corner, 1 deep inside rock wall
                            float ex = Mathf.Lerp(x1, x1 + 2.8f, wingT);
                            float ez = Mathf.Lerp(curZ, curZ - wingDepth, wingT);
                            float ripple = Mathf.Sin(wingT * 12f + ty * 20f) * 0.18f;
                            pos = new Vector3(ex - ripple * 0.5f, curY, ez + ripple);
                            n = Vector3.Lerp(new Vector3(0.4f, 0.3f, 0.8f), new Vector3(1f, 0.2f, 0.1f), wingT).normalized;
                        }
                        else
                        {
                            // Main front torrent face
                            float frontT = (tx - 0.22f) / 0.56f; // 0 at x0, 1 at x1
                            float fx = Mathf.Lerp(x0, x1, frontT);
                            float ripple = Mathf.Sin(frontT * 14f + ty * 20f) * 0.28f;
                            pos = new Vector3(fx, curY, curZ + ripple);
                            n = new Vector3(0f, 0.3f, 1f).normalized;
                        }

                        int idx = y * (sx + 1) + x;
                        verts[idx] = pos;
                        norms[idx] = n;
                        uvs[idx] = new Vector2(tx, ty * uvYScale);
                    }
                }

                int t = 0;
                for (int y = 0; y < sy; y++)
                {
                    for (int x = 0; x < sx; x++)
                    {
                        int a = y * (sx + 1) + x;
                        int b = a + 1;
                        int c = a + (sx + 1);
                        int d = c + 1;
                        tris[t++] = a; tris[t++] = c; tris[t++] = b;
                        tris[t++] = b; tris[t++] = c; tris[t++] = d;
                    }
                }

                var m = new Mesh { name = name };
                m.vertices = verts;
                m.normals = norms;
                m.uv = uvs;
                m.triangles = tris;
                m.RecalculateBounds();
                return m;
            }

            // 1. Tier 1: Upper Hanging Gorge Chute (from Y=27.5m, Z=-258m down to Y=15.0m, Z=-248m)
            var upperMesh = BuildFlowingCurtain("Upper Gorge Chute", 22, 18, 17f, 33f, 27.5f, 15.0f, -258f, -248f, 0.5f, 3.5f, 6.0f);
            var upperObj = new GameObject("Waterfall Tier 1 Upper Chute");
            upperObj.transform.SetParent(root.transform, false);
            upperObj.AddComponent<MeshFilter>().sharedMesh = upperMesh;
            upperObj.AddComponent<MeshRenderer>().sharedMaterial = cascadeMat;

            // 2. Mid Basalt Churn Ledge Shelf (crashed water boiling horizontally across rock shelf at Y=15.0m to 13.8m)
            var midShelfMesh = BuildFlowingCurtain("Mid Churn Ledge", 22, 10, 16.5f, 33.5f, 15.0f, 13.8f, -248f, -245f, 0.25f, 2.0f, 5.0f);
            var midObj = new GameObject("Waterfall Tier 2 Mid Ledge");
            midObj.transform.SetParent(root.transform, false);
            midObj.AddComponent<MeshFilter>().sharedMesh = midShelfMesh;
            midObj.AddComponent<MeshRenderer>().sharedMaterial = cascadeMat;

            // 3. Tier 3: Main Roaring Plunge (from Y=13.8m down to Y=-2.6m penetrating deep into pool water)
            var mainMesh = BuildFlowingCurtain("Main Plunge Curtain", 24, 24, 16.5f, 33.5f, 13.8f, -2.6f, -245f, -237f, 0.85f, 5.0f, 7.5f);
            var mainObj = new GameObject("Waterfall Tier 3 Main Plunge");
            mainObj.transform.SetParent(root.transform, false);
            mainObj.AddComponent<MeshFilter>().sharedMesh = mainMesh;
            mainObj.AddComponent<MeshRenderer>().sharedMaterial = cascadeMat;

            // 4. Volumetric Spray & Mist Veil (slightly forward-offset curtain +0.45m)
            var veilMesh = BuildFlowingCurtain("Volumetric Mist Veil", 20, 20, 15.8f, 34.2f, 14.0f, -2.5f, -244.5f, -236.4f, 1.15f, 4.0f, 7.0f);
            var veilObj = new GameObject("Waterfall Spray Veil");
            veilObj.transform.SetParent(root.transform, false);
            veilObj.AddComponent<MeshFilter>().sharedMesh = veilMesh;
            veilObj.AddComponent<MeshRenderer>().sharedMaterial = veilMat;

            // 5. Churning Radial Plunge Pool Disc (at Y=-1.95m, 5cm above river water level -2.0m)
            int poolSegs = 32;
            var pVerts = new Vector3[poolSegs + 2];
            var pNorms = new Vector3[poolSegs + 2];
            var pUvs = new Vector2[poolSegs + 2];
            var pTris = new int[poolSegs * 3];

            Vector3 poolCenter = new Vector3(25f, -1.95f, -236f);
            float radiusX = 14.5f, radiusZ = 12.0f;
            pVerts[0] = poolCenter;
            pNorms[0] = Vector3.up;
            pUvs[0] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i <= poolSegs; i++)
            {
                float angle = (i % poolSegs) * (Mathf.PI * 2f / poolSegs);
                float px = poolCenter.x + Mathf.Cos(angle) * radiusX;
                float pz = poolCenter.z + Mathf.Sin(angle) * radiusZ;
                pVerts[i + 1] = new Vector3(px, poolCenter.y, pz);
                pNorms[i + 1] = Vector3.up;
                pUvs[i + 1] = new Vector2(0.5f + Mathf.Cos(angle) * 0.5f, 0.5f + Mathf.Sin(angle) * 0.5f);
            }

            int pt = 0;
            for (int i = 0; i < poolSegs; i++)
            {
                pTris[pt++] = 0;
                pTris[pt++] = i + 1;
                pTris[pt++] = i + 2;
            }

            var poolMesh = new Mesh { name = "Waterfall plunge pool procedural" };
            poolMesh.vertices = pVerts;
            poolMesh.normals = pNorms;
            poolMesh.uv = pUvs;
            poolMesh.triangles = pTris;
            poolMesh.RecalculateBounds();

            var poolObj = new GameObject("Waterfall Plunge Pool");
            poolObj.transform.SetParent(root.transform, false);
            poolObj.AddComponent<MeshFilter>().sharedMesh = poolMesh;
            poolObj.AddComponent<MeshRenderer>().sharedMaterial = poolMat;

            // 6. Basalt rock material for gorge cliffs and plunge pool boulders
            var basaltMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            {
                name = "Basalt rock dark",
                color = new Color(0.35f, 0.38f, 0.42f, 1f)
            };
            if (basaltMat.HasProperty("_Smoothness")) basaltMat.SetFloat("_Smoothness", 0.15f);
            if (!string.IsNullOrEmpty(folder)) AssetDatabase.CreateAsset(basaltMat, folder + "/BasaltRockDark.mat");

            // 7. Flanking Basalt Cliff Walls & Canyon Headwalls (Solid grounded 3D chasm bedrock)
            var cliffGroup = new GameObject("Basalt Gorge Cliffs");
            cliffGroup.transform.SetParent(root.transform, false);

            void CreateBasaltPillar(string name, Vector3 pos, Vector3 size, int seed)
            {
                var pObj = new GameObject(name);
                pObj.transform.SetParent(cliffGroup.transform, false);
                pObj.transform.position = pos;
                var pMesh = CoastalRocks.RockMesh(size.x, size.y, size.z, seed);
                pObj.AddComponent<MeshFilter>().sharedMesh = pMesh;
                pObj.AddComponent<MeshRenderer>().sharedMaterial = basaltMat;
                pObj.transform.localScale = Vector3.one;
                var col = pObj.AddComponent<MeshCollider>();
                col.sharedMesh = pMesh;
                pObj.layer = 8;
            }

            // West Flank Buttresses (towering left canyon gorge wall, framing the cascade on west side)
            CreateBasaltPillar("West Chasm Buttress Lower", new Vector3(8.5f, 2.5f, -236f), new Vector3(6.5f, 13f, 7.0f), 801);
            CreateBasaltPillar("West Chasm Buttress Mid", new Vector3(7.5f, 13.0f, -250f), new Vector3(7.5f, 16f, 8.0f), 802);
            CreateBasaltPillar("West Chasm Upper Rim", new Vector3(6.5f, 24.0f, -260f), new Vector3(8.5f, 18f, 9.0f), 803);
            CreateBasaltPillar("West Plunge Bed Rock", new Vector3(12.5f, -2.2f, -228f), new Vector3(4.0f, 3.5f, 4.5f), 804);

            // East Flank Buttresses (towering right canyon gorge wall, enclosing east gorge and sealing trench)
            CreateBasaltPillar("East Chasm Buttress Lower", new Vector3(41.5f, 2.5f, -236f), new Vector3(6.5f, 13f, 7.0f), 805);
            CreateBasaltPillar("East Chasm Buttress Mid", new Vector3(42.5f, 13.0f, -250f), new Vector3(7.5f, 16f, 8.0f), 806);
            CreateBasaltPillar("East Chasm Upper Rim", new Vector3(43.5f, 24.0f, -260f), new Vector3(8.5f, 18f, 9.0f), 807);
            CreateBasaltPillar("East Plunge Bed Rock", new Vector3(37.5f, -2.2f, -228f), new Vector3(4.0f, 3.5f, 4.5f), 808);
            CreateBasaltPillar("East Canyon Wall Flank North", new Vector3(48.0f, 6.0f, -232f), new Vector3(8.0f, 14f, 10.0f), 812);
            CreateBasaltPillar("East Canyon Wall Flank South", new Vector3(50.0f, 16.0f, -250f), new Vector3(9.0f, 18f, 11.0f), 813);

            // Solid Rear Basalt Cliff Face (directly behind falling water curtains, supporting the headwall)
            CreateBasaltPillar("Rear Cliff Face Upper Headwall", new Vector3(25.0f, 24.0f, -266f), new Vector3(12.0f, 16f, 5.5f), 810);
            CreateBasaltPillar("Rear Cliff Face Mid Headwall", new Vector3(25.0f, 14.0f, -255f), new Vector3(11.0f, 12f, 4.5f), 809);
            CreateBasaltPillar("Mid Shelf Basalt Support", new Vector3(25.0f, 10.5f, -250f), new Vector3(8.0f, 3.5f, 2.5f), 811);

            // 8. Plunge Pool Rim Boulders encircling the side shorelines of the churn basin
            var rimStoneGroup = new GameObject("Plunge Pool Rim Boulders");
            rimStoneGroup.transform.SetParent(root.transform, false);
            int boulderCount = 16;
            for (int b = 0; b < boulderCount; b++)
            {
                float angle = b * (Mathf.PI * 2f / boulderCount);
                // Only place rim boulders along the side shorelines (skip river mouth 55-125 deg and rear cliff 215-325 deg)
                float deg = (angle * Mathf.Rad2Deg + 360f) % 360f;
                if ((deg > 55f && deg < 125f) || (deg > 215f && deg < 325f)) continue;

                float bx = poolCenter.x + Mathf.Cos(angle) * (radiusX + 0.8f);
                float bz = poolCenter.z + Mathf.Sin(angle) * (radiusZ + 0.6f);
                float by = CoastalTerrain.Height(bx, bz);

                var bObj = new GameObject($"Rim_Boulder_{b + 1:D2}");
                bObj.transform.SetParent(rimStoneGroup.transform, false);
                bObj.transform.position = new Vector3(bx, by + 0.35f, bz);
                bObj.transform.rotation = Quaternion.Euler((b * 27) % 360, (b * 53) % 360, 0);
                float scale = 1.35f + (b % 4) * 0.35f;
                var bMesh = CityLife.Stones.StoneMeshGenerator.GenerateMesh(
                    b % 2 == 0 ? CityLife.Stones.StoneShapeKind.Fieldstone : CityLife.Stones.StoneShapeKind.RiverCobble,
                    seed: 700 + b, variantIndex: b % 3, uniformScale: scale, flatShaded: true);
                bObj.AddComponent<MeshFilter>().sharedMesh = bMesh;
                bObj.AddComponent<MeshRenderer>().sharedMaterial = basaltMat;
                var bCol = bObj.AddComponent<MeshCollider>();
                bCol.sharedMesh = bMesh;
                bObj.layer = 8;
            }

            return root;
        }

        private static GameObject CreateCanyonDryGrass(Transform parent, string folder)
        {
            var grassRoot = new GameObject("Canyon Arid Dry Grass");
            grassRoot.transform.SetParent(parent, false);

            var grassMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            {
                name = "Arid dry grass material"
            };
            if (grassMat.HasProperty("_Smoothness")) grassMat.SetFloat("_Smoothness", 0.05f);
            if (!string.IsNullOrEmpty(folder)) AssetDatabase.CreateAsset(grassMat, folder + "/AridDryGrass.mat");

            var grassMesh = IslandFloraGeometry.DryGrass();

            // Scatter dry grass tufts across canyon terraces, riverbanks, and refuge surroundings
            var grassLocations = new (float x, float z, float scale, float rot)[]
            {
                // Riverbank Shallows & Verges
                (28f, -212f, 1.2f, 35f), (21f, -205f, 1.0f, 85f), (32f, -185f, 1.4f, 142f),
                (18f, -160f, 1.1f, 210f), (35f, -140f, 1.3f, 45f), (12f, -120f, 1.0f, 310f),
                (38f, -95f, 1.35f, 115f), (15f, -70f, 1.2f, 185f), (28f, -40f, 1.1f, 260f),
                (32f, -15f, 1.4f, 15f), (12f, 15f, 1.25f, 95f), (22f, 45f, 1.1f, 175f),
                // Activity Terrace
                (16f, -32f, 1.3f, 55f), (24f, -28f, 1.1f, 120f), (12f, -18f, 1.2f, 230f),
                (18f, -10f, 1.4f, 315f), (26f, -4f, 1.15f, 40f), (8f, 5f, 1.3f, 160f),
                // Refuge Cave Terrace & Foothills
                (-160f, 112f, 1.4f, 25f), (-168f, 125f, 1.2f, 80f), (-155f, 120f, 1.35f, 190f),
                (-162f, 105f, 1.1f, 280f), (-150f, 115f, 1.25f, 340f), (-172f, 118f, 1.3f, 105f),
                // Southern Waterfall Approach
                (18f, -230f, 1.4f, 45f), (33f, -228f, 1.2f, 160f), (15f, -222f, 1.3f, 275f),
                (36f, -218f, 1.1f, 330f), (25f, -200f, 1.5f, 90f)
            };

            for (int i = 0; i < grassLocations.Length; i++)
            {
                var (gx, gz, gScale, gRot) = grassLocations[i];
                float gy = CoastalTerrain.Height(gx, gz);
                if (gy < -1.8f) continue; // Skip underwater spots

                var tObj = new GameObject($"DryGrass_Tuft_{i + 1:D2}");
                tObj.transform.SetParent(grassRoot.transform, false);
                tObj.transform.position = new Vector3(gx, gy + 0.02f, gz);
                tObj.transform.rotation = Quaternion.Euler(0, gRot, 0);
                tObj.transform.localScale = Vector3.one * gScale;

                tObj.AddComponent<MeshFilter>().sharedMesh = grassMesh;
                tObj.AddComponent<MeshRenderer>().sharedMaterial = grassMat;
            }

            return grassRoot;
        }
    }
}
