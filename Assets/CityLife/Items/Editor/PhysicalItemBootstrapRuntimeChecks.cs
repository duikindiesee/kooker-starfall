using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using CityLife.World;
using CityLife.World.Editor;

namespace CityLife.Items.Editor
{
    /// <summary>
    /// Additive runtime integration checks verifying:
    /// 1. Cold construction of mixed containment graph with exact stable IDs and single live instances.
    /// 2. Complete interactable registry exposure before NpcActionApi copies it, preserving baseline non-physical interactables.
    /// 3. Cold runtime restoration across Free, Carried, and Stored visibility, colliders, physics, ownership, and mass.
    /// 4. Save and reload stability across fresh bootstrap and model instances for >= 5 consecutive cycles.
    /// 5. Backward compatibility for legacy single-item demonstration save payloads (both Free and Carried).
    /// 6. Strict fail-closed refusal of unknown types, invalid graphs (cycles), duplicate IDs, wrong scopes, corrupt payloads,
    ///    missing hand, nonphysical collisions, and unsupported live reload with zero staged object leaks, unmodified baseline state, and untouched on-disk save bytes.
    /// 7. Restored action receipt replay, conflicting request denial, monotonic sequence allocation, and subsequent execution.
    /// 8. Idempotent repeated initialization and autonomy reset without instance accumulation or receipt reapplication.
    /// 9. Authoritative FixedUpdate synchronization of all eligible Free items.
    /// </summary>
    internal static class PhysicalItemBootstrapRuntimeChecks
    {
        private static ItemDefinition[] CreateSyntheticDefinitions()
        {
            return new[]
            {
                new ItemDefinition
                {
                    itemTypeId = "multi-type-chest",
                    dimensions = new PhysicalDimensions(0.6f, 0.4f, 0.5f),
                    massKg = 5.0f,
                    isContainer = true,
                    maxContainedSlots = 6,
                    maxContainedVolumeM3 = 0.5f,
                    maxContainedMassKg = 40.0f
                },
                new ItemDefinition
                {
                    itemTypeId = "multi-type-basket",
                    dimensions = new PhysicalDimensions(0.3f, 0.25f, 0.3f),
                    massKg = 2.0f,
                    isContainer = true,
                    maxContainedSlots = 4,
                    maxContainedVolumeM3 = 0.08f,
                    maxContainedMassKg = 15.0f
                },
                new ItemDefinition
                {
                    itemTypeId = "multi-type-pouch",
                    dimensions = new PhysicalDimensions(0.15f, 0.15f, 0.15f),
                    massKg = 0.5f,
                    isContainer = true,
                    maxContainedSlots = 2,
                    maxContainedVolumeM3 = 0.01f,
                    maxContainedMassKg = 5.0f
                },
                new ItemDefinition
                {
                    itemTypeId = "multi-type-gem",
                    dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f),
                    massKg = 0.1f,
                    isContainer = false
                },
                new ItemDefinition
                {
                    itemTypeId = "multi-type-chisel",
                    dimensions = new PhysicalDimensions(0.1f, 0.05f, 0.2f),
                    massKg = 0.4f,
                    isContainer = false
                },
                new ItemDefinition
                {
                    itemTypeId = "multi-type-rock",
                    dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                    massKg = 1.0f,
                    isContainer = false
                }
            };
        }

        private static void RegisterSyntheticDefinitions(PhysicalItemCatalog catalog)
        {
            if (catalog == null) return;
            foreach (var def in CreateSyntheticDefinitions())
            {
                catalog.Register(def);
            }
        }

        private static void RegisterBootstrapSyntheticDefinitions(PhysicalItemBootstrap bootstrap)
        {
            if (bootstrap == null) return;
            foreach (var def in CreateSyntheticDefinitions())
            {
                bootstrap.RegisterAuthoritativeDefinition(def);
            }
        }

        private sealed class BindingSnapshot
        {
            public string itemId;
            public Vector3 position;
            public Quaternion rotation;
            public Transform parent;
            public bool isCarried;
            public bool isStored;
            public string storedContainerItemId;
            public Transform carriedHand;
            public string heldBy;
            public bool colliderEnabled;
            public bool colliderIsTrigger;
            public bool rbIsKinematic;
            public bool rbUseGravity;
            public float rbMass;
        }

        private sealed class ResetSnapshot
        {
            public NpcActionApi actionsRef;
            public NpcInteractable heldRef;
            public ItemModel modelRef;
            public int receiptCount;
            public int highestReceiptId;
            public int tick;
            public string phase;
            public int logTotal;
            public Vector3 actorPos;
            public Quaternion actorRot;
            public int requestId;
            public List<BindingSnapshot> bindingSnapshots = new List<BindingSnapshot>();
        }

