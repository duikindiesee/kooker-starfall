using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using CityLife.World;

namespace CityLife.Items.Editor
{
    /// <summary>
    /// Focused additive test suite for Basket Slice B player integration contracts:
    /// 1. WovenBasketVisual procedural geometry bounds (<= 0.3x0.3x0.3m), collider isolation, mesh disposal.
    /// 2. PhysicalItemBootstrap opt-in fresh starter layout (basket + small objects) isolated from user default saves.
    /// 3. Authoritative slot projection, persistent empty hole display, and capacity enforcement via GetContainerSlotOccupants.
    /// 4. Transactional Store and Retrieve action execution with monotonic receipts and refusal invariants.
    /// 5. Distance and line-of-sight authority enforcement through real physical adapter.
    /// 6. Authoritative cold restore cleanly superseding fresh defaults without starter ID merging.
    /// 7. Normal save entry point producing valid, atomic, checksum-verified on-disk saves.
    /// 8. Safe default save path, rejected save byte preservation, unique collision-free recovery, and overwrite refusal.
    /// 9. Closed panel container selection separation from pickup selection (open basket -> close -> pick ruby).
    /// </summary>
    internal static class BasketPlayerIntegrationChecks
    {
        private static bool ByteArraysEqual(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        public static IEnumerator RunAsync(
            Action<bool, string, string> checkRaw,
            List<string> passed,
            Action<Action> setCleanup)
        {
            string lastFailedAssertion = null;
            void check(bool condition, string name, string diagnostic = null)
            {
                if (!condition)
                {
                    lastFailedAssertion = !string.IsNullOrEmpty(diagnostic)
                        ? $"BASKET PLAYER CHECK FAILED: {name} ({diagnostic})"
                        : $"BASKET PLAYER CHECK FAILED: {name}";
                }
                checkRaw(condition, name, diagnostic);
            }

            bool testCompletedSuccessfully = false;

            const string worldId = "starfall.physical-basket-player.v1";
            const string genId = "gen-01";
            const string agentId = "agent-starfall-01";

            var scene = SceneManager.CreateScene("BasketPlayerIntegration isolated scene", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var physics = scene.GetPhysicsScene();
            var owned = new List<GameObject>();

            GameObject LocalCreate(string name)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, scene);
                owned.Add(go);
                return go;
            }

            // Path tracking for robust cleanup
            string starterTempDir = null;
            string tempDir = null;
            string corruptDir = null;
            string customSaveDir = null;
            string recPath = null;
            string recPath2 = null;

            Keyboard keyboard = null;
            Mouse mouse = null;
            NpcPlayerControls controls = null;

            InputSettings liveSettings = null;
            bool hasCapturedSettings = false;
            InputSettings.UpdateMode originalUpdateMode = default;
            InputSettings.EditorInputBehaviorInPlayMode originalEditorBehavior = default;
            InputSettings.BackgroundBehavior originalBackgroundBehavior = default;
            Keyboard originalKeyboard = null;
            Mouse originalMouse = null;
            Action onBeforeUpdateHandler = null;
            Action onAfterUpdateHandler = null;
            Action activeTapObserver = null;
            Action<InputEventPtr, InputDevice> onEventHandler = null;
            int dynamicUpdateFrames = 0;
            int editorUpdateFrames = 0;
            InputUpdateType lastObservedPhase = InputUpdateType.None;
            int eventsObserved = 0;
            InputUpdateType lastEventPhase = InputUpdateType.None;
            string lastTapDiagnostic = null;

            var cleanupErrors = new List<string>();
            void RecordCleanupError(string diagnostic)
            {
                cleanupErrors.Add(diagnostic);
                Debug.LogWarning($"[BasketPlayerIntegrationChecks] Cleanup error: {diagnostic}");
            }

            var unexpectedLogs = new List<string>();
            void OnLogMessageReceived(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    // Precisely distinguish explicitly expected negative-test error logs:
                    // PhysicalItemBootstrap logs an Error when ExplicitSavePathOverride is not fully qualified:
                    // "[PhysicalItemBootstrap] Explicit save path override '{ExplicitSavePathOverride}' is not fully qualified and absolute. Halting save IO without fallback to user default."
                    if (!string.IsNullOrEmpty(condition) && condition.Contains("[PhysicalItemBootstrap]") &&
                        (condition.Contains("is not fully qualified and absolute") || condition.Contains("cannot be normalized") || condition.Contains("invalid-configured-save-path")))
                    {
                        return;
                    }

                    unexpectedLogs.Add($"[{type} in scene {scene.name}/{scene.handle}] {condition}\n{stackTrace}");
                }
            }

            Application.logMessageReceived += OnLogMessageReceived;

