using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;

namespace CityLife.World.Editor
{
    /// <summary>
    /// Isolated editor/GPU inspection. No scene save, play mode, window, cursor or input APIs.
    /// RenderBatch is launched by tools/render-kokerboom.ps1 without -nographics or -quit.
    /// Technical checks deliberately do not score the artwork or claim model acceptance.
    /// </summary>
    public static partial class KokerboomRender
    {
        private sealed class Subject
        {
            public GameObject Root;
            public int Seed;
            public float Age;
            public int Lod;
            public string Kind = "tree";
            public string AssetPath = "";
            public string[] SourceMaterialNames = Array.Empty<string>();
        }

        private sealed class Shot
        {
            public string Id, Purpose;
            public string ComparisonGroup, ComparisonVariant;
            public bool ComparisonNormalMap;
            public int Height;
            public Action Configure;
            public Action Restore;
        }

        private static readonly List<Subject> subjects = new List<Subject>();
        private static readonly List<Object> owned = new List<Object>();
        private static readonly List<ShotRecord> results = new List<ShotRecord>();
        private static Camera camera;
        private static Light key, fill, rim;
        private static GameObject mainTree, lineup, companions, backdrop, neutralFloor, cosmicFloor, metreRuler;
        private static GameObject rosetteFixture, juvenileFixture, adultVariants;
        private static Bounds mainBounds, lineupBounds;
        private static Material neutralMaterial, sandMaterial, rulerMaterial;
        private static RenderTexture target;
        private static UniversalRenderPipelineAsset inspectionPipeline;
        private static UniversalRendererData inspectionRenderer;
        private static RenderPipelineAsset previousGraphicsPipeline, previousQualityPipeline;
        private static int seed, width, errors, warnings;
        private static string outputDirectory, relativeDirectory, dateStamp;
        private static bool safeToWrite, previousAsyncCompilation, pipelineConfigured;
        private static int previousAntiAliasing;
        private static bool previousLodCrossFade, previousSrpBatching, previousLinearLightIntensity, previousColorTemperature;
        private static bool importedCandidateMode;
        private static bool hybridMode;
        private static bool playablePreviewMode;
        private static bool ph02CrownMode;
        private static bool ph02FittedMode;
        private static bool ph02FamilyMode,ph02FamilyMeshReadbackPassed;
        private static float ph02FoliageTint;
        private static bool ph02ShotSelectionRequested;
        private static string[] ph02SelectedShotIds=Array.Empty<string>();
        private static PH02FamilyComponent.Report ph02FamilyChecks;
        private static string ph02FamilyMeshSha256,ph02FamilyFailureType;
        private static bool ph02ImportedTuplesRequested,ph02ImportedCrownMeshReadbackPassed;
        private static PH02ImportedCrownCandidate.Report ph02ImportedCrownChecks;
        private static string ph02ImportedMeshExpandedSha256,ph02ReferenceCameraManifestSha256;
        private static readonly Dictionary<string,CameraRecord> ph02ReferenceCameras=new Dictionary<string,CameraRecord>();
        private const string PH02ReferenceCameraManifest="evidence/milestones/kokerboom/round-13/metrics.json";
        private static bool woodDiagnosticMode;
        private static GameObject ph02FitOriginal,ph02FitCurrent,ph02FitCandidate;
        private static Material ph02FitSourceMaterial,ph02FitSupportMaterial,ph02FitGrayMaterial;
        private static Bounds ph02FitBounds;
        private static PH02FittedSupportCandidate.Report ph02FitMetrics;
        private static PH02FitReadback ph02FitReadback;
        private static string ph02FitFailureType;
        private static bool ph01TangentMode,ph01ExactTupleDedup,ph01OriginalSubsetsReady;
        private static Mesh[] tangentBaseline,tangentCandidates,tangentOriginals;
        private static Material tangentNormalOn,tangentNormalOff;
        private static GameObject tangentFixture;
        private static Vector3[] tangentRoots;
        private static Bounds[] tangentFrameBounds;
        private static PH01ImportedTupleCandidate.Report importedTupleChecks;
        private static string importedTupleFailureType;
        private static float ph02CutHeight;
        private static GameObject ph02Full, ph02Crown, ph02Attached;
        private static PH02CrownCandidate.CutReport ph02CutReport;
        private static Mesh[] sourceRosettes,sourceBasalMeshes;
        private static Material sourceLeafMaterial;
        private static GameObject sourceFixture;
        private static readonly Dictionary<Renderer,Material[]> albedoOriginals=new Dictionary<Renderer,Material[]>();
        private static int activeCandidate;
        private static readonly Dictionary<int, GameObject> importedTrees = new Dictionary<int, GameObject>();
        private static readonly Dictionary<string, Material> importedMaterials = new Dictionary<string, Material>();
        private static readonly Dictionary<Material, TextureBinding[]> importedBindings = new Dictionary<Material, TextureBinding[]>();
        private static AmbientRecord initialAmbient;
        private static Material[] diagnosticOriginalMaterials;
        private static Renderer diagnosticRenderer;
        private static int ExpectedCaptures => coastalMode ? 6 : playablePreviewMode ? 2 : ph02FamilyMode ? ph02SelectedShotIds.Length : woodDiagnosticMode ? 3 : ph02FittedMode ? 12 : ph01TangentMode ? (ph01OriginalSubsetsReady ? 12 : 8) : ph02CrownMode ? 8 : importedCandidateMode ? 12 : hybridMode ? 21 : 15;

        public static void RenderBatch() => Run(false);
        public static void RenderImportedCandidates() => Run(true);
        public static void RenderHybridFamily() { hybridMode=true;Run(false); }
        public static void BuildPlayablePreview() { playablePreviewMode=true;hybridMode=true;Run(false); }
        public static void BuildR19PlayablePreview() { playablePreviewMode=true;ph02FamilyMode=true;hybridMode=true;Run(false); }
        public static void RenderPH02CrownCandidate() { ph02CrownMode=true;Run(false); }
        public static void RenderPH01TangentComparison() { ph01TangentMode=true;Run(false); }
        public static void RenderPH02FittedSupport() { ph02FittedMode=true;Run(false); }
        public static void RenderPH02Family() { ph02FamilyMode=true;hybridMode=true;Run(false); }
        public static void RenderWoodDiagnostic() { woodDiagnosticMode=true;hybridMode=true;Run(false); }

        private static void Run(bool imported)
        {
            importedCandidateMode = imported;
            int exitCode = 2;
            Application.logMessageReceived += ObserveLog;
            try
            {
                if (!Application.isBatchMode)
                    throw new InvalidOperationException("Kokerboom inspection is batch-only. Launch the isolated script; no desktop editor operation is needed.");
                ConfigureOutput();
                SetupPipeline();
                SetupScene();
                List<Shot> shots = woodDiagnosticMode ? BuildShots().Where(s=>s.Id.StartsWith("04-",StringComparison.Ordinal)||s.Id.StartsWith("05-",StringComparison.Ordinal)||s.Id.StartsWith("15-",StringComparison.Ordinal)).ToList() : ph02FittedMode ? BuildPH02FittedShots() : ph01TangentMode ? BuildPH01TangentShots() : ph02CrownMode ? BuildPH02Shots() : playablePreviewMode ? BuildPreviewShots() : importedCandidateMode ? BuildImportedShots() : BuildShots();
                if(coastalMode)shots=BuildCoastalShots();
                else if(ph02FamilyMode&&!playablePreviewMode)shots=shots.Where(s=>ph02SelectedShotIds.Contains(s.Id,StringComparer.Ordinal)).ToList();
                foreach (Shot shot in shots)
                {
                    shot.Configure();
                    if(ph02ImportedTuplesRequested)ApplyPH02ReferenceCamera(shot.Id);
                    try { Capture(shot); }
                    finally { shot.Restore?.Invoke(); }
                }
                bool technicalPass = errors == 0 && results.Count == ExpectedCaptures && results.All(r => r.image.nonUniformContent && r.subjects.Length > 0)
                    && (!ph01TangentMode || (importedTupleChecks != null && importedTupleChecks.numericChecksPassed))
                    && (!ph02FittedMode || (ph02FitMetrics != null && ph02FitMetrics.numericChecksPassed && ph02FitReadback != null && ph02FitReadback.passed))
                    && (!ph02FamilyMode || (ph02FamilyChecks!=null&&ph02FamilyChecks.numericChecksPassed&&ph02FamilyMeshReadbackPassed));
                WriteReport(technicalPass);
                if(coastalMode && technicalPass && Argument("-coastalPlayer", "0") == "1")
                {
                    CoastalCamera(new Vector3(-7,1.85f,-5),new Vector3(0,4,14));
                    CoastalPlayerBuild.Build(camera,coast.GetComponentInChildren<MeshCollider>().gameObject,new Dictionary<string,string>{
                        {"Hidden/CityLife/KokerboomGasInspection",GasShader},
                        {"Hidden/CityLife/KokerboomAtmosphereInspection",AtmosphereShader},
                        {"Hidden/CityLife/KokerboomStarsInspection",StarShader},
                        {"Hidden/Starfall/CoastalGalaxy",CoastalGalaxyShader}
                    },outputDirectory,true,Argument("-previewSourceCommit",""),ph02FamilyMeshSha256);
                }
                if(playablePreviewMode&&technicalPass)
                {
                    if(coastalMode)CoastalCamera(new Vector3(21,4.6f,-25),new Vector3(0,3.4f,14));
                    else PreviewEye();
                    CosmicPreviewBuild.Build(camera,cosmicFloor,new Dictionary<string,string>{
                        {"Hidden/CityLife/KokerboomGasInspection",GasShader},
                        {"Hidden/CityLife/KokerboomAtmosphereInspection",AtmosphereShader},
                        {"Hidden/CityLife/KokerboomStarsInspection",StarShader},
                        {"Hidden/Starfall/CoastalGalaxy",CoastalGalaxyShader}
                    },outputDirectory,ph02FamilyMode,Argument("-previewSourceCommit",""),ph02FamilyMeshSha256,coastalMode,coastalTerrain);
                }
                Debug.Log("KOKERBOOM_RENDER_FINISHED " + relativeDirectory + "/metrics.json technicalChecks=" + technicalPass + (playablePreviewMode&&ph02FamilyMode?"; frozen R19 integration evidence, not a new tree review.":"; visual acceptance requires independent critique."));
                exitCode = technicalPass ? 0 : 3;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (safeToWrite)
                {
                    try { WriteReport(false); }
                    catch (Exception reportError) { Debug.LogException(reportError); }
                }
            }
            finally
            {
                Cleanup();
                Application.logMessageReceived -= ObserveLog;
                EditorApplication.Exit(exitCode);
            }
        }

        private static void ConfigureOutput()
        {
            string round = Argument("-kokerboomRound", "round-01");
            if (!Regex.IsMatch(round, "^round-[0-9]{2,3}$")) throw new ArgumentException("Round must look like round-01.");
            seed = int.Parse(Argument("-kokerboomSeed", "4242"), CultureInfo.InvariantCulture);
            width = int.Parse(Argument("-kokerboomWidth", "1600"), CultureInfo.InvariantCulture);
            if(ph02CrownMode) ph02CutHeight=float.Parse(Argument("-ph02CutHeight",PH02CrownCandidate.ProposedCutHeight.ToString("R",CultureInfo.InvariantCulture)),CultureInfo.InvariantCulture);
            if(ph01TangentMode)ph01ExactTupleDedup=Argument("-ph01ExactTupleDedup","0")=="1";
            if(ph02FittedMode)ph02ImportedTuplesRequested=Argument("-ph02ImportedTuples","0")=="1";
            ph02ShotSelectionRequested=Array.IndexOf(Environment.GetCommandLineArgs(),"-ph02Shots")>=0;
            if(ph02ShotSelectionRequested&&!ph02FamilyMode)throw new ArgumentException("-ph02Shots requires the PH02Family inspection mode.");
            if(ph02FamilyMode)
            {
                // Building the shot declarations does not execute any Configure callback.
                // Resolve against this actual PH02 catalogue before creating a scene/tree.
                if(playablePreviewMode&&ph02ShotSelectionRequested)throw new ArgumentException("R19 preview uses its two fixed study views.");
                ph02SelectedShotIds=playablePreviewMode?BuildPreviewShots().Select(s=>s.Id).ToArray():ResolvePH02ShotSelection(ph02ShotSelectionRequested?Argument("-ph02Shots",""):null,BuildShots().Select(s=>s.Id).ToArray());
                ph02FoliageTint=float.Parse(Argument("-ph02FoliageTint",playablePreviewMode?"1":"0"),CultureInfo.InvariantCulture);
                if(float.IsNaN(ph02FoliageTint)||float.IsInfinity(ph02FoliageTint)||ph02FoliageTint<0||ph02FoliageTint>1)throw new ArgumentOutOfRangeException("ph02FoliageTint");
                if(playablePreviewMode&&(seed!=4242||ph02FoliageTint!=1))throw new ArgumentException("Frozen R19 preview requires seed4242 and foliage tint1.");
            }
            if (width < 800 || width > 3840) throw new ArgumentException("Width must be between 800 and 3840 pixels.");
            relativeDirectory = (coastalMode?"evidence/milestones/coastal/":"evidence/milestones/kokerboom/") + round;
            outputDirectory = Path.Combine(Path.GetDirectoryName(Application.dataPath), relativeDirectory);
            if (Directory.Exists(outputDirectory) && Directory.EnumerateFiles(outputDirectory).Any())
                throw new IOException("This render round already contains evidence. Select a new round; previous captures are preserved.");
            Directory.CreateDirectory(outputDirectory);
            dateStamp = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            safeToWrite = true;
        }

        private static string[] ResolvePH02ShotSelection(string csv,string[] availableIds)
        {
            if(availableIds==null||availableIds.Length!=21||availableIds.Distinct(StringComparer.Ordinal).Count()!=availableIds.Length)
                throw new InvalidOperationException("The PH02 family catalogue must contain its 21 unique existing shot IDs.");
            if(csv==null)return (string[])availableIds.Clone();
            if(string.IsNullOrWhiteSpace(csv))throw new ArgumentException("PH02Shots must be a nonempty comma-separated list such as 05,08,13,18,20,21.");
            var numbers=new HashSet<string>(StringComparer.Ordinal);
            foreach(string raw in csv.Split(','))
            {
                string number=raw.Trim();
                if(!Regex.IsMatch(number,"^[0-9]{2}$")||!availableIds.Any(id=>id.StartsWith(number+"-",StringComparison.Ordinal)))
                    throw new ArgumentException("Unknown PH02 shot number: "+number);
                if(!numbers.Add(number))throw new ArgumentException("Duplicate PH02 shot number: "+number);
            }
            // Always preserve the full catalogue's order and exact Configure/Restore
            // delegates. Requested order must not change cross-shot state or cameras.
            return availableIds.Where(id=>numbers.Contains(id.Substring(0,2))).ToArray();
        }

        private static string Argument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        private static void SetupPipeline()
        {
            previousGraphicsPipeline = GraphicsSettings.defaultRenderPipeline;
            previousQualityPipeline = QualitySettings.renderPipeline;
            previousAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            previousAntiAliasing = QualitySettings.antiAliasing;
            previousLodCrossFade = QualitySettings.enableLODCrossFade;
            previousSrpBatching = GraphicsSettings.useScriptableRenderPipelineBatching;
            previousLinearLightIntensity = GraphicsSettings.lightsUseLinearIntensity;
            previousColorTemperature = GraphicsSettings.lightsUseColorTemperature;
            pipelineConfigured = true;
            ShaderUtil.allowAsyncCompilation = false;
            var source = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            var rendererSource = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            if (source == null || rendererSource == null) throw new InvalidOperationException("The pinned URP pipeline/renderer assets are required.");
            inspectionPipeline = Object.Instantiate(source);
            inspectionRenderer = Object.Instantiate(rendererSource);
            inspectionPipeline.name = "Temporary kokerboom inspection pipeline";
            inspectionRenderer.name = "Temporary kokerboom inspection renderer";
            inspectionPipeline.hideFlags = inspectionRenderer.hideFlags = HideFlags.HideAndDontSave;
            inspectionRenderer.rendererFeatures.Clear();
            var rendererSettings = new SerializedObject(inspectionRenderer);
            rendererSettings.FindProperty("m_RenderingMode").intValue = 0;
            rendererSettings.ApplyModifiedPropertiesWithoutUndo();
            var settings = new SerializedObject(inspectionPipeline);
            var renderers = settings.FindProperty("m_RendererDataList");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = inspectionRenderer;
            settings.FindProperty("m_DefaultRendererIndex").intValue = 0;
            settings.ApplyModifiedPropertiesWithoutUndo();
            inspectionPipeline.renderScale = 1f;
            inspectionPipeline.msaaSampleCount = 4;
            inspectionPipeline.shadowDistance = 100f;
            inspectionPipeline.supportsHDR = true;
            GraphicsSettings.defaultRenderPipeline = inspectionPipeline;
            QualitySettings.renderPipeline = inspectionPipeline;
            // Only in-memory clones are changed; the project pipeline assets are not saved.
        }

        private static void SetupScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            initialAmbient = AmbientRecord.From(RenderSettings.ambientProbe, "Scene probe before explicit harness ambient");
            var cameraObject = new GameObject("Kokerboom offscreen camera");
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 1500f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.useOcclusionCulling = false;
            var extra = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = false;
            extra.antialiasing = AntialiasingMode.None;
            extra.SetRenderer(0);