        public static void Run(
            Scene scene,
            PhysicsScene physics,
            Func<string, GameObject> createGo,
            Action<bool, string, string> check,
            List<string> passed,
            string worldId,
            string genId,
            string agentId,
            GameObject actorGo,
            GameObject handGo)
        {
            string suiteAgentId = NpcAutonomy.AgentId;
            string tempDir = Path.Combine(Path.GetTempPath(), "bootstrap-runtime-checks-" + Guid.NewGuid().ToString("N"));
            var ownedObjects = new List<GameObject>();

            GameObject CreateTrackedGo(string name)
            {
                var go = createGo(name);
                ownedObjects.Add(go);
                return go;
            }

            ResetSnapshot CaptureResetSnapshot(NpcAutonomy autonomy, PhysicalItemBootstrap bootstrap)
            {
                var snap = new ResetSnapshot
                {
                    actionsRef = autonomy.Actions,
                    heldRef = autonomy.Actions != null ? autonomy.Actions.Held : null,
                    modelRef = bootstrap.Model,
                    receiptCount = bootstrap.Model != null ? bootstrap.Model.GetAllReceiptRecords().Count : 0,
                    highestReceiptId = bootstrap.Model != null ? bootstrap.Model.HighestReceiptRequestId : 0,
                    tick = autonomy.Tick,
                    phase = autonomy.Phase,
                    logTotal = autonomy.Log != null ? autonomy.Log.Total : 0,
                    actorPos = autonomy.Actor != null ? autonomy.Actor.transform.position : Vector3.zero,
                    actorRot = autonomy.Actor != null ? autonomy.Actor.transform.rotation : Quaternion.identity,
                    requestId = autonomy.RequestId
                };

                if (bootstrap.Bindings != null)
                {
                    foreach (var b in bootstrap.Bindings)
                    {
                        if (b == null || b.physicalItem == null) continue;
                        var p = b.physicalItem;
                        var col = p.GetComponent<Collider>();
                        var rb = p.GetComponent<Rigidbody>();
                        snap.bindingSnapshots.Add(new BindingSnapshot
                        {
                            itemId = b.itemId,
                            position = p.transform.position,
                            rotation = p.transform.rotation,
                            parent = p.transform.parent,
                            isCarried = p.IsCarried,
                            isStored = p.IsStored,
                            storedContainerItemId = p.BoundContainerItemId,
                            carriedHand = p.CarriedHand,
                            heldBy = b.interactable != null ? b.interactable.HeldBy : null,
                            colliderEnabled = col != null && col.enabled,
                            colliderIsTrigger = col != null && col.isTrigger,
                            rbIsKinematic = rb != null && rb.isKinematic,
                            rbUseGravity = rb != null && rb.useGravity,
                            rbMass = rb != null ? rb.mass : 0f
                        });
                    }
                }

                return snap;
            }

            void AssertResetSnapshotEqual(ResetSnapshot snap, NpcAutonomy autonomy, PhysicalItemBootstrap bootstrap, string label)
            {
                // 1. Actions reference and Held unchanged
                check(autonomy.Actions == snap.actionsRef, $"{label}-actions-ref-preserved", null);
                check((autonomy.Actions != null ? autonomy.Actions.Held : null) == snap.heldRef, $"{label}-actions-held-preserved", null);

                // 2. PhysicalModel and receipts unchanged
                check(bootstrap.Model == snap.modelRef, $"{label}-model-ref-preserved", null);
                check(bootstrap.Model != null && bootstrap.Model.GetAllReceiptRecords().Count == snap.receiptCount,
                      $"{label}-receipt-count-preserved", null);
                check(bootstrap.Model != null && bootstrap.Model.HighestReceiptRequestId == snap.highestReceiptId,
                      $"{label}-highest-receipt-id-preserved", null);

                // 3. Autonomy nonphysical state unchanged
                check(autonomy.Tick == snap.tick, $"{label}-tick-preserved", null);
                check(string.Equals(autonomy.Phase, snap.phase, StringComparison.Ordinal), $"{label}-phase-preserved", null);
                check((autonomy.Log != null ? autonomy.Log.Total : 0) == snap.logTotal, $"{label}-log-total-preserved", null);
                check(autonomy.RequestId == snap.requestId, $"{label}-request-id-preserved", null);
                if (autonomy.Actor != null)
                {
                    check(Vector3.Distance(autonomy.Actor.transform.position, snap.actorPos) <= 0.0001f,
                          $"{label}-actor-position-preserved", null);
                    check(Quaternion.Angle(autonomy.Actor.transform.rotation, snap.actorRot) <= 0.01f,
                          $"{label}-actor-rotation-preserved", null);
                }

                // 4. Physical bindings, hierarchy, physics, colliders, and ownership unchanged
                check(bootstrap.Bindings.Count == snap.bindingSnapshots.Count, $"{label}-bindings-count-preserved", null);
                var curBindingsMap = new Dictionary<string, PhysicalItemRuntimeBinding>(StringComparer.Ordinal);
                foreach (var b in bootstrap.Bindings)
                {
                    if (b != null && !string.IsNullOrEmpty(b.itemId)) curBindingsMap[b.itemId] = b;
                }

                foreach (var bs in snap.bindingSnapshots)
                {
                    check(curBindingsMap.TryGetValue(bs.itemId, out var b), $"{label}-binding-exists-{bs.itemId}", null);
                    if (b == null || b.physicalItem == null) continue;
                    var p = b.physicalItem;
                    check(Vector3.Distance(p.transform.position, bs.position) <= 0.0001f,
                          $"{label}-{bs.itemId}-position-preserved", null);
                    check(Quaternion.Angle(p.transform.rotation, bs.rotation) <= 0.01f,
                          $"{label}-{bs.itemId}-rotation-preserved", null);
                    check(p.transform.parent == bs.parent,
                          $"{label}-{bs.itemId}-parent-preserved", null);
                    check(p.IsCarried == bs.isCarried,
                          $"{label}-{bs.itemId}-is-carried-preserved", null);
                    check(p.IsStored == bs.isStored,
                          $"{label}-{bs.itemId}-is-stored-preserved", null);
                    check(string.Equals(p.BoundContainerItemId, bs.storedContainerItemId, StringComparison.Ordinal),
                          $"{label}-{bs.itemId}-stored-container-preserved", null);
                    check(p.CarriedHand == bs.carriedHand,
                          $"{label}-{bs.itemId}-carried-hand-preserved", null);
                    check(string.Equals(b.interactable != null ? b.interactable.HeldBy : null, bs.heldBy, StringComparison.Ordinal),
                          $"{label}-{bs.itemId}-held-by-preserved", null);

                    var col = p.GetComponent<Collider>();
                    check(col != null && col.enabled == bs.colliderEnabled && col.isTrigger == bs.colliderIsTrigger,
                          $"{label}-{bs.itemId}-collider-preserved", null);

                    var rb = p.GetComponent<Rigidbody>();
                    check(rb != null && rb.isKinematic == bs.rbIsKinematic && rb.useGravity == bs.rbUseGravity && Mathf.Abs(rb.mass - bs.rbMass) <= 0.0001f,
                          $"{label}-{bs.itemId}-rigidbody-preserved", null);
                }
            }

            try
            {
                Directory.CreateDirectory(tempDir);
                var auth = new BasicItemActionAuthority();

                // Setup genuine CharacterPreviewActor, CharacterController, and humanoid Animator from Superhero_Male_FullBody.fbx
                var capsule = actorGo.GetComponent<CharacterController>();
                if (capsule == null)
                {
                    capsule = actorGo.AddComponent<CharacterController>();
                }
                capsule.height = 1.85f;
                capsule.center = new Vector3(0, 0.93f, 0);
                capsule.radius = 0.3f;
                capsule.skinWidth = 0.025f;
                capsule.stepOffset = 0.25f;
                capsule.slopeLimit = 45;
                capsule.minMoveDistance = 0;

                var previewActor = actorGo.GetComponent<CharacterPreviewActor>();
                if (previewActor == null)
                {
                    previewActor = actorGo.AddComponent<CharacterPreviewActor>();
                }
                previewActor.Capsule = capsule;

                var bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAssetImport.Body);
                check(bodyPrefab != null, "bootstrap-fixture-body-prefab-loaded", null);
                if (bodyPrefab != null)
                {
                    var bodyInstance = UnityEngine.Object.Instantiate(bodyPrefab, actorGo.transform);
                    bodyInstance.transform.localPosition = Vector3.zero;
                    bodyInstance.transform.localRotation = Quaternion.identity;
                    ownedObjects.Add(bodyInstance);
                    previewActor.Animator = bodyInstance.GetComponent<Animator>();
                }
                check(previewActor.Animator != null, "bootstrap-fixture-animator-present", null);
                check(previewActor.Animator != null && previewActor.Animator.avatar != null &&
                      previewActor.Animator.avatar.isValid && previewActor.Animator.avatar.isHuman,
                      "bootstrap-fixture-humanoid-avatar-valid", null);

                Transform genuineHand = null;
                if (previewActor.Animator != null)
                {
                    genuineHand = previewActor.Animator.GetBoneTransform(HumanBodyBones.RightHand);
                }
                check(genuineHand != null, "bootstrap-fixture-genuine-right-hand-proven", null);

                void ConfigureAutonomy(NpcAutonomy autonomy, GameObject brainGo)
                {
                    if (autonomy == null) return;
                    autonomy.Actor = previewActor;
                    if (autonomy.Log == null && brainGo != null)
                    {
                        autonomy.Log = brainGo.AddComponent<NpcDecisionLog>();
                    }
                    if (autonomy.OptionalPlanner == null && brainGo != null)
                    {
                        autonomy.OptionalPlanner = brainGo.AddComponent<NpcOptionalPlanner>();
                    }
                    if (autonomy.Survival == null && brainGo != null)
                    {
                        autonomy.Survival = brainGo.AddComponent<StarfallSurvivalAutonomy>();
                    }
                    autonomy.Registry = Array.Empty<NpcInteractable>();
                    autonomy.InstanceWorldId = worldId;
                    autonomy.SpawnPosition = actorGo.transform.position;
                }

                // -------------------------------------------------------------
                // 1. Baseline Cold Construction (No Save File)
                // -------------------------------------------------------------
                var baseBrainGo = CreateTrackedGo("base-brain-go");
                var baseAutonomy = baseBrainGo.AddComponent<NpcAutonomy>();
                ConfigureAutonomy(baseAutonomy, baseBrainGo);
                baseAutonomy.InstanceWorldId = worldId;
                var baseBootstrap = baseBrainGo.AddComponent<PhysicalItemBootstrap>();
                baseBootstrap.Brain = baseAutonomy;
                baseAutonomy.PhysicalItems = baseBootstrap;

                check(baseBootstrap.DemonstrationItem != null,
                      "bootstrap-cold-construction-baseline-demo-item-created", null);
                check(string.Equals(baseBootstrap.DemonstrationItemId, "canyon-artifact-01", StringComparison.Ordinal),
                      "bootstrap-cold-construction-baseline-demo-id-exact", null);
                check(Mathf.Abs(baseBootstrap.DemonstrationItem.massKg - 2.5f) <= 0.0001f,
                      "bootstrap-cold-construction-baseline-demo-mass-exact", null);
                check(baseBootstrap.Model != null && baseBootstrap.Model.ItemCount == 1,
                      "bootstrap-cold-construction-baseline-model-count", null);
                check(baseBootstrap.Model.TryGetItem("canyon-artifact-01", out var baseSnap) && baseSnap.location == ItemLocationKind.Free,
                      "bootstrap-cold-construction-baseline-demo-free", null);
                check(!baseBootstrap.SaveRejected && !baseBootstrap.HasSavedPayload,
                      "bootstrap-cold-construction-baseline-flags", null);

                int baseInteractableCount = 0;
                foreach (var it in baseBootstrap.AllInteractables)
                {
                    if (it != null) baseInteractableCount++;
                }
                check(baseInteractableCount == 1,
                      "bootstrap-cold-construction-baseline-all-interactables-count", null);

                // -------------------------------------------------------------
                // 1B. Authoritative Demonstration Definition Configuration & Baseline Refusal
                // -------------------------------------------------------------
                var nondefaultBrainGo = CreateTrackedGo("nondefault-demo-brain-go");
                var nondefaultAutonomy = nondefaultBrainGo.AddComponent<NpcAutonomy>();
                ConfigureAutonomy(nondefaultAutonomy, nondefaultBrainGo);
                nondefaultAutonomy.InstanceWorldId = worldId;
                nondefaultBrainGo.SetActive(false);
                var nondefaultBootstrap = nondefaultBrainGo.AddComponent<PhysicalItemBootstrap>();
                nondefaultBootstrap.DemonstrationItemId = "canyon-custom-01";
                nondefaultBootstrap.DemonstrationItemMassKg = 3.5f;
                nondefaultBootstrap.DemonstrationItemDimensions = new Vector3(0.3f, 0.4f, 0.5f);
                nondefaultBootstrap.Brain = nondefaultAutonomy;
                nondefaultAutonomy.PhysicalItems = nondefaultBootstrap;
                nondefaultBrainGo.SetActive(true);

                check(nondefaultBootstrap.DemonstrationItem != null,
                      "bootstrap-configured-demo-item-created", null);
                check(Mathf.Abs(nondefaultBootstrap.DemonstrationItem.massKg - 3.5f) <= 0.0001f,
                      "bootstrap-configured-demo-item-mass-exact", null);
                check(Mathf.Abs(nondefaultBootstrap.DemonstrationItem.dimensions.width - 0.3f) <= 0.0001f &&
                      Mathf.Abs(nondefaultBootstrap.DemonstrationItem.dimensions.height - 0.4f) <= 0.0001f &&
                      Mathf.Abs(nondefaultBootstrap.DemonstrationItem.dimensions.depth - 0.5f) <= 0.0001f,
                      "bootstrap-configured-demo-item-dimensions-exact", null);
                check(nondefaultBootstrap.Model != null &&
                      nondefaultBootstrap.Model.TryGetDefinition(nondefaultBootstrap.DemonstrationItemTypeId, out var nondefModelDef) &&
                      Mathf.Abs(nondefModelDef.massKg - 3.5f) <= 0.0001f,
                      "bootstrap-configured-demo-model-mass-matches", null);
                check(nondefaultBootstrap.Catalog != null &&
                      nondefaultBootstrap.Catalog.TryGet(nondefaultBootstrap.DemonstrationItemTypeId, out var nondefCatDef) &&
                      Mathf.Abs(nondefCatDef.massKg - 3.5f) <= 0.0001f,
                      "bootstrap-configured-demo-catalog-mass-matches", null);
                UnityEngine.Object.DestroyImmediate(nondefaultBrainGo);

                // Invalid config contract: negative mass rejected before any invalid objects created
                int preInvalidSceneRoots = scene.rootCount;
                var invalidBrainGo = CreateTrackedGo("invalid-demo-brain-go");
                var invalidAutonomy = invalidBrainGo.AddComponent<NpcAutonomy>();
                ConfigureAutonomy(invalidAutonomy, invalidBrainGo);
                invalidAutonomy.InstanceWorldId = worldId;
                invalidBrainGo.SetActive(false);
                var invalidBootstrap = invalidBrainGo.AddComponent<PhysicalItemBootstrap>();
                invalidBootstrap.DemonstrationItemMassKg = -1.0f; // Invalid mass
                invalidBootstrap.Brain = invalidAutonomy;
                invalidAutonomy.PhysicalItems = invalidBootstrap;
                invalidBrainGo.SetActive(true);

                check(invalidBootstrap.SaveRejected,
                      "bootstrap-invalid-config-save-rejected", null);
                check(invalidBootstrap.DemonstrationItem == null,
                      "bootstrap-invalid-config-zero-demo-item", null);
                check(invalidBootstrap.Bindings.Count == 0,
                      "bootstrap-invalid-config-zero-bindings", null);
                UnityEngine.Object.DestroyImmediate(invalidBrainGo);
                check(scene.rootCount == preInvalidSceneRoots,
                      "bootstrap-invalid-config-zero-leaked-roots", null);

                // -------------------------------------------------------------
                // 2. Multi-Item Mixed Graph Construction & Save to Disk
                // -------------------------------------------------------------
                var authorModel = new ItemModel(worldId, genId);
                var catalog = PhysicalItemCatalog.CreateDefaultCatalog();
                RegisterSyntheticDefinitions(catalog);
                catalog.PopulateModel(authorModel);

                // Hierarchy:
                // Free: chest-01 (multi-type-chest, 5.0kg) -> Stored: chisel-01 (multi-type-chisel, 0.4kg)
                // Carried: basket-01 (multi-type-basket, 2.0kg) -> Stored: pouch-01 (multi-type-pouch, 0.5kg) -> Stored: gem-01 (multi-type-gem, 0.1kg)
                // Free: rock-01 (multi-type-rock, 1.0kg)
                // Register ALL items Free initially with checked registration success
                check(authorModel.RegisterItem("chest-01", "multi-type-chest", ItemLocationKind.Free, new Vector3(2f, 0.2f, 2f), Quaternion.identity),
                      "bootstrap-author-register-chest-free", null);
                check(authorModel.RegisterItem("basket-01", "multi-type-basket", ItemLocationKind.Free, new Vector3(1f, 0.2f, 1f), Quaternion.identity),
                      "bootstrap-author-register-basket-free", null);
                check(authorModel.RegisterItem("pouch-01", "multi-type-pouch", ItemLocationKind.Free, new Vector3(1.5f, 0.2f, 1.5f), Quaternion.identity),
                      "bootstrap-author-register-pouch-free", null);
                check(authorModel.RegisterItem("gem-01", "multi-type-gem", ItemLocationKind.Free, new Vector3(1.6f, 0.2f, 1.6f), Quaternion.identity),
                      "bootstrap-author-register-gem-free", null);
                check(authorModel.RegisterItem("chisel-01", "multi-type-chisel", ItemLocationKind.Free, new Vector3(2f, 0.2f, 2f), Quaternion.identity),
                      "bootstrap-author-register-chisel-free", null);
                check(authorModel.RegisterItem("rock-01", "multi-type-rock", ItemLocationKind.Free, new Vector3(3f, 0.2f, 3f), Quaternion.identity),
                      "bootstrap-author-register-rock-free", null);

                int authorReqSeq = 0;

                // Step A: Pick up chisel-01 and store in chest-01
                var pickupChiselReq = new ItemActionRequest
                {
                    requestId = ++authorReqSeq,
                    action = ItemActionKind.Pickup,
                    actorId = suiteAgentId,
                    itemId = "chisel-01"
                };
                var pickupChiselReceipt = authorModel.Execute(worldId, genId, pickupChiselReq, auth);
                check(pickupChiselReceipt.success && pickupChiselReceipt.requestId == pickupChiselReq.requestId,
                      "bootstrap-author-pickup-chisel", null);

                var storeChiselReq = new ItemActionRequest
                {
                    requestId = ++authorReqSeq,
                    action = ItemActionKind.Store,
                    actorId = suiteAgentId,
                    itemId = "chisel-01",
                    targetId = "chest-01"
                };
                var storeChiselReceipt = authorModel.Execute(worldId, genId, storeChiselReq, auth);
                check(storeChiselReceipt.success && storeChiselReceipt.requestId == storeChiselReq.requestId,
                      "bootstrap-author-store-chisel", null);

                // Step B: Pick up gem-01 and store in pouch-01
                var pickupGemReq = new ItemActionRequest
                {
                    requestId = ++authorReqSeq,
                    action = ItemActionKind.Pickup,
                    actorId = suiteAgentId,
                    itemId = "gem-01"
                };
                var pickupGemReceipt = authorModel.Execute(worldId, genId, pickupGemReq, auth);
                check(pickupGemReceipt.success && pickupGemReceipt.requestId == pickupGemReq.requestId,
                      "bootstrap-author-pickup-gem", null);

                var storeGemReq = new ItemActionRequest
                {
                    requestId = ++authorReqSeq,
                    action = ItemActionKind.Store,
                    actorId = suiteAgentId,
                    itemId = "gem-01",
                    targetId = "pouch-01"
                };
                var storeGemReceipt = authorModel.Execute(worldId, genId, storeGemReq, auth);
                check(storeGemReceipt.success && storeGemReceipt.requestId == storeGemReq.requestId,
                      "bootstrap-author-store-gem", null);

                // Step C: Pick up pouch-01 (containing gem-01) and store in basket-01
                var pickupPouchReq = new ItemActionRequest
                {
                    requestId = ++authorReqSeq,
                    action = ItemActionKind.Pickup,
                    actorId = suiteAgentId,
                    itemId = "pouch-01"
                };
                var pickupPouchReceipt = authorModel.Execute(worldId, genId, pickupPouchReq, auth);
                check(pickupPouchReceipt.success && pickupPouchReceipt.requestId == pickupPouchReq.requestId,
                      "bootstrap-author-pickup-pouch", null);

                var storePouchReq = new ItemActionRequest
                {
                    requestId = ++authorReqSeq,
                    action = ItemActionKind.Store,
                    actorId = suiteAgentId,
                    itemId = "pouch-01",
                    targetId = "basket-01"
                };
                var storePouchReceipt = authorModel.Execute(worldId, genId, storePouchReq, auth);
                check(storePouchReceipt.success && storePouchReceipt.requestId == storePouchReq.requestId,
                      "bootstrap-author-store-pouch", null);

                // Step D: Pick up basket-01 (containing pouch-01 and gem-01) to establish carried root
                var pickupBasketReq = new ItemActionRequest
                {
                    requestId = ++authorReqSeq,
                    action = ItemActionKind.Pickup,
                    actorId = suiteAgentId,
                    itemId = "basket-01"
                };
                var pickupReceipt = authorModel.Execute(worldId, genId, pickupBasketReq, auth);
                check(pickupReceipt.success && pickupReceipt.requestId == pickupBasketReq.requestId,
                      "bootstrap-author-model-pickup-receipt", null);

                int expectedAuthorHighestReceiptId = authorModel.HighestReceiptRequestId;
                int expectedAuthorReceiptCount = authorModel.GetAllReceiptRecords().Count;

                string mixedSavePath = Path.Combine(tempDir, "mixed-graph.json");
                var mixedPayload = ItemPersistence.CreateSnapshot(authorModel, suiteAgentId);
                check(ItemPersistence.SaveAtomic(mixedSavePath, mixedPayload),
                      "bootstrap-mixed-save-atomic-success", null);
                check(File.Exists(mixedSavePath),
                      "bootstrap-mixed-save-exists-on-disk", null);
                check(mixedPayload.receipts.Count == expectedAuthorReceiptCount,
                      "bootstrap-mixed-save-receipt-count-matches", null);
                byte[] origMixedBytes = File.ReadAllBytes(mixedSavePath);

                // -------------------------------------------------------------
                // 3. Cold Construction of Mixed Graph with Exact Stable IDs
                // -------------------------------------------------------------
                var coldBrainGo = CreateTrackedGo("cold-brain-go");
                var coldAutonomy = coldBrainGo.AddComponent<NpcAutonomy>();
                ConfigureAutonomy(coldAutonomy, coldBrainGo);
                coldAutonomy.InstanceWorldId = worldId;
                var coldBootstrap = coldBrainGo.AddComponent<PhysicalItemBootstrap>();
                coldBootstrap.Brain = coldAutonomy;
                coldAutonomy.PhysicalItems = coldBootstrap;
                RegisterBootstrapSyntheticDefinitions(coldBootstrap);

                // Load mixed graph save payload
                bool coldLoaded = coldBootstrap.LoadSavePayload(mixedSavePath);
                check(coldLoaded, "bootstrap-cold-construction-mixed-graph-load-success", null);
                check(coldBootstrap.HasSavedPayload && !coldBootstrap.SaveRejected,
                      "bootstrap-cold-construction-mixed-flags-valid", null);
                check(coldBootstrap.Bindings.Count == 6,
                      "bootstrap-cold-construction-exact-binding-count", null);

                var expectedIds = new HashSet<string>(StringComparer.Ordinal)
                {
                    "chest-01", "basket-01", "pouch-01", "gem-01", "chisel-01", "rock-01"
                };
                var foundIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var b in coldBootstrap.Bindings)
                {
                    check(b != null && b.physicalItem != null && b.interactable != null,
                          "bootstrap-cold-binding-non-null", b?.itemId);
                    check(expectedIds.Contains(b.itemId),
                          "bootstrap-cold-binding-expected-id", b.itemId);
                    check(foundIds.Add(b.itemId),
                          "bootstrap-cold-binding-unique-instance", b.itemId);
                    check(string.Equals(b.physicalItem.itemId, b.itemId, StringComparison.Ordinal),
                          "bootstrap-cold-physical-item-id-match", b.itemId);
                    check(string.Equals(b.interactable.StableId, b.itemId, StringComparison.Ordinal),
                          "bootstrap-cold-interactable-id-match", b.itemId);
                }
                check(foundIds.Count == 6, "bootstrap-cold-construction-all-six-ids-present", null);