            void CleanupFixture()
            {
                cleanupErrors.Clear();

                try
                {
                    Application.logMessageReceived -= OnLogMessageReceived;
                }
                catch (Exception ex)
                {
                    RecordCleanupError($"Unsubscribe logMessageReceived: {ex.Message}");
                }

                if (activeTapObserver != null)
                {
                    try
                    {
                        InputSystem.onAfterUpdate -= activeTapObserver;
                        activeTapObserver = null;
                    }
                    catch (Exception ex)
                    {
                        RecordCleanupError($"Unsubscribe activeTapObserver: {ex.Message}");
                    }
                }

                if (onAfterUpdateHandler != null)
                {
                    try
                    {
                        InputSystem.onAfterUpdate -= onAfterUpdateHandler;
                        onAfterUpdateHandler = null;
                    }
                    catch (Exception ex)
                    {
                        RecordCleanupError($"Unsubscribe onAfterUpdateHandler: {ex.Message}");
                    }
                }

                if (onBeforeUpdateHandler != null)
                {
                    try
                    {
                        InputSystem.onBeforeUpdate -= onBeforeUpdateHandler;
                        onBeforeUpdateHandler = null;
                    }
                    catch (Exception ex)
                    {
                        RecordCleanupError($"Unsubscribe onBeforeUpdateHandler: {ex.Message}");
                    }
                }

                if (onEventHandler != null)
                {
                    try
                    {
                        InputSystem.onEvent -= onEventHandler;
                        onEventHandler = null;
                    }
                    catch (Exception ex)
                    {
                        RecordCleanupError($"Unsubscribe onEventHandler: {ex.Message}");
                    }
                }

                if (controls != null)
                {
                    try
                    {
                        controls.SuppressInput = true;
                        controls.TestKeyboard = null;
                        controls.TestMouse = null;
                    }
                    catch (Exception ex)
                    {
                        RecordCleanupError($"Reset controls: {ex.Message}");
                    }
                }

                if (keyboard != null)
                {
                    try
                    {
                        if (keyboard.added)
                        {
                            InputSystem.RemoveDevice(keyboard);
                            if (keyboard.added)
                            {
                                RecordCleanupError($"Failed to remove test keyboard '{keyboard.name}' (id={keyboard.deviceId}): device is still marked as added.");
                            }
                            else
                            {
                                keyboard = null;
                            }
                        }
                        else
                        {
                            keyboard = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        RecordCleanupError($"Exception removing test keyboard: {ex.Message}");
                    }
                }

                if (mouse != null)
                {
                    try
                    {
                        if (mouse.added)
                        {
                            InputSystem.RemoveDevice(mouse);
                            if (mouse.added)
                            {
                                RecordCleanupError($"Failed to remove test mouse '{mouse.name}' (id={mouse.deviceId}): device is still marked as added.");
                            }
                            else
                            {
                                mouse = null;
                            }
                        }
                        else
                        {
                            mouse = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        RecordCleanupError($"Exception removing test mouse: {ex.Message}");
                    }
                }

                if (originalKeyboard != null)
                {
                    try
                    {
                        if (originalKeyboard.added)
                        {
                            originalKeyboard.MakeCurrent();
                            if (Keyboard.current != originalKeyboard)
                            {
                                RecordCleanupError($"Failed to verify restoration of original keyboard '{originalKeyboard.name}': Keyboard.current is '{(Keyboard.current != null ? Keyboard.current.name : "null")}'.");
                            }
                            else
                            {
                                originalKeyboard = null;
                            }
                        }
                        else
                        {
                            originalKeyboard = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        RecordCleanupError($"Exception restoring original keyboard: {ex.Message}");
                    }
                }

                if (originalMouse != null)
                {
                    try
                    {
                        if (originalMouse.added)
                        {
                            originalMouse.MakeCurrent();
                            if (Mouse.current != originalMouse)
                            {
                                RecordCleanupError($"Failed to verify restoration of original mouse '{originalMouse.name}': Mouse.current is '{(Mouse.current != null ? Mouse.current.name : "null")}'.");
                            }
                            else
                            {
                                originalMouse = null;
                            }
                        }
                        else
                        {
                            originalMouse = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        RecordCleanupError($"Exception restoring original mouse: {ex.Message}");
                    }
                }

                if (hasCapturedSettings)
                {
                    try
                    {
                        if (liveSettings == null)
                        {
                            RecordCleanupError("Failed to restore InputSettings: liveSettings instance reference is null.");
                        }
                        else if (InputSystem.settings == null)
                        {
                            RecordCleanupError("Failed to restore InputSettings: InputSystem.settings is null; cannot verify live settings identity.");
                        }
                        else if (InputSystem.settings != liveSettings)
                        {
                            RecordCleanupError($"Failed to restore InputSettings identity: current={InputSystem.settings.name} (id={InputSystem.settings.GetEntityId()}), expected={liveSettings.name} (id={liveSettings.GetEntityId()})");
                        }
                        else
                        {
                            liveSettings.updateMode = originalUpdateMode;
                            liveSettings.editorInputBehaviorInPlayMode = originalEditorBehavior;
                            liveSettings.backgroundBehavior = originalBackgroundBehavior;

                            if (liveSettings.updateMode != originalUpdateMode ||
                                liveSettings.editorInputBehaviorInPlayMode != originalEditorBehavior ||
                                liveSettings.backgroundBehavior != originalBackgroundBehavior)
                            {
                                RecordCleanupError($"Failed to verify restoration of InputSettings properties: updateMode={liveSettings.updateMode} (expected {originalUpdateMode}), editorBehavior={liveSettings.editorInputBehaviorInPlayMode} (expected {originalEditorBehavior}), backgroundBehavior={liveSettings.backgroundBehavior} (expected {originalBackgroundBehavior})");
                            }
                            else
                            {
                                hasCapturedSettings = false;
                                liveSettings = null;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        RecordCleanupError($"Exception restoring InputSettings: {ex.Message}");
                    }
                }

                void SafeDeleteDir(string path, string label)
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        try
                        {
                            if (Directory.Exists(path)) Directory.Delete(path, true);
                        }
                        catch (Exception ex)
                        {
                            RecordCleanupError($"Delete {label} directory '{path}': {ex.Message}");
                        }
                    }
                }
                void SafeDeleteFile(string path, string label)
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        try
                        {
                            if (File.Exists(path)) File.Delete(path);
                        }
                        catch (Exception ex)
                        {
                            RecordCleanupError($"Delete {label} file '{path}': {ex.Message}");
                        }
                    }
                }

                SafeDeleteDir(starterTempDir, "starterTempDir");
                SafeDeleteDir(tempDir, "tempDir");
                SafeDeleteDir(corruptDir, "corruptDir");
                SafeDeleteDir(customSaveDir, "customSaveDir");
                SafeDeleteFile(recPath, "recPath");
                SafeDeleteFile(recPath2, "recPath2");

                for (int i = 0; i < owned.Count; i++)
                {
                    if (owned[i] != null)
                    {
                        try
                        {
                            UnityEngine.Object.DestroyImmediate(owned[i]);
                        }
                        catch (Exception ex)
                        {
                            RecordCleanupError($"Destroy owned GameObject '{owned[i]?.name}': {ex.Message}");
                        }
                    }
                }
                owned.Clear();

                if (scene.IsValid())
                {
                    try
                    {
                        SceneManager.UnloadSceneAsync(scene);
                    }
                    catch (Exception ex)
                    {
                        RecordCleanupError($"Unload scene '{scene.name}': {ex.Message}");
                    }
                }

                if (cleanupErrors.Count > 0)
                {
                    string combinedErrors = string.Join("\n", cleanupErrors);
                    cleanupErrors.Clear();
                    if (testCompletedSuccessfully)
                    {
                        throw new InvalidOperationException(
                            $"[BasketPlayerIntegrationChecks] Cleanup failed with {combinedErrors}");
                    }
                    else if (lastFailedAssertion != null)
                    {
                        throw new InvalidOperationException(
                            $"{lastFailedAssertion}\n[Additional Cleanup Errors]:\n{combinedErrors}");
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"[BasketPlayerIntegrationChecks] Test failed and cleanup failed:\n{combinedErrors}");
                    }
                }
            }

            setCleanup(CleanupFixture);

            try
            {
                // Setup isolated static ground floor
                var floor = LocalCreate("test-floor");
                floor.layer = 8;
                floor.transform.position = new Vector3(0, -0.5f, 0);
                floor.transform.localScale = new Vector3(100f, 1f, 100f);
                floor.AddComponent<BoxCollider>();

                // -------------------------------------------------------------
                // 1. WovenBasketVisual Procedural Geometry Contracts & Disposal
                // -------------------------------------------------------------
                var basketParent = LocalCreate("basket-visual-test-root");
                var boxCol = basketParent.AddComponent<BoxCollider>();
                boxCol.size = new Vector3(0.3f, 0.3f, 0.3f);
                boxCol.center = Vector3.zero;

                var testMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                var visualGo = WovenBasketVisual.CreateVisual(basketParent.transform, testMat, new Vector3(0.3f, 0.3f, 0.3f));
                var visualComp = visualGo.GetComponent<WovenBasketVisual>();

                check(visualComp != null, "basket-player-visual-component-present");
                check(visualComp.GeneratedMesh != null, "basket-player-mesh-generated");

                var mesh = visualComp.GeneratedMesh;
                check(mesh.vertexCount > 50, "basket-player-mesh-has-vertices", $"vertexCount={mesh.vertexCount}");
                check(mesh.triangles.Length > 50, "basket-player-mesh-has-triangles", $"triangles={mesh.triangles.Length}");

                // Strict catalog dimension adherence: must fit within 0.3 x 0.3 x 0.3 m
                var bounds = mesh.bounds;
                check(bounds.size.x <= 0.301f, "basket-player-mesh-width-within-catalog", $"sizeX={bounds.size.x:F4}");
                check(bounds.size.y <= 0.301f, "basket-player-mesh-height-within-catalog", $"sizeY={bounds.size.y:F4}");
                check(bounds.size.z <= 0.301f, "basket-player-mesh-depth-within-catalog", $"sizeZ={bounds.size.z:F4}");

                // Strict collider ownership: visual child MUST NOT own any collider
                var visualColliders = visualGo.GetComponentsInChildren<Collider>(true);
                check(visualColliders.Length == 0, "basket-player-visual-has-zero-colliders", $"colliders={visualColliders.Length}");

                // Strict component ownership: visual child MUST NOT own PhysicalItem or Rigidbody
                check(visualGo.GetComponent<PhysicalItem>() == null, "basket-player-visual-no-duplicate-item");
                check(visualGo.GetComponent<Rigidbody>() == null, "basket-player-visual-no-duplicate-body");

                // Scale-neutral transform
                check(Vector3.Distance(visualGo.transform.localScale, Vector3.one) <= 0.001f, "basket-player-visual-unit-scale");

                // Mesh disposal verification on destroy
                var meshRoot = LocalCreate("basket-mesh-disposal-root");
                var dispVisualGo = WovenBasketVisual.CreateVisual(meshRoot.transform, testMat, new Vector3(0.3f, 0.3f, 0.3f));
                var dispVisualComp = dispVisualGo.GetComponent<WovenBasketVisual>();
                var genMesh = dispVisualComp.GeneratedMesh;
                check(genMesh != null, "basket-player-mesh-disposal-precondition");
                UnityEngine.Object.DestroyImmediate(dispVisualGo);
                check(genMesh == null || !dispVisualGo, "basket-player-mesh-disposed-on-destroy");

                // -------------------------------------------------------------
                // 2. PhysicalItemBootstrap Opt-In Starter Layout (Isolated Test Path)
                // -------------------------------------------------------------
                starterTempDir = Path.Combine(Application.temporaryCachePath, "BasketStarter_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(starterTempDir);
                string starterIsolatedPath = Path.Combine(starterTempDir, "starter-isolated.json");

                var starterGo = LocalCreate("test-starter-bootstrap-actor");
                starterGo.SetActive(false);
                starterGo.transform.position = new Vector3(10f, 0f, 10f);
                var starterBrain = starterGo.AddComponent<NpcAutonomy>();
                starterBrain.InstanceWorldId = worldId;
                starterBrain.ManualSimulation = true;
                starterBrain.Running = false;
                var starterLog = starterGo.AddComponent<NpcDecisionLog>();
                starterBrain.Log = starterLog;
                var starterPerception = starterGo.AddComponent<NpcPerception>();
                starterPerception.WorldId = worldId;
                starterBrain.Perception = starterPerception;

                var starterBootstrap = starterGo.AddComponent<PhysicalItemBootstrap>();
                starterBootstrap.DemonstrationMaterial = testMat;
                starterBootstrap.BasketMaterial = testMat;
                starterBootstrap.OptInStarterLayout = true;
                starterBootstrap.ExplicitSavePathOverride = starterIsolatedPath;
                starterBootstrap.Brain = starterBrain;
                starterBrain.PhysicalItems = starterBootstrap;
                starterGo.SetActive(true);

                var catalog = PhysicalItemCatalog.CreateDefaultCatalog();
                check(catalog.Contains("container-basket"), "basket-player-catalog-has-basket");
                check(catalog.TryGet("container-basket", out var basketDef), "basket-player-catalog-get-basket");
                check(basketDef.maxContainedSlots == 4, "basket-player-basket-has-4-slots");
                check(Mathf.Abs(basketDef.maxContainedMassKg - 10f) <= 0.0001f, "basket-player-basket-10kg-limit");

                check(starterBootstrap.Model != null && starterBootstrap.Model.ItemCount == 6, "basket-player-bootstrap-starter-model-count",
                    $"count={starterBootstrap.Model?.ItemCount}");
                check(starterBootstrap.Model.TryGetItem("canyon-basket-01", out var starterBasketSnap) && starterBasketSnap.location == ItemLocationKind.Free,
                    "basket-player-bootstrap-basket-created");

                // -------------------------------------------------------------
                // Setup: Coherent Single-Graph Fixture (Actor, Brain, Bootstrap, HUD, Controls, Panel)
                // -------------------------------------------------------------
                var actorGo = LocalCreate("fixture-actor");
                actorGo.SetActive(false);
                actorGo.transform.position = Vector3.zero;
                actorGo.transform.rotation = Quaternion.identity;

                var capsule = actorGo.AddComponent<CharacterController>();
                capsule.height = 1.85f; capsule.center = new Vector3(0, 0.93f, 0); capsule.radius = 0.3f;

                var previewActor = actorGo.AddComponent<CharacterPreviewActor>();
                previewActor.ExternalDrive = true;
                previewActor.Capsule = capsule;

                var bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CityLife.World.Editor.CharacterAssetImport.Body);
                if (bodyPrefab != null)
                {
                    var bodyInstance = UnityEngine.Object.Instantiate(bodyPrefab, actorGo.transform);
                    bodyInstance.transform.localPosition = Vector3.zero;
                    bodyInstance.transform.localRotation = Quaternion.identity;
                    owned.Add(bodyInstance);
                    previewActor.Animator = bodyInstance.GetComponent<Animator>();
                }
                check(previewActor.Animator != null, "basket-player-animator-present");

                Transform genuineHand = previewActor.Animator != null ? previewActor.Animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
                check(genuineHand != null, "basket-player-humanoid-hand-present");

                var brain = actorGo.AddComponent<NpcAutonomy>();
                brain.Actor = previewActor;
                brain.InstanceWorldId = worldId;
                brain.SpawnPosition = actorGo.transform.position;
                brain.Log = actorGo.AddComponent<NpcDecisionLog>();
                var perception = actorGo.AddComponent<NpcPerception>();
                perception.WorldId = worldId;
                brain.Perception = perception;
                brain.ManualSimulation = true;

                var bootstrap = actorGo.AddComponent<PhysicalItemBootstrap>();
                bootstrap.DemonstrationMaterial = testMat;
                bootstrap.BasketMaterial = testMat;
                bootstrap.OptInStarterLayout = false;
                string fixtureIsolatedPath = Path.Combine(starterTempDir, "fixture-save.json");
                bootstrap.ExplicitSavePathOverride = fixtureIsolatedPath;
                bootstrap.Brain = brain;
                brain.PhysicalItems = bootstrap;

                var cameraGo = LocalCreate("fixture-camera");
                cameraGo.transform.SetParent(actorGo.transform, false);
                cameraGo.transform.localPosition = new Vector3(0f, 1.5f, -2f);
                cameraGo.transform.localRotation = Quaternion.identity;
                var camera = cameraGo.AddComponent<Camera>();

                var previewCam = cameraGo.AddComponent<CharacterPreviewCamera>();
                previewCam.Target = actorGo.transform;
                previewActor.View = previewCam;

                var display = cameraGo.AddComponent<PreviewDisplayMode>();
                display.MenuOnly = true;

                var hud = cameraGo.AddComponent<NpcDecisionHud>();
                hud.Brain = brain;
                hud.View = camera;

                controls = actorGo.AddComponent<NpcPlayerControls>();
                controls.Brain = brain;
                controls.Hud = hud;
                controls.View = previewCam;
                controls.Display = display;
                controls.AllowUnfocusedTestInput = true;
                hud.Controls = controls;

                var panel = actorGo.AddComponent<PhysicalContainerPanel>();
                panel.Controls = controls;
                panel.Bootstrap = bootstrap;
                controls.ContainerPanel = panel;

                // Scoped reversible installed InputSystem settings:
                // Snapshot exactly 3 modified properties on the SAME live instance, apply test values,
                // and restore them in finally without settings swaps, clones, destruction, or null fallbacks.
                liveSettings = InputSystem.settings;
                if (liveSettings == null)
                {
                    string diag = "InputSystem.settings is null before test setup; failclosed on missing original settings instance.";
                    check(false, "basket-player-input-settings-present", diag);
                    throw new InvalidOperationException(diag);
                }

                originalUpdateMode = liveSettings.updateMode;
                originalEditorBehavior = liveSettings.editorInputBehaviorInPlayMode;
                originalBackgroundBehavior = liveSettings.backgroundBehavior;
                hasCapturedSettings = true;

                liveSettings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
                liveSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                liveSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

                originalKeyboard = Keyboard.current;
                originalMouse = Mouse.current;

                onBeforeUpdateHandler = () =>
                {
                    var phase = InputState.currentUpdateType;
                    lastObservedPhase = phase;
                    if ((phase & InputUpdateType.Editor) != 0)
                        editorUpdateFrames++;
                };

                onAfterUpdateHandler = () =>
                {
                    var phase = InputState.currentUpdateType;
                    lastObservedPhase = phase;
                    if ((phase & InputUpdateType.Dynamic) != 0)
                        dynamicUpdateFrames++;
                };

                onEventHandler = (eventPtr, device) =>
                {
                    if (device == keyboard)
                    {
                        eventsObserved++;
                        lastEventPhase = InputState.currentUpdateType;
                    }
                };

                InputSystem.onBeforeUpdate += onBeforeUpdateHandler;
                InputSystem.onAfterUpdate += onAfterUpdateHandler;
                InputSystem.onEvent += onEventHandler;

                keyboard = InputSystem.AddDevice<Keyboard>("BasketIntegrationKeyboard");
                mouse = InputSystem.AddDevice<Mouse>("BasketIntegrationMouse");
                keyboard.MakeCurrent();
                mouse.MakeCurrent();
                controls.TestKeyboard = keyboard;
                controls.TestMouse = mouse;
                controls.SuppressInput = false;

                actorGo.SetActive(true);

                bootstrap.Model.SetActorCarryLimits(agentId, new ActorCarryLimits(25f, 1));
                bootstrap.Model.SetActorCarryLimits(NpcAutonomy.AgentId, new ActorCarryLimits(25f, 1));

                // Authoritative Item Creation Helper on Bootstrap:
                // Physical authority enters strictly through bootstrap bindings only, NEVER brain.Registry!
                NpcInteractable CreateItem(string itemId, string itemTypeId, Vector3 worldPos, PhysicalDimensions dims, float mass)
                {
                    var go = LocalCreate(itemId);
                    go.transform.position = worldPos;
                    var col = go.AddComponent<BoxCollider>();
                    col.size = new Vector3(dims.width, dims.height, dims.depth);
                    var rb = go.AddComponent<Rigidbody>();
                    rb.mass = mass;
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                    if (string.Equals(itemTypeId, "container-basket", StringComparison.Ordinal))
                    {
                        WovenBasketVisual.CreateVisual(go.transform, testMat, new Vector3(dims.width, dims.height, dims.depth));
                    }
                    else
                    {
                        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        cube.name = "Visual";
                        var cCol = cube.GetComponent<Collider>();
                        if (cCol != null) UnityEngine.Object.DestroyImmediate(cCol);
                        cube.transform.SetParent(go.transform, false);
                        cube.transform.localScale = new Vector3(dims.width, dims.height, dims.depth);
                        var rend = cube.GetComponent<MeshRenderer>();
                        if (rend != null) rend.sharedMaterial = testMat;
                    }

                    var ni = go.AddComponent<NpcInteractable>();
                    ni.StableId = itemId;
                    ni.WorldId = worldId;
                    ni.Kind = NpcObjectKind.Item;
                    ni.Permission = true;
                    var app = LocalCreate(itemId + "-approach");
                    app.transform.SetParent(go.transform, false);
                    app.transform.localPosition = Vector3.zero;
                    ni.Approach = app.transform;

                    var phys = go.AddComponent<PhysicalItem>();
                    phys.itemId = itemId;
                    phys.itemTypeId = itemTypeId;
                    phys.massKg = mass;
                    phys.dimensions = dims;
                    phys.ConfigureComponents();
                    phys.Bind(bootstrap.Model, worldId, bootstrap.Model.GenerationId);

                    bootstrap.Model.RegisterItem(itemId, itemTypeId, ItemLocationKind.Free, worldPos, Quaternion.identity);
                    var binding = new PhysicalItemRuntimeBinding(itemId, phys, ni);
                    bootstrap.RegisterBinding(binding);
                    return ni;
                }

                // 1. basket-01 (in reach: ~0.47m)
                var basketNi = CreateItem("basket-01", "container-basket",
                    actorGo.transform.position + new Vector3(0f, 0.15f, 0.45f),
                    new PhysicalDimensions(0.3f, 0.3f, 0.3f), 1.0f);
                var basketPhys = basketNi.GetComponent<PhysicalItem>();

                // 2. ruby-01 (in reach: ~0.43m)
                var rubyNi = CreateItem("ruby-01", "gem-ruby",
                    actorGo.transform.position + new Vector3(0.15f, 0.05f, 0.4f),
                    new PhysicalDimensions(0.02f, 0.02f, 0.02f), 0.1f);
                var rubyPhys = rubyNi.GetComponent<PhysicalItem>();

                // 3. chisel-01 (in reach: ~0.43m)
                var chiselNi = CreateItem("chisel-01", "tool-chisel",
                    actorGo.transform.position + new Vector3(-0.15f, 0.05f, 0.4f),
                    new PhysicalDimensions(0.1f, 0.05f, 0.05f), 0.5f);
                var chiselPhys = chiselNi.GetComponent<PhysicalItem>();

                // 4. ruby-02 (in reach: ~0.49m)
                var ruby2Ni = CreateItem("ruby-02", "gem-ruby",
                    actorGo.transform.position + new Vector3(0.2f, 0.05f, 0.45f),
                    new PhysicalDimensions(0.02f, 0.02f, 0.02f), 0.1f);
                var ruby2Phys = ruby2Ni.GetComponent<PhysicalItem>();

                // 5. chisel-02 (in reach: ~0.51m)
                var chisel2Ni = CreateItem("chisel-02", "tool-chisel",
                    actorGo.transform.position + new Vector3(-0.25f, 0.05f, 0.45f),
                    new PhysicalDimensions(0.1f, 0.05f, 0.05f), 0.5f);
                var chisel2Phys = chisel2Ni.GetComponent<PhysicalItem>();

                // 6. chisel-overflow (in reach: ~0.57m)
                var overflowNi = CreateItem("chisel-overflow", "tool-chisel",
                    actorGo.transform.position + new Vector3(-0.35f, 0.05f, 0.45f),
                    new PhysicalDimensions(0.1f, 0.05f, 0.05f), 0.5f);
                var overflowPhys = overflowNi.GetComponent<PhysicalItem>();

                // 7. far-item (out of reach: ~4.95m)
                var farNi = CreateItem("far-item", "gem-ruby",
                    actorGo.transform.position + new Vector3(3.5f, 0.05f, 3.5f),
                    new PhysicalDimensions(0.02f, 0.02f, 0.02f), 0.1f);

                // 8. los-blocked-item behind los-wall (approach ~0.46m <= 0.65m, sight blocked by wall on layer 8)
                var losBlockedNi = CreateItem("los-blocked-item", "gem-ruby",
                    actorGo.transform.position + new Vector3(0.0f, 0.1f, 0.45f),
                    new PhysicalDimensions(0.02f, 0.02f, 0.02f), 0.1f);

                var losWall = LocalCreate("los-wall");
                losWall.SetActive(false);
                losWall.layer = 8;
                losWall.transform.position = actorGo.transform.position + new Vector3(0.0f, 0.5f, 0.25f);
                losWall.transform.localScale = new Vector3(1.0f, 1.0f, 0.1f);
                losWall.AddComponent<BoxCollider>();

                void SettlePhysics(PhysicalItem phys = null, int maxSteps = 100)
                {
                    for (int i = 0; i < maxSteps; i++)
                    {
                        physics.Simulate(0.02f);
                        Physics.SyncTransforms();
                        bootstrap.SyncFreeItems();
                        if (phys != null && phys.Body != null && i >= 15)
                        {
                            if (phys.Body.linearVelocity.magnitude <= 0.03f && phys.Body.angularVelocity.magnitude <= 0.052f)
                            {
                                break;
                            }
                        }
                    }
                }

                void EnsureReachableApproach(NpcInteractable target, float maxReach = 0.60f, float desiredDistance = 0.48f, int maxSteps = 40)
                {
                    if (target == null || target.Approach == null) return;
                    Physics.SyncTransforms();
                    float curDist = Vector3.Distance(actorGo.transform.position, target.Approach.position);
                    if (curDist > maxReach)
                    {
                        for (int step = 0; step < maxSteps; step++)
                        {
                            curDist = Vector3.Distance(actorGo.transform.position, target.Approach.position);
                            if (curDist <= desiredDistance) break;
                            Vector3 toTarget = (target.Approach.position - actorGo.transform.position);
                            toTarget.y = 0;
                            if (toTarget.sqrMagnitude < 0.0001f) break;
                            Vector3 dir = toTarget.normalized;
                            previewActor.Step(dir, 0.02f);
                            physics.Simulate(0.02f);
                            Physics.SyncTransforms();
                            bootstrap.SyncFreeItems();
                        }
                    }
                }

                bool CheckItemSightGate(NpcInteractable target, Vector3 actorPosition, out string sightDiagnostic)
                {
                    sightDiagnostic = null;
                    if (target == null)
                    {
                        sightDiagnostic = "target-null";
                        return false;
                    }
                    float sightDist = Vector3.Distance(actorPosition + Vector3.up, target.SightPoint);
                    if (sightDist > 1.7f)
                    {
                        sightDiagnostic = $"sight-dist-exceeded({sightDist:F3}m > 1.7m)";
                        return false;
                    }
                    Vector3 eye = actorPosition + Vector3.up * 1.6f;
                    Vector3 delta = target.SightPoint - eye;
                    float dist = delta.magnitude;
                    if (dist > 0.001f)
                    {
                        var ps = scene.GetPhysicsScene();
                        bool hitSomething = ps.IsValid()
                            ? ps.Raycast(eye, delta.normalized, out var hit, dist, (1 << 8) | (1 << 10), QueryTriggerInteraction.Ignore)
                            : Physics.Raycast(eye, delta.normalized, out hit, dist, (1 << 8) | (1 << 10), QueryTriggerInteraction.Ignore);
                        if (hitSomething && hit.collider != null)
                        {
                            Transform hitT = hit.collider.transform;
                            if (hitT != target.transform && !hitT.IsChildOf(target.transform) &&
                                hitT != actorGo.transform && !hitT.IsChildOf(actorGo.transform))
                            {
                                sightDiagnostic = $"los-blocked-by({hit.collider.name}, layer={hit.collider.gameObject.layer})";
                                return false;
                            }
                        }
                    }
                    return true;
                }

                string FormatSimultaneousDiagnostic(string label, NpcInteractable a, PhysicalItem physA, NpcInteractable b, PhysicalItem physB)
                {
                    Vector3 actPos = actorGo.transform.position;
                    Vector3 posA = a != null ? a.transform.position : Vector3.zero;
                    Vector3 appA = a != null && a.Approach != null ? a.Approach.position : Vector3.zero;
                    float distA = a != null && a.Approach != null ? Vector3.Distance(actPos, appA) : -1f;
                    Vector3 velA = physA != null && physA.Body != null ? physA.Body.linearVelocity : Vector3.zero;
                    string snapA = "none";
                    if (a != null && bootstrap.Model != null && bootstrap.Model.TryGetItem(a.StableId, out var sA))
                        snapA = $"loc={sA.location},pos={sA.position:F3}";

                    Vector3 posB = b != null ? b.transform.position : Vector3.zero;
                    Vector3 appB = b != null && b.Approach != null ? b.Approach.position : Vector3.zero;
                    float distB = b != null && b.Approach != null ? Vector3.Distance(actPos, appB) : -1f;
                    Vector3 velB = physB != null && physB.Body != null ? physB.Body.linearVelocity : Vector3.zero;
                    string snapB = "none";
                    if (b != null && bootstrap.Model != null && bootstrap.Model.TryGetItem(b.StableId, out var sB))
                        snapB = $"loc={sB.location},pos={sB.position:F3}";

                    float sightDistA = a != null ? Vector3.Distance(actPos + Vector3.up, a.SightPoint) : -1f;
                    float sightDistB = b != null ? Vector3.Distance(actPos + Vector3.up, b.SightPoint) : -1f;
                    bool sightPassA = CheckItemSightGate(a, actPos, out string sDiagA);
                    bool sightPassB = CheckItemSightGate(b, actPos, out string sDiagB);
                    float pairDist = (a != null && a.Approach != null && b != null && b.Approach != null)
                        ? Vector3.Distance(a.Approach.position, b.Approach.position) : -1f;

                    return $"{label}[actPos={actPos:F3},actSpeed={previewActor.ActualSpeed:F3},pairDist={pairDist:F3}," +
                           $"itemA={a?.StableId ?? "null"},posA={posA:F3},appA={appA:F3},distA={distA:F3},sightDistA={sightDistA:F3},sightA={sightPassA}({sDiagA ?? "ok"}),velA={velA:F3},{snapA}," +
                           $"itemB={b?.StableId ?? "null"},posB={posB:F3},appB={appB:F3},distB={distB:F3},sightDistB={sightDistB:F3},sightB={sightPassB}({sDiagB ?? "ok"}),velB={velB:F3},{snapB}]";
                }

                void EnsureSimultaneousReach(NpcInteractable targetA, PhysicalItem physA, NpcInteractable targetB, PhysicalItem physB, int maxSteps = 60)
                {
                    if (targetA == null || targetA.Approach == null || targetB == null || targetB.Approach == null)
                    {
                        string nullDiag = $"Target or Approach is null (targetA={(targetA != null ? targetA.name : "null")}, targetB={(targetB != null ? targetB.name : "null")})";
                        check(false, "basket-player-simultaneous-reach-valid-targets", nullDiag);
                        throw new InvalidOperationException(nullDiag);
                    }

                    Physics.SyncTransforms();
                    Vector3 posA = targetA.Approach.position;
                    Vector3 posB = targetB.Approach.position;
                    float pairDistance = Vector3.Distance(posA, posB);

                    // Geometrical infeasibility check: if approaches are further apart than 2 * 0.65m, no point can reach both
                    if (pairDistance > 1.30f)
                    {
                        string infeasibleDiag = FormatSimultaneousDiagnostic("pairInfeasible", targetA, physA, targetB, physB);
                        check(false, "basket-player-simultaneous-reach-geometry-feasible", infeasibleDiag);
                        throw new InvalidOperationException($"Pair geometry infeasible: distance {pairDistance:F3}m > 1.30m max reach span: {infeasibleDiag}");
                    }

                    // Determine target common position in horizontal plane
                    Vector2 a2 = new Vector2(posA.x, posA.z);
                    Vector2 b2 = new Vector2(posB.x, posB.z);
                    Vector2 mid2 = (a2 + b2) * 0.5f;
                    Vector2 seg = b2 - a2;
                    Vector2 perp = new Vector2(-seg.y, seg.x);
                    if (perp.sqrMagnitude < 0.0001f) perp = Vector2.up;
                    perp = perp.normalized;

                    Vector2 act2 = new Vector2(actorGo.transform.position.x, actorGo.transform.position.z);
                    if (Vector2.Dot(act2 - mid2, perp) < 0f) perp = -perp;

                    bool IsCandidateCollisionFree(Vector3 candPos)
                    {
                        float halfHeight = capsule.height * 0.5f;
                        float r = capsule.radius;
                        float cylinderHalf = Mathf.Max(0f, halfHeight - r);
                        Vector3 pBottom = candPos + capsule.center - Vector3.up * cylinderHalf;
                        Vector3 pTop = candPos + capsule.center + Vector3.up * cylinderHalf;

                        Collider[] hits = new Collider[32];
                        int count = physics.IsValid()
                            ? physics.OverlapCapsule(pBottom, pTop, r * 0.95f, hits, ~0, QueryTriggerInteraction.Ignore)
                            : Physics.OverlapCapsuleNonAlloc(pBottom, pTop, r * 0.95f, hits, ~0, QueryTriggerInteraction.Ignore);

                        for (int i = 0; i < count; i++)
                        {
                            var col = hits[i];
                            if (col == null || !col.enabled || col.isTrigger) continue;
                            if (col.transform == actorGo.transform || col.transform.IsChildOf(actorGo.transform)) continue;
                            if (col.gameObject == floor || col.transform.IsChildOf(floor.transform)) continue;
                            if (col.gameObject == basketParent || col.transform.IsChildOf(basketParent.transform) || col.gameObject.name.Contains("visual-test-root")) continue;
                            return false;
                        }
                        return true;
                    }

                    // Test standoff distances to find best candidate that satisfies <= 0.60m to both
                    float bestMaxDist = float.MaxValue;
                    Vector2 bestTarget2 = mid2;
                    bool foundFeasible = false;
                    float fallbackMinDist = float.MaxValue;
                    Vector2 fallbackTarget2 = mid2;
                    float[] standoffs = {
                        0.0f,
                        0.02f, -0.02f,
                        0.05f, -0.05f,
                        0.08f, -0.08f,
                        0.10f, -0.10f,
                        0.15f, -0.15f,
                        0.20f, -0.20f,
                        0.25f, -0.25f,
                        0.30f, -0.30f,
                        0.35f, -0.35f,
                        0.38f, -0.38f,
                        0.40f, -0.40f,
                        0.42f, -0.42f,
                        0.44f, -0.44f,
                        0.46f, -0.46f,
                        0.48f, -0.48f,
                        0.50f, -0.50f,
                        0.52f, -0.52f
                    };
                    for (int s = 0; s < standoffs.Length; s++)
                    {
                        Vector2 cand2 = mid2 + perp * standoffs[s];
                        Vector3 candPos = new Vector3(cand2.x, actorGo.transform.position.y, cand2.y);
                        float dA = Vector3.Distance(candPos, posA);
                        float dB = Vector3.Distance(candPos, posB);
                        float maxD = Mathf.Max(dA, dB);

                        if (maxD < fallbackMinDist)
                        {
                            fallbackMinDist = maxD;
                            fallbackTarget2 = cand2;
                        }

                        if (maxD > 0.65f) continue;
                        if (!CheckItemSightGate(targetA, candPos, out _)) continue;
                        if (!CheckItemSightGate(targetB, candPos, out _)) continue;
                        if (!IsCandidateCollisionFree(candPos)) continue;

                        if (maxD < bestMaxDist)
                        {
                            bestMaxDist = maxD;
                            bestTarget2 = cand2;
                            foundFeasible = true;
                        }
                    }

                    if (!foundFeasible)
                    {
                        bestTarget2 = fallbackTarget2;
                    }

                    Vector3 targetPos = new Vector3(bestTarget2.x, actorGo.transform.position.y, bestTarget2.y);

                    // Bounded real actor movement
                    for (int step = 0; step < maxSteps; step++)
                    {
                        Physics.SyncTransforms();
                        float curDistA = Vector3.Distance(actorGo.transform.position, targetA.Approach.position);
                        float curDistB = Vector3.Distance(actorGo.transform.position, targetB.Approach.position);
                        bool curSightA = CheckItemSightGate(targetA, actorGo.transform.position, out _);
                        bool curSightB = CheckItemSightGate(targetB, actorGo.transform.position, out _);

                        if (curDistA <= 0.60f && curDistB <= 0.60f && curSightA && curSightB)
                        {
                            break;
                        }

                        Vector3 toTarget = targetPos - actorGo.transform.position;
                        toTarget.y = 0;
                        if (toTarget.sqrMagnitude < 0.0004f)
                        {
                            if (curDistA <= 0.65f && curDistB <= 0.65f && curSightA && curSightB)
                                break;
                        }

                        Vector3 moveDir = toTarget.sqrMagnitude >= 0.0004f ? toTarget.normalized : Vector3.zero;
                        if (moveDir.sqrMagnitude > 0.0001f)
                        {
                            previewActor.Step(moveDir, 0.02f);
                            physics.Simulate(0.02f);
                            Physics.SyncTransforms();
                            bootstrap.SyncFreeItems();
                        }
                        else
                        {
                            break;
                        }
                    }

                    // Verified recheck after last movement
                    Physics.SyncTransforms();
                    float postDistA = Vector3.Distance(actorGo.transform.position, targetA.Approach.position);
                    float postDistB = Vector3.Distance(actorGo.transform.position, targetB.Approach.position);
                    bool postSightA = CheckItemSightGate(targetA, actorGo.transform.position, out string postSightDiagA);
                    bool postSightB = CheckItemSightGate(targetB, actorGo.transform.position, out string postSightDiagB);

                    bool reached = postDistA <= 0.65f && postDistB <= 0.65f && postSightA && postSightB;
                    string postDiag = FormatSimultaneousDiagnostic("postSimultaneousReach", targetA, physA, targetB, physB);
                    check(reached, "basket-player-simultaneous-reach-acquired", postDiag);
                    if (!reached)
                    {
                        throw new InvalidOperationException($"Simultaneous reach failed to acquire feasible common position: {postDiag}");
                    }
                }

                string FormatItemDiagnostic(string stepLabel, NpcActionResult result, NpcInteractable item, PhysicalItem phys, NpcInteractable container = null)
                {
                    string heldId = brain.Actions != null && brain.Actions.Held != null ? brain.Actions.Held.StableId : "none";
                    string code = result.code ?? "null";
                    string succ = result.success ? "true" : "false";
                    string dup = result.duplicate ? "true" : "false";
                    string snapInfo = "no-snap";
                    if (item != null && bootstrap != null && bootstrap.Model != null && bootstrap.Model.TryGetItem(item.StableId, out var snap))
                    {
                        snapInfo = $"loc={snap.location},holder={snap.holderActorId ?? "none"},cont={snap.containerItemId ?? "none"},slot={snap.containerSlot},snapPos={snap.position:F3}";
                    }
                    Vector3 actPos = actorGo.transform.position;
                    Vector3 hPos = genuineHand != null ? genuineHand.position : Vector3.zero;
                    Vector3 itemPos = item != null ? item.transform.position : Vector3.zero;
                    Vector3 appPos = item != null && item.Approach != null ? item.Approach.position : Vector3.zero;
                    float appDist = item != null && item.Approach != null ? Vector3.Distance(actPos, item.Approach.position) : -1f;
                    float sightDist = item != null ? Vector3.Distance(actPos + Vector3.up, item.SightPoint) : -1f;
                    Vector3 vel = phys != null && phys.Body != null ? phys.Body.linearVelocity : Vector3.zero;
                    string contInfo = "";
                    if (container != null)
                    {
                        float cAppDist = container.Approach != null ? Vector3.Distance(actPos, container.Approach.position) : -1f;
                        contInfo = $",contId={container.StableId},cAppDist={cAppDist:F3}";
                    }
                    int itemScene = item != null && item.gameObject.scene.IsValid() ? (int)item.gameObject.scene.handle.GetRawData() : -1;
                    int actScene = actorGo.scene.IsValid() ? (int)actorGo.scene.handle.GetRawData() : -1;
                    return $"{stepLabel}[succ={succ},code={code},dup={dup},held={heldId},{snapInfo},actPos={actPos:F3},hPos={hPos:F3},itemPos={itemPos:F3},appPos={appPos:F3},appDist={appDist:F3},sightDist={sightDist:F3},vel={vel:F3},wId={item?.WorldId ?? "none"},actScn={actScene},itemScn={itemScene}{contInfo}]";
                }

                void AssertNoUnexpectedLogs(string phase)
                {
                    if (unexpectedLogs.Count > 0)
                    {
                        string allErrors = string.Join("\n---\n", unexpectedLogs);
                        unexpectedLogs.Clear();
                        check(false, $"basket-player-no-unexpected-exceptions-{phase}", allErrors);
                    }
                }

                // Authoritative single-registry invariant:
                // brain.Registry holds ONLY non-physical interactables (empty here).
                // Physical items enter exclusively through bootstrap.Bindings.
                brain.Registry = Array.Empty<NpcInteractable>();

                // Await genuine PlayMode frame lifecycle:
                // Unity invokes Start() on active MonoBehaviours.
                // NpcAutonomy.Start() executes Ready = ResetState();
                // NpcPlayerControls.Start() and PhysicalContainerPanel.Start() execute EnsureInitialized().
                yield return null;
                for (int f = 0; f < 10 && !brain.Ready; f++)
                {
                    yield return null;
                }

                AssertNoUnexpectedLogs("startup");

                check(brain.Ready, "basket-player-brain-ready");
                check(brain.Actions != null, "basket-player-brain-actions-initialized");
                check(brain.Actions.PhysicalModel == bootstrap.Model, "basket-player-actions-model-aligned");

                // Freeze autonomy appropriately after Ready to prevent autonomous wandering during tests
                brain.Pause();
                brain.ManualSimulation = true;

                // Ensure UI components are initialized
                controls.EnsureInitialized();
                panel.EnsureInitialized();

                var api = brain.Actions;

                string FormatInputDiagnostic(string label, Key key)
                {
                    var keyControl = keyboard != null ? keyboard[key] : null;
                    bool isPressed = keyControl != null && keyControl.isPressed;
                    bool wasPressed = keyControl != null && keyControl.wasPressedThisFrame;
                    bool wasReleased = keyControl != null && keyControl.wasReleasedThisFrame;
                    string curPhase = InputState.currentUpdateType.ToString();
                    bool kbCurrent = Keyboard.current == keyboard;
                    bool kbEnabled = keyboard != null && keyboard.enabled;
                    bool kbAdded = keyboard != null && keyboard.added;
                    string kbName = Keyboard.current != null ? Keyboard.current.name : "none";
                    bool ctrlActive = controls != null && controls.gameObject.activeInHierarchy;
                    bool ctrlEnabled = controls != null && controls.enabled;
                    bool ctrlIsActiveAndEnabled = controls != null && controls.isActiveAndEnabled;
                    bool brainReady = brain != null && brain.Ready;
                    bool brainPossessed = brain != null && brain.Possessed;
                    bool appFocused = Application.isFocused;
                    bool allowUnfocused = controls != null && controls.AllowUnfocusedTestInput;
                    bool suppressInput = controls != null && controls.SuppressInput;
                    bool displayShortcut = controls != null && controls.DisplayShortcutActive;
                    bool menuOpen = controls != null && controls.MenuOpen;
                    bool panelOpen = panel != null && panel.IsOpen;
                    string panelSelected = panel != null ? (panel.SelectedContainerId ?? "none") : "null";
                    return $"{label}[frame={Time.frameCount},phase={curPhase},dynUpdates={dynamicUpdateFrames},edUpdates={editorUpdateFrames}," +
                           $"eventsObs={eventsObserved},lastPhase={lastObservedPhase},lastEvtPhase={lastEventPhase}," +
                           $"key={key},pressed={isPressed},wasPressed={wasPressed},wasReleased={wasReleased}," +
                           $"kbCurrent={kbCurrent},kbName={kbName},kbEnabled={kbEnabled},kbAdded={kbAdded}," +
                           $"ctrlActive={ctrlActive},ctrlEnabled={ctrlEnabled},ctrlActiveAndEnabled={ctrlIsActiveAndEnabled}," +
                           $"brainReady={brainReady},brainPossessed={brainPossessed}," +
                           $"appFocused={appFocused},allowUnfocused={allowUnfocused},suppressInput={suppressInput}," +
                           $"displayShortcut={displayShortcut},menuOpen={menuOpen},panelOpen={panelOpen},panelSelected={panelSelected}]";
                }

                IEnumerator Tap(Key keyToTap)
                {
                    // Nonnull prerequisite
                    if (keyboard == null || !keyboard.added)
                    {
                        string diag = $"Keyboard test device is null or not added (keyboard={(keyboard != null ? keyboard.name : "null")})";
                        check(false, "basket-player-tap-keyboard-prerequisite", diag);
                        throw new InvalidOperationException(diag);
                    }
                    var keyControl = keyboard[keyToTap];
                    if (keyControl == null || keyControl.device != keyboard)
                    {
                        string diag = $"Key control for '{keyToTap}' is null or not bound to test keyboard (control={(keyControl != null ? keyControl.name : "null")}, device={(keyControl?.device != null ? keyControl.device.name : "null")})";
                        check(false, "basket-player-tap-control-prerequisite", diag);
                        throw new InvalidOperationException(diag);
                    }

                    const int maxWait = 5;

                    // Establish neutral prerequisite without manual input driving or possession mutation
                    int neutralWaitFrames = 0;
                    while (neutralWaitFrames < maxWait && (keyControl.isPressed || keyControl.wasPressedThisFrame))
                    {
                        if (keyControl.isPressed)
                        {
                            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        }
                        yield return null;
                        neutralWaitFrames++;
                    }

                    if (keyControl.isPressed)
                    {
                        string diag = $"Key control for '{keyToTap}' failed neutral prerequisite: " + FormatInputDiagnostic("neutralPrereqFailed", keyToTap);
                        check(false, "basket-player-tap-neutral-prerequisite", diag);
                        throw new InvalidOperationException(diag);
                    }

                    int startFrame = Time.frameCount;
                    int startDynUpdates = dynamicUpdateFrames;

                    bool observedDown = false;
                    bool observedUp = false;
                    bool watchingDown = true;
                    int queueDownDynUpdates = -1;
                    int queueUpDynUpdates = -1;
                    InputUpdateType downEdgePhase = InputUpdateType.None;
                    int downEdgeFrame = -1;
                    InputUpdateType upEdgePhase = InputUpdateType.None;
                    int upEdgeFrame = -1;

                    Action tapObserver = () =>
                    {
                        var phase = InputState.currentUpdateType;
                        if ((phase & InputUpdateType.Dynamic) == 0)
                            return;

                        if (watchingDown)
                        {
                            if (!observedDown &&
                                dynamicUpdateFrames > queueDownDynUpdates &&
                                keyControl.device == keyboard &&
                                keyControl == keyboard[keyToTap] &&
                                keyControl.wasPressedThisFrame &&
                                keyControl.isPressed)
                            {
                                observedDown = true;
                                downEdgePhase = phase;
                                downEdgeFrame = Time.frameCount;
                            }
                        }
                        else
                        {
                            if (!observedUp &&
                                dynamicUpdateFrames > queueUpDynUpdates &&
                                keyControl.device == keyboard &&
                                keyControl == keyboard[keyToTap] &&
                                keyControl.wasReleasedThisFrame &&
                                !keyControl.isPressed)
                            {
                                observedUp = true;
                                upEdgePhase = phase;
                                upEdgeFrame = Time.frameCount;
                            }
                        }
                    };

                    activeTapObserver = tapObserver;
                    InputSystem.onAfterUpdate += tapObserver;

                    int downWaitFrames = 0;
                    int upWaitFrames = 0;

                    try
                    {
                        // 1. Real queued DOWN state event
                        queueDownDynUpdates = dynamicUpdateFrames;
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keyToTap));

                        // Bounded observation of DOWN phase
                        while (downWaitFrames < maxWait && !observedDown)
                        {
                            yield return null;
                            downWaitFrames++;
                        }

                        // 2. Real queued UP state event (empty keyboard state = all keys released)
                        watchingDown = false;
                        queueUpDynUpdates = dynamicUpdateFrames;
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());

                        // Bounded observation of UP phase
                        while (upWaitFrames < maxWait && !observedUp)
                        {
                            yield return null;
                            upWaitFrames++;
                        }

                        // 3. Subsequent REAL player frame for natural Controls.Update
                        yield return null;
                    }
                    finally
                    {
                        if (activeTapObserver != null)
                        {
                            try
                            {
                                InputSystem.onAfterUpdate -= activeTapObserver;
                                activeTapObserver = null;
                            }
                            catch (Exception ex)
                            {
                                string diag = $"Failed to unsubscribe activeTapObserver: {ex.Message}";
                                RecordCleanupError(diag);
                                check(false, "basket-player-tap-unsubscribe-failure", diag);
                                throw new InvalidOperationException(diag, ex);
                            }
                        }
                    }