            key = MakeLight("Key", new Vector3(-8, 12, -10), new Vector3(38, -35, 0));
            fill = MakeLight("Fill", new Vector3(9, 7, -3), new Vector3(24, 100, 0));
            rim = MakeLight("Rim", new Vector3(3, 10, 12), new Vector3(35, 170, 0));
            key.shadows = LightShadows.Soft;
            RenderSettings.sun = key;
            RenderSettings.fog = false;
            RenderSettings.skybox = null;

            if(ph02FamilyMode)SetupPH02FamilySources();else if(hybridMode)SetupHybridSources();

            mainTree = ph02FittedMode ? SetupPH02Fitted() : ph01TangentMode ? SetupPH01Tangents() : ph02CrownMode ? SetupPH02Candidate() : importedCandidateMode ? AddImportedTree(1) : AddTree(seed, 1f, Vector3.zero, "Mature inspection tree");
            mainBounds = TreeBounds(mainTree);
            if (mainBounds.size.y <= 0.1f) throw new InvalidOperationException("Tree has no measurable vertical extent.");
            if (!importedCandidateMode && !ph02CrownMode && !ph01TangentMode && !ph02FittedMode)
            {
                RequiredAnchor("Inspection/BranchUnion");
                RequiredAnchor("Inspection/TrunkBark");
                RequiredAnchor("Inspection/TerminalRosette");
            }

            neutralMaterial = LitMaterial("Inspection neutral ground", new Color(0.42f, 0.44f, 0.45f), 0.05f);
            // The photograph already supplies the albedo. Multiplying it by dark orange
            // in linear colour space made the first prototype ground maroon and unreadable.
            sandMaterial = LitMaterial("Prototype warm sand - untinted source albedo", Color.white, 0.03f);
            Texture2D dryGround = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CityLife/Art/Resources/CityLifeArt/DryGround_1K.jpg");
            if (dryGround != null)
            {
                sandMaterial.SetTexture("_BaseMap", dryGround);
                sandMaterial.SetTextureScale("_BaseMap", Vector2.one * 20f);
            }
            sandMaterial.EnableKeyword("_EMISSION");
            sandMaterial.SetColor("_EmissionColor", new Color(.11f, .065f, .023f));
            sandMaterial.SetTexture("_EmissionMap", dryGround);
            rulerMaterial = UnlitMaterial("Metre scale marks", new Color(0.90f, 0.95f, 0.95f));
            neutralFloor = Primitive(PrimitiveType.Plane, "Neutral inspection ground", neutralMaterial, new Vector3(0, -0.03f, 0), new Vector3(8, 1, 8));
        }

        private static void EnsureLineup()
        {
            if (lineup != null) return;
            lineup = new GameObject("Age lineup - same seed, unscaled metre geometry");
            float cursor = 0f;
            foreach (float age in new[] { 0.05f, 0.3f, 0.6f, 0.9f })
            {
                GameObject tree = AddTree(seed, age, Vector3.zero, "Age " + age.ToString("F2", CultureInfo.InvariantCulture));
                Bounds bounds = TreeBounds(tree);
                tree.transform.SetParent(lineup.transform, true);
                tree.transform.position = new Vector3(cursor + bounds.extents.x, 0, 0);
                cursor += bounds.size.x + 2f;
            }
            lineup.transform.position = new Vector3(-cursor * 0.5f + 1f, 0, 0);
            lineupBounds = TreeBounds(lineup);
            BuildMetreRuler(lineupBounds);
            lineup.SetActive(false);
            metreRuler.SetActive(false);
        }

        private static void EnsurePrototype()
        {
            if (companions != null) return;
            BuildPrototypeGround();
            companions = new GameObject("Prototype companion trees");
            float span = Mathf.Max(mainBounds.size.x, 4f);
            Vector3 young = new Vector3(-span * 1.25f, 0, 7); young.y = PrototypeHeight(young.x, young.z);
            Vector3 middle = new Vector3(span * 1.3f, 0, 11); middle.y = PrototypeHeight(middle.x, middle.z);
            if (!importedCandidateMode && !ph02CrownMode && !ph01TangentMode && !ph02FittedMode)
            {
                AddTree(seed + 1, 0.3f, young, "Prototype young tree").transform.SetParent(companions.transform, true);
                AddTree(seed + 2, 0.6f, middle, "Prototype middle-aged tree").transform.SetParent(companions.transform, true);
            }
            companions.SetActive(false);
            BuildCosmicBackdrop();
            backdrop.SetActive(false);
        }

        private static GameObject AddTree(int treeSeed, float age, Vector3 position, string label)
        {
            Debug.Log("KOKERBOOM_CREATE_BEGIN seed=" + treeSeed + " age=" + age.ToString("F2", CultureInfo.InvariantCulture) + " lod=0");
            var generationWatch = Stopwatch.StartNew();
            GameObject tree = KokerboomGeometry.Create(treeSeed, age, 0);
            if (tree == null) throw new InvalidOperationException("KokerboomGeometry.Create returned no object.");
            tree.name = label;
            tree.transform.position = position;
            subjects.Add(new Subject { Root = tree, Seed = treeSeed, Age = age, Lod = 0,
                AssetPath=ph02FamilyMode?PH02CrownCandidate.SourceAssetPath:"",
                SourceMaterialNames=ph02FamilyMode?new[]{"PH02 compound crown/support; PH01 mapped procedural wood"}:Array.Empty<string>() });
            Debug.Log("KOKERBOOM_CREATE_END seed=" + treeSeed + " age=" + age.ToString("F2", CultureInfo.InvariantCulture) + " milliseconds=" + generationWatch.ElapsedMilliseconds);
            return tree;
        }

        private static void SetupPH02FamilySources()
        {
            try
            {
                PH02FamilyComponent.Result component=PH02FamilyComponent.Create(true,out ph02FamilyChecks);
                owned.Add(component.Component);ph02FamilyChecks=component.Metrics;
                ph02FamilyMeshSha256=FamilyComponentMeshHash(component.Component);
                ph02FamilyMeshReadbackPassed=ph02FamilyMeshSha256==ph02FamilyChecks.componentSha256;
                if(!ph02FamilyChecks.actualImportedGatePassed||!ph02FamilyChecks.actualMeshCreated||!ph02FamilyChecks.numericChecksPassed||!ph02FamilyChecks.inputsUnchanged||!ph02FamilyMeshReadbackPassed)
                    throw new InvalidOperationException("PH02 family component failed actual import, buffer or complete Mesh readback checks.");

                Material source=ImportedMaterial("Assets/CityLife/Art/PolyHaven/QuiverTree02/textures/quiver_tree_02");
                Shader shader=Shader.Find("CityLife/PH02FittedSupport");
                if(shader==null||!shader.isSupported)throw new InvalidOperationException("Required PH02 compound shader unavailable.");
                sourceLeafMaterial=new Material(shader){name="PH02 compound crown/support - controlled foliage tint "+ph02FoliageTint.ToString("R",CultureInfo.InvariantCulture),hideFlags=HideFlags.HideAndDontSave};
                owned.Add(sourceLeafMaterial);
                foreach(string property in new[]{"_BaseMap","_BumpMap","_MetallicGlossMap","_BaseColor","_BumpScale","_Smoothness","_Cull","_FoliageTintStrength","_DiagnosticAlbedo"})
                    if(!sourceLeafMaterial.HasProperty(property))throw new InvalidOperationException("Required PH02 family shader property missing: "+property);
                foreach(string property in new[]{"_BaseMap","_BumpMap","_MetallicGlossMap"})
                {
                    sourceLeafMaterial.SetTexture(property,source.GetTexture(property));
                    sourceLeafMaterial.SetTextureScale(property,source.GetTextureScale(property));sourceLeafMaterial.SetTextureOffset(property,source.GetTextureOffset(property));
                }
                sourceLeafMaterial.SetColor("_BaseColor",source.GetColor("_BaseColor"));
                foreach(string property in new[]{"_BumpScale","_Smoothness","_Cull"})sourceLeafMaterial.SetFloat(property,source.GetFloat(property));
                sourceLeafMaterial.SetFloat("_FoliageTintStrength",ph02FoliageTint);
                sourceLeafMaterial.SetFloat("_MatchTreeSkin",1f);
                sourceLeafMaterial.SetFloat("_DiagnosticAlbedo",0f);
                sourceLeafMaterial.EnableKeyword("_NORMALMAP");sourceLeafMaterial.EnableKeyword("_METALLICSPECGLOSSMAP");
                importedBindings.Add(sourceLeafMaterial,importedBindings[source]);

                Material sourceBark=ImportedMaterial("Assets/CityLife/Art/PolyHaven/QuiverTree01/textures/quiver_tree_01_trunk");
                Shader barkShader=Shader.Find("CityLife/KokerboomSurface");
                if(barkShader==null||!barkShader.isSupported)throw new InvalidOperationException("Required mapped procedural wood shader unavailable.");
                var wood=new Material(barkShader){name="PH01 bark atlas interior on procedural PH02 family wood",hideFlags=HideFlags.HideAndDontSave};owned.Add(wood);
                foreach(string property in new[]{"_BaseMap","_BumpMap","_MetallicGlossMap"})wood.SetTexture(property,sourceBark.GetTexture(property));
                wood.SetFloat("_UseSourceBark",1);wood.SetFloat("_Smoothness",.2f);importedBindings.Add(wood,importedBindings[sourceBark]);
                KokerboomGeometry.ConfigureFittedCrownForInspection(component.Component,sourceLeafMaterial,wood,component.BottomRadiusMetres);
            }
            catch(Exception exception){ph02FamilyFailureType=exception.GetType().Name;throw;}
            finally
            {
                File.WriteAllText(Path.Combine(outputDirectory,"ph02-family-component-checks.json"),JsonUtility.ToJson(new PH02FamilyEvidence{
                    component=ph02FamilyChecks,actualMeshReadbackPassed=ph02FamilyMeshReadbackPassed,actualMeshSha256=ph02FamilyMeshSha256,
                    failureType=ph02FamilyFailureType,foliageTintStrength=ph02FoliageTint,geometryVersion=KokerboomGeometry.Version
                },true));
            }
        }
        private static string FamilyComponentMeshHash(Mesh mesh)
        {
            Vector3[] p=mesh.vertices,n=mesh.normals;Vector2[] uv=mesh.uv,uv1=mesh.uv2;Vector4[] t=mesh.tangents;Color[] colors=mesh.colors;int[] indices=mesh.triangles;
            if(p.Length!=n.Length||p.Length!=uv.Length||p.Length!=t.Length||p.Length!=uv1.Length||p.Length!=colors.Length||mesh.subMeshCount!=1)
                throw new InvalidOperationException("PH02 component requires complete PNUT,UV1,colour arrays and one material submesh.");
            using(var bytes=new MemoryStream())using(var writer=new BinaryWriter(bytes))
            {
                writer.Write(p.Length);
                for(int i=0;i<p.Length;i++)
                {
                    writer.Write(p[i].x);writer.Write(p[i].y);writer.Write(p[i].z);writer.Write(n[i].x);writer.Write(n[i].y);writer.Write(n[i].z);
                    writer.Write(uv[i].x);writer.Write(uv[i].y);writer.Write(t[i].x);writer.Write(t[i].y);writer.Write(t[i].z);writer.Write(t[i].w);
                }
                writer.Write(indices.Length);foreach(int i in indices)writer.Write(i);
                writer.Write(uv1.Length);foreach(Vector2 value in uv1){writer.Write(value.x);writer.Write(value.y);}
                writer.Write(colors.Length);foreach(Color value in colors){writer.Write(value.r);writer.Write(value.g);writer.Write(value.b);writer.Write(value.a);}
                writer.Flush();return IslandDefinition.Hash(bytes.ToArray());
            }
        }
        [Serializable] private sealed class PH02FamilyEvidence
        {
            public string schema="starfall.ph02-family-setup.v1",geometryVersion,failureType,actualMeshSha256;
            public string scope="Actual imported crown plus owned-clone fitted support, rigid lower-centre frame and one compound material. Component numeric checks and source hashes do not establish full-family topology, visual or runtime acceptance.";
            public PH02FamilyComponent.Report component;
            public bool actualMeshReadbackPassed;
            public float foliageTintStrength;
        }

        private static void SetupHybridSources()
        {
            sourceRosettes=KokerboomSourceRosettes.CreateRosettes(false);
            sourceBasalMeshes=KokerboomSourceRosettes.CreateBasalMeshes();
            foreach(Mesh mesh in sourceRosettes)owned.Add(mesh);
            foreach(Mesh mesh in sourceBasalMeshes)owned.Add(mesh);
            const string stem="Assets/CityLife/Art/PolyHaven/QuiverTree01/textures/quiver_tree_01";
            sourceLeafMaterial=ImportedMaterial(stem+"_leaf");
            sourceLeafMaterial.name="Hybrid PH01 source leaves - blue-green tint preserving source detail";
            sourceLeafMaterial.SetColor("_BaseColor",new Color(.79f,1f,.96f,1f));
            Material sourceBark=ImportedMaterial(stem+"_trunk");
            var wood=new Material(Shader.Find("CityLife/KokerboomSurface")){name="Hybrid PH01 bark atlas interior mapping",hideFlags=HideFlags.HideAndDontSave};
            owned.Add(wood);
            foreach(string property in new[]{"_BaseMap","_BumpMap","_MetallicGlossMap"})wood.SetTexture(property,sourceBark.GetTexture(property));
            wood.SetFloat("_UseSourceBark",1);wood.SetFloat("_Smoothness",.2f);
            importedBindings.Add(wood,importedBindings[sourceBark]);
            KokerboomGeometry.ConfigureSourceRosettesForInspection(sourceRosettes,sourceLeafMaterial,wood);
            File.WriteAllText(Path.Combine(outputDirectory,"source-rosette-extraction.json"),KokerboomSourceRosettes.DescribeJson());
            File.WriteAllText(Path.Combine(outputDirectory,"ph01-source-identity.json"),KokerboomSourceIdentity.CollectJson());
        }

        private static void SourceFixture(bool basalOnly)
        {
            if(sourceFixture!=null)Object.DestroyImmediate(sourceFixture);
            sourceFixture=new GameObject("Source rosette A with separately visible original basal pieces");
            void Part(string name,Mesh mesh)
            {
                var part=new GameObject(name);part.transform.SetParent(sourceFixture.transform,false);
                part.AddComponent<MeshFilter>().sharedMesh=mesh;part.AddComponent<MeshRenderer>().sharedMaterial=sourceLeafMaterial;
            }
            if(!basalOnly)Part("Original 21 leaves",sourceRosettes[0]);
            Part("Original associated closed basal pieces",sourceBasalMeshes[0]);
            subjects.RemoveAll(s=>s.Kind=="source-rosette-diagnostic");
            subjects.Add(new Subject{Root=sourceFixture,Kind="source-rosette-diagnostic",Age=-1,AssetPath=KokerboomSourceRosettes.SourceAssetPath});
            Neutral();mainTree.SetActive(false);neutralFloor.SetActive(false);sourceFixture.SetActive(true);
        }