                // Baseline demo item canyon-artifact-01 must NOT exist in the scene or bindings (no duplicate demo)
                check(!foundIds.Contains("canyon-artifact-01"),
                      "bootstrap-cold-no-leftover-demo-item", null);

                // -------------------------------------------------------------
                // 4. Registry Exposure Before NpcActionApi Copies It
                // -------------------------------------------------------------
                var nonphysCargoGo = CreateTrackedGo("base-cargo-nonphys");
                var cargoNi = nonphysCargoGo.AddComponent<NpcInteractable>();
                cargoNi.StableId = "cargo-nonphys";
                cargoNi.WorldId = worldId;
                cargoNi.Kind = NpcObjectKind.Item;
                cargoNi.Permission = true;

                var nonphysDepotGo = CreateTrackedGo("base-depot-nonphys");
                var depotNi = nonphysDepotGo.AddComponent<NpcInteractable>();
                depotNi.StableId = "depot-nonphys";
                depotNi.WorldId = worldId;
                depotNi.Kind = NpcObjectKind.Destination;
                depotNi.Permission = true;

                coldAutonomy.Registry = new[] { cargoNi, depotNi };

                var allInteractablesList = new List<NpcInteractable>(coldAutonomy.AllInteractables);
                check(allInteractablesList.Count == 8,
                      "bootstrap-registry-includes-all-eight-interactables", $"actual count={allInteractablesList.Count}");