                    bool boundFailed = !observedDown || !observedUp;
                    lastTapDiagnostic = $"tapDetail[key={keyToTap},boundFailed={boundFailed},startFrame={startFrame},endFrame={Time.frameCount}," +
                        $"downWait={downWaitFrames},upWait={upWaitFrames},observedDown={observedDown},observedUp={observedUp}," +
                        $"downPhase={downEdgePhase},downFrame={downEdgeFrame},upPhase={upEdgePhase},upFrame={upEdgeFrame}," +
                        $"dynUpdatesDelta={dynamicUpdateFrames - startDynUpdates}," +
                        $"lastPhase={lastObservedPhase},lastEvtPhase={lastEventPhase}," +
                        $"pressed={keyControl.isPressed},wasPressed={keyControl.wasPressedThisFrame},wasReleased={keyControl.wasReleasedThisFrame}]";

                    if (boundFailed)
                    {
                        lastTapDiagnostic += " -> " + FormatInputDiagnostic("boundFailed", keyToTap);
                        check(false, "basket-player-tap-bounded-failure", lastTapDiagnostic);
                        throw new InvalidOperationException($"Tap failed within bounds for key {keyToTap}: {lastTapDiagnostic}");
                    }
                }

                // -------------------------------------------------------------
                // 3. Authoritative Slot Projection & Persistent Holes
                // -------------------------------------------------------------
                var initialOccupants = bootstrap.Model.GetContainerSlotOccupants("basket-01");
                check(initialOccupants.Count == 0, "basket-player-initial-slots-empty");

