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
}