                var seenRegistryIds = new HashSet<string>(StringComparer.Ordinal);
                bool cargoFound = false, depotFound = false;
                foreach (var ni in allInteractablesList)
                {
                    check(seenRegistryIds.Add(ni.StableId),
                          "bootstrap-registry-unique-id", ni.StableId);
                    if (string.Equals(ni.StableId, "cargo-nonphys", StringComparison.Ordinal)) cargoFound = true;
                    if (string.Equals(ni.StableId, "depot-nonphys", StringComparison.Ordinal)) depotFound = true;
                }
                check(cargoFound && depotFound,
                      "bootstrap-registry-baseline-nonphysical-preserved", null);

                // Construct NpcActionApi: must succeed cleanly without throwing duplicate ID exception
                var coldActions = new NpcActionApi(suiteAgentId, worldId, actorGo.transform, genuineHand, coldAutonomy.AllInteractables);
                check(coldActions.IsObjectRegistered("chest-01", coldBootstrap.Bindings[0].interactable) ||
                      coldActions.IsObjectRegistered("cargo-nonphys", cargoNi),
                      "bootstrap-actions-api-constructed-cleanly", null);

                foreach (var b in coldBootstrap.Bindings)
                {
                    check(coldActions.IsObjectRegistered(b.itemId, b.interactable),
                          "bootstrap-actions-registers-physical-item", b.itemId);
                }

                // -------------------------------------------------------------
                // 5. Cold Restore: Free / Carried / Stored Physics, Visibility, Ownership, Mass
                // -------------------------------------------------------------
                coldBootstrap.OnActionsCreated(coldActions);

                var bindingMap = new Dictionary<string, PhysicalItemRuntimeBinding>(StringComparer.Ordinal);
                foreach (var b in coldBootstrap.Bindings) bindingMap[b.itemId] = b;

                // Carried root: basket-01
                var basketB = bindingMap["basket-01"];
                check(basketB.physicalItem.IsCarried,
                      "bootstrap-cold-restore-basket-is-carried", null);
                check(basketB.physicalItem.CarriedHand == genuineHand,
                      "bootstrap-cold-restore-basket-carried-hand-match", null);
                check(coldActions.Held == basketB.interactable,
                      "bootstrap-cold-restore-actions-held-match", null);
                check(string.Equals(basketB.interactable.HeldBy, suiteAgentId, StringComparison.Ordinal),
                      "bootstrap-cold-restore-basket-held-by-agent", null);
                check(basketB.physicalItem.ItemCollider != null && basketB.physicalItem.ItemCollider.enabled && basketB.physicalItem.ItemCollider.isTrigger,
                      "bootstrap-cold-restore-basket-trigger-collider-enabled", null);
                check(basketB.physicalItem.Body != null && basketB.physicalItem.Body.isKinematic,
                      "bootstrap-cold-restore-basket-kinematic-body", null);

                var basketRends = new List<Renderer>();
                basketB.physicalItem.GetOwnedRenderers(basketRends);
                check(basketRends.Count > 0 && basketRends[0].enabled,
                      "bootstrap-cold-restore-basket-renderer-visible", null);

                // Stored descendants: pouch-01 (in basket) and gem-01 (in pouch)
                var pouchB = bindingMap["pouch-01"];
                var gemB = bindingMap["gem-01"];
                check(pouchB.physicalItem.IsStored && string.Equals(pouchB.physicalItem.BoundContainerItemId, "basket-01", StringComparison.Ordinal),
                      "bootstrap-cold-restore-pouch-stored-in-basket", null);
                check(pouchB.physicalItem.transform.parent == basketB.physicalItem.transform,
                      "bootstrap-cold-restore-pouch-parented-to-basket", null);
                check(pouchB.physicalItem.ItemCollider != null && !pouchB.physicalItem.ItemCollider.enabled,
                      "bootstrap-cold-restore-pouch-collider-disabled", null);
                check(pouchB.physicalItem.Body != null && pouchB.physicalItem.Body.isKinematic,
                      "bootstrap-cold-restore-pouch-body-kinematic", null);
                var pouchRends = new List<Renderer>();
                pouchB.physicalItem.GetOwnedRenderers(pouchRends);
                check(pouchRends.Count > 0 && !pouchRends[0].enabled,
                      "bootstrap-cold-restore-pouch-renderer-hidden", null);

                check(gemB.physicalItem.IsStored && string.Equals(gemB.physicalItem.BoundContainerItemId, "pouch-01", StringComparison.Ordinal),
                      "bootstrap-cold-restore-gem-stored-in-pouch", null);
                check(gemB.physicalItem.transform.parent == pouchB.physicalItem.transform,
                      "bootstrap-cold-restore-gem-parented-to-pouch", null);
                check(gemB.physicalItem.ItemCollider != null && !gemB.physicalItem.ItemCollider.enabled,
                      "bootstrap-cold-restore-gem-collider-disabled", null);

                // Stored in Free root: chisel-01 (in chest-01)
                var chestB = bindingMap["chest-01"];
                var chiselB = bindingMap["chisel-01"];
                check(chiselB.physicalItem.IsStored && string.Equals(chiselB.physicalItem.BoundContainerItemId, "chest-01", StringComparison.Ordinal),
                      "bootstrap-cold-restore-chisel-stored-in-chest", null);
                check(chiselB.physicalItem.transform.parent == chestB.physicalItem.transform,
                      "bootstrap-cold-restore-chisel-parented-to-chest", null);
                check(chiselB.physicalItem.ItemCollider != null && !chiselB.physicalItem.ItemCollider.enabled,
                      "bootstrap-cold-restore-chisel-collider-disabled", null);

                // Free roots: chest-01 and rock-01
                var rockB = bindingMap["rock-01"];
                check(!chestB.physicalItem.IsCarried && !chestB.physicalItem.IsStored,
                      "bootstrap-cold-restore-chest-is-free", null);
                check(chestB.physicalItem.ItemCollider != null && chestB.physicalItem.ItemCollider.enabled && !chestB.physicalItem.ItemCollider.isTrigger,
                      "bootstrap-cold-restore-chest-solid-collider-enabled", null);
                check(chestB.physicalItem.Body != null && !chestB.physicalItem.Body.isKinematic && chestB.physicalItem.Body.useGravity,
                      "bootstrap-cold-restore-chest-dynamic-physics", null);