                // -------------------------------------------------------------
                // 4. Transactional Store and Retrieve Action Flow & Registration
                // -------------------------------------------------------------
                // Pickup ruby-01 into hand
                var pickupRes = api.Execute(1, NpcActionKind.Pickup, "ruby-01");
                check(pickupRes.success && api.Held == rubyNi, "basket-player-pickup-ruby-success");

                // Store ruby-01 into basket-01
                var storeRes = api.Execute(2, NpcActionKind.Store, "ruby-01", "basket-01");
                check(storeRes.success && string.Equals(storeRes.code, "stored-in-container", StringComparison.Ordinal),
                    "basket-player-store-ruby-success", $"code={storeRes.code}");
                check(api.Held == null, "basket-player-held-cleared-after-store");

                // Authoritative slot check
                var occAfterStore = bootstrap.Model.GetContainerSlotOccupants("basket-01");
                check(occAfterStore.Count == 1 && occAfterStore.ContainsKey(0), "basket-player-slot-0-occupied");
                check(string.Equals(occAfterStore[0], "ruby-01", StringComparison.Ordinal), "basket-player-slot-0-has-ruby");
                check(bootstrap.Model.TryGetItemContainerSlot("ruby-01", out int rubySlot) && rubySlot == 0,
                    "basket-player-try-get-item-slot-0");