        private static void LeafAlbedoDiagnostic()
        {
            albedoOriginals.Clear();
            Material albedo;
            if(ph02FamilyMode)
            {
                albedo=new Material(sourceLeafMaterial){name="Diagnostic PH02 compound albedo - source/support blend retained",hideFlags=HideFlags.HideAndDontSave};owned.Add(albedo);
                if(!albedo.HasProperty("_DiagnosticAlbedo"))throw new InvalidOperationException("Compound shader lacks required albedo diagnostic.");
                albedo.SetFloat("_DiagnosticAlbedo",1f);
                importedBindings.Add(albedo,importedBindings[sourceLeafMaterial]);
            }
            else
            {
                albedo=UnlitMaterial("Diagnostic only original leaf albedo",Color.white);
                albedo.SetTexture("_BaseMap",sourceLeafMaterial.GetTexture("_BaseMap"));
                albedo.SetColor("_BaseColor",sourceLeafMaterial.GetColor("_BaseColor"));
            }
            foreach(Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if(renderer.sharedMaterials.Contains(sourceLeafMaterial))
                { albedoOriginals.Add(renderer,renderer.sharedMaterials);renderer.sharedMaterial=albedo; }
        }

        private static void RestoreLeafAlbedo()
        { foreach(var pair in albedoOriginals)if(pair.Key!=null)pair.Key.sharedMaterials=pair.Value;albedoOriginals.Clear(); }

        private static void StrongCoolFill()
        {
            fill.transform.rotation=Quaternion.Euler(-25,100,0);fill.color=new Color(.43f,.73f,1f);fill.intensity=.95f;
            rim.transform.rotation=Quaternion.Euler(-12,170,0);rim.intensity=.85f;
            RenderSettings.ambientMode=AmbientMode.Custom;
            var probe=new SphericalHarmonicsL2();probe.Clear();
            probe.AddAmbientLight(new Color(.16f,.23f,.30f));
            probe.AddDirectionalLight(Vector3.up,new Color(.25f,.40f,.60f),.65f);
            probe.AddDirectionalLight(Vector3.down,new Color(.17f,.28f,.32f),.35f);
            RenderSettings.ambientProbe=probe;
        }

        private static GameObject AddImportedTree(int candidate)
        {
            string id = "quiver_tree_0" + candidate;
            string folder = "Assets/CityLife/Art/PolyHaven/QuiverTree0" + candidate;
            string path = folder + "/" + id + "_2k.fbx";
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) throw new InvalidOperationException("Imported comparison FBX is unavailable: " + path);
            GameObject root = Object.Instantiate(asset);
            root.name = "Poly Haven " + id + " - original imported scale";
            root.transform.position = Vector3.zero;
            var sourceNames = new List<string>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    string original = materials[i] == null ? "missing material slot" : materials[i].name;
                    sourceNames.Add(original);
                    string textureStem = id;
                    if (candidate == 1) textureStem += original.IndexOf("leaf", StringComparison.OrdinalIgnoreCase) >= 0 || i == 1 ? "_leaf" : "_trunk";
                    string key = folder + "/textures/" + textureStem;
                    if (!importedMaterials.TryGetValue(key, out Material mapped))
                    {
                        mapped = ImportedMaterial(key);
                        importedMaterials.Add(key, mapped);
                    }
                    materials[i] = mapped;
                }
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            // Translate the imported lower bound onto the stage. Never resize or replace geometry.
            Bounds bounds = TreeBounds(root);
            root.transform.position -= Vector3.up * bounds.min.y;
            subjects.Add(new Subject { Root = root, Seed = 0, Age = -1f, Lod = 0, Kind = "imported-unreviewed-original", AssetPath = path, SourceMaterialNames = sourceNames.ToArray() });
            importedTrees.Add(candidate, root);
            Debug.Log("KOKERBOOM_IMPORTED " + path + " measuredHeightMetres=" + TreeBounds(root).size.y.ToString("F4", CultureInfo.InvariantCulture));
            return root;
        }

        private static Material ImportedMaterial(string stem)
        {
            Texture2D Find(string suffix)
            {
                foreach (string extension in new[] { ".png", ".jpg", ".exr" })
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(stem + suffix + "_2k" + extension);
                    if (texture != null) return texture;
                }
                throw new InvalidOperationException("Source comparison map is missing: " + stem + suffix);
            }
            var diffuse = Find("_diff"); var normal = Find("_nor_gl"); var rough = Find("_rough");
            Material material = LitMaterial(Path.GetFileName(stem) + " explicit original URP maps", Color.white, 1f);
            material.SetTexture("_BaseMap", diffuse);
            material.SetFloat("_Cull", 2f);
            var normalImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal)) as TextureImporter;
            if (normalImporter != null && normalImporter.textureType == TextureImporterType.NormalMap)
                material.SetTexture("_BumpMap", normal);
            else
            {
                Color[] pixels = ReadRawMapPixels(normal);
                // RG + alpha1 is supported by URP's normal unpacking. Preserve OpenGL +Y data.
                for (int i = 0; i < pixels.Length; i++) pixels[i].a = 1f;
                material.SetTexture("_BumpMap", DataTexture(normal.width, normal.height, pixels, normal.name + " - temporary linear RG normal"));
            }
            material.EnableKeyword("_NORMALMAP"); material.SetFloat("_BumpScale", 1f);
            Color[] roughness = ReadRawMapPixels(rough);
            for (int i = 0; i < roughness.Length; i++) roughness[i] = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(roughness[i].r));
            material.SetTexture("_MetallicGlossMap", DataTexture(rough.width, rough.height, roughness, rough.name + " - temporary metallic0 smoothness1-roughness"));
            material.EnableKeyword("_METALLICSPECGLOSSMAP"); material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.DisableKeyword("_ALPHATEST_ON"); material.SetFloat("_AlphaClip", 0f);
            importedBindings.Add(material, new[]
            {
                new TextureBinding { property = "_BaseMap", sourceAsset = AssetDatabase.GetAssetPath(diffuse), runtimeTexture = diffuse.name, conversion = "Original diffuse; white tint; source alpha ignored because opaque geometry is under inspection" },
                new TextureBinding { property = "_BumpMap", sourceAsset = AssetDatabase.GetAssetPath(normal), runtimeTexture = material.GetTexture("_BumpMap").name, conversion = "OpenGL +Y normal; existing normal import or temporary linear RG data; no source/importer mutation" },
                new TextureBinding { property = "_MetallicGlossMap", sourceAsset = AssetDatabase.GetAssetPath(rough), runtimeTexture = material.GetTexture("_MetallicGlossMap").name, conversion = "Temporary linear RGBA: metallicRGB0, alpha1-minus-source-roughness" }
            });
            return material;
        }

        private static Color[] ReadRawMapPixels(Texture2D source)
        {
            bool srgb = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat);
            RenderTexture previous = RenderTexture.active;
            RenderTexture readback = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(source, readback);
                RenderTexture.active = readback;
                texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false); texture.Apply(false, false);
                Color[] pixels = texture.GetPixels();
                // Default texture import may mark data PNGs as sRGB. Undo that sampling
                // conversion in memory; no source bytes or TextureImporter settings change.
                if (srgb) for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
                return pixels;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(readback); Object.DestroyImmediate(texture); }
        }

        private static Texture2D DataTexture(int width, int height, Color[] pixels, string name)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, true)
            { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixels(pixels); texture.Apply(true, true); owned.Add(texture); return texture;
        }

        private static void RosetteFixture()
        {
            if (rosetteFixture == null)
            {
                Debug.Log("KOKERBOOM_ROSETTE_CREATE_BEGIN seed=" + seed + " lod=0");
                var watch = Stopwatch.StartNew();
                rosetteFixture = KokerboomGeometry.CreateRosette(seed, 0);
                if (rosetteFixture == null) throw new InvalidOperationException("CreateRosette returned no object.");
                rosetteFixture.name = "Isolated actual terminal rosette - source leaf geometry and material";
                subjects.Add(new Subject { Root = rosetteFixture, Seed = seed, Age = 1f, Lod = 0, Kind = "isolated-terminal-rosette" });
                Debug.Log("KOKERBOOM_ROSETTE_CREATE_END milliseconds=" + watch.ElapsedMilliseconds);
            }
            Neutral(); mainTree.SetActive(false); neutralFloor.SetActive(false); rosetteFixture.SetActive(true);
        }

        private static void JuvenileFixture()
        {
            if (juvenileFixture == null) juvenileFixture = AddTree(seed, .05f, Vector3.zero, "Juvenile age .05 - unscaled closeup");
            Neutral(); mainTree.SetActive(false); juvenileFixture.SetActive(true);
        }

        private static void AdultVariationFixture()
        {
            if (adultVariants == null)
            {
                adultVariants = new GameObject("Same-age .90 adult seed variation");
                float cursor = 0f;
                for (int i = 0; i < 3; i++)
                {
                    GameObject tree = AddTree(seed + i, .9f, Vector3.zero, "Adult .90 seed " + (seed + i));
                    Bounds bounds = TreeBounds(tree);
                    tree.transform.SetParent(adultVariants.transform, true);
                    tree.transform.position = new Vector3(cursor + bounds.extents.x, 0, 0);
                    cursor += bounds.size.x + 2f;
                }
                adultVariants.transform.position = new Vector3(-cursor * .5f + 1f, 0, 0);
            }
            Neutral(); mainTree.SetActive(false); adultVariants.SetActive(true);
        }

        private static Light MakeLight(string name, Vector3 position, Vector3 rotation)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.SetPositionAndRotation(position, Quaternion.Euler(rotation));
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            return light;
        }

        private static Material LitMaterial(string name, Color color, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader unavailable.");
            var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_BaseColor", color.linear);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            owned.Add(material);
            return material;
        }

        private static Material UnlitMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP Unlit shader unavailable.");
            var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_BaseColor", color.linear);
            owned.Add(material);
            return material;
        }

        private static GameObject Primitive(PrimitiveType type, string name, Material material, Vector3 position, Vector3 scale, Transform parent = null)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static Bounds TreeBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Tree contains no renderers.");
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static Transform RequiredAnchor(string path)
        {
            Transform anchor = mainTree.transform.Find(path);
            if (anchor == null) throw new InvalidOperationException("The model is missing inspection anchor " + path + ".");
            return anchor;
        }

        private static List<Shot> BuildShots()
        {
            int portraitHeight = width * 3 / 4;
            int gameplayHeight = width * 9 / 16;
            var shots = new List<Shot>
            {
                new Shot { Id = "01-neutral-front", Purpose = "Neutral front: silhouette, age form, colour separation and connected crown.", Height = portraitHeight, Configure = () => { Neutral(); Frame(mainBounds, new Vector3(0, 0.09f, -1), 1.18f, portraitHeight); } },
                new Shot { Id = "02-neutral-three-quarter", Purpose = "Neutral three-quarter: branch depth and crown volume.", Height = portraitHeight, Configure = () => { Neutral(); Frame(mainBounds, new Vector3(1, 0.18f, -1), 1.18f, portraitHeight); } },
                new Shot { Id = "03-neutral-rear", Purpose = "Neutral rear: concealed intersections and rosette distribution.", Height = portraitHeight, Configure = () => { Neutral(); Frame(mainBounds, new Vector3(0, 0.09f, 1), 1.18f, portraitHeight); } },
                new Shot { Id = "04-branch-union-closeup", Purpose = "Actual branch-union anchor: continuity, pinching, seams and bark-to-upper-branch treatment.", Height = portraitHeight, Configure = () => { Neutral(); Closeup("Inspection/BranchUnion", mainBounds.size.y * 0.27f, new Vector3(0.65f, 0.16f, -1), portraitHeight); } },
                new Shot { Id = "05-trunk-bark-closeup", Purpose = "Actual trunk-bark anchor: surface structure without lighting camouflage.", Height = portraitHeight, Configure = () => { Neutral(); Closeup("Inspection/TrunkBark", mainBounds.size.y * 0.21f, new Vector3(0.25f, 0.08f, -1), portraitHeight); } },
                new Shot { Id = "06-terminal-rosette-closeup", Purpose = "Actual terminal rosette: succulent leaf thickness, taper, curvature and attachment.", Height = portraitHeight, Configure = () => { Neutral(); var d = KokerboomGeometry.Describe(seed, 1f); Closeup("Inspection/TerminalRosette", Mathf.Max(0.5f, d.leafLengthMax * 2.7f), new Vector3(0.55f, 0.48f, -1), portraitHeight); } },
                new Shot { Id = "07-age-lineup-metres", Purpose = "Same seed at age 0.05 / 0.30 / 0.60 / 0.90, left to right. Ruler ticks and numeral labels are spaced exactly one Unity metre apart. Trees are not rescaled.", Height = portraitHeight, Configure = () => { EnsureLineup(); Neutral(); mainTree.SetActive(false); lineup.SetActive(true); metreRuler.SetActive(true); Bounds b = lineupBounds; b.Encapsulate(metreRuler.GetComponentsInChildren<Renderer>().Aggregate(new Bounds(new Vector3(lineupBounds.min.x - 1.3f, 0, 0), Vector3.zero), (a, r) => { a.Encapsulate(r.bounds); return a; })); Frame(b, new Vector3(0, 0.045f, -1), 1.14f, portraitHeight); } },
                new Shot { Id = "08-cosmic-gameplay-eye-level", Purpose = "Small sculpted 60m prototype ground, actual 1.85m eye clearance, rocks and several grounded trees. Warm key/cool fill and one blue gas giant; no Earth, water or reference-image billboards.", Height = gameplayHeight, Configure = () => { Cosmic(); Vector3 eye = new Vector3(mainBounds.size.x * 0.75f, 0, -mainBounds.size.y * 2.2f); eye.y = PrototypeHeight(eye.x, eye.z) + 1.85f; Perspective(eye, new Vector3(0, mainBounds.size.y * 0.43f, 1), gameplayHeight); } },
                new Shot { Id = "09-cosmic-gameplay-overlook", Purpose = "Second real prototype-ground view: tree spacing, ground contact and intended gold/teal/cobalt palette. Scene stage only; no new world terrain.", Height = gameplayHeight, Configure = () => { Cosmic(); Perspective(new Vector3(-mainBounds.size.x * 1.8f, mainBounds.size.y * 0.85f, -mainBounds.size.y * 1.8f), new Vector3(0, mainBounds.size.y * 0.35f, 2), gameplayHeight); } },
                new Shot { Id = "10-neutral-front-no-shadows", Purpose = "Diagnostic only. Same tree, camera, materials and lighting as 01; key-light shadows disabled to separate shadow artefacts from mesh/normal defects. Does not replace the required normal-lighting captures.", Height = portraitHeight, Configure = () => { Neutral(); key.shadows = LightShadows.None; Frame(mainBounds, new Vector3(0, 0.09f, -1), 1.18f, portraitHeight); } },
                new Shot { Id = "11-isolated-rosette-above", Purpose = "Actual CreateRosette source geometry and material, isolated above view. No stand-in leaves, surrounding crown or decorative stump; inspect phyllotaxis, taper and leaflet overlap.", Height = portraitHeight, Configure = () => { RosetteFixture(); Frame(TreeBounds(rosetteFixture), new Vector3(.18f, 1f, -.35f), 1.18f, portraitHeight); } },
                new Shot { Id = "12-isolated-rosette-side", Purpose = "Same actual isolated rosette in side view; inspect thickness, upward inner growth, drooping outer leaves and attachment volume.", Height = portraitHeight, Configure = () => { RosetteFixture(); Frame(TreeBounds(rosetteFixture), new Vector3(0, .06f, -1f), 1.18f, portraitHeight); } },
                new Shot { Id = "13-juvenile-closeup", Purpose = "Seed4242 (or selected seed), age .05, actual juvenile geometry closeup. Verify unbranched form and persistent vertical leaf rows at true model proportions.", Height = portraitHeight, Configure = () => { JuvenileFixture(); Frame(TreeBounds(juvenileFixture), new Vector3(.3f, .08f, -1f), 1.16f, portraitHeight); } },
                new Shot { Id = "14-adult-seed-variation", Purpose = "Same adult age .90, selected seed / seed+1 / seed+2 from left to right; unscaled geometry and identical light/camera. Inspect within-age identity and actual variation.", Height = portraitHeight, Configure = () => { AdultVariationFixture(); Frame(TreeBounds(adultVariants), new Vector3(0, .04f, -1), 1.14f, portraitHeight); } },
                new Shot { Id = "15-untextured-branch-union-closeup", Purpose = "Diagnostic only: exact04 camera/geometry with mid-gray URP/Lit wood. Separates branch mesh/normal defects from the procedural bark surface. Original material restored afterward.", Height = portraitHeight, Configure = () => { Neutral(); diagnosticRenderer = mainTree.transform.Find("Bark").GetComponent<Renderer>(); diagnosticOriginalMaterials = diagnosticRenderer.sharedMaterials; diagnosticRenderer.sharedMaterial = LitMaterial("Diagnostic mid-gray wood", new Color(.5f,.5f,.5f), .25f); Closeup("Inspection/BranchUnion", mainBounds.size.y * .27f, new Vector3(.65f, .16f, -1), portraitHeight); }, Restore = () => { diagnosticRenderer.sharedMaterials = diagnosticOriginalMaterials; } }
            };
            if(hybridMode)
            {
                void Eye(){Cosmic();Vector3 eye=new Vector3(mainBounds.size.x*.75f,0,-mainBounds.size.y*2.2f);eye.y=PrototypeHeight(eye.x,eye.z)+1.85f;Perspective(eye,new Vector3(0,mainBounds.size.y*.43f,1),gameplayHeight);}
                shots.Add(new Shot{Id="16-cosmic-leaf-albedo-only",Purpose="Diagnostic exact08 camera and geometry. Only leaf materials replaced with unlit original albedo and the same tint; coverage versus lighting, not an accepted lighting treatment.",Height=gameplayHeight,Configure=()=>{Eye();LeafAlbedoDiagnostic();},Restore=RestoreLeafAlbedo});
                shots.Add(new Shot{Id="17-cosmic-strong-cool-fill",Purpose="Diagnostic exact08 camera/materials/geometry. Stronger cool lower fill and explicit custom SH; no leaf emission. Lighting comparison only.",Height=gameplayHeight,Configure=()=>{Eye();StrongCoolFill();}});
                if(ph02FamilyMode)
                {
                    shots[15].Purpose="Diagnostic exact08 camera and geometry. Clone of the PH02 compound shader with _DiagnosticAlbedo1 preserves the source/support blend and geometric foliage weights; no generic unlit-atlas replacement.";
                    shots.Add(new Shot{Id="18-ph02-lower-crown-wood-join-side",Purpose="Actual lower crown/support-to-procedural-wood anchor, side closeup. Inspect measured overlap, radius agreement, material seam and visibility of an open rim; no weld assumed.",Height=portraitHeight,Configure=()=>{Neutral();Closeup("Inspection/LowerCrownJoin",Mathf.Max(.65f,ph02FamilyChecks.bottomRadiusMetres*8),new Vector3(.65f,.08f,-1),portraitHeight);}});
                    shots.Add(new Shot{Id="19-ph02-lower-crown-wood-join-underside",Purpose="Same actual lower crown-to-wood join from underneath; expose gaps, intersections or remaining collar without hiding geometry.",Height=portraitHeight,Configure=()=>{Neutral();Closeup("Inspection/LowerCrownJoin",Mathf.Max(.65f,ph02FamilyChecks.bottomRadiusMetres*8),new Vector3(.2f,-1,-.35f),portraitHeight);}});
                    shots.Add(new Shot{Id="20-ph02-juvenile-base",Purpose="Actual age .05 juvenile bark/base at native generated scale. Inspect root contact, basal shoulder and transition into the supported crown.",Height=portraitHeight,Configure=()=>{JuvenileFixture();Bounds b=TreeBounds(juvenileFixture.transform.Find("Bark").gameObject);b.Expand(.15f);Frame(b,new Vector3(.6f,.12f,-1),1.15f,portraitHeight);}});
                    shots.Add(new Shot{Id="21-ph02-mature-base-second-angle",Purpose="Second close angle of the actual mature bark/base. Inspect basal roots, shoulder and surface continuity; no extra specimen or decorative cover geometry.",Height=portraitHeight,Configure=()=>{Neutral();Bounds b=TreeBounds(mainTree.transform.Find("Bark").gameObject);float span=Mathf.Max(.85f,mainBounds.size.y*.20f);Vector3 centre=mainTree.transform.position;centre.y=b.min.y+span*.30f;Frame(new Bounds(centre,Vector3.one*span),new Vector3(-.8f,.12f,-1),1.15f,portraitHeight);}});
                    return shots;
                }
                shots.Add(new Shot{Id="18-source-rosette-with-basal-front",Purpose="PH01 source groupA at original metre scale with original separate closed basal pieces. No generated socket, identity of basal pieces remains under review.",Height=portraitHeight,Configure=()=>{SourceFixture(false);Frame(TreeBounds(sourceFixture),new Vector3(.3f,.4f,-1),1.18f,portraitHeight);}});
                shots.Add(new Shot{Id="19-source-rosette-with-basal-side",Purpose="Same original source groupA and separate basal pieces, side view for curvature and attachment.",Height=portraitHeight,Configure=()=>{SourceFixture(false);Frame(TreeBounds(sourceFixture),new Vector3(1,.02f,0),1.18f,portraitHeight);}});
                shots.Add(new Shot{Id="20-source-rosette-with-basal-underside",Purpose="Original source groupA underside; exposes retained open leaf bases and basal components without a new cap or socket.",Height=portraitHeight,Configure=()=>{SourceFixture(false);Frame(TreeBounds(sourceFixture),new Vector3(.2f,-1,-.3f),1.18f,portraitHeight);}});
                shots.Add(new Shot{Id="21-source-basal-pieces-only",Purpose="The two closed source leaf-material components associated with groupA, isolated unchanged for identity/suitability review. Not included in this round's hybrid trees.",Height=portraitHeight,Configure=()=>{SourceFixture(true);Frame(TreeBounds(sourceFixture),new Vector3(.6f,.2f,-1),1.18f,portraitHeight);}});
            }
            return shots;
        }

        private static void PreviewEye()
        {
            Cosmic();StrongCoolFill();
            Vector3 eye=new Vector3(mainBounds.size.x*.75f,0,-mainBounds.size.y*2.2f);eye.y=PrototypeHeight(eye.x,eye.z)+1.85f;
            Perspective(eye,new Vector3(0,mainBounds.size.y*.43f,1),width*9/16);
        }
        private static List<Shot> BuildPreviewShots()=>new List<Shot>{
            new Shot{Id="01-preview-eye-level",Purpose="Current hybrid WIP stage with stronger cool fill, before standalone bake. Shader/frame verification; not family acceptance.",Height=width*9/16,Configure=PreviewEye},
            new Shot{Id="02-preview-overlook",Purpose="Current WIP stage overview before standalone bake. No larger world, galaxy or living sea yet.",Height=width*9/16,Configure=()=>{Cosmic();StrongCoolFill();Perspective(new Vector3(-mainBounds.size.x*1.8f,mainBounds.size.y*.85f,-mainBounds.size.y*1.8f),new Vector3(0,mainBounds.size.y*.35f,2),width*9/16);}}
        };

        private static GameObject SetupPH01Tangents()
        {
            try
            {
                tangentCandidates=PH01ImportedTupleCandidate.CreateRosettes(ph01ExactTupleDedup,out importedTupleChecks,out tangentOriginals);
                foreach(Mesh mesh in tangentCandidates)owned.Add(mesh);
                foreach(Mesh mesh in tangentOriginals)if(mesh!=null)owned.Add(mesh);
                if(!importedTupleChecks.numericChecksPassed)throw new InvalidOperationException("Imported tuple candidate failed numeric attribute/topology checks.");
            }
            catch(Exception exception){importedTupleFailureType=exception.GetType().Name;throw;}
            finally
            {
                File.WriteAllText(Path.Combine(outputDirectory,"ph01-imported-tuple-checks.json"),importedTupleChecks==null
                    ? "{\"status\":\"Adapter failed before report creation; no successful numeric checks claimed\"}"
                    : JsonUtility.ToJson(importedTupleChecks,true));
            }
            tangentBaseline=KokerboomSourceRosettes.CreateRosettes(false);
            foreach(Mesh mesh in tangentBaseline)owned.Add(mesh);
            var source=KokerboomSourceRosettes.Describe();
            tangentRoots=source.groups.Select(g=>g.sourceRootUnity).ToArray();
            tangentFrameBounds=tangentBaseline.Select((mesh,i)=>new Bounds(mesh.bounds.center+tangentRoots[i],mesh.bounds.size)).ToArray();
            ph01OriginalSubsetsReady=tangentOriginals[0]!=null&&tangentOriginals[3]!=null;
            tangentNormalOn=ImportedMaterial("Assets/CityLife/Art/PolyHaven/QuiverTree01/textures/quiver_tree_01_leaf");
            tangentNormalOn.name="PH01 tangent trial - original white-tinted maps - normal ON";
            tangentNormalOff=new Material(tangentNormalOn){name="PH01 tangent trial - same maps - normal OFF",hideFlags=HideFlags.HideAndDontSave};
            tangentNormalOff.DisableKeyword("_NORMALMAP");tangentNormalOff.SetFloat("_BumpScale",0f);owned.Add(tangentNormalOff);
            importedBindings.Add(tangentNormalOff,importedBindings[tangentNormalOn]);
            tangentFixture=PH02MeshObject("PH01 tangent trial A - current extractor",tangentBaseline[0],tangentNormalOn);
            tangentFixture.transform.position=tangentRoots[0];
            subjects.Add(new Subject{Root=tangentFixture,Kind="ph01-tangent-comparison",Age=-1,Seed=0,AssetPath=KokerboomSourceRosettes.SourceAssetPath,SourceMaterialNames=new[]{KokerboomSourceRosettes.SourceMaterialName}});
            return tangentFixture;
        }

        private static List<Shot> BuildPH01TangentShots()
        {
            var shots=new List<Shot>();int portrait=width*3/4;
            string[] variants=ph01OriginalSubsetsReady?new[]{"current","imported-tuples","original-subset"}:new[]{"current","imported-tuples"};
            foreach(int group in new[]{0,3})foreach(string variant in variants)foreach(bool normalMap in new[]{true,false})
            {
                int selectedGroup=group;string selectedVariant=variant;bool selectedNormal=normalMap;
                string letter=group==0?"A":"D",mapLabel=normalMap?"normal-on":"normal-off";
                shots.Add(new Shot{
                    Id=(shots.Count+1).ToString("D2",CultureInfo.InvariantCulture)+"-ph01-"+letter.ToLowerInvariant()+"-"+variant+"-"+mapLabel,
                    Purpose="Controlled PH01 rosette "+letter+", "+variant+", source normal map "+(normalMap?"on":"off")+". Same original-scale native root, neutral illumination and fixed group camera. Imported-tuples candidate retains extractor triangulation; original-subset, when available, retains actual imported triangles. No normal/UV flip, family or runtime integration claim.",
                    Height=portrait,ComparisonGroup=letter,ComparisonVariant=variant,ComparisonNormalMap=normalMap,
                    Configure=()=>{
                        Mesh mesh=selectedVariant=="current"?tangentBaseline[selectedGroup]:selectedVariant=="imported-tuples"?tangentCandidates[selectedGroup]:tangentOriginals[selectedGroup];
                        tangentFixture.GetComponent<MeshFilter>().sharedMesh=mesh;
                        tangentFixture.GetComponent<Renderer>().sharedMaterial=selectedNormal?tangentNormalOn:tangentNormalOff;
                        tangentFixture.name="PH01 tangent trial "+(selectedGroup==0?"A":"D")+" - "+selectedVariant+" - "+(selectedNormal?"normal ON":"normal OFF");
                        tangentFixture.transform.position=tangentRoots[selectedGroup];mainTree=tangentFixture;Neutral();neutralFloor.SetActive(false);
                        Vector3 direction=selectedGroup==0?new Vector3(.65f,.30f,-1):new Vector3(1,.12f,-.15f);
                        Frame(tangentFrameBounds[selectedGroup],direction,1.18f,portrait);
                    }
                });
            }
            return shots;
        }

        private static GameObject SetupPH02Fitted()
        {
            try
            {
                PH02FittedSupportCandidate.Result result;
                if(ph02ImportedTuplesRequested)
                {
                    Mesh importedCrown=null;
                    try
                    {
                        importedCrown=PH02ImportedCrownCandidate.Create(.65f,out ph02ImportedCrownChecks,true);
                        ph02ImportedMeshExpandedSha256=ExpandedMeshTupleHash(importedCrown);
                        ph02ImportedCrownMeshReadbackPassed=ph02ImportedMeshExpandedSha256==ph02ImportedCrownChecks.expandedAfterDedupSha256;
                        if(!ph02ImportedCrownChecks.actualUnityImportedData||!ph02ImportedCrownChecks.mappingComplete||!ph02ImportedCrownChecks.meshCreated
                            ||!ph02ImportedCrownChecks.exactTupleDedup||!ph02ImportedCrownChecks.exactExpandedTuplePreservation||!ph02ImportedCrownMeshReadbackPassed
                            ||ph02ImportedCrownChecks.unmatchedSourceTriangles!=0||ph02ImportedCrownChecks.ambiguousSourceTriangles!=0
                            ||ph02ImportedCrownChecks.matchedSourceTriangles!=82074||ph02ImportedCrownChecks.outputTriangles!=71207
                            ||ph02ImportedCrownChecks.boundaryEdges!=69||ph02ImportedCrownChecks.boundaryComponents!=1||!ph02ImportedCrownChecks.boundaryAllDegreeTwo)
                            throw new InvalidOperationException("Actual imported PH02 tuple mapping, expanded Mesh readback or boundary checks failed.");
                        result=PH02FittedSupportCandidate.Create(importedCrown,.65f);
                        if(!result.Metrics.callerCloneUsed||!result.Metrics.callerCrownUnchanged||!result.Metrics.cloneMatchesCaller)
                        {result.Dispose();throw new InvalidOperationException("Fitted support did not preserve its caller-owned imported crown and owned clone.");}
                    }
                    finally
                    {
                        if(importedCrown!=null)Object.DestroyImmediate(importedCrown);
                        File.WriteAllText(Path.Combine(outputDirectory,"ph02-imported-crown-checks.json"),ph02ImportedCrownChecks==null
                            ?"{\"status\":\"Adapter failed before report creation; no successful mapping claimed\"}":JsonUtility.ToJson(ph02ImportedCrownChecks,true));
                    }
                }
                else result=PH02FittedSupportCandidate.Create();
                owned.Add(result.Crown);owned.Add(result.Support);ph02FitMetrics=result.Metrics;
                ph02FitReadback=VerifyPH02FittedReadback(result.Crown,result.Support,ph02FitMetrics.cutHeightMetres);
                if(!ph02FitMetrics.numericChecksPassed || !ph02FitMetrics.crownUnchanged || !ph02FitMetrics.allCutEdgesReversed || !ph02FitReadback.passed)
                    throw new InvalidOperationException("PH02 fitted support failed Create metrics or actual shared-rim attribute readback.");
                if(ph02ImportedTuplesRequested)LoadPH02ReferenceCameras();

                ph02FitSourceMaterial=ImportedMaterial("Assets/CityLife/Art/PolyHaven/QuiverTree02/textures/quiver_tree_02");
                Shader shader=Shader.Find("CityLife/PH02FittedSupport");
                if(shader==null || !shader.isSupported || shader.name=="Hidden/InternalErrorShader")
                    throw new InvalidOperationException("Required CityLife/PH02FittedSupport shader is absent or unsupported.");
                ph02FitSupportMaterial=new Material(shader){name="PH02 source-rim to pale support - unaccepted material blend",hideFlags=HideFlags.HideAndDontSave};
                owned.Add(ph02FitSupportMaterial);
                foreach(string property in new[]{"_BaseMap","_BumpMap","_MetallicGlossMap","_BaseColor","_BumpScale","_Smoothness","_Cull"})
                    if(!ph02FitSupportMaterial.HasProperty(property))throw new InvalidOperationException("Required fitted-support shader property is absent: "+property);
                foreach(string property in new[]{"_BaseMap","_BumpMap","_MetallicGlossMap"})
                {
                    ph02FitSupportMaterial.SetTexture(property,ph02FitSourceMaterial.GetTexture(property));
                    ph02FitSupportMaterial.SetTextureScale(property,ph02FitSourceMaterial.GetTextureScale(property));
                    ph02FitSupportMaterial.SetTextureOffset(property,ph02FitSourceMaterial.GetTextureOffset(property));
                }
                ph02FitSupportMaterial.SetColor("_BaseColor",ph02FitSourceMaterial.GetColor("_BaseColor"));
                foreach(string property in new[]{"_BumpScale","_Smoothness","_Cull"})ph02FitSupportMaterial.SetFloat(property,ph02FitSourceMaterial.GetFloat(property));
                ph02FitSupportMaterial.EnableKeyword("_NORMALMAP");ph02FitSupportMaterial.EnableKeyword("_METALLICSPECGLOSSMAP");
                importedBindings.Add(ph02FitSupportMaterial,importedBindings[ph02FitSourceMaterial]);
                ph02FitGrayMaterial=LitMaterial("PH02 shared-rim geometry diagnostic - mid-gray",new Color(.5f,.5f,.5f),.22f);

                ph02FitCandidate=new GameObject("PH02 crown with fitted irregular-rim support - unaccepted");
                PH02MeshObject("Unchanged native-scale source crown",result.Crown,ph02FitSourceMaterial).transform.SetParent(ph02FitCandidate.transform,false);
                PH02MeshObject("Separate fitted open support - exact source rim",result.Support,ph02FitSupportMaterial).transform.SetParent(ph02FitCandidate.transform,false);
                Mesh original=PH02CrownCandidate.CreateFullOriginalForComparison();owned.Add(original);
                ph02FitOriginal=PH02MeshObject("PH02 full source - original native coordinates",original,ph02FitSourceMaterial);
                ph02FitCurrent=new GameObject("PH02 crown with prior R08 simple support in native source frame");
                PH02MeshObject("Same unchanged native-scale source crown",result.Crown,ph02FitSourceMaterial).transform.SetParent(ph02FitCurrent.transform,false);
                Mesh oldSupport=PH02SupportMesh();owned.Add(oldSupport);
                GameObject oldBranch=PH02MeshObject("Prior R08 open support - retained 4cm overlap",oldSupport,ph02FitGrayMaterial);
                oldBranch.transform.SetParent(ph02FitCurrent.transform,false);
                oldBranch.transform.localPosition=ph02FitMetrics.rimCentre-new Vector3(0,.90f,0);
                foreach(var item in new[]{(Root:ph02FitOriginal,Kind:"ph02-fitted-source-original"),(Root:ph02FitCurrent,Kind:"ph02-fitted-prior-support"),(Root:ph02FitCandidate,Kind:"ph02-fitted-candidate")})
                    subjects.Add(new Subject{Root=item.Root,Kind=item.Kind,Age=-1,Seed=0,AssetPath=PH02CrownCandidate.SourceAssetPath,SourceMaterialNames=new[]{"quiver_tree_02"}});
                ph02FitBounds=TreeBounds(ph02FitCandidate);
                ph02FitOriginal.SetActive(false);ph02FitCurrent.SetActive(false);
                return ph02FitCandidate;
            }
            catch(Exception exception){ph02FitFailureType=exception.GetType().Name;throw;}
            finally
            {
                File.WriteAllText(Path.Combine(outputDirectory,"ph02-fitted-support-checks.json"),JsonUtility.ToJson(new PH02FitEvidence{
                    create=ph02FitMetrics,actualMeshReadback=ph02FitReadback,failureType=ph02FitFailureType,
                    importedTuplesRequested=ph02ImportedTuplesRequested,importedCrown=ph02ImportedCrownChecks,importedCrownMeshReadbackPassed=ph02ImportedCrownMeshReadbackPassed,
                    importedMeshExpandedSha256=ph02ImportedMeshExpandedSha256,referenceCameraManifest=ph02ImportedTuplesRequested?PH02ReferenceCameraManifest:"",referenceCameraManifestSha256=ph02ReferenceCameraManifestSha256,
                    supportShader="CityLife/PH02FittedSupport",currentSupportTranslation=ph02FitMetrics==null?Vector3.zero:ph02FitMetrics.rimCentre-new Vector3(0,.90f,0)
                },true));
            }
        }

        private static string ExpandedMeshTupleHash(Mesh mesh)
        {
            Vector3[] p=mesh.vertices,n=mesh.normals;Vector2[] uv=mesh.uv;Vector4[] t=mesh.tangents;int[] indices=mesh.triangles;
            if(p.Length!=n.Length||p.Length!=uv.Length||p.Length!=t.Length)throw new InvalidOperationException("Imported crown Mesh lacks complete tuple arrays.");
            using(var bytes=new MemoryStream())using(var writer=new BinaryWriter(bytes))
            {
                foreach(int i in indices)
                {
                    writer.Write(p[i].x);writer.Write(p[i].y);writer.Write(p[i].z);writer.Write(n[i].x);writer.Write(n[i].y);writer.Write(n[i].z);
                    writer.Write(uv[i].x);writer.Write(uv[i].y);writer.Write(t[i].x);writer.Write(t[i].y);writer.Write(t[i].z);writer.Write(t[i].w);
                }
                writer.Flush();return IslandDefinition.Hash(bytes.ToArray());
            }
        }
        private static void LoadPH02ReferenceCameras()
        {
            byte[] bytes=File.ReadAllBytes(PH02ReferenceCameraManifest);ph02ReferenceCameraManifestSha256=IslandDefinition.Hash(bytes);
            Report baseline=JsonUtility.FromJson<Report>(System.Text.Encoding.UTF8.GetString(bytes));
            if(!baseline.technicalChecksPassed||baseline.mode!="ph02-fitted-support-comparison"||baseline.captures.Length!=12)
                throw new InvalidOperationException("Required R13 fitted comparison camera manifest is missing or invalid.");
            foreach(ShotRecord capture in baseline.captures)
            {
                if(capture.widthPixels!=width||capture.heightPixels!=width*3/4&&capture.heightPixels!=width*9/16)
                    throw new InvalidOperationException("Imported-tuple comparison must retain the R13 image dimensions.");
                ph02ReferenceCameras.Add(capture.id,capture.camera);
            }
        }
        private static void ApplyPH02ReferenceCamera(string shotId)
        {
            if(!ph02ReferenceCameras.TryGetValue(shotId,out CameraRecord reference))throw new InvalidOperationException("No preserved R13 camera for comparison shot "+shotId);
            camera.transform.SetPositionAndRotation(reference.position,Quaternion.Euler(reference.rotationEuler));
            camera.orthographic=reference.orthographic;camera.orthographicSize=reference.orthographicSizeMetres;
            camera.fieldOfView=reference.fieldOfView;camera.nearClipPlane=reference.nearClip;camera.farClipPlane=reference.farClip;
        }

        private static PH02FitReadback VerifyPH02FittedReadback(Mesh crown,Mesh support,float cutHeight)
        {
            var record=new PH02FitReadback();
            Vector3[] cp=crown.vertices,cn=crown.normals,sp=support.vertices,sn=support.normals;
            Vector2[] cuv=crown.uv,suv=support.uv;Vector4[] ct=crown.tangents,st=support.tangents;Color[] blend=support.colors;
            record.completeAttributeArrays=cp.Length==cn.Length&&cp.Length==cuv.Length&&cp.Length==ct.Length&&sp.Length==sn.Length&&sp.Length==suv.Length&&sp.Length==st.Length&&sp.Length==blend.Length;
            if(!record.completeAttributeArrays)return record;
            var expected=new HashSet<Tuple<Vector3,Vector3,Vector2,Vector4>>();
            var actual=new HashSet<Tuple<Vector3,Vector3,Vector2,Vector4>>();
            var sourcePlaneEdges=PlaneEdges(cp,crown.triangles,cutHeight);
            var supportPlaneEdges=PlaneEdges(sp,support.triangles,cutHeight);
            var sourceBoundaryCorners=new HashSet<int>();
            var allPlaneTuples=new HashSet<Tuple<Vector3,Vector3,Vector2,Vector4>>();
            // A triangulated clipped quad also has a corner on the cut plane in its other
            // triangle. That corner is not incident to a cut edge and can have a different
            // per-triangle tangent. The seam contract belongs to true boundary-edge endpoints.
            // Count geometric edge incidences so attribute splits cannot create false edges.
            foreach(var entry in sourcePlaneEdges)
            {
                if(entry.Value.Count==2){record.sourceInteriorPlaneEdges++;continue;}
                if(entry.Value.Count!=1){record.nonManifoldPlaneEdges++;continue;}
                record.sourceBoundaryEdges++;int a=entry.Value[0].Item1,b=entry.Value[0].Item2;
                sourceBoundaryCorners.Add(a);sourceBoundaryCorners.Add(b);
                expected.Add(Tuple.Create(cp[a],cn[a],cuv[a],ct[a]));expected.Add(Tuple.Create(cp[b],cn[b],cuv[b],ct[b]));
                if(!supportPlaneEdges.TryGetValue(entry.Key,out List<Tuple<int,int>> supportEdges)||supportEdges.Count!=1)
                {record.missingSupportBoundaryEdges++;continue;}
                int sa=supportEdges[0].Item1,sb=supportEdges[0].Item2;
                if(!sp[sa].Equals(cp[b])||!sp[sb].Equals(cp[a]))record.nonReversedBoundaryEdges++;
                if(!Tuple.Create(sp[sa],sn[sa],suv[sa],st[sa]).Equals(Tuple.Create(cp[b],cn[b],cuv[b],ct[b]))
                    ||!Tuple.Create(sp[sb],sn[sb],suv[sb],st[sb]).Equals(Tuple.Create(cp[a],cn[a],cuv[a],ct[a])))record.boundaryEdgeAttributeMismatches++;
            }
            foreach(var entry in supportPlaneEdges)
            {
                if(entry.Value.Count==2){record.supportInteriorPlaneEdges++;continue;}
                if(entry.Value.Count!=1){record.nonManifoldPlaneEdges++;continue;}
                record.supportBoundaryEdges++;
                if(!sourcePlaneEdges.TryGetValue(entry.Key,out List<Tuple<int,int>> sourceEdges)||sourceEdges.Count!=1)record.unexpectedSupportBoundaryEdges++;
            }
            for(int i=0;i<cp.Length;i++)if(cp[i].y==cutHeight)
            {
                record.allSourceCutPlaneCornerIndices++;allPlaneTuples.Add(Tuple.Create(cp[i],cn[i],cuv[i],ct[i]));
                if(!sourceBoundaryCorners.Contains(i))record.excludedNonBoundaryCornerIndices++;
            }
            record.sourceBoundaryCornerIndices=sourceBoundaryCorners.Count;
            record.allSourceCutPlaneDistinctTuples=allPlaneTuples.Count;
            record.excludedOnlyNonBoundaryTuples=allPlaneTuples.Count(tuple=>!expected.Contains(tuple));
            for(int i=0;i<sp.Length;i++)if(sp[i].y==cutHeight)
            {
                record.supportTopVertices++;
                var tuple=Tuple.Create(sp[i],sn[i],suv[i],st[i]);actual.Add(tuple);
                if(!expected.Contains(tuple))record.unmatchedTopTuples++;
                if(blend[i].r!=0f)record.nonzeroTopBlendWeights++;
            }
            record.distinctSourceRimTuples=expected.Count;record.distinctSupportRimTuples=actual.Count;
            record.missingSourceRimTuples=expected.Count(tuple=>!actual.Contains(tuple));
            record.passed=record.sourceBoundaryEdges==record.expectedSourceBoundaryEdges&&record.supportBoundaryEdges==record.expectedSourceBoundaryEdges
                &&record.nonManifoldPlaneEdges==0&&record.missingSupportBoundaryEdges==0&&record.unexpectedSupportBoundaryEdges==0
                &&record.nonReversedBoundaryEdges==0&&record.boundaryEdgeAttributeMismatches==0
                &&expected.Count>=3&&record.supportTopVertices>=3&&record.unmatchedTopTuples==0&&record.missingSourceRimTuples==0&&record.nonzeroTopBlendWeights==0;
            return record;
        }
        private static Dictionary<Tuple<Vector3,Vector3>,List<Tuple<int,int>>> PlaneEdges(Vector3[] positions,int[] indices,float height)
        {
            var edges=new Dictionary<Tuple<Vector3,Vector3>,List<Tuple<int,int>>>();
            for(int triangle=0;triangle<indices.Length;triangle+=3)for(int e=0;e<3;e++)
            {
                int a=indices[triangle+e],b=indices[triangle+(e+1)%3];Vector3 pa=positions[a],pb=positions[b];
                if(pa.y!=height||pb.y!=height)continue;
                int order=pa.x.CompareTo(pb.x);if(order==0)order=pa.y.CompareTo(pb.y);if(order==0)order=pa.z.CompareTo(pb.z);
                var key=order<=0?Tuple.Create(pa,pb):Tuple.Create(pb,pa);
                if(!edges.TryGetValue(key,out List<Tuple<int,int>> list)){list=new List<Tuple<int,int>>();edges.Add(key,list);}
                list.Add(Tuple.Create(a,b));
            }
            return edges;
        }

        private static void PH02FittedSelect(GameObject root)
        {
            mainTree=root;mainBounds=TreeBounds(root);Neutral();neutralFloor.SetActive(false);
        }
        private static void PH02FittedGray(bool gray)
        {
            foreach(Renderer renderer in ph02FitCandidate.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterial=gray?ph02FitGrayMaterial:renderer.name.StartsWith("Separate",StringComparison.Ordinal)?ph02FitSupportMaterial:ph02FitSourceMaterial;
        }
        private static List<Shot> BuildPH02FittedShots()
        {
            int portrait=width*3/4,landscape=width*9/16;
            Vector3 under=new Vector3(.2f,-1,-.35f),join=new Vector3(.4f,-.2f,-1);
            Bounds joinBounds=new Bounds(ph02FitMetrics.rimCentre-Vector3.up*.06f,Vector3.one*.40f);
            void Whole(Vector3 direction){PH02FittedSelect(ph02FitCandidate);Frame(ph02FitBounds,direction,1.18f,portrait);}
            void Compare(GameObject root,bool close){PH02FittedSelect(root);Frame(close?joinBounds:ph02FitBounds,close?join:under,1.18f,portrait);}
            return new List<Shot>{
                new Shot{Id="01-ph02-fitted-neutral-front",Purpose="Original-scale unchanged PH02 crown with separate fitted support; neutral front, no whole-tree weld or acceptance.",Height=portrait,Configure=()=>Whole(new Vector3(0,.06f,-1))},
                new Shot{Id="02-ph02-fitted-neutral-side",Purpose="Same fitted-support candidate, neutral side; inspect transition taper without resizing the crown.",Height=portrait,Configure=()=>Whole(new Vector3(1,.04f,0))},
                new Shot{Id="03-ph02-fitted-neutral-oblique-under",Purpose="Whole candidate from below at an oblique angle; expose remaining gaps, seams and hanging leaves.",Height=portrait,Configure=()=>Whole(new Vector3(.15f,-.45f,-1))},
                new Shot{Id="04-ph02-fitted-neutral-above",Purpose="Whole candidate from above with unchanged source leaf coverage and native metre scale.",Height=portrait,Configure=()=>Whole(new Vector3(.2f,1,-.3f))},
                new Shot{Id="05-ph02-fitted-textured-join",Purpose="Close fitted rim and transition with source diffuse/normal/roughness on both sides. Atlas extrapolation and blend quality remain under review.",Height=portrait,Configure=()=>Compare(ph02FitCandidate,true)},
                new Shot{Id="06-ph02-fitted-gray-join",Purpose="Exact05 camera with both crown and support in the same mid-gray material, no source normal maps; isolates geometric and vertex-normal continuity.",Height=portrait,Configure=()=>{Compare(ph02FitCandidate,true);PH02FittedGray(true);},Restore=()=>PH02FittedGray(false)},
                new Shot{Id="07-ph02-fitted-cosmic-whole",Purpose="Candidate-only cosmic illumination on the small prototype. Native source origin/scale retained; isolated support and crown do not constitute a population or full family.",Height=landscape,Configure=()=>{PH02FittedSelect(ph02FitCandidate);Cosmic();Frame(ph02FitBounds,new Vector3(.5f,.14f,-1),1.22f,landscape);}},
                new Shot{Id="08-ph02-source-underside-comparison",Purpose="Full original PH02 source in the same native-coordinate upper-region frame as09/10. Source lower stem may extend beyond this deliberate crown/support crop.",ComparisonGroup="PH02 underside",ComparisonVariant="source-original",Height=portrait,Configure=()=>Compare(ph02FitOriginal,false)},
                new Shot{Id="09-ph02-prior-support-underside",Purpose="Prior R08 simple open support translated into source coordinates, retaining4cm overlap. Exact08/10 camera; source crown is unchanged.",ComparisonGroup="PH02 underside",ComparisonVariant="prior-support",Height=portrait,Configure=()=>Compare(ph02FitCurrent,false)},
                new Shot{Id="10-ph02-fitted-underside-comparison",Purpose="Fitted support with exact08/09 camera and unchanged crown. Inspect the separately indexed open support without a collar or hidden cap.",ComparisonGroup="PH02 underside",ComparisonVariant="fitted-support",Height=portrait,Configure=()=>Compare(ph02FitCandidate,false)},
                new Shot{Id="11-ph02-prior-support-join",Purpose="Prior R08 overlap with exact05/06/12 join framing, preserving the gray simple support for direct comparison.",ComparisonGroup="PH02 join",ComparisonVariant="prior-support",Height=portrait,Configure=()=>Compare(ph02FitCurrent,true)},
                new Shot{Id="12-ph02-source-original-join",Purpose="Original source stem across the same cut-height region and exact05/06/11 camera. No support substitute or crown resizing.",ComparisonGroup="PH02 join",ComparisonVariant="source-original",Height=portrait,Configure=()=>Compare(ph02FitOriginal,true)}
            };
        }
        [Serializable] private sealed class PH02FitReadback
        {
            public string scope="Actual Unity Mesh getter readback: geometric single-incidence cut edges select the true rim endpoint position/normal/UV/tangent4 tuples. All69 source edges must have reversed support edges with exact endpoint attributes and zero source-rim vertex-red blend. Other triangle corners coincident with the cut plane are counted separately, not treated as boundary endpoints. No tolerance relaxation, visual score or whole-tree weld claim.";
            public bool completeAttributeArrays,passed;
            public int supportTopVertices,distinctSourceRimTuples,distinctSupportRimTuples,unmatchedTopTuples,missingSourceRimTuples,nonzeroTopBlendWeights;
            public int expectedSourceBoundaryEdges=69;
            public int sourceBoundaryEdges,supportBoundaryEdges,sourceInteriorPlaneEdges,supportInteriorPlaneEdges,nonManifoldPlaneEdges;
            public int missingSupportBoundaryEdges,unexpectedSupportBoundaryEdges,nonReversedBoundaryEdges,boundaryEdgeAttributeMismatches;
            public int allSourceCutPlaneCornerIndices,sourceBoundaryCornerIndices,excludedNonBoundaryCornerIndices,allSourceCutPlaneDistinctTuples,excludedOnlyNonBoundaryTuples;
        }
        [Serializable] private sealed class PH02FitEvidence
        {
            public string schema="starfall.ph02-fitted-inspection.v1",failureType,supportShader;
            public string status="Unaccepted attachment experiment; separate open meshes at native source scale/origin. Numeric checks and independent visual review are distinct.";
            public PH02FittedSupportCandidate.Report create;
            public bool importedTuplesRequested,importedCrownMeshReadbackPassed;
            public PH02ImportedCrownCandidate.Report importedCrown;
            public string importedMeshExpandedSha256,referenceCameraManifest,referenceCameraManifestSha256;
            public PH02FitReadback actualMeshReadback;
            public Vector3 currentSupportTranslation;
        }

        private static GameObject SetupPH02Candidate()
        {
            const string stem="Assets/CityLife/Art/PolyHaven/QuiverTree02/textures/quiver_tree_02";
            Material material=ImportedMaterial(stem);
            Mesh fullMesh=PH02CrownCandidate.CreateFullOriginalForComparison();owned.Add(fullMesh);
            Mesh crownMesh=PH02CrownCandidate.CreateCrown(ph02CutHeight);owned.Add(crownMesh);
            ph02CutReport=PH02CrownCandidate.Describe(ph02CutHeight);
            File.WriteAllText(Path.Combine(outputDirectory,"ph02-crown-extraction.json"),JsonUtility.ToJson(ph02CutReport,true));
            ph02Full=PH02MeshObject("PH02 full original - retained metre scale",fullMesh,material);
            ph02Full.transform.position=-Vector3.up*fullMesh.bounds.min.y;
            ph02Crown=PH02MeshObject("PH02 crown - explicit open horizontal cut",crownMesh,material);
            ph02Attached=new GameObject("PH02 crown on separate diagnostic support - unaccepted join");
            GameObject crown=PH02MeshObject("Original-scale clipped source crown",crownMesh,material);
            crown.transform.SetParent(ph02Attached.transform,false);
            Vector3 target=new Vector3(0,.90f,0);
            crown.transform.localPosition=target-ph02CutReport.boundaryCentre;
            Mesh support=PH02SupportMesh();owned.Add(support);
            GameObject branch=PH02MeshObject("Separate mid-gray open tapered support - not source geometry",support,LitMaterial("PH02 diagnostic support - mid-gray",new Color(.5f,.5f,.5f),.15f));
            branch.transform.SetParent(ph02Attached.transform,false);
            foreach(var item in new[]{new {Root=ph02Full,Kind="ph02-full-source-comparison"},new {Root=ph02Crown,Kind="ph02-open-cut-crown"},new {Root=ph02Attached,Kind="ph02-unaccepted-attachment"}})
                subjects.Add(new Subject{Root=item.Root,Kind=item.Kind,Age=-1,Seed=0,AssetPath=PH02CrownCandidate.SourceAssetPath,SourceMaterialNames=new[]{"quiver_tree_02"}});
            File.WriteAllText(Path.Combine(outputDirectory,"ph02-attachment-setup.json"),JsonUtility.ToJson(new PH02AttachmentRecord{
                sourceBoundaryCentre=ph02CutReport.boundaryCentre,attachmentTarget=target,crownTranslation=crown.transform.localPosition,
                cutHeightMetres=ph02CutHeight,supportTopMetres=.94f,supportBottomRadius=.11f,supportTopRadius=.068f,
                note="Original source crown scale1; translation only. Separate 32-sided mid-gray open tapered support from y0 to0.94; cut centre at y0.90 gives0.04m overlap. Neither mesh is welded or capped; the fit is a diagnostic, not an accepted branch join or species claim."
            },true));
            ph02Crown.SetActive(false);ph02Attached.SetActive(false);
            return ph02Full;
        }

        private static GameObject PH02MeshObject(string name,Mesh mesh,Material material)
        {
            var root=new GameObject(name);
            root.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
            return root;
        }

        private static Mesh PH02SupportMesh()
        {
            const int sides=32;
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            for(int ring=0;ring<2;ring++)for(int i=0;i<=sides;i++)
            {
                float angle=i*2f*Mathf.PI/sides, radius=ring==0?.11f:.068f;
                vertices.Add(new Vector3(Mathf.Cos(angle)*radius,ring*.94f,Mathf.Sin(angle)*radius));
                normals.Add(new Vector3(Mathf.Cos(angle),(.11f-.068f)/.94f,Mathf.Sin(angle)).normalized);
                uv.Add(new Vector2((float)i/sides,ring));
            }
            for(int i=0;i<sides;i++)
            {
                int a=i,b=i+1,c=i+sides+1,d=c+1;
                triangles.AddRange(new[]{a,c,b,b,c,d});
            }
            var mesh=new Mesh{name="PH02 separate open diagnostic support"};
            mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
            return mesh;
        }

        private static void PH02Select(GameObject root,bool showGround)
        {
            mainTree=root;mainBounds=TreeBounds(root);Neutral();neutralFloor.SetActive(showGround);
        }

        private static List<Shot> BuildPH02Shots()
        {
            int portrait=width*3/4,landscape=width*9/16;
            return new List<Shot>{
                new Shot{Id="01-ph02-full-original-neutral",Purpose="Pinned full PH02 source at original metre scale, original mapped vertex normals and polygon-corner UVs. Neutral original comparison; no age/seed/family acceptance.",Height=portrait,Configure=()=>{PH02Select(ph02Full,true);Frame(mainBounds,new Vector3(.35f,.12f,-1),1.18f,portrait);}},
                new Shot{Id="02-ph02-full-original-cosmic",Purpose="Same full source geometry and white-tinted source maps on cosmic prototype ground at original scale. Single candidate palette/readability comparison only.",Height=landscape,Configure=()=>{PH02Select(ph02Full,false);Cosmic();Vector3 eye=new Vector3(mainBounds.size.x*.75f,0,-3.5f);eye.y=PrototypeHeight(eye.x,eye.z)+1.85f;Perspective(eye,mainBounds.center,landscape);}},
                new Shot{Id="03-ph02-cut-crown-front",Purpose="Original-scale crown after the recorded explicit horizontal stem cut; front. No cap/socket or leaf-completeness claim.",Height=portrait,Configure=()=>{PH02Select(ph02Crown,false);Frame(mainBounds,new Vector3(0,.06f,-1),1.18f,portrait);}},
                new Shot{Id="04-ph02-cut-crown-side",Purpose="Same clipped source crown, side view exposes hanging leaves and retained short stem; no resizing.",Height=portrait,Configure=()=>{PH02Select(ph02Crown,false);Frame(mainBounds,new Vector3(1,.04f,0),1.18f,portrait);}},
                new Shot{Id="05-ph02-cut-crown-underside",Purpose="Clipped crown underside: explicit open boundary, retained leaf attachments and source topology without a fabricated socket.",Height=portrait,Configure=()=>{PH02Select(ph02Crown,false);Frame(mainBounds,new Vector3(.2f,-1,-.35f),1.18f,portrait);}},
                new Shot{Id="06-ph02-cut-crown-above",Purpose="Clipped crown from above: source leaf overlap and coverage with original source maps and scale.",Height=portrait,Configure=()=>{PH02Select(ph02Crown,false);Frame(mainBounds,new Vector3(.2f,1,-.3f),1.18f,portrait);}},
                new Shot{Id="07-ph02-diagnostic-attachment",Purpose="Original-scale source crown translated by its recorded boundary centre onto a separate mid-gray open tapered support. Four-centimetre overlap is an unaccepted fit test, not a welded branch or full family.",Height=portrait,Configure=()=>{PH02Select(ph02Attached,true);Frame(mainBounds,new Vector3(.75f,.1f,-1),1.18f,portrait);}},
                new Shot{Id="08-ph02-attachment-join-closeup",Purpose="Close side/underside of the explicit crown/support overlap around y0.90m. Reveals gaps/intersections without a new cap or socket; original source scale retained.",Height=portrait,Configure=()=>{PH02Select(ph02Attached,false);Frame(new Bounds(new Vector3(0,.94f,0),Vector3.one*.48f),new Vector3(.4f,-.2f,-1),1.12f,portrait);}}
            };
        }

        [Serializable] private sealed class PH02AttachmentRecord
        {
            public Vector3 sourceBoundaryCentre,attachmentTarget,crownTranslation;
            public float cutHeightMetres,supportTopMetres,supportBottomRadius,supportTopRadius;
            public string note;
        }

        private static List<Shot> BuildImportedShots()
        {
            var shots = new List<Shot>();
            int portrait = width * 3 / 4, landscape = width * 9 / 16;
            for (int index = 1; index <= 2; index++)
            {
                int candidate = index, first = (index - 1) * 6 + 1;
                string name = "ph" + index.ToString("D2", CultureInfo.InvariantCulture);
                string Prefix(int offset) => (first + offset).ToString("D2", CultureInfo.InvariantCulture) + "-" + name + "-";
                shots.Add(new Shot { Id = Prefix(0) + "neutral-front", Purpose = "Imported Poly Haven original at its imported scale; neutral front. Narrow candidate comparison, no procedural age/seed or acceptance claim.", Height = portrait, Configure = () => { SelectImported(candidate); Neutral(); Frame(mainBounds, new Vector3(0, .09f, -1), 1.18f, portrait); } });
                shots.Add(new Shot { Id = Prefix(1) + "neutral-three-quarter", Purpose = "Original imported geometry/material maps; neutral three-quarter at unmodified model scale.", Height = portrait, Configure = () => { SelectImported(candidate); Neutral(); Frame(mainBounds, new Vector3(1, .18f, -1), 1.18f, portrait); } });
                shots.Add(new Shot { Id = Prefix(2) + "branch-region", Purpose = "Original mesh middle/upper stem region; bounds-derived inspection target, not a publisher-identified branch-union marker.", Height = portrait, Configure = () => { SelectImported(candidate); Neutral(); ImportedRegion(.56f, .40f, new Vector3(.65f, .16f, -1), portrait); } });
                shots.Add(new Shot { Id = Prefix(3) + "trunk-bark", Purpose = "Original trunk diffuse/normal/roughness mapping in neutral light. Bounds-derived lower-stem target.", Height = portrait, Configure = () => { SelectImported(candidate); Neutral(); ImportedRegion(.23f, .33f, new Vector3(.25f, .08f, -1), portrait); } });
                shots.Add(new Shot { Id = Prefix(4) + "foliage", Purpose = "Original upper foliage region with explicit source material-map assignment; no generated replacement leaves or inferred age.", Height = portrait, Configure = () => { SelectImported(candidate); Neutral(); ImportedRegion(.84f, .43f, new Vector3(.3f, .25f, -1), portrait); } });
                shots.Add(new Shot { Id = Prefix(5) + "cosmic-eye-level", Purpose = "Single imported original at true imported scale on the small cosmic prototype patch; camera ground clearance1.85m. Candidate look comparison only, not population or age-form validation.", Height = landscape, Configure = () => { SelectImported(candidate); Cosmic(); Vector3 eye = new Vector3(mainBounds.size.x * .75f, 0, -Mathf.Max(3.5f, mainBounds.size.y * 2.2f)); eye.y = PrototypeHeight(eye.x, eye.z) + 1.85f; Perspective(eye, new Vector3(mainBounds.center.x, mainBounds.min.y + mainBounds.size.y * .5f, 0), landscape); } });
            }
            return shots;
        }

        private static void SelectImported(int candidate)
        {
            mainTree = importedTrees.TryGetValue(candidate, out GameObject tree) ? tree : AddImportedTree(candidate);
            activeCandidate = candidate;
            mainBounds = TreeBounds(mainTree);
        }

        private static void ImportedRegion(float heightFraction, float extentFraction, Vector3 direction, int height)
        {
            Vector3 point = new Vector3(mainBounds.center.x, mainBounds.min.y + mainBounds.size.y * heightFraction, mainBounds.center.z);
            Frame(new Bounds(point, Vector3.one * mainBounds.size.y * extentFraction), direction, 1.08f, height);
        }

        private static void Neutral()
        {
            mainTree.SetActive(true);
            foreach (GameObject tree in importedTrees.Values) if (tree != mainTree) tree.SetActive(false);
            foreach(GameObject tree in new[]{ph02Full,ph02Crown,ph02Attached})if(tree!=null&&tree!=mainTree)tree.SetActive(false);
            foreach(GameObject tree in new[]{ph02FitOriginal,ph02FitCurrent,ph02FitCandidate})if(tree!=null&&tree!=mainTree)tree.SetActive(false);
            if (lineup != null) lineup.SetActive(false);
            if (metreRuler != null) metreRuler.SetActive(false);
            if (companions != null) companions.SetActive(false);
            if (backdrop != null) backdrop.SetActive(false);
            if (rosetteFixture != null) rosetteFixture.SetActive(false);
            if (juvenileFixture != null) juvenileFixture.SetActive(false);
            if (adultVariants != null) adultVariants.SetActive(false);
            if (sourceFixture != null) sourceFixture.SetActive(false);
            neutralFloor.SetActive(true);
            if (cosmicFloor != null) cosmicFloor.SetActive(false);
            camera.backgroundColor = new Color(0.30f, 0.33f, 0.35f);
            key.color = Color.white; key.intensity = 1.55f;
            fill.transform.rotation=Quaternion.Euler(24,100,0);rim.transform.rotation=Quaternion.Euler(35,170,0);
            key.shadows = LightShadows.Soft;
            fill.color = Color.white; fill.intensity = 0.48f;
            rim.color = Color.white; rim.intensity = 0.55f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.32f, 0.34f, 0.36f);
            RenderSettings.ambientEquatorColor = new Color(0.19f, 0.20f, 0.21f);
            RenderSettings.ambientGroundColor = new Color(0.09f, 0.095f, 0.10f);
            SetExplicitAmbient(false);
        }

        private static void Cosmic()
        {
            EnsurePrototype();
            Neutral();
            neutralFloor.SetActive(false);
            cosmicFloor.SetActive(true);
            companions.SetActive(true);
            backdrop.SetActive(true);
            camera.backgroundColor = new Color(0.012f, 0.028f, 0.085f);
            key.color = new Color(1f, 0.87f, 0.66f); key.intensity = 2.25f;
            fill.color = new Color(0.42f, 0.66f, 1f); fill.intensity = 0.52f;
            rim.color = new Color(0.30f, 0.77f, 0.88f); rim.intensity = 0.65f;
            RenderSettings.ambientSkyColor = new Color(0.20f, 0.25f, 0.37f);
            RenderSettings.ambientEquatorColor = new Color(0.19f, 0.16f, 0.11f);
            RenderSettings.ambientGroundColor = new Color(0.13f, 0.09f, 0.04f);
            SetExplicitAmbient(true);
        }

        private static void SetExplicitAmbient(bool cosmic)
        {
            // Explicit SH avoids depending on an editor GI/environment update in an isolated
            // render request. It changes ambient illumination, not exposure or model albedo.
            var probe = new SphericalHarmonicsL2();
            probe.Clear();
            probe.AddAmbientLight(cosmic ? new Color(.075f, .085f, .11f) : new Color(.12f, .13f, .14f));
            probe.AddDirectionalLight(Vector3.up, cosmic ? new Color(.14f, .20f, .32f) : new Color(.24f, .26f, .28f), .45f);
            RenderSettings.ambientProbe = probe;
        }

        private static float PrototypeHeight(float x, float z)
        {
            return .42f * Mathf.Sin(x * .17f) * Mathf.Sin(z * .12f) + .32f * (Mathf.Cos(z * .20f) - 1f)
                + .7f * (Mathf.Exp(-((x - 10f) * (x - 10f) + (z - 12f) * (z - 12f)) / 120f) - Mathf.Exp(-244f / 120f));
        }

        private static void BuildPrototypeGround()
        {
            cosmicFloor = new GameObject("Small sculpted 60m prototype ground and rocks");
            const int side = 65;
            var vertices = new Vector3[side * side]; var uv = new Vector2[vertices.Length];
            var indices = new int[(side - 1) * (side - 1) * 6]; int cursor = 0;
            for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
            {
                float wx = x * 60f / (side - 1) - 30f, wz = z * 60f / (side - 1) - 22f;
                int index = z * side + x;
                vertices[index] = new Vector3(wx, PrototypeHeight(wx, wz), wz);
                uv[index] = new Vector2(x / (float)(side - 1), z / (float)(side - 1));
                if (x == side - 1 || z == side - 1) continue;
                indices[cursor++] = index; indices[cursor++] = index + side; indices[cursor++] = index + 1;
                indices[cursor++] = index + 1; indices[cursor++] = index + side; indices[cursor++] = index + side + 1;
            }
            var mesh = new Mesh { name = "Prototype sculpted metre-space ground", vertices = vertices, uv = uv, triangles = indices };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); owned.Add(mesh);
            cosmicFloor.AddComponent<MeshFilter>().sharedMesh = mesh;
            cosmicFloor.AddComponent<MeshRenderer>().sharedMaterial = sandMaterial;
            Material stone = LitMaterial("Prototype fractured ochre stones", new Color(.58f, .46f, .31f), .07f);
            var random = new System.Random(3319);
            for (int i = 0; i < 17; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float radius = 2.7f + (float)random.NextDouble() * 12f;
                float x = Mathf.Cos(angle) * radius, z = Mathf.Sin(angle) * radius + 3f;
                Vector3 scale = new Vector3(.5f + (float)random.NextDouble() * 1.1f, .3f + (float)random.NextDouble() * .7f, .45f + (float)random.NextDouble() * .7f);
                var rock = new GameObject("Prototype angular grounded stone " + i);
                rock.transform.SetParent(cosmicFloor.transform, false);
                rock.transform.localPosition = new Vector3(x, PrototypeHeight(x, z) - .07f, z);
                rock.transform.localScale = scale;
                rock.AddComponent<MeshFilter>().sharedMesh = MakeRockMesh(random);
                rock.AddComponent<MeshRenderer>().sharedMaterial = stone;
                rock.transform.localRotation = Quaternion.Euler(0, (float)random.NextDouble() * 180f, 0);
            }
            cosmicFloor.SetActive(false);
        }

        private static Mesh MakeRockMesh(System.Random random)
        {
            const int sides = 7, rings = 4;
            float[] heights = { 0f, .18f, .61f, .88f };
            float[] radii = { .39f, .53f, .45f, .27f };
            var points = new Vector3[sides * rings + 2];
            for (int r = 0; r < rings; r++) for (int s = 0; s < sides; s++)
            {
                float angle = (s + (float)random.NextDouble() * .19f + r * .065f) * Mathf.PI * 2f / sides;
                float radius = radii[r] * (.78f + (float)random.NextDouble() * .36f);
                points[r * sides + s] = new Vector3(Mathf.Cos(angle) * radius + r * .025f,
                    heights[r] + (r == 0 ? 0f : ((float)random.NextDouble() - .5f) * .085f), Mathf.Sin(angle) * radius);
            }
            points[points.Length - 2] = new Vector3(0, 0, 0);
            points[points.Length - 1] = new Vector3(.1f, .91f, -.03f);
            var vertices = new List<Vector3>(); var indices = new List<int>();
            void Face(int a, int b, int c)
            {
                Vector3 pa = points[a], pb = points[b], pc = points[c];
                if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), (pa + pb + pc) / 3f - new Vector3(0, .43f, 0)) < 0f)
                { Vector3 swap = pb; pb = pc; pc = swap; }
                int start = vertices.Count; vertices.Add(pa); vertices.Add(pb); vertices.Add(pc);
                indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
            }
            for (int r = 0; r < rings - 1; r++) for (int s = 0; s < sides; s++)
            {
                int next = (s + 1) % sides;
                Face(r * sides + s, r * sides + next, (r + 1) * sides + s);
                Face(r * sides + next, (r + 1) * sides + next, (r + 1) * sides + s);
            }
            for (int s = 0; s < sides; s++)
            {
                Face(points.Length - 2, s, (s + 1) % sides);
                Face(points.Length - 1, (rings - 1) * sides + s, (rings - 1) * sides + (s + 1) % sides);
            }
            var mesh = new Mesh { name = "Deterministic fractured rock - flat face normals" };
            mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            owned.Add(mesh); return mesh;
        }

        private static void Frame(Bounds bounds, Vector3 direction, float padding, int height)
        {
            camera.aspect = width / (float)height;
            camera.orthographic = true;
            camera.transform.position = bounds.center + direction.normalized * (bounds.size.magnitude * 2f + 8f);
            camera.transform.LookAt(bounds.center);
            float horizontal = 0f, vertical = 0f;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 offset = Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                horizontal = Mathf.Max(horizontal, Mathf.Abs(Vector3.Dot(offset, camera.transform.right)));
                vertical = Mathf.Max(vertical, Mathf.Abs(Vector3.Dot(offset, camera.transform.up)));
            }
            camera.orthographicSize = Mathf.Max(vertical, horizontal / camera.aspect) * padding;
        }

        private static void Closeup(string anchorName, float extent, Vector3 direction, int height)
        {
            Transform anchor = RequiredAnchor(anchorName);
            Frame(new Bounds(anchor.position, Vector3.one * Mathf.Max(0.2f, extent)), direction, 1.08f, height);
        }

        private static void Perspective(Vector3 position, Vector3 lookAt, int height)
        {
            camera.aspect = width / (float)height;
            camera.orthographic = false;
            camera.fieldOfView = 52f;
            camera.transform.position = position;
            camera.transform.LookAt(lookAt);
        }

        private static void BuildMetreRuler(Bounds bounds)
        {
            metreRuler = new GameObject("One-metre ruler - geometry, no font dependency");
            float x = bounds.min.x - 1f;
            int top = Mathf.CeilToInt(bounds.max.y);
            Primitive(PrimitiveType.Cube, "Metre pole", rulerMaterial, new Vector3(x, top * 0.5f, -0.3f), new Vector3(0.025f, top, 0.025f), metreRuler.transform);
            for (int metre = 0; metre <= top; metre++)
            {
                Primitive(PrimitiveType.Cube, "Tick " + metre + "m", rulerMaterial, new Vector3(x, metre, -0.3f), new Vector3(0.28f, 0.025f, 0.025f), metreRuler.transform);
                DrawNumber(metre.ToString(CultureInfo.InvariantCulture), new Vector3(x - 0.60f, metre - 0.11f, -0.34f));
            }
            // The lower symbol is a geometric 'm', and each numbered major tick is exactly 1m.
            Stroke(new Vector2(0, 0), new Vector2(0, 0.2f), new Vector3(x + 0.23f, 0, -0.34f));
            Stroke(new Vector2(0, 0.2f), new Vector2(0.08f, 0.12f), new Vector3(x + 0.23f, 0, -0.34f));
            Stroke(new Vector2(0.08f, 0.12f), new Vector2(0.16f, 0.2f), new Vector3(x + 0.23f, 0, -0.34f));
            Stroke(new Vector2(0.16f, 0.2f), new Vector2(0.16f, 0), new Vector3(x + 0.23f, 0, -0.34f));
        }

        private static void DrawNumber(string text, Vector3 origin)
        {
            string[] patterns = { "abcdef", "bc", "abdeg", "abcdg", "bcfg", "acdfg", "acdefg", "abc", "abcdefg", "abcdfg" };
            Vector2[] starts = { new Vector2(0, .22f), new Vector2(.12f, .22f), new Vector2(.12f, .11f), new Vector2(0, 0), new Vector2(0, .11f), new Vector2(0, .22f), new Vector2(0, .11f) };
            Vector2[] ends = { new Vector2(.12f, .22f), new Vector2(.12f, .11f), new Vector2(.12f, 0), new Vector2(.12f, 0), new Vector2(0, 0), new Vector2(0, .11f), new Vector2(.12f, .11f) };
            foreach (char character in text)
            {
                foreach (char segment in patterns[character - '0']) Stroke(starts[segment - 'a'], ends[segment - 'a'], origin);
                origin.x += .17f;
            }
        }

        private static void Stroke(Vector2 a, Vector2 b, Vector3 origin)
        {
            Vector2 delta = b - a;
            GameObject line = Primitive(PrimitiveType.Cube, "Ruler numeral stroke", rulerMaterial, origin + (Vector3)((a + b) * 0.5f), new Vector3(.019f, delta.magnitude, .02f), metreRuler.transform);
            line.transform.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg);
        }

        private static void BuildCosmicBackdrop()
        {
            backdrop = new GameObject("Cosmic prototype accents - blue gas giant only");
            Mesh sphere = MakeSmoothSphere();
            Material planetMaterial = TemporaryShaderMaterial("Procedural cobalt gas giant - no Earth texture", GasShader);
            var planet = new GameObject("Blue gas giant - procedural volumetric cloud bands");
            planet.transform.SetParent(backdrop.transform, false);
            planet.transform.localPosition = new Vector3(45, 78, 210);
            planet.transform.localScale = Vector3.one * 98f;
            planet.transform.localRotation = Quaternion.Euler(0, 0, -18f);
            planet.AddComponent<MeshFilter>().sharedMesh = sphere;
            planet.AddComponent<MeshRenderer>().sharedMaterial = planetMaterial;
            planet.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Material atmosphereMaterial = TemporaryShaderMaterial("Thin blue atmospheric limb", AtmosphereShader);
            var atmosphere = new GameObject("Gas giant atmosphere - Fresnel shell");
            atmosphere.transform.SetParent(planet.transform, false);
            atmosphere.transform.localScale = Vector3.one * 1.018f;
            atmosphere.AddComponent<MeshFilter>().sharedMesh = sphere;
            atmosphere.AddComponent<MeshRenderer>().sharedMaterial = atmosphereMaterial;
            atmosphere.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            Material starMaterial = TemporaryShaderMaterial("Procedural stellar points", StarShader);
            var random = new System.Random(73451);
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var colours = new List<Color>();
            for (int i = 0; i < 1250; i++)
            {
                Vector3 p = new Vector3((float)random.NextDouble() * 650f - 325f, 3f + (float)random.NextDouble() * 250f, 360f);
                float bright = (float)random.NextDouble();
                float radius = .07f + Mathf.Pow(bright, 4f) * .39f;
                Color colour = Color.Lerp(new Color(.21f, .37f, .59f), new Color(.85f, .85f, .68f), (float)random.NextDouble()) * (.35f + bright * .65f);
                int first = vertices.Count;
                vertices.Add(p + new Vector3(-radius, 0, 0)); vertices.Add(p + new Vector3(0, radius, 0)); vertices.Add(p + new Vector3(radius, 0, 0)); vertices.Add(p + new Vector3(0, -radius, 0));
                triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
                for (int j = 0; j < 4; j++) colours.Add(colour);
            }
            var stars = new GameObject("Seeded procedural stars"); stars.transform.SetParent(backdrop.transform, false);
            var mesh = new Mesh { name = "Prototype stellar geometry - 1250 varied points" }; mesh.SetVertices(vertices); mesh.SetColors(colours); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); owned.Add(mesh);
            stars.AddComponent<MeshFilter>().sharedMesh = mesh; stars.AddComponent<MeshRenderer>().sharedMaterial = starMaterial;
            stars.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Material TemporaryShaderMaterial(string name, string source)
        {
            // Compile only an in-memory inspection shader; no shader asset or model material is edited.
            Shader shader = ShaderUtil.CreateShaderAsset(source, true);
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Prototype shader compilation failed: " + name);
            shader.hideFlags = HideFlags.HideAndDontSave; owned.Add(shader);
            var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
            owned.Add(material); return material;
        }

        private static Mesh MakeSmoothSphere()
        {
            const int longitude = 128, latitude = 80;
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var indices = new List<int>();
            for (int y = 0; y <= latitude; y++) for (int x = 0; x <= longitude; x++)
            {
                float v = y * Mathf.PI / latitude, u = x * Mathf.PI * 2f / longitude;
                Vector3 normal = new Vector3(Mathf.Sin(v) * Mathf.Cos(u), Mathf.Cos(v), Mathf.Sin(v) * Mathf.Sin(u));
                vertices.Add(normal * .5f); normals.Add(normal);
            }
            void Face(int a, int b, int c)
            {
                if (Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), vertices[a] + vertices[b] + vertices[c]) < 0f)
                { int swap = b; b = c; c = swap; }
                indices.Add(a); indices.Add(b); indices.Add(c);
            }
            for (int y = 0; y < latitude; y++) for (int x = 0; x < longitude; x++)
            {
                int a = y * (longitude + 1) + x, b = a + longitude + 1;
                if (y > 0) Face(a, b, a + 1);
                if (y < latitude - 1) Face(a + 1, b, b + 1);
            }
            var mesh = new Mesh { name = "Smooth gas-giant sphere 128x80" };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds(); owned.Add(mesh); return mesh;
        }

        private const string GasShader = @"