                check(!rockB.physicalItem.IsCarried && !rockB.physicalItem.IsStored,
                      "bootstrap-cold-restore-rock-is-free", null);
                check(rockB.physicalItem.ItemCollider != null && rockB.physicalItem.ItemCollider.enabled && !rockB.physicalItem.ItemCollider.isTrigger,
                      "bootstrap-cold-restore-rock-solid-collider-enabled", null);

                // Ownership and Masses:
                // basket carried mass = 2.0 (basket) + 0.5 (pouch) + 0.1 (gem) = 2.6kg
                float carriedMass = coldBootstrap.Model.GetActorCarriedMassKg(suiteAgentId);
                check(Mathf.Abs(carriedMass - 2.6f) <= 0.0001f,
                      "bootstrap-cold-restore-carried-mass-exact", $"actual carriedMass={carriedMass}");
                float basketContainedMass = coldBootstrap.Model.GetContainerContainedMassKg("basket-01");
                check(coldBootstrap.Model.TryGetItem("basket-01", out _) && Mathf.Abs(basketContainedMass - 0.6f) <= 0.0001f,
                      "bootstrap-cold-restore-basket-contained-mass-exact", $"actual={basketContainedMass}");
                float chestTotalMass = coldBootstrap.Model.GetItemTotalMassKg("chest-01");
                check(coldBootstrap.Model.TryGetItem("chest-01", out _) && Mathf.Abs(chestTotalMass - 5.4f) <= 0.0001f,
                      "bootstrap-cold-restore-chest-total-mass-exact", $"actual={chestTotalMass}");

                // -------------------------------------------------------------
                // 6. Save/Reload Stability Across >= 5 Cycles
                // -------------------------------------------------------------
                string cycleSavePath = Path.Combine(tempDir, "cycle-save.json");
                var currentSnap = ItemPersistence.CreateSnapshot(coldBootstrap.Model, suiteAgentId);
                check(ItemPersistence.SaveAtomic(cycleSavePath, currentSnap),
                      "bootstrap-save-reload-cycle-0-save", null);

                // Clean up previous test instances so baselineSceneRoots can be measured cleanly
                UnityEngine.Object.DestroyImmediate(coldBrainGo);
                UnityEngine.Object.DestroyImmediate(baseBrainGo);
                UnityEngine.Object.DestroyImmediate(nonphysCargoGo);
                UnityEngine.Object.DestroyImmediate(nonphysDepotGo);

                int baselineSceneRoots = scene.rootCount;

                for (int cycle = 1; cycle <= 5; cycle++)
                {
                    var freshBrainGo = CreateTrackedGo($"fresh-brain-cycle-{cycle}");
                    var freshAutonomy = freshBrainGo.AddComponent<NpcAutonomy>();
                    ConfigureAutonomy(freshAutonomy, freshBrainGo);
                    freshAutonomy.InstanceWorldId = worldId;
                    var freshBootstrap = freshBrainGo.AddComponent<PhysicalItemBootstrap>();
                    freshBootstrap.Brain = freshAutonomy;
                    freshAutonomy.PhysicalItems = freshBootstrap;
                    RegisterBootstrapSyntheticDefinitions(freshBootstrap);

                    bool cycleLoaded = freshBootstrap.LoadSavePayload(cycleSavePath);
                    check(cycleLoaded, $"bootstrap-save-reload-cycle-{cycle}-load-success", null);
                    check(freshBootstrap.Bindings.Count == 6,
                          $"bootstrap-save-reload-cycle-{cycle}-count-exact", null);

                    var cycleActions = new NpcActionApi(suiteAgentId, worldId, actorGo.transform, genuineHand, freshAutonomy.AllInteractables);
                    freshBootstrap.OnActionsCreated(cycleActions);

                    // Verify carried root, stored items, free items, and mass stability
                    float cycleCarriedMass = freshBootstrap.Model.GetActorCarriedMassKg(suiteAgentId);
                    check(Mathf.Abs(cycleCarriedMass - 2.6f) <= 0.0001f,
                          $"bootstrap-save-reload-cycle-{cycle}-carried-mass-stable", $"actual={cycleCarriedMass}");
                    float cycleChestMass = freshBootstrap.Model.GetItemTotalMassKg("chest-01");
                    check(freshBootstrap.Model.TryGetItem("chest-01", out _) && Mathf.Abs(cycleChestMass - 5.4f) <= 0.0001f,
                          $"bootstrap-save-reload-cycle-{cycle}-chest-mass-stable", $"actual={cycleChestMass}");

                    // Re-save for next iteration
                    var nextSnap = ItemPersistence.CreateSnapshot(freshBootstrap.Model, suiteAgentId);
                    check(ItemPersistence.SaveAtomic(cycleSavePath, nextSnap),
                          $"bootstrap-save-reload-cycle-{cycle}-resave-success", null);

                    // Destroy freshBrainGo: invokes OnDestroy which cleans up all 6 physical item instances
                    UnityEngine.Object.DestroyImmediate(freshBrainGo);
                    check(scene.rootCount == baselineSceneRoots,
                          $"bootstrap-save-reload-cycle-{cycle}-scene-roots-clean", $"actual={scene.rootCount}, baseline={baselineSceneRoots}");
                }

                check(scene.rootCount == baselineSceneRoots,
                      "bootstrap-save-reload-all-cycles-teardown-clean", null);

                // -------------------------------------------------------------
                // 7. Old Single-Demo Save Compatibility
                // -------------------------------------------------------------
                string singleFreeSavePath = Path.Combine(tempDir, "single-free-save.json");
                var singleFreePayload = new PhysicalSavePayload
                {
                    worldId = worldId,
                    generationId = genId,
                    actorId = suiteAgentId,
                    tick = 10,
                    items = new List<SavedItemRecord>
                    {
                        new SavedItemRecord
                        {
                            itemId = "canyon-artifact-01",
                            itemTypeId = "canyon-stone",
                            location = ItemLocationKind.Free,
                            holderActorId = null,
                            containerItemId = null,
                            massKg = 2.5f,
                            dimensions = new PhysicalDimensions(0.25f, 0.25f, 0.25f),
                            position = new Vector3(0.5f, 0.2f, 0.5f),
                            rotation = Quaternion.identity,
                            lastUpdatedTick = 10
                        }
                    },
                    receipts = new List<SavedReceiptRecord>()
                };
                check(ItemPersistence.SaveAtomic(singleFreeSavePath, singleFreePayload),
                      "bootstrap-save-single-demo-free-success", null);

                var singleBrainGo = CreateTrackedGo("single-demo-brain-go");
                var singleAutonomy = singleBrainGo.AddComponent<NpcAutonomy>();
                ConfigureAutonomy(singleAutonomy, singleBrainGo);
                singleAutonomy.InstanceWorldId = worldId;
                var singleBootstrap = singleBrainGo.AddComponent<PhysicalItemBootstrap>();
                singleBootstrap.Brain = singleAutonomy;
                singleAutonomy.PhysicalItems = singleBootstrap;

                bool singleLoaded = singleBootstrap.LoadSavePayload(singleFreeSavePath);
                check(singleLoaded, "bootstrap-old-single-demo-free-load-success", null);
                check(singleBootstrap.Bindings.Count == 1,
                      "bootstrap-old-single-demo-free-count-one", null);
                check(singleBootstrap.DemonstrationItem != null && string.Equals(singleBootstrap.DemonstrationItem.itemId, "canyon-artifact-01", StringComparison.Ordinal),
                      "bootstrap-old-single-demo-free-demonstration-item-wired", null);

                var singleActions = new NpcActionApi(suiteAgentId, worldId, actorGo.transform, genuineHand, singleAutonomy.AllInteractables);
                singleBootstrap.OnActionsCreated(singleActions);
                check(!singleBootstrap.DemonstrationItem.IsCarried && !singleBootstrap.DemonstrationItem.IsStored,
                      "bootstrap-old-single-demo-free-state-free", null);

                UnityEngine.Object.DestroyImmediate(singleBrainGo);

                // Also test single Carried demo item
                string singleCarriedSavePath = Path.Combine(tempDir, "single-carried-save.json");
                var singleCarriedPayload = new PhysicalSavePayload
                {
                    worldId = worldId,
                    generationId = genId,
                    actorId = suiteAgentId,
                    tick = 20,
                    items = new List<SavedItemRecord>
                    {
                        new SavedItemRecord
                        {
                            itemId = "canyon-artifact-01",
                            itemTypeId = "canyon-stone",
                            location = ItemLocationKind.Carried,
                            holderActorId = suiteAgentId,
                            containerItemId = null,
                            massKg = 2.5f,
                            dimensions = new PhysicalDimensions(0.25f, 0.25f, 0.25f),
                            position = genuineHand.position,
                            rotation = Quaternion.identity,
                            lastUpdatedTick = 20
                        }
                    },
                    receipts = new List<SavedReceiptRecord>()
                };
                check(ItemPersistence.SaveAtomic(singleCarriedSavePath, singleCarriedPayload),
                      "bootstrap-save-single-demo-carried-success", null);