                // Retrieve ruby-01 from basket-01
                var retRes = api.Execute(3, NpcActionKind.Retrieve, "ruby-01", "basket-01");
                check(retRes.success && string.Equals(retRes.code, "retrieved-from-container", StringComparison.Ordinal),
                    "basket-player-retrieve-ruby-success", $"code={retRes.code}");
                check(api.Held == rubyNi, "basket-player-ruby-held-after-retrieve");

                var occAfterRet = bootstrap.Model.GetContainerSlotOccupants("basket-01");
                check(occAfterRet.Count == 0, "basket-player-slot-empty-after-retrieve");

                // -------------------------------------------------------------
                // 5. Refusal Invariants: Hands-Full, Capacity & LOS/Reach
                // -------------------------------------------------------------
                // Put ruby back into basket first
                var storeBackRes = api.Execute(4, NpcActionKind.Store, "ruby-01", "basket-01");
                check(storeBackRes.success, "basket-player-store-back-success");

                // Pickup chisel-01 into hand
                var pickChiselRes = api.Execute(5, NpcActionKind.Pickup, "chisel-01");
                check(pickChiselRes.success && api.Held == chiselNi, "basket-player-pickup-chisel-success");

                // Attempt retrieve ruby-01 while hand is full: MUST fail with hands-full
                var handsFullRes = api.Execute(6, NpcActionKind.Retrieve, "ruby-01", "basket-01");
                check(!handsFullRes.success && string.Equals(handsFullRes.code, "hands-full", StringComparison.Ordinal),
                    "basket-player-refuse-hands-full-retrieve", $"code={handsFullRes.code}");
                check(api.Held == chiselNi, "basket-player-held-preserved-on-refusal");

                // Out of reach distance refusal check
                var farPickupRes = api.Execute(7, NpcActionKind.Pickup, "far-item");
                check(!farPickupRes.success && string.Equals(farPickupRes.code, "out-of-reach", StringComparison.Ordinal),
                    "basket-player-refuse-out-of-reach", $"code={farPickupRes.code}");
                check(api.Held == chiselNi, "basket-player-held-preserved-on-far-refusal");

                // True Line of Sight authority obstruction refusal check (blocked by losWall on layer 8)
                losWall.SetActive(true);
                Physics.SyncTransforms();
                try
                {
                    var losRes = api.Execute(8, NpcActionKind.Pickup, "los-blocked-item");
                    check(!losRes.success && string.Equals(losRes.code, "line-of-sight-blocked", StringComparison.Ordinal),
                        "basket-player-refuse-los-blocked", $"code={losRes.code}");
                    check(api.Held == chiselNi, "basket-player-held-preserved-on-los-refusal");
                }
                finally
                {
                    losWall.SetActive(false);
                    Physics.SyncTransforms();
                }

                // Drop chisel-01 and verify stable slot holes
                var drop9Res = api.Execute(9, NpcActionKind.Drop, "chisel-01");
                string drop9Diag = FormatItemDiagnostic("drop9", drop9Res, chiselNi, chiselPhys, basketNi);
                check(drop9Res.success && string.Equals(drop9Res.code, "dropped", StringComparison.Ordinal),
                    "basket-player-drop-chisel-success", drop9Diag);
                check(api.Held == null, "basket-player-hand-cleared-after-drop", drop9Diag);

                // Advance isolated physics to settle dropped chisel to floor, then ensure reachable approach
                SettlePhysics(chiselPhys);
                EnsureReachableApproach(chiselNi);

                // Store chisel-01 into basket-01 -> gets slot 1
                var pickup10Res = api.Execute(10, NpcActionKind.Pickup, "chisel-01");
                string pickup10Diag = FormatItemDiagnostic("pickup10", pickup10Res, chiselNi, chiselPhys, basketNi);
                check(pickup10Res.success && string.Equals(pickup10Res.code, "picked-up", StringComparison.Ordinal) && api.Held == chiselNi,
                    "basket-player-pickup-chisel-re-pickup-success", pickup10Diag);

                EnsureReachableApproach(basketNi);
                var storeChiselRes = api.Execute(11, NpcActionKind.Store, "chisel-01", "basket-01");
                string store11Diag = FormatItemDiagnostic("store11", storeChiselRes, chiselNi, chiselPhys, basketNi);
                check(storeChiselRes.success, "basket-player-store-chisel-success", store11Diag);
                check(bootstrap.Model.TryGetItemContainerSlot("chisel-01", out int chSlot) && chSlot == 1,
                    "basket-player-chisel-in-slot-1");

                // Retrieve ruby-01 from slot 0, leaving slot 0 as vacant hole while slot 1 remains occupied
                var retRubyHoleRes = api.Execute(12, NpcActionKind.Retrieve, "ruby-01", "basket-01");
                check(retRubyHoleRes.success, "basket-player-retrieve-hole-success");
                var occHole = bootstrap.Model.GetContainerSlotOccupants("basket-01");
                check(!occHole.ContainsKey(0) && occHole.ContainsKey(1) && string.Equals(occHole[1], "chisel-01", StringComparison.Ordinal),
                    "basket-player-stable-hole-slot-0");

                // Re-store ruby-01 -> deterministically reuses vacant hole at slot 0 without disturbing slot 1
                var refilledHoleRes = api.Execute(13, NpcActionKind.Store, "ruby-01", "basket-01");
                check(refilledHoleRes.success, "basket-player-refill-hole-success");
                check(bootstrap.Model.TryGetItemContainerSlot("ruby-01", out int refilledSlot) && refilledSlot == 0,
                    "basket-player-hole-refilled-slot-0");
                check(bootstrap.Model.TryGetItemContainerSlot("chisel-01", out int chSlotAfter) && chSlotAfter == 1,
                    "basket-player-slot-1-chisel-unmoved");

                // Fill slot 2 and slot 3 to reach 4-slot container capacity
                api.Execute(14, NpcActionKind.Pickup, "ruby-02");
                api.Execute(15, NpcActionKind.Store, "ruby-02", "basket-01");
                api.Execute(16, NpcActionKind.Pickup, "chisel-02");
                api.Execute(17, NpcActionKind.Store, "chisel-02", "basket-01");

                var occFull = bootstrap.Model.GetContainerSlotOccupants("basket-01");
                check(occFull.Count == 4, "basket-player-all-4-slots-filled");

                // Attempt to store 5th item into full container: MUST fail closed with slot-capacity-exceeded
                api.Execute(18, NpcActionKind.Pickup, "chisel-overflow");
                var overflowStoreRes = api.Execute(19, NpcActionKind.Store, "chisel-overflow", "basket-01");
                check(!overflowStoreRes.success && string.Equals(overflowStoreRes.code, "container-slot-capacity-exceeded", StringComparison.Ordinal),
                    "basket-player-refuse-slot-capacity-exceeded", $"code={overflowStoreRes.code}");
                var dropOverflowRes = api.Execute(20, NpcActionKind.Drop, "chisel-overflow");
                string dropOverflowDiag = FormatItemDiagnostic("drop20", dropOverflowRes, overflowNi, overflowPhys, basketNi);
                check(dropOverflowRes.success && string.Equals(dropOverflowRes.code, "dropped", StringComparison.Ordinal),
                    "basket-player-drop-overflow-success", dropOverflowDiag);
                SettlePhysics(overflowPhys);

                // Retrieve ruby-01 and drop it so ruby-01 is Free and hand is empty
                var retRuby21Res = api.Execute(21, NpcActionKind.Retrieve, "ruby-01", "basket-01");
                string retRuby21Diag = FormatItemDiagnostic("ret21", retRuby21Res, rubyNi, rubyPhys, basketNi);
                check(retRuby21Res.success && string.Equals(retRuby21Res.code, "retrieved-from-container", StringComparison.Ordinal),
                    "basket-player-retrieve-ruby-21-success", retRuby21Diag);
                var dropRuby22Res = api.Execute(22, NpcActionKind.Drop, "ruby-01");
                string dropRuby22Diag = FormatItemDiagnostic("drop22", dropRuby22Res, rubyNi, rubyPhys, basketNi);
                check(dropRuby22Res.success && string.Equals(dropRuby22Res.code, "dropped", StringComparison.Ordinal),
                    "basket-player-drop-ruby-22-success", dropRuby22Diag);
                SettlePhysics(rubyPhys);
                EnsureSimultaneousReach(basketNi, basketPhys, rubyNi, rubyPhys);
                check(api.Held == null, "basket-player-hand-cleared-for-panel");
                check(bootstrap.Model.TryGetItem("ruby-01", out var rSnap) && rSnap.location == ItemLocationKind.Free, "basket-player-ruby-free-for-panel");
                check(bootstrap.Model.TryGetItem("basket-01", out var bSnap) && bSnap.location == ItemLocationKind.Free, "basket-player-basket-free-for-panel");