Shader ""Hidden/CityLife/KokerboomGasInspection"" {
SubShader { Tags { ""RenderPipeline""=""UniversalPipeline"" ""RenderType""=""Opaque"" }
Pass { Tags { ""LightMode""=""SRPDefaultUnlit"" } Cull Back ZWrite On
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
struct A { float4 p:POSITION; float3 n:NORMAL; };
struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; float3 n:TEXCOORD1; float3 local:TEXCOORD2; };
V Vert(A i) { V o; o.p=TransformObjectToHClip(i.p.xyz); o.world=TransformObjectToWorld(i.p.xyz); o.n=TransformObjectToWorldNormal(i.n); o.local=i.n; return o; }
float hash(float3 p) { p=frac(p*.1031); p+=dot(p,p.yzx+33.33); return frac((p.x+p.y)*p.z); }
float noise(float3 p) {
 float3 i=floor(p),f=frac(p); f=f*f*(3-2*f);
 return lerp(lerp(lerp(hash(i),hash(i+float3(1,0,0)),f.x),lerp(hash(i+float3(0,1,0)),hash(i+float3(1,1,0)),f.x),f.y),lerp(lerp(hash(i+float3(0,0,1)),hash(i+float3(1,0,1)),f.x),lerp(hash(i+float3(0,1,1)),hash(i+1),f.x),f.y),f.z);
}
float fbm(float3 p) { return noise(p)*.55+noise(p*2.03+3.7)*.28+noise(p*4.07+9.1)*.12+noise(p*8.11)*.05; }
half4 Frag(V i):SV_Target {
 float3 p=normalize(i.local); float turbulence=fbm(p*5.8);
 float latitude=p.y+.12*(turbulence-.5)+.025*sin(p.x*17+p.z*7);
 float broad=sin(latitude*20+fbm(p*2.3)*4)*.20+sin(latitude*47+turbulence*5)*.11;
 float cloud=fbm(float3(p.x*28,p.y*92,p.z*28));
 float ribbons=smoothstep(.42,.73,fbm(float3(p.x*13,latitude*63,p.z*13)))*.12;
 float density=saturate(.48+broad+(cloud-.5)*.28+ribbons);
 float3 colour=lerp(float3(.014,.055,.15),float3(.14,.35,.59),density);
 colour=lerp(colour,float3(.24,.49,.65),smoothstep(.66,.86,density)*.22);
 float3 n=normalize(i.n),v=normalize(GetCameraPositionWS()-i.world);
 float illumination=.21+.86*pow(saturate(dot(n,normalize(float3(-.5,.65,-.55)))),.65);
 float limb=pow(1-saturate(dot(n,v)),4);
 return half4(colour*illumination+float3(.021,.11,.24)*limb,1);
}
ENDHLSL
} } }";

        private const string AtmosphereShader = @"