                var singleCarriedBrainGo = CreateTrackedGo("single-carried-brain-go");
                var singleCarriedAutonomy = singleCarriedBrainGo.AddComponent<NpcAutonomy>();
                ConfigureAutonomy(singleCarriedAutonomy, singleCarriedBrainGo);
                singleCarriedAutonomy.InstanceWorldId = worldId;
                var singleCarriedBootstrap = singleCarriedBrainGo.AddComponent<PhysicalItemBootstrap>();
                singleCarriedBootstrap.Brain = singleCarriedAutonomy;
                singleCarriedAutonomy.PhysicalItems = singleCarriedBootstrap;

                check(singleCarriedBootstrap.LoadSavePayload(singleCarriedSavePath),
                      "bootstrap-old-single-demo-carried-load-success", null);
                var singleCarriedActions = new NpcActionApi(suiteAgentId, worldId, actorGo.transform, genuineHand, singleCarriedAutonomy.AllInteractables);
                singleCarriedBootstrap.OnActionsCreated(singleCarriedActions);
                check(singleCarriedBootstrap.DemonstrationItem.IsCarried && singleCarriedActions.Held == singleCarriedBootstrap.DemonstrationInteractable,
                      "bootstrap-old-single-demo-carried-held-match", null);

                UnityEngine.Object.DestroyImmediate(singleCarriedBrainGo);

                // -------------------------------------------------------------
                // 8. Refusal Guards with Baseline Preservation & Zero Staged Leaks
                // -------------------------------------------------------------
                var refusalBrainGo = CreateTrackedGo("refusal-brain-go");
                var refusalAutonomy = refusalBrainGo.AddComponent<NpcAutonomy>();
                ConfigureAutonomy(refusalAutonomy, refusalBrainGo);
                refusalAutonomy.InstanceWorldId = worldId;
                var refusalBootstrap = refusalBrainGo.AddComponent<PhysicalItemBootstrap>();
                refusalBootstrap.Brain = refusalAutonomy;
                refusalAutonomy.PhysicalItems = refusalBootstrap;
                RegisterBootstrapSyntheticDefinitions(refusalBootstrap);

                int initialSceneGoCount = scene.rootCount;
                var origBaselineDemo = refusalBootstrap.DemonstrationItem;
                check(origBaselineDemo != null, "bootstrap-refusal-baseline-demo-exists", null);
                int origModelItemCount = refusalBootstrap.Model.ItemCount;

                // Test 8A: Unknown item type refusal (do not fabricate!)
                string unknownTypeSavePath = Path.Combine(tempDir, "unknown-type.json");
                var unknownPayload = new PhysicalSavePayload
                {
                    worldId = worldId,
                    generationId = genId,
                    actorId = suiteAgentId,
                    tick = 10,
                    items = new List<SavedItemRecord>
                    {
                        new SavedItemRecord
                        {
                            itemId = "alien-item-01",
                            itemTypeId = "fabricated-alien-material",
                            location = ItemLocationKind.Free,
                            massKg = 5f,
                            dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                            position = new Vector3(1f, 0.2f, 1f),
                            rotation = Quaternion.identity,
                            lastUpdatedTick = 10
                        }
                    },
                    receipts = new List<SavedReceiptRecord>()
                };
                ItemPersistence.SaveAtomic(unknownTypeSavePath, unknownPayload);
                byte[] unknownBytesBefore = File.ReadAllBytes(unknownTypeSavePath);

                bool unknownRefused = !refusalBootstrap.LoadSavePayload(unknownTypeSavePath);
                check(unknownRefused && refusalBootstrap.SaveRejected,
                      "bootstrap-refusal-unknown-type-refused", null);
                check(refusalBootstrap.DemonstrationItem == origBaselineDemo && refusalBootstrap.Model.ItemCount == origModelItemCount,
                      "bootstrap-refusal-unknown-type-preserves-baseline-model-and-demo", null);
                check(scene.rootCount == initialSceneGoCount,
                      "bootstrap-refusal-unknown-type-zero-staged-leaks", null);
                check(System.Linq.Enumerable.SequenceEqual(unknownBytesBefore, File.ReadAllBytes(unknownTypeSavePath)),
                      "bootstrap-refusal-unknown-type-preserves-save-bytes", null);