                // -------------------------------------------------------------
                // 6. Normal Save Entry Point & Live Model Disk Verification
                // -------------------------------------------------------------
                tempDir = Path.Combine(Application.temporaryCachePath, "BasketPlayerIntegration_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string savePath = Path.Combine(tempDir, "player-normal-save.json");

                bool saved = bootstrap.SaveCurrentState(savePath);
                check(saved, "basket-player-normal-save-returns-true");
                check(File.Exists(savePath), "basket-player-save-file-created");
                check(!bootstrap.SaveRejected, "basket-player-save-not-rejected");

                // Re-read and verify on disk using definition-backed live model
                bool loaded = ItemPersistence.TryLoad(savePath, worldId, genId, NpcAutonomy.AgentId, bootstrap.Model, out var diskPayload);
                check(loaded && diskPayload != null, "basket-player-disk-payload-valid");

                // -------------------------------------------------------------
                // 7. Safe Default Save Path, Sibling Preservation & Cold Corrupt Save Protection
                // -------------------------------------------------------------
                string defaultSavePath = PhysicalItemBootstrap.GetDefaultSavePath(worldId);
                check(!string.IsNullOrEmpty(defaultSavePath) && Path.IsPathRooted(defaultSavePath), "basket-player-default-save-path-rooted");
                check(defaultSavePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase), "basket-player-default-save-path-is-json");

                // Focused additive path qualification assertions
                check(PhysicalItemBootstrap.IsFullyQualifiedPath(defaultSavePath), "basket-player-default-save-path-fully-qualified");
                check(!PhysicalItemBootstrap.IsFullyQualifiedPath("C:save.json"), "basket-player-refuse-drive-relative-path");
                check(!PhysicalItemBootstrap.IsFullyQualifiedPath("\\save.json"), "basket-player-refuse-root-relative-path");
                check(!PhysicalItemBootstrap.IsFullyQualifiedPath(@"\\save.json"), "basket-player-refuse-unc-root-relative-path");
                check(!PhysicalItemBootstrap.IsFullyQualifiedPath("relative-save.json"), "basket-player-refuse-relative-path");
                check(!PhysicalItemBootstrap.IsFullyQualifiedPath(""), "basket-player-refuse-empty-path");
                check(!PhysicalItemBootstrap.IsFullyQualifiedPath("C:\\invalid<?>name.json"), "basket-player-refuse-malformed-path");

                // SaveCurrentState must reject non-fully-qualified path input and preserve originals
                bool driveRelSave = bootstrap.SaveCurrentState("C:save.json");
                check(!driveRelSave && bootstrap.LastSaveFailed, "basket-player-save-refuse-drive-relative");
                bool rootRelSave = bootstrap.SaveCurrentState("\\save.json");
                check(!rootRelSave && bootstrap.LastSaveFailed, "basket-player-save-refuse-root-relative");
                bool relSave = bootstrap.SaveCurrentState("save.json");
                check(!relSave && bootstrap.LastSaveFailed, "basket-player-save-refuse-relative");

                // CLI argument parser path qualification contract
                check(!PhysicalItemBootstrap.ValidateCommandLineSaveArgs(new[] { "-physicalSave" }, out _, out string errMissing) && errMissing == "invalid-cli-save-path",
                    "basket-player-cli-missing-path-rejected");
                check(!PhysicalItemBootstrap.ValidateCommandLineSaveArgs(new[] { "-physicalSave", "C:save.json" }, out _, out string errDriveRel) && errDriveRel == "invalid-cli-save-path",
                    "basket-player-cli-drive-relative-rejected");
                check(!PhysicalItemBootstrap.ValidateCommandLineSaveArgs(new[] { "-physicalSave", "\\save.json" }, out _, out string errRootRel) && errRootRel == "invalid-cli-save-path",
                    "basket-player-cli-root-relative-rejected");
                check(!PhysicalItemBootstrap.ValidateCommandLineSaveArgs(new[] { "-physicalSave", "relative.json" }, out _, out string errRel) && errRel == "invalid-cli-save-path",
                    "basket-player-cli-relative-rejected");

                // Invalid override fails before IO without falling back
                var invalidOverrideGo = LocalCreate("test-invalid-override-actor");
                invalidOverrideGo.SetActive(false);
                var invalidOverrideBootstrap = invalidOverrideGo.AddComponent<PhysicalItemBootstrap>();
                invalidOverrideBootstrap.ExplicitSavePathOverride = "C:save.json";
                invalidOverrideGo.SetActive(true);
                check(invalidOverrideBootstrap.SaveRejected && invalidOverrideBootstrap.LastSaveFailed &&
                      string.Equals(invalidOverrideBootstrap.LastSaveError, "invalid-configured-save-path", StringComparison.Ordinal),
                      "basket-player-invalid-override-refused-before-io");

