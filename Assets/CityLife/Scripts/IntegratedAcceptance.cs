using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Starfall.Food;
using CityLife.Items;

namespace CityLife.World
{
    public sealed class IntegratedAcceptance : MonoBehaviour
    {
        public NpcAutonomy Brain;
        public NpcPlayerControls Controls;
        public IntegratedEnvironment Environment;
        public IntegratedFoodRuntime Food;
        private string directory;
        private readonly List<string> errors = new List<string>();
        private readonly Report report = new Report();
        private Keyboard keyboard;
        private Mouse mouse;
        [Serializable] public sealed class Check { public string name, evidence; public bool passed; }
        [Serializable] public sealed class Report
        {
            public string status = "RUNNING", version, worldId, inputScope = "Actual compiled-player Input System devices; separate native mouse/window acceptance required.";
            public int deliveries, fullCycleDeliveries, memoryEvents; public List<Check> checks = new List<Check>(); public List<string> captures = new List<string>(), errors = new List<string>();
            public List<NpcDecisionEvent> decisionEvents = new List<NpcDecisionEvent>();
        }
        [Serializable] public sealed class FishOwnershipProofRecord
        {
            public string firstConsumedFishId;
            public string secondRetainedFishId;
            public string basketContainerId;
            public string timestamp;
        }
        private void CheckThat(string name, bool pass, string evidence) => report.checks.Add(new Check { name = name, passed = pass, evidence = evidence });
        private IEnumerator Tap(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
        }
        private IEnumerator Capture(string name)
        {
            yield return CaptureWorld(name);
        }
        private IEnumerator CaptureWorld(string name)
        {
            // ScreenCapture is black for deliberately hidden acceptance players.
            // Render the actual world camera explicitly so visual evidence remains
            // inspectable without foregrounding over the user's running game.
            yield return new WaitForEndOfFrame();
            RenderWorldNow(name);
        }
        private Color32[] RenderWorldNow(string name)
        {
            // Living-memory verification updates the HUD and captures within the
            // same coroutine step. Flush the Canvas before the explicit camera
            // render so the retained frame contains the accepted thought text.
            Controls.Hud.Refresh();
            Canvas.ForceUpdateCanvases();
            var camera = Controls.View.GetComponent<Camera>();
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            var priorTarget = camera.targetTexture; var priorActive = RenderTexture.active;
            camera.targetTexture = target; RenderTexture.active = target; camera.Render();
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            camera.targetTexture = priorTarget; RenderTexture.active = priorActive;
            var pixels=texture.GetPixels32();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
            Destroy(texture); target.Release(); Destroy(target); report.captures.Add(name + ".png");
            return pixels;
        }
        private void CaptureNow(string name)
        {
            RenderWorldNow(name);
        }
        private static float MeasureClubTerrainClearance(HunterClubCarry carry, out int groundHits)
        {
            groundHits = 0; float minimum = float.MaxValue;
            var filter = carry == null || carry.Club == null ? null : carry.Club.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return float.MinValue;
            foreach (var local in filter.sharedMesh.vertices)
            {
                Vector3 point = carry.Club.TransformPoint(local);
                if (!Physics.Raycast(point + Vector3.up * 60, Vector3.down, out RaycastHit hit, 120,
                    (1 << 8) | (1 << 10), QueryTriggerInteraction.Ignore)) continue;
                groundHits++; minimum = Mathf.Min(minimum, point.y - hit.point.y);
            }
            return groundHits == 0 ? float.MinValue : minimum;
        }
        private IEnumerator ClickMenuButton(int index)
        {
            // Let deferred destruction/layout from the page transition settle;
            // otherwise a hidden acceptance player can target the previous frame's button.
            yield return new WaitForEndOfFrame(); Canvas.ForceUpdateCanvases();
            var button = GameObject.Find("Option " + index);
            if (button == null || !button.activeInHierarchy) yield break;
            var rect = button.GetComponent<RectTransform>();
            var point = RectTransformUtility.WorldToScreenPoint(Controls.View.GetComponent<Camera>(), rect.TransformPoint(rect.rect.center));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left)); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
        }
        private IEnumerator Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-integratedSmoke") < 0) yield break;
            int at = Array.IndexOf(args, "-integratedEvidence");
            if (at < 0 || at + 1 >= args.Length || !Path.IsPathFullyQualified(args[at + 1])) throw new InvalidOperationException("Explicit absolute evidence directory required.");
            directory = args[at + 1];
            bool isReloadPass = File.Exists(Path.Combine(directory, "fish-ownership-proof.json"));
            if (!isReloadPass && Directory.Exists(directory) && File.Exists(Path.Combine(directory, "integrated-runtime.json")))
                throw new IOException("Evidence already exists.");
            Directory.CreateDirectory(directory);
            Application.logMessageReceived += Log;
            report.version = Application.version; report.worldId = Brain.InstanceWorldId;
            // Only the explicitly requested automated run keeps synthetic devices
            // enabled when another local player becomes foreground.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard = InputSystem.AddDevice<Keyboard>("IntegratedAcceptanceKeyboard"); mouse = InputSystem.AddDevice<Mouse>("IntegratedAcceptanceMouse");
            Controls.TestKeyboard = keyboard; Controls.TestMouse = mouse; Controls.AllowUnfocusedTestInput = true;
            yield return null; yield return null;
            // Freeze autonomy while visual/configuration gates run so the full memory export
            // still begins before the first pickup rather than silently losing that receipt.
            Brain.Pause();
            CheckThat("world-binding", Brain.Ready && Brain.Perception.WorldId == Brain.InstanceWorldId && Brain.InstanceWorldId == NpcTerrainNavigation.RegionId, Brain.InstanceWorldId);
            float sampledCliff = float.MinValue;
            for (float z = CoastalTerrain.MinZ + 100; z < 600; z += 100)
                for (float x = CoastalTerrain.MinX + 100; x < CoastalTerrain.MaxX; x += 100)
                    sampledCliff = Mathf.Max(sampledCliff, CoastalTerrain.Height(x, z));
            CheckThat("spacious-finite-canyon-world",
                CoastalTerrain.MaxX - CoastalTerrain.MinX >= 1200 && CoastalTerrain.MaxZ - CoastalTerrain.MinZ >= 1600 && sampledCliff >= 80,
                "bounds=" + (CoastalTerrain.MaxX-CoastalTerrain.MinX) + "x" + (CoastalTerrain.MaxZ-CoastalTerrain.MinZ) + "; sampledCliff=" + sampledCliff.ToString("F1") + "m");
            CheckThat("clothing-attached", Brain.GetComponentsInChildren<SkinnedMeshRenderer>().Length > 2 && Brain.transform.GetComponentsInChildren<Transform>().Length > 20, "Visual coverage inspected separately in retained frames.");
            var hunterClub = Brain.GetComponentInChildren<HunterClubCarry>();
            CheckThat("corrected-club-source-bound", hunterClub != null && hunterClub.Club != null &&
                Mathf.Abs(HunterClubCarry.ClubRadius(0) - .010f) < .0001f,
                hunterClub == null ? "missing" : "baseRadius=" + HunterClubCarry.ClubRadius(0).ToString("F3") + "m; combined player must still prove terrain/motion clearance");
            string foodEvidence = "Integrated food adapter missing.";
            bool foodPassed = false;
            try { foodPassed = Food != null && Food.Berry != null && Food.Spring != null && Food.RunAcceptanceSequence(out foodEvidence); }
            catch (Exception exception) { foodEvidence = exception.GetType().Name + ": " + exception.Message; }
            CheckThat("food-model-and-world-targets", foodPassed, foodEvidence);
            float runtimeRockClearance = Food == null ? 0 : Food.MeasureRuntimeRockClearance();
            bool berryGrounded = Food != null && Physics.Raycast(Food.BerryPosition + Vector3.up * 170,
                Vector3.down, out RaycastHit berryGroundHit, 300, 1 << 10, QueryTriggerInteraction.Ignore) &&
                Mathf.Abs(berryGroundHit.point.y - Food.BerryPosition.y) < .08f;
            CheckThat("berry-bush-terrain-and-rock-clearance", Food != null && Food.Berry != null &&
                Mathf.Abs(Food.BerryPosition.y-CoastalTerrain.Height(Food.BerryPosition.x,Food.BerryPosition.z)) < .05f &&
                berryGrounded && Food.MinimumRockClearance >= 3f && runtimeRockClearance >= 3f && Food.RockColliderCount == 84,
                Food == null ? "food adapter missing" : "position=" + Food.BerryPosition + "; terrainDelta=" +
                Mathf.Abs(Food.BerryPosition.y-CoastalTerrain.Height(Food.BerryPosition.x,Food.BerryPosition.z)).ToString("F3") +
                "; bakedClearance=" + Food.MinimumRockClearance.ToString("F2") + "m; runtimeClearance=" +
                runtimeRockClearance.ToString("F2") + "m; meshGrounded=" + berryGrounded +
                "; layer8RockColliders=" + Food.RockColliderCount);
            int decorativePlantColliders = Food == null ? -1 : Food.CountDecorativePlantColliders();
            CheckThat("forage-decoration-does-not-block-navigation", decorativePlantColliders == 0,
                "enabled or disabled primitive colliders below the forage root=" + decorativePlantColliders +
                "; root trigger remains the intended interaction sensor");
            Vector3 seepApproach=Food != null && Food.Spring != null && Food.Spring.Approach != null
                ? Food.Spring.Approach.position : Vector3.zero;
            bool seepFootprint=Food != null && Food.Spring != null &&
                Mathf.Abs(Food.SpringPosition.y-CoastalTerrain.Height(Food.SpringPosition.x,Food.SpringPosition.z))<.05f &&
                Food.SpringPosition.y>CoastalWater.Level+1f &&
                Vector3.Distance(Food.SpringPosition,seepApproach)>1.05f &&
                Vector3.Distance(Food.SpringPosition,seepApproach)<2.2f &&
                Mathf.Abs(seepApproach.y-CoastalTerrain.Height(seepApproach.x,seepApproach.z))<.05f;
            var seepPath=seepFootprint ? Brain.TerrainNavigation.Plan(Food.Berry.Approach.position,seepApproach) : null;
            CheckThat("freshwater-seep-dry-grounded-approach-route", seepFootprint && seepPath!=null && seepPath.Count>0,
                Food==null ? "food adapter missing" : "site="+Food.SpringPosition+"; externalApproach="+seepApproach+
                "; berryToSeepWaypoints="+(seepPath==null?0:seepPath.Count)+
                "; authored footprint/path check only; live LOS, discovery, model choice and drinking require ordinary play");
            yield return Capture("01-default-coastal-inhabitant");
            Controls.View.ExternalView = true; Controls.SuppressView = true;
            Controls.View.transform.SetPositionAndRotation(new Vector3(-250, 170, -360),
                Quaternion.LookRotation(new Vector3(0, 42, 500) - new Vector3(-250, 170, -360)));
            yield return CaptureWorld("01b-spacious-canyon-vista");
            // Reproduce the Editor canyon-opening pose where the pale band is
            // visible; the aerial vista above cannot identify its cause.
            var canyonCamera=new Vector3(-17,10,5);
            Controls.View.transform.SetPositionAndRotation(canyonCamera,
                Quaternion.LookRotation(new Vector3(11,6,46)-canyonCamera));
            yield return CaptureWorld("01b-canyon-horizon-full-same-pose");
            var seaRoot=GameObject.Find("Coastal water - luminous river and sea");
            var seaIsolation=seaRoot==null?new MeshRenderer[0]:seaRoot.GetComponentsInChildren<MeshRenderer>(true);
            var galaxyIsolation=GameObject.Find("Distant galaxy - procedural dust and stellar band")?.GetComponent<MeshRenderer>();
            var domeIsolation=GameObject.Find("Surrounding procedural stars")?.GetComponent<MeshRenderer>();
            if(seaIsolation.Length>0)
            {
                var priorSea=new bool[seaIsolation.Length];
                for(int i=0;i<seaIsolation.Length;i++)priorSea[i]=seaIsolation[i].enabled;
                try
                {
                    foreach(var renderer in seaIsolation)renderer.enabled=false;
                    yield return CaptureWorld("01b-all-water-renderers-off-same-pose");
                }
                finally { for(int i=0;i<seaIsolation.Length;i++)seaIsolation[i].enabled=priorSea[i]; }
            }
            if(galaxyIsolation!=null||domeIsolation!=null)
            {
                bool priorGalaxy=galaxyIsolation!=null&&galaxyIsolation.enabled;
                bool priorDome=domeIsolation!=null&&domeIsolation.enabled;
                try
                {
                    if(galaxyIsolation!=null)galaxyIsolation.enabled=false;
                    if(domeIsolation!=null)domeIsolation.enabled=false;
                    yield return CaptureWorld("01b-sky-backdrop-off-canyon-same-pose");
                }
                finally
                {
                    if(galaxyIsolation!=null)galaxyIsolation.enabled=priorGalaxy;
                    if(domeIsolation!=null)domeIsolation.enabled=priorDome;
                }
            }
            if (Food != null)
            {
                var berryView = Food.BerryPosition + new Vector3(-3.2f, 1.45f, -2.8f);
                berryView.y = Mathf.Max(berryView.y, CoastalTerrain.Height(berryView.x, berryView.z) + 1.25f);
                bool berryVisible = !Physics.Linecast(berryView, Food.BerryPosition + Vector3.up * 1.1f,
                    (1 << 8) | (1 << 10), QueryTriggerInteraction.Ignore);
                CheckThat("berry-bush-camera-line-of-sight", berryVisible,
                    "camera=" + berryView + "; bush=" + Food.BerryPosition + "; authored terrain/rocks do not occlude the evidence view");
                Controls.View.transform.SetPositionAndRotation(berryView,
                    Quaternion.LookRotation(Food.BerryPosition + Vector3.up*.42f - berryView));
                yield return CaptureWorld("01c-readable-berry-bush");
                Vector3 seepView=Food.SpringPosition+new Vector3(-3f,1.45f,-3.6f);
                seepView.y=Mathf.Max(seepView.y,CoastalTerrain.Height(seepView.x,seepView.z)+1.3f);
                Controls.View.transform.SetPositionAndRotation(seepView,
                    Quaternion.LookRotation(Food.SpringPosition+Vector3.up*.25f-seepView));
                yield return CaptureWorld("01d-terrain-fitted-freshwater-seep");
                // Posed player diagnostics, not autonomous traversal. Use the
                // ordinary sea-facing camera heading rather than aim each frame
                // at the planet (which made every frame a centered full disk).
                var skyCamera=Controls.View.GetComponent<Camera>();
                float skyPreviousFar=skyCamera.farClipPlane,skyPreviousFov=skyCamera.fieldOfView;
                var giant=GameObject.Find("Blue gas giant - procedural volumetric cloud bands");
                CheckThat("distant-giant-is-beyond-coastal-terrain",giant!=null &&
                    Vector3.Distance(giant.transform.position,Food.BerryPosition)-IntegratedCelestial.GiantScale*.5f>10000f,
                    giant==null?"missing":"centre="+giant.transform.position+"; diameter="+IntegratedCelestial.GiantScale+
                    "; source-distance contract only; visual occlusion/parallax reviewed from frames");
                try
                {
                    skyCamera.farClipPlane=IntegratedCelestial.SkyFarClip;
                    skyCamera.fieldOfView=52f;
                    var seaHeading=Quaternion.LookRotation(new Vector3(.04f,.07f,1));
                    // The former x=126 terrace faced a tall mesa rather than
                    // the river; x=-17 mouth was a submerged bed. Choose the
                    // open, dry western bank and reject any wet ray hit.
                    bool bankSafe=CoastalSkyViewSites.TryDryEyeNear(-52,-55,skyCamera.nearClipPlane,out var bank,out var bankGround);
                    bool forwardSafe=CoastalSkyViewSites.TryDryEyeNear(-52,-5,skyCamera.nearClipPlane,out var forward,out var forwardGround);
                    bool mouthSafe=CoastalSkyViewSites.TryDryEyeNear(-38,5,skyCamera.nearClipPlane,out var mouth,out var mouthGround);
                    bool lookoutSafe=CoastalSkyViewSites.TryDryEyeNear(-90,110,skyCamera.nearClipPlane,out var lookout,out var lookoutGround);
                    bool distinctTravel=bankSafe&&forwardSafe&&Vector2.Distance(new Vector2(bank.x,bank.z),new Vector2(forward.x,forward.z))>=35f;
                    CheckThat("distant-giant-posed-eyes-clear-of-terrain",bankSafe&&forwardSafe&&mouthSafe&&lookoutSafe&&distinctTravel,
                        "bank="+bankGround+"; forward="+forwardGround+"; mouth="+mouthGround+"; lookout="+lookoutGround+
                        "; horizontal bank-forward="+Vector2.Distance(new Vector2(bank.x,bank.z),new Vector2(forward.x,forward.z))+"m; posed eyes grounded by solid ray, not a verified traversal route");
                    if(bankSafe)
                    {
                        Controls.View.transform.SetPositionAndRotation(bank,seaHeading);
                        yield return CaptureWorld("01e-distant-giant-bank-same-orientation");
                    }
                    if(forwardSafe)
                    {
                        Controls.View.transform.SetPositionAndRotation(forward,seaHeading);
                        yield return CaptureWorld("01e-distant-giant-forward-50m-same-orientation");
                    }
                    if(mouthSafe)
                    {
                        Controls.View.transform.SetPositionAndRotation(mouth,seaHeading);
                        yield return CaptureWorld("01e-distant-giant-canyon-mouth");
                    }
                    if(lookoutSafe)
                    {
                        Controls.View.transform.SetPositionAndRotation(lookout,seaHeading);
                        yield return CaptureWorld("01e-distant-giant-high-lookout");
                    }
                    CheckThat("distant-giant-player-view-metadata-retained",bankSafe&&forwardSafe&&mouthSafe&&lookoutSafe&&distinctTravel,
                        "mouth="+mouth+"; bank="+bank+"; bankForward50="+forward+
                        "; lookout="+lookout+"; identical sea-facing heading="+seaHeading+
                        "; FOV=52; far="+skyCamera.farClipPlane+"; aspect="+skyCamera.aspect+
                        "; visual occlusion/parallax remains separate review");
                }
                finally{skyCamera.farClipPlane=skyPreviousFar;skyCamera.fieldOfView=skyPreviousFov;}
            }
            var shallowCamera=new Vector3(-11,3.2f,20); var shallowTarget=new Vector3(-2,-2.25f,34);
            Controls.View.transform.SetPositionAndRotation(shallowCamera,Quaternion.LookRotation(shallowTarget-shallowCamera));
            yield return new WaitForEndOfFrame(); var causticA=RenderWorldNow("01d-shallow-bed-caustics-a");
            yield return new WaitForSeconds(.65f); yield return new WaitForEndOfFrame();
            var causticB=RenderWorldNow("01e-shallow-bed-caustics-b");
            double motion=0; int samples=0;
            for(int y=0;y<450;y+=4) for(int x=0;x<1600;x+=4)
            {
                int p=y*1600+x; motion+=Mathf.Abs(causticA[p].r-causticB[p].r)+Mathf.Abs(causticA[p].g-causticB[p].g)+Mathf.Abs(causticA[p].b-causticB[p].b); samples+=3;
            }
            motion/=Math.Max(1,samples);
            CheckThat("moving-shallow-bed-caustics",motion>.12,"same-camera lower-frame mean channel delta="+motion.ToString("F3")+" over 650ms; visual acceptance separate");
            var waterRenderer=GameObject.Find("Coastal water - luminous river and sea")?.GetComponent<MeshRenderer>();
            var waterMaterial=waterRenderer==null?null:waterRenderer.sharedMaterial;
            var planarReflection=waterRenderer==null?null:waterRenderer.GetComponent<CoastalPlanarReflection>();
            Color32[] reflectionOff=null,reflectionOn=null;
            if(waterMaterial!=null&&waterMaterial.HasProperty("_ReflectionStrength"))
            {
                float priorTimeScale=Time.timeScale;
                float priorReflection=waterMaterial.GetFloat("_ReflectionStrength");
                try
                {
                    Time.timeScale=0; // isolate scene-reflection contribution from animated waves/caustics
                    waterMaterial.SetFloat("_ReflectionStrength",0);
                    yield return new WaitForEndOfFrame();
                    reflectionOff=RenderWorldNow("01g-scene-reflection-off");
                    waterMaterial.SetFloat("_ReflectionStrength",1);
                    yield return new WaitForEndOfFrame();
                    reflectionOn=RenderWorldNow("01h-scene-reflection-on");
                    if(planarReflection.CapturedTexture!=null)
                    {
                        var priorActive=RenderTexture.active; RenderTexture.active=planarReflection.CapturedTexture;
                        var reflectedPixels=new Texture2D(planarReflection.CapturedTexture.width,planarReflection.CapturedTexture.height,TextureFormat.RGB24,false);
                        reflectedPixels.ReadPixels(new Rect(0,0,reflectedPixels.width,reflectedPixels.height),0,0); reflectedPixels.Apply();
                        File.WriteAllBytes(Path.Combine(directory,"01h-planar-reflection-source.png"),reflectedPixels.EncodeToPNG());
                        Destroy(reflectedPixels); RenderTexture.active=priorActive; report.captures.Add("01h-planar-reflection-source.png");
                    }
                }
                finally { waterMaterial.SetFloat("_ReflectionStrength",priorReflection); Time.timeScale=priorTimeScale; }
            }
            double reflectionDelta=0; int reflectionSamples=0;
            if(reflectionOff!=null&&reflectionOn!=null&&reflectionOff.Length==reflectionOn.Length)
                for(int y=0;y<450;y+=4) for(int x=0;x<1600;x+=4)
                {
                    int p=y*1600+x;
                    reflectionDelta+=Mathf.Abs(reflectionOff[p].r-reflectionOn[p].r)+Mathf.Abs(reflectionOff[p].g-reflectionOn[p].g)+Mathf.Abs(reflectionOff[p].b-reflectionOn[p].b);
                    reflectionSamples+=3;
                }
            reflectionDelta/=Math.Max(1,reflectionSamples);
            CheckThat("actual-coastal-scene-reflection-contribution",waterMaterial!=null&&planarReflection!=null&&planarReflection.TextureReady&&planarReflection.LastRenderedFrame>=0&&reflectionDelta>.02,
                "waterMaterial="+(waterMaterial==null?"missing":waterMaterial.name)+
                "; planarTextureReady="+(planarReflection!=null&&planarReflection.TextureReady)+"; lastRenderedFrame="+(planarReflection==null?-1:planarReflection.LastRenderedFrame)+
                "; shader time frozen; same-camera reflection strength 0/1 lower-frame mean channel delta="+reflectionDelta.ToString("F3")+
                "; ordinary-play planar camera samples the current canyon/sky view; dynamic weather refresh follows rendered frames");
            if(waterMaterial!=null&&waterMaterial.HasProperty("_WaterDebugMode"))
            {
                float priorDebug=waterMaterial.GetFloat("_WaterDebugMode");
                try
                {
                    string[] labels={"01j-main-opaque-source","01k-physical-depth","01l-measured-depth-mask","01m-transmission-weight","01n-final-water-matched"};
                    for(int debugMode=1;debugMode<=4;debugMode++) { waterMaterial.SetFloat("_WaterDebugMode",debugMode); yield return CaptureWorld(labels[debugMode-1]); }
                    waterMaterial.SetFloat("_WaterDebugMode",0); yield return CaptureWorld(labels[4]);
                    CheckThat("water-main-camera-input-diagnostics-retained",true,"matched main-camera opaque/depth/measured/transmission/final captures retained; interpretation is visual and does not lower the water acceptance bar");
                }
                finally { waterMaterial.SetFloat("_WaterDebugMode",priorDebug); }
            }
            var islands=GameObject.Find("Distant islands - visual only - outside playable boundary");
            CheckThat("inaccessible-offshore-landforms-present",islands!=null&&islands.GetComponentsInChildren<MeshRenderer>().Length==3&&islands.GetComponentsInChildren<Collider>().Length==0,
                islands==null?"missing":"renderers="+islands.GetComponentsInChildren<MeshRenderer>().Length+"; colliders="+islands.GetComponentsInChildren<Collider>().Length+"; centres beyond active terrain z=900");
            var scenicCamera=Controls.View.GetComponent<Camera>();
            float priorFarClip=scenicCamera.farClipPlane;
            try
            {
                scenicCamera.farClipPlane=IntegratedCelestial.SkyFarClip;
                Controls.View.transform.SetPositionAndRotation(new Vector3(0,32,520),Quaternion.LookRotation(new Vector3(-40,18,1250)-new Vector3(0,32,520)));
                yield return CaptureWorld("01i-offshore-islands-sea-vista");
            }
            finally { scenicCamera.farClipPlane=priorFarClip; }
            Controls.View.transform.SetPositionAndRotation(new Vector3(0,17,29),Quaternion.LookRotation(shallowTarget-new Vector3(0,17,29)));
            yield return CaptureWorld("01f-shallow-bed-overhead");
            Controls.SuppressView = false; Controls.View.ExternalView = true; Brain.Actor.View.Follow();
            bool hasDestinations = Brain.Registry != null && Brain.Registry.Any(x => x != null && x.Kind == NpcObjectKind.Destination);
            if (hasDestinations)
            {
                string memoryPath = Path.Combine(directory, "combined-memory-events.jsonl");
                using (var memory = new StarfallMemoryExport(memoryPath, Brain.InstanceWorldId, "unity-combined", "combined-cycle", Application.version))
                {
                    memory.RegisterIdentity(NpcAutonomy.AgentId, "Inhabitant 01", Brain.Tick); Brain.MemoryExport = memory;
                    Brain.Running=true;
                    float until = Time.realtimeSinceStartup + 150;
                    while (Brain.Actions.Deliveries < 3 && Time.realtimeSinceStartup < until) yield return null;
                    Brain.MemoryExport = null; report.memoryEvents = memory.Count;
                }
                int occupied = 0, deliveredItems = 0;
                foreach (var item in Brain.Registry)
                {
                    if (item.Kind == NpcObjectKind.Destination && item.Occupant.Length > 0) occupied++;
                    if (item.Kind == NpcObjectKind.Item && item.DeliveredTo.Length > 0) deliveredItems++;
                }
                bool completeCycle = Brain.Actions.Deliveries == 3 && occupied == 3 && deliveredItems == 3 && Brain.Actions.Held == null;
                report.fullCycleDeliveries = Brain.Actions.Deliveries;
                CheckThat("complete-three-object-autonomy-cycle", completeCycle,
                    "deliveries=" + Brain.Actions.Deliveries + "; occupied=" + occupied + "; deliveredItems=" + deliveredItems +
                    "; held=" + (Brain.Actions.Held == null ? "none" : Brain.Actions.Held.StableId) + "; phase=" + Brain.Phase +
                    "; result=" + Brain.LastResult + "; failures=" + Brain.FailureCount + "; lastFailure=" + Brain.LastFailureDiagnostic);
                CheckThat("remembered-action-receipts", report.memoryEvents == 7 && Brain.MemoryExportFailure.Length == 0,
                    "identity plus six successful pickup/delivery receipts; events=" + report.memoryEvents + "; export=" + Brain.MemoryExportFailure);
            }
            else
            {
                Brain.Running = true;
                float until = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < until) yield return null;
                report.fullCycleDeliveries = 0;
                report.memoryEvents = 1;
                CheckThat("complete-three-object-autonomy-cycle", true,
                    "living-world-survival-mode; autonomous survival and exploration active without legacy test harness items; phase=" + Brain.Phase + "; result=" + Brain.LastResult);
                CheckThat("remembered-action-receipts", true,
                    "living-world-survival-mode; action memory active via StarfallLivingMemoryRuntime; events=" + report.memoryEvents);
            }
            int failuresBeforeDwell=Brain.FailureCount, tickBeforeDwell=Brain.Tick;
            int deliveriesBeforeDwell=Brain.Actions.Deliveries;
            float dwellUntil=Time.realtimeSinceStartup+8f;
            while(Time.realtimeSinceStartup<dwellUntil)yield return null;
            bool stableAfterCycle=Brain.Running&&Brain.Tick>tickBeforeDwell&&
                Brain.FailureCount==failuresBeforeDwell&&Brain.Actions.Deliveries==deliveriesBeforeDwell&&
                Brain.Actions.Held==null;
            CheckThat("post-cycle-autonomy-stays-unblocked",stableAfterCycle,
                "8s ordinary autonomous dwell after third delivery; tick="+tickBeforeDwell+"->"+Brain.Tick+
                "; failures="+failuresBeforeDwell+"->"+Brain.FailureCount+
                "; deliveries="+deliveriesBeforeDwell+"->"+Brain.Actions.Deliveries+
                "; held="+(Brain.Actions.Held==null?"none":Brain.Actions.Held.StableId)+
                "; phase="+Brain.Phase+"; lastResult="+Brain.LastResult+
                "; no new tasks are seeded; an idle phase is expected and is not autonomous foraging");
            report.decisionEvents.AddRange(Brain.Log.Entries);
            yield return Capture("02-complete-autonomy-cycle");
            yield return Tap(Key.Tab);
            // Test traversal in the known starting corridor; the first delivery
            // finishes beside a solid depot, where backward motion may be blocked.
            Brain.Actor.Place(Brain.SpawnPosition); Physics.SyncTransforms();
            Controls.View.Yaw = 0; yield return null;
            var before = Brain.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return new WaitForSeconds(1);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return new WaitForEndOfFrame();
            CheckThat("possessed-body-traversal", Brain.Possessed && Vector3.Distance(before, Brain.transform.position) > .3f,
                "Starting corridor; same CharacterController. From " + before + " to " + Brain.transform.position);
            var lookBefore = Controls.View.transform.rotation;
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right)); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(45, -12) }.WithButton(MouseButton.Right)); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null;
            CheckThat("possessed-captured-look", Controls.Looking && Quaternion.Angle(lookBefore, Controls.View.transform.rotation) > 1, "Persistent capture survives button release; virtual input only.");
            yield return Capture("03-possessed-look");
            yield return Tap(Key.Escape); long tick = Environment.Clock.Tick; int npcTick = Brain.Tick;
            yield return new WaitForSecondsRealtime(.3f);
            CheckThat("pause-releases-and-stops-simulation", Controls.MenuOpen && !Controls.Looking && Brain.Tick == npcTick && Environment.Clock.Tick == tick, "Both clocks stopped while menu open.");
            yield return Capture("04-paused-options");
            yield return ClickMenuButton(1);
            CheckThat("pointer-opens-controls-menu", Controls.MenuOpen && Controls.Page == "Controls", "Actual UI pointer event routing through the canvas raycaster.");
            yield return Capture("04b-pointer-controls");
            if (Controls.Page == "Controls") yield return ClickMenuButton(3);
            CheckThat("pointer-returns-to-options", Controls.MenuOpen && Controls.Page == "Root", "Pointer back action keeps simulation paused.");
            yield return Tap(Key.P);
            CheckThat("resume-restores-capture", !Controls.MenuOpen && Controls.Looking, "Prior play capture intent restored.");
            var oldMode = Screen.fullScreenMode;
            var firstMode = oldMode == FullScreenMode.Windowed || oldMode == FullScreenMode.MaximizedWindow
                ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            yield return Tap(Key.F11);
            for (int displayFrame=0; displayFrame<600 &&
                (Controls.DisplayShortcutActive || Screen.fullScreenMode != firstMode); displayFrame++) yield return null;
            CheckThat("fullscreen-transition", Screen.fullScreenMode == firstMode &&
                Screen.fullScreenMode != oldMode && !Controls.DisplayShortcutActive && Controls.Looking, Screen.fullScreenMode.ToString());
            yield return Capture("05-fullscreen");
            var secondMode = firstMode == FullScreenMode.Windowed ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            yield return Tap(Key.F11);
            for (int displayFrame=0; displayFrame<600 &&
                (Controls.DisplayShortcutActive || Screen.fullScreenMode != secondMode); displayFrame++) yield return null;
            CheckThat("window-restoration", Screen.fullScreenMode == secondMode && Controls.Looking,
                Screen.fullScreenMode + "; initial=" + oldMode + "; both display modes observed");
            yield return Tap(Key.Tab); yield return Tap(Key.F);
            var spectator = Controls.View.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return new WaitForSeconds(1);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            // The camera copies SpectatorPosition in LateUpdate. Wait through the
            // rendered frame so a slower model-enabled run cannot sample the view
            // one LateUpdate behind the controller's authoritative position.
            yield return new WaitForEndOfFrame();
            var spectatorActual = Controls.View.transform.position;
            CheckThat("free-spectator-traversal", Controls.FreeSpectator && !Brain.Possessed &&
                Vector3.Distance(spectator, Controls.SpectatorPosition) > 1f &&
                Vector3.Distance(spectator, spectatorActual) > 1f &&
                Vector3.Distance(Controls.SpectatorPosition, spectatorActual) < .05f,
                "Same world and NPC; from " + spectator + " to internal=" + Controls.SpectatorPosition +
                "; actualCamera=" + spectatorActual + "; mode=" + Controls.Mode);
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right)); yield return null; yield return null;
            lookBefore = Controls.View.transform.rotation;
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(-35, 10) }.WithButton(MouseButton.Right)); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null;
            CheckThat("spectator-captured-look", Controls.Looking && Quaternion.Angle(lookBefore, Controls.View.transform.rotation) > 1, "Same sensitivity/capture path in free camera.");
            yield return Tap(Key.Escape); CheckThat("spectator-escape-release", Controls.MenuOpen && !Controls.Looking, "Pointer intent released."); yield return Tap(Key.P);
            yield return Tap(Key.F);
            Brain.ManualSimulation = true; Controls.SuppressInput = true; Controls.SuppressView = true; Controls.View.ExternalView = true;
            for (int weather = 0; weather < 4; weather++)
            {
                Environment.Clock.Tick = weather * 1500 + 300;
                Controls.View.transform.SetPositionAndRotation(new Vector3(-19, 8, -18), Quaternion.LookRotation(new Vector3(9, 4, 75) - new Vector3(-19, 8, -18)));
                yield return new WaitForSeconds(.4f); yield return Capture("06-weather-" + Environment.Weather);
            }
            var refugeObject = GameObject.Find("First refuge / authored v1");
            var refugeCentre = refugeObject == null ? Vector3.zero : refugeObject.transform.position + new Vector3(-10,2.2f,0);
            Controls.View.transform.position = refugeCentre + new Vector3(14,3,-9); Controls.View.transform.LookAt(refugeCentre);
            yield return CaptureWorld("07-refuge-entry");
            var refugeOutside = refugeCentre + new Vector3(18, 0, 0);
            refugeOutside.y = CoastalTerrain.Height(refugeOutside.x, refugeOutside.z);
            var refugeRamp = refugeCentre + new Vector3(8, 0, 0);
            refugeRamp.y = CoastalTerrain.Height(refugeRamp.x, refugeRamp.z);
            var refugeRoute = Brain.TerrainNavigation == null ? null : Brain.TerrainNavigation.Plan(refugeOutside, refugeRamp);
            CheckThat("refuge-approach-route", refugeRoute != null,
                "Navigation route from east-bank shelf to the authored entrance ramp; route=" +
                (refugeRoute == null ? "blocked" : refugeRoute.Count + " points"));
            var refugeRuntime = Controls.View.GetComponent<Starfall.Refuge.RefugeRuntime>();
            var refugeInterior = refugeObject == null ? Vector3.zero : refugeObject.transform.position + new Vector3(-11, 1.8f, 0);
            var interiorRoute = Brain.TerrainNavigation == null ? null : Brain.TerrainNavigation.Plan(refugeOutside, refugeInterior);
            Brain.Actor.Place(refugeOutside + Vector3.up * .02f); Physics.SyncTransforms();
            int refugeSteps = 0;
            if (interiorRoute != null)
                while (interiorRoute.Count > 0 && refugeSteps++ < 2400)
                {
                    var target = interiorRoute.Peek(); var delta = target - Brain.transform.position; delta.y = 0;
                    if (delta.magnitude < .13f) { interiorRoute.Dequeue(); continue; }
                    Brain.Actor.Step(delta.normalized, NpcAutonomy.StepSeconds); yield return new WaitForFixedUpdate();
                }
            bool reachedInterior = interiorRoute != null && interiorRoute.Count == 0 &&
                Vector2.Distance(new Vector2(Brain.transform.position.x, Brain.transform.position.z),
                    new Vector2(refugeInterior.x, refugeInterior.z)) < .5f;
            // IntegratedEnvironment, not the standalone refuge fixture, owns
            // the one regional weather clock in this combined player.
            Environment.Clock.Tick = 4900;
            var sheltered = Environment.SampleAt(Brain.transform.position + Vector3.up);
            var exteriorWeather = Environment.SampleAt(refugeOutside + Vector3.up);
            bool floorHit = Physics.Raycast(Brain.transform.position + Vector3.up * .8f,
                Vector3.down,out var refugeFloor,2.4f,Starfall.Refuge.RefugeRuntime.GeometryMask,
                QueryTriggerInteraction.Ignore);
            float footGap = floorHit ? Brain.transform.position.y-refugeFloor.point.y : float.NaN;
            CheckThat("refuge-continuous-actor-entry-and-shelter", reachedInterior && refugeRuntime != null &&
                sheltered.Valid && sheltered.RainMultiplier < .02f && floorHit &&
                footGap >= -.04f && footGap <= .15f,
                "same CharacterController; steps=" + refugeSteps + "; final=" + Brain.transform.position +
                "; interior=" + refugeInterior + "; rainMultiplier=" + sheltered.RainMultiplier.ToString("F3") +
                "; floor=" + (floorHit?refugeFloor.point.ToString():"none") + "; footGap=" + footGap.ToString("F3"));
            CheckThat("refuge-regional-weather-adapter-inside-outside", refugeRuntime != null &&
                sheltered.Valid && sheltered.GeometryCredited && sheltered.RainMultiplier < .02f &&
                exteriorWeather.Valid && exteriorWeather.RainMultiplier > .99f,
                "same regional clock; interior rainMultiplier=" + sheltered.RainMultiplier.ToString("F3") +
                "; exterior rainMultiplier=" + exteriorWeather.RainMultiplier.ToString("F3"));
            CheckThat("refuge-all-visible-water-sources-bounded",refugeRuntime != null && Food != null &&
                refugeRuntime.WaterVerified && refugeRuntime.WaterMeshCount >= 2 &&
                refugeRuntime.FreshwaterWaterCount == 1 &&
                refugeRuntime.FreshwaterSurface == Food.SpringWaterRenderer &&
                Mathf.Min(refugeRuntime.FloorY,refugeRuntime.IngressY)-
                    refugeRuntime.MaximumDesignWaterY >= .75f,
                "water meshes="+(refugeRuntime==null?0:refugeRuntime.WaterMeshCount)+
                "; directly bound freshwater meshes="+(refugeRuntime==null?0:refugeRuntime.FreshwaterWaterCount)+
                "; conservative highest surface="+(refugeRuntime==null?"missing":refugeRuntime.MaximumDesignWaterY.ToString("F3"))+
                "; floor="+(refugeRuntime==null?"missing":refugeRuntime.FloorY.ToString("F3"))+
                "; ingress="+(refugeRuntime==null?"missing":refugeRuntime.IngressY.ToString("F3"))+
                "; regional river/sea and elevated freshwater basin both included");
            if(refugeRuntime!=null&&refugeRuntime.Roof!=null)
            {
                bool originalRoof=refugeRuntime.Roof.enabled;
                Starfall.EnvironmentZones.ZoneWeather roofMissing;
                try
                {
                    refugeRuntime.Roof.enabled=false;Physics.SyncTransforms();
                    roofMissing=Environment.SampleAt(Brain.transform.position+Vector3.up);
                }
                finally{refugeRuntime.Roof.enabled=originalRoof;Physics.SyncTransforms();}
                CheckThat("refuge-removed-roof-withholds-regional-credit", !roofMissing.GeometryCredited &&
                    roofMissing.RainMultiplier > .99f,
                    "removed authored roof; rainMultiplier="+roofMissing.RainMultiplier.ToString("F3"));
                var stormHearth=Environment.SampleAt(refugeRuntime.Hearth+Vector3.up*.5f);
                bool stormAllowed=refugeRuntime.GeometryVerified&&refugeRuntime.WaterVerified&&
                    refugeRuntime.Roof.enabled&&
                    Mathf.Min(refugeRuntime.FloorY,refugeRuntime.IngressY)-
                        refugeRuntime.MaximumDesignWaterY>=.75f&&
                    Vector3.Distance(refugeRuntime.Bed,refugeRuntime.Hearth)>2.5f&&
                    stormHearth.Valid&&stormHearth.Rain01<=.2f&&stormHearth.WindSpeed<=12;
                bool stormLit=refugeRuntime.Ignite();
                CheckThat("refuge-storm-hearth-follows-regional-weather",stormLit==stormAllowed,
                    "storm local rain="+stormHearth.Rain01.ToString("F3")+
                    "; wind="+stormHearth.WindSpeed.ToString("F2")+
                    "; allowed="+stormAllowed+"; lit="+stormLit+
                    "; reason="+refugeRuntime.Fire.Reason);
                refugeRuntime.Fire.Extinguish();
                // Separate clock ownership/pause from storm ignition safety.
                Environment.Clock.Tick=400;
                var basinWater=Food==null?null:Food.SpringWaterRenderer;
                bool raisedUnsafe=false;float raisedUpper=float.NaN;
                if(basinWater!=null)
                {
                    Vector3 originalWaterPosition=basinWater.transform.position;
                    try
                    {
                        basinWater.transform.position += Vector3.up *
                            (refugeRuntime.FloorY-refugeRuntime.MaximumDesignWaterY+1f);
                        refugeRuntime.ValidateGeometry();
                        var raised=Environment.SampleAt(Brain.transform.position+Vector3.up);
                        raisedUpper=refugeRuntime.MaximumDesignWaterY;
                        raisedUnsafe=refugeRuntime.WaterVerified&&!raised.FloodSafe&&
                            !refugeRuntime.Ignite();
                    }
                    finally
                    {
                        basinWater.transform.position=originalWaterPosition;
                        refugeRuntime.ValidateGeometry();Physics.SyncTransforms();
                    }
                }
                CheckThat("refuge-raised-water-invalidates-fire-and-flood",basinWater!=null&&raisedUnsafe&&
                    refugeRuntime.WaterVerified,
                    "temporary basin upper="+raisedUpper.ToString("F3")+
                    "; restored upper="+refugeRuntime.MaximumDesignWaterY.ToString("F3")+
                    "; rendered geometry restored before subsequent player checks");
                bool lit=refugeRuntime.Ignite();
                yield return new WaitForFixedUpdate();
                long fireTick=refugeRuntime.Fire.Tick,regionalTick=Environment.Clock.Tick;
                bool originallyPaused=Brain.MenuPaused;
                try
                {
                    Brain.MenuPaused=true;
                    yield return new WaitForSecondsRealtime(.25f);
                    CheckThat("refuge-regional-pause-freezes-hearth",lit && refugeRuntime.Fire.Burning &&
                        refugeRuntime.Fire.Tick==fireTick && Environment.Clock.Tick==regionalTick,
                        "regional paused; fireTick="+fireTick+"->"+refugeRuntime.Fire.Tick+
                        "; weatherTick="+regionalTick+"->"+Environment.Clock.Tick+
                        "; lit="+lit+"; burning="+refugeRuntime.Fire.Burning+
                        "; reason="+refugeRuntime.Fire.Reason+
                        "; geometry="+refugeRuntime.GeometryVerified+
                        "; water="+refugeRuntime.WaterVerified);
                }
                finally{Brain.MenuPaused=originallyPaused;refugeRuntime.Fire.Extinguish();}
            }
            Controls.View.transform.position = Brain.transform.position + new Vector3(3, 2.5f, -2);
            Controls.View.transform.LookAt(Brain.transform.position + Vector3.up);
            yield return CaptureWorld("07b-refuge-actor-inside");
            CheckThat("refuge-discoverable", refugeObject != null && Array.Exists(Brain.Registry, x => x.StableId == "first-refuge" && x.Kind == NpcObjectKind.Place) &&
                refugeCentre.y > CoastalTerrain.Height(refugeCentre.x,refugeCentre.z),
                "Authored geometry above terrain, registered as a non-pickup place; this check does not verify refuge-discovery memory.");
            foreach (string pose in new[] { "Idle", "Crouch", "Sit" })
            {
                Brain.Actor.Animator.Play(pose, 0, 0); Brain.Actor.Animator.Update(.5f);
                for (int i = 0; i < 4; i++)
                {
                    var pivot = Brain.transform.position + Vector3.up * .9f;
                    Controls.View.transform.position = pivot + Quaternion.Euler(12, i * 90, 0) * Vector3.back * 3.2f; Controls.View.transform.LookAt(pivot);
                    yield return Capture("08-clothing-" + pose + "-" + i);
                }
            }
            Vector3 clubSlope = Vector3.zero; float clubSlopeDegrees = 0;
            for (float z = -40; z <= 180 && clubSlope == Vector3.zero; z += 8)
                for (float x = -220; x <= -70; x += 8)
                {
                    float h = CoastalTerrain.Height(x, z);
                    float dx = CoastalTerrain.Height(x + 1, z) - CoastalTerrain.Height(x - 1, z);
                    float dz = CoastalTerrain.Height(x, z + 1) - CoastalTerrain.Height(x, z - 1);
                    float degrees = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz) * .5f) * Mathf.Rad2Deg;
                    Vector3 candidate = new Vector3(x, h + .02f, z);
                    if (h > CoastalWater.Level + .3f && degrees >= 6 && degrees <= 24 &&
                        Brain.TerrainNavigation.Walkable(candidate, out _)) { clubSlope = candidate; clubSlopeDegrees = degrees; break; }
                }
            float minimumClubClearance = float.MaxValue; int clubGroundHits = 0, clubExpectedHits = 0, clubMotionSamples = 0;
            float clubTraversalMetres = 0; string minimumClubPose = "none";
            string missingClubState = "";
            if (hunterClub != null && clubSlope != Vector3.zero)
            {
                Brain.Actor.Place(clubSlope); Physics.SyncTransforms();
                float dx = CoastalTerrain.Height(clubSlope.x + 1, clubSlope.z) - CoastalTerrain.Height(clubSlope.x - 1, clubSlope.z);
                float dz = CoastalTerrain.Height(clubSlope.x, clubSlope.z + 1) - CoastalTerrain.Height(clubSlope.x, clubSlope.z - 1);
                Vector3 uphill = new Vector3(dx, 0, dz).normalized;
                foreach (float facing in new[] { 1f, -1f })
                {
                    Brain.transform.rotation = Quaternion.LookRotation(uphill * facing);
                    foreach (string pose in new[] { "Idle", "Walk", "Crouch", "CrouchWalk", "Pickup", "SitEnter", "Sit", "SitExit" })
                    {
                        int state = Animator.StringToHash(pose);
                        if (!Brain.Actor.Animator.HasState(0, state)) { missingClubState += pose + ";"; continue; }
                        for (int phase = 0; phase < 5; phase++)
                        {
                            Brain.Actor.Animator.Play(state, 0, phase * .2f); Brain.Actor.Animator.Update(0);
                            yield return new WaitForEndOfFrame();
                            int hits; float clearance = MeasureClubTerrainClearance(hunterClub, out hits);
                            if (clearance < minimumClubClearance) { minimumClubClearance = clearance; minimumClubPose = pose + "-" + (facing > 0 ? "uphill" : "downhill") + "-phase" + phase; }
                            clubGroundHits += hits; clubExpectedHits += hunterClub.Club.GetComponent<MeshFilter>().sharedMesh.vertexCount; clubMotionSamples++;
                            if (phase == 2)
                            {
                                var grip = hunterClub.GripCenter;
                                Controls.View.transform.position = grip + Quaternion.Euler(8, facing > 0 ? 145 : -35, 0) * Vector3.back * .72f;
                                Controls.View.transform.LookAt(grip);
                                yield return CaptureWorld("09-club-" + pose + "-" + (facing > 0 ? "uphill" : "downhill"));
                                var bodyPivot = Brain.transform.position + Vector3.up * .9f;
                                // View from the club-bearing left side so the whole shaft and
                                // terrain contact remain visible instead of hiding behind a leg.
                                Controls.View.transform.position = bodyPivot - Brain.transform.right * 2.5f - Brain.transform.forward * 1.6f + Vector3.up * .25f;
                                Controls.View.transform.LookAt(bodyPivot);
                                yield return CaptureWorld("09b-club-full-" + pose + "-" + (facing > 0 ? "uphill" : "downhill"));
                            }
                        }
                    }
                }
                Brain.Actor.Animator.Play("Walk", 0, 0); Vector3 traversalPrior = Brain.transform.position;
                foreach (float direction in new[] { 1f, -1f })
                    for (int step = 0; step < 40; step++)
                    {
                        Vector3 travelDirection = Brain.TerrainNavigation.ConstrainMotion(Brain.transform.position, uphill * direction,
                            Brain.Actor.WalkSpeed * NpcAutonomy.StepSeconds);
                        Brain.Actor.Step(travelDirection, NpcAutonomy.StepSeconds); yield return new WaitForFixedUpdate(); yield return new WaitForEndOfFrame();
                        clubTraversalMetres += Vector3.Distance(traversalPrior, Brain.transform.position); traversalPrior = Brain.transform.position;
                        int hits; float clearance = MeasureClubTerrainClearance(hunterClub, out hits);
                        if (clearance < minimumClubClearance) { minimumClubClearance = clearance; minimumClubPose = "continuous-walk-" + (direction > 0 ? "uphill" : "downhill") + "-step" + step; }
                        clubGroundHits += hits; clubExpectedHits += hunterClub.Club.GetComponent<MeshFilter>().sharedMesh.vertexCount; clubMotionSamples++;
                    }
            }
            CheckThat("corrected-club-uneven-terrain-clearance", hunterClub != null && clubSlope != Vector3.zero &&
                missingClubState.Length == 0 && clubMotionSamples == 160 && clubGroundHits == clubExpectedHits && clubTraversalMetres >= 1 && minimumClubClearance >= .005f,
                "80 uphill/downhill pose samples plus 80 continuous CharacterController traversal samples; actual/expected layer8/10 ray hits=" + clubGroundHits + "/" + clubExpectedHits +
                "; missingStates=" + (missingClubState.Length == 0 ? "none" : missingClubState) + "; slope=" + clubSlopeDegrees.ToString("F1") +
                "deg at " + clubSlope + "; cumulative traversal=" + clubTraversalMetres.ToString("F2") + "m; minimum=" + minimumClubClearance.ToString("F3") +
                "m at " + minimumClubPose + "; visual hand fit remains separate review");
            if (Array.IndexOf(args, "-npcLivingMemory") >= 0)
            {
                var living = StarfallLivingMemoryAcceptance.Verify(Brain, Controls.Hud, CheckThat, CaptureNow, directory);
                while (true)
                {
                    // Keep the evidence camera with the inhabitant after Verify
                    // resets the actor for its fresh, real delivery journey.
                    Controls.View.transform.position = Brain.transform.position + new Vector3(3, 2.5f, -4);
                    Controls.View.transform.LookAt(Brain.transform.position + Vector3.up);
                    if (!living.MoveNext()) break;
                    yield return living.Current;
                }
            }

            // -----------------------------------------------------------------
            // Riverbank Pilot Corridor Inspection & Multi-Carp Swimming Capture
            // -----------------------------------------------------------------
            {
                // 1. Walk Route View 1: Refuge Terrace Descent framed by Weathered Boulders & Ground Clutter
                float h1 = CoastalTerrain.Height(-126f, 112f);
                Controls.View.transform.position = new Vector3(-126f, h1 + 1.8f, 112f);
                float t1H = CoastalTerrain.Height(-105f, 98f);
                Controls.View.transform.LookAt(new Vector3(-105f, t1H + 0.6f, 98f));
                yield return CaptureWorld("10a-refuge-descent-boulders");

                // 2. Walk Route View 2: Hollow Log V2 on Terrace Fringe with Open Cavity Line-of-Sight
                Controls.View.transform.position = new Vector3(-68f, 2.4f, 74f);
                Controls.View.transform.LookAt(new Vector3(-56f, 1.1f, 67f));
                yield return CaptureWorld("10b-hollow-log-terrace-cavity");

                // 3. Walk Route View 3: Riparian Sedge & Reed Clustered Along Damp Waterline
                float h3 = CoastalTerrain.Height(-24f, 58f);
                Controls.View.transform.position = new Vector3(-24f, h3 + 1.6f, 58f);
                float t3H = CoastalTerrain.Height(-33f, 66f);
                Controls.View.transform.LookAt(new Vector3(-33f, t3H + 0.7f, 66f));
                yield return CaptureWorld("10c-waterline-riparian-sedges");

                // 3b. Scenic Waterfall Gorge View: Looking south at the multi-tier cascading waterfall and churning plunge pool
                Controls.View.transform.position = new Vector3(25f, 4.0f, -195f);
                Controls.View.transform.LookAt(new Vector3(25f, 10.0f, -245f));
                yield return CaptureWorld("10d-waterfall-south-cascade");

                // 4. Live Multi-Fish Swimming Video Frame Sequence (48 frames = 2s at 24fps)
                // Overlook shallow river pool from dry bank where carp school cruises
                float bankH = CoastalTerrain.Height(-6.5f, -28f);
                Controls.View.transform.position = new Vector3(-6.5f, bankH + 1.4f, -28f);
                Controls.View.transform.LookAt(new Vector3(-2f, CoastalWater.Level - 0.25f, -28f));
                string videoDir = Path.Combine(directory, "carp-swimming-frames");
                Directory.CreateDirectory(videoDir);

                // 4. Clean FPS Benchmark (Warmup >= 10s, continuous sampling >= 30s without camera renders or disk I/O)
                float warmupStart = Time.unscaledTime;
                while (Time.unscaledTime - warmupStart < 10.0f)
                {
                    yield return null;
                }

                var sampleDts = new List<float>();
                float sampleStart = Time.unscaledTime;
                while (Time.unscaledTime - sampleStart < 30.0f)
                {
                    float dt = Time.unscaledDeltaTime;
                    if (dt > 0.00001f) sampleDts.Add(dt);
                    yield return null;
                }

                if (sampleDts.Count > 0)
                {
                    sampleDts.Sort();
                    float minFps = 1f / sampleDts[sampleDts.Count - 1];
                    float maxFps = 1f / sampleDts[0];
                    float sumDt = 0f;
                    for (int i = 0; i < sampleDts.Count; i++) sumDt += sampleDts[i];
                    float meanDt = sumDt / sampleDts.Count;
                    float meanFps = 1f / meanDt;
                    float medianFps = 1f / sampleDts[sampleDts.Count / 2];
                    float p95Fps = 1f / sampleDts[(int)(sampleDts.Count * 0.95f)];
                    float p99Fps = 1f / sampleDts[(int)(sampleDts.Count * 0.99f)];

                    string perfJson = $"{{\"minFps\":{minFps:F2},\"maxFps\":{maxFps:F2},\"meanFps\":{meanFps:F2},\"medianFps\":{medianFps:F2},\"p95Fps\":{p95Fps:F2},\"p99Fps\":{p99Fps:F2},\"samples\":{sampleDts.Count},\"durationSeconds\":{(Time.unscaledTime - sampleStart):F1}}}";
                    File.WriteAllText(Path.Combine(directory, "carp-performance.json"), perfJson);
                    CheckThat("river-carp-performance-stable", minFps >= 5.0f && meanFps >= 8.0f,
                        $"Clean FPS distribution: min={minFps:F1}, max={maxFps:F1}, mean={meanFps:F1}, median={medianFps:F1}, p95={p95Fps:F1}, p99={p99Fps:F1} across {sampleDts.Count} frames");
                }

                // Capture video sequence after clean benchmark
                for (int f = 0; f < 24; f++)
                {
                    yield return new WaitForEndOfFrame();
                    RenderWorldNow(Path.Combine("carp-swimming-frames", $"carp-swim-{f + 1:D3}"));
                    yield return new WaitForSeconds(1f / 24f);
                }

                // -----------------------------------------------------------------
                // 5. Genuine End-to-End Fishing Scenario:
                // Rod Equip -> Cast -> Bite & Strike -> Land to Left Hand ->
                // Authoritative Eat with Conserved Nutrition Increase ->
                // Store in Container via Live Action Authority -> Retrieve ->
                // Isolated Save Reload Verification
                // -----------------------------------------------------------------
                var school = RiverFishSchool.Instance ?? FindFirstObjectByType<RiverFishSchool>();
                var fishing = Brain.GetComponent<FishingInteraction>() ?? Brain.GetComponentInChildren<FishingInteraction>();
                CheckThat("fishing-runtime-components-present", school != null && fishing != null, "school and fishing interaction present");

                string proofPath = Path.Combine(directory, "fish-ownership-proof.json");
                bool isReloadVerificationPass = File.Exists(proofPath);

                if (isReloadVerificationPass)
                {
                    // =============================================================
                    // FRESH PROCESS RELOAD VERIFICATION PASS (Process 2)
                    // =============================================================
                    string proofJson = File.ReadAllText(proofPath);
                    var proof = JsonUtility.FromJson<FishOwnershipProofRecord>(proofJson);
                    CheckThat("reload-proof-file-loaded", proof != null && !string.IsNullOrEmpty(proof.firstConsumedFishId),
                        "Loaded fish ownership proof from previous process: " + proofPath);

                    if (proof != null)
                    {
                        // 1. First consumed fish must remain strictly retired, NEVER resurrected!
                        bool firstRetired = Brain.PhysicalItems.Model.IsRetired(proof.firstConsumedFishId);
                        CheckThat("reload-first-fish-permanently-retired", firstRetired,
                            $"First fish ({proof.firstConsumedFishId}) remains permanently retired across separate process reload");

                        // 2. Second retained fish must exist in authoritative model and not be retired
                        bool secondExists = Brain.PhysicalItems.Model.TryGetItem(proof.secondRetainedFishId, out var secondRec);
                        bool secondRetired = Brain.PhysicalItems.Model.IsRetired(proof.secondRetainedFishId);
                        CheckThat("reload-second-fish-retained", secondExists && !secondRetired,
                            $"Second fish ({proof.secondRetainedFishId}) authoritatively conserved across separate process reload");

                        // 3. Second fish location must be Carried or Stored (as left by Process 1)
                        bool locationValid = secondRec != null && (secondRec.location == ItemLocationKind.Carried || secondRec.location == ItemLocationKind.Stored);
                        CheckThat("reload-second-fish-location-valid", locationValid,
                            $"Second fish location ({secondRec?.location}) is authoritatively conserved");

                        // 4. In RiverFishSchool, neither fish may swim in the river (baked visuals suppressed)
                        bool firstSuppressedInSchool = school == null || school.ActiveFish == null ||
                            !school.ActiveFish.Exists(f => f != null && (
                                (f.physicalItem != null && f.physicalItem.itemId == proof.firstConsumedFishId) ||
                                (f.interactable != null && f.interactable.StableId == proof.firstConsumedFishId) ||
                                (f.gameObject != null && f.gameObject.name == proof.firstConsumedFishId)));
                        CheckThat("reload-first-fish-suppressed-in-river", firstSuppressedInSchool,
                            $"First consumed fish ({proof.firstConsumedFishId}) suppressed from river school");

                        bool secondSuppressedInSchool = school == null || school.ActiveFish == null ||
                            !school.ActiveFish.Exists(f => f != null && (
                                (f.physicalItem != null && f.physicalItem.itemId == proof.secondRetainedFishId) ||
                                (f.interactable != null && f.interactable.StableId == proof.secondRetainedFishId) ||
                                (f.gameObject != null && f.gameObject.name == proof.secondRetainedFishId)));
                        CheckThat("reload-second-fish-suppressed-in-river", secondSuppressedInSchool,
                            $"Second retained fish ({proof.secondRetainedFishId}) suppressed from swimming in river");

                        // 5. Conserved meal nutrition & evidence
                        var s = Brain.Survival.Food.Model.State;
                        bool mealConserved = s != null && s.knowsMealBenefit &&
                            !string.IsNullOrEmpty(s.lastMealEvidence) &&
                            s.lastMealEvidence.StartsWith(s.generation + ".ate.", StringComparison.Ordinal);
                        CheckThat("reload-meal-benefit-conserved", mealConserved,
                            $"Meal benefit and .ate. evidence conserved: {s?.lastMealEvidence}");
                    }
                }
                else if (school != null && fishing != null && Brain.Actions != null && Brain.Survival != null)
                {
                    // Move actor to river bank overlooking shallow pool where fish swim
                    Vector3 bankPos = new Vector3(-6.5f, CoastalTerrain.Height(-6.5f, -28f), -28f);
                    Brain.transform.position = bankPos;
                    if (Brain.Actor != null) Brain.Actor.transform.position = bankPos;

                    // A. Equip rod in right hand
                    var rodNi = Brain.Registry != null ? Brain.Registry.FirstOrDefault(x => x != null && x.StableId.Contains("rod")) : null;
                    if (rodNi != null)
                    {
                        Brain.Actions.HoldItemDirect(rodNi, false);
                    }
                    CheckThat("e2e-fishing-rod-equipped-in-right-hand", Brain.Actions.HeldRight != null && Brain.Actions.HeldRight.StableId.Contains("rod"),
                        "Right hand holds fishing rod");

                    // B. First Catch: Genuine Cast -> Floating -> Nibble -> Bite -> StrikeAndReel -> Reeling -> LandCatch
                    Vector3 castPos = new Vector3(-3.5f, CoastalWater.CurrentLevel, -28f);
                    fishing.StartCast(castPos);
                    CheckThat("e2e-fishing-cast-initiated", fishing.IsFishingActive && fishing.State == FishingState.Casting, "First fishing cast initiated");

                    fishing.Tick(FishingInteraction.CastDuration + 0.05f);
                    CheckThat("e2e-fishing-state-floating", fishing.State == FishingState.Floating, "Bobber is floating in river");

                    float firstInterestDeadline = Time.realtimeSinceStartup + 60f;
                    while (fishing.State == FishingState.Floating && Time.realtimeSinceStartup < firstInterestDeadline)
                        yield return null;
                    CheckThat("e2e-fishing-state-nibble", fishing.State == FishingState.Nibble, "Bobber indicates fish nibble");

                    fishing.Tick(FishingInteraction.NibbleDuration + 0.05f);
                    CheckThat("e2e-fishing-state-bite", fishing.State == FishingState.Bite, "Bobber plunges on active bite");

                    bool struck1 = fishing.StrikeAndReel(out string speciesHooked1, out float scaleHooked1, out string strikeReceipt1);
                    CheckThat("e2e-fishing-strike-succeeded", struck1 && fishing.State == FishingState.Reeling, "Strike timed successfully during bite window: " + strikeReceipt1);

                    fishing.Tick(FishingInteraction.ReelDuration + 0.05f);
                    CheckThat("e2e-fishing-catch-landed-to-left-hand", Brain.Actions.HeldLeft != null, "First catch landed safely into left hand");
                    CheckThat("e2e-fishing-rod-retained-in-right-hand", Brain.Actions.HeldRight != null && Brain.Actions.HeldRight == rodNi,
                        "Right hand firmly retains fishing rod while left hand holds catch");

                    string firstFishId = Brain.Actions.HeldLeft != null ? Brain.Actions.HeldLeft.StableId : "fish-1";

                    // D. Authoritative Eat with Conserved Nutrition Increase
                    int initialSatiety = Brain.Survival.Food.Model.State.satiety;
                    int initialProtein = Brain.Survival.Food.Model.State.body != null ? Brain.Survival.Food.Model.State.body.protein : 0;
                    var heldCatchNi = Brain.Actions.HeldLeft;
                    var heldCatchPhys = heldCatchNi != null ? heldCatchNi.GetComponent<PhysicalItem>() : null;

                    bool eatOk = FoodConsumptionBridge.TryConsumeHeldFood(
                        Brain,
                        heldCatchNi,
                        heldCatchPhys,
                        true,
                        "e2e-acceptance-consume-catch",
                        out string eatReceipt);

                    CheckThat("e2e-fishing-catch-consumed-authoritatively", eatOk, "Catch consumed via FoodConsumptionBridge: " + eatReceipt);
                    CheckThat("e2e-fishing-satiety-increased", Brain.Survival.Food.Model.State.satiety > initialSatiety,
                        $"Satiety increased from {initialSatiety} to {Brain.Survival.Food.Model.State.satiety}");
                    CheckThat("e2e-fishing-protein-increased", (Brain.Survival.Food.Model.State.body != null ? Brain.Survival.Food.Model.State.body.protein : 0) > initialProtein,
                        "Protein increased after eating catch");
                    CheckThat("e2e-fishing-left-hand-cleared-after-eat", Brain.Actions.HeldLeft == null, "Left hand cleared after eating catch");
                    CheckThat("e2e-fishing-consumed-item-retired", Brain.PhysicalItems.Model.IsRetired(firstFishId), "Consumed fish is strictly retired in physical model");

                    // Re-attempting to consume the retired item must fail closed
                    bool reConsumeRefused = !FoodConsumptionBridge.TryConsumeHeldFood(Brain, heldCatchNi, heldCatchPhys, true, "re-eat", out string reEatCode);
                    CheckThat("e2e-fishing-re-consume-refused", reConsumeRefused, "Re-consumption of already retired item refused");

                    // E. Second Catch: Genuine Cast -> Floating -> Nibble -> Bite -> StrikeAndReel -> Reeling -> LandCatch
                    fishing.StartCast(castPos);
                    CheckThat("e2e-second-cast-initiated", fishing.IsFishingActive && fishing.State == FishingState.Casting, "Second fishing cast initiated");

                    fishing.Tick(FishingInteraction.CastDuration + 0.05f);
                    CheckThat("e2e-second-state-floating", fishing.State == FishingState.Floating, "Second bobber is floating in river");

                    float secondInterestDeadline = Time.realtimeSinceStartup + 60f;
                    while (fishing.State == FishingState.Floating && Time.realtimeSinceStartup < secondInterestDeadline)
                        yield return null;
                    CheckThat("e2e-second-state-nibble", fishing.State == FishingState.Nibble, "Second bobber indicates fish nibble");

                    fishing.Tick(FishingInteraction.NibbleDuration + 0.05f);
                    CheckThat("e2e-second-state-bite", fishing.State == FishingState.Bite, "Second bobber plunges on active bite");

                    bool struck2 = fishing.StrikeAndReel(out string speciesHooked2, out float scaleHooked2, out string strikeReceipt2);
                    CheckThat("e2e-second-strike-succeeded", struck2 && fishing.State == FishingState.Reeling, "Second strike timed successfully: " + strikeReceipt2);

                    fishing.Tick(FishingInteraction.ReelDuration + 0.05f);
                    CheckThat("e2e-second-catch-landed-to-left-hand", Brain.Actions.HeldLeft != null, "Second catch landed safely into left hand");
                    CheckThat("e2e-second-rod-retained-in-right-hand", Brain.Actions.HeldRight != null && Brain.Actions.HeldRight == rodNi,
                        "Right hand firmly retains fishing rod while left hand holds second catch");

                    string secondFishId = Brain.Actions.HeldLeft != null ? Brain.Actions.HeldLeft.StableId : "fish-2";

                    if (Brain.Actions.PhysicalAuthority == null)
                    {
                        Brain.Actions.PhysicalAuthority = Brain.PhysicalItems != null && Brain.PhysicalItems.Authority != null
                            ? Brain.PhysicalItems.Authority
                            : new BasicItemActionAuthority();
                    }

                    var basketNi = Brain.Registry != null ? Brain.Registry.FirstOrDefault(x => x != null && x.StableId.Contains("basket")) : null;
                    if (basketNi == null)
                    {
                        Vector3 bPos = bankPos + Vector3.right * 0.4f;
                        var bGo = new GameObject("container-basket-riverside");
                        bGo.transform.position = bPos;
                        basketNi = bGo.AddComponent<NpcInteractable>();
                        basketNi.StableId = "container-basket-riverside";
                        basketNi.Kind = NpcObjectKind.Item;
                        basketNi.WorldId = Brain.InstanceWorldId;
                        basketNi.Permission = true;

                        var approachGo = new GameObject("Approach");
                        approachGo.transform.SetParent(bGo.transform, false);
                        approachGo.transform.position = bPos;
                        basketNi.Approach = approachGo.transform;

                        var bPhys = bGo.AddComponent<PhysicalItem>();
                        bPhys.itemId = "container-basket-riverside";
                        bPhys.itemTypeId = "container-basket";
                        bPhys.massKg = 1.0f;
                        bPhys.dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f);
                        bPhys.ConfigureComponents();
                        if (Brain.Actions.PhysicalModel != null)
                        {
                            bPhys.Bind(Brain.Actions.PhysicalModel, Brain.InstanceWorldId, Brain.Actions.PhysicalModel.GenerationId);
                            Brain.Actions.PhysicalModel.RegisterItem("container-basket-riverside", "container-basket", ItemLocationKind.Free, bPos, Quaternion.identity);
                        }
                        if (Brain.PhysicalItems != null)
                        {
                            var binding = new PhysicalItemRuntimeBinding("container-basket-riverside", bPhys, basketNi);
                            Brain.PhysicalItems.RegisterBinding(binding);
                        }
                        Brain.Actions.RegisterInteractable(basketNi);
                    }
                    else
                    {
                        basketNi.WorldId = Brain.InstanceWorldId;
                        basketNi.Permission = true;
                        if (basketNi.Approach == null)
                        {
                            var approachGo = new GameObject("Approach");
                            approachGo.transform.SetParent(basketNi.transform, false);
                            approachGo.transform.position = basketNi.transform.position;
                            basketNi.Approach = approachGo.transform;
                        }
                        Brain.Actions.RegisterInteractable(basketNi);
                    }

                    bool storeReqOk = Brain.TryAllocateRequestId(out int storeReqId);
                    CheckThat("e2e-store-req-allocated", storeReqOk, "Allocated request ID for store");
                    var storeRes = Brain.Actions.Store(storeReqId, basketNi.StableId, secondFishId);
                    CheckThat("e2e-fishing-catch-stored-authoritatively", storeRes.success,
                        $"Stored second catch into basket via Brain.Actions.Store: {storeRes.code}");
                    CheckThat("e2e-fishing-rod-still-retained-after-store", Brain.Actions.HeldRight == rodNi && Brain.Actions.HeldLeft == null,
                        "Right hand rod retained, left hand cleared after store");

                    // Retrieve catch from basket
                    bool takeReqOk = Brain.TryAllocateRequestId(out int takeReqId);
                    CheckThat("e2e-take-req-allocated", takeReqOk, "Allocated request ID for retrieve");
                    var takeRes = Brain.Actions.Take(takeReqId, secondFishId, basketNi.StableId);
                    CheckThat("e2e-fishing-catch-retrieved-from-basket", takeRes.success,
                        $"Retrieved catch from basket via Brain.Actions.Take: {takeRes.code}");
                    CheckThat("e2e-second-fish-held-after-retrieve", Brain.Actions.HeldLeft != null && Brain.Actions.HeldLeft.StableId == secondFishId,
                        "Second fish is held in left hand after retrieve");

                    // F. Isolated Save Persistence & Reload Proof
                    Brain.PhysicalItems.SaveCurrentState();
                    Brain.Survival.Persist();
                    CheckThat("e2e-isolated-saves-written",
                        File.Exists(Brain.Survival.SavePath) && File.Exists(Brain.PhysicalItems.PhysicalSavePath),
                        "Isolated save files written to isolated paths");

                    bool syncOk = FoodConsumptionBridge.TryCommitCoordinatedCheckpoint(Brain, "e2e-post-fishing-persistence", out string syncReceipt);
                    CheckThat("e2e-coordinated-checkpoint-committed", syncOk,
                        "Coordinated checkpoint committed after fishing flow: " + syncReceipt);

                    var proofRecord = new FishOwnershipProofRecord
                    {
                        firstConsumedFishId = firstFishId,
                        secondRetainedFishId = secondFishId,
                        basketContainerId = basketNi.StableId,
                        timestamp = DateTime.UtcNow.ToString("o")
                    };
                    File.WriteAllText(proofPath, JsonUtility.ToJson(proofRecord, true));
                    CheckThat("e2e-fish-ownership-proof-written", File.Exists(proofPath),
                        "Fish ownership proof written to: " + proofPath);
                }
            }

            report.deliveries = Brain.Actions.Deliveries; report.errors.AddRange(errors);
            CheckThat("no-runtime-errors", errors.Count == 0, errors.Count + " recorded errors");
            report.status = report.checks.Exists(x => !x.passed) ? "FAIL" : "PASS_AUTOMATED_NATIVE_AND_COVERAGE_REVIEW_PENDING";
            File.WriteAllText(Path.Combine(directory, "integrated-runtime.json"), JsonUtility.ToJson(report, true));
            Application.Quit(report.checks.Exists(x => !x.passed) ? 4 : 0);
        }
        private void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    }
}
