using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using CityLife.World;

namespace CityLife.Items.Editor
{
    /// <summary>
    /// Isolated physics scene integration checks verifying:
    /// 1. Non-physical interactable pickup/delivery regressions.
    /// 2. Physical item pickup gating:
    ///    - PhysicalModel required for all physical items.
    ///    - Read-only metadata & definition validation.
    ///    - World/scene mismatch denial.
    ///    - Live line-of-sight occlusion denial.
    ///    - Anchored, reach, permission, capacity, and hands-full denials.
    ///    - Valid kinematic hand attachment and trigger transition.
    /// 3. Physical item delivery denial (prevents desynchronizing legacy sockets).
    /// 4. Physical item drop gating:
    ///    - Revalidation of permission, active/enabled, world/scene, ownership, and carried state.
    ///    - Full-shape collider edge clearance and swept path obstruction denials.
    ///    - Restoration of dynamic Rigidbody and gravity at hand release pose.
    /// 5. Realistic falling, continuous transform synchronization during motion, and settling:
    ///    - Settling within 5.0s.
    ///    - Settled linear speed <= 0.03 m/s, angular speed <= 0.052 rad/s (3 deg/s).
    ///    - Penetration <= 0.01m verified via Physics.ComputePenetration.
    ///    - Rest drift <= 0.02m over full 30.0s (1500 steps).
    /// 6. Idempotent drop replay post-motion without teleportation, and conflict preservation.
    /// 7. Authoritative SyncFreeTransform narrowed to trusted bound adapter with world/generation/impostor rejection.
    /// </summary>
    public static class PhysicalItemRuntimeChecks
    {
        public static List<string> Run()
        {
            var passed = new List<string>();

            var scene = SceneManager.CreateScene("PhysicalItem isolated runtime tests", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var physics = scene.GetPhysicsScene();
            var owned = new List<GameObject>();

            GameObject CreateGo(string name)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, scene);
                owned.Add(go);
                return go;
            }