                // Create corrupt save file with invalid sha to test rejected provenance and overwrite refusal
                corruptDir = Path.Combine(Application.temporaryCachePath, "BasketCorrupt_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(corruptDir);
                string corruptFile = Path.Combine(corruptDir, "corrupt-save.json");
                byte[] corruptBytes = System.Text.Encoding.UTF8.GetBytes("{\"schema\":\"starfall.physical-save.v2\",\"payload\":\"corrupted\",\"sha256\":\"bad\"}");
                File.WriteAllBytes(corruptFile, corruptBytes);

                // Use separate COLD configured fixture before Actions exists to verify genuine cold corrupt read
                var coldCorruptGo = LocalCreate("test-cold-corrupt-actor");
                coldCorruptGo.SetActive(false);
                var coldCorruptBrain = coldCorruptGo.AddComponent<NpcAutonomy>();
                coldCorruptBrain.InstanceWorldId = worldId;
                coldCorruptBrain.ManualSimulation = true;
                coldCorruptBrain.Running = false;
                var coldCorruptLog = coldCorruptGo.AddComponent<NpcDecisionLog>();
                coldCorruptBrain.Log = coldCorruptLog;
                var coldCorruptPerception = coldCorruptGo.AddComponent<NpcPerception>();
                coldCorruptPerception.WorldId = worldId;
                coldCorruptBrain.Perception = coldCorruptPerception;

                var coldCorruptBootstrap = coldCorruptGo.AddComponent<PhysicalItemBootstrap>();
                coldCorruptBootstrap.ExplicitSavePathOverride = corruptFile;
                coldCorruptBootstrap.Brain = coldCorruptBrain;
                coldCorruptBrain.PhysicalItems = coldCorruptBootstrap;
                coldCorruptGo.SetActive(true);
                coldCorruptGo.SetActive(false);
                check(coldCorruptBootstrap.Model != null, "basket-player-cold-corrupt-model-nonnull");
                check(coldCorruptBrain.Actions == null, "basket-player-cold-corrupt-actions-null");

                bool corruptLoaded = coldCorruptBootstrap.LoadSavePayload(corruptFile);
                check(!corruptLoaded, "basket-player-corrupt-load-returns-false");
                check(coldCorruptBootstrap.SourceSaveRejected, "basket-player-corrupt-sets-source-rejected");
                check(string.Equals(coldCorruptBootstrap.RejectedSourcePath, corruptFile, StringComparison.Ordinal), "basket-player-corrupt-records-rejected-path");

                // Strengthened byte-for-byte equality verification
                byte[] afterLoadBytes = File.ReadAllBytes(corruptFile);
                check(ByteArraysEqual(afterLoadBytes, corruptBytes), "basket-player-rejected-save-bytes-unchanged");

                // SaveCurrentState MUST refuse to overwrite rejected source file
                bool overwriteResult = coldCorruptBootstrap.SaveCurrentState(corruptFile);
                check(!overwriteResult, "basket-player-refuse-overwriting-rejected-source");
                check(coldCorruptBootstrap.LastSaveFailed, "basket-player-last-save-failed-flag-set");
                check(string.Equals(coldCorruptBootstrap.LastSaveError, "refused-overwriting-rejected-source", StringComparison.Ordinal),
                    "basket-player-refuse-error-code-match", $"error={coldCorruptBootstrap.LastSaveError}");

                byte[] afterOverwriteBytes = File.ReadAllBytes(corruptFile);
                check(ByteArraysEqual(afterOverwriteBytes, corruptBytes), "basket-player-rejected-source-still-byte-unchanged");

                // Pre-create unrelated existing sibling recovery file to test collision prevention
                string siblingRecoveryFile = Path.Combine(corruptDir, "corrupt-save-recovery.json");
                byte[] siblingRecoveryBytes = System.Text.Encoding.UTF8.GetBytes("{\"schema\":\"starfall.existing-sibling.v1\",\"note\":\"unrelated\"}");
                File.WriteAllBytes(siblingRecoveryFile, siblingRecoveryBytes);

                // Explicit recovery save MUST NOT overwrite existing sibling! It must select a collision-free path:
                bool recSaved = coldCorruptBootstrap.SaveRecoveryState(out recPath);
                string recoveryDiag = $"lastSaveError={coldCorruptBootstrap.LastSaveError ?? "none"}," +
                    $"sourceSaveRejected={coldCorruptBootstrap.SourceSaveRejected},saveRejected={coldCorruptBootstrap.SaveRejected}," +
                    $"configuredPath={coldCorruptBootstrap.ExplicitSavePathOverride ?? "none"},rejectedPath={coldCorruptBootstrap.RejectedSourcePath ?? "none"}," +
                    $"selectedPath={recPath ?? "none"},recFileExists={File.Exists(recPath)}," +
                    $"model={(coldCorruptBootstrap.Model != null ? $"items={coldCorruptBootstrap.Model.ItemCount}" : "null")}";
                check(recSaved, "basket-player-recovery-save-succeeds", recoveryDiag);
                check(File.Exists(recPath), "basket-player-recovery-file-created");
                check(!string.Equals(recPath, corruptFile, StringComparison.OrdinalIgnoreCase), "basket-player-recovery-path-distinct");
                check(!string.Equals(recPath, siblingRecoveryFile, StringComparison.OrdinalIgnoreCase), "basket-player-recovery-path-not-sibling");
                check(ByteArraysEqual(File.ReadAllBytes(siblingRecoveryFile), siblingRecoveryBytes), "basket-player-existing-sibling-preserved");
                check(ByteArraysEqual(File.ReadAllBytes(corruptFile), corruptBytes), "basket-player-rejected-source-preserved-after-recovery");

                // Repeated recovery save to the deliberate current recovery target succeeds and overwrites the same path:
                bool recSaved2 = coldCorruptBootstrap.SaveRecoveryState(out recPath2);
                check(recSaved2 && string.Equals(recPath2, recPath, StringComparison.OrdinalIgnoreCase), "basket-player-recovery-repeat-same-target");

                // Direct attempt to overwrite existing sibling via SaveCurrentState(isRecoveryAction: true) must fail closed:
                bool directSiblingClobber = coldCorruptBootstrap.SaveCurrentState(siblingRecoveryFile, isRecoveryAction: true);
                check(!directSiblingClobber && coldCorruptBootstrap.LastSaveFailed && string.Equals(coldCorruptBootstrap.LastSaveError, "refused-overwriting-existing-sibling", StringComparison.Ordinal),
                    "basket-player-refuse-overwriting-sibling");
                check(ByteArraysEqual(File.ReadAllBytes(siblingRecoveryFile), siblingRecoveryBytes), "basket-player-sibling-bytes-preserved-after-refusal");

                // -------------------------------------------------------------
                // 8. Panel / Controls Regression: Real InputSystem Keys & Button onClick
                // -------------------------------------------------------------
                // Enter possession via real Tab key
                string preTabDiag = FormatInputDiagnostic("preTab", Key.Tab);
                yield return Tap(Key.Tab);
                string postTabDiag = FormatInputDiagnostic("postTab", Key.Tab);
                string tabDiag = $"{preTabDiag} -> {lastTapDiagnostic} -> {postTabDiag}";
                check(brain.Possessed, "basket-player-tab-entered-possession", tabDiag);

                // Reachable verification
                string preUIReachDiag = FormatSimultaneousDiagnostic("preUIReachable", basketNi, basketPhys, rubyNi, rubyPhys);
                check(Vector3.Distance(actorGo.transform.position, basketNi.Approach.position) <= 0.65f, "basket-player-regression-basket-in-reach", preUIReachDiag);
                check(Vector3.Distance(actorGo.transform.position, rubyNi.Approach.position) <= 0.65f, "basket-player-regression-ruby-in-reach", preUIReachDiag);

                // Open panel via real C key and verify selection
                yield return Tap(Key.C);
                check(panel.IsOpen, "basket-player-panel-is-open");
                check(!controls.Looking, "basket-player-panel-open-suppresses-looking");
                check(string.Equals(panel.SelectedContainerId, "basket-01", StringComparison.Ordinal) || panel.SelectedTarget == basketNi,
                    "basket-player-panel-selected-basket");

                // Actual UI Button onClick interactions:
                // Click slot 1 (contains chisel-01) -> retrieves into hand!
                var slot1Btn = panel.GetSlotButton(1);
                check(slot1Btn != null && slot1Btn.interactable, "basket-player-slot-1-button-interactable");
                slot1Btn.onClick.Invoke();
                check(api.Held == chiselNi, "basket-player-slot-button-click-retrieves-chisel");

                // With hand full, click slot 2 (contains ruby-02) -> must refuse with hands-full!
                var slot2Btn = panel.GetSlotButton(2);
                check(slot2Btn != null, "basket-player-slot-2-button-exists");
                slot2Btn.onClick.Invoke();
                check(api.Held == chiselNi, "basket-player-slot-click-hands-full-preserves-held");
                check(panel.LastReceipt.Contains("hands-full"), "basket-player-slot-click-hands-full-receipt");

                // Click Store button -> stores chisel-01 back into basket-01!
                var storeBtn = panel.StoreButton;
                check(storeBtn != null && storeBtn.interactable, "basket-player-store-button-interactable");
                storeBtn.onClick.Invoke();
                check(api.Held == null, "basket-player-store-button-stores-item");

                // Explicit No-Container Save Verification:
                // Close panel, move far away from all containers, open panel with no selected container
                yield return Tap(Key.Escape);
                check(!panel.IsOpen, "basket-player-panel-closed-for-move");

                Vector3 originalActorPos = actorGo.transform.position;
                actorGo.transform.position = originalActorPos + new Vector3(50f, 0f, 50f);
                yield return null;

                yield return Tap(Key.C);
                check(panel.IsOpen, "basket-player-panel-open-away-from-containers");
                check(string.IsNullOrEmpty(panel.SelectedContainerId), "basket-player-selected-container-absent-away");

                var saveBtn = panel.SaveButton;
                check(saveBtn != null && saveBtn.interactable, "basket-player-save-renders-without-container");
                saveBtn.onClick.Invoke();
                check(panel.ReceiptLabel != null && !string.IsNullOrEmpty(panel.ReceiptLabel.text), "basket-player-save-receipt-rendered");

                yield return Tap(Key.Escape);
                check(!panel.IsOpen, "basket-player-panel-closed-away");

                // Move back to item interaction zone
                actorGo.transform.position = originalActorPos;
                Physics.SyncTransforms();
                yield return null;

                // -------------------------------------------------------------
                // B14 Staging: Stage ruby-01 into slot 0 before later slot 0 retrieval expectation
                // Account for actual chisel slot/held state after intervening chisel store into first hole.
                // -------------------------------------------------------------
                EnsureSimultaneousReach(basketNi, basketPhys, rubyNi, rubyPhys);

                bool chiselInSlot = bootstrap.Model.TryGetItemContainerSlot("chisel-01", out int currentChiselSlot);
                bool chiselHeld = (api.Held == chiselNi) ||
                    (bootstrap.Model.TryGetItem("chisel-01", out var chState) && chState.location == ItemLocationKind.Carried);

                // If chisel occupies slot 0, retrieve it so slot 0 can receive ruby-01
                if (chiselInSlot && currentChiselSlot == 0)
                {
                    check(brain.TryAllocateRequestId(out int stageRetChiselReq), "basket-player-stage-req-ret-chisel");
                    var retChiselResult = api.Execute(stageRetChiselReq, NpcActionKind.Retrieve, "chisel-01", "basket-01");
                    string retChiselDiag = FormatItemDiagnostic("stageRetChisel", retChiselResult, chiselNi, chiselPhys, basketNi);
                    check(retChiselResult.success && api.Held == chiselNi, "basket-player-stage-retrieve-chisel-vacating-slot-0", retChiselDiag);
                    chiselHeld = true;
                    chiselInSlot = false;
                }

                // If chisel is held in hand, drop it temporarily to clear hand for ruby-01 pickup
                if (chiselHeld)
                {
                    check(brain.TryAllocateRequestId(out int stageDropChiselReq), "basket-player-stage-req-drop-chisel");
                    var dropChiselResult = api.Execute(stageDropChiselReq, NpcActionKind.Drop, "chisel-01");
                    string dropChiselDiag = FormatItemDiagnostic("stageDropChisel", dropChiselResult, chiselNi, chiselPhys, basketNi);
                    check(dropChiselResult.success && api.Held == null, "basket-player-stage-drop-chisel-clearing-hand", dropChiselDiag);
                    SettlePhysics(chiselPhys);
                    EnsureReachableApproach(chiselNi);
                    chiselHeld = false;
                }

                // Ensure reach to ruby-01, then execute real Pickup for ruby-01
                EnsureReachableApproach(rubyNi);
                check(brain.TryAllocateRequestId(out int stagePickRubyReq), "basket-player-stage-req-pick-ruby");
                var pickRubyResult = api.Execute(stagePickRubyReq, NpcActionKind.Pickup, "ruby-01");
                string pickRubyDiag = FormatItemDiagnostic("stagePickRuby", pickRubyResult, rubyNi, rubyPhys, basketNi);
                check(pickRubyResult.success && api.Held == rubyNi, "basket-player-stage-pickup-ruby-success", pickRubyDiag);

                // Store ruby-01 into basket-01: occupies first hole (slot 0)
                EnsureReachableApproach(basketNi);
                check(brain.TryAllocateRequestId(out int stageStoreRubyReq), "basket-player-stage-req-store-ruby");
                var storeRubyResult = api.Execute(stageStoreRubyReq, NpcActionKind.Store, "ruby-01", "basket-01");
                string storeRubyDiag = FormatItemDiagnostic("stageStoreRuby", storeRubyResult, rubyNi, rubyPhys, basketNi);
                check(storeRubyResult.success && api.Held == null, "basket-player-stage-store-ruby-success", storeRubyDiag);
                check(bootstrap.Model.TryGetItemContainerSlot("ruby-01", out int stagedRubySlot) && stagedRubySlot == 0,
                    "basket-player-stage-ruby-confirmed-slot-0", $"slot={stagedRubySlot}");

                // Re-store chisel-01 if it was displaced to ground: pick up and store into basket-01 (occupies slot 1)
                if (bootstrap.Model.TryGetItem("chisel-01", out var chGroundState) && chGroundState.location == ItemLocationKind.Free)
                {
                    EnsureReachableApproach(chiselNi);
                    check(brain.TryAllocateRequestId(out int stagePickChiselReq), "basket-player-stage-req-pick-chisel");
                    var pickChiselResult = api.Execute(stagePickChiselReq, NpcActionKind.Pickup, "chisel-01");
                    string pickChiselDiag = FormatItemDiagnostic("stagePickChisel", pickChiselResult, chiselNi, chiselPhys, basketNi);
                    check(pickChiselResult.success && api.Held == chiselNi, "basket-player-stage-pickup-displaced-chisel", pickChiselDiag);

                    EnsureReachableApproach(basketNi);
                    check(brain.TryAllocateRequestId(out int stageStoreChiselReq), "basket-player-stage-req-store-chisel");
                    var storeChiselResult = api.Execute(stageStoreChiselReq, NpcActionKind.Store, "chisel-01", "basket-01");
                    string storeChiselDiag = FormatItemDiagnostic("stageStoreChisel", storeChiselResult, chiselNi, chiselPhys, basketNi);
                    check(storeChiselResult.success && api.Held == null, "basket-player-stage-store-chisel-success", storeChiselDiag);
                    check(bootstrap.Model.TryGetItemContainerSlot("chisel-01", out int stagedChiselSlot) && stagedChiselSlot == 1,
                        "basket-player-stage-chisel-confirmed-slot-1", $"slot={stagedChiselSlot}");
                }

                // Verify authoritative model slot occupants before UI panel interaction
                var postStageOccupants = bootstrap.Model.GetContainerSlotOccupants("basket-01");
                check(postStageOccupants.TryGetValue(0, out string s0Occ) && string.Equals(s0Occ, "ruby-01", StringComparison.Ordinal),
                    "basket-player-stage-verified-slot-0-ruby", $"slot0={s0Occ ?? "empty"}");
                check(postStageOccupants.TryGetValue(1, out string s1Occ) && string.Equals(s1Occ, "chisel-01", StringComparison.Ordinal),
                    "basket-player-stage-verified-slot-1-chisel", $"slot1={s1Occ ?? "empty"}");
                check(api.Held == null, "basket-player-stage-verified-hand-empty");

                // Ensure actor approaches accessible basket root and verify authoritative stored states
                EnsureReachableApproach(basketNi);
                Physics.SyncTransforms();

                bool postRubyStored = bootstrap.Model.TryGetItem("ruby-01", out var postRubySnap) &&
                    postRubySnap.location == ItemLocationKind.Stored &&
                    string.Equals(postRubySnap.containerItemId, "basket-01", StringComparison.Ordinal) &&
                    postRubySnap.containerSlot == 0;
                bool postChiselStored = bootstrap.Model.TryGetItem("chisel-01", out var postChiselSnap) &&
                    postChiselSnap.location == ItemLocationKind.Stored &&
                    string.Equals(postChiselSnap.containerItemId, "basket-01", StringComparison.Ordinal) &&
                    postChiselSnap.containerSlot == 1;
                bool postBasketFreeRoot = bootstrap.Model.TryGetItem("basket-01", out var postBasketSnap) &&
                    postBasketSnap.location == ItemLocationKind.Free &&
                    string.IsNullOrEmpty(postBasketSnap.containerItemId);
                bool postStageHandEmpty = api.Held == null;

                float basketAppDist = basketNi.Approach != null
                    ? Vector3.Distance(actorGo.transform.position, basketNi.Approach.position)
                    : float.MaxValue;
                float basketSightDist = Vector3.Distance(actorGo.transform.position + Vector3.up, basketNi.SightPoint);
                bool basketSightPass = CheckItemSightGate(basketNi, actorGo.transform.position, out string basketSightDiag);

                string postStageDiag = $"root=basket-01(active={basketNi.isActiveAndEnabled},appDist={basketAppDist:F3},sightDist={basketSightDist:F3},losPass={basketSightPass},losDiag={basketSightDiag ?? "ok"},pos={basketNi.transform.position:F3},appPos={(basketNi.Approach != null ? basketNi.Approach.position.ToString("F3") : "null")})," +
                    $"actorPos={actorGo.transform.position:F3},held={(api.Held != null ? api.Held.StableId : "none")}," +
                    $"basketModel=(loc={postBasketSnap?.location},cont={postBasketSnap?.containerItemId ?? "none"},slot={postBasketSnap?.containerSlot})," +
                    $"rubyModel=(loc={postRubySnap?.location},cont={postRubySnap?.containerItemId ?? "none"},slot={postRubySnap?.containerSlot})," +
                    $"chiselModel=(loc={postChiselSnap?.location},cont={postChiselSnap?.containerItemId ?? "none"},slot={postChiselSnap?.containerSlot})";

                check(postBasketFreeRoot, "basket-player-stage-verified-basket-root-free", postStageDiag);
                check(postRubyStored, "basket-player-stage-verified-stored-ruby-slot-0", postStageDiag);
                check(postChiselStored, "basket-player-stage-verified-stored-chisel-slot-1", postStageDiag);
                check(postStageHandEmpty, "basket-player-stage-verified-hand-cleared-for-retrieve", postStageDiag);
                check(basketAppDist <= 0.65f, "basket-player-stage-basket-root-approach-in-reach", postStageDiag);
                check(basketSightDist <= 1.7f && basketSightPass, "basket-player-stage-basket-root-sight-and-los-pass", postStageDiag);

                // Open panel and retrieve ruby-01 from slot 0, then drop it so ruby-01 is Free
                yield return Tap(Key.C);
                check(panel.IsOpen, "basket-player-panel-is-open-for-retrieve");
                var slot0Btn = panel.GetSlotButton(0);
                check(slot0Btn != null && slot0Btn.interactable, "basket-player-slot-0-button-interactable");
                slot0Btn.onClick.Invoke();
                check(api.Held == rubyNi, "basket-player-slot-button-click-retrieves-ruby");

                yield return Tap(Key.Escape);
                check(!panel.IsOpen, "basket-player-panel-closed-for-drop");

                // Drop ruby-01 to ground via G key
                yield return Tap(Key.G);
                check(api.Held == null, "basket-player-hand-cleared-after-drop");
                check(bootstrap.Model.TryGetItem("ruby-01", out var rubyFreeCheck) && rubyFreeCheck.location == ItemLocationKind.Free,
                    "basket-player-ruby-free-for-panel");
                check(bootstrap.Model.TryGetItem("basket-01", out var basketFreeCheck) && basketFreeCheck.location == ItemLocationKind.Free,
                    "basket-player-basket-free-for-panel");

                // Settle ruby-01 after dropping via G key so it falls to ground and is in reach for pickup
                SettlePhysics(rubyPhys);
                EnsureReachableApproach(rubyNi);

                // Cycle target using real T key
                var capturedEligible = controls.GetEligiblePickupTargets();
                bool rubyEligible = capturedEligible != null && capturedEligible.Contains(rubyNi);
                float rubyAppDist = rubyNi != null && rubyNi.Approach != null
                    ? Vector3.Distance(actorGo.transform.position, rubyNi.Approach.position)
                    : -1f;
                float rubySightDist = rubyNi != null
                    ? Vector3.Distance(actorGo.transform.position + Vector3.up, rubyNi.SightPoint)
                    : -1f;
                bool rubySightPass = CheckItemSightGate(rubyNi, actorGo.transform.position, out string rubySightDiag);
                bootstrap.Model.TryGetItem("ruby-01", out var rubyModelSnap);
                string rubyModelDiag = rubyModelSnap != null
                    ? $"loc={rubyModelSnap.location},cont={rubyModelSnap.containerItemId ?? "none"},slot={rubyModelSnap.containerSlot},pos={rubyModelSnap.position:F3}"
                    : "null";
                string eligibleDiag = capturedEligible != null
                    ? string.Join(",", capturedEligible.ConvertAll(t => t?.StableId ?? "null"))
                    : "null";
                string rubyPrecheckDiag = $"ruby(id={rubyNi?.StableId ?? "null"},active={rubyNi != null && rubyNi.isActiveAndEnabled},perm={rubyNi != null && rubyNi.Permission},appDist={rubyAppDist:F3},sightDist={rubySightDist:F3},sightPass={rubySightPass},sightDiag={rubySightDiag ?? "ok"},model=[{rubyModelDiag}],eligible=[{eligibleDiag}])";

                if (!rubyEligible)
                {
                    throw new InvalidOperationException($"Prerequisite rubyNi is not in eligible pickup targets: {rubyPrecheckDiag}");
                }

                const int hardCeiling = 10;
                int maxTaps = Math.Min(Math.Max((capturedEligible != null ? capturedEligible.Count : 0) + 2, 3), hardCeiling);
                var stepHistory = new List<string>();
                string eligibilityChangeDiag = null;
                int actualTaps = 0;

                for (int tap = 0; tap < maxTaps; tap++)
                {
                    yield return Tap(Key.T);
                    actualTaps++;

                    var stepEligible = controls.GetEligiblePickupTargets();
                    string stepEligibleIds = stepEligible != null
                        ? string.Join(",", stepEligible.ConvertAll(t => t?.StableId ?? "null"))
                        : "null";
                    bool rubyStillEligible = stepEligible != null && stepEligible.Contains(rubyNi);
                    if (!rubyStillEligible && eligibilityChangeDiag == null)
                    {
                        eligibilityChangeDiag = $"ruby lost eligibility at tap {actualTaps}: eligible=[{stepEligibleIds}]";
                    }

                    string stepDiag = $"tap={actualTaps}/{maxTaps},target={controls.CurrentPickupTarget?.StableId ?? "null"},eligible=[{stepEligibleIds}],rubyEligible={rubyStillEligible},frame={Time.frameCount},{lastTapDiagnostic}";
                    stepHistory.Add(stepDiag);

                    if (tap == 0)
                    {
                        check(controls.CurrentPickupTarget != null, "basket-player-pickup-target-non-null",
                            $"target={controls.CurrentPickupTarget?.StableId ?? "null"},step={stepDiag}");
                    }

                    if (controls.CurrentPickupTarget == rubyNi)
                    {
                        break;
                    }
                }

                string finalDiag = $"target={controls.CurrentPickupTarget?.StableId ?? "null"},expected={rubyNi.StableId},taps={actualTaps}/{maxTaps}," +
                    $"eligibilityChange={(eligibilityChangeDiag ?? "none")},history=[{string.Join(" | ", stepHistory)}],precheck=[{rubyPrecheckDiag}]";
                check(controls.CurrentPickupTarget == rubyNi, "basket-player-pickup-target-is-ruby", finalDiag);

                // Interact G picks ruby, NOT basket!
                yield return Tap(Key.G);
                check(api.Held == rubyNi, "basket-player-pickup-picks-ruby-not-basket");
                check(bootstrap.Model.TryGetItem("basket-01", out var basketFree) && basketFree.location == ItemLocationKind.Free,
                    "basket-player-basket-remains-on-ground");

                // Drop ruby back
                yield return Tap(Key.G);
                check(api.Held == null, "basket-player-ruby-dropped-back");
                SettlePhysics(rubyPhys);

                // Keyboard / menu transitions via real Escape key
                yield return Tap(Key.Escape);
                check(controls.MenuOpen, "basket-player-menu-open");
                yield return Tap(Key.Escape);
                check(!controls.MenuOpen, "basket-player-menu-resume");

                AssertNoUnexpectedLogs("panel-controls");

                // -------------------------------------------------------------
                // 9. Cold Restore Complete Replacement (Graph, Visual, Stored Slots, Object Absence)
                // -------------------------------------------------------------
                // Part A: 1-item replacement (preserving existing frozen assertion names)
                var customPayload = new PhysicalSavePayload
                {
                    worldId = worldId,
                    generationId = "gen-01",
                    actorId = NpcAutonomy.AgentId,
                    tick = 50,
                    items = new List<SavedItemRecord>
                    {
                        new SavedItemRecord
                        {
                            itemId = "custom-restore-stone",
                            itemTypeId = "canyon-stone",
                            location = ItemLocationKind.Free,
                            massKg = 2.5f,
                            dimensions = new PhysicalDimensions(0.25f, 0.25f, 0.25f),
                            position = new Vector3(1f, 0f, 1f),
                            rotation = Quaternion.identity,
                            lastUpdatedTick = 10
                        }
                    },
                    receipts = new List<SavedReceiptRecord>()
                };
                customSaveDir = Path.Combine(Application.temporaryCachePath, "BasketColdRestore_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(customSaveDir);
                string customSaveFile = Path.Combine(customSaveDir, "cold-restore.json");
                ItemPersistence.SaveAtomic(customSaveFile, customPayload);

                var coldRestoreGo = LocalCreate("test-cold-restore-actor");
                coldRestoreGo.SetActive(false);
                var coldBrain = coldRestoreGo.AddComponent<NpcAutonomy>();
                coldBrain.InstanceWorldId = worldId;
                coldBrain.ManualSimulation = true;
                coldBrain.Running = false;
                var coldLog = coldRestoreGo.AddComponent<NpcDecisionLog>();
                coldBrain.Log = coldLog;
                var coldPerception = coldRestoreGo.AddComponent<NpcPerception>();
                coldPerception.WorldId = worldId;
                coldBrain.Perception = coldPerception;

                var coldBootstrap = coldRestoreGo.AddComponent<PhysicalItemBootstrap>();
                coldBootstrap.ExplicitSavePathOverride = customSaveFile;
                coldBootstrap.Brain = coldBrain;
                coldBrain.PhysicalItems = coldBootstrap;
                coldRestoreGo.SetActive(true);

                bool customLoaded = coldBootstrap.LoadSavePayload(customSaveFile);
                check(customLoaded, "basket-player-cold-restore-load-succeeds");
                check(coldBootstrap.Model.ItemCount == 1, "basket-player-cold-restore-item-count-exact-1");
                check(coldBootstrap.Model.TryGetItem("custom-restore-stone", out _), "basket-player-cold-restore-has-custom-item");
                check(!coldBootstrap.Model.TryGetItem("canyon-artifact-01", out _), "basket-player-cold-restore-cleared-default-item");

                // Part B: Enhanced replacement of Opt-In 6-Item Graph with Restored Woven Basket & Stored Slots
                var richColdGo = LocalCreate("test-rich-cold-restore-actor");
                richColdGo.SetActive(false);
                var richBrain = richColdGo.AddComponent<NpcAutonomy>();
                richBrain.InstanceWorldId = worldId;
                richBrain.ManualSimulation = true;
                richBrain.Running = false;
                var richLog = richColdGo.AddComponent<NpcDecisionLog>();
                richBrain.Log = richLog;
                var richPerception = richColdGo.AddComponent<NpcPerception>();
                richPerception.WorldId = worldId;
                richBrain.Perception = richPerception;

                var richBootstrap = richColdGo.AddComponent<PhysicalItemBootstrap>();
                richBootstrap.DemonstrationMaterial = testMat;
                richBootstrap.BasketMaterial = testMat;
                richBootstrap.OptInStarterLayout = true;
                string richIsolatedPath = Path.Combine(customSaveDir, "rich-starter-isolated.json");
                richBootstrap.ExplicitSavePathOverride = richIsolatedPath;
                richBootstrap.Brain = richBrain;
                richBrain.PhysicalItems = richBootstrap;
                richColdGo.SetActive(true);

                // Verify pre-restore 6-item starter graph
                check(richBootstrap.Model.ItemCount == 6, "basket-player-cold-restore-pre-count-is-6");

                // Capture exact owner reference from richBootstrap.Bindings (never global Find)
                var oldBindings = new List<PhysicalItemRuntimeBinding>(richBootstrap.Bindings);
                var oldBasketBinding = oldBindings.Find(b => string.Equals(b.itemId, "canyon-basket-01", StringComparison.Ordinal));
                GameObject oldBasketObj = oldBasketBinding != null && oldBasketBinding.physicalItem != null ? oldBasketBinding.physicalItem.gameObject : null;
                check(oldBasketObj != null, "basket-player-cold-restore-old-basket-existed");

                var richPayload = new PhysicalSavePayload
                {
                    worldId = worldId,
                    generationId = "gen-01",
                    actorId = NpcAutonomy.AgentId,
                    tick = 100,
                    items = new List<SavedItemRecord>
                    {
                        new SavedItemRecord
                        {
                            itemId = "restored-woven-basket",
                            itemTypeId = "container-basket",
                            location = ItemLocationKind.Free,
                            massKg = 1.0f,
                            dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                            position = new Vector3(2f, 0f, 2f),
                            rotation = Quaternion.identity,
                            lastUpdatedTick = 20
                        },
                        new SavedItemRecord
                        {
                            itemId = "restored-gem",
                            itemTypeId = "gem-ruby",
                            location = ItemLocationKind.Stored,
                            containerItemId = "restored-woven-basket",
                            containerSlot = 0,
                            massKg = 0.1f,
                            dimensions = new PhysicalDimensions(0.02f, 0.02f, 0.02f),
                            position = new Vector3(2f, 0f, 2f),
                            rotation = Quaternion.identity,
                            lastUpdatedTick = 20
                        }
                    },
                    receipts = new List<SavedReceiptRecord>()
                };
                string richSaveFile = Path.Combine(customSaveDir, "rich-cold-restore.json");
                ItemPersistence.SaveAtomic(richSaveFile, richPayload);

                bool richLoaded = richBootstrap.LoadSavePayload(richSaveFile);
                check(richLoaded, "basket-player-cold-restore-rich-load-succeeds");
                check(richBootstrap.Model.ItemCount == 2, "basket-player-cold-restore-rich-count-is-2");

                // Absence of prior scene GameObjects confirmed on captured owner reference
                check(oldBasketObj == null || !oldBasketObj, "basket-player-cold-restore-old-scene-objects-destroyed");

                // Restored woven basket visual verification from authoritative bindings
                var restoredBindings = new List<PhysicalItemRuntimeBinding>(richBootstrap.Bindings);
                var restoredBasketBinding = restoredBindings.Find(b => string.Equals(b.itemId, "restored-woven-basket", StringComparison.Ordinal));
                check(restoredBasketBinding != null && restoredBasketBinding.physicalItem != null, "basket-player-cold-restore-new-basket-created");
                var newBasketObj = restoredBasketBinding != null && restoredBasketBinding.physicalItem != null ? restoredBasketBinding.physicalItem.gameObject : null;
                var newBasketVisual = newBasketObj != null ? newBasketObj.GetComponentInChildren<WovenBasketVisual>() : null;
                check(newBasketVisual != null && newBasketVisual.GeneratedMesh != null, "basket-player-cold-restore-woven-basket-visual-created");
                var visualCollidersInNew = newBasketVisual != null ? newBasketVisual.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();
                check(visualCollidersInNew.Length == 0, "basket-player-cold-restore-visual-has-no-colliders");

                // Restored stored slot verification
                var restoredOcc = richBootstrap.Model.GetContainerSlotOccupants("restored-woven-basket");
                check(restoredOcc.Count == 1 && restoredOcc.ContainsKey(0) && string.Equals(restoredOcc[0], "restored-gem", StringComparison.Ordinal),
                    "basket-player-cold-restore-stored-slot-restored");
                check(richBootstrap.Bindings.Count == 2, "basket-player-cold-restore-bindings-count-exact");

                check(unexpectedLogs.Count == 0, "basket-player-no-unexpected-exceptions",
                    unexpectedLogs.Count > 0 ? string.Join("\n---\n", unexpectedLogs) : null);
                testCompletedSuccessfully = true;
            }
            finally
            {
                CleanupFixture();
            }
        }
    }
}
