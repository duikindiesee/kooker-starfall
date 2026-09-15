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
            actor.View.Yaw = 0; actor.View.Pitch = 12; actor.View.Distance = 5.8f;
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
            camera.fieldOfView = 60; actor.View.Yaw = 0; actor.View.Pitch = 12; actor.View.Follow();
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