            try
            {
                const string worldId = "starfall.physical-runtime.v1";
                const string genId = "gen-01";
                const string agentId = "agent-starfall-01";

                // Setup static ground floor: top surface at y = 0.0
                var floor = CreateGo("test-floor");
                floor.layer = 8;
                floor.transform.position = new Vector3(0, -0.5f, 0);
                floor.transform.localScale = new Vector3(100f, 1f, 100f);
                var floorCollider = floor.AddComponent<BoxCollider>();

                // Setup actor and hand with realistic animated hunter rig hierarchy (non-unit scale and bone rotation)
                var actorGo = CreateGo("test-actor");
                actorGo.transform.position = new Vector3(0, 0, 0);
                actorGo.transform.rotation = Quaternion.identity;
                actorGo.transform.localScale = new Vector3(1.15f, 1.0f, 1.08f);

                var handGo = CreateGo("test-hand");
                handGo.transform.SetParent(actorGo.transform, false);
                handGo.transform.localPosition = new Vector3(0.3f, 1.0f, 0.4f);
                handGo.transform.localRotation = Quaternion.Euler(15f, 25f, 0f);

                // Diagnostics-aware assertion helpers:
                void Check(bool condition, string name, string diagnostic = null)
                {
                    if (!condition)
                    {
                        string msg = !string.IsNullOrEmpty(diagnostic)
                            ? $"PHYSICAL ITEM RUNTIME CHECK FAILED: {name} ({diagnostic})"
                            : $"PHYSICAL ITEM RUNTIME CHECK FAILED: {name}";
                        throw new InvalidOperationException(msg);
                    }
                    passed.Add(name);
                }

                void CheckAction(NpcActionResult actual, string expectedCode, bool expectedSuccess,
                    bool extraCondition, string name, Transform targetApproach = null, Vector3? sightPoint = null)
                {
                    float distApproach = targetApproach != null ? Vector3.Distance(actorGo.transform.position, targetApproach.position) : -1f;
                    float distSight = sightPoint.HasValue ? Vector3.Distance(actorGo.transform.position + Vector3.up, sightPoint.Value) : -1f;
                    if (actual.success != expectedSuccess || actual.code != expectedCode || !extraCondition)
                    {
                        string diag = $"{name} (expected code='{expectedCode}', success={expectedSuccess}; actual code='{actual.code}', success={actual.success}, duplicate={actual.duplicate}, distApproach={distApproach:F3}m, distSight={distSight:F3}m)";
                        throw new InvalidOperationException("PHYSICAL ITEM RUNTIME CHECK FAILED: " + diag);
                    }
                    passed.Add(name);
                }

                // -------------------------------------------------------------
                // 1. Non-physical pickup and deliver regression fixtures
                // -------------------------------------------------------------
                // Cargo located at (0.35, 0.1, -0.15) ~0.394m from actor (<= 0.65m)
                var nonphysCargoGo = CreateGo("cargo-nonphys");
                nonphysCargoGo.transform.position = new Vector3(0.35f, 0.1f, -0.15f);
                var cargoNi = nonphysCargoGo.AddComponent<NpcInteractable>();
                cargoNi.StableId = "cargo-nonphys";
                cargoNi.WorldId = worldId;
                cargoNi.Kind = NpcObjectKind.Item;
                cargoNi.Permission = true;
                var cargoApproach = CreateGo("cargo-approach");
                cargoApproach.transform.SetParent(nonphysCargoGo.transform, false);
                cargoApproach.transform.localPosition = Vector3.zero;
                cargoNi.Approach = cargoApproach.transform;

                // Depot located at (0.35, 0.1, 0.15) ~0.394m from actor (<= 0.65m), non-overlapping with cargo
                var depotGo = CreateGo("depot-dest");
                depotGo.transform.position = new Vector3(0.35f, 0.1f, 0.15f);
                var depotNi = depotGo.AddComponent<NpcInteractable>();
                depotNi.StableId = "depot-dest";
                depotNi.WorldId = worldId;
                depotNi.Kind = NpcObjectKind.Destination;
                depotNi.Permission = true;
                var depotApproach = CreateGo("depot-approach");
                depotApproach.transform.SetParent(depotGo.transform, false);
                depotApproach.transform.localPosition = Vector3.zero;
                depotNi.Approach = depotApproach.transform;
                var depotSocket = CreateGo("depot-socket");
                depotSocket.transform.SetParent(depotGo.transform, false);
                depotSocket.transform.localPosition = Vector3.up * 0.1f;
                depotNi.Socket = depotSocket.transform;

                // Distinct reachable target for hands-full check: situated at (0.15, 0.2, 0.15) ~0.292m from actor
                var extraCargoGo = CreateGo("extra-reachable-item");
                extraCargoGo.transform.position = new Vector3(0.15f, 0.2f, 0.15f);
                var extraNi = extraCargoGo.AddComponent<NpcInteractable>();
                extraNi.StableId = "extra-reachable-item";
                extraNi.WorldId = worldId;
                extraNi.Kind = NpcObjectKind.Item;
                extraNi.Permission = true;
                var extraApproach = CreateGo("extra-approach");
                extraApproach.transform.SetParent(extraCargoGo.transform, false);
                extraApproach.transform.localPosition = Vector3.zero;
                extraNi.Approach = extraApproach.transform;

                var registry = new List<NpcInteractable> { cargoNi, depotNi, extraNi };

                // -------------------------------------------------------------
                // 2. Physical items fixtures with isolated non-overlapping positions
                // -------------------------------------------------------------
                // Model initialization
                var model = new ItemModel(worldId, genId);

                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "type-wood-block",
                    dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                    massKg = 2f,
                    isAnchored = false
                });
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "type-anvil",
                    dimensions = new PhysicalDimensions(0.4f, 0.3f, 0.4f),
                    massKg = 50f,
                    isAnchored = true
                });
                model.RegisterDefinition(new ItemDefinition
                {
                    itemTypeId = "type-boulder",
                    dimensions = new PhysicalDimensions(0.5f, 0.5f, 0.5f),
                    massKg = 30f,
                    isAnchored = false
                });

                // Set carry limit: 25kg
                model.SetActorCarryLimits(agentId, new ActorCarryLimits(25f, 1));

                // Item 1: Standard wood block at (0.3, 0.2, 0.35) ~0.503m from actor (<= 0.65m)
                var physWoodGo = CreateGo("phys-wood");
                physWoodGo.transform.position = new Vector3(0.3f, 0.2f, 0.35f);
                var woodNi = physWoodGo.AddComponent<NpcInteractable>();
                woodNi.StableId = "phys-wood";
                woodNi.WorldId = worldId;
                woodNi.Kind = NpcObjectKind.Item;
                woodNi.Permission = true;
                var woodApproach = CreateGo("wood-approach");
                woodApproach.transform.SetParent(physWoodGo.transform, false);
                woodApproach.transform.localPosition = Vector3.zero;
                woodNi.Approach = woodApproach.transform;

                var woodPhys = physWoodGo.AddComponent<PhysicalItem>();
                woodPhys.itemId = "phys-wood";
                woodPhys.itemTypeId = "type-wood-block";
                woodPhys.massKg = 2f;
                woodPhys.dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f);
                woodPhys.ConfigureComponents();
                woodPhys.Bind(model, worldId, genId);
                registry.Add(woodNi);
                model.RegisterItem("phys-wood", "type-wood-block", ItemLocationKind.Free, physWoodGo.transform.position, Quaternion.identity);

                // Item 2: Anchored anvil at (-0.4, 0.2, 0.2) ~0.490m from actor, separated on -X side
                var anvilGo = CreateGo("anchored-anvil");
                anvilGo.transform.position = new Vector3(-0.4f, 0.2f, 0.2f);
                var anvilNi = anvilGo.AddComponent<NpcInteractable>();
                anvilNi.StableId = "anchored-anvil";
                anvilNi.WorldId = worldId;
                anvilNi.Kind = NpcObjectKind.Item;
                anvilNi.Permission = true;
                var anvilApproach = CreateGo("anvil-approach");
                anvilApproach.transform.SetParent(anvilGo.transform, false);
                anvilApproach.transform.localPosition = Vector3.zero;
                anvilNi.Approach = anvilApproach.transform;

                var anvilPhys = anvilGo.AddComponent<PhysicalItem>();
                anvilPhys.itemId = "anchored-anvil";
                anvilPhys.itemTypeId = "type-anvil";
                anvilPhys.massKg = 50f;
                anvilPhys.dimensions = new PhysicalDimensions(0.4f, 0.3f, 0.4f);
                anvilPhys.isAnchored = true;
                anvilPhys.ConfigureComponents();
                anvilPhys.Bind(model, worldId, genId);
                registry.Add(anvilNi);
                model.RegisterItem("anchored-anvil", "type-anvil", ItemLocationKind.Anchored, anvilGo.transform.position, Quaternion.identity);

                // Item 3: Heavy boulder (30kg) at (-0.2, 0.25, 0.35) ~0.474m from actor, separated on -X side
                var heavyGo = CreateGo("heavy-boulder");
                heavyGo.transform.position = new Vector3(-0.2f, 0.25f, 0.35f);
                var heavyNi = heavyGo.AddComponent<NpcInteractable>();
                heavyNi.StableId = "heavy-boulder";
                heavyNi.WorldId = worldId;
                heavyNi.Kind = NpcObjectKind.Item;
                heavyNi.Permission = true;
                var heavyApproach = CreateGo("heavy-approach");
                heavyApproach.transform.SetParent(heavyGo.transform, false);
                heavyApproach.transform.localPosition = Vector3.zero;
                heavyNi.Approach = heavyApproach.transform;

                var heavyPhys = heavyGo.AddComponent<PhysicalItem>();
                heavyPhys.itemId = "heavy-boulder";
                heavyPhys.itemTypeId = "type-boulder";
                heavyPhys.massKg = 30f;
                heavyPhys.dimensions = new PhysicalDimensions(0.5f, 0.5f, 0.5f);
                heavyPhys.ConfigureComponents();
                heavyPhys.Bind(model, worldId, genId);
                registry.Add(heavyNi);
                model.RegisterItem("heavy-boulder", "type-boulder", ItemLocationKind.Free, heavyGo.transform.position, Quaternion.identity);

                // Item 4: Distant item at (15.0, 0.2, 15.0) for out-of-reach testing
                var distantGo = CreateGo("distant-item");
                distantGo.transform.position = new Vector3(15.0f, 0.2f, 15.0f);
                var distantNi = distantGo.AddComponent<NpcInteractable>();
                distantNi.StableId = "distant-item";
                distantNi.WorldId = worldId;
                distantNi.Kind = NpcObjectKind.Item;
                distantNi.Permission = true;
                var distantApproach = CreateGo("distant-approach");
                distantApproach.transform.SetParent(distantGo.transform, false);
                distantApproach.transform.localPosition = Vector3.zero;
                distantNi.Approach = distantApproach.transform;

                var distantPhys = distantGo.AddComponent<PhysicalItem>();
                distantPhys.itemId = "distant-item";
                distantPhys.itemTypeId = "type-wood-block";
                distantPhys.massKg = 2f;
                distantPhys.dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f);
                distantPhys.ConfigureComponents();
                distantPhys.Bind(model, worldId, genId);
                registry.Add(distantNi);
                model.RegisterItem("distant-item", "type-wood-block", ItemLocationKind.Free, distantGo.transform.position, Quaternion.identity);

                // Item 5: Hidden item behind solid obstacle at (0.0, 0.2, -0.6) ~0.632m from actor (<= 0.65m)
                var hiddenGo = CreateGo("hidden-item");
                hiddenGo.transform.position = new Vector3(0.0f, 0.2f, -0.6f);
                var hiddenNi = hiddenGo.AddComponent<NpcInteractable>();
                hiddenNi.StableId = "hidden-item";
                hiddenNi.WorldId = worldId;
                hiddenNi.Kind = NpcObjectKind.Item;
                hiddenNi.Permission = true;
                var hiddenApproach = CreateGo("hidden-approach");
                hiddenApproach.transform.SetParent(hiddenGo.transform, false);
                hiddenApproach.transform.localPosition = Vector3.zero;
                hiddenNi.Approach = hiddenApproach.transform;

                var hiddenPhys = hiddenGo.AddComponent<PhysicalItem>();
                hiddenPhys.itemId = "hidden-item";
                hiddenPhys.itemTypeId = "type-wood-block";
                hiddenPhys.massKg = 2f;
                hiddenPhys.dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f);
                hiddenPhys.ConfigureComponents();
                hiddenPhys.Bind(model, worldId, genId);
                registry.Add(hiddenNi);
                model.RegisterItem("hidden-item", "type-wood-block", ItemLocationKind.Free, hiddenGo.transform.position, Quaternion.identity);

                // Obstacle wall occluding hidden-item on layer 8 at z = -0.3
                var losWall = CreateGo("los-wall");
                losWall.layer = 8;
                losWall.transform.position = new Vector3(0.0f, 0.8f, -0.3f);
                losWall.transform.localScale = new Vector3(2.0f, 2.0f, 0.2f);
                losWall.AddComponent<BoxCollider>();

                // Item 6: Metadata mismatched item at (-0.35, 0.2, -0.2) ~0.450m from actor
                var mismatchGo = CreateGo("mismatch-item");
                mismatchGo.transform.position = new Vector3(-0.35f, 0.2f, -0.2f);
                var mismatchNi = mismatchGo.AddComponent<NpcInteractable>();
                mismatchNi.StableId = "mismatch-item";
                mismatchNi.WorldId = worldId;
                mismatchNi.Kind = NpcObjectKind.Item;
                mismatchNi.Permission = true;
                var mismatchApproach = CreateGo("mismatch-approach");
                mismatchApproach.transform.SetParent(mismatchGo.transform, false);
                mismatchApproach.transform.localPosition = Vector3.zero;
                mismatchNi.Approach = mismatchApproach.transform;

                var mismatchPhys = mismatchGo.AddComponent<PhysicalItem>();
                mismatchPhys.itemId = "mismatch-item";
                mismatchPhys.itemTypeId = "type-wood-block";
                mismatchPhys.massKg = 99.0f; // Mismatches model definition (2.0kg)
                mismatchPhys.dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f);
                mismatchPhys.ConfigureComponents();
                mismatchPhys.Bind(model, worldId, genId);
                registry.Add(mismatchNi);
                model.RegisterItem("mismatch-item", "type-wood-block", ItemLocationKind.Free, mismatchGo.transform.position, Quaternion.identity);

                // Item 7: Unsupported non-unit scale item at (-0.4, 0.2, 0.0) ~0.447m from actor
                var unsuppScaleGo = CreateGo("unsupp-scale-item");
                unsuppScaleGo.transform.position = new Vector3(-0.4f, 0.2f, 0.0f);
                unsuppScaleGo.transform.localScale = new Vector3(2.0f, 1.0f, 1.0f); // Non-unit scale
                var unsuppScaleNi = unsuppScaleGo.AddComponent<NpcInteractable>();
                unsuppScaleNi.StableId = "unsupp-scale-item";
                unsuppScaleNi.WorldId = worldId;
                unsuppScaleNi.Kind = NpcObjectKind.Item;
                unsuppScaleNi.Permission = true;
                var unsuppScaleApproach = CreateGo("unsupp-scale-approach");
                unsuppScaleApproach.transform.SetParent(unsuppScaleGo.transform, false);
                unsuppScaleApproach.transform.localPosition = Vector3.zero;
                unsuppScaleNi.Approach = unsuppScaleApproach.transform;

                var unsuppScalePhys = unsuppScaleGo.AddComponent<PhysicalItem>();
                unsuppScalePhys.itemId = "unsupp-scale-item";
                unsuppScalePhys.itemTypeId = "type-wood-block";
                unsuppScalePhys.massKg = 2.0f;
                unsuppScalePhys.dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f);
                unsuppScalePhys.ConfigureComponents();
                unsuppScalePhys.Bind(model, worldId, genId);
                registry.Add(unsuppScaleNi);
                model.RegisterItem("unsupp-scale-item", "type-wood-block", ItemLocationKind.Free, unsuppScaleGo.transform.position, Quaternion.identity);

                // Item 8: Wrong world item at (-0.15, 0.2, -0.35) ~0.430m from actor
                var wrongWorldGo = CreateGo("wrong-world-item");
                wrongWorldGo.transform.position = new Vector3(-0.15f, 0.2f, -0.35f);
                var wrongWorldNi = wrongWorldGo.AddComponent<NpcInteractable>();
                wrongWorldNi.StableId = "wrong-world-item";
                wrongWorldNi.WorldId = "other-alien-world.v1"; // Mismatch!
                wrongWorldNi.Kind = NpcObjectKind.Item;
                wrongWorldNi.Permission = true;
                var wrongWorldApproach = CreateGo("wrong-world-approach");
                wrongWorldApproach.transform.SetParent(wrongWorldGo.transform, false);
                wrongWorldApproach.transform.localPosition = Vector3.zero;
                wrongWorldNi.Approach = wrongWorldApproach.transform;

                var wrongWorldPhys = wrongWorldGo.AddComponent<PhysicalItem>();
                wrongWorldPhys.itemId = "wrong-world-item";
                wrongWorldPhys.itemTypeId = "type-wood-block";
                wrongWorldPhys.massKg = 2.0f;
                wrongWorldPhys.dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f);
                wrongWorldPhys.ConfigureComponents();
                wrongWorldPhys.Bind(model, worldId, genId);
                registry.Add(wrongWorldNi);

                Physics.SyncTransforms();

                // Setup NpcActionApi
                var api = new NpcActionApi(agentId, worldId, actorGo.transform, handGo.transform, registry);

                // -------------------------------------------------------------
                // 3. Regression checks: Non-physical interactables
                // -------------------------------------------------------------
                var nonphysRes = api.Execute(1, NpcActionKind.Pickup, "cargo-nonphys");
                CheckAction(nonphysRes, "picked-up", true,
                    api.Held == cargoNi && cargoNi.transform.parent == handGo.transform,
                    "nonphysical-pickup-regression",
                    cargoNi.Approach, cargoNi.SightPoint);

                var deliverRes = api.Execute(2, NpcActionKind.Deliver, "depot-dest");
                CheckAction(deliverRes, "delivered", true,
                    api.Held == null && cargoNi.DeliveredTo == "depot-dest" && depotNi.Occupant == "cargo-nonphys" && api.Deliveries == 1,
                    "nonphysical-deliver-regression",
                    depotNi.Approach, depotNi.SightPoint);

                // -------------------------------------------------------------
                // 4. Physical pickup validations & denials (Finding 1)
                // -------------------------------------------------------------
                // Check: PhysicalModel is required for all physical items
                api.PhysicalModel = null;
                var noModelRes = api.Execute(3, NpcActionKind.Pickup, "phys-wood");
                CheckAction(noModelRes, "physical-model-required", false,
                    api.Held == null,
                    "physical-pickup-denied-when-missing-model",
                    woodNi.Approach, woodNi.SightPoint);
                api.PhysicalModel = model;

                // Check: World mismatch rejected
                var wrongWorldRes = api.Execute(4, NpcActionKind.Pickup, "wrong-world-item");
                CheckAction(wrongWorldRes, "world-mismatch", false,
                    api.Held == null,
                    "physical-pickup-denied-when-world-mismatch",
                    wrongWorldNi.Approach, wrongWorldNi.SightPoint);

                // Check: Line of sight blocked by obstacle wall
                var losRes = api.Execute(5, NpcActionKind.Pickup, "hidden-item");
                CheckAction(losRes, "line-of-sight-blocked", false,
                    api.Held == null,
                    "physical-pickup-denied-when-los-blocked",
                    hiddenNi.Approach, hiddenNi.SightPoint);

                // Check: Anchored physical item denied
                var anchorRes = api.Execute(6, NpcActionKind.Pickup, "anchored-anvil");
                CheckAction(anchorRes, "anchored-item-cannot-be-picked-up", false,
                    api.Held == null,
                    "physical-pickup-denied-when-anchored",
                    anvilNi.Approach, anvilNi.SightPoint);

                // Check: Out of reach physical item denied
                var reachRes = api.Execute(7, NpcActionKind.Pickup, "distant-item");
                CheckAction(reachRes, "out-of-reach", false,
                    api.Held == null,
                    "physical-pickup-denied-when-out-of-reach",
                    distantNi.Approach, distantNi.SightPoint);

                // Check: Carry mass capacity exceeded (30kg > 25kg)
                var massRes = api.Execute(8, NpcActionKind.Pickup, "heavy-boulder");
                CheckAction(massRes, "carry-mass-capacity-exceeded", false,
                    api.Held == null,
                    "physical-pickup-denied-when-carry-mass-exceeded",
                    heavyNi.Approach, heavyNi.SightPoint);

                // Check: Metadata mismatch between component and model denied
                var mismatchRes = api.Execute(9, NpcActionKind.Pickup, "mismatch-item");
                CheckAction(mismatchRes, "physical-metadata-mismatch", false,
                    api.Held == null,
                    "physical-pickup-denied-when-metadata-mismatched",
                    mismatchNi.Approach, mismatchNi.SightPoint);

                // Check: Unsupported non-unit scale denied by read-only IsValid
                var unsuppScaleRes = api.Execute(10, NpcActionKind.Pickup, "unsupp-scale-item");
                CheckAction(unsuppScaleRes, "physical-item-invalid", false,
                    api.Held == null,
                    "physical-pickup-denied-when-unsupported-scale",
                    unsuppScaleNi.Approach, unsuppScaleNi.SightPoint);

                // Check: Permission revoked denied
                woodNi.Permission = false;
                var permRes = api.Execute(11, NpcActionKind.Pickup, "phys-wood");
                CheckAction(permRes, "permission-denied", false,
                    api.Held == null,
                    "physical-pickup-denied-when-permission-revoked",
                    woodNi.Approach, woodNi.SightPoint);
                woodNi.Permission = true;

                // Check: Valid physical pickup transitions to kinematic carry via scale-neutral root follower
                var pickupRes = api.Execute(12, NpcActionKind.Pickup, "phys-wood");
                Vector3 expectedHandPose = handGo.transform.TransformPoint(woodPhys.GripLocalOffset);
                Quaternion expectedHandRot = handGo.transform.rotation * woodPhys.GripLocalRotation;
                bool handPoseMatches = Vector3.Distance(woodNi.transform.position, expectedHandPose) <= 0.001f &&
                    Quaternion.Angle(woodNi.transform.rotation, expectedHandRot) <= 0.1f;
                bool unitLossyScale = Mathf.Abs(woodPhys.transform.lossyScale.x - 1f) <= 0.001f &&
                    Mathf.Abs(woodPhys.transform.lossyScale.y - 1f) <= 0.001f &&
                    Mathf.Abs(woodPhys.transform.lossyScale.z - 1f) <= 0.001f;
                bool declaredBoxSizePreserved = woodPhys.ItemCollider is BoxCollider woodBox &&
                    Mathf.Abs(woodBox.size.x * woodPhys.transform.lossyScale.x - 0.2f) <= 0.001f &&
                    Mathf.Abs(woodBox.size.y * woodPhys.transform.lossyScale.y - 0.2f) <= 0.001f &&
                    Mathf.Abs(woodBox.size.z * woodPhys.transform.lossyScale.z - 0.2f) <= 0.001f;

                CheckAction(pickupRes, "picked-up", true,
                    api.Held == woodNi && woodPhys.IsCarried && woodPhys.CarriedHand == handGo.transform &&
                    woodNi.transform.parent == null && unitLossyScale && handPoseMatches && declaredBoxSizePreserved &&
                    woodPhys.Body.isKinematic && !woodPhys.Body.useGravity && woodPhys.ItemCollider.isTrigger,
                    "physical-pickup-attaches-to-hand-kinematic",
                    woodNi.Approach, woodNi.SightPoint);

                // Check: ItemModel updated to Carried
                Check(model.TryGetItem("phys-wood", out var carriedSnap) && carriedSnap.location == ItemLocationKind.Carried &&
                      carriedSnap.holderActorId == agentId, "physical-pickup-updates-model-state");

                // Check: Hands full prevents second pickup using distinct currently reachable target
                var handsFullRes = api.Execute(13, NpcActionKind.Pickup, "extra-reachable-item");
                CheckAction(handsFullRes, "hands-full", false,
                    api.Held == woodNi,
                    "physical-pickup-denied-when-hands-full",
                    extraNi.Approach, extraNi.SightPoint);

                // Check: Legacy socket delivery explicitly denied for physical items
                var physDeliverRes = api.Execute(14, NpcActionKind.Deliver, "depot-dest");
                CheckAction(physDeliverRes, "physical-delivery-not-supported-in-slice", false,
                    api.Held == woodNi,
                    "physical-delivery-denied-for-physical-item",
                    depotNi.Approach, depotNi.SightPoint);

                // -------------------------------------------------------------
                // Freeze unrelated dynamic fixtures so they cannot collide into falling wood
                // -------------------------------------------------------------
                heavyPhys.Body.isKinematic = true;
                heavyPhys.Body.linearVelocity = Vector3.zero;
                heavyPhys.Body.angularVelocity = Vector3.zero;

                distantPhys.Body.isKinematic = true;
                hiddenPhys.Body.isKinematic = true;
                mismatchPhys.Body.isKinematic = true;
                unsuppScalePhys.Body.isKinematic = true;
                wrongWorldPhys.Body.isKinematic = true;
                anvilPhys.Body.isKinematic = true;

                // -------------------------------------------------------------
                // 5. Drop validations & full-shape clearance checks (Findings 2 & 3)
                // -------------------------------------------------------------
                // Check: Permission revoked after pickup denies Drop without altering state
                woodNi.Permission = false;
                var permDropRes = api.Execute(15, NpcActionKind.Drop, "phys-wood");
                CheckAction(permDropRes, "permission-denied", false,
                    api.Held == woodNi && woodPhys.IsCarried && woodPhys.Body.isKinematic,
                    "physical-drop-denied-when-permission-revoked-after-pickup");
                woodNi.Permission = true;

                // Check: World mismatch after pickup denies Drop
                woodNi.WorldId = "altered-world-id";
                var worldDropRes = api.Execute(16, NpcActionKind.Drop, "phys-wood");
                CheckAction(worldDropRes, "world-mismatch", false,
                    api.Held == woodNi && woodPhys.IsCarried && woodPhys.Body.isKinematic,
                    "physical-drop-denied-when-world-mismatched-after-pickup");
                woodNi.WorldId = worldId;

                Vector3 expectedReleasePos = handGo.transform.position + handGo.transform.forward * 0.25f;

                // Check: Full-shape edge clearance denial (wall edge intersects item bound even though center is clear)
                // Wood dimensions: 0.2m x 0.2m x 0.2m (half-extents 0.1m). Place obstacle at offset along hand right axis
                var edgeObstacle = CreateGo("edge-obstacle");
                edgeObstacle.layer = 8;
                edgeObstacle.transform.position = expectedReleasePos + handGo.transform.right * 0.12f;
                edgeObstacle.transform.rotation = handGo.transform.rotation;
                edgeObstacle.transform.localScale = new Vector3(0.1f, 0.4f, 0.4f);
                edgeObstacle.AddComponent<BoxCollider>();
                Physics.SyncTransforms();

                var edgeDropRes = api.Execute(17, NpcActionKind.Drop, "phys-wood");
                CheckAction(edgeDropRes, "release-clearance-blocked", false,
                    api.Held == woodNi && woodPhys.IsCarried,
                    "physical-drop-denied-when-full-shape-edge-collides");

                UnityEngine.Object.DestroyImmediate(edgeObstacle);
                Physics.SyncTransforms();

                // Check: Swept path clearance denial (wall between held pose and release pose)
                Vector3 heldPoseCenter = woodNi.transform.position;
                Vector3 midSweepPoint = (heldPoseCenter + expectedReleasePos) * 0.5f;
                var sweepObstacle = CreateGo("sweep-obstacle");
                sweepObstacle.layer = 8;
                sweepObstacle.transform.position = midSweepPoint;
                sweepObstacle.transform.rotation = handGo.transform.rotation;
                sweepObstacle.transform.localScale = new Vector3(0.4f, 0.4f, 0.05f);
                sweepObstacle.AddComponent<BoxCollider>();
                Physics.SyncTransforms();

                var sweepDropRes = api.Execute(18, NpcActionKind.Drop, "phys-wood");
                CheckAction(sweepDropRes, "release-clearance-blocked", false,
                    api.Held == woodNi && woodPhys.IsCarried,
                    "physical-drop-denied-when-swept-path-blocked");

                UnityEngine.Object.DestroyImmediate(sweepObstacle);
                Physics.SyncTransforms();

                // Check: AllowDrop = false denies Drop
                api.AllowDrop = false;
                var allowDropRes = api.Execute(19, NpcActionKind.Drop, "phys-wood");
                CheckAction(allowDropRes, "permission-denied", false,
                    api.Held == woodNi,
                    "physical-drop-denied-when-permission-denied");
                api.AllowDrop = true;

                // Check: Target ID mismatch denies Drop
                var idMismatchDrop = api.Execute(20, NpcActionKind.Drop, "wrong-cargo-id");
                CheckAction(idMismatchDrop, "cargo-ownership-mismatch", false,
                    api.Held == woodNi,
                    "physical-drop-denied-when-target-id-mismatched");

                // -------------------------------------------------------------
                // 6. Successful dynamic Drop & physics restoration (Finding 5)
                // -------------------------------------------------------------
                var dropRes = api.Execute(21, NpcActionKind.Drop, "phys-wood");
                bool dropUnitLossyScale = Mathf.Abs(woodPhys.transform.lossyScale.x - 1f) <= 0.001f &&
                    Mathf.Abs(woodPhys.transform.lossyScale.y - 1f) <= 0.001f &&
                    Mathf.Abs(woodPhys.transform.lossyScale.z - 1f) <= 0.001f;
                bool dropBoxSizePreserved = woodPhys.ItemCollider is BoxCollider woodBoxDrop &&
                    Mathf.Abs(woodBoxDrop.size.x * woodPhys.transform.lossyScale.x - 0.2f) <= 0.001f &&
                    Mathf.Abs(woodBoxDrop.size.y * woodPhys.transform.lossyScale.y - 0.2f) <= 0.001f &&
                    Mathf.Abs(woodBoxDrop.size.z * woodPhys.transform.lossyScale.z - 0.2f) <= 0.001f;

                CheckAction(dropRes, "dropped", true,
                    api.Held == null && woodNi.transform.parent == null && !woodPhys.IsCarried &&
                    woodPhys.CarriedHand == null && dropUnitLossyScale && dropBoxSizePreserved &&
                    !woodPhys.Body.isKinematic && woodPhys.Body.useGravity &&
                    !woodPhys.ItemCollider.isTrigger && woodPhys.ItemCollider.enabled &&
                    woodPhys.Body.collisionDetectionMode == CollisionDetectionMode.ContinuousSpeculative,
                    "physical-drop-releases-to-physics-dynamic");


                Check(Vector3.Distance(physWoodGo.transform.position, expectedReleasePos) <= 0.001f,
                    "physical-drop-position-at-hand-release-pose",
                    $"actual={physWoodGo.transform.position}, expected={expectedReleasePos}");

                Check(model.TryGetItem("phys-wood", out var droppedSnap) && droppedSnap.location == ItemLocationKind.Free,
                    "physical-drop-updates-model-to-free");

                // Idempotent drop replay immediately after drop
                var dropReplay = api.Execute(21, NpcActionKind.Drop, "phys-wood");
                CheckAction(dropReplay, "dropped", true,
                    dropReplay.duplicate,
                    "physical-drop-idempotency-replay-before-move");

                // -------------------------------------------------------------
                // 7. Motion, live synchronization, falling, contact, and settling (Findings 4 & 6)
                // -------------------------------------------------------------
                // Step physics: verify gravity acceleration and continuous transform sync during movement
                for (int i = 0; i < 10; i++)
                {
                    physics.Simulate(0.02f);
                    woodPhys.SyncToModel();
                }

                Check(physWoodGo.transform.position.y < expectedReleasePos.y && woodPhys.Body.linearVelocity.y < 0f,
                    "physical-falling-accelerates-under-gravity",
                    $"y={physWoodGo.transform.position.y:F3} < releaseY={expectedReleasePos.y:F3}, vy={woodPhys.Body.linearVelocity.y:F3}");

                Check(model.TryGetItem("phys-wood", out var inFlightSnap) &&
                      Vector3.Distance(inFlightSnap.position, physWoodGo.transform.position) <= 0.001f,
                    "physical-moving-body-syncs-authoritative-pose");

                // Step to floor contact and settling
                bool settled = false;
                float settleTime = 0f;
                for (int i = 0; i < 200; i++)
                {
                    physics.Simulate(0.02f);
                    woodPhys.SyncToModel();
                    float elapsed = (10 + i + 1) * 0.02f;
                    float linSpeed = woodPhys.Body.linearVelocity.magnitude;
                    float angSpeed = woodPhys.Body.angularVelocity.magnitude;

                    // Thresholds: linear speed <= 0.03 m/s, angular speed <= 0.052 rad/s (3 deg/s)
                    if (elapsed >= 0.4f && linSpeed <= 0.03f && angSpeed <= 0.052f)
                    {
                        settled = true;
                        settleTime = elapsed;
                        break;
                    }
                }

                Check(settled, "physical-settling-achieved-within-5s",
                    $"settled={settled}, elapsed={settleTime:F3}s, linSpeed={woodPhys.Body.linearVelocity.magnitude:F4}m/s, angSpeed={woodPhys.Body.angularVelocity.magnitude:F4}rad/s");
                Check(settleTime <= 5.0f, "physical-settling-time-budget",
                    $"settleTime={settleTime:F3}s <= 5.0s");
                Check(woodPhys.Body.linearVelocity.magnitude <= 0.03f, "physical-settled-linear-speed-tolerance",
                    $"linSpeed={woodPhys.Body.linearVelocity.magnitude:F4}m/s <= 0.03m/s");
                Check(woodPhys.Body.angularVelocity.magnitude <= 0.052f, "physical-settled-angular-speed-tolerance",
                    $"angSpeed={woodPhys.Body.angularVelocity.magnitude:F4}rad/s <= 0.052rad/s");

                // Penetration tolerance check using Physics.ComputePenetration
                bool overlapFound = Physics.ComputePenetration(
                    woodPhys.ItemCollider, physWoodGo.transform.position, physWoodGo.transform.rotation,
                    floorCollider, floor.transform.position, floor.transform.rotation,
                    out Vector3 penDir, out float penDist);

                // Penetration depth must be <= 0.01m (10mm)
                Check(penDist <= 0.01f, "physical-penetration-within-tolerance",
                    $"penDist={penDist:F4}m <= 0.01m");

                // Full 30.0s rest drift check (1500 steps of 0.02s)
                Vector3 restStartPos = physWoodGo.transform.position;
                for (int i = 0; i < 1500; i++)
                {
                    physics.Simulate(0.02f);
                }
                Vector3 restEndPos = physWoodGo.transform.position;
                float restDrift30s = Vector3.Distance(restStartPos, restEndPos);
                Check(restDrift30s <= 0.02f, "physical-rest-drift-within-budget-30s",
                    $"drift30s={restDrift30s:F4}m <= 0.02m");

                // -------------------------------------------------------------
                // 8. Post-motion replay & conflict checks (Finding 6)
                // -------------------------------------------------------------
                // Replay of drop request after body settled must NOT teleport the body back to the drop release pose
                Vector3 preReplayPos = physWoodGo.transform.position;
                var postMoveReplay = api.Execute(21, NpcActionKind.Drop, "phys-wood");
                CheckAction(postMoveReplay, "dropped", true,
                    postMoveReplay.duplicate && Vector3.Distance(physWoodGo.transform.position, preReplayPos) <= 0.0001f,
                    "physical-drop-replay-after-settle-does-not-teleport");

                // Request ID conflict check: same request ID with different payload denies and preserves state
                var conflictDrop = api.Execute(21, NpcActionKind.Drop, "different-target");
                CheckAction(conflictDrop, "request-id-conflict", false,
                    Vector3.Distance(physWoodGo.transform.position, preReplayPos) <= 0.0001f,
                    "physical-drop-conflict-preserves-state");

                // -------------------------------------------------------------
                // 9. Authoritative SyncFreeTransform & Impostor Rejection (Finding 4)
                // -------------------------------------------------------------
                bool syncOk = api.SyncFreeTransform("phys-wood");
                Check(syncOk, "sync-free-transform-authoritative-update");

                Check(model.TryGetItem("phys-wood", out var syncedItem) &&
                      Vector3.Distance(syncedItem.position, physWoodGo.transform.position) <= 0.001f &&
                      syncedItem.location == ItemLocationKind.Free,
                      "synced-item-matches-settled-transform");

                // Unregistered / impostor object with same ID cannot sync state
                var impostorGo = CreateGo("impostor-wood");
                impostorGo.transform.position = new Vector3(99f, 99f, 99f);
                var impostorPhys = impostorGo.AddComponent<PhysicalItem>();
                impostorPhys.itemId = "phys-wood"; // Same ID as real item!
                impostorPhys.itemTypeId = "type-wood-block";
                impostorPhys.massKg = 2.0f;
                impostorPhys.ConfigureComponents();
                // Impostor is not registered in api and not bound to model

                bool impostorSync = impostorPhys.SyncToModel();
                Check(!impostorSync, "impostor-object-cannot-sync-state");

                // Verify model position did NOT change to impostor position
                model.TryGetItem("phys-wood", out var afterImpostorSnap);
                Check(Vector3.Distance(afterImpostorSnap.position, preReplayPos) <= 0.001f,
                      "impostor-attempt-preserved-model-state");

                // -------------------------------------------------------------
                // 10. Re-pickup of settled item & transform sync rejection cases
                // -------------------------------------------------------------
                // Navigate actor to settled position
                actorGo.transform.position = physWoodGo.transform.position;
                woodApproach.transform.position = physWoodGo.transform.position;
                Physics.SyncTransforms();

                var repickRes = api.Execute(22, NpcActionKind.Pickup, "phys-wood");
                CheckAction(repickRes, "picked-up", true,
                    api.Held == woodNi && woodPhys.IsCarried && woodPhys.CarriedHand == handGo.transform && woodNi.transform.parent == null,
                    "re-pickup-of-settled-physical-item",
                    woodNi.Approach, woodNi.SightPoint);

                // Reject sync when item is Carried
                bool syncCarriedRejected = model.SyncFreeTransform(worldId, genId, "phys-wood", new Vector3(5, 5, 5), Quaternion.identity);
                Check(!syncCarriedRejected, "sync-free-transform-rejects-carried-item");

                // Reject sync when item is Anchored
                bool syncAnchoredRejected = model.SyncFreeTransform(worldId, genId, "anchored-anvil", new Vector3(5, 5, 5), Quaternion.identity);
                Check(!syncAnchoredRejected, "sync-free-transform-rejects-anchored-item");

                // Reject NaN position
                bool syncNanRejected = model.SyncFreeTransform(worldId, genId, "heavy-boulder", new Vector3(float.NaN, 0, 0), Quaternion.identity);
                Check(!syncNanRejected, "sync-free-transform-rejects-nan-position");

                // Reject unnormalized zero quaternion
                bool syncZeroRotRejected = model.SyncFreeTransform(worldId, genId, "heavy-boulder", Vector3.zero, new Quaternion(0, 0, 0, 0));
                Check(!syncZeroRotRejected, "sync-free-transform-rejects-zero-quaternion");

                // Reject wrong world ID
                bool syncWorldMismatch = model.SyncFreeTransform("wrong-world-id", genId, "heavy-boulder", Vector3.zero, Quaternion.identity);
                Check(!syncWorldMismatch, "sync-free-transform-rejects-world-mismatch");

                // Reject wrong generation ID
                bool syncGenMismatch = model.SyncFreeTransform(worldId, "wrong-gen-id", "heavy-boulder", Vector3.zero, Quaternion.identity);
                Check(!syncGenMismatch, "sync-free-transform-rejects-generation-mismatch");

                // -------------------------------------------------------------
                // 11. Multi-Item Stored Graph Runtime Restore Suite (Additive)
                // -------------------------------------------------------------
                PhysicalItemRuntimeRestoreChecks.Run(scene, physics, CreateGo, Check, passed, worldId, genId, agentId, actorGo, handGo);

                // -------------------------------------------------------------
                // 12. Multi-Instance Bootstrap and Cold Restore Suite (Additive)
                // -------------------------------------------------------------
                PhysicalItemBootstrapRuntimeChecks.Run(scene, physics, CreateGo, Check, passed, worldId, genId, agentId, actorGo, handGo);

                // -------------------------------------------------------------
                // 13. Production Transactional Basket & Stable Slots Suite (Additive)
                // -------------------------------------------------------------
                BasketTransactionChecks.Run(scene, physics, CreateGo, Check, passed, worldId, genId, agentId, actorGo, handGo);
            }
            finally
            {
                foreach (var go in owned)
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                }
                if (scene.IsValid())
                {
                    SceneManager.UnloadSceneAsync(scene);
                }
            }

            return passed;
        }
    }

    internal static class PhysicalItemRuntimeRestoreChecks
    {
        private struct RendererState
        {
            public Renderer renderer;
            public bool enabled;
        }

        private sealed class PhysicalItemState
        {
            public Vector3 pos;
            public Quaternion rot;
            public Vector3 scale;
            public Transform parent;
            public bool isCarried;
            public bool isStored;
            public Transform carriedHand;
            public string boundContainerId;
            public ItemModel boundModel;
            public string boundWorldId;
            public string boundGenId;
            public float massKg;
            public PhysicalDimensions dimensions;
            public bool isAnchored;

            public Rigidbody body;
            public float bodyMass;
            public bool isKinematic;
            public bool useGravity;
            public Vector3 linearVelocity;
            public Vector3 angularVelocity;

            public Collider collider;
            public bool colEnabled;
            public bool isTrigger;

            public List<RendererState> renderers;

            public NpcInteractable interactable;
            public string stableId;
            public string worldId;
            public NpcObjectKind kind;
            public bool permission;
            public string heldBy;
        }

        private sealed class ExtraObjectState
        {
            public GameObject go;
            public Vector3 pos;
            public Quaternion rot;
            public Vector3 scale;
            public Transform parent;
            public Rigidbody body;
            public float bodyMass;
            public bool isKinematic;
            public bool useGravity;
            public Vector3 linearVelocity;
            public Vector3 angularVelocity;
        }

        private sealed class FullSceneState
        {
            public long tick;
            public int highestReceiptId;
            public int itemCount;
            public List<ItemStateSnapshot> itemSnapshots;
            public List<(int requestId, string signature, ItemReceipt receipt)> receipts;
            public NpcInteractable held;
            public Dictionary<string, PhysicalItemState> items;
            public List<ExtraObjectState> extraObjects;
        }

        public static void Run(
            Scene scene,
            PhysicsScene physics,
            Func<string, GameObject> CreateGo,
            Action<bool, string, string> Check,
            List<string> passed,
            string worldId,
            string genId,
            string agentId,
            GameObject actorGo,
            GameObject handGo)
        {
            // 1. Setup Definitions
            var model = new ItemModel(worldId, genId);

            var defChest = new ItemDefinition
            {
                itemTypeId = "multi-type-chest",
                dimensions = new PhysicalDimensions(0.6f, 0.4f, 0.5f),
                massKg = 5.0f,
                isContainer = true,
                maxContainedSlots = 6,
                maxContainedVolumeM3 = 0.5f,
                maxContainedMassKg = 40.0f
            };
            var defBasket = new ItemDefinition
            {
                itemTypeId = "multi-type-basket",
                dimensions = new PhysicalDimensions(0.3f, 0.25f, 0.3f),
                massKg = 2.0f,
                isContainer = true,
                maxContainedSlots = 4,
                maxContainedVolumeM3 = 0.08f,
                maxContainedMassKg = 15.0f
            };
            var defPouch = new ItemDefinition
            {
                itemTypeId = "multi-type-pouch",
                dimensions = new PhysicalDimensions(0.15f, 0.15f, 0.15f),
                massKg = 0.5f,
                isContainer = true,
                maxContainedSlots = 2,
                maxContainedVolumeM3 = 0.01f,
                maxContainedMassKg = 5.0f
            };
            var defGem = new ItemDefinition
            {
                itemTypeId = "multi-type-gem",
                dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f),
                massKg = 0.1f,
                isContainer = false
            };
            var defChisel = new ItemDefinition
            {
                itemTypeId = "multi-type-chisel",
                dimensions = new PhysicalDimensions(0.1f, 0.05f, 0.2f),
                massKg = 0.4f,
                isContainer = false
            };
            var defRock = new ItemDefinition
            {
                itemTypeId = "multi-type-rock",
                dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                massKg = 1.0f,
                isContainer = false
            };

            model.RegisterDefinition(defChest);
            model.RegisterDefinition(defBasket);
            model.RegisterDefinition(defPouch);
            model.RegisterDefinition(defGem);
            model.RegisterDefinition(defChisel);
            model.RegisterDefinition(defRock);
            model.SetActorCarryLimits(agentId, new ActorCarryLimits(25.0f, 1));

            // 2. Setup GameObjects and Components
            var reg = new List<NpcInteractable>();

            PhysicalItem CreateItem(string itemId, string itemTypeId, float mass, PhysicalDimensions dims, Vector3 initPos)
            {
                var go = CreateGo("multi-" + itemId);
                go.transform.position = initPos;
                go.transform.rotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;

                var ni = go.AddComponent<NpcInteractable>();
                ni.StableId = itemId;
                ni.WorldId = worldId;
                ni.Kind = NpcObjectKind.Item;
                ni.Permission = true;

                var appGo = CreateGo("app-" + itemId);
                appGo.transform.SetParent(go.transform, false);
                appGo.transform.localPosition = Vector3.zero;
                ni.Approach = appGo.transform;

                var phys = go.AddComponent<PhysicalItem>();
                phys.itemId = itemId;
                phys.itemTypeId = itemTypeId;
                phys.massKg = mass;
                phys.dimensions = dims;
                phys.ConfigureComponents();
                phys.Bind(model, worldId, genId);

                var mr = go.AddComponent<MeshRenderer>();
                mr.enabled = true;
                phys.RecordInitialRendererStates();

                reg.Add(ni);
                return phys;
            }

            var chestPhys = CreateItem("item-chest", "multi-type-chest", 5.0f, new PhysicalDimensions(0.6f, 0.4f, 0.5f), new Vector3(2f, 0.2f, 2f));
            var basketPhys = CreateItem("item-basket", "multi-type-basket", 2.0f, new PhysicalDimensions(0.3f, 0.25f, 0.3f), new Vector3(0.3f, 1.0f, 0.4f));
            var pouchPhys = CreateItem("item-pouch", "multi-type-pouch", 0.5f, new PhysicalDimensions(0.15f, 0.15f, 0.15f), new Vector3(0.3f, 1.0f, 0.4f));
            var gemPhys = CreateItem("item-gem", "multi-type-gem", 0.1f, new PhysicalDimensions(0.08f, 0.08f, 0.08f), new Vector3(0.3f, 1.0f, 0.4f));
            var chiselPhys = CreateItem("item-chisel", "multi-type-chisel", 0.4f, new PhysicalDimensions(0.1f, 0.05f, 0.2f), new Vector3(2f, 0.2f, 2f));
            var rockPhys = CreateItem("item-rock", "multi-type-rock", 1.0f, new PhysicalDimensions(0.2f, 0.2f, 0.2f), new Vector3(3f, 0.2f, 3f));

            // Decorative disabled renderer child on basket (owned by basket, not a PhysicalItem itself)
            var basketDecoGo = CreateGo("multi-basket-deco");
            basketDecoGo.transform.SetParent(basketPhys.transform, false);
            basketDecoGo.transform.localPosition = Vector3.zero;
            var basketDecoMr = basketDecoGo.AddComponent<MeshRenderer>();
            basketDecoMr.enabled = false;
            basketPhys.RecordInitialRendererStates();

            // Alien item registered in actions to test unrelated actions.Held rejection
            var alienGo = CreateGo("multi-alien");
            alienGo.transform.position = new Vector3(10f, 0.2f, 10f);
            var alienNi = alienGo.AddComponent<NpcInteractable>();
            alienNi.StableId = "item-alien";
            alienNi.WorldId = worldId;
            alienNi.Kind = NpcObjectKind.Item;
            alienNi.Permission = true;
            reg.Add(alienNi);

            var actions = new NpcActionApi(agentId, worldId, actorGo.transform, handGo.transform, reg);
            actions.PhysicalModel = model;
            var auth = new BasicItemActionAuthority();
            actions.PhysicalAuthority = auth;

            var bindings = new List<PhysicalItemRuntimeBinding>
            {
                new PhysicalItemRuntimeBinding(chestPhys),
                new PhysicalItemRuntimeBinding(basketPhys),
                new PhysicalItemRuntimeBinding(pouchPhys),
                new PhysicalItemRuntimeBinding(gemPhys),
                new PhysicalItemRuntimeBinding(chiselPhys),
                new PhysicalItemRuntimeBinding(rockPhys)
            };

            PhysicalSavePayload CopyPayload(PhysicalSavePayload src)
            {
                string json = JsonUtility.ToJson(src);
                return JsonUtility.FromJson<PhysicalSavePayload>(json);
            }

            FullSceneState CaptureFullState(IEnumerable<PhysicalItemRuntimeBinding> extraBindings = null, IEnumerable<GameObject> extraGameObjects = null)
            {
                var state = new FullSceneState
                {
                    tick = model.Tick,
                    highestReceiptId = model.HighestReceiptRequestId,
                    itemCount = model.ItemCount,
                    itemSnapshots = model.GetAllItemSnapshots(),
                    receipts = model.GetAllReceiptRecords(),
                    held = actions.Held,
                    items = new Dictionary<string, PhysicalItemState>(StringComparer.Ordinal),
                    extraObjects = new List<ExtraObjectState>()
                };

                void RecordBinding(PhysicalItemRuntimeBinding b)
                {
                    if (b == null || b.physicalItem == null) return;
                    var p = b.physicalItem;
                    var ni = b.interactable;
                    var rends = new List<Renderer>();
                    p.GetOwnedRenderers(rends);
                    var rStates = new List<RendererState>(rends.Count);
                    for (int i = 0; i < rends.Count; i++)
                    {
                        if (rends[i] != null)
                        {
                            rStates.Add(new RendererState { renderer = rends[i], enabled = rends[i].enabled });
                        }
                    }

                    state.items[p.itemId] = new PhysicalItemState
                    {
                        pos = p.transform.position,
                        rot = p.transform.rotation,
                        scale = p.transform.lossyScale,
                        parent = p.transform.parent,
                        isCarried = p.IsCarried,
                        isStored = p.IsStored,
                        carriedHand = p.CarriedHand,
                        boundContainerId = p.BoundContainerItemId,
                        boundModel = p.BoundModel,
                        boundWorldId = p.BoundWorldId,
                        boundGenId = p.BoundGenerationId,
                        massKg = p.massKg,
                        dimensions = p.dimensions,
                        isAnchored = p.isAnchored,
                        body = p.Body,
                        bodyMass = p.Body != null ? p.Body.mass : 0f,
                        isKinematic = p.Body != null && p.Body.isKinematic,
                        useGravity = p.Body != null && p.Body.useGravity,
                        linearVelocity = p.Body != null ? p.Body.linearVelocity : Vector3.zero,
                        angularVelocity = p.Body != null ? p.Body.angularVelocity : Vector3.zero,
                        collider = p.ItemCollider,
                        colEnabled = p.ItemCollider != null && p.ItemCollider.enabled,
                        isTrigger = p.ItemCollider != null && p.ItemCollider.isTrigger,
                        renderers = rStates,
                        interactable = ni,
                        stableId = ni != null ? ni.StableId : null,
                        worldId = ni != null ? ni.WorldId : null,
                        kind = ni != null ? ni.Kind : NpcObjectKind.Item,
                        permission = ni != null && ni.Permission,
                        heldBy = ni != null ? ni.HeldBy : ""
                    };
                }

                foreach (var b in bindings)
                {
                    RecordBinding(b);
                }

                if (extraBindings != null)
                {
                    foreach (var eb in extraBindings)
                    {
                        RecordBinding(eb);
                    }
                }

                if (extraGameObjects != null)
                {
                    foreach (var ego in extraGameObjects)
                    {
                        if (ego == null) continue;
                        var rb = ego.GetComponent<Rigidbody>();
                        state.extraObjects.Add(new ExtraObjectState
                        {
                            go = ego,
                            pos = ego.transform.position,
                            rot = ego.transform.rotation,
                            scale = ego.transform.lossyScale,
                            parent = ego.transform.parent,
                            body = rb,
                            bodyMass = rb != null ? rb.mass : 0f,
                            isKinematic = rb != null && rb.isKinematic,
                            useGravity = rb != null && rb.useGravity,
                            linearVelocity = rb != null ? rb.linearVelocity : Vector3.zero,
                            angularVelocity = rb != null ? rb.angularVelocity : Vector3.zero
                        });
                    }
                }

                return state;
            }

            bool MatchesFullState(FullSceneState before, IEnumerable<PhysicalItemRuntimeBinding> extraBindings = null)
            {
                if (before == null) return false;
                if (model.Tick != before.tick) return false;
                if (model.HighestReceiptRequestId != before.highestReceiptId) return false;
                if (model.ItemCount != before.itemCount) return false;
                if (actions.Held != before.held) return false;

                // Model snapshots exact match
                var currSnaps = model.GetAllItemSnapshots();
                if (currSnaps.Count != before.itemSnapshots.Count) return false;
                for (int i = 0; i < currSnaps.Count; i++)
                {
                    var a = currSnaps[i];
                    var b = before.itemSnapshots[i];
                    if (!string.Equals(a.itemId, b.itemId, StringComparison.Ordinal)) return false;
                    if (!string.Equals(a.itemTypeId, b.itemTypeId, StringComparison.Ordinal)) return false;
                    if (a.location != b.location) return false;
                    if (!string.Equals(a.holderActorId, b.holderActorId, StringComparison.Ordinal)) return false;
                    if (!string.Equals(a.containerItemId, b.containerItemId, StringComparison.Ordinal)) return false;
                    if (!string.Equals(a.placedSupportId, b.placedSupportId, StringComparison.Ordinal)) return false;
                    if (Vector3.Distance(a.position, b.position) > 0.0001f) return false;
                    if (Quaternion.Angle(a.rotation, b.rotation) > 0.01f) return false;
                    if (a.lastUpdatedTick != b.lastUpdatedTick) return false;
                }

                // Receipts exact match: every ItemReceipt struct field, signature, and request key
                var currReceipts = model.GetAllReceiptRecords();
                if (currReceipts.Count != before.receipts.Count) return false;
                for (int i = 0; i < currReceipts.Count; i++)
                {
                    var rA = currReceipts[i];
                    var rB = before.receipts[i];
                    if (rA.requestId != rB.requestId) return false;
                    if (!string.Equals(rA.signature, rB.signature, StringComparison.Ordinal)) return false;

                    // ItemReceipt struct field comparison
                    if (rA.receipt.requestId != rB.receipt.requestId) return false;
                    if (rA.receipt.requestId != rA.requestId) return false;
                    if (!string.Equals(rA.receipt.worldId, rB.receipt.worldId, StringComparison.Ordinal)) return false;
                    if (!string.Equals(rA.receipt.generationId, rB.receipt.generationId, StringComparison.Ordinal)) return false;
                    if (!string.Equals(rA.receipt.actorId, rB.receipt.actorId, StringComparison.Ordinal)) return false;
                    if (rA.receipt.action != rB.receipt.action) return false;
                    if (!string.Equals(rA.receipt.itemId, rB.receipt.itemId, StringComparison.Ordinal)) return false;
                    if (!string.Equals(rA.receipt.targetId, rB.receipt.targetId, StringComparison.Ordinal)) return false;
                    if (rA.receipt.success != rB.receipt.success) return false;
                    if (rA.receipt.duplicate != rB.receipt.duplicate) return false;
                    if (!string.Equals(rA.receipt.code, rB.receipt.code, StringComparison.Ordinal)) return false;
                    if (Mathf.Abs(rA.receipt.totalCarriedMassKg - rB.receipt.totalCarriedMassKg) > 0.0001f) return false;
                }

                bool CheckBindingMatch(PhysicalItemRuntimeBinding b)
                {
                    var p = b.physicalItem;
                    var ni = b.interactable;
                    if (!before.items.TryGetValue(p.itemId, out var s)) return false;

                    if (Vector3.Distance(p.transform.position, s.pos) > 0.0001f) return false;
                    if (Quaternion.Angle(p.transform.rotation, s.rot) > 0.01f) return false;
                    if (!ItemDefinition.Finite(p.transform.lossyScale.x) ||
                        !ItemDefinition.Finite(p.transform.lossyScale.y) ||
                        !ItemDefinition.Finite(p.transform.lossyScale.z)) return false;
                    if (Vector3.Distance(p.transform.lossyScale, s.scale) > 0.0001f) return false;
                    if (p.transform.parent != s.parent) return false;
                    if (p.IsCarried != s.isCarried) return false;
                    if (p.IsStored != s.isStored) return false;
                    if (p.CarriedHand != s.carriedHand) return false;
                    if (!string.Equals(p.BoundContainerItemId, s.boundContainerId, StringComparison.Ordinal)) return false;
                    if (p.BoundModel != s.boundModel) return false;
                    if (!string.Equals(p.BoundWorldId, s.boundWorldId, StringComparison.Ordinal)) return false;
                    if (!string.Equals(p.BoundGenerationId, s.boundGenId, StringComparison.Ordinal)) return false;
                    if (Mathf.Abs(p.massKg - s.massKg) > 0.0001f) return false;
                    if (p.isAnchored != s.isAnchored) return false;

                    if (p.Body != s.body) return false;
                    if (p.Body != null)
                    {
                        if (Mathf.Abs(p.Body.mass - s.bodyMass) > 0.0001f) return false;
                        if (p.Body.isKinematic != s.isKinematic) return false;
                        if (p.Body.useGravity != s.useGravity) return false;
                        if (Vector3.Distance(p.Body.linearVelocity, s.linearVelocity) > 0.0001f) return false;
                        if (Vector3.Distance(p.Body.angularVelocity, s.angularVelocity) > 0.0001f) return false;
                    }

                    if (p.ItemCollider != s.collider) return false;
                    if (p.ItemCollider != null)
                    {
                        if (p.ItemCollider.enabled != s.colEnabled) return false;
                        if (p.ItemCollider.isTrigger != s.isTrigger) return false;
                    }

                    var currRends = new List<Renderer>();
                    p.GetOwnedRenderers(currRends);
                    int validRendCount = 0;
                    for (int i = 0; i < currRends.Count; i++) if (currRends[i] != null) validRendCount++;
                    if (validRendCount != s.renderers.Count) return false;
                    for (int i = 0; i < s.renderers.Count; i++)
                    {
                        var expRend = s.renderers[i];
                        if (expRend.renderer == null || expRend.renderer.enabled != expRend.enabled) return false;
                    }

                    if (ni != s.interactable) return false;
                    if (ni != null)
                    {
                        if (!string.Equals(ni.StableId, s.stableId, StringComparison.Ordinal)) return false;
                        if (!string.Equals(ni.WorldId, s.worldId, StringComparison.Ordinal)) return false;
                        if (ni.Kind != s.kind) return false;
                        if (ni.Permission != s.permission) return false;
                        if (!string.Equals(ni.HeldBy, s.heldBy, StringComparison.Ordinal)) return false;
                    }
                    return true;
                }

                // Scene and physics match
                foreach (var b in bindings)
                {
                    if (!CheckBindingMatch(b)) return false;
                }

                if (extraBindings != null)
                {
                    foreach (var eb in extraBindings)
                    {
                        if (!CheckBindingMatch(eb)) return false;
                    }
                }

                if (before.extraObjects != null)
                {
                    foreach (var exo in before.extraObjects)
                    {
                        if (exo.go == null) return false;
                        if (Vector3.Distance(exo.go.transform.position, exo.pos) > 0.0001f) return false;
                        if (Quaternion.Angle(exo.go.transform.rotation, exo.rot) > 0.01f) return false;
                        if (Vector3.Distance(exo.go.transform.lossyScale, exo.scale) > 0.0001f) return false;
                        if (exo.go.transform.parent != exo.parent) return false;
                        var currRb = exo.go.GetComponent<Rigidbody>();
                        if (currRb != exo.body) return false;
                        if (currRb != null)
                        {
                            if (Mathf.Abs(currRb.mass - exo.bodyMass) > 0.0001f) return false;
                            if (currRb.isKinematic != exo.isKinematic) return false;
                            if (currRb.useGravity != exo.useGravity) return false;
                            if (Vector3.Distance(currRb.linearVelocity, exo.linearVelocity) > 0.0001f) return false;
                            if (Vector3.Distance(currRb.angularVelocity, exo.angularVelocity) > 0.0001f) return false;
                        }
                    }
                }

                return true;
            }

            // Construct canonical request and signature for replay fixture
            var canonicalPickupReq = new ItemActionRequest
            {
                requestId = 10,
                action = ItemActionKind.Pickup,
                actorId = agentId,
                itemId = "item-basket"
            };
            string canonicalPickupSig = ItemModel.BuildRequestSignature(canonicalPickupReq, Quaternion.identity);

            // Construct valid mixed containment payload:
            // Carried: basket (holds pouch, which holds gem)
            // Free: chest (holds chisel)
            // Free: rock
            var payload = new PhysicalSavePayload
            {
                worldId = worldId,
                generationId = genId,
                actorId = agentId,
                tick = 100,
                items = new List<SavedItemRecord>
                {
                    new SavedItemRecord
                    {
                        itemId = "item-chest",
                        itemTypeId = "multi-type-chest",
                        location = ItemLocationKind.Free,
                        holderActorId = null,
                        containerItemId = null,
                        massKg = 5.0f,
                        dimensions = new PhysicalDimensions(0.6f, 0.4f, 0.5f),
                        position = new Vector3(2f, 0.2f, 2f),
                        rotation = Quaternion.identity,
                        lastUpdatedTick = 100
                    },
                    new SavedItemRecord
                    {
                        itemId = "item-basket",
                        itemTypeId = "multi-type-basket",
                        location = ItemLocationKind.Carried,
                        holderActorId = agentId,
                        containerItemId = null,
                        massKg = 2.0f,
                        dimensions = new PhysicalDimensions(0.3f, 0.25f, 0.3f),
                        position = handGo.transform.position,
                        rotation = handGo.transform.rotation,
                        lastUpdatedTick = 100
                    },
                    new SavedItemRecord
                    {
                        itemId = "item-pouch",
                        itemTypeId = "multi-type-pouch",
                        location = ItemLocationKind.Stored,
                        holderActorId = null,
                        containerItemId = "item-basket",
                        massKg = 0.5f,
                        dimensions = new PhysicalDimensions(0.15f, 0.15f, 0.15f),
                        position = Vector3.zero,
                        rotation = Quaternion.identity,
                        lastUpdatedTick = 100
                    },
                    new SavedItemRecord
                    {
                        itemId = "item-gem",
                        itemTypeId = "multi-type-gem",
                        location = ItemLocationKind.Stored,
                        holderActorId = null,
                        containerItemId = "item-pouch",
                        massKg = 0.1f,
                        dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f),
                        position = Vector3.zero,
                        rotation = Quaternion.identity,
                        lastUpdatedTick = 100
                    },
                    new SavedItemRecord
                    {
                        itemId = "item-chisel",
                        itemTypeId = "multi-type-chisel",
                        location = ItemLocationKind.Stored,
                        holderActorId = null,
                        containerItemId = "item-chest",
                        massKg = 0.4f,
                        dimensions = new PhysicalDimensions(0.1f, 0.05f, 0.2f),
                        position = Vector3.zero,
                        rotation = Quaternion.identity,
                        lastUpdatedTick = 100
                    },
                    new SavedItemRecord
                    {
                        itemId = "item-rock",
                        itemTypeId = "multi-type-rock",
                        location = ItemLocationKind.Free,
                        holderActorId = null,
                        containerItemId = null,
                        massKg = 1.0f,
                        dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                        position = new Vector3(3f, 0.2f, 3f),
                        rotation = Quaternion.identity,
                        lastUpdatedTick = 100
                    }
                },
                receipts = new List<SavedReceiptRecord>
                {
                    new SavedReceiptRecord
                    {
                        requestId = 10,
                        signature = canonicalPickupSig,
                        receipt = new ItemReceipt
                        {
                            requestId = 10,
                            worldId = worldId,
                            generationId = genId,
                            actorId = agentId,
                            action = ItemActionKind.Pickup,
                            itemId = "item-basket",
                            targetId = null,
                            success = true,
                            duplicate = false,
                            code = "picked-up",
                            totalCarriedMassKg = 2.6f
                        }
                    }
                }
            };

            // -------------------------------------------------------------
            // A. Initial Mixed Graph Restoration
            // -------------------------------------------------------------
            bool restoreOk = ItemPersistence.RestoreRuntime(payload, model, actions, bindings);
            Check(restoreOk, "multi-item-restore-mixed-graph-success", null);

            var basketNi = reg.Find(x => x.StableId == "item-basket");
            var pouchNi = reg.Find(x => x.StableId == "item-pouch");
            var gemNi = reg.Find(x => x.StableId == "item-gem");
            var chestNi = reg.Find(x => x.StableId == "item-chest");
            var chiselNi = reg.Find(x => x.StableId == "item-chisel");
            var rockNi = reg.Find(x => x.StableId == "item-rock");

            // Carried root verification
            Check(basketPhys.IsCarried && !basketPhys.IsStored && basketPhys.CarriedHand == handGo.transform &&
                  actions.Held == basketNi && basketNi.HeldBy == agentId &&
                  basketPhys.ItemCollider.enabled && basketPhys.ItemCollider.isTrigger &&
                  basketPhys.GetComponent<MeshRenderer>().enabled && !basketDecoMr.enabled &&
                  Mathf.Abs(basketPhys.transform.lossyScale.x - 1f) <= 0.001f,
                  "multi-item-restore-carried-root-state", null);

            // Explicit verification of disabled decorative renderer state preservation
            Check(!basketDecoMr.enabled && basketPhys.GetComponent<MeshRenderer>().enabled,
                  "multi-item-restore-disabled-decorative-renderer-initial-state", null);

            // Nested stored verification (pouch inside basket, gem inside pouch)
            Check(pouchPhys.IsStored && !pouchPhys.IsCarried && string.Equals(pouchPhys.BoundContainerItemId, "item-basket", StringComparison.Ordinal) &&
                  pouchPhys.transform.parent == basketPhys.transform &&
                  !pouchPhys.ItemCollider.enabled && pouchPhys.Body.isKinematic && !pouchPhys.Body.useGravity &&
                  !pouchPhys.GetComponent<MeshRenderer>().enabled &&
                  gemPhys.IsStored && !gemPhys.IsCarried && string.Equals(gemPhys.BoundContainerItemId, "item-pouch", StringComparison.Ordinal) &&
                  gemPhys.transform.parent == pouchPhys.transform &&
                  !gemPhys.ItemCollider.enabled && gemPhys.Body.isKinematic && !gemPhys.Body.useGravity &&
                  !gemPhys.GetComponent<MeshRenderer>().enabled,
                  "multi-item-restore-nested-stored-state", null);

            // Free root and stored-in-free verification (chest on floor with chisel stored, rock on floor)
            Check(!chestPhys.IsCarried && !chestPhys.IsStored && chestNi.HeldBy == "" &&
                  chestPhys.ItemCollider.enabled && !chestPhys.ItemCollider.isTrigger && !chestPhys.Body.isKinematic &&
                  chestPhys.GetComponent<MeshRenderer>().enabled &&
                  chiselPhys.IsStored && !chiselPhys.IsCarried && string.Equals(chiselPhys.BoundContainerItemId, "item-chest", StringComparison.Ordinal) &&
                  chiselPhys.transform.parent == chestPhys.transform &&
                  !chiselPhys.ItemCollider.enabled && chiselPhys.Body.isKinematic &&
                  !chiselPhys.GetComponent<MeshRenderer>().enabled &&
                  !rockPhys.IsCarried && !rockPhys.IsStored && rockNi.HeldBy == "" &&
                  rockPhys.ItemCollider.enabled && !rockPhys.ItemCollider.isTrigger && !rockPhys.Body.isKinematic &&
                  rockPhys.GetComponent<MeshRenderer>().enabled,
                  "multi-item-restore-free-root-state", null);

            // Exact mass verification: carried root basket (2.0) + pouch (0.5) + gem (0.1) = 2.6kg
            float carriedMass = model.GetActorCarriedMassKg(agentId);
            float basketTreeMass = model.GetItemTotalMassKg("item-basket");
            float chestTreeMass = model.GetItemTotalMassKg("item-chest");
            Check(Mathf.Abs(carriedMass - 2.6f) <= 0.0001f &&
                  Mathf.Abs(basketTreeMass - 2.6f) <= 0.0001f &&
                  Mathf.Abs(chestTreeMass - 5.4f) <= 0.0001f &&
                  Mathf.Abs(basketPhys.Body.mass - 2.0f) <= 0.0001f &&
                  Mathf.Abs(pouchPhys.Body.mass - 0.5f) <= 0.0001f &&
                  Mathf.Abs(gemPhys.Body.mass - 0.1f) <= 0.0001f,
                  "multi-item-restore-descendant-mass-exact", null);

            // -------------------------------------------------------------
            // B. Repeated Five Restores (Idempotency and Stability)
            // -------------------------------------------------------------
            bool repeatedOk = true;
            for (int cycle = 1; cycle <= 5; cycle++)
            {
                bool rep = ItemPersistence.RestoreRuntime(payload, model, actions, bindings);
                if (!rep) { repeatedOk = false; break; }
                if (!basketPhys.IsCarried || !pouchPhys.IsStored || !gemPhys.IsStored || !chiselPhys.IsStored)
                {
                    repeatedOk = false; break;
                }
                if (Mathf.Abs(model.GetActorCarriedMassKg(agentId) - 2.6f) > 0.0001f)
                {
                    repeatedOk = false; break;
                }
                if (Mathf.Abs(basketPhys.transform.lossyScale.x - 1f) > 0.001f ||
                    Mathf.Abs(pouchPhys.transform.lossyScale.x - 1f) > 0.001f ||
                    Mathf.Abs(gemPhys.transform.lossyScale.x - 1f) > 0.001f)
                {
                    repeatedOk = false; break;
                }
                if (basketDecoMr.enabled || !basketPhys.GetComponent<MeshRenderer>().enabled)
                {
                    repeatedOk = false; break;
                }
            }
            Check(repeatedOk, "multi-item-restore-repeated-five-cycles-stable", null);

            // -------------------------------------------------------------
            // C. Transitions: Stored -> Free -> Carried -> Stored
            // -------------------------------------------------------------
            // C1. Stored to Free: gem removed from pouch and dropped on floor at (4, 0.2, 4)
            var transPayload1 = CopyPayload(payload);
            transPayload1.tick = 101;
            var gemRec1 = transPayload1.items.Find(x => x.itemId == "item-gem");
            gemRec1.location = ItemLocationKind.Free;
            gemRec1.holderActorId = null;
            gemRec1.containerItemId = null;
            gemRec1.position = new Vector3(4f, 0.2f, 4f);
            gemRec1.rotation = Quaternion.identity;
            gemRec1.lastUpdatedTick = 101;

            bool trans1Ok = ItemPersistence.RestoreRuntime(transPayload1, model, actions, bindings);
            Check(trans1Ok &&
                  !gemPhys.IsStored && !gemPhys.IsCarried && gemPhys.transform.parent == null &&
                  Vector3.Distance(gemPhys.transform.position, new Vector3(4f, 0.2f, 4f)) <= 0.001f &&
                  gemPhys.ItemCollider.enabled && !gemPhys.ItemCollider.isTrigger &&
                  !gemPhys.Body.isKinematic && gemPhys.Body.useGravity &&
                  gemPhys.GetComponent<MeshRenderer>().enabled &&
                  Mathf.Abs(model.GetItemTotalMassKg("item-basket") - 2.5f) <= 0.0001f,
                  "multi-item-restore-transition-stored-to-free", null);

            // C2. Free to Carried: gem picked up into hand; basket placed Free on floor at (1, 0.2, 1)
            var transPayload2 = CopyPayload(transPayload1);
            transPayload2.tick = 102;
            var gemRec2 = transPayload2.items.Find(x => x.itemId == "item-gem");
            gemRec2.location = ItemLocationKind.Carried;
            gemRec2.holderActorId = agentId;
            gemRec2.lastUpdatedTick = 102;

            var basketRec2 = transPayload2.items.Find(x => x.itemId == "item-basket");
            basketRec2.location = ItemLocationKind.Free;
            basketRec2.holderActorId = null;
            basketRec2.containerItemId = null;
            basketRec2.position = new Vector3(1f, 0.2f, 1f);
            basketRec2.rotation = Quaternion.identity;
            basketRec2.lastUpdatedTick = 102;

            bool trans2Ok = ItemPersistence.RestoreRuntime(transPayload2, model, actions, bindings);
            Check(trans2Ok &&
                  gemPhys.IsCarried && !gemPhys.IsStored && gemPhys.CarriedHand == handGo.transform &&
                  actions.Held == gemNi && gemNi.HeldBy == agentId &&
                  gemPhys.ItemCollider.enabled && gemPhys.ItemCollider.isTrigger &&
                  gemPhys.GetComponent<MeshRenderer>().enabled &&
                  !basketPhys.IsCarried && !basketPhys.IsStored && basketNi.HeldBy == "" &&
                  basketPhys.ItemCollider.enabled && !basketPhys.ItemCollider.isTrigger &&
                  !basketPhys.Body.isKinematic && basketPhys.Body.useGravity &&
                  basketPhys.GetComponent<MeshRenderer>().enabled &&
                  Mathf.Abs(model.GetActorCarriedMassKg(agentId) - 0.1f) <= 0.0001f,
                  "multi-item-restore-transition-free-to-carried", null);

            // Parent drop renderer preservation: basket dropped to floor preserves disabled decorative renderer and nested pouch renderer
            Check(!basketDecoMr.enabled && basketPhys.GetComponent<MeshRenderer>().enabled && !pouchPhys.GetComponent<MeshRenderer>().enabled,
                  "multi-item-restore-parent-drop-preserves-child-and-decorative-renderers", null);

            // C3. Carried to Stored: gem stored back into pouch; basket picked back up into hand
            bool trans3Ok = ItemPersistence.RestoreRuntime(payload, model, actions, bindings);
            Check(trans3Ok &&
                  gemPhys.IsStored && !gemPhys.IsCarried && string.Equals(gemPhys.BoundContainerItemId, "item-pouch", StringComparison.Ordinal) &&
                  gemPhys.transform.parent == pouchPhys.transform &&
                  !gemPhys.ItemCollider.enabled && gemPhys.Body.isKinematic &&
                  !gemPhys.GetComponent<MeshRenderer>().enabled &&
                  basketPhys.IsCarried && actions.Held == basketNi &&
                  Mathf.Abs(model.GetActorCarriedMassKg(agentId) - 2.6f) <= 0.0001f,
                  "multi-item-restore-transition-carried-to-stored", null);

            // Repeated transitions preserve decorative renderer state on basket
            Check(!basketDecoMr.enabled && basketPhys.GetComponent<MeshRenderer>().enabled,
                  "multi-item-restore-repeated-transitions-preserve-decorative-renderer", null);

            // C4. Stored-exit renderer test: basket with intentionally disabled owned decorative renderer placed into Stored
            var transPayload3 = CopyPayload(payload);
            transPayload3.tick = 103;
            var basketRec3 = transPayload3.items.Find(x => x.itemId == "item-basket");
            basketRec3.location = ItemLocationKind.Stored;
            basketRec3.holderActorId = null;
            basketRec3.containerItemId = "item-chest";
            basketRec3.position = Vector3.zero;
            basketRec3.rotation = Quaternion.identity;
            basketRec3.lastUpdatedTick = 103;

            bool transStoreBasketOk = ItemPersistence.RestoreRuntime(transPayload3, model, actions, bindings);
            Check(transStoreBasketOk &&
                  basketPhys.IsStored && !basketPhys.IsCarried && string.Equals(basketPhys.BoundContainerItemId, "item-chest", StringComparison.Ordinal) &&
                  !basketPhys.GetComponent<MeshRenderer>().enabled && !basketDecoMr.enabled &&
                  !basketPhys.ItemCollider.enabled && basketPhys.Body.isKinematic &&
                  actions.Held == null,
                  "multi-item-restore-transition-basket-with-deco-to-stored", null);

            // Repeated restores while Stored keep both renderers disabled and preserve mass
            bool repStoredOk = true;
            for (int r = 1; r <= 3; r++)
            {
                if (!ItemPersistence.RestoreRuntime(transPayload3, model, actions, bindings)) { repStoredOk = false; break; }
                if (!basketPhys.IsStored || basketPhys.GetComponent<MeshRenderer>().enabled || basketDecoMr.enabled) { repStoredOk = false; break; }
                if (Mathf.Abs(model.GetItemTotalMassKg("item-chest") - 8.0f) > 0.0001f) { repStoredOk = false; break; }
            }
            Check(repStoredOk, "multi-item-restore-stored-with-deco-repeated-restores-stable", null);

            // C5. Transition Stored item with decorative renderer to Free: original enabled AND disabled states survive
            var transPayload4 = CopyPayload(transPayload3);
            transPayload4.tick = 104;
            var basketRec4 = transPayload4.items.Find(x => x.itemId == "item-basket");
            basketRec4.location = ItemLocationKind.Free;
            basketRec4.holderActorId = null;
            basketRec4.containerItemId = null;
            basketRec4.position = new Vector3(1f, 0.2f, 1f);
            basketRec4.rotation = Quaternion.identity;
            basketRec4.lastUpdatedTick = 104;

            bool transFreeBasketOk = ItemPersistence.RestoreRuntime(transPayload4, model, actions, bindings);
            Check(transFreeBasketOk &&
                  !basketPhys.IsStored && !basketPhys.IsCarried && basketPhys.transform.parent == null &&
                  basketPhys.GetComponent<MeshRenderer>().enabled && !basketDecoMr.enabled &&
                  basketPhys.ItemCollider.enabled && !basketPhys.ItemCollider.isTrigger && !basketPhys.Body.isKinematic &&
                  Mathf.Abs(model.GetItemTotalMassKg("item-basket") - 2.6f) <= 0.0001f &&
                  Mathf.Abs(model.GetItemTotalMassKg("item-chest") - 5.4f) <= 0.0001f,
                  "multi-item-restore-stored-exit-to-free-preserves-decorative-and-main-renderers", null);

            // C6. Transition that same item from Free to Carried: original enabled AND disabled states survive
            var transPayload5 = CopyPayload(transPayload4);
            transPayload5.tick = 105;
            var basketRec5 = transPayload5.items.Find(x => x.itemId == "item-basket");
            basketRec5.location = ItemLocationKind.Carried;
            basketRec5.holderActorId = agentId;
            basketRec5.lastUpdatedTick = 105;

            bool transCarriedBasketOk = ItemPersistence.RestoreRuntime(transPayload5, model, actions, bindings);
            Check(transCarriedBasketOk &&
                  basketPhys.IsCarried && !basketPhys.IsStored && basketPhys.CarriedHand == handGo.transform &&
                  actions.Held == basketNi &&
                  basketPhys.GetComponent<MeshRenderer>().enabled && !basketDecoMr.enabled &&
                  Mathf.Abs(model.GetActorCarriedMassKg(agentId) - 2.6f) <= 0.0001f,
                  "multi-item-restore-stored-exit-to-carried-preserves-decorative-and-main-renderers", null);

            // C7. Transition back to Stored: both renderers disabled
            bool transBackStoredOk = ItemPersistence.RestoreRuntime(transPayload3, model, actions, bindings);
            Check(transBackStoredOk &&
                  basketPhys.IsStored &&
                  !basketPhys.GetComponent<MeshRenderer>().enabled && !basketDecoMr.enabled,
                  "multi-item-restore-transition-back-to-stored-hides-all-renderers", null);

            // Reset back to initial baseline payload for subsequent invariant suites
            bool restoreOrigPayload = ItemPersistence.RestoreRuntime(payload, model, actions, bindings);
            Check(restoreOrigPayload && basketPhys.IsCarried && basketPhys.GetComponent<MeshRenderer>().enabled && !basketDecoMr.enabled &&
                  pouchPhys.IsStored && !pouchPhys.GetComponent<MeshRenderer>().enabled &&
                  gemPhys.IsStored && !gemPhys.GetComponent<MeshRenderer>().enabled,
                  "multi-item-restore-reset-to-carried-root", null);

            // Ordinary parent ReleaseToPhysics outside restore binder:
            // Assert stored nested PhysicalItem renderers (pouch and gem) remain hidden (enabled == false)
            basketPhys.ReleaseToPhysics(new Vector3(1.2f, 0.2f, 1.2f), Quaternion.identity);
            Check(!basketPhys.IsCarried && !basketPhys.IsStored &&
                  basketPhys.GetComponent<MeshRenderer>().enabled && !basketDecoMr.enabled &&
                  !pouchPhys.GetComponent<MeshRenderer>().enabled && !gemPhys.GetComponent<MeshRenderer>().enabled,
                  "multi-item-parent-release-to-physics-outside-binder-preserves-hidden-nested-renderers", null);

            // Ordinary parent AttachToHand outside restore binder:
            // Assert stored nested PhysicalItem renderers (pouch and gem) remain hidden (enabled == false)
            basketPhys.AttachToHand(handGo.transform);
            Check(basketPhys.IsCarried && !basketPhys.IsStored &&
                  basketPhys.GetComponent<MeshRenderer>().enabled && !basketDecoMr.enabled &&
                  !pouchPhys.GetComponent<MeshRenderer>().enabled && !gemPhys.GetComponent<MeshRenderer>().enabled,
                  "multi-item-parent-attach-to-hand-outside-binder-preserves-hidden-nested-renderers", null);

            actions.RestoreHeld(basketNi);

            // -------------------------------------------------------------
            // D. Uniqueness and Ownership Invariants
            // -------------------------------------------------------------
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var seenGameObjects = new HashSet<GameObject>();
            var seenBodies = new HashSet<Rigidbody>();
            var seenColliders = new HashSet<Collider>();
            bool uniqueOk = true;

            foreach (var b in bindings)
            {
                var p = b.physicalItem;
                if (!seenIds.Add(p.itemId)) uniqueOk = false;
                if (!seenGameObjects.Add(p.gameObject)) uniqueOk = false;
                if (!seenBodies.Add(p.Body)) uniqueOk = false;
                if (!seenColliders.Add(p.ItemCollider)) uniqueOk = false;
                if (p.Body == null || p.Body.gameObject != p.gameObject) uniqueOk = false;
                if (p.ItemCollider == null || p.ItemCollider.gameObject != p.gameObject) uniqueOk = false;
                if (p.GetComponent<Rigidbody>() != p.Body) uniqueOk = false;
                var cols = p.GetComponents<Collider>();
                if (cols.Length != 1 || cols[0] != p.ItemCollider) uniqueOk = false;
                if (!ItemDefinition.Finite(p.transform.lossyScale.x) ||
                    !ItemDefinition.Finite(p.transform.lossyScale.y) ||
                    !ItemDefinition.Finite(p.transform.lossyScale.z) ||
                    Mathf.Abs(p.transform.lossyScale.x - 1f) > 0.001f ||
                    Mathf.Abs(p.transform.lossyScale.y - 1f) > 0.001f ||
                    Mathf.Abs(p.transform.lossyScale.z - 1f) > 0.001f)
                {
                    uniqueOk = false;
                }
                if (!model.TryGetDefinition(p.itemTypeId, out var d) || Mathf.Abs(p.Body.mass - d.massKg) > 0.0001f)
                {
                    uniqueOk = false;
                }
            }
            Check(uniqueOk, "multi-item-restore-uniqueness-invariants", null);

            // -------------------------------------------------------------
            // E. Refusal Invariants with Complete Pre-Refusal State Equality
            // -------------------------------------------------------------
            // E1. Missing binding: 5 bindings provided for 6 items in payload
            var bMissing = new List<PhysicalItemRuntimeBinding>(bindings);
            bMissing.RemoveAt(bMissing.Count - 1);
            var stateBeforeE1 = CaptureFullState();
            bool refMissing = ItemPersistence.RestoreRuntime(payload, model, actions, bMissing);
            Check(!refMissing && MatchesFullState(stateBeforeE1), "multi-item-restore-refusal-missing-binding-preserves-state", null);

            // E2. Extra binding: 7 bindings provided for 6 items in payload
            var extraPhys = CreateItem("extra-item", "multi-type-rock", 1.0f, new PhysicalDimensions(0.2f, 0.2f, 0.2f), new Vector3(9f, 0.2f, 9f));
            var bExtra = new List<PhysicalItemRuntimeBinding>(bindings);
            bExtra.Add(new PhysicalItemRuntimeBinding(extraPhys));
            var stateBeforeE2 = CaptureFullState(bExtra);
            bool refExtra = ItemPersistence.RestoreRuntime(payload, model, actions, bExtra);
            Check(!refExtra && MatchesFullState(stateBeforeE2, bExtra), "multi-item-restore-refusal-extra-binding-preserves-state", null);
            UnityEngine.Object.DestroyImmediate(extraPhys.gameObject);

            // E3. Duplicate binding ID
            var bDup = new List<PhysicalItemRuntimeBinding>(bindings);
            bDup[1] = bDup[0];
            var stateBeforeE3 = CaptureFullState();
            bool refDup = ItemPersistence.RestoreRuntime(payload, model, actions, bDup);
            Check(!refDup && MatchesFullState(stateBeforeE3), "multi-item-restore-refusal-duplicate-binding-preserves-state", null);

            // E4. Cross-world binding
            gemNi.WorldId = "foreign-world-01";
            var stateBeforeE4 = CaptureFullState();
            bool refCrossWorld = ItemPersistence.RestoreRuntime(payload, model, actions, bindings);
            Check(!refCrossWorld && MatchesFullState(stateBeforeE4), "multi-item-restore-refusal-cross-world-preserves-state", null);
            gemNi.WorldId = worldId;

            // E5. Generation mismatch
            gemPhys.Bind(model, worldId, "gen-foreign");
            var stateBeforeE5 = CaptureFullState();
            bool refGen = ItemPersistence.RestoreRuntime(payload, model, actions, bindings);
            Check(!refGen && MatchesFullState(stateBeforeE5), "multi-item-restore-refusal-generation-mismatch-preserves-state", null);
            gemPhys.Bind(model, worldId, genId);

            // E6. Actor mismatch
            var badActorPayload = CopyPayload(payload);
            badActorPayload.actorId = "alien-actor-01";
            var stateBeforeE6 = CaptureFullState();
            bool refActor = ItemPersistence.RestoreRuntime(badActorPayload, model, actions, bindings);
            Check(!refActor && MatchesFullState(stateBeforeE6), "multi-item-restore-refusal-actor-mismatch-preserves-state", null);

            // E7. Incompatible mass
            gemPhys.Body.mass = 99.0f;
            var stateBeforeE7 = CaptureFullState();
            bool refMass = ItemPersistence.RestoreRuntime(payload, model, actions, bindings);
            Check(!refMass && MatchesFullState(stateBeforeE7), "multi-item-restore-refusal-incompatible-mass-preserves-state", null);
            gemPhys.Body.mass = 0.1f;

            // E8. Incompatible dimensions
            gemPhys.dimensions = new PhysicalDimensions(0.9f, 0.9f, 0.9f);
            var stateBeforeE8 = CaptureFullState();
            bool refDims = ItemPersistence.RestoreRuntime(payload, model, actions, bindings);
            Check(!refDims && MatchesFullState(stateBeforeE8), "multi-item-restore-refusal-incompatible-dimensions-preserves-state", null);
            gemPhys.dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f);

            // E9. Multiple carried roots (basket and rock both carried)
            var multiCarriedPayload = CopyPayload(payload);
            var rockRec = multiCarriedPayload.items.Find(x => x.itemId == "item-rock");
            rockRec.location = ItemLocationKind.Carried;
            rockRec.holderActorId = agentId;
            var stateBeforeE9 = CaptureFullState();
            bool refMultiCarried = ItemPersistence.RestoreRuntime(multiCarriedPayload, model, actions, bindings);
            Check(!refMultiCarried && MatchesFullState(stateBeforeE9), "multi-item-restore-refusal-multiple-carried-roots-preserves-state", null);

            // E10. Unrelated actions.Held fixture
            // Establish valid held alien using NpcActionApi contract: registered non-physical Item parented to hand with HeldBy == agentId
            alienGo.transform.SetParent(handGo.transform, false);
            alienNi.HeldBy = agentId;
            bool setupAlienHeld = actions.RestoreHeld(alienNi);
            Check(setupAlienHeld && actions.Held == alienNi, "multi-item-restore-unrelated-held-setup-success", null);

            var stateBeforeE10 = CaptureFullState(extraGameObjects: new[] { alienGo });
            bool refUnrelatedHeld = ItemPersistence.RestoreRuntime(payload, model, actions, bindings);
            Check(!refUnrelatedHeld && MatchesFullState(stateBeforeE10), "multi-item-restore-refusal-unrelated-held-preserves-state", null);

            // Clean fixture correctly and restore previous held root
            alienGo.transform.SetParent(null, true);
            alienNi.HeldBy = "";
            bool restoreBasketHeld = actions.RestoreHeld(basketNi);
            Check(restoreBasketHeld && actions.Held == basketNi, "multi-item-restore-unrelated-held-cleanup-success", null);

            // E11. Omitted live item: payload and bindings match each other (5 items) but omit live rock from nonempty model (Finding 1)
            var omittedPayload = CopyPayload(payload);
            omittedPayload.items.RemoveAll(x => x.itemId == "item-rock");
            var omittedBindings = bindings.FindAll(x => !string.Equals(x.itemId, "item-rock", StringComparison.Ordinal));
            var stateBeforeE11 = CaptureFullState();
            bool refOmittedLive = ItemPersistence.RestoreRuntime(omittedPayload, model, actions, omittedBindings);
            Check(!refOmittedLive && MatchesFullState(stateBeforeE11), "multi-item-restore-refusal-omitted-live-item-preserves-state", null);

            // E12. Empty replacement of non-empty model (Finding 1)
            var emptyPayload = new PhysicalSavePayload
            {
                worldId = worldId,
                generationId = genId,
                actorId = agentId,
                tick = 100,
                items = new List<SavedItemRecord>()
            };
            var emptyBindings = new List<PhysicalItemRuntimeBinding>();
            var stateBeforeE12 = CaptureFullState();
            bool refEmptyReplacement = ItemPersistence.RestoreRuntime(emptyPayload, model, actions, emptyBindings);
            Check(!refEmptyReplacement && MatchesFullState(stateBeforeE12), "multi-item-restore-refusal-empty-replacement-preserves-state", null);

            // Empty-empty positive: dedicated empty model with valid world/generation/actor limits and empty payload/bindings, no unrelated Held
            var emptyModel = new ItemModel(worldId, genId);
            emptyModel.SetActorCarryLimits(agentId, new ActorCarryLimits(25.0f, 1));
            actions.PhysicalModel = emptyModel;
            actions.RestoreHeld(null);

            var dedEmptyPayload = new PhysicalSavePayload
            {
                worldId = worldId,
                generationId = genId,
                actorId = agentId,
                tick = 200,
                items = new List<SavedItemRecord>(),
                receipts = new List<SavedReceiptRecord>()
            };
            var dedEmptyBindings = new List<PhysicalItemRuntimeBinding>();
            var sceneBeforeEmpty = CaptureFullState();

            bool emptyOk = ItemPersistence.RestoreRuntime(dedEmptyPayload, emptyModel, actions, dedEmptyBindings);
            Check(emptyOk &&
                  emptyModel.ItemCount == 0 &&
                  emptyModel.Tick == 200 &&
                  emptyModel.GetAllReceiptRecords().Count == 0 &&
                  actions.Held == null &&
                  MatchesFullState(sceneBeforeEmpty),
                  "multi-item-restore-empty-model-empty-payload-success", null);

            // Restore main actions context
            actions.PhysicalModel = model;
            actions.RestoreHeld(basketNi);

            // E13. Genuinely equal-mass cross-wired Body alias across bindings
            // Dedicated fixture with two compatible equal-mass definitions (1.0kg each, identical dimensions)
            var eqModel = new ItemModel(worldId, genId);
            var defEqA = new ItemDefinition
            {
                itemTypeId = "type-eq-a",
                dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                massKg = 1.0f,
                isContainer = false
            };
            var defEqB = new ItemDefinition
            {
                itemTypeId = "type-eq-b",
                dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                massKg = 1.0f,
                isContainer = false
            };
            eqModel.RegisterDefinition(defEqA);
            eqModel.RegisterDefinition(defEqB);
            eqModel.SetActorCarryLimits(agentId, new ActorCarryLimits(25.0f, 1));
            eqModel.RegisterItem("item-eq-a", "type-eq-a", ItemLocationKind.Free, new Vector3(6f, 0.2f, 6f), Quaternion.identity);
            eqModel.RegisterItem("item-eq-b", "type-eq-b", ItemLocationKind.Free, new Vector3(7f, 0.2f, 7f), Quaternion.identity);

            var eqPhysA = CreateItem("item-eq-a", "type-eq-a", 1.0f, new PhysicalDimensions(0.2f, 0.2f, 0.2f), new Vector3(6f, 0.2f, 6f));
            var eqPhysB = CreateItem("item-eq-b", "type-eq-b", 1.0f, new PhysicalDimensions(0.2f, 0.2f, 0.2f), new Vector3(7f, 0.2f, 7f));
            eqPhysA.Bind(eqModel, worldId, genId);
            eqPhysB.Bind(eqModel, worldId, genId);
            var eqNiA = reg.Find(x => x.StableId == "item-eq-a");
            var eqNiB = reg.Find(x => x.StableId == "item-eq-b");

            var eqActions = new NpcActionApi(agentId, worldId, actorGo.transform, handGo.transform, new[] { eqNiA, eqNiB });
            eqActions.PhysicalModel = eqModel;
            eqActions.PhysicalAuthority = auth;

            var eqBindings = new List<PhysicalItemRuntimeBinding>
            {
                new PhysicalItemRuntimeBinding(eqPhysA, eqNiA),
                new PhysicalItemRuntimeBinding(eqPhysB, eqNiB)
            };
            var eqPayload = new PhysicalSavePayload
            {
                worldId = worldId,
                generationId = genId,
                actorId = agentId,
                tick = 100,
                items = new List<SavedItemRecord>
                {
                    new SavedItemRecord
                    {
                        itemId = "item-eq-a",
                        itemTypeId = "type-eq-a",
                        location = ItemLocationKind.Free,
                        massKg = 1.0f,
                        dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                        position = new Vector3(6f, 0.2f, 6f),
                        rotation = Quaternion.identity,
                        lastUpdatedTick = 100
                    },
                    new SavedItemRecord
                    {
                        itemId = "item-eq-b",
                        itemTypeId = "type-eq-b",
                        location = ItemLocationKind.Free,
                        massKg = 1.0f,
                        dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                        position = new Vector3(7f, 0.2f, 7f),
                        rotation = Quaternion.identity,
                        lastUpdatedTick = 100
                    }
                }
            };

            // Assert fixture preconditions prior to mutation:
            // Equal masses, unique bodies, valid components, valid initial restore
            Check(Mathf.Abs(defEqA.massKg - defEqB.massKg) <= 0.0001f &&
                  Mathf.Abs(eqPhysA.Body.mass - 1.0f) <= 0.0001f &&
                  Mathf.Abs(eqPhysB.Body.mass - 1.0f) <= 0.0001f &&
                  Mathf.Abs(eqPhysA.Body.mass - eqPhysB.Body.mass) <= 0.0001f &&
                  eqPhysA.Body != eqPhysB.Body &&
                  eqPhysA.IsValid() && eqPhysB.IsValid() &&
                  eqActions.IsObjectRegistered("item-eq-a", eqNiA) &&
                  eqActions.IsObjectRegistered("item-eq-b", eqNiB) &&
                  eqActions.Held == null,
                  "multi-item-restore-equal-mass-fixture-preconditions", null);

            bool eqPreRestore = ItemPersistence.RestoreRuntime(eqPayload, eqModel, eqActions, eqBindings);
            Check(eqPreRestore, "multi-item-restore-equal-mass-baseline-success", null);

            FullSceneState CaptureEqState()
            {
                var st = new FullSceneState
                {
                    tick = eqModel.Tick,
                    highestReceiptId = eqModel.HighestReceiptRequestId,
                    itemCount = eqModel.ItemCount,
                    itemSnapshots = eqModel.GetAllItemSnapshots(),
                    receipts = eqModel.GetAllReceiptRecords(),
                    held = eqActions.Held,
                    items = new Dictionary<string, PhysicalItemState>(StringComparer.Ordinal),
                    extraObjects = new List<ExtraObjectState>()
                };
                foreach (var b in eqBindings)
                {
                    var p = b.physicalItem;
                    var ni = b.interactable;
                    st.items[p.itemId] = new PhysicalItemState
                    {
                        pos = p.transform.position,
                        rot = p.transform.rotation,
                        scale = p.transform.lossyScale,
                        parent = p.transform.parent,
                        body = p.Body,
                        bodyMass = p.Body != null ? p.Body.mass : 0f,
                        isKinematic = p.Body != null && p.Body.isKinematic,
                        useGravity = p.Body != null && p.Body.useGravity,
                        collider = p.ItemCollider,
                        colEnabled = p.ItemCollider != null && p.ItemCollider.enabled,
                        isTrigger = p.ItemCollider != null && p.ItemCollider.isTrigger,
                        interactable = ni,
                        heldBy = ni != null ? ni.HeldBy : ""
                    };
                }
                return st;
            }

            bool MatchesEqState(FullSceneState before)
            {
                if (before == null) return false;
                if (eqModel.Tick != before.tick) return false;
                if (eqModel.ItemCount != before.itemCount) return false;
                if (eqActions.Held != before.held) return false;
                foreach (var b in eqBindings)
                {
                    var p = b.physicalItem;
                    if (!before.items.TryGetValue(p.itemId, out var s)) return false;
                    if (Vector3.Distance(p.transform.position, s.pos) > 0.0001f) return false;
                    if (Quaternion.Angle(p.transform.rotation, s.rot) > 0.01f) return false;
                    if (p.Body != s.body) return false;
                    if (p.Body != null && Mathf.Abs(p.Body.mass - s.bodyMass) > 0.0001f) return false;
                    if (p.ItemCollider != s.collider) return false;
                }
                return true;
            }

            // Mutate: cross-wire Body alias across bindings
            var origEqBodyA = eqPhysA.Body;
            eqPhysA.Body = eqPhysB.Body;

            // Precondition assertion after mutation: Body masses are still strictly equal (both 1.0kg, matching defEqA)
            // proving that refusal is specifically due to reference uniqueness/ownership refusal, not preexisting mass mismatch
            Check(eqPhysA.Body == eqPhysB.Body &&
                  Mathf.Abs(eqPhysA.Body.mass - defEqA.massKg) <= 0.0001f &&
                  Mathf.Abs(eqPhysB.Body.mass - defEqB.massKg) <= 0.0001f,
                  "multi-item-restore-equal-mass-crosswire-preserves-mass-match", null);

            var stateBeforeE13 = CaptureEqState();
            bool refCrosswiredBody = ItemPersistence.RestoreRuntime(eqPayload, eqModel, eqActions, eqBindings);
            Check(!refCrosswiredBody && MatchesEqState(stateBeforeE13), "multi-item-restore-refusal-crosswired-body-preserves-state", null);

            // Undo mutation after state comparison
            eqPhysA.Body = origEqBodyA;

            // Clean up dedicated equal-mass fixture
            UnityEngine.Object.DestroyImmediate(eqPhysA.gameObject);
            UnityEngine.Object.DestroyImmediate(eqPhysB.gameObject);
            reg.Remove(eqNiA);
            reg.Remove(eqNiB);

            // E14. Cross-wired Collider alias across bindings (Finding 2)
            var origRockCol = rockPhys.ItemCollider;
            rockPhys.ItemCollider = chiselPhys.ItemCollider;
            var stateBeforeE14 = CaptureFullState();
            bool refCrosswiredCol = ItemPersistence.RestoreRuntime(payload, model, actions, bindings);
            Check(!refCrosswiredCol && MatchesFullState(stateBeforeE14), "multi-item-restore-refusal-crosswired-collider-preserves-state", null);
            rockPhys.ItemCollider = origRockCol;

            // E15. Foreign GameObject Body outside own GameObject (Finding 2)
            var origRockBody = rockPhys.Body;
            var foreignBody = alienGo.AddComponent<Rigidbody>();
            rockPhys.Body = foreignBody;
            var stateBeforeE15 = CaptureFullState(extraGameObjects: new[] { alienGo });
            bool refForeignBody = ItemPersistence.RestoreRuntime(payload, model, actions, bindings);
            Check(!refForeignBody && MatchesFullState(stateBeforeE15), "multi-item-restore-refusal-foreign-gameobject-body-preserves-state", null);
            rockPhys.Body = origRockBody;
            UnityEngine.Object.DestroyImmediate(foreignBody);

            // E16. Incoherent physical flags: Free item with kinematic Body (Finding 2)
            rockPhys.Body.isKinematic = true;
            var stateBeforeE16 = CaptureFullState();
            bool refIncoherentFlags = ItemPersistence.RestoreRuntime(payload, model, actions, bindings);
            Check(!refIncoherentFlags && MatchesFullState(stateBeforeE16), "multi-item-restore-refusal-incoherent-physics-flags-preserves-state", null);
            rockPhys.Body.isKinematic = false;

            // E17. Model item type mismatch against live binding (Finding 1)
            chestPhys.itemTypeId = "multi-type-rock";
            var stateBeforeE17 = CaptureFullState();
            bool refTypeMismatch = ItemPersistence.RestoreRuntime(payload, model, actions, bindings);
            Check(!refTypeMismatch && MatchesFullState(stateBeforeE17), "multi-item-restore-refusal-model-type-mismatch-preserves-state", null);
            chestPhys.itemTypeId = "multi-type-chest";

            // E18. Non-unit scale refusal
            rockPhys.transform.localScale = new Vector3(1.5f, 1.0f, 1.0f);
            var stateBeforeE18 = CaptureFullState();
            bool refScale = ItemPersistence.RestoreRuntime(payload, model, actions, bindings);
            Check(!refScale && MatchesFullState(stateBeforeE18), "multi-item-restore-refusal-non-unit-scale-preserves-state", null);
            rockPhys.transform.localScale = Vector3.one;

            // Helper-level nonfinite checks and honest Unity runtime boundary reporting
            Check(!ItemDefinition.Finite(float.NaN) && !ItemDefinition.Finite(float.PositiveInfinity) && !ItemDefinition.Finite(float.NegativeInfinity),
                  "lossy-scale-finite-helper-contract", null);
            Vector3 preNanScale = rockPhys.transform.localScale;
            rockPhys.transform.localScale = new Vector3(float.NaN, 1.0f, 1.0f);
            bool unityRejectsNonfiniteScale = ItemDefinition.Finite(rockPhys.transform.localScale.x) && rockPhys.transform.localScale == preNanScale;
            Check(unityRejectsNonfiniteScale, "unity-transform-rejects-nonfinite-scale-assignment", null);

            // -------------------------------------------------------------
            // F. Preserved Legacy Behavior and Receipt Replay
            // -------------------------------------------------------------
            // F1. Legacy single-item RestoreRuntime compatibility and refusal
            var singleFreePayload = new PhysicalSavePayload
            {
                worldId = worldId,
                generationId = genId,
                actorId = agentId,
                tick = 105,
                items = new List<SavedItemRecord>
                {
                    new SavedItemRecord
                    {
                        itemId = "item-rock",
                        itemTypeId = "multi-type-rock",
                        location = ItemLocationKind.Free,
                        holderActorId = null,
                        containerItemId = null,
                        massKg = 1.0f,
                        dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                        position = new Vector3(3f, 0.2f, 3f),
                        rotation = Quaternion.identity,
                        lastUpdatedTick = 105
                    }
                }
            };
            var singleModel = new ItemModel(worldId, genId);
            singleModel.RegisterDefinition(defRock);
            singleModel.RegisterItem("item-rock", "multi-type-rock", ItemLocationKind.Free, new Vector3(3f, 0.2f, 3f), Quaternion.identity);
            rockPhys.Bind(singleModel, worldId, genId);
            actions.PhysicalModel = singleModel;
            actions.RestoreHeld(null);

            // Single item restore succeeds with legacy 5-argument method
            bool legOk = ItemPersistence.RestoreRuntime(singleFreePayload, singleModel, actions, rockPhys, rockNi);
            Check(legOk, "legacy-single-item-restore-success", null);

            // Multi-item payload passed to 5-argument method fails closed atomically
            bool legMultiFail = ItemPersistence.RestoreRuntime(payload, singleModel, actions, rockPhys, rockNi);
            Check(!legMultiFail, "legacy-single-item-restore-rejects-multi-item", null);

            // Restore main model & binding on rockPhys
            rockPhys.Bind(model, worldId, genId);
            actions.PhysicalModel = model;
            ItemPersistence.RestoreRuntime(payload, model, actions, bindings);

            // F2. Receipt Replay Verification
            // Existing receipt 10 duplicate replay
            var replayReq = new ItemActionRequest
            {
                requestId = 10,
                action = ItemActionKind.Pickup,
                actorId = agentId,
                itemId = "item-basket"
            };
            var replayReceipt = model.Execute(worldId, genId, replayReq, auth);
            Check(replayReceipt.success && replayReceipt.duplicate, "multi-item-restore-receipt-duplicate-replay", null);

            // Conflict request with same requestId 10 (valid changed request: Drop instead of Pickup)
            var conflictReq = new ItemActionRequest
            {
                requestId = 10,
                action = ItemActionKind.Drop,
                actorId = agentId,
                itemId = "item-basket",
                position = new Vector3(5f, 0.2f, 5f),
                rotation = Quaternion.identity
            };
            Check(ItemDefinition.TryCanonicalizeRotation(conflictReq.rotation, out var canConflictRot),
                  "multi-item-restore-conflict-req-valid-rotation", null);
            string conflictSig = ItemModel.BuildRequestSignature(conflictReq, canConflictRot);
            Check(!string.Equals(conflictSig, canonicalPickupSig, StringComparison.Ordinal),
                  "multi-item-restore-conflict-signature-changed", null);

            var stateBeforeConflict = CaptureFullState();
            var conflictReceipt = model.Execute(worldId, genId, conflictReq, auth);
            Check(!conflictReceipt.success && !conflictReceipt.duplicate && conflictReceipt.code == "request-id-conflict" && MatchesFullState(stateBeforeConflict),
                  "multi-item-restore-receipt-conflict-refusal",
                  $"actual code='{conflictReceipt.code}', success={conflictReceipt.success}, duplicate={conflictReceipt.duplicate}");

            // Monotonic sequence allocation above restored receipts
            Check(model.TryAllocateNextRequestId(out int nextId) && nextId == 11, "multi-item-restore-monotonic-sequence-allocation", null);

            // Subsequent action execution
            var dropReq = new ItemActionRequest
            {
                requestId = nextId,
                action = ItemActionKind.Drop,
                actorId = agentId,
                itemId = "item-basket",
                position = new Vector3(5f, 0.2f, 5f),
                rotation = Quaternion.identity
            };
            var dropReceipt = model.Execute(worldId, genId, dropReq, auth);
            Check(dropReceipt.success && !dropReceipt.duplicate, "multi-item-restore-subsequent-action-success", null);
        }
    }
}