Shader ""Hidden/CityLife/KokerboomAtmosphereInspection"" {
SubShader { Tags { ""RenderPipeline""=""UniversalPipeline"" ""RenderType""=""Transparent"" ""Queue""=""Transparent"" }
Pass { Tags { ""LightMode""=""SRPDefaultUnlit"" } Cull Back ZWrite Off Blend One One
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
struct A { float4 p:POSITION; float3 n:NORMAL; };
struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; float3 n:TEXCOORD1; };
V Vert(A i) { V o; o.p=TransformObjectToHClip(i.p.xyz); o.world=TransformObjectToWorld(i.p.xyz); o.n=TransformObjectToWorldNormal(i.n); return o; }
half4 Frag(V i):SV_Target { float3 n=normalize(i.n),v=normalize(GetCameraPositionWS()-i.world); float rim=pow(1-saturate(dot(n,v)),5); float light=.25+.75*saturate(dot(n,normalize(float3(-.5,.65,-.55)))); return half4(float3(.023,.13,.30)*rim*light,0); }
ENDHLSL
} } }";

        private const string StarShader = @"
Shader ""Hidden/CityLife/KokerboomStarsInspection"" {
SubShader { Tags { ""RenderPipeline""=""UniversalPipeline"" ""RenderType""=""Opaque"" }
Pass { Tags { ""LightMode""=""SRPDefaultUnlit"" } Cull Off ZWrite On
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
struct A { float4 p:POSITION; float4 c:COLOR; }; struct V { float4 p:SV_POSITION; float4 c:COLOR; };
V Vert(A i) { V o; o.p=TransformObjectToHClip(i.p.xyz); o.c=i.c; return o; }
half4 Frag(V i):SV_Target { return i.c; }
ENDHLSL
} } }";

        private static void Capture(Shot shot)
        {
            AmbientRecord beforeAmbient = AmbientRecord.From(RenderSettings.ambientProbe, "Before warmup render requests");
            foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                foreach (Material material in renderer.sharedMaterials)
                    if (material == null || material.shader == null || !material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader))
                        throw new InvalidOperationException("An active inspection-stage material or shader failed its technical check.");
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            target = new RenderTexture(width, shot.Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { name = "Kokerboom inspection GPU target", antiAliasing = 1, useMipMap = false, hideFlags = HideFlags.HideAndDontSave };
            if (!target.Create()) throw new InvalidOperationException("Cannot create the offscreen GPU render texture.");
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("Active URP does not support SingleCameraRequest on this graphics device.");
            for (int warmup = 0; warmup < 3; warmup++) RenderPipeline.SubmitRenderRequest(camera, request);
            var watch = Stopwatch.StartNew();
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture previous = RenderTexture.active;
            var image = new Texture2D(width, shot.Height, TextureFormat.RGB24, false, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, shot.Height), 0, 0, false);
                image.Apply(false, false);
            }
            finally { RenderTexture.active = previous; }
            watch.Stop();
            ImageRecord imageRecord = InspectImage(image);
            string filename = dateStamp + "-" + shot.Id + ".png";
            File.WriteAllBytes(Path.Combine(outputDirectory, filename), image.EncodeToPNG());
            Object.DestroyImmediate(image);
            results.Add(new ShotRecord
            {
                id = shot.Id, purpose = shot.Purpose, imagePath = relativeDirectory + "/" + filename,
                comparisonGroup=shot.ComparisonGroup,comparisonVariant=shot.ComparisonVariant,comparisonNormalMap=shot.ComparisonNormalMap,
                widthPixels = width, heightPixels = shot.Height,
                renderReadbackMilliseconds = watch.Elapsed.TotalMilliseconds,
                camera = new CameraRecord { position = camera.transform.position, rotationEuler = camera.transform.eulerAngles, orthographic = camera.orthographic, orthographicSizeMetres = camera.orthographicSize, fieldOfView = camera.fieldOfView, nearClip = camera.nearClipPlane, farClip = camera.farClipPlane },
                lights = new[] { LightRecord.From(key), LightRecord.From(fill), LightRecord.From(rim) },
                ambientSky = RenderSettings.ambientSkyColor, ambientEquator = RenderSettings.ambientEquatorColor, ambientGround = RenderSettings.ambientGroundColor,
                ambientProbeBeforeRendering = beforeAmbient,
                ambientProbeAfterRendering = AmbientRecord.From(RenderSettings.ambientProbe, "After captured render request"),
                image = imageRecord, subjects = subjects.Where(s => s.Root.activeInHierarchy).Select(InspectSubject).ToArray()
            });
            Debug.Log("KOKERBOOM_IMAGE " + relativeDirectory + "/" + filename + " nonUniform=" + imageRecord.nonUniformContent);
        }

        private static ImageRecord InspectImage(Texture2D image)
        {
            float min = 1f, max = 0f, sum = 0f;
            int count = 0;
            for (int y = 0; y < 30; y++) for (int x = 0; x < 40; x++)
            {
                Color color = image.GetPixel((int)((x + .5f) * image.width / 40), (int)((y + .5f) * image.height / 30));
                float value = color.r * .2126f + color.g * .7152f + color.b * .0722f;
                min = Mathf.Min(min, value); max = Mathf.Max(max, value); sum += value; count++;
            }
            return new ImageRecord { sampledPixels = count, minimumLuminance = min, maximumLuminance = max, meanLuminance = sum / count, nonUniformContent = max > .025f && max - min > .015f };
        }

        private static SubjectRecord InspectSubject(Subject subject)
        {
            var record = new SubjectRecord
            {
                name = subject.Root.name, kind = subject.Kind, seed = subject.Seed, age01 = subject.Age, lod = subject.Lod,
                proceduralIdentityApplicable = subject.Kind == "tree" || subject.Kind == "isolated-terminal-rosette",
                sourceAssetPath = subject.AssetPath, originalMaterialNames = subject.SourceMaterialNames,
                position = subject.Root.transform.position, scale = subject.Root.transform.lossyScale,
                bounds = TreeBounds(subject.Root), descriptorJson = subject.Kind == "tree" ? JsonUtility.ToJson(KokerboomGeometry.Describe(subject.Seed, subject.Age)) : "",
                rendererCount = subject.Root.GetComponentsInChildren<Renderer>().Length
            };
            if (!string.IsNullOrEmpty(subject.AssetPath))
            {
                record.sourceAssetSha256 = IslandDefinition.Hash(File.ReadAllBytes(subject.AssetPath));
                var importer = AssetImporter.GetAtPath(subject.AssetPath) as ModelImporter;
                if (importer != null) { record.importerGlobalScale = importer.globalScale; record.importerUseFileScale = importer.useFileScale; }
                record.sourceNote = subject.Kind=="source-rosette-diagnostic" ? "Extracted source polygon components from PH01. Original per-corner UVs/normals, original scale, handedness conversion and root translation; no new caps. Basal components are separate and semantically unverified." : "Original imported LOD0; model scale unchanged. Only root translation aligns lower bound to stage. Imported vertices may split at UV/normal seams. Exact species, procedural seed and age are not asserted. Candidate02 mask is not treated as opacity without evidence.";
                if(subject.Kind.StartsWith("ph02-",StringComparison.Ordinal))
                    record.sourceNote="Pinned PH02 source decoded at metre scale with control-vertex-indexed normals and corner-indexed UVs; reflected winding and derived tangents. Full original and explicit open-cut crown are separate candidates. Attachment adds only a separately labelled mid-gray support and recorded translation; no source crown resizing, cap, weld or acceptance. See ph02-crown-extraction.json and ph02-attachment-setup.json. Mask is not assumed opacity.";
                if(subject.Kind=="ph01-tangent-comparison")
                    record.sourceNote="Controlled source-rosette tangent comparison. Current extractor uses its existing triangulation/recalculated tangent basis; imported-tuples candidate copies actual imported P/N/UV/tangent4 and preserves that basis without recalculation. Optional original subset retains imported triangles only after exact count and mapped-corner coverage checks. Original root and scale, same camera/source maps; see importedTupleChecks and per-shot comparison fields.";
                if(subject.Kind.StartsWith("ph02-fitted-",StringComparison.Ordinal))
                    record.sourceNote="Scoped PH02 source/current/fitted support comparison. Original source crown retains its native origin/metre scale, attributes and indices. Fitted support shares actual cut-rim P/N/UV/tangent4 but remains a separate open mesh. Prior R08 simple support is translated into the same source frame; source/current/fitted comparison cameras are fixed. Required custom support shader blends source maps by vertex red weight; see actual Create metrics and independent shared-rim mesh readback. No whole-tree weld, botanical, family, palette or runtime acceptance.";
                if(ph02FamilyMode&&subject.Kind=="tree")
                    record.sourceNote="Changed v2-preview family: PH02 imported-tuple crown plus fitted support, rigidly aligned at measured lower centre, then placed on procedural wood using PH01 mapped bark. Complete source attributes and compound blend weights are retained by the component; geometric green weights are an art-tint heuristic, not publisher semantics. Actual lower join, bases, age/seed variation and population need independent review. Component numeric checks are not full-family numeric or visual acceptance.";
            }
            var meshes = new List<MeshRecord>();
            foreach (MeshFilter filter in subject.Root.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) throw new InvalidOperationException("Subject contains a missing mesh.");
                long triangles = 0;
                for (int sub = 0; sub < mesh.subMeshCount; sub++) triangles += (long)mesh.GetIndexCount(sub) / 3;
                var materials = filter.GetComponent<Renderer>().sharedMaterials;
                foreach (Material material in materials)
                    if (material == null || material.shader == null || !material.shader.isSupported || material.shader.name == "Hidden/InternalErrorShader")
                        throw new InvalidOperationException("Subject material/shader is missing or unsupported.");
                meshes.Add(new MeshRecord { name = mesh.name, objectName = filter.name, vertices = mesh.vertexCount, triangles = triangles, localBounds = mesh.bounds, objectWorldScale = filter.transform.lossyScale, materialNames = materials.Select(m => m.name).ToArray(), shaders = materials.Select(m => m.shader.name).ToArray(), materials = materials.Select(m => new MaterialRecord { name = m.name, shader = m.shader.name, bindings = importedBindings.TryGetValue(m, out TextureBinding[] bindings) ? bindings : Array.Empty<TextureBinding>() }).ToArray() });
                record.vertices += mesh.vertexCount; record.triangles += triangles;
            }
            record.meshCount = meshes.Count; record.meshes = meshes.ToArray();
            if (record.meshCount == 0 || record.triangles == 0) throw new InvalidOperationException("Subject contains no renderable triangles.");
            return record;
        }

        private static void WriteReport(bool passed)
        {
            var report = new Report
            {
                schema = "citylife.kokerboom-inspection.v1", utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                status = passed ? (ph02ShotSelectionRequested?"Rendered scoped PH02 inspection; not a full-family review set":"Rendered for independent critique") : "Technical rendering failure; see local editor log",
                technicalChecksPassed = passed, visualAcceptance = ph02ShotSelectionRequested?"Not scored. Selected shots do not constitute a full-family evidence set.":"Not scored. Independent critique is required.",
                scope = ph02ShotSelectionRequested ? "Scoped selected-shot PH02 inspection from the existing full catalogue. Same view categories: shot functions and canonical order retained, but bounds-dependent cameras may differ. Actual recorded camera-field deltas against R16 are written by the launcher to ph02-shot-camera-comparison.json; R16 matrices were not recorded. Only selected Configure callbacks execute. This is not a full-family review round, numeric pass or runtime acceptance." : ph02FamilyMode ? "Changed v2-preview PH02 fitted-crown family: actual imported tuple component and PH01 mapped procedural wood; first17 standard family views plus measured lower joins and bases. Component checks are separate from full-family numerical/visual acceptance, native controls and sustained runtime performance." : woodDiagnosticMode ? "Scoped main-specimen wood diagnostic using unchanged R09 BuildShots configurations04,05,15: seed4242,age1,LOD0,source-leaf hybrid. No extra ages, seed lineup or prototype generation; compare actual recorded cameras before attributing image differences to geometry." : ph02FittedMode ? "Scoped PH02 source/current/fitted support comparison. Original-scale unchanged crown at native source origin; separately indexed open support with actual rim P/N/UV/tangent4 and vertex-red source-to-pale blend. Actual Create metrics and independent mesh readback retained; no family generation, whole-tree weld or visual acceptance." : ph01TangentMode ? "Scoped PH01 A/D current versus imported-tuple tangent comparison; same native roots, original scale, neutral illumination and fixed group cameras, normal map on/off. Optional original imported subsets have separately checked triangle count and mapped-corner coverage. No family generation, normal/UV flips, runtime substitution or visual acceptance." : ph02CrownMode ? "Scoped PH02 original/cut/attachment comparison at original metre scale with explicit source maps. The crown cut is open and leaf completeness/attachment remain unaccepted. No procedural family, native input, world integration or earlier numeric validation claim." : importedCandidateMode ? "Actual Unity URP GPU comparison of two unreviewed imported Poly Haven originals at imported scale with explicit source material maps. No procedural age/seed variation or full rubric-gate claim; no native input or desktop presentation." : "Actual Unity URP GPU renders of a procedural tree and small inspection/prototype stage. No full terrain, native input, desktop presentation, player save access or reference-image billboard.",
                mode = ph02ShotSelectionRequested ? "hybrid-ph02-fitted-crown-scoped-inspection" : ph02FamilyMode ? "hybrid-ph02-fitted-crown-family-experiment" : woodDiagnosticMode ? "r09-matched-wood-diagnostic" : ph02FittedMode ? "ph02-fitted-support-comparison" : ph01TangentMode ? "ph01-imported-tangent-comparison" : ph02CrownMode ? "ph02-source-crown-comparison" : playablePreviewMode ? "preacceptance-local-playable-preview-stage" : importedCandidateMode ? "imported-candidate-comparison" : hybridMode ? "hybrid-source-rosette-family-experiment" : "procedural-model-inspection",
                importedTupleChecks=importedTupleChecks,importedTupleFailureType=importedTupleFailureType,originalSubsetImagesIncluded=ph01OriginalSubsetsReady,
                ph02FittedChecks=ph02FitMetrics,ph02FittedReadback=ph02FitReadback,ph02FittedFailureType=ph02FitFailureType,
                ph02ImportedTuplesRequested=ph02ImportedTuplesRequested,ph02ImportedCrownChecks=ph02ImportedCrownChecks,ph02ImportedCrownMeshReadbackPassed=ph02ImportedCrownMeshReadbackPassed,
                ph02ImportedMeshExpandedSha256=ph02ImportedMeshExpandedSha256,ph02ReferenceCameraManifest=ph02ImportedTuplesRequested?PH02ReferenceCameraManifest:"",ph02ReferenceCameraManifestSha256=ph02ReferenceCameraManifestSha256,
                ph02FamilyChecks=ph02FamilyChecks,ph02FamilyMeshReadbackPassed=ph02FamilyMeshReadbackPassed,ph02FamilyMeshSha256=ph02FamilyMeshSha256,
                ph02FamilyFailureType=ph02FamilyFailureType,ph02FoliageTintStrength=ph02FoliageTint,
                ph02ShotSelectionRequested=ph02ShotSelectionRequested,ph02SelectedShotIds=ph02SelectedShotIds,
                initialAmbientProbe = initialAmbient,
                referenceDirection = "User-provided Kooker Nexus artwork; gold/ochre tree, teal succulent leaves, navy/cobalt sky. Earth explicitly excluded; one procedural blue gas giant only.",
                unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                renderPath = "URP SingleCameraRequest to GPU RenderTexture; synchronous RGB readback; editor batch process",
                timingScope = "Per-image render plus synchronous readback wall time after three warmup requests. Not gameplay FPS or a sustained runtime benchmark.",
                errors = errors, warnings = warnings, expectedCaptures = ExpectedCaptures, captures = results.ToArray()
            };
            if(playablePreviewMode&&ph02FamilyMode)
            {
                report.mode="frozen-r19-playable-preview-stage";
                report.status=passed?"Frozen R19 prebuild captures completed":"Technical rendering failure; see local editor log";
                report.scope="Two existing study views before a separately versioned player bake. Frozen R19 PH02 family, seed4242, foliage tint1. No new tree polishing, wider landscape or full-family review.";
                report.visualAcceptance="R19 review remains frozen at7.375/10; these captures establish build integration only. Native player evidence is separate.";
            }
            if(coastalMode)
            {
                report.mode="starfall-coastal-slice-first-composition";
                report.status=passed?"Actual Unity coastal comparison views completed":"Technical coastal rendering failure";
                report.scope="Separate coastal slice v1, terrain seed1904242, tree/rock seed4242, 180x200m. Provisional user concept reference; fixed side-view and element cameras. Frozen R19 tree, rocky bank, turquoise river opening toward sea, canyon terrain and procedural sky. No swimming, aquatic animals, saved world or native player acceptance.";
                report.visualAcceptance="First combined scene for proportional element and composition review; exact reference match and user acceptance are not established.";
            }
            File.WriteAllText(Path.Combine(outputDirectory, "metrics.json"), JsonUtility.ToJson(report, true));
        }

        private static void ObserveLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
            else if (type == LogType.Warning) warnings++;
        }

        private static void Cleanup()
        {
            try
            {
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (pipelineConfigured)
                {
                    GraphicsSettings.defaultRenderPipeline = previousGraphicsPipeline;
                    QualitySettings.renderPipeline = previousQualityPipeline;
                }
                for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
                if (inspectionPipeline != null) Object.DestroyImmediate(inspectionPipeline);
                if (inspectionRenderer != null) Object.DestroyImmediate(inspectionRenderer);
            }
            finally
            {
                if (pipelineConfigured)
                {
                    // URP's constructor writes these process-global settings, including the
                    // serialized quality MSAA value. Restore after temporary pipeline teardown.
                    QualitySettings.antiAliasing = previousAntiAliasing;
                    QualitySettings.enableLODCrossFade = previousLodCrossFade;
                    GraphicsSettings.useScriptableRenderPipelineBatching = previousSrpBatching;
                    GraphicsSettings.lightsUseLinearIntensity = previousLinearLightIntensity;
                    GraphicsSettings.lightsUseColorTemperature = previousColorTemperature;
                    ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
                    Debug.Log("KOKERBOOM_SETTINGS_RESTORED antiAliasing=" + QualitySettings.antiAliasing + " lodCrossFade=" + QualitySettings.enableLODCrossFade);
                }
            }
        }

        [Serializable] private sealed class Report
        {
            public PH01ImportedTupleCandidate.Report importedTupleChecks;
            public PH02FittedSupportCandidate.Report ph02FittedChecks;
            public PH02FamilyComponent.Report ph02FamilyChecks;
            public bool ph02FamilyMeshReadbackPassed;
            public string ph02FamilyMeshSha256,ph02FamilyFailureType;
            public float ph02FoliageTintStrength;
            public bool ph02ShotSelectionRequested;
            public string[] ph02SelectedShotIds;
            public bool ph02ImportedTuplesRequested,ph02ImportedCrownMeshReadbackPassed;
            public PH02ImportedCrownCandidate.Report ph02ImportedCrownChecks;
            public string ph02ImportedMeshExpandedSha256,ph02ReferenceCameraManifest,ph02ReferenceCameraManifestSha256;
            public PH02FitReadback ph02FittedReadback;
            public string ph02FittedFailureType;
            public string importedTupleFailureType;
            public bool originalSubsetImagesIncluded;
            public string schema, utc, status, visualAcceptance, scope, mode, referenceDirection, unityVersion, graphicsDevice, graphicsApi, renderPath, timingScope;
            public bool technicalChecksPassed;
            public int errors, warnings, expectedCaptures;
            public AmbientRecord initialAmbientProbe;
            public ShotRecord[] captures;
        }
        [Serializable] private sealed class ShotRecord
        {
            public string id, purpose, imagePath;
            public string comparisonGroup,comparisonVariant;
            public bool comparisonNormalMap;
            public int widthPixels, heightPixels;
            public double renderReadbackMilliseconds;
            public CameraRecord camera;
            public LightRecord[] lights;
            public Color ambientSky, ambientEquator, ambientGround;
            public AmbientRecord ambientProbeBeforeRendering, ambientProbeAfterRendering;
            public ImageRecord image;
            public SubjectRecord[] subjects;
        }
        [Serializable] private sealed class CameraRecord { public Vector3 position, rotationEuler; public bool orthographic; public float orthographicSizeMetres, fieldOfView, nearClip, farClip; }
        [Serializable] private sealed class LightRecord
        {
            public string name, type, shadows; public Vector3 position, rotationEuler; public Color color; public float intensity, shadowBias, shadowNormalBias;
            public static LightRecord From(Light light) => new LightRecord { name = light.name, type = light.type.ToString(), shadows = light.shadows.ToString(), position = light.transform.position, rotationEuler = light.transform.eulerAngles, color = light.color, intensity = light.intensity, shadowBias = light.shadowBias, shadowNormalBias = light.shadowNormalBias };
        }
        [Serializable] private sealed class ImageRecord { public int sampledPixels; public float minimumLuminance, maximumLuminance, meanLuminance; public bool nonUniformContent; }
        [Serializable] private sealed class SubjectRecord
        {
            public string name, kind, descriptorJson, sourceAssetPath, sourceAssetSha256, sourceNote;
            public bool proceduralIdentityApplicable, importerUseFileScale;
            public string[] originalMaterialNames;
            public int seed, lod, rendererCount, meshCount; public float age01, importerGlobalScale; public long vertices, triangles; public Vector3 position, scale; public Bounds bounds; public MeshRecord[] meshes;
        }
        [Serializable] private sealed class MeshRecord { public string name, objectName; public int vertices; public long triangles; public Bounds localBounds; public Vector3 objectWorldScale; public string[] materialNames, shaders; public MaterialRecord[] materials; }
        [Serializable] private sealed class MaterialRecord { public string name, shader; public TextureBinding[] bindings; }
        [Serializable] private sealed class TextureBinding { public string property, sourceAsset, runtimeTexture, conversion; }
        [Serializable] private sealed class AmbientRecord
        {
            public string scope, coefficientOrder = "RGB channels, nine SH coefficients each", directionOrder = "up,down,right,left,forward,back";
            public float[] coefficients;
            public Color[] evaluatedIrradiance;
            public static AmbientRecord From(SphericalHarmonicsL2 probe, string scope)
            {
                var record = new AmbientRecord { scope = scope, coefficients = new float[27], evaluatedIrradiance = new Color[6] };
                for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++) record.coefficients[c * 9 + i] = probe[c, i];
                probe.Evaluate(new[] { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back }, record.evaluatedIrradiance);
                return record;
            }
        }
    }
}