                // Test 8B: Invalid graph cycle refusal (container A in B, B in A)
                string cycleGraphSavePath = Path.Combine(tempDir, "cycle-graph.json");
                var cyclePayload = new PhysicalSavePayload
                {
                    worldId = worldId,
                    generationId = genId,
                    actorId = suiteAgentId,
                    tick = 10,
                    items = new List<SavedItemRecord>
                    {
                        new SavedItemRecord
                        {
                            itemId = "cycle-chest",
                            itemTypeId = "multi-type-chest",
                            location = ItemLocationKind.Stored,
                            containerItemId = "cycle-basket",
                            massKg = 5f,
                            dimensions = new PhysicalDimensions(0.6f, 0.4f, 0.5f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity,
                            lastUpdatedTick = 10
                        },
                        new SavedItemRecord
                        {
                            itemId = "cycle-basket",
                            itemTypeId = "multi-type-basket",
                            location = ItemLocationKind.Stored,
                            containerItemId = "cycle-chest",
                            massKg = 2f,
                            dimensions = new PhysicalDimensions(0.3f, 0.25f, 0.3f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity,
                            lastUpdatedTick = 10
                        }
                    },
                    receipts = new List<SavedReceiptRecord>()
                };
                ItemPersistence.SaveAtomic(cycleGraphSavePath, cyclePayload);
                byte[] cycleBytesBefore = File.ReadAllBytes(cycleGraphSavePath);

                bool cycleRefused = !refusalBootstrap.LoadSavePayload(cycleGraphSavePath);
                check(cycleRefused && refusalBootstrap.SaveRejected,
                      "bootstrap-refusal-cycle-graph-refused", null);
                check(refusalBootstrap.DemonstrationItem == origBaselineDemo && refusalBootstrap.Model.ItemCount == origModelItemCount,
                      "bootstrap-refusal-cycle-preserves-baseline-model-and-demo", null);
                check(scene.rootCount == initialSceneGoCount,
                      "bootstrap-refusal-cycle-zero-staged-leaks", null);
                check(System.Linq.Enumerable.SequenceEqual(cycleBytesBefore, File.ReadAllBytes(cycleGraphSavePath)),
                      "bootstrap-refusal-cycle-preserves-save-bytes", null);

                // Test 8C: Duplicate item IDs in payload refusal
                string dupSavePath = Path.Combine(tempDir, "duplicate-ids.json");
                var dupPayload = new PhysicalSavePayload
                {
                    worldId = worldId,
                    generationId = genId,
                    actorId = suiteAgentId,
                    tick = 10,
                    items = new List<SavedItemRecord>
                    {
                        new SavedItemRecord
                        {
                            itemId = "dup-item",
                            itemTypeId = "multi-type-rock",
                            location = ItemLocationKind.Free,
                            massKg = 1f,
                            dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                            position = new Vector3(1f, 0.2f, 1f),
                            rotation = Quaternion.identity,
                            lastUpdatedTick = 10
                        },
                        new SavedItemRecord
                        {
                            itemId = "dup-item",
                            itemTypeId = "multi-type-rock",
                            location = ItemLocationKind.Free,
                            massKg = 1f,
                            dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                            position = new Vector3(2f, 0.2f, 2f),
                            rotation = Quaternion.identity,
                            lastUpdatedTick = 10
                        }
                    },
                    receipts = new List<SavedReceiptRecord>()
                };
                ItemPersistence.SaveAtomic(dupSavePath, dupPayload);
                byte[] dupBytesBefore = File.ReadAllBytes(dupSavePath);

                bool dupRefused = !refusalBootstrap.LoadSavePayload(dupSavePath);
                check(dupRefused && refusalBootstrap.SaveRejected,
                      "bootstrap-refusal-duplicate-ids-refused", null);
                check(refusalBootstrap.DemonstrationItem == origBaselineDemo && refusalBootstrap.Model.ItemCount == origModelItemCount,
                      "bootstrap-refusal-duplicate-preserves-baseline-model-and-demo", null);
                check(scene.rootCount == initialSceneGoCount,
                      "bootstrap-refusal-duplicate-zero-staged-leaks", null);
                check(System.Linq.Enumerable.SequenceEqual(dupBytesBefore, File.ReadAllBytes(dupSavePath)),
                      "bootstrap-refusal-duplicate-preserves-save-bytes", null);

                // Test 8D: Scope mismatch (wrong worldId, wrong genId, wrong actorId)
                string wrongScopePath = Path.Combine(tempDir, "wrong-scope.json");
                var wrongScopePayload = new PhysicalSavePayload
                {
                    worldId = "foreign-world-id",
                    generationId = genId,
                    actorId = suiteAgentId,
                    tick = 10,
                    items = new List<SavedItemRecord>
                    {
                        new SavedItemRecord
                        {
                            itemId = "scope-rock",
                            itemTypeId = "multi-type-rock",
                            location = ItemLocationKind.Free,
                            massKg = 1f,
                            dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                            position = new Vector3(1f, 0.2f, 1f),
                            rotation = Quaternion.identity,
                            lastUpdatedTick = 10
                        }
                    },
                    receipts = new List<SavedReceiptRecord>()
                };
                ItemPersistence.SaveAtomic(wrongScopePath, wrongScopePayload);
                byte[] scopeBytesBefore = File.ReadAllBytes(wrongScopePath);

                bool scopeRefused = !refusalBootstrap.LoadSavePayload(wrongScopePath);
                check(scopeRefused && refusalBootstrap.SaveRejected,
                      "bootstrap-refusal-wrong-scope-refused", null);
                check(refusalBootstrap.DemonstrationItem == origBaselineDemo && refusalBootstrap.Model.ItemCount == origModelItemCount,
                      "bootstrap-refusal-wrong-scope-preserves-baseline-model-and-demo", null);
                check(scene.rootCount == initialSceneGoCount,
                      "bootstrap-refusal-wrong-scope-zero-staged-leaks", null);
                check(System.Linq.Enumerable.SequenceEqual(scopeBytesBefore, File.ReadAllBytes(wrongScopePath)),
                      "bootstrap-refusal-wrong-scope-preserves-save-bytes", null);

                // Test 8E: Corrupt envelope / checksum mismatch
                string corruptPath = Path.Combine(tempDir, "corrupt-file.json");
                var corruptEnvelope = new PhysicalSaveEnvelope
                {
                    schema = ItemPersistence.SchemaVersion,
                    payload = "{\"worldId\":\"corrupt\"}",
                    sha256 = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef"
                };
                File.WriteAllText(corruptPath, JsonUtility.ToJson(corruptEnvelope, true));
                byte[] corruptBytesBefore = File.ReadAllBytes(corruptPath);

                bool corruptRefused = !refusalBootstrap.LoadSavePayload(corruptPath);
                check(corruptRefused && refusalBootstrap.SaveRejected,
                      "bootstrap-refusal-corrupt-envelope-refused", null);
                check(refusalBootstrap.DemonstrationItem == origBaselineDemo && refusalBootstrap.Model.ItemCount == origModelItemCount,
                      "bootstrap-refusal-corrupt-preserves-baseline-model-and-demo", null);
                check(scene.rootCount == initialSceneGoCount,
                      "bootstrap-refusal-corrupt-zero-staged-leaks", null);
                check(System.Linq.Enumerable.SequenceEqual(corruptBytesBefore, File.ReadAllBytes(corruptPath)),
                      "bootstrap-refusal-corrupt-preserves-save-bytes", null);

                // Test 8F: Missing hand refusal when save has carried item
                var prevActor = refusalAutonomy.Actor;
                refusalAutonomy.Actor = null;
                bool missingHandRefused = !refusalBootstrap.LoadSavePayload(singleCarriedSavePath);
                refusalAutonomy.Actor = prevActor;
                check(missingHandRefused && refusalBootstrap.SaveRejected,
                      "bootstrap-refusal-missing-hand-refused", null);
                check(refusalBootstrap.DemonstrationItem == origBaselineDemo && refusalBootstrap.Model.ItemCount == origModelItemCount,
                      "bootstrap-refusal-missing-hand-preserves-baseline", null);
                check(scene.rootCount == initialSceneGoCount,
                      "bootstrap-refusal-missing-hand-zero-staged-leaks", null);

                // Test 8G: Nonphysical registry collision refusal
                var collidingGo = CreateTrackedGo("colliding-cargo-go");
                var collidingNi = collidingGo.AddComponent<NpcInteractable>();
                collidingNi.StableId = "chest-01"; // Collides with chest-01 in mixedSavePath
                collidingNi.WorldId = worldId;
                refusalAutonomy.Registry = new[] { collidingNi };

                bool collisionRefused = !refusalBootstrap.LoadSavePayload(mixedSavePath);
                refusalAutonomy.Registry = Array.Empty<NpcInteractable>();
                UnityEngine.Object.DestroyImmediate(collidingGo);

                check(collisionRefused && refusalBootstrap.SaveRejected,
                      "bootstrap-refusal-nonphysical-collision-refused", null);
                check(refusalBootstrap.DemonstrationItem == origBaselineDemo && refusalBootstrap.Model.ItemCount == origModelItemCount,
                      "bootstrap-refusal-nonphysical-collision-preserves-baseline", null);
                check(scene.rootCount == initialSceneGoCount,
                      "bootstrap-refusal-nonphysical-collision-zero-staged-leaks", null);

                // Test 8H: Unsupported live reload refusal when Brain.Actions != null
                refusalAutonomy.ResetState();
                check(refusalAutonomy.Actions != null, "bootstrap-refusal-live-reload-actions-initialized", null);

                bool liveReloadRefused = !refusalBootstrap.LoadSavePayload(mixedSavePath);
                check(liveReloadRefused && refusalBootstrap.SaveRejected,
                      "bootstrap-refusal-live-reload-refused", null);
                check(scene.rootCount == initialSceneGoCount,
                      "bootstrap-refusal-live-reload-zero-staged-leaks", null);

                // Teardown refusal brain
                UnityEngine.Object.DestroyImmediate(refusalBrainGo);

                // -------------------------------------------------------------
                // 9. Receipt Replay and Request Sequence Above Restored Receipts
                // -------------------------------------------------------------
                var replayBrainGo = CreateTrackedGo("replay-brain-go");
                var replayAutonomy = replayBrainGo.AddComponent<NpcAutonomy>();
                ConfigureAutonomy(replayAutonomy, replayBrainGo);
                replayAutonomy.InstanceWorldId = worldId;
                var replayBootstrap = replayBrainGo.AddComponent<PhysicalItemBootstrap>();
                replayBootstrap.Brain = replayAutonomy;
                replayAutonomy.PhysicalItems = replayBootstrap;
                RegisterBootstrapSyntheticDefinitions(replayBootstrap);

                check(replayBootstrap.LoadSavePayload(mixedSavePath),
                      "bootstrap-replay-load-mixed-success", null);
                var replayActions = new NpcActionApi(suiteAgentId, worldId, actorGo.transform, genuineHand, replayAutonomy.AllInteractables);
                replayBootstrap.OnActionsCreated(replayActions);

                // Existing receipt duplicate replay
                var replayReq = new ItemActionRequest
                {
                    requestId = pickupBasketReq.requestId,
                    action = ItemActionKind.Pickup,
                    actorId = suiteAgentId,
                    itemId = "basket-01"
                };
                var dupReceipt = replayBootstrap.Model.Execute(worldId, genId, replayReq, auth);
                check(dupReceipt.success && dupReceipt.duplicate,
                      "bootstrap-receipt-duplicate-replay-success", null);

                // Conflict request with same requestId (Drop instead of Pickup)
                var conflictReq = new ItemActionRequest
                {
                    requestId = pickupBasketReq.requestId,
                    action = ItemActionKind.Drop,
                    actorId = suiteAgentId,
                    itemId = "basket-01",
                    position = new Vector3(5f, 0.2f, 5f),
                    rotation = Quaternion.identity
                };
                var conflictReceipt = replayBootstrap.Model.Execute(worldId, genId, conflictReq, auth);
                check(!conflictReceipt.success && !conflictReceipt.duplicate && conflictReceipt.code == "request-id-conflict",
                      "bootstrap-receipt-conflict-refusal", null);

                // Monotonic sequence allocation strictly above restored receipts
                check(replayBootstrap.Model.TryAllocateNextRequestId(out int allocatedId) && allocatedId >= expectedAuthorHighestReceiptId + 1,
                      "bootstrap-monotonic-sequence-allocation", $"allocatedId={allocatedId}");

                // Subsequent action execution: drop basket-01
                var subsequentReq = new ItemActionRequest
                {
                    requestId = allocatedId,
                    action = ItemActionKind.Drop,
                    actorId = suiteAgentId,
                    itemId = "basket-01",
                    position = new Vector3(4f, 0.2f, 4f),
                    rotation = Quaternion.identity
                };
                var subsequentReceipt = replayBootstrap.Model.Execute(worldId, genId, subsequentReq, auth);
                check(subsequentReceipt.success && !subsequentReceipt.duplicate,
                      "bootstrap-subsequent-action-success", null);

                // Subsequent action execution: pick basket-01 back up so it remains carried into Section 10
                check(replayBootstrap.Model.TryAllocateNextRequestId(out int repickAllocatedId) && repickAllocatedId > allocatedId,
                      "bootstrap-monotonic-sequence-allocation-after-drop", null);
                var repickReq = new ItemActionRequest
                {
                    requestId = repickAllocatedId,
                    action = ItemActionKind.Pickup,
                    actorId = suiteAgentId,
                    itemId = "basket-01"
                };
                var repickReceipt = replayBootstrap.Model.Execute(worldId, genId, repickReq, auth);
                check(repickReceipt.success && !repickReceipt.duplicate,
                      "bootstrap-subsequent-repickup-basket-success", null);

                // -------------------------------------------------------------
                // 10. Repeated Autonomy Reset & Init: No Duplicates or Accumulation
                // -------------------------------------------------------------
                int interactablesCountBeforeReset = new List<NpcInteractable>(replayAutonomy.AllInteractables).Count;
                int physItemsCountBeforeReset = replayBrainGo.GetComponentsInChildren<PhysicalItem>(true).Length;

                for (int r = 0; r < 5; r++)
                {
                    replayAutonomy.ResetState();

                    var rBindingMap = new Dictionary<string, PhysicalItemRuntimeBinding>(StringComparer.Ordinal);
                    foreach (var b in replayBootstrap.Bindings) rBindingMap[b.itemId] = b;

                    var rBasketB = rBindingMap["basket-01"];
                    var rPouchB = rBindingMap["pouch-01"];
                    var rGemB = rBindingMap["gem-01"];
                    var rChestB = rBindingMap["chest-01"];
                    var rChiselB = rBindingMap["chisel-01"];
                    var rRockB = rBindingMap["rock-01"];

                    // 1. Actions.Held matches basket interactable
                    check(replayAutonomy.Actions != null && replayAutonomy.Actions.Held == rBasketB.interactable,
                          $"bootstrap-repeated-reset-{r}-actions-held-match", null);

                    // 2. HeldBy == suiteAgentId
                    check(string.Equals(rBasketB.interactable.HeldBy, suiteAgentId, StringComparison.Ordinal),
                          $"bootstrap-repeated-reset-{r}-basket-held-by-agent", null);

                    // 3. IsCarried == true and CarriedHand == genuineHand
                    check(rBasketB.physicalItem.IsCarried,
                          $"bootstrap-repeated-reset-{r}-basket-is-carried", null);
                    check(rBasketB.physicalItem.CarriedHand == genuineHand,
                          $"bootstrap-repeated-reset-{r}-basket-carried-hand-match", null);

                    // 4. Stored parentage intact: pouch in basket, gem in pouch, chisel in chest
                    check(rPouchB.physicalItem.IsStored && string.Equals(rPouchB.physicalItem.BoundContainerItemId, "basket-01", StringComparison.Ordinal),
                          $"bootstrap-repeated-reset-{r}-pouch-stored-in-basket", null);
                    check(rPouchB.physicalItem.transform.parent == rBasketB.physicalItem.transform,
                          $"bootstrap-repeated-reset-{r}-pouch-parent-basket", null);

                    check(rGemB.physicalItem.IsStored && string.Equals(rGemB.physicalItem.BoundContainerItemId, "pouch-01", StringComparison.Ordinal),
                          $"bootstrap-repeated-reset-{r}-gem-stored-in-pouch", null);
                    check(rGemB.physicalItem.transform.parent == rPouchB.physicalItem.transform,
                          $"bootstrap-repeated-reset-{r}-gem-parent-pouch", null);

                    check(rChiselB.physicalItem.IsStored && string.Equals(rChiselB.physicalItem.BoundContainerItemId, "chest-01", StringComparison.Ordinal),
                          $"bootstrap-repeated-reset-{r}-chisel-stored-in-chest", null);
                    check(rChiselB.physicalItem.transform.parent == rChestB.physicalItem.transform,
                          $"bootstrap-repeated-reset-{r}-chisel-parent-chest", null);

                    // 5. Free items intact: chest and rock
                    check(!rChestB.physicalItem.IsCarried && !rChestB.physicalItem.IsStored,
                          $"bootstrap-repeated-reset-{r}-chest-is-free", null);
                    check(!rRockB.physicalItem.IsCarried && !rRockB.physicalItem.IsStored,
                          $"bootstrap-repeated-reset-{r}-rock-is-free", null);

                    // 6. RequestId preserved above receipts
                    check(replayAutonomy.RequestId >= repickAllocatedId,
                          $"bootstrap-repeated-reset-{r}-request-id-preserved", $"actual={replayAutonomy.RequestId}");
                }

                int interactablesCountAfterReset = new List<NpcInteractable>(replayAutonomy.AllInteractables).Count;
                int physItemsCountAfterReset = replayBrainGo.GetComponentsInChildren<PhysicalItem>(true).Length;

                check(interactablesCountAfterReset == interactablesCountBeforeReset,
                      "bootstrap-repeated-reset-interactables-count-constant", $"before={interactablesCountBeforeReset}, after={interactablesCountAfterReset}");
                check(physItemsCountAfterReset == physItemsCountBeforeReset,
                      "bootstrap-repeated-reset-phys-items-count-constant", $"before={physItemsCountBeforeReset}, after={physItemsCountAfterReset}");

                // -------------------------------------------------------------
                // 10B. Reset Refusal Snapshot Verification
                // -------------------------------------------------------------
                // Advance nonphysical state to verify refusal preserves dirty/active state without reset side effects
                if (replayAutonomy.Log != null)
                {
                    replayAutonomy.Log.Record(replayAutonomy.Tick, replayAutonomy.Phase, "test-perceive", "test-goal", "test-action", "test-result");
                }
                if (replayAutonomy.Actor != null)
                {
                    replayAutonomy.Actor.Place(replayAutonomy.SpawnPosition + new Vector3(3f, 0f, 3f));
                    replayAutonomy.Actor.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
                }

                var preRefusalSnapshot = CaptureResetSnapshot(replayAutonomy, replayBootstrap);

                // Refusal 1: Missing hand / actor refusal
                var origActor = replayAutonomy.Actor;
                replayAutonomy.Actor = null;
                bool missingHandResetResult = replayAutonomy.ResetState();
                check(!missingHandResetResult, "bootstrap-reset-refusal-missing-hand-refused", null);
                replayAutonomy.Actor = origActor;
                AssertResetSnapshotEqual(preRefusalSnapshot, replayAutonomy, replayBootstrap, "bootstrap-reset-refusal-missing-hand");

                // Refusal 2: Nonphysical registry collision refusal
                var collGo = CreateTrackedGo("reset-colliding-cargo");
                var collNi = collGo.AddComponent<NpcInteractable>();
                collNi.StableId = "basket-01"; // Collides with physical item basket-01
                collNi.WorldId = worldId;
                replayAutonomy.Registry = new[] { collNi };

                bool collisionResetResult = replayAutonomy.ResetState();
                check(!collisionResetResult, "bootstrap-reset-refusal-collision-refused", null);
                replayAutonomy.Registry = Array.Empty<NpcInteractable>();
                UnityEngine.Object.DestroyImmediate(collGo);
                AssertResetSnapshotEqual(preRefusalSnapshot, replayAutonomy, replayBootstrap, "bootstrap-reset-refusal-collision");

                // Refusal 3: World mismatch refusal
                string origWorldId = replayAutonomy.InstanceWorldId;
                replayAutonomy.InstanceWorldId = "foreign.world.v1";

                bool mismatchResetResult = replayAutonomy.ResetState();
                check(!mismatchResetResult, "bootstrap-reset-refusal-world-mismatch-refused", null);
                replayAutonomy.InstanceWorldId = origWorldId;
                AssertResetSnapshotEqual(preRefusalSnapshot, replayAutonomy, replayBootstrap, "bootstrap-reset-refusal-world-mismatch");

                // Clean successful reset: verify nonphysical reset executes and restores baseline cleanly
                bool cleanSuccess = replayAutonomy.ResetState();
                check(cleanSuccess, "bootstrap-reset-clean-success", null);
                check(replayAutonomy.Actions != null, "bootstrap-reset-success-actions-not-null", null);
                check(replayAutonomy.Actions != preRefusalSnapshot.actionsRef, "bootstrap-reset-success-actions-recreated", null);
                check(replayAutonomy.Actions.Held == preRefusalSnapshot.heldRef, "bootstrap-reset-success-held-rebound", null);
                check(replayAutonomy.Tick == 0, "bootstrap-reset-success-tick-zero", null);
                check(string.Equals(replayAutonomy.Phase, "Observe", StringComparison.Ordinal), "bootstrap-reset-success-phase-observe", null);
                check(replayAutonomy.Log != null && replayAutonomy.Log.Total == 0, "bootstrap-reset-success-log-cleared", null);
                check(replayAutonomy.Actor != null && Vector3.Distance(replayAutonomy.Actor.transform.position, replayAutonomy.SpawnPosition) <= 0.0001f,
                      "bootstrap-reset-success-actor-spawn-position", null);
                check(replayAutonomy.RequestId >= repickAllocatedId, "bootstrap-reset-success-request-id-preserved", null);

                // -------------------------------------------------------------
                // 11. FixedUpdate Synchronization of All Eligible Free Items
                // -------------------------------------------------------------
                var syncBindingMap = new Dictionary<string, PhysicalItemRuntimeBinding>(StringComparer.Ordinal);
                foreach (var b in replayBootstrap.Bindings) syncBindingMap[b.itemId] = b;

                var chestItem = syncBindingMap["chest-01"].physicalItem;
                var rockItem = syncBindingMap["rock-01"].physicalItem;

                // Move both free physical items in physics space
                chestItem.transform.position = new Vector3(12f, 0.5f, 12f);
                rockItem.transform.position = new Vector3(14f, 0.5f, 14f);

                replayBootstrap.SyncFreeItems();

                check(replayBootstrap.Model.TryGetItem("chest-01", out var syncedChest) &&
                      Vector3.Distance(syncedChest.position, new Vector3(12f, 0.5f, 12f)) <= 0.0001f,
                      "bootstrap-fixedupdate-syncs-chest-free-item", null);
                check(replayBootstrap.Model.TryGetItem("rock-01", out var syncedRock) &&
                      Vector3.Distance(syncedRock.position, new Vector3(14f, 0.5f, 14f)) <= 0.0001f,
                      "bootstrap-fixedupdate-syncs-rock-free-item", null);

                // Verify stored items did NOT have their positions desynchronized to free space
                check(replayBootstrap.Model.TryGetItem("chisel-01", out var storedChisel) &&
                      storedChisel.location == ItemLocationKind.Stored,
                      "bootstrap-fixedupdate-stored-item-location-preserved", null);

                UnityEngine.Object.DestroyImmediate(replayBrainGo);
            }
            finally
            {
                foreach (var go in ownedObjects)
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                }
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch { }
            }
        }
    }
}
